using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §2 (rev-4 L1, rev-5 F1), §5.1–§5.4 and §8 N4a, checked on
/// compiled IL of every built <c>src</c> assembly and on source text:
/// <list type="bullet">
/// <item>the only <c>ReviewedUpdateWireCodec.CreateResponse</c> call site in <c>src</c> is the endpoint (besides the codec's
/// own canonical re-encode), and the endpoint never references the protocol-1 <c>ElevatedHelperProtocol</c>;</item>
/// <item>Execution references only the §5.4 allow-list of native types: the endpoint the port interfaces and value records,
/// the protocol-2 bootstrap <c>OpenPeer</c>, the handle-based <c>VerifyPeer</c> overload the peer, and the composition root
/// (<c>ElevatedHelperEntryPoint.RunReviewedAsync</c>) only the native port's constructor (N4b);</item>
/// <item>the stage and upgrade value records are closed kinds; <c>NativeChocolateyHelperQualification</c>, its item and its peer
/// are the only <c>src</c> implementations of the port interfaces and <c>HelperDeploymentCapabilityCheck</c> the only one of the
/// G1 seam; the composition root constructs only the native port;</item>
/// <item>the native item's test seams have no <c>src</c> caller;</item>
/// <item>by compiled signature (N4b review L5): the G1 check's production constructor, the Helper-role reader constructor,
/// <c>ResolveHelperLayout</c> and the <c>ReviewedHelperHost</c> constructor each have only their pinned callers, and protocol-2
/// peer trust uses the shared cache-only Authenticode policy;</item>
/// <item><c>--protocol 2</c> is dispatched to the reviewed bootstrap, which alone calls the endpoint and accepts only the
/// protocol-2 locator; the protocol-1 path names no protocol-2 type and still refuses <c>--protocol 2</c> itself.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class HelperEndpointBoundaryTests
{
    private const string Native = "PCSteward.Provider.Chocolatey.Native.";
    private const string Endpoint = "PCSteward.Execution.ReviewedHelperEndpoint";
    private const string Codec = "PCSteward.Execution.ReviewedUpdateWireCodec";
    private const string StageValues = Native + "ChocolateyStageValues";
    private const string UpgradeResult = Native + "ChocolateyUpgradeResult";

    /// <summary>
    /// §2 allow-list: the port interfaces of §5.4 plus the value records and their reason enums (rev-5 F1). The two reason
    /// enums are carried by the value records; they are named here explicitly so nothing else can slip in. ADR-031 design §8.1
    /// adds the plain run-fault enum carried by the upgrade result (no watchdog type crosses).
    /// </summary>
    private static readonly string[] AllowList =
    [
        "IChocolateyHelperQualificationPort", "IChocolateyPeer", "IChocolateyHelperItem", "ChocolateyStageValues", "ChocolateyCandidateValues",
        "ChocolateyUpgradeResult", "NativeProcessObservation", "NativeInvocation", "ChocolateyItemRefusal", "ChocolateyStageRefusal",
        "ChocolateyCandidateRefusal", "ChocolateyClientRunFault",
    ];

    /// <summary>
    /// Task 50 qualification probe spec rev 4 §7 H4: the Execution allow-list of native names grows only by the probe port and
    /// item, their value record, the token verdict and the two closed enums behind <c>failedCheck</c> and <c>failedAccess</c>.
    /// The probe entry may also use the shared peer (<c>TryReadIdentity</c>) and nothing else of the update port's surface.
    /// </summary>
    private static readonly string[] ProbeAllowList =
    [
        "IChocolateyProbePort", "IChocolateyProbeItem", "ChocolateyProbeValues", "TokenVerdict", "ChocolateyProbeFailedCheck",
        "ChocolateyProbeFailedAccess",
    ];

    private const string ProbeEndpoint = "PCSteward.Execution.ReviewedHelperProbeEndpoint";
    private const string ProbeBootstrap = "PCSteward.Execution.ReviewedHelperProbeBootstrap";
    private const string NativeProbePort = Native + "NativeChocolateyProbePort";

    private static readonly string[] PortCalls =
    [
        "IChocolateyHelperQualificationPort.BeginItem", "IChocolateyHelperItem.ObserveProfile", "IChocolateyHelperItem.QueryCandidateAsync",
        "IChocolateyHelperItem.RevalidateBeforeLaunch", "IChocolateyHelperItem.UpgradeAsync", "IChocolateyHelperItem.get_AggregateSettled",
        "IChocolateyHelperItem.RequesterCannotModify", "IChocolateyPeer.TryReadIdentity", "IChocolateyPeer.get_HasExited",
    ];

    private static readonly string[] PortInterfaces = [Native + "IChocolateyHelperQualificationPort", Native + "IChocolateyPeer", Native + "IChocolateyHelperItem"];

    private const string Bootstrap = "PCSteward.Execution.ReviewedHelperBootstrap";
    private const string SelfUpdateBootstrap = "PCSteward.Execution.SelfUpdateHelperBootstrap";
    private const string EntryPoint = "PCSteward.Execution.ElevatedHelperEntryPoint";
    private const string Verifier = "PCSteward.Execution.WindowsPeerIdentityVerifier";
    private const string CapabilityCheck = "PCSteward.Execution.HelperDeploymentCapabilityCheck";
    private const string NativePort = Native + "NativeChocolateyHelperQualification";
    private const string NativePeer = Native + "NativeChocolateyPeer";
    private const string NativeItem = Native + "NativeChocolateyHelperItem";
    private const string ItemSeam = Native + "ChocolateyHelperItemTestSeam";

    /// <summary>
    /// N4b: the Execution callers outside the endpoint and the exact native members each may reference (the endpoint itself is
    /// bounded by <see cref="AllowList"/>). Disposing the peer is a call on <see cref="IDisposable"/>.
    /// </summary>
    private static readonly (string Caller, string[] Members)[] BootstrapAllowList =
    [
        (Bootstrap, [Native + "IChocolateyHelperQualificationPort.OpenPeer"]),
        (Verifier, [Native + "IChocolateyPeer.get_ProcessId", Native + "IChocolateyPeer.get_HasExited", Native + "IChocolateyPeer.TryReadIdentity"]),
        (EntryPoint, [NativePort + "..ctor", NativeProbePort + "..ctor"]),
        // Probe spec rev 4 §3 P0 (review M2): the probe bootstrap opens the peer only through the probe port.
        (ProbeBootstrap, [Native + "IChocolateyProbePort.OpenPeer"]),
        // ADR-032 revision 2 §6 step 1: the self-update session bootstrap applies the same peer checks and takes the requester
        // token only for the install check's protection principal (its closure is reported under the bootstrap type).
        (SelfUpdateBootstrap, [Native + "IChocolateyHelperQualificationPort.OpenPeer", Native + "IChocolateyHelperQualificationPort.BeginItem",
            Native + "IChocolateyPeer.TryReadIdentity", Native + "IChocolateyHelperItem.RequesterCannotModify"]),
    ];

    [TestMethod]
    public void TheOnlyCreateResponseCallSiteInSrcIsTheEndpoint()
    {
        List<string> assemblies = RequireBuilt();
        HashSet<string> callers = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Codec && reference.TargetMember is "CreateResponse" or "CreateClientResponse" or "CreateWinGetMachineResponse" or "CreateResponseCore")
                    callers.Add(reference.LogicalCaller + "->" + reference.TargetMember);
        // TryReadResponse re-encodes the frame it read to prove canonical form; the endpoint is the only producer of either
        // reply shape (ADR-031 design §8: the package entry and the client entry share one private core).
        CollectionAssert.AreEquivalent(new[]
        {
            Codec + ".TryReadResponse->CreateResponseCore", Codec + ".CreateResponse->CreateResponseCore",
            Codec + ".CreateClientResponse->CreateResponseCore", Endpoint + ".RunAsync->CreateResponse", Endpoint + ".RunAsync->CreateClientResponse",
            // D4b design §5.1: the machine WinGet route's reply (same 20 properties, its own version grammar).
            Codec + ".CreateWinGetMachineResponse->CreateResponseCore", Endpoint + ".RunAsync->CreateWinGetMachineResponse",
        }, callers.ToArray(), string.Join(Environment.NewLine, callers));

        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        foreach (string path in Sources(src))
        {
            string name = Path.GetFileName(path);
            if (name is "ReviewedUpdateWireCodec.cs" or "ReviewedHelperEndpoint.cs") continue;
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(path), @"ReviewedUpdateWireCodec\s*\.\s*Create(WinGetMachine)?Response\b"), $"{path} creates a protocol-2 response");
        }
    }

    [TestMethod]
    public void TheEndpointNeverReferencesProtocolOne()
    {
        List<string> assemblies = RequireBuilt();
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        string[] protocolOne =
        [
            "PCSteward.Execution.ElevatedHelperProtocol", "PCSteward.Execution.OneShotHelperRequestValidator", "PCSteward.Execution.HelperBootstrap",
            "PCSteward.Execution.ElevatedHelperResult",
        ];
        List<string> violations = [.. CompiledIlScanner.References(execution)
            .Where(reference => reference.Caller == Endpoint && protocolOne.Contains(reference.TargetType))
            .Select(reference => $"{reference.LogicalCaller} references {reference.TargetType}.{reference.TargetMember}")];
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        string source = EndpointSource();
        foreach (string token in new[] { "ElevatedHelperProtocol", "OneShotHelperRequestValidator", "HelperBootstrap", "ImmutableUpdateJob" })
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"the endpoint names {token}");
    }

    [TestMethod]
    public void ExecutionReferencesOnlyTheAllowListedNativeTypesAndOnlyFromTheEndpoint()
    {
        List<string> assemblies = RequireBuilt();
        List<string> violations = [];
        foreach (string path in assemblies)
        {
            string assembly = Path.GetFileNameWithoutExtension(path);
            if (assembly == "PCSteward.Provider.Chocolatey") continue;
            foreach (string type in CompiledIlScanner.TypeReferences(path).Where(type => type.StartsWith(Native, StringComparison.Ordinal)))
                if (assembly != "PCSteward.Execution" || !(IsAllowListed(type) || IsProbeAllowListed(type) || type == NativePort || type == NativeProbePort))
                    violations.Add($"{assembly} references type {type}");
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path).Where(reference => reference.TargetType.StartsWith(Native, StringComparison.Ordinal)))
                if (assembly != "PCSteward.Execution" || !IsPermitted(reference))
                    violations.Add($"{assembly}: {reference.Caller} references {reference.TargetType}.{reference.TargetMember}");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        // The scanner sees the endpoint's port calls (a self-check that the allow-list test is not vacuous).
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        string[] called = [.. CompiledIlScanner.References(execution).Where(reference => reference.Caller == Endpoint)
            .Select(reference => reference.TargetType + "." + reference.TargetMember)];
        foreach (string member in PortCalls)
            CollectionAssert.Contains(called, Native + member);
        CollectionAssert.DoesNotContain(called, Native + "IChocolateyHelperQualificationPort.OpenPeer", "OpenPeer belongs to the N4b bootstrap");

        // The composition root constructs the port only in RunReviewedAsync and the self-update session root (ADR-032 r2 §6).
        string[] constructing = [.. CompiledIlScanner.References(execution).Where(reference => reference.TargetType == NativePort)
            .Select(reference => reference.LogicalCaller + " -> " + reference.TargetMember).Distinct()];
        CollectionAssert.AreEquivalent(new[] { EntryPoint + ".RunReviewedAsync -> .ctor", EntryPoint + ".RunSelfUpdateAsync -> .ctor" }, constructing);

        // Source: exactly the endpoint and the protocol-2 bootstrap name the Native namespace.
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        string[] naming = [.. Sources(src).Where(path => File.ReadAllText(path).Contains("PCSteward.Provider.Chocolatey.Native", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        Assert.AreEqual("ReviewedHelperBootstrap.cs, ReviewedHelperEndpoint.cs, ReviewedHelperProbeBootstrap.cs, ReviewedHelperProbeEndpoint.cs, " +
            "SelfUpdateHelperBootstrap.cs", string.Join(", ", naming));
    }

    private static bool IsPermitted(CompiledIlScanner.IlReference reference)
    {
        if (reference.Caller == Endpoint) return IsAllowListed(reference.TargetType);
        // Probe spec rev 4 §7 H4: the probe entry reaches only the probe names and the shared peer's identity read.
        if (reference.Caller == ProbeEndpoint)
            return IsProbeAllowListed(reference.TargetType) || reference.TargetType + "." + reference.TargetMember == Native + "IChocolateyPeer.TryReadIdentity";
        string member = reference.TargetType + "." + reference.TargetMember;
        foreach ((string caller, string[] members) in BootstrapAllowList)
            if (reference.Caller == caller) return members.Contains(member);
        return false;
    }

    [TestMethod]
    public void TheStageAndUpgradeValuesAreClosedKindsAndThePortHasExactlyTheNativeImplementations()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        CollectionAssert.AreEquivalent(new[] { StageValues + "+Observed", StageValues + "+Refused", StageValues + "+TimedOut", StageValues + "+Cancelled", StageValues + "+OutOfOrder" },
            assemblies.SelectMany(path => DerivedFrom(path, StageValues)).ToArray(), "the stage values are a closed kind");
        CollectionAssert.AreEquivalent(new[] { UpgradeResult + "+Observed", UpgradeResult + "+OutOfOrder" },
            assemblies.SelectMany(path => DerivedFrom(path, UpgradeResult)).ToArray(), "the upgrade result is a closed record");
        foreach (string type in new[] { StageValues, UpgradeResult })
            foreach ((MethodAttributes attributes, int parameters) in Constructors(chocolatey, type))
                Assert.IsTrue((attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private ||
                    parameters == 1 && (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family, $"{type} has a non-private constructor");

        // §5.4 single-implementation rule: exactly one native implementation of each port interface, all in Native/.
        List<string> implementations = [];
        foreach (string path in assemblies)
            implementations.AddRange(Implementations(path, PortInterfaces).Select(type => $"{Path.GetFileName(path)}: {type}"));
        CollectionAssert.AreEquivalent(new[]
        {
            "PCSteward.Provider.Chocolatey.dll: " + NativePort, "PCSteward.Provider.Chocolatey.dll: " + NativePeer,
            "PCSteward.Provider.Chocolatey.dll: " + NativeItem,
        }, implementations, string.Join(Environment.NewLine, implementations));
        // The helper-side G1 seam has exactly the Helper-role check as its src implementation.
        List<string> checks = [.. assemblies.SelectMany(path => Implementations(path, ["PCSteward.Execution.IReviewedHelperCapabilityCheck"]))];
        CollectionAssert.AreEquivalent(new[] { CapabilityCheck }, checks);
        // The native peer and item have only private constructors; they are created only by their factories.
        foreach (string type in new[] { NativePeer, NativeItem })
            foreach ((MethodAttributes attributes, _) in Constructors(chocolatey, type))
                Assert.AreEqual(MethodAttributes.Private, attributes & MethodAttributes.MemberAccessMask, $"{type} has a non-private constructor");
    }

    [TestMethod]
    public void TheNativeItemAndPeerAreReachedOnlyThroughThePortAndTheItemTestSeamsHaveNoSrcCaller()
    {
        List<string> assemblies = RequireBuilt();
        string chocolatey = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Provider.Chocolatey");
        IReadOnlyList<(string Type, string Method)> defined = CompiledIlScanner.MethodDefinitions(chocolatey);
        (string Type, string Member)[] testOnly =
        [
            (NativeItem, "CreateForTests"), (NativeItem, "CreateOverFixtureForTests"), (NativeItem, "CreateClientOverFixtureForTests"),
            (ItemSeam, "CreateForTests"),
        ];
        foreach ((string type, string member) in testOnly)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the ban list");
        (string Type, string Member, string[] Callers)[] pinned =
        [
            (NativeItem, "Create", [NativePort + ".BeginItemWithVerdict"]),
            (NativePeer, "TryOpen", [NativePort + ".OpenPeer", NativeProbePort + ".OpenPeer"]),
            (NativePeer, "TryAcquireRequesterToken", [NativePort + ".BeginItemWithVerdict", NativeProbePort + ".BeginProbe"]),
            (NativePort, "BeginItemWithVerdict", [NativePort + ".BeginItem"]),
            (Native + "RequesterToken", "TryForVerifiedPeer", [NativePeer + ".TryAcquireRequesterToken"]),
            (Native + "PeerProcess", "TryOpen", [NativePeer + ".TryOpen"]),
            // N5 (design rev 6 §7): the Desktop-role producers observe only through their single production observation.
            (Native + "ChocolateyProfileObserver", "Observe", [NativeItem + ".ProductionObserve", Native + "ChocolateyDesktopObservation.ProductionObserve"]),
            // N6: the review's split-token probe (design rev 6 §(L6)) reads the same desktop token; it observes nothing.
            // ADR-031 design §7.1/§8.3: the client-mode Desktop observation (F′ and verification) reads the same token.
            (Native + "RequesterToken", "TryFromCurrentProcess",
                [Native + "ChocolateyDesktopObservation.ProductionObserve", Native + "ChocolateyDesktopObservation.ProductionObserveClient",
                    Native + "ChocolateyReviewedRouteComposition.ProbeSplitTokenState"]),
        ];
        foreach ((string type, string member, _) in pinned)
            Assert.IsTrue(defined.Contains((type, member)), $"{type}.{member} is not defined; update the pin list");
        List<string> violations = [];
        HashSet<string> seen = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (testOnly.Contains((reference.TargetType, reference.TargetMember)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls test-only {reference.TargetType}.{reference.TargetMember}");
                if (reference.TargetType == ItemSeam &&
                    !(reference.Caller.StartsWith(NativeItem, StringComparison.Ordinal) || reference.Caller.StartsWith(ItemSeam, StringComparison.Ordinal)))
                    violations.Add($"{Path.GetFileName(path)}: {reference.Caller} references the item test seam");
                foreach ((string type, string member, string[] callers) in pinned)
                {
                    if (reference.TargetType != type || reference.TargetMember != member) continue;
                    if (callers.Contains(reference.LogicalCaller)) seen.Add(type + "." + member);
                    else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls {type}.{member}");
                }
                // Only the native port's peer factory and the item's own factories construct the native implementations.
                if (reference.TargetMember == ".ctor" && reference.TargetType == NativeItem &&
                    reference.LogicalCaller is not (NativeItem + ".Create" or NativeItem + ".CreateForTests" or NativeItem + ".CreateOverFixtureForTests" or
                        NativeItem + ".CreateClientOverFixtureForTests"))
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the native item");
                if (reference.TargetMember == ".ctor" && reference.TargetType == NativePeer && reference.LogicalCaller != NativePeer + ".TryOpen")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the native peer");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // Every pinned production path is actually reached (the scan is not vacuous).
        CollectionAssert.AreEquivalent(pinned.Select(pin => pin.Type + "." + pin.Member).ToArray(), seen.ToArray());
    }

    [TestMethod]
    public void ProtocolTwoIsDispatchedToTheReviewedBootstrapWhichAloneRunsTheEndpoint()
    {
        List<string> assemblies = RequireBuilt();
        HashSet<string> endpointCallers = [], bootstrapCallers = [], checkConstructors = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (reference.TargetType == Endpoint && reference.TargetMember == "RunAsync" && reference.Caller != Endpoint)
                    endpointCallers.Add(reference.LogicalCaller);
                if (reference.TargetType == Bootstrap && reference.TargetMember == "RunAsync") bootstrapCallers.Add(reference.LogicalCaller);
                if (reference.TargetType == CapabilityCheck && reference.TargetMember == ".ctor" && reference.Caller != CapabilityCheck)
                    checkConstructors.Add(reference.LogicalCaller);
            }
        CollectionAssert.AreEquivalent(new[] { Bootstrap + ".RunAsync" }, endpointCallers.ToArray());
        CollectionAssert.AreEquivalent(new[] { EntryPoint + ".RunReviewedAsync" }, bootstrapCallers.ToArray());
        // Probe spec rev 4 §3 A3: the probe composition root constructs the same Helper-role check.
        // ADR-032 r2 §6 step 2: the self-update session root constructs it for the route-less release-install check.
        CollectionAssert.AreEquivalent(new[] { EntryPoint + ".RunReviewedAsync", EntryPoint + ".RunProbeAsync", EntryPoint + ".RunSelfUpdateAsync" },
            checkConstructors.ToArray());

        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        string transport = File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "WindowsElevatedHelperTransport.cs"));
        string bootstrap = File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "ReviewedHelperBootstrap.cs"));
        string protocol = File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "ElevatedHelperProtocol.cs"));
        // D4b stage 3 (design note §8): protocol 1 is retired. Every line that is not self-update, probe or protocol 2 is refused with
        // exit code 20 before anything is connected or read, and the protocol-1 request, validator and locator are gone.
        Assert.IsFalse(transport.Contains("ReviewedHelperEndpoint", StringComparison.Ordinal), "the shared transport file never names the endpoint");
        Assert.IsFalse(transport.Contains("PCSteward.Provider.Chocolatey.Native", StringComparison.Ordinal), "the shared transport file never names the native port");
        Assert.IsTrue(Regex.IsMatch(transport, @"SelectsProtocol2\(arguments\)\s*\?\s*RunReviewedAsync\(arguments\)\s*:\s*Task\.FromResult\(Protocol1RefusedExitCode\);"),
            "--protocol 2 is dispatched to the reviewed bootstrap and every other line is refused");
        StringAssert.Contains(transport, "internal const int Protocol1RefusedExitCode = 20;");
        foreach (string retired in new[] { "RunProtocol1Async", "OneShotHelperRequestValidator", "HelperBootstrap.TryParse", "WingetMachineUpdateExecutor",
            "WindowsElevatedHelperTransport", "ElevatedHelperProtocol.Version" })
            Assert.IsFalse(transport.Contains(retired, StringComparison.Ordinal), $"protocol 1 is retired ({retired})");
        Assert.IsFalse(protocol.Contains("Version", StringComparison.Ordinal), "no protocol-1 version remains");
        // Protocol 2 accepts only its own locator, and so does the shared pipe factory.
        StringAssert.Contains(bootstrap, "WindowsReviewedHelperTransport.PipeNamePattern().IsMatch(fields[\"--pipe\"])");
        StringAssert.Contains(transport, "internal static bool IsAcceptedLocator(string pipeName) =>\n        WindowsReviewedHelperTransport.PipeNamePattern().IsMatch(pipeName);");
        StringAssert.Contains(bootstrap, "TokenImpersonationLevel.Identification");
        foreach (string path in Sources(Path.Combine(src, "PCSteward.ElevatedHelper")))
            Assert.IsFalse(File.ReadAllText(path).Contains("ReviewedHelperEndpoint", StringComparison.Ordinal), path);
    }

    [TestMethod]
    public void ProductionG1AndHostConstructionArePinnedBySignature()
    {
        // N4b review L5: overloads are told apart by their compiled parameter types, not by name alone.
        List<string> assemblies = RequireBuilt();
        const string Reader = "PCSteward.Execution.OwnerOnlyDeploymentCapabilityReader";
        const string Host = "PCSteward.Execution.ReviewedHelperHost";
        const string LayoutFactory = "(System.Func`1<PCSteward.Execution.DeploymentCapabilityLayout>)";
        const string ReaderTwo = "(System.Func`1<PCSteward.Execution.DeploymentCapabilityLayout>, PCSteward.Execution.DeploymentLayoutProtectionCheck)";
        const string ReaderRole = "(PCSteward.Execution.DeploymentCapabilityRole, System.Func`1<PCSteward.Execution.DeploymentCapabilityLayout>, " +
            "PCSteward.Execution.DeploymentLayoutProtectionCheck)";
        // ADR-034 §4: the role constructor chains into the one that also takes the release pin check (a fixture seam).
        const string ReaderPin = "(PCSteward.Execution.DeploymentCapabilityRole, System.Func`1<PCSteward.Execution.DeploymentCapabilityLayout>, " +
            "PCSteward.Execution.DeploymentLayoutProtectionCheck, System.Func`3<System.String, System.String, System.Boolean>)";
        const string HostFacts = "(System.String, System.String, System.String, System.Int32)";
        // Each permitted edge: target type, member, target signature, logical caller, caller signature (null: any).
        (string Type, string Member, string Signature, string Caller, string? CallerSignature)[] permitted =
        [
            // Only the composition root constructs the G1 check, and only through the production (parameterless) constructor;
            // the fixture-layout overload is reached only by that constructor's chain.
            (CapabilityCheck, ".ctor", "()", EntryPoint + ".RunReviewedAsync", null),
            (CapabilityCheck, ".ctor", "()", EntryPoint + ".RunProbeAsync", null),
            (CapabilityCheck, ".ctor", "()", EntryPoint + ".RunSelfUpdateAsync", null),
            (CapabilityCheck, ".ctor", LayoutFactory, CapabilityCheck + "..ctor", "()"),
            // The fixed helper layout is bound only by the production constructor.
            (Reader, "ResolveHelperLayout", "()", CapabilityCheck + "..ctor", "()"),
            // The Helper-role reader (the role and protection-principal constructor) is constructed only by G1's shared
            // EvaluateFor (the route check and the self-update release-install check); the reader's other constructors only
            // chain into it and have no other src caller.
            (Reader, ".ctor", ReaderRole, CapabilityCheck + ".EvaluateFor", null),
            (Reader, ".ctor", ReaderRole, Reader + "..ctor", ReaderTwo),
            (Reader, ".ctor", ReaderTwo, Reader + "..ctor", "()"),
            (Reader, ".ctor", ReaderPin, Reader + "..ctor", ReaderRole),
            // Probe spec rev 4 §5.2 step 6 (S4): the desktop probe's production composition is the only src caller of the
            // Desktop-role production (parameterless) reader; the fixture seam constructors stay without a src caller.
            (Reader, ".ctor", "()", "PCSteward.Execution.ReviewedHelperProbeTransport.ProductionDependencies", "()"),
            // Chocolatey Confirm design §3 item 1: the client self-update's production composition is the only other src caller of
            // the Desktop-role production reader (one per review session, shared by the dispatcher and the protocol-2 transport).
            (Reader, ".ctor", "()", "PCSteward.Execution.ChocolateyClientUpdateComposition.CreateSession", "()"),
            // Chocolatey package Confirm addendum §5: and the package route's production composition, in the same shape.
            (Reader, ".ctor", "()", "PCSteward.Execution.ChocolateyPackageUpdateComposition.CreateSession", "()"),
            // D4b stage 2c: and the whole-PC WinGet route's production composition, shared by its exchange and protocol-2 transport.
            (Reader, ".ctor", "()", "PCSteward.Execution.WingetMachineUpdateComposition.CreateElevatedRunner", "()"),
            // ADR-032 revision 2 §6 and §8: the desktop's self-update launch (production constructor) and the reopened desktop's
            // check of its own install each construct the Desktop-role production reader.
            (Reader, ".ctor", "()", "PCSteward.Execution.SelfUpdateDesktopTransport..ctor", "()"),
            (Reader, ".ctor", "()", "PCSteward.Execution.SelfUpdateDesktopFiles.Evaluate", null),
            // The host facts are constructed only from the running process; the record's copy constructor only by its clone.
            (Host, ".ctor", HostFacts, Host + ".TryResolve", null),
            (Host, ".ctor", "(" + Host + ")", Host + ".Clone", "()"),
        ];
        string[] watched = [CapabilityCheck + "..ctor", Reader + "..ctor", Reader + ".ResolveHelperLayout", Host + "..ctor", Host + ".<Clone>$"];
        List<string> violations = [];
        HashSet<int> seen = [];
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                if (!watched.Contains(reference.TargetType + "." + reference.TargetMember)) continue;
                int index = Array.FindIndex(permitted, edge => edge.Type == reference.TargetType && edge.Member == reference.TargetMember &&
                    edge.Signature == reference.TargetSignature && edge.Caller == reference.LogicalCaller &&
                    (edge.CallerSignature is null || edge.CallerSignature == reference.CallerSignature));
                if (index >= 0) seen.Add(index);
                else violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller}{reference.CallerSignature} -> " +
                    $"{reference.TargetType}.{reference.TargetMember}{reference.TargetSignature}");
            }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
        // Every pinned edge exists (the signatures above are the compiled ones, so the scan is not vacuous).
        string[] missing = [.. Enumerable.Range(0, permitted.Length).Where(index => !seen.Contains(index))
            .Select(index => $"{permitted[index].Caller} -> {permitted[index].Type}.{permitted[index].Member}{permitted[index].Signature}")];
        Assert.AreEqual(0, missing.Length, string.Join(Environment.NewLine, missing));

        // The role argument at the one Evaluate construction site is the Helper role (source; the IL constant is not scanned).
        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution");
        string check = File.ReadAllText(Path.Combine(src, "HelperDeploymentCapabilityCheck.cs"));
        Assert.AreEqual(1, Regex.Count(check, @"OwnerOnlyDeploymentCapabilityReader reader = new\(DeploymentCapabilityRole\.Helper, \(\) => layout, requesterCannotModify\);"));
        string[] naming = [.. Sources(src).Where(path => File.ReadAllText(path).Contains("DeploymentCapabilityRole.Helper", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        Assert.AreEqual("HelperDeploymentCapabilityCheck.cs, OwnerOnlyDeploymentCapabilityReader.cs", string.Join(", ", naming));
    }

    [TestMethod]
    public void ProtocolTwoPeerTrustUsesTheSharedCacheOnlyAuthenticodePolicy()
    {
        // N4b review M2: the handle-based VerifyPeer reaches WinVerifyTrust only through AuthenticodeIdentity, whose policy has
        // no revocation check and cache-only URL retrieval, so peer verification does no network I/O.
        List<string> assemblies = RequireBuilt();
        string execution = assemblies.Single(path => Path.GetFileNameWithoutExtension(path) == "PCSteward.Execution");
        string[] called = [.. CompiledIlScanner.References(execution)
            .Where(reference => reference.LogicalCaller == Verifier + ".VerifyPeer" && reference.CallerSignature.StartsWith("(" + Native + "IChocolateyPeer,", StringComparison.Ordinal))
            .Select(reference => reference.TargetType + "." + reference.TargetMember)];
        CollectionAssert.Contains(called, "PCSteward.Execution.AuthenticodeIdentity.IsTrustedAndSameSigner");
        string[] trustCallers = [.. CompiledIlScanner.References(execution)
            .Where(reference => reference.TargetType == "PCSteward.Execution.NativeMethods" && reference.TargetMember == "WinVerifyTrust")
            .Select(reference => reference.LogicalCaller).Distinct()];
        Assert.AreEqual("PCSteward.Execution.AuthenticodeIdentity.VerifyTrust", string.Join(", ", trustCallers));

        string transport = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "WindowsElevatedHelperTransport.cs"));
        StringAssert.Contains(transport, "RevocationChecks = NativeMethods.WtdRevokeNone,");
        StringAssert.Contains(transport, "ProviderFlags = NativeMethods.WtdCacheOnlyUrlRetrieval");
        StringAssert.Contains(transport, "internal const uint WtdRevokeNone = 0;");
        StringAssert.Contains(transport, "internal const uint WtdCacheOnlyUrlRetrieval = 0x00001000;");
    }

    [TestMethod]
    public void ProtocolOneIsRetiredAndNoHelperPathReachesAProviderExecutorOutsideTheEndpoint()
    {
        // D4b stage 3 (design note §8): the protocol-1 types are gone, and nothing in src calls a protected machine-update executor
        // (the protocol-1 provider seam); the helper reaches a provider only through the protocol-2 endpoint's own routed work.
        List<string> assemblies = RequireBuilt();
        string[] retired =
        [
            "PCSteward.Execution.HelperBootstrap", "PCSteward.Execution.OneShotHelperRequestValidator", "PCSteward.Execution.WindowsElevatedHelperTransport",
            "PCSteward.Execution.ElevatedHelperResult", "PCSteward.Execution.ElevatedHelperOutcome", "PCSteward.Execution.IElevatedHelperTransport",
            "PCSteward.Execution.ElevatedHelperLauncher", "PCSteward.Provider.WinGet.WingetMachineUpdateExecutor",
        ];
        List<string> violations = [];
        int scanned = 0;
        foreach (string path in assemblies)
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
            {
                scanned++;
                if (retired.Any(type => reference.TargetType == type || reference.Caller == type || reference.Caller.StartsWith(type + "+", StringComparison.Ordinal)))
                    violations.Add($"{reference.LogicalCaller} -> {reference.TargetType}.{reference.TargetMember}");
                if (reference.TargetMember == "ExecuteProtectedAsync" || reference.TargetMember.EndsWith(".ExecuteProtectedAsync", StringComparison.Ordinal))
                    violations.Add($"{reference.LogicalCaller} calls {reference.TargetType}.{reference.TargetMember}");
            }
        Assert.IsTrue(scanned > 1000, $"the scan is not vacuous ({scanned})");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        string src = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src");
        foreach (string path in Sources(src))
        {
            string source = File.ReadAllText(path);
            foreach (string name in new[] { "class WindowsElevatedHelperTransport", "class OneShotHelperRequestValidator", "record HelperBootstrap(",
                "class WingetMachineUpdateExecutor", "interface IElevatedHelperTransport", "RunProtocol1Async", "RunNoOpEvidenceAsync" })
                Assert.IsFalse(source.Contains(name, StringComparison.Ordinal), $"{path} still has {name}");
        }
    }

    [TestMethod]
    public void TheEndpointReadsNoAmbientStateAndUpgradesAtOneCallSite()
    {
        string source = EndpointSource();
        foreach (string token in new[]
        {
            "Sqlite", "Registry", "GetEnvironmentVariable", "ExpandEnvironmentVariables", "Environment.", "File.", "Directory.",
            "System.Diagnostics", "ProcessStartInfo", "Process.", "HttpClient", "DateTimeOffset.UtcNow", "DateTime.Now", "DateTime.UtcNow", "Stopwatch", "Success",
        })
            Assert.IsFalse(source.Contains(token, StringComparison.Ordinal), $"the endpoint uses {token}");
        Assert.AreEqual(1, Regex.Count(source, @"\.UpgradeAsync\("), "exactly one upgrade call site");
        Assert.AreEqual(1, Regex.Count(source, @"PipeMessageFraming\.WriteAsync\("), "exactly one response write");
        Assert.AreEqual(1, Regex.Count(source, @"ReviewedUpdateWireCodec\.CreateResponse\("), "the response is built only through the codec");
        Assert.AreEqual(1, Regex.Count(source, @"ReviewedUpdateWireCodec\.CreateWinGetMachineResponse\("), "one machine WinGet reply site (D4b §5.1)");
    }

    private static bool IsProbeAllowListed(string fullName) =>
        fullName.StartsWith(Native, StringComparison.Ordinal) && ProbeAllowList.Contains(fullName[Native.Length..].Split('+')[0]);

    private static bool IsAllowListed(string fullName) =>
        fullName.StartsWith(Native, StringComparison.Ordinal) && AllowList.Contains(fullName[Native.Length..].Split('+')[0]);

    private static List<string> RequireBuilt()
    {
        List<string> assemblies = CompiledIlScanner.BuiltSrcAssemblies();
        foreach (string required in new[] { "PCSteward.Provider.Chocolatey", "PCSteward.Execution", "PCSteward.ElevatedHelper" })
            Assert.IsTrue(assemblies.Any(path => Path.GetFileNameWithoutExtension(path) == required), $"{required} was not built");
        return assemblies;
    }

    private static string EndpointSource()
    {
        string path = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Execution", "ReviewedHelperEndpoint.cs");
        // Code only: documentation may name what the endpoint never does.
        return string.Join('\n', File.ReadAllText(path).Split('\n').Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal) &&
            !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }

    private static IEnumerable<string> Sources(string root) => Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

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
            if (TypeName(md, md.GetTypeDefinition(handle).BaseType) == baseFullName) derived.Add(CompiledIlScanner.FullName(md, handle));
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
                if (interfaces.Contains(TypeName(md, md.GetInterfaceImplementation(implementation).Interface)))
                    found.Add(CompiledIlScanner.FullName(md, handle));
        return found;
    }

    private static string? TypeName(MetadataReader md, EntityHandle handle)
    {
        if (handle.IsNil) return null;
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => CompiledIlScanner.FullName(md, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => ReferenceName(md, (TypeReferenceHandle)handle),
            _ => null,
        };
    }

    private static string ReferenceName(MetadataReader md, TypeReferenceHandle handle)
    {
        TypeReference reference = md.GetTypeReference(handle);
        string name = md.GetString(reference.Name);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference) return ReferenceName(md, (TypeReferenceHandle)reference.ResolutionScope) + "+" + name;
        string ns = md.GetString(reference.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
