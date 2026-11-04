using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §5 and §7 D1–D5, D7 (slice S4), on the compiled IL of
/// every built <c>src</c> assembly and on source text (the Desktop project builds on Windows only, so its obligations are also
/// pinned on source): <c>ProbeAsync</c> has one caller, the entry, which only <c>App.OnLaunched</c> calls, first; the update
/// launcher never emits <c>--kind</c>; the probe path names no database, reset completion, scheduled task, lease, window or
/// view model; the test seams have no <c>src</c> caller; and the admission pins are not touched.
/// </summary>
[TestClass]
public sealed class QualificationProbeDesktopBoundaryTests
{
    private const string Execution = "PCSteward.Execution.";
    private const string Transport = Execution + "ReviewedHelperProbeTransport";
    private const string Entry = Execution + "QualificationProbeEntry";
    private const string Runner = Execution + "QualificationProbeRunner";
    private const string Dependencies = Execution + "ReviewedHelperProbeDependencies";
    private const string FileSystem = Execution + "WindowsQualificationResultFileSystem";

    /// <summary>Every Execution type of the S4 desktop probe path (nested types included by prefix).</summary>
    private static readonly string[] DesktopProbeTypes =
    [
        Transport, Execution + "ReviewedHelperProbeSession", Execution + "ReviewedHelperProbeSessionResult", Execution + "ReviewedHelperProbeHold",
        Execution + "ProbeHold", Dependencies, Execution + "ReviewedHelperProbeIdentity", Execution + "ReviewedHelperProbeLauncher",
        Execution + "IReviewedHelperProbeChannel", Execution + "QualificationProbeRun", Execution + "QualificationProbeTransportFacts",
        Execution + "QualificationProbePins", Execution + "QualificationProbeDesktopOutcome", Entry, Runner, Execution + "QualificationProbeLaunch",
        Execution + "IQualificationProbeGateSession", Execution + "QualificationProbeResultDocument", Execution + "QualificationResultFile",
        Execution + "QualificationResultNode", Execution + "IQualificationResultFolder", Execution + "IQualificationResultFileSystem", FileSystem,
    ];

    private static bool IsDesktopProbeType(string? type) => type is not null &&
        DesktopProbeTypes.Any(probe => type == probe || type.StartsWith(probe + "+", StringComparison.Ordinal) || type.StartsWith(probe + "`", StringComparison.Ordinal));

    [TestMethod]
    public void D1ProbeAsyncHasExactlyOneSrcCallerTheEntryWhichOnlyOnLaunchedCalls()
    {
        List<string> assemblies = RequireBuilt();
        List<string> callers = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Transport && reference.TargetMember == "ProbeAsync")
                    callers.Add($"{Path.GetFileNameWithoutExtension(path)}: {reference.LogicalCaller}");
        Assert.AreEqual(("PCSteward.Execution: " + Entry + ".Run"), string.Join(", ", callers.Distinct().ToArray()), string.Join(Environment.NewLine, callers));

        // The entry's Run is called only by the desktop's OnLaunched (IL where the Desktop is built; source everywhere).
        List<string> runCallers = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Entry && reference.TargetMember == "Run")
                    runCallers.Add(reference.LogicalCaller);
        Assert.IsTrue(runCallers.All(caller => caller == "PCSteward.Desktop.App.OnLaunched"), string.Join(Environment.NewLine, runCallers));
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string[] naming = [.. Sources(src).Where(path => Regex.IsMatch(Code(path), @"\bQualificationProbeEntry\.Run\b"))
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/'))];
        Assert.AreEqual(("PCSteward.Desktop/App.xaml.cs"), string.Join(", ", naming));
        Assert.AreEqual(1, Regex.Count(Read(src, "PCSteward.Desktop", "App.xaml.cs"), @"QualificationProbeEntry\.Run\("));
    }

    [TestMethod]
    public void D1TheTestSeamsHaveNoSrcCallerOutsideTheProductionComposition()
    {
        List<string> assemblies = RequireBuilt();
        // Each permitted edge: target type, member, target signature, logical caller.
        (string Type, string Member, string Signature, string Caller)[] permitted =
        [
            (Transport, ".ctor", "()", Entry + ".Run"),
            (Transport, ".ctor", "(" + Dependencies + ")", Transport + "..ctor"),
            (Transport, "ProductionDependencies", "()", Transport + "..ctor"),
            (Runner, ".ctor", null!, Entry + ".Run"),
            (FileSystem, "get_Instance", "()", Entry + ".Run"),
        ];
        string[] watched = [Transport + "..ctor", Transport + ".ProductionDependencies", Runner + "..ctor", FileSystem + ".get_Instance", Dependencies + "..ctor"];
        List<string> violations = [];
        HashSet<int> seen = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                string target = reference.TargetType + "." + reference.TargetMember;
                if (!watched.Contains(target)) continue;
                // The dependencies record is constructed only by the production factory (and its own compiler-generated clone).
                if (reference.TargetType == Dependencies)
                {
                    if (reference.LogicalCaller is not (Transport + ".ProductionDependencies") and not (Dependencies + ".Clone") and not (Dependencies + ".<Clone>$"))
                        violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} -> {target}");
                    continue;
                }
                int index = Array.FindIndex(permitted, edge => edge.Type == reference.TargetType && edge.Member == reference.TargetMember &&
                    (edge.Signature is null || edge.Signature == reference.TargetSignature) && edge.Caller == reference.LogicalCaller);
                if (index >= 0) seen.Add(index);
                else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller}{reference.CallerSignature} -> {target}{reference.TargetSignature}");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        Assert.AreEqual(permitted.Length, seen.Count, "every pinned edge exists (the scan is not vacuous)");
    }

    [TestMethod]
    public void D2NothingOutsideTheEntryReachesTheProbeAndExecutionReferencesNoStateUiOrScheduleProject()
    {
        // ScheduledHost, Application, scan workflows, view models and windows cannot reference the probe: the S3 boundary test
        // pins the type references; here the only src file naming the entry is App.xaml.cs, and Execution (where every
        // probe type lives) cannot see Storage, Persistence, Scheduling, Inventory or the Desktop at all.
        string root = CompiledIlScanner.FindRepositoryRoot();
        string src = Path.Combine(root, "src");
        string[] naming = [.. Sources(src).Where(path => Regex.IsMatch(Code(path), @"\bQualificationProbeEntry\b"))
            .Select(path => Path.GetRelativePath(src, path).Replace('\\', '/')).Order(StringComparer.Ordinal)];
        Assert.AreEqual("PCSteward.Desktop/App.xaml.cs, PCSteward.Execution/QualificationProbeEntry.cs", string.Join(", ", naming));
        foreach (string file in new[] { "MainWindow.cs", "ReviewedRouteSurfaces.cs", "StewardPresentation.cs" })
            Assert.IsFalse(Read(src, "PCSteward.Desktop", file).Contains("QualificationProbe", StringComparison.Ordinal), $"{file} names the probe");

        string[] references = [.. XDocument.Load(Path.Combine(src, "PCSteward.Execution", "PCSteward.Execution.csproj")).Descendants("ProjectReference")
            .Select(element => (string?)element.Attribute("Include") ?? string.Empty)];
        foreach (string forbidden in new[] { "Storage", "Persistence", "Scheduling", "Inventory", "Desktop", "ScheduledHost", "Diagnostics" })
            Assert.IsFalse(references.Any(reference => reference.Contains(forbidden, StringComparison.Ordinal)), $"Execution references {forbidden}");

        // Not reachable from the update path either: no update transport, dispatcher, workflow or endpoint type calls a probe type.
        List<string> violations = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (IsDesktopProbeType(reference.TargetType) && !IsDesktopProbeType(reference.CallerType ?? reference.Caller) &&
                    !IsDesktopProbeType(reference.Caller) && reference.Caller != "PCSteward.Desktop.App" && reference.Caller != "PCSteward.Desktop.QualificationProbeLaunchGate")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} -> {reference.TargetType}.{reference.TargetMember}");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void D3TheUpdateLauncherNeverEmitsTheKindAndTheProbeLauncherIsTheOnlyEmitter()
    {
        string execution = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        // The update transport's launcher argument list: exactly the eight protocol-2 elements, no kind (byte-for-byte unchanged).
        string update = File.ReadAllText(Path.Combine(execution, "ReviewedHelperProtocol2Transport.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Match launcher = Regex.Match(update, @"private static Process\? LaunchHelper\(string helperPath, string pipeName, string nonce, out bool elevationDeclined\)\s*\{(?<body>.*?)\n    \}",
            RegexOptions.Singleline);
        Assert.IsTrue(launcher.Success, "the update launcher was not found");
        string body = launcher.Groups["body"].Value;
        StringAssert.Contains(body, "\"--protocol\", ReviewedUpdateWireCodec.ProtocolVersion,\n" +
            "            \"--desktop-pid\", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),\n" +
            "            \"--pipe\", pipeName,\n" +
            "            \"--nonce\", nonce,\n        })");
        foreach (string forbidden in new[] { "--kind", "KindKey", "QualificationProbe", "qualify-chocolatey-helper", "ProbeArguments" })
            Assert.IsFalse(update.Contains(forbidden, StringComparison.Ordinal), $"the update transport names {forbidden}");

        // The only src file that adds process arguments next to the kind is the probe transport, through ProbeArguments.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string[] kindLiteral = [.. Sources(src).Where(path => Code(path).Contains("\"--kind\"", StringComparison.Ordinal)).Select(Path.GetFileName)!];
        Assert.AreEqual(("ReviewedHelperProbeBootstrap.cs"), string.Join(", ", kindLiteral), "the kind key literal is declared once (the helper parser's constant)");
        string[] kindUse = [.. Sources(src).Where(path => Regex.IsMatch(Code(path), @"\bReviewedHelperProbeBootstrap\.KindKey\b"))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        Assert.AreEqual(("ReviewedHelperProbeTransport.cs"), string.Join(", ", kindUse), "only the probe transport emits the kind key");
        string[] emitters = [.. Sources(src).Where(path => Code(path) is var text && text.Contains("ArgumentList.Add", StringComparison.Ordinal) &&
            (text.Contains("KindKey", StringComparison.Ordinal) || text.Contains("QualificationProbeWireCodec.Kind", StringComparison.Ordinal))).Select(Path.GetFileName)!];
        Assert.AreEqual(("ReviewedHelperProbeTransport.cs"), string.Join(", ", emitters));
        string probe = File.ReadAllText(Path.Combine(execution, "ReviewedHelperProbeTransport.cs"));
        StringAssert.Contains(probe, "        ReviewedHelperProbeBootstrap.KindKey, QualificationProbeWireCodec.Kind,");
        Assert.AreEqual(1, Regex.Count(probe, @"ProbeArguments\(d\.CurrentProcessId, pipeName, nonce\)"), "one launch with the probe line");

        // ProbeArguments has exactly one src caller: ProbeAsync.
        List<string> callers = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Transport && reference.TargetMember == "ProbeArguments") callers.Add(reference.LogicalCaller);
        Assert.AreEqual((Transport + ".ProbeAsync"), string.Join(", ", callers.Distinct().ToArray()), string.Join(Environment.NewLine, callers));
    }

    [TestMethod]
    public void D4TheProbeTouchesNoAdmissionPin()
    {
        // HasSupportedMode, CapabilityOperationAllowlist and the dispatcher's Supports keep their existing (unmodified) tests;
        // the probe only reads the compiled allow-list (to hold every token) and never calls an admission member.
        List<string> violations = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (!IsDesktopProbeType(reference.CallerType ?? reference.Caller) && !IsDesktopProbeType(reference.Caller)) continue;
                if (reference.TargetMember is "HasSupportedMode" or "Supports" or "StillSupported" ||
                    reference.TargetType.EndsWith("RouteDispatcher", StringComparison.Ordinal) ||
                    reference.TargetType == "PCSteward.Execution.WindowsReviewedHelperTransport" && reference.TargetMember != "CreatePipeName")
                    violations.Add($"{reference.LogicalCaller} -> {reference.TargetType}.{reference.TargetMember}");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        string probe = Read(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src"), "PCSteward.Execution", "ReviewedHelperProbeTransport.cs");
        Assert.AreEqual(1, Regex.Count(probe, @"DeploymentCapabilityPolicy\.CapabilityOperationAllowlist"), "read once, to hold every token");
        Assert.IsFalse(Regex.IsMatch(probe, @"CapabilityOperationAllowlist\s*(=|\.Add|\.Remove|\.Clear)"), "the allow-list is never changed");
    }

    [TestMethod]
    public void D5TheProbeBranchNamesNoDatabaseResetCompletionScheduleLeaseWindowOrViewModel()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string gate = Code(Path.Combine(src, "PCSteward.Desktop", "QualificationProbeLaunchGate.cs"));
        string app = Code(Path.Combine(src, "PCSteward.Desktop", "App.xaml.cs"));
        int start = app.IndexOf("protected override void OnLaunched(", StringComparison.Ordinal);
        int normal = app.IndexOf("CurrentUserDataOperationSession? session;", StringComparison.Ordinal);
        Assert.IsTrue(start > 0 && normal > start);
        string branch = app[start..normal];
        string[] forbidden = ["SqliteDatabase", "WindowsCurrentUserScheduledTaskRegistrar", "DataOperationLease", "Window", "ViewModel", "Database",
            "MigrateAsync", "Reconcile", "ScheduledCheck", "InventoryWorkflow", "ActivateWindow", "CompletePendingAsync", "Arm(", "AcknowledgeOnboarding",
            "VerifyOnboardingBootstrap", "GetStateRoot"];
        foreach (string text in forbidden)
        {
            Assert.IsFalse(gate.Contains(text, StringComparison.Ordinal), $"the probe gate names {text}");
            Assert.IsFalse(branch.Contains(text, StringComparison.Ordinal), $"the probe branch names {text}");
        }
        // The only FactoryReset member is the read-only marker read; the only gate members are TryAcquire, Context and Dispose.
        string[] resetMembers = [.. Regex.Matches(gate, @"\bFactoryReset\.(\w+)").Select(match => match.Groups[1].Value).Distinct()];
        Assert.AreEqual(("GetMarkerState"), string.Join(", ", resetMembers));
        StringAssert.Contains(gate, "FactoryReset.GetMarkerState() == FactoryResetMarkerState.None");
        StringAssert.Contains(gate, "CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, CancellationToken.None)");
        StringAssert.Contains(gate, "session.Context.RecoveryRequired");
        Assert.AreEqual(1, Regex.Count(gate, @"\bnew HeldGate\("));
        Assert.IsFalse(Regex.IsMatch(gate, @"\bstatic\s+(?!class\b)[\w<>?,\s]+\s+\w+\s*[=;]"), "no static state in the gate");

        // On the compiled Desktop (Windows builds): the gate type references FactoryReset only through GetMarkerState.
        string? desktop = CompiledIlScanner.BuiltSrcAssemblies().SingleOrDefault(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Desktop");
        if (desktop is not null)
        {
            string[] il = [.. CompiledIlScanner.References(desktop).Where(reference => reference.Caller == "PCSteward.Desktop.QualificationProbeLaunchGate")
                .Select(reference => reference.TargetType + "." + reference.TargetMember).Distinct()];
            foreach (string target in il)
            {
                Assert.IsFalse(target.StartsWith("PCSteward.Storage.FactoryReset.", StringComparison.Ordinal) && target != "PCSteward.Storage.FactoryReset.GetMarkerState",
                    target);
                Assert.IsFalse(target.StartsWith("PCSteward.Persistence", StringComparison.Ordinal) || target.StartsWith("Microsoft.UI", StringComparison.Ordinal) ||
                    target.Contains("DataOperationLease", StringComparison.Ordinal) || target.Contains("ScheduledTask", StringComparison.Ordinal), target);
            }
        }
    }

    [TestMethod]
    public void D7ClassifyIsFirstTheFolderPrecedesTheGateAndTheFileIsWrittenBeforeTheGateIsReleased()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string app = Read(src, "PCSteward.Desktop", "App.xaml.cs");
        Match onLaunched = Regex.Match(app, @"protected override void OnLaunched\(LaunchActivatedEventArgs args\)\s*\{\s*(//[^\n]*\n\s*)*(?<first>[^\n]*)");
        Assert.IsTrue(onLaunched.Success);
        Assert.AreEqual("QualificationProbeLaunch launch = QualificationProbeEntry.Classify(Environment.GetCommandLineArgs());",
            onLaunched.Groups["first"].Value.Trim(), "Classify is the first statement of OnLaunched");
        int classify = app.IndexOf("QualificationProbeEntry.Classify(", StringComparison.Ordinal);
        int acquire = app.IndexOf("CurrentUserDataOperationSession.TryAcquire(", StringComparison.Ordinal);
        Assert.IsTrue(classify > 0 && acquire > classify, "Classify precedes the normal path's gate");
        Assert.AreEqual(1, Regex.Count(app, @"CurrentUserDataOperationSession\.TryAcquire\("), "the normal path has one TryAcquire");
        int exit = app.IndexOf("Environment.Exit(", StringComparison.Ordinal);
        Assert.IsTrue(exit > classify && exit < acquire, "a probe or malformed start exits before the gate");
        // The probe path passes the real launch gate: the only Run call, in this exact form, between Classify and the gate.
        const string RunWithRealGate = "QualificationProbeEntry.Run(QualificationProbeLaunchGate.TryAcquire)";
        Assert.AreEqual(1, Regex.Count(app, @"QualificationProbeEntry\.Run\("), "one Run call");
        Assert.AreEqual(1, Regex.Count(app, Regex.Escape(RunWithRealGate)), "Run is passed QualificationProbeLaunchGate.TryAcquire");
        int run = app.IndexOf(RunWithRealGate, StringComparison.Ordinal);
        Assert.IsTrue(run > exit && run < acquire, "the gated Run is the probe exit code, before the normal path's gate");

        string runner = Read(src, "PCSteward.Execution", "QualificationProbeEntry.cs");
        int folder = runner.IndexOf("QualificationResultFile.TryCreateFolder(", StringComparison.Ordinal);
        int gate = runner.IndexOf("session = acquireGate();", StringComparison.Ordinal);
        int probe = runner.IndexOf("RunProbe();", StringComparison.Ordinal);
        int write = runner.IndexOf("Write(folder, run, started)", StringComparison.Ordinal);
        int dispose = runner.IndexOf("session.Dispose();", StringComparison.Ordinal);
        Assert.IsTrue(folder > 0 && gate > folder && probe > gate && write > probe && dispose > write, "folder, gate, probe, write, release");
        Assert.AreEqual(1, Regex.Count(runner, @"acquireGate\(\)"), "the gate is acquired once, on the calling thread");
        StringAssert.Contains(runner, "Task.Run(() => probe(CancellationToken.None)).GetAwaiter().GetResult()");
    }

    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Execution", "PCSteward.Storage", "PCSteward.ScheduledHost", "PCSteward.Application" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");
        return assemblies;
    }

    private static string Read(string src, string project, string file) =>
        File.ReadAllText(Path.Combine(src, project, file)).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Source text without comment lines (documentation may name what the code must not reference).</summary>
    private static string Code(string path) => string.Join("\n", File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
        .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static IEnumerable<string> Sources(string root) => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));
}
