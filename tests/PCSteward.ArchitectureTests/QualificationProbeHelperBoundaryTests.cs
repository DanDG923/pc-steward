using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §7 (slice S3, helper side), checked on compiled IL of every
/// built <c>src</c> assembly and on source text. Each test names the H-obligation it pins; the behavioural halves of H3, H6–H10
/// and H12 and the H11 trigger are in <c>PCSteward.Security.FixtureTests</c> (<c>QualificationProbeHelper*Tests</c>).
/// </summary>
[TestClass]
public sealed class QualificationProbeHelperBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Execution = "PCSteward.Execution.";
    private const string ProbeBootstrap = Execution + "ReviewedHelperProbeBootstrap";
    private const string ProbeEndpoint = Execution + "ReviewedHelperProbeEndpoint";
    private const string EntryPoint = Execution + "ElevatedHelperEntryPoint";
    private const string ProbePort = Native + "IChocolateyProbePort";
    private const string ProbeItem = Native + "IChocolateyProbeItem";
    private const string NativeProbePort = Native + "NativeChocolateyProbePort";
    private const string NativeProbeItem = Native + "NativeChocolateyProbeItem";
    private const string ProbeValues = Native + "ChocolateyProbeValues";
    private const string ProbeSeam = Native + "ChocolateyProbeItemTestSeam";
    private const string Protection = Native + "ChocolateyRequesterProtection";
    private const string Binding = Native + "ChocolateyHeldClientBinding";
    private const string Observer = Native + "ChocolateyProfileObserver";
    private const string Report = Native + "ChocolateyObservationReport";
    private const string Sink = Native + "RequesterAccessSink";

    /// <summary>The probe path (H1): every type whose code is the probe entry, bootstrap, port, item or their native implementations.</summary>
    private static readonly string[] ProbePathTypes = [ProbeBootstrap, ProbeEndpoint, ProbePort, ProbeItem, NativeProbePort, NativeProbeItem, ProbeValues, ProbeSeam, Protection];

    /// <summary>The entry-point members of the probe path (the rest of the entry point is the update and protocol-1 dispatch).</summary>
    private static readonly string[] ProbeEntryMembers = [EntryPoint + ".RunProbeAsync", EntryPoint + ".SelectsProbe"];

    /// <summary>H1: types the probe path never references (any member; nested types included).</summary>
    private static readonly string[] BannedTypes =
    [
        Native + "LaunchJob", Native + "LaunchJobNativeMethods", Native + "NativeLaunchSpec", Native + "NativeCommandLine",
        Native + "ChocolateyCompiledCommand", Native + "ChocolateyClientQueries", Native + "ChocolateyClientUpdateArguments",
        Native + "ChocolateyExactListArguments", Native + "ChocolateyClientUpdateWatchdog", Native + "IChocolateyHelperItem",
        Native + "NativeChocolateyHelperItem", Native + "IChocolateyHelperQualificationPort", Native + "NativeChocolateyHelperQualification",
        Execution + "ReviewedHelperEndpoint", Native + "ChocolateyHelperItemTestSeam",
    ];

    /// <summary>H1: members the probe path never references.</summary>
    private static readonly string[] BannedMembers =
    [
        Execution + "ReviewedHelperBootstrap.RunAsync", Execution + "ReviewedHelperBootstrap.OpenVerifiedPeer",
        Execution + "ReviewedUpdateWireCodec.TryReadRequest", Execution + "ReviewedUpdateWireCodec.CreateResponse",
        Execution + "ReviewedUpdateWireCodec.CreateClientResponse", Execution + "PipeMessageFraming.ReadAsync",
        "System.Diagnostics.Process.Start", "System.Diagnostics.ProcessStartInfo..ctor",
    ];

    private static readonly string[] ProbeFiles =
    [
        Path.Combine("PCSteward.Execution", "ReviewedHelperProbeBootstrap.cs"), Path.Combine("PCSteward.Execution", "ReviewedHelperProbeEndpoint.cs"),
        Path.Combine("PCSteward.Provider.Chocolatey", "Native", "ChocolateyProbePort.cs"),
        Path.Combine("PCSteward.Provider.Chocolatey", "Native", "NativeChocolateyProbe.cs"),
    ];

    /// <summary>Test-only factories of S3 (banned from every <c>src</c> caller).</summary>
    private static readonly (string Type, string Member)[] TestOnly =
    [
        (NativeProbeItem, "CreateOverFixtureForTests"), (NativeProbeItem, "CreateForTests"), (ProbeSeam, "CreateForTests"),
        (Observer, "ObserveClientWithReportForTests"), (Observer, "ObserveWithReportForTests"),
    ];

    private static readonly string[] ItemMembers = ["RequesterCannotModify", "ObserveClient"];
    private static readonly string[] PortMembers = ["OpenPeer", "BeginProbe"];

    [TestMethod]
    public void H1TheProbePathReferencesNoLaunchQueryUpdateItemEndpointOrRequestSurface()
    {
        List<string> violations = [];
        HashSet<string> seen = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(IsProbePath))
            {
                string target = reference.TargetType + "." + reference.TargetMember;
                seen.Add(reference.LogicalCaller + " -> " + target);
                if (BannedTypes.Any(type => reference.TargetType == type || reference.TargetType.StartsWith(type + "+", StringComparison.Ordinal)) ||
                    BannedMembers.Contains(target))
                    violations.Add($"{reference.LogicalCaller} references {target}");
                // Review M2: the binding is touched only through Dispose and IsDisposed.
                if (reference.TargetType == Binding && reference.TargetMember is not ("Dispose" or "get_IsDisposed" or ""))
                    violations.Add($"{reference.LogicalCaller} uses the held binding through {target}");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // Not vacuous: the scan sees the probe path's own calls.
        foreach (string expected in new[]
        {
            ProbeEndpoint + "+Stages.RunAsync -> " + ProbeItem + ".ObserveClient", ProbeEndpoint + "+Stages.RunAsync -> " + ProbePort + ".BeginProbe",
            ProbeEndpoint + ".RunAsync -> " + Execution + "QualificationProbeWireCodec.CreateProbeResponse",
            ProbeEndpoint + ".RunAsync -> " + Execution + "PipeMessageFraming.WriteAsync",
            NativeProbeItem + "+NativeWork.ObserveClient -> " + Binding + ".Dispose", NativeProbeItem + "+NativeWork.ObserveClient -> " + Binding + ".get_IsDisposed",
            NativeProbeItem + ".ProductionObserve -> " + Observer + ".ObserveClientWithReport",
            ProbeBootstrap + ".OpenVerifiedProbePeer -> " + ProbePort + ".OpenPeer",
        })
            Assert.IsTrue(seen.Contains(expected), $"the probe scan did not see {expected}");

        // Source (code lines only): the probe files never name a banned type or member.
        Regex banned = new(@"\b(LaunchJob|LaunchJobNativeMethods|NativeLaunchSpec|NativeCommandLine|ChocolateyCompiledCommand|ChocolateyClientQueries|" +
            @"ChocolateyClientUpdateArguments|ChocolateyExactListArguments|ChocolateyClientUpdateWatchdog|IChocolateyHelperItem|NativeChocolateyHelperItem|" +
            @"IChocolateyHelperQualificationPort|NativeChocolateyHelperQualification|BeginItemWithVerdict|ReviewedHelperEndpoint|OpenVerifiedPeer|" +
            @"TryReadRequest|CreateResponse|CreateClientResponse|UpgradeAsync|QueryCandidateAsync|RevalidateBeforeLaunch|ObserveProfile|Process\.Start)\b",
            RegexOptions.CultureInvariant);
        foreach (string file in ProbeFiles)
        {
            string code = Code(file);
            Assert.IsFalse(banned.IsMatch(code), $"{file} names {banned.Match(code).Value}");
            Assert.IsFalse(Regex.IsMatch(code, @"PipeMessageFraming\s*\.\s*ReadAsync"), $"{file} reads a frame");
            Assert.IsFalse(Regex.IsMatch(code, @"ReviewedHelperBootstrap\s*\.\s*RunAsync"), $"{file} runs the update bootstrap");
        }
    }

    [TestMethod]
    public void H2TheProbeItemHasOnlyItsTwoMembersOneNativeImplementationAndNeverHoldsTheBinding()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = Assembly(assemblies, "PCSteward.Provider.Chocolatey");
        IReadOnlyList<(string Type, string Method)> methods = CompiledIlScanner.MethodDefinitions(chocolatey);
        CollectionAssert.AreEquivalent(ItemMembers,
            methods.Where(method => method.Type == ProbeItem).Select(method => method.Method).ToArray(), "the probe item declares no launch, query or revalidation member");
        CollectionAssert.AreEquivalent(PortMembers, methods.Where(method => method.Type == ProbePort).Select(method => method.Method).ToArray());

        // Exactly one implementation of each probe interface, both in Native/ of the Chocolatey provider.
        List<string> implementations = [];
        foreach (string path in assemblies)
            implementations.AddRange(Implementations(path, [ProbePort, ProbeItem]).Select(type => $"{Path.GetFileName(path)}: {type}"));
        CollectionAssert.AreEquivalent(new[] { "PCSteward.Provider.Chocolatey.dll: " + NativeProbePort, "PCSteward.Provider.Chocolatey.dll: " + NativeProbeItem },
            implementations, string.Join(Environment.NewLine, implementations));

        // No field, property or return value of any probe-path type (compiler-generated nested types included) holds a binding.
        List<string> holders = [];
        foreach (string path in assemblies)
            holders.AddRange(MembersTyped(path, Binding).Where(member => ProbePathTypes.Any(type =>
                member.StartsWith(type + ".", StringComparison.Ordinal) || member.StartsWith(type + "+", StringComparison.Ordinal))));
        Assert.AreEqual(0, holders.Count, string.Join(Environment.NewLine, holders));

        // The binding is obtained and disposed in one method: the only probe-path read of the observation's binding is there.
        string[] readers = [.. CompiledIlScanner.References(chocolatey)
            .Where(reference => IsProbePath(reference) && reference.TargetMember == "get_HeldClient")
            .Select(reference => reference.LogicalCaller).Distinct()];
        CollectionAssert.AreEquivalent(new[] { NativeProbeItem + "+NativeWork.ObserveClient" }, readers);
    }

    [TestMethod]
    public void H2H4TheProbeValuesAreAClosedKindWithoutAFingerprintString()
    {
        string chocolatey = Assembly(RequireBuilt(), "PCSteward.Provider.Chocolatey");
        CollectionAssert.AreEquivalent(new[] { ProbeValues + "+Observed", ProbeValues + "+Refused", ProbeValues + "+TimedOut", ProbeValues + "+Cancelled", ProbeValues + "+Faulted" },
            DerivedFrom(chocolatey, ProbeValues).ToArray(), "the probe values are a closed kind");
        foreach ((MethodAttributes attributes, int parameters) in Constructors(chocolatey, ProbeValues))
            Assert.IsTrue((attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private ||
                parameters == 1 && (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family, "the probe values base is open");
        // H4: only the length of the fingerprint crosses; the only string members are the two version values.
        List<string> strings = [.. Properties(chocolatey).Where(property => property.Type.StartsWith(ProbeValues, StringComparison.Ordinal) &&
            property.PropertyType == "System.String").Select(property => property.Type + "." + property.Name)];
        CollectionAssert.AreEquivalent(new[] { ProbeValues + "+Observed.ClientVersion", ProbeValues + "+Refused.ObservedInstalled" }, strings);
        Assert.IsFalse(Properties(chocolatey).Any(property => property.Type.StartsWith(ProbeValues, StringComparison.Ordinal) &&
            property.Name.Contains("Fingerprint", StringComparison.Ordinal) && property.Name != "FingerprintLength"));
    }

    [TestMethod]
    public void H4ExecutionNamesOnlyTheProbeAllowListAndTheSharedPeerFromThePortSurface()
    {
        // HelperEndpointBoundaryTests pins the whole allow-list; this pins the probe entry's exact native vocabulary.
        string execution = Assembly(RequireBuilt(), "PCSteward.Execution");
        string[] allowed =
        [
            "IChocolateyProbePort", "IChocolateyProbeItem", "ChocolateyProbeValues", "TokenVerdict", "ChocolateyProbeFailedCheck",
            "ChocolateyProbeFailedAccess", "IChocolateyPeer", "NativeChocolateyProbePort",
        ];
        List<string> violations = [];
        foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(execution))
        {
            bool probe = reference.Caller is ProbeBootstrap or ProbeEndpoint || ProbeEntryMembers.Contains(reference.LogicalCaller);
            if (!probe || !reference.TargetType.StartsWith(Native, StringComparison.Ordinal)) continue;
            if (!allowed.Contains(reference.TargetType[Native.Length..].Split('+')[0])) violations.Add($"{reference.LogicalCaller} -> {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // The update stage machine and bootstrap never reference a probe type (an update-mode helper never reaches the probe entry).
        string[] probeTypes = [ProbeBootstrap, ProbeEndpoint, ProbePort, ProbeItem, ProbeValues, NativeProbePort, Execution + "QualificationProbeWireCodec"];
        List<string> crossings = [.. CompiledIlScanner.References(execution)
            .Where(reference => reference.Caller is Execution + "ReviewedHelperEndpoint" or Execution + "ReviewedHelperBootstrap")
            .Where(reference => probeTypes.Any(type => reference.TargetType == type || reference.TargetType.StartsWith(type + "+", StringComparison.Ordinal)))
            .Select(reference => $"{reference.LogicalCaller} -> {reference.TargetType}.{reference.TargetMember}")];
        Assert.AreEqual(0, crossings.Count, string.Join(Environment.NewLine, crossings));
    }

    [TestMethod]
    public void H5OnlyTheNativeProbePortConstructsTheProbeItemAndTheEntryPointConstructsOnlyTheNativePorts()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = Assembly(assemblies, "PCSteward.Provider.Chocolatey");
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        foreach ((string type, string member) in TestOnly)
            Assert.IsTrue(defined.Contains((type, member)) || assemblies.Any(path => CompiledIlScanner.MethodDefinitions(path).Contains((type, member))),
                $"{type}.{member} is not defined; update the ban list");
        (string Type, string Member, string[] Callers)[] pinned =
        [
            (NativeProbeItem, "Create", [NativeProbePort + ".BeginProbe"]),
            (NativeProbePort, ".ctor", [EntryPoint + ".RunProbeAsync"]),
            (Observer, "ObserveClientWithReport", [NativeProbeItem + ".ProductionObserve"]),
            (Report, ".ctor", [NativeProbeItem + "+NativeWork.ObserveClient"]),
            (Native + "ProtectedTreeWalker", "WalkWithSink", [Observer + "+Observation.Walk"]),
            (Native + "RequesterAccess", "EvaluateAncestryWithSink", [Observer + "+Observation.AncestryCore"]),
            (Protection, "CannotModify", [NativeProbeItem + "+NativeWork.RequesterCannotModify", Native + "NativeChocolateyHelperItem+NativeWork.RequesterCannotModify"]),
            (ProbeEndpoint, "RunAsync", [ProbeBootstrap + ".RunAsync", ProbeEndpoint + ".RunAsync"]),
            (ProbeBootstrap, "RunAsync", [EntryPoint + ".RunProbeAsync"]),
            (ProbeBootstrap, "TryParse", [EntryPoint + ".RunProbeAsync"]),
            (ProbeBootstrap, "SelectsProbe", [EntryPoint + ".SelectsProbe"]),
            (EntryPoint, "SelectsProbe", [EntryPoint + ".RunAsync"]),
            (EntryPoint, "RunProbeAsync", [EntryPoint + ".RunAsync"]),
        ];
        List<string> violations = [];
        HashSet<string> reached = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (TestOnly.Contains((reference.TargetType, reference.TargetMember)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls test-only {reference.TargetType}.{reference.TargetMember}");
                if (reference.TargetType == ProbeSeam && !(reference.Caller.StartsWith(NativeProbeItem, StringComparison.Ordinal) || reference.Caller.StartsWith(ProbeSeam, StringComparison.Ordinal)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references the probe item test seam");
                if (reference.TargetMember == ".ctor" && reference.TargetType == NativeProbeItem &&
                    reference.LogicalCaller is not (NativeProbeItem + ".Create" or NativeProbeItem + ".CreateOverFixtureForTests" or NativeProbeItem + ".CreateForTests"))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the native probe item");
                foreach ((string type, string member, string[] callers) in pinned)
                {
                    if (reference.TargetType != type || reference.TargetMember != member) continue;
                    if (callers.Contains(reference.LogicalCaller)) reached.Add(type + "." + member);
                    else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {type}.{member}");
                }
                // H5: the entry point constructs only the two native ports among the native types.
                if (reference.Caller == EntryPoint && reference.TargetMember == ".ctor" && reference.TargetType.StartsWith(Native, StringComparison.Ordinal) &&
                    reference.TargetType is not (NativeProbePort or Native + "NativeChocolateyHelperQualification"))
                    violations.Add($"{reference.LogicalCaller} constructs {reference.TargetType}");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        CollectionAssert.AreEquivalent(pinned.Select(pin => pin.Type + "." + pin.Member).Distinct().ToArray(), reached.ToArray(), "every pinned edge is reached");

        // The native probe item's constructor stays private.
        foreach ((MethodAttributes attributes, _) in Constructors(chocolatey, NativeProbeItem))
            Assert.AreEqual(MethodAttributes.Private, attributes & MethodAttributes.MemberAccessMask, "the native probe item has a non-private constructor");
        // The helper's Program only forwards to the entry point.
        string program = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.ElevatedHelper", "Program.cs"));
        StringAssert.Contains(program, "private static Task<int> Main(string[] args) => ElevatedHelperEntryPoint.RunAsync(args);");
    }

    [TestMethod]
    public void H6TheProbeIsSelectedFirstAndTheUpdateDispatchIsUnchanged()
    {
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        string transport = File.ReadAllText(Path.Combine(src, "WindowsElevatedHelperTransport.cs"));
        Assert.IsTrue(Regex.IsMatch(transport,
            @"SelectsProbe\(arguments\)\s*\?\s*RunProbeAsync\(arguments\)\s*:\s*SelectsProtocol2\(arguments\)\s*\?\s*RunReviewedAsync\(arguments\)\s*:\s*RunProtocol1Async\(arguments\);"),
            "§2 order: the probe first, then protocol 2, then protocol 1");
        string bootstrap = File.ReadAllText(Path.Combine(src, "ReviewedHelperProbeBootstrap.cs"));
        StringAssert.Contains(bootstrap, "arguments is { Length: ArgumentCount } && string.Equals(arguments[0], KindKey, StringComparison.Ordinal);");
        StringAssert.Contains(bootstrap, "private const int ArgumentCount = 10;");
        StringAssert.Contains(bootstrap, "WindowsReviewedHelperTransport.PipeNamePattern().IsMatch(arguments[7])");
        StringAssert.Contains(bootstrap, "TokenImpersonationLevel.Identification");
        // The existing parsers still require exactly eight arguments first.
        StringAssert.Contains(File.ReadAllText(Path.Combine(src, "ReviewedHelperBootstrap.cs")), "if (arguments is null || arguments.Length != 8) return false;");
        StringAssert.Contains(transport, "if (arguments is null || arguments.Length != 8)");
    }

    [TestMethod]
    public void S4DesktopReachesTheProbeOnlyThroughTheEntryAndScheduledHostAndApplicationReferenceNoProbeType()
    {
        // S4 (probe spec rev 4 §7 D1/D2): the desktop is wired only through the entry's public surface; ScheduledHost,
        // Application and every other assembly reference no probe type.
        string[] probe = [ProbeBootstrap, ProbeEndpoint, Execution + "QualificationProbe", Execution + "ReviewedHelperProbe", Execution + "IQualificationProbe",
            Execution + "IReviewedHelperProbe", ProbePort, ProbeItem, ProbeValues, NativeProbePort, NativeProbeItem];
        string[] desktopSurface = [Execution + "QualificationProbeEntry", Execution + "QualificationProbeLaunch", Execution + "IQualificationProbeGateSession"];
        List<string> violations = [];
        foreach (string path in RequireBuilt())
        {
            string assembly = Path.GetFileNameWithoutExtension(path);
            if (assembly is "PCSteward.Execution" or "PCSteward.Provider.Chocolatey") continue;
            foreach (string type in CompiledIlScanner.TypeReferences(path))
                if (probe.Any(prefix => type.StartsWith(prefix, StringComparison.Ordinal)) &&
                    !(assembly == "PCSteward.Desktop" && desktopSurface.Contains(type, StringComparer.Ordinal)))
                    violations.Add($"{assembly} references {type}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        Regex names = new(@"\b(ReviewedHelperProbe\w*|QualificationProbe\w*|IQualificationProbe\w*|IReviewedHelperProbe\w*|IChocolateyProbe\w*|ChocolateyProbe\w*|NativeChocolateyProbe\w*|RunProbeAsync|SelectsProbe)\b",
            RegexOptions.CultureInvariant);
        foreach (string project in new[] { "PCSteward.ScheduledHost", "PCSteward.Application" })
            foreach (string file in Sources(Path.Combine(src, project)))
                Assert.IsFalse(names.IsMatch(File.ReadAllText(file)), $"{file} names a probe type");
        string[] desktopFiles = [.. Sources(Path.Combine(src, "PCSteward.Desktop")).Where(file => names.IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
        string[] expectedDesktopFiles = ["App.xaml.cs", "QualificationProbeLaunchGate.cs"];
        CollectionAssert.AreEqual(expectedDesktopFiles, desktopFiles, "only the launch branch and its gate name the probe");
        foreach (string file in desktopFiles)
            foreach (Match name in names.Matches(File.ReadAllText(Path.Combine(src, "PCSteward.Desktop", file!))))
                Assert.IsTrue(name.Value is "QualificationProbeEntry" or "QualificationProbeLaunch" or "IQualificationProbeGateSession" or "QualificationProbeLaunchGate",
                    $"{file} names {name.Value}");
    }

    [TestMethod]
    public void H9H12NoStaticThreadStaticOrAmbientStateCarriesTheSinkReportOrCounts()
    {
        string chocolatey = Assembly(RequireBuilt(), "PCSteward.Provider.Chocolatey");
        using FileStream stream = File.OpenRead(chocolatey);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> violations = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            string type = CompiledIlScanner.FullName(md, handle);
            foreach (FieldDefinitionHandle fieldHandle in md.GetTypeDefinition(handle).GetFields())
            {
                FieldDefinition field = md.GetFieldDefinition(fieldHandle);
                string fieldType = field.DecodeSignature(Names.Instance, null);
                bool carries = fieldType.Contains(Sink, StringComparison.Ordinal) || fieldType.Contains(Report, StringComparison.Ordinal);
                if (carries && (field.Attributes & FieldAttributes.Static) != 0) violations.Add($"static {type}.{md.GetString(field.Name)}");
                if (carries && (fieldType.Contains("AsyncLocal", StringComparison.Ordinal) || fieldType.Contains("ThreadLocal", StringComparison.Ordinal)))
                    violations.Add($"ambient {type}.{md.GetString(field.Name)}");
                // The counters live only in the sink instance.
                if (type == Sink && (field.Attributes & FieldAttributes.Static) != 0) violations.Add($"static sink field {md.GetString(field.Name)}");
            }
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        string native = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Provider.Chocolatey", "Native");
        foreach (string file in new[] { "RequesterAccess.cs", "ProtectedTreeWalker.cs", "ChocolateyProfileObserver.cs", "NativeChocolateyProbe.cs", "ChocolateyProbePort.cs" })
        {
            string source = File.ReadAllText(Path.Combine(native, file));
            foreach (string token in new[] { "ThreadStatic", "AsyncLocal", "ThreadLocal", "static RequesterAccessSink", "static ChocolateyObservationReport" })
                Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"{file} uses {token}");
        }
    }

    [TestMethod]
    public void H10TheProbeBudgetsAndTheStopWindowMatchTheSpec()
    {
        string endpoint = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ReviewedHelperProbeEndpoint.cs"));
        StringAssert.Contains(endpoint, "new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),");
        StringAssert.Contains(endpoint, "budgets.ResponseWrite < budgets.ResponseMargin ? budgets.ResponseWrite : budgets.ResponseMargin;");
        string code = Code(Path.Combine("PCSteward.Execution", "ReviewedHelperProbeEndpoint.cs"));
        Assert.AreEqual(1, Regex.Count(code, @"PipeMessageFraming\.WriteAsync\("), "exactly one response write");
        Assert.AreEqual(1, Regex.Count(code, @"QualificationProbeWireCodec\.CreateProbeResponse\("), "the response is built only through the probe codec");
        Assert.AreEqual(1, Regex.Count(code, @"\.ObserveClient\("), "exactly one B′ observation call site");
        Assert.AreEqual(1, Regex.Count(code, @"pipe\.ReadAsync\("), "exactly one inbound read (the monitor)");
        foreach (string token in new[] { "Sqlite", "Registry", "GetEnvironmentVariable", "File.", "Directory.", "Process.", "HttpClient", "DateTime.Now", "DateTime.UtcNow", "Stopwatch" })
            Assert.IsFalse(code.Contains(token, StringComparison.Ordinal), $"the probe entry uses {token}");
    }

    [TestMethod]
    public void H3StopDisposesTheItemBeforeTheResponseIsBuiltInCompiledOrder()
    {
        // Spec §3 Stop: the item is disposed, then the frame is built. The behavioural H3 test sees only the write, so this pins
        // the order of the two calls in the compiled body of RunAsync (its async state machine's MoveNext), in IL order.
        string execution = Assembly(RequireBuilt(), "PCSteward.Execution");
        List<CompiledIlScanner.IlReference> body = [.. CompiledIlScanner.References(execution)
            .Where(reference => reference.CallerType is { } type && type.StartsWith(ProbeEndpoint + "+<RunAsync>", StringComparison.Ordinal) &&
                reference.CallerMethod == "MoveNext")];
        Assert.AreNotEqual(0, body.Count, "the RunAsync state machine was not found");
        int[] disposals = [.. body.Select((reference, index) => (reference, index))
            .Where(pair => pair.reference.TargetType == ProbeEndpoint + "+Stages" && pair.reference.TargetMember == "DisposeItem").Select(pair => pair.index)];
        int[] builds = [.. body.Select((reference, index) => (reference, index))
            .Where(pair => pair.reference.TargetType == Execution + "QualificationProbeWireCodec" && pair.reference.TargetMember == "CreateProbeResponse")
            .Select(pair => pair.index)];
        Assert.AreEqual(1, disposals.Length, "exactly one Stop disposal of the item in RunAsync");
        Assert.AreEqual(1, builds.Length, "exactly one response build in RunAsync");
        Assert.IsLessThan(builds[0], disposals[0], "Stop: the item is disposed before the response is built");
        // Source (LF-normalised, code lines only): the same order in the text.
        string code = Code(Path.Combine("PCSteward.Execution", "ReviewedHelperProbeEndpoint.cs")).Replace("\r", string.Empty, StringComparison.Ordinal);
        int dispose = code.IndexOf("stages.DisposeItem();", StringComparison.Ordinal);
        int build = code.IndexOf("QualificationProbeWireCodec.CreateProbeResponse(", StringComparison.Ordinal);
        Assert.IsTrue(dispose >= 0 && build > dispose, "Stop: stages.DisposeItem() precedes CreateProbeResponse in the source");
    }

    [TestMethod]
    public void H2TheObservedBindingIsCapturedFirstAndDisposedInAFinally()
    {
        // Review (S3): a value getter that throws must not leave the binding open. The native work is Windows-only, so the
        // structure is pinned on the source (LF-normalised, code lines only); the Faulted result is the item's existing catch.
        string code = Code(Path.Combine("PCSteward.Provider.Chocolatey", "Native", "NativeChocolateyProbe.cs")).Replace("\r", string.Empty, StringComparison.Ordinal);
        Match block = Regex.Match(code,
            @"ChocolateyHeldClientBinding\? binding = null;\s*string version;\s*string fingerprint;\s*try\s*\{\s*" +
            @"ChocolateyProfileObservation observation = observed\.Observation;\s*binding = observation\.HeldClient;\s*" +
            @"version = observation\.InstalledVersion\.Normalized;\s*fingerprint = observation\.Fingerprint;\s*\}\s*" +
            @"finally\s*\{\s*try\s*\{\s*binding\?\.Dispose\(\);\s*\}", RegexOptions.CultureInvariant);
        Assert.IsTrue(block.Success, "the binding is captured before the values and disposed in a finally");
        Assert.AreEqual(1, Regex.Count(code, @"binding\?\.Dispose\(\)"), "one disposal site");
        StringAssert.Contains(code, "bool releasedBinding = binding is not null && binding.IsDisposed;");
    }

    // ---- Helpers ----

    private static bool IsProbePath(CompiledIlScanner.IlReference reference) =>
        ProbePathTypes.Any(type => reference.Caller == type || reference.Caller.StartsWith(type + "+", StringComparison.Ordinal)) ||
        ProbeEntryMembers.Contains(reference.LogicalCaller);

    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");
        return assemblies;
    }

    private static string Assembly(List<string> assemblies, string name) => assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == name);

    private static string Code(string relative)
    {
        string path = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", relative);
        return string.Join('\n', File.ReadAllText(path).Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Sources(string root) => !Directory.Exists(root) ? [] : Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary>"Type.Member" of every field, property and method return type whose signature names <paramref name="typeName"/>.</summary>
    private static List<string> MembersTyped(string path, string typeName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> found = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            TypeDefinition definition = md.GetTypeDefinition(handle);
            string type = CompiledIlScanner.FullName(md, handle);
            foreach (FieldDefinitionHandle field in definition.GetFields())
                if (md.GetFieldDefinition(field).DecodeSignature(Names.Instance, null).Contains(typeName, StringComparison.Ordinal))
                    found.Add($"{type}.{md.GetString(md.GetFieldDefinition(field).Name)} (field)");
            foreach (PropertyDefinitionHandle property in definition.GetProperties())
                if (md.GetPropertyDefinition(property).DecodeSignature(Names.Instance, null).ReturnType.Contains(typeName, StringComparison.Ordinal))
                    found.Add($"{type}.{md.GetString(md.GetPropertyDefinition(property).Name)} (property)");
            foreach (MethodDefinitionHandle method in definition.GetMethods())
                if (md.GetMethodDefinition(method).DecodeSignature(Names.Instance, null).ReturnType.Contains(typeName, StringComparison.Ordinal))
                    found.Add($"{type}.{md.GetString(md.GetMethodDefinition(method).Name)} (return)");
        }
        return found;
    }

    private static List<(string Type, string Name, string PropertyType)> Properties(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<(string, string, string)> found = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
            foreach (PropertyDefinitionHandle property in md.GetTypeDefinition(handle).GetProperties())
            {
                PropertyDefinition definition = md.GetPropertyDefinition(property);
                found.Add((CompiledIlScanner.FullName(md, handle), md.GetString(definition.Name), definition.DecodeSignature(Names.Instance, null).ReturnType));
            }
        return found;
    }

    private static List<(MethodAttributes Attributes, int Parameters)> Constructors(string path, string fullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<(MethodAttributes, int)> result = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            if (CompiledIlScanner.FullName(md, handle) != fullName) continue;
            foreach (MethodDefinitionHandle methodHandle in md.GetTypeDefinition(handle).GetMethods())
            {
                MethodDefinition method = md.GetMethodDefinition(methodHandle);
                if (md.GetString(method.Name) == ".ctor") result.Add((method.Attributes, method.GetParameters().Count));
            }
        }
        Assert.IsTrue(result.Count > 0, $"{fullName} has no constructor");
        return result;
    }

    private static List<string> DerivedFrom(string path, string baseFullName)
    {
        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);
        MetadataReader md = pe.GetMetadataReader();
        List<string> derived = [];
        foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
        {
            EntityHandle baseType = md.GetTypeDefinition(handle).BaseType;
            if (!baseType.IsNil && baseType.Kind == HandleKind.TypeDefinition && CompiledIlScanner.FullName(md, (TypeDefinitionHandle)baseType) == baseFullName)
                derived.Add(CompiledIlScanner.FullName(md, handle));
        }
        return derived;
    }

    private static List<string> Implementations(string path, string[] interfaces)
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
                    HandleKind.TypeDefinition => CompiledIlScanner.FullName(md, (TypeDefinitionHandle)target),
                    HandleKind.TypeReference => Names.Instance.GetTypeFromReference(md, (TypeReferenceHandle)target, 0),
                    _ => null,
                };
                if (name is not null && interfaces.Contains(name)) found.Add(CompiledIlScanner.FullName(md, handle));
            }
        return found;
    }

    /// <summary>Full metadata names for decoded signatures.</summary>
    private sealed class Names : ISignatureTypeProvider<string, object?>
    {
        internal static readonly Names Instance = new();

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => CompiledIlScanner.FullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference reference = reader.GetTypeReference(handle);
            string name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference) return GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, 0) + "+" + name;
            string ns = reader.GetString(reference.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
