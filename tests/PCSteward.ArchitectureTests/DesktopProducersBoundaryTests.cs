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
/// (<c>ChocolateyReviewedRouteComposition</c>) constructs them and their ONE shared baseline store, and nothing in
/// <c>src</c> constructs that composition or the dispatch workflow, and <c>HasSupportedMode</c> admits only WinGet (the
/// route stays non-admitted);</item>
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
    /// and their shared baselines, and nothing in <c>src</c> constructs it, so the route stays non-admitted.
    /// </summary>
    private const string Composition = Native + "ChocolateyReviewedRouteComposition";

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
                if (reference.TargetType == Composition && reference.Caller != Composition)
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the composition (no src caller may compose the route yet)");
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

        // The test-only factory has no src caller.
        List<string> violations = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetType == Composition && reference.TargetMember == "CreateForTests")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} calls the composition's test-only factory");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// The CRITICAL scope limit of N6: the runtime-limit Chocolatey route stays non-admitted. The review gate admits only
    /// <c>WinGetUserExactV3</c>, and no <c>src</c> type implements a route dispatcher for any Chocolatey route.
    /// </summary>
    [TestMethod]
    public void TheRuntimeLimitRouteStaysNonAdmitted()
    {
        string review = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Application", "ReviewedUpdateReview.cs"));
        Match gate = Regex.Match(review, @"internal static bool HasSupportedMode\(ReviewedUpdateOperation operation\) =>(?<body>[^;]*);");
        Assert.IsTrue(gate.Success, "HasSupportedMode not found");
        string body = gate.Groups["body"].Value;
        StringAssert.Contains(body, "operation.RouteContract == UpdateRouteContract.WinGetUserExactV3");
        Assert.IsFalse(body.Contains("Chocolatey", StringComparison.Ordinal), "HasSupportedMode must not admit a Chocolatey route");
        Assert.AreEqual(1, Regex.Count(body, @"RouteContract\s*=="), "HasSupportedMode compares the route with exactly one contract");

        // No src code composes the reviewed dispatch workflow, so no UI path can reach any route dispatcher.
        List<string> violations = [];
        foreach (string path in RequireBuilt())
            foreach (CompiledIlScanner.IlReference reference in CompiledIlScanner.References(path))
                if (reference.TargetMember == ".ctor" && reference.TargetType == "PCSteward.Application.ReviewedUpdateDispatchWorkflow")
                    violations.Add($"{Path.GetFileName(path)}: {reference.LogicalCaller} constructs the reviewed dispatch workflow");
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));
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
