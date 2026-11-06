using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §7 and §8 N5, adapter gates §A.6.1 (A2-L6), fact extension
/// rev 3 §4.3 (L5): the desktop producers' boundaries, on compiled IL of every built <c>src</c> assembly and on source.
/// <list type="bullet">
/// <item>they are the only <c>src</c> implementations of the evidence-source and verifier ports; only the N6 composition
/// (<c>ChocolateyReviewedRouteComposition</c>) constructs them and their ONE shared baseline store; since the Chocolatey Confirm
/// build (design §3, §5 item 2) only its public façade <c>ChocolateyClientUpdateProducers</c> constructs that composition, only
/// <c>ChocolateyClientUpdateComposition.CreateSession</c> constructs the façade, the dispatch workflow and the route dispatcher,
/// and the composed dispatcher admits only the client self-update shape; since the package Confirm addendum (§5) the package
/// façade <c>ChocolateyPackageUpdateProducers</c> constructs a second composition, only <c>ChocolateyPackageUpdateComposition.CreateSession</c>
/// constructs that façade, its workflow and its route dispatcher, and that dispatcher admits only the package shape;</item>
/// <item>their test-only factories have no <c>src</c> caller;</item>
/// <item>their code never reaches a process, the launcher, the N3 client surface, the helper item, the helper or its
/// transport, the file system directly, the registry or the environment: the only native call is the Desktop-role
/// observation over the current process token, with no expected fingerprint;</item>
/// <item>the evidence identity is formed only from the fresh observation's fingerprint;</item>
/// <item>the verifier runs the file-only capture before the target re-observation and never reports a restart.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class DesktopProducersBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Source = Native + "ChocolateyProfileEvidenceSource";
    private const string Verifier = Native + "ChocolateyReviewedUpdateVerifier";
    private const string Observation = Native + "ChocolateyDesktopObservation";
    private const string Baselines = Native + "ChocolateyDispatchBaselines";

    private static readonly string[] ProducerTypes = [Source, Verifier, Observation, Baselines];

    private static readonly string[] ProducerFiles =
        ["ChocolateyProfileEvidenceSource.cs", "ChocolateyReviewedUpdateVerifier.cs", "ChocolateyDesktopObservation.cs", "ChocolateyReviewedRouteComposition.cs"];

    /// <summary>Target types (exact, or as a prefix ending in "." or "+") that producer code must never reference.</summary>
    private static readonly string[] BannedTargets =
    [
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo", "System.IO.File", "System.IO.FileStream", "System.IO.FileInfo",
        "System.IO.Directory", "System.IO.DirectoryInfo", "System.IO.RandomAccess", "Microsoft.Win32.Registry", "Microsoft.Win32.RegistryKey",
        "System.Environment", "Microsoft.Data.Sqlite.",
        Native + "LaunchJob", Native + "NativeLaunchSpec", Native + "ChocolateyClientQueries", Native + "ChocolateyCompiledCommand",
        Native + "ChocolateyHeldProfileFiles", Native + "NativeChocolateyHelperItem", Native + "NativeChocolateyHelperQualification",
        Native + "NativeChocolateyPeer", Native + "PeerProcess", Native + "ProtectedNodeReader", Native + "ProtectedTreeWalker",
        Native + "RequesterAccess", Native + "ChocolateyObserverTestSeam", "PCSteward.Execution.", "PCSteward.ElevatedHelper.",
    ];

    /// <summary>
    /// N6 (obligations (a)/(b)): the single composition point. It is the only <c>src</c> constructor site of the producers
    /// and their shared baselines. Chocolatey Confirm design §3: its only <c>src</c> constructor caller is the public façade
    /// <see cref="Producers"/>.
    /// </summary>
    private const string Composition = Native + "ChocolateyReviewedRouteComposition";

    /// <summary>Chocolatey Confirm design §3: the public façade over one N6 composition for the client self-update.</summary>
    private const string Producers = "PCSteward.Provider.Chocolatey.ChocolateyClientUpdateProducers";

    /// <summary>Chocolatey Confirm design §3 item 1: the single production composition of the client self-update pipeline.</summary>
    private const string ClientComposition = "PCSteward.Execution.ChocolateyClientUpdateComposition";
    private const string ClientCompositionSite = ClientComposition + ".CreateSession";

    /// <summary>Chocolatey package Confirm addendum §5: the package route's façade and its single production composition.</summary>
    private const string PackageProducers = "PCSteward.Provider.Chocolatey.ChocolateyPackageUpdateProducers";
    private const string PackageCompositionSite = "PCSteward.Execution.ChocolateyPackageUpdateComposition.CreateSession";
    private static readonly string[] CompositionSites = [ClientCompositionSite, PackageCompositionSite];

    [TestMethod]
    public void TheProducersAreTheOnlyPortImplementationsAndOnlyTheCompositionConstructsThem()
    {
        List<string> assemblies = RequireBuilt();
        List<string> sources = [], verifiers = [];
        foreach (string path in assemblies)
        {
            sources.AddRange(Implementations(path, "PCSteward.Application.IReviewedUpdateDispatchEvidenceSource"));
            verifiers.AddRange(Implementations(path, "PCSteward.Application.IReviewedUpdateVerifier"));
        }
        CollectionAssert.AreEquivalent(new[] { Source }, sources, string.Join(Environment.NewLine, sources));
        CollectionAssert.AreEquivalent(new[] { Verifier }, verifiers, string.Join(Environment.NewLine, verifiers));

        List<string> violations = [];
        HashSet<string> compositionConstructs = [];
        foreach (string path in assemblies)
        {
            bool chocolatey = Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey";
            if (!chocolatey)
                foreach (string type in CompiledIlScanner.TypeReferences(path).Where(type => ProducerTypes.Contains(type) || type == Composition))
                    violations.Add($"{Path.GetFileName(path)} references {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetMember != ".ctor") continue;
                if (reference.TargetType == Composition && reference.Caller != Composition &&
                    !(chocolatey && reference.LogicalCaller is Producers + "..ctor" or PackageProducers + "..ctor"))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the composition (only its public façades may)");
                if (reference.TargetType == Producers && reference.LogicalCaller != ClientCompositionSite)
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the producer façade (only the client composition may)");
                if (reference.TargetType == PackageProducers && reference.LogicalCaller != PackageCompositionSite)
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the package producer façade (only the package composition may)");
                if (!ProducerTypes.Contains(reference.TargetType) || reference.Caller == reference.TargetType) continue;
                // The one permitted site: the composition's constructor (its static factory lambdas included).
                if (chocolatey && reference.LogicalCaller == Composition + "..ctor" &&
                    reference.TargetType is Source or Verifier or Baselines)
                    compositionConstructs.Add(reference.TargetType);
                else
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs {reference.TargetType}");
            }
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        CollectionAssert.AreEquivalent(new[] { Source, Verifier, Baselines }, compositionConstructs.ToArray(),
            "the composition constructs the source, the verifier and exactly one baseline store");
    }

    [TestMethod]
    public void TheCompositionSharesOneBaselineStoreAndSuppliesNoDispatcherOrAdmission()
    {
        string source = Read("ChocolateyReviewedRouteComposition.cs");
        Assert.AreEqual(1, Regex.Count(source, @"new\s*\(\s*\)|new ChocolateyDispatchBaselines\s*\("), "exactly one baseline store construction");
        StringAssert.Contains(source, "ChocolateyDispatchBaselines baselines = new();");
        StringAssert.Contains(source, "evidenceSource = createSource(baselines)");
        StringAssert.Contains(source, "verifier = createVerifier(baselines)");
        Assert.IsFalse(Regex.IsMatch(source, @"\b(IReviewedUpdateRouteDispatcher|ReviewedUpdateDispatchWorkflow|HasSupportedMode|Supports|ReviewedUpdateReview|runas|ElevatedHelper|HelperTransport)\b"),
            "the composition supplies no dispatcher, workflow or admission");

        // The test-only factories have no src caller.
        List<string> violations = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Composition && reference.TargetMember is "CreateForTests" or "CreateWithClientReviewForTests")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls the composition's test-only factory");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The CRITICAL scope limit of N6, changed deliberately by the Task 50 activation (ADR-031 owner decision 7, step C-A): the
    /// review gate admits the runtime-limit Chocolatey route only by its exact shape, through the single
    /// <c>ReviewedChocolateyRouteAdmission.Admits</c> term after the unchanged WinGet rule (one route comparison in the gate
    /// itself). Changed deliberately by the Chocolatey Confirm build (design §3, §5 items 2 and 6, §6): the reviewed dispatch
    /// workflow and the route dispatcher (<c>PCSteward.Execution.ChocolateyRuntimeLimitsRouteDispatcher</c>) are constructed only in
    /// <c>ChocolateyClientUpdateComposition.CreateSession</c>, and the dispatcher it composes is wrapped by
    /// <c>ClientRouteOnlyDispatcher</c>, which refuses every operation other than the client self-update. Changed deliberately by
    /// the package Confirm addendum (§5): <c>ChocolateyPackageUpdateComposition.CreateSession</c> is the one other construction site,
    /// and the dispatcher it composes is wrapped by <c>PackageRouteOnlyDispatcher</c>, which refuses every operation other than the
    /// exact package shape. So each Chocolatey route is reachable only through its own composition.
    /// </summary>
    [TestMethod]
    public void TheChocolateyRoutesAreAdmittedOnlyByShapeAndEachIsComposedOnlyByItsOwnComposition()
    {
        string review = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Application", "ReviewedUpdateReview.cs"));
        Match gate = Regex.Match(review, @"internal static bool HasSupportedMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(gate.Success, "HasSupportedMode not found");
        string body = gate.Groups["body"].Value;
        StringAssert.Contains(body, "operation.RouteContract == UpdateRouteContract.WinGetUserExactV3");
        Assert.IsTrue(Regex.Replace(body, @"\s+", " ").TrimEnd().EndsWith("|| ReviewedChocolateyRouteAdmission.Admits(operation)", StringComparison.Ordinal),
            "the Chocolatey routes are admitted only through the activation term");
        Assert.AreEqual(1, Regex.Count(body, "Chocolatey"), "the gate itself names Chocolatey only in the activation term");
        Assert.AreEqual(1, Regex.Count(body, @"RouteContract\s*=="), "HasSupportedMode compares the route with exactly one contract itself");

        // Only the two Chocolatey compositions compose the reviewed dispatch workflow and the route dispatcher.
        const string RouteDispatcher = "PCSteward.Execution.ChocolateyRuntimeLimitsRouteDispatcher";
        List<string> assemblies = RequireBuilt();
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        Assert.IsTrue(CompiledIlScanner.MethodDefinitions(execution).Any(method => method.Type == RouteDispatcher && method.Method == ".ctor"),
            "the scan must see the route dispatcher's constructor, or its non-construction proves nothing");
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetMember == ".ctor" && reference.TargetType == "PCSteward.Application.ReviewedUpdateDispatchWorkflow" &&
                    !CompositionSites.Contains(reference.LogicalCaller))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the reviewed dispatch workflow");
                // Its own constructor chaining (": this(...)") and the client composition only.
                if (reference.TargetMember == ".ctor" && reference.TargetType == RouteDispatcher &&
                    !(reference.Caller == RouteDispatcher && reference.CallerMethod == ".ctor") && !CompositionSites.Contains(reference.LogicalCaller))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the runtime-limit route dispatcher");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        Assert.IsTrue(CompiledIlScanner.References(execution).Any(reference => reference.LogicalCaller == ClientCompositionSite &&
            reference.TargetType == "PCSteward.Application.ReviewedUpdateDispatchWorkflow" && reference.TargetMember == ".ctor"),
            "the scan must see the composition construct the workflow");

        // Design §5 item 6: the composed dispatcher is the client-only wrapper around the route dispatcher, and it refuses any other shape.
        string composition = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ChocolateyClientUpdateComposition.cs"));
        StringAssert.Contains(composition, "ClientRouteOnlyDispatcher dispatcher = new(new ChocolateyRuntimeLimitsRouteDispatcher(capability, transport), phases);");
        StringAssert.Contains(composition, "ReviewedUpdateDispatchWorkflow workflow = new(runUnderGate, producers.EvidenceSource, dispatcher, producers.Verifier, store);");
        StringAssert.Contains(composition, "try { return IsClientRoute(operation) && inner.Supports(operation); }");
        StringAssert.Contains(composition, "if (!IsClientRoute(ticket.Operation))");
        StringAssert.Contains(composition, "operation is not null && operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&");
        Assert.AreEqual(1, Regex.Count(composition, @"ReviewedUpdateDispatchWorkflow\s+\w+\s*=\s*new\("), "one workflow per session");
        foreach (string site in CompositionSites)
            Assert.IsTrue(CompiledIlScanner.References(execution).Any(reference => reference.LogicalCaller == site &&
                reference.TargetType == "PCSteward.Application.ReviewedUpdateDispatchWorkflow" && reference.TargetMember == ".ctor"), $"the scan must see {site} construct the workflow");

        // Addendum §5: the package composition's dispatcher is the package-only wrapper, and it refuses any other shape.
        string package = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ChocolateyPackageUpdateComposition.cs"));
        StringAssert.Contains(package, "PackageRouteOnlyDispatcher dispatcher = new(new ChocolateyRuntimeLimitsRouteDispatcher(capability, transport), phases);");
        StringAssert.Contains(package, "ReviewedUpdateDispatchWorkflow workflow = new(runUnderGate, producers.EvidenceSource, dispatcher, producers.Verifier, store);");
        StringAssert.Contains(package, "try { return IsPackageRoute(operation) && inner.Supports(operation); }");
        StringAssert.Contains(package, "if (!IsPackageRoute(ticket.Operation))");
        StringAssert.Contains(package, "operation is not null && operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 &&");
        StringAssert.Contains(package, "!ReviewedUpdateOperation.IsChocolateyClientIdentity(operation.Selection.PackageId);");
        Assert.AreEqual(1, Regex.Count(package, @"ReviewedUpdateDispatchWorkflow\s+\w+\s*=\s*new\("), "one workflow per session");
    }

    [TestMethod]
    public void TheProducerTestFactoriesHaveNoSrcCaller()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        (string Type, string Member)[] testOnly = [(Source, "CreateForTests"), (Verifier, "CreateForTests")];
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        foreach ((string type, string member) in testOnly)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");
        List<string> violations = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (testOnly.Contains((reference.TargetType, reference.TargetMember)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls test-only {reference.TargetType}.{reference.TargetMember}");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void ProducerCodeNeverLaunchesElevatesWritesOrReadsAmbientState()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        List<string> violations = [];
        int scanned = 0;
        foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(chocolatey))
        {
            if (!ProducerTypes.Append(Composition).Any(type => reference.Caller == type || reference.Caller.StartsWith(type + "+", StringComparison.Ordinal))) continue;
            scanned++;
            if (BannedTargets.Any(banned => reference.TargetType == banned ||
                    (banned.EndsWith('.') || banned.EndsWith('+')) && reference.TargetType.StartsWith(banned, StringComparison.Ordinal) ||
                    reference.TargetType.StartsWith(banned + "+", StringComparison.Ordinal)))
                violations.Add($"{reference.LogicalCaller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.IsTrue(scanned > 20, $"only {scanned} producer references resolved");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        string observation = Read("ChocolateyDesktopObservation.cs");
        StringAssert.Contains(observation,
            "ChocolateyProfileObserver.Observe(token, ChocolateyObserverRole.Desktop, targetId, frozenInstalled, expectedFingerprint: null,");
        StringAssert.Contains(observation, "RequesterToken.TryFromCurrentProcess(out _)");
        foreach (string file in ProducerFiles)
        {
            string source = Read(file);
            Assert.IsFalse(source.Contains("ChocolateyObserverRole.Helper", StringComparison.Ordinal), $"{file} observes in the Helper role");
            Assert.IsFalse(Regex.IsMatch(source, @"\b(runas|Verb\s*=|UseShellExecute|ElevatedHelper|HelperTransport|ReviewedHelper)\b"), $"{file} names elevation or the helper");
        }
    }

    [TestMethod]
    public void TheEvidenceIdentityIsFormedOnlyFromTheFreshFingerprint()
    {
        string source = Read("ChocolateyProfileEvidenceSource.cs");
        Assert.AreEqual(1, Regex.Count(source, @"new FreshUpdateDispatchEvidence\("), "one evidence construction");
        StringAssert.Contains(source, "string freshEvidenceId = ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint;");
        StringAssert.Contains(source, "freshEvidenceId, observedAt, operation.RouteContract, operation.Mode, actionable: true, sourceIsFresh: true);");
        // N5 review L4: the evidence time is taken before the observation starts.
        int stamp = source.IndexOf("DateTimeOffset observedAt = time.GetUtcNow();", StringComparison.Ordinal);
        int observe = source.IndexOf("ChocolateyDesktopObservation.TryObserveAsync(", StringComparison.Ordinal);
        Assert.IsTrue(stamp >= 0 && stamp < observe, "the evidence is timestamped before the observation");
        Assert.IsFalse(Regex.IsMatch(source, @"\.\s*EvidenceId\b"), "the frozen EvidenceId is never read");
        // The requester is the fresh token's, not the operation's (which admission compares it with).
        StringAssert.Contains(source, "string requester = observation.Common.RequestingUserSid;");
    }

    [TestMethod]
    public void TheVerifierComparesFilesFirstAndNeverReportsARestart()
    {
        string source = Read("ChocolateyReviewedUpdateVerifier.cs");
        int run = source.IndexOf("ChocolateyPostUpdateClientCheck.RunAsync(baseline.ClientState,", StringComparison.Ordinal);
        int capture = source.IndexOf("token => CaptureFilesAsync(", StringComparison.Ordinal);
        int target = source.IndexOf("token => ReobserveTargetAsync(", StringComparison.Ordinal);
        Assert.IsTrue(run >= 0 && run < capture && capture < target, "the file-only capture is RunAsync's first callback, the target its second");
        Assert.IsFalse(Regex.IsMatch(source, @"PostUpdateInventoryResult\(\s*(true|false)\s*,\s*true\b|\bnew\(\s*(true|false)\s*,\s*true\b|RestartPending\s*:\s*true"),
            "the verifier never constructs a restart-pending result");
        Assert.AreEqual(1, Regex.Count(source, @"new PostUpdateInventoryResult\(true, false\)"), "exactly one Success construction");
    }

    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        Assert.IsTrue(assemblies.Count >= 5, "src assemblies were not built");
        return assemblies;
    }

    private static string Read(string file) =>
        File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native", file));

    private static List<string> Implementations(string path, string iface)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> found = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            foreach (InterfaceImplementationHandle implementation in md.GetTypeDefinition(handle).GetInterfaceImplementations())
            {
                EntityHandle target = md.GetInterfaceImplementation(implementation).Interface;
                string? name = target.Kind switch
                {
                    HandleKind.TypeReference => Name(md, (TypeReferenceHandle)target),
                    HandleKind.TypeDefinition => CompiledIlScanner.FullName(md, (TypeDefinitionHandle)target),
                    _ => null,
                };
                if (name == iface) found.Add(CompiledIlScanner.FullName(md, handle));
            }
        return found;
    }

    private static string Name(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference reference = md.GetTypeReference(handle);
        string ns = md.GetString(reference.Namespace);
        return ns.Length == 0 ? md.GetString(reference.Name) : ns + "." + md.GetString(reference.Name);
    }
}
