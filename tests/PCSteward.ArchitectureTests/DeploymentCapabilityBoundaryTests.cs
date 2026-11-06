using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 adapter gates Part A §A.2 placement: the capability policy is pure Execution code the
/// helper can reuse, Application/Persistence/ScheduledHost never see a capability type, every <c>Supports</c> call in the
/// workflow is exception-wrapped, and no fresh-evidence producer copies the frozen operation's EvidenceId (§A.6.1).
/// </summary>
[TestClass]
public sealed class DeploymentCapabilityBoundaryTests
{
    private static readonly string[] CapabilityTypes =
        ["IHelperProtocolCapability", "HelperProtocolCapabilityStatus", "HelperProtocolCapabilityResult", "DeploymentCapabilityPolicy",
         "DeploymentCapabilityInputs", "DeploymentObservedFile", "DeploymentLedger", "HeldHelperBinding", "OwnerOnlyDeploymentCapabilityReader",
         "DeploymentCapabilityLayout", "DeploymentLayoutProtectionCheck", "DeploymentCapabilityStage", "DeploymentFileAccess", "WindowsFileIdentity"];
    private static readonly string[] BindingReferencingFiles =
        ["HeldHelperBinding.cs", "IHelperProtocolCapability.cs", "OwnerOnlyDeploymentCapabilityReader.cs", "ReviewedHelperProbeTransport.cs",
         "ReviewedHelperProtocol2Transport.cs"];
    private static readonly string[] ReaderSources =
        ["OwnerOnlyDeploymentCapabilityReader.cs", "HeldHelperBinding.cs", "DeploymentFileAccess.cs"];

    [TestMethod]
    public void CapabilityTypesLiveOnlyInExecution()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        foreach (string path in Sources(src))
        {
            string project = Path.GetRelativePath(src, path).Split(Path.DirectorySeparatorChar)[0];
            if (project == "PCSteward.Execution") continue;
            string source = File.ReadAllText(path);
            foreach (string type in CapabilityTypes)
                Assert.IsFalse(Regex.IsMatch(source, $@"\b{type}\b"), $"{path} references {type}");
        }
        // Implementations of the seam exist only in Execution: exactly one, the Windows reader.
        string[] implementations = [.. Sources(src).Where(path =>
            Regex.IsMatch(File.ReadAllText(path), @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIHelperProtocolCapability\b"))];
        Assert.AreEqual(1, implementations.Length, string.Join(", ", implementations));
        Assert.AreEqual(Path.Combine(src, "PCSteward.Execution", "OwnerOnlyDeploymentCapabilityReader.cs"), implementations[0]);
    }

    [TestMethod]
    public void WindowsReaderReadsOnlyFixedProtectedInputsAndCachesNothing()
    {
        // §A.2/§A.5: fixed paths from SpecialFolder.ProgramFiles and Environment.ProcessPath only; no database, environment
        // variable, registry, network or current-directory input; no cache; no command execution.
        string execution = Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Execution");
        foreach (string file in ReaderSources)
        {
            string source = File.ReadAllText(Path.Combine(execution, file));
            foreach (string forbidden in new[]
            {
                "Sqlite", "Registry", "GetEnvironmentVariable", "ExpandEnvironmentVariables", "HttpClient", "CurrentDirectory",
                "GetCurrentDirectory", "Process.Start", "ProcessStartInfo", "MemoryCache", "Lazy<", "ConcurrentDictionary", "FileShare.ReadWrite",
                "FileShare.Delete", "FileShare.Write", "FileAccess.Write", "FileAccess.ReadWrite", "FileMode.Create", "FileMode.OpenOrCreate",
            })
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"{file} contains {forbidden}");
            foreach (Match member in Regex.Matches(source, @"\bEnvironment\.\w+"))
                Assert.IsTrue(member.Value is "Environment.ProcessPath" or "Environment.GetFolderPath" or "Environment.SpecialFolder", $"{file} uses {member.Value}");
            foreach (Match folder in Regex.Matches(source, @"SpecialFolder\.\w+"))
                Assert.AreEqual("SpecialFolder.ProgramFiles", folder.Value, file);
            // No static state other than constants and the immutable empty observation map.
            foreach (Match field in Regex.Matches(source, @"^\s*(private|internal)\s+static\s+(?!readonly\s+IReadOnlyDictionary<string, DeploymentObservedFile> NoObservations)(?!class\b)[^(=;]*[=;]", RegexOptions.Multiline))
                Assert.Fail($"{file} declares static state: {field.Value.Trim()}");
        }
        StringAssert.Contains(File.ReadAllText(Path.Combine(execution, "DeploymentFileAccess.cs")),
            "File.OpenHandle(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None)");
    }

    [TestMethod]
    public void ProtocolOneTransportIsNotWiredToTheCapabilitySeamYet()
    {
        // The protocol-2 transport is a separate type; the protocol-1 transport stays free of the seam, and no file
        // constructs the reader (no production update caller until route admission). Qualification probe spec rev 4 §5.2
        // step 6 (S4): the desktop probe transport's production composition is the one file that constructs the Desktop-role
        // reader, exactly once and only through its parameterless (production) constructor.
        string src = Path.Combine(FindRepositoryRoot(), "src");
        string transport = File.ReadAllText(Path.Combine(src, "PCSteward.Execution", "WindowsElevatedHelperTransport.cs"));
        foreach (string type in new[] { "IHelperProtocolCapability", "HeldHelperBinding", "OwnerOnlyDeploymentCapabilityReader", "DeploymentFileAccess", "TryHold" })
            Assert.IsFalse(transport.Contains(type, StringComparison.Ordinal), $"protocol-1 transport references {type}");
        foreach (string path in Sources(src))
        {
            if (path.EndsWith("OwnerOnlyDeploymentCapabilityReader.cs", StringComparison.Ordinal)) continue;
            int constructions = Regex.Count(File.ReadAllText(path), @"new\s+OwnerOnlyDeploymentCapabilityReader\s*\(|OwnerOnlyDeploymentCapabilityReader\s+\w+\s*=\s*new\s*\(");
            if (path.EndsWith(Path.Combine("PCSteward.Execution", "ReviewedHelperProbeTransport.cs"), StringComparison.Ordinal))
            {
                Assert.AreEqual(1, constructions, "the probe composition constructs the Desktop-role reader once");
                Assert.AreEqual(1, Regex.Count(File.ReadAllText(path), @"new\s+OwnerOnlyDeploymentCapabilityReader\(\)"), "only the parameterless reader");
            }
            // Chocolatey Confirm design §3 item 1: the client self-update's composition constructs the parameterless reader once.
            else if (path.EndsWith(Path.Combine("PCSteward.Execution", "ChocolateyClientUpdateComposition.cs"), StringComparison.Ordinal))
                Assert.AreEqual(1, constructions, "the client composition constructs the Desktop-role reader once");
            else if (path.EndsWith(Path.Combine("PCSteward.Execution", "HelperDeploymentCapabilityCheck.cs"), StringComparison.Ordinal)) { }
            else Assert.AreEqual(0, constructions, $"{path} constructs the reader (no other production update caller)");
        }
    }

    [TestMethod]
    public void HeldBindingIsCreatedOnlyByTheReaderAndReleasedOnlyAtTheOwnershipPoint()
    {
        // Review-16 L2: TryCreate is called only by the reader; outside the reader's own refusal paths, the binding is
        // disposed only when no helper process exists or by the continuation of the ownership task (exit observed and
        // transport sealed); the transport keeps a strong reference until after seal.
        string src = Path.Combine(FindRepositoryRoot(), "src");
        string execution = Path.Combine(src, "PCSteward.Execution");
        string[] referencing = [.. Sources(src).Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bHeldHelperBinding\b"))
            .Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)];
        CollectionAssert.AreEqual(BindingReferencingFiles, referencing);
        foreach (string path in Sources(src))
            if (Path.GetFileName(path) is not ("OwnerOnlyDeploymentCapabilityReader.cs" or "HeldHelperBinding.cs"))
                Assert.IsFalse(File.ReadAllText(path).Contains("HeldHelperBinding.TryCreate", StringComparison.Ordinal), $"{path} creates a binding");
        Assert.AreEqual(1, Regex.Count(File.ReadAllText(Path.Combine(execution, "OwnerOnlyDeploymentCapabilityReader.cs")), @"HeldHelperBinding\.TryCreate\("));

        string transport = File.ReadAllText(Path.Combine(execution, "ReviewedHelperProtocol2Transport.cs"));
        Assert.AreEqual(2, Regex.Count(transport, @"\.Dispose\(\)"), "the only two release points");
        Assert.IsTrue(Regex.IsMatch(transport, @"private static void ReleaseWithoutHelper\(HeldHelperBinding binding\) => binding\.Dispose\(\);"));
        Assert.IsTrue(Regex.IsMatch(transport,
            @"private static void ReleaseAfterOwnership\(Task ownedCompletion, HeldHelperBinding binding\) =>\s+_ = ownedCompletion\.ContinueWith\(static \(_, state\) => \(\(HeldHelperBinding\)state!\)\.Dispose\(\)"));
        Assert.AreEqual(1, Regex.Count(transport, @"ReleaseAfterOwnership\(owned\.OwnedCompletion, binding\);"));
        // Release-without-helper only before ownership exists; no other release route (review-17 L6).
        int ownership = transport.IndexOf("owned = new(helper);", StringComparison.Ordinal);
        Assert.IsTrue(ownership > 0);
        foreach (Match call in Regex.Matches(transport, @"ReleaseWithoutHelper\(binding\);"))
            Assert.IsTrue(call.Index < ownership, "ReleaseWithoutHelper is called only before the helper is owned");
        Assert.IsFalse(transport.Contains("ReleaseHandles", StringComparison.Ordinal), "the A2-L5 fallback is not used");
        Assert.IsFalse(Regex.IsMatch(transport, @"using\s*\(?\s*(HeldHelperBinding|binding\b)"), "no using-scoped binding");
        int release = transport.IndexOf("ReleaseAfterOwnership(owned.OwnedCompletion, binding);", StringComparison.Ordinal);
        int run = transport.IndexOf("await ReviewedHelperExchangeSession.RunAsync(", StringComparison.Ordinal);
        int keepAlive = transport.IndexOf("GC.KeepAlive(binding);", StringComparison.Ordinal);
        Assert.IsTrue(release > 0 && run > release && keepAlive > run, "release is registered before the exchange; the strong reference outlives it");
        // The ownership task completes only after HelperProcessWork.SealTransport and an observed exit.
        StringAssert.Contains(File.ReadAllText(Path.Combine(execution, "HelperProcessWork.cs")), "await Task.WhenAll(exit, finished)");
        Assert.IsTrue(Regex.IsMatch(File.ReadAllText(Path.Combine(execution, "ReviewedHelperProtocol2Transport.cs")),
            @"finally\s*\{\s*owned\.SealTransport\(\);\s*\}"), "the session seals the transport in a finally block");

        // Qualification probe spec rev 4 §5.2 steps 6–10 (S4): the probe transport keeps the same A-M2 lifetime for every
        // held binding: release without a helper only before ownership exists, otherwise only after exit and seal.
        string probe = File.ReadAllText(Path.Combine(execution, "ReviewedHelperProbeTransport.cs"));
        Assert.AreEqual(2, Regex.Count(probe, @"binding\.Dispose\(\)"), "the probe's only two release points");
        Assert.IsTrue(Regex.IsMatch(probe,
            @"private static void ReleaseWithoutHelper\(List<HeldHelperBinding> bindings\)\s*\{\s*foreach \(HeldHelperBinding binding in bindings\) binding\.Dispose\(\);\s*\}"));
        Assert.IsTrue(Regex.IsMatch(probe,
            @"private static void ReleaseAfterOwnership\(Task ownedCompletion, List<HeldHelperBinding> bindings\) =>\s+_ = ownedCompletion\.ContinueWith\(static \(_, state\) =>"));
        Assert.AreEqual(1, Regex.Count(probe, @"ReleaseAfterOwnership\(owned\.OwnedCompletion, bindings\);"));
        int probeOwnership = probe.IndexOf("owned = new(helper);", StringComparison.Ordinal);
        Assert.IsTrue(probeOwnership > 0);
        Assert.AreEqual(4, Regex.Count(probe, @"ReleaseWithoutHelper\(bindings\);"), "capability, final trust, cancellation and no helper");
        foreach (Match call in Regex.Matches(probe, @"ReleaseWithoutHelper\(bindings\);"))
            Assert.IsTrue(call.Index < probeOwnership, "ReleaseWithoutHelper is called only before the helper is owned");
        Assert.IsFalse(probe.Contains("ReleaseHandles", StringComparison.Ordinal), "the A2-L5 fallback is not used");
        int probeRelease = probe.IndexOf("ReleaseAfterOwnership(owned.OwnedCompletion, bindings);", StringComparison.Ordinal);
        int probeRun = probe.IndexOf("await ReviewedHelperProbeSession.RunAsync(", StringComparison.Ordinal);
        int probeKeepAlive = probe.IndexOf("GC.KeepAlive(bindings);", StringComparison.Ordinal);
        Assert.IsTrue(probeRelease > 0 && probeRun > probeRelease && probeKeepAlive > probeRun, "release registered before the session; reference outlives it");
        Assert.IsTrue(Regex.IsMatch(probe, @"finally\s*\{\s*owned\.SealTransport\(\);\s*\}"), "the probe session seals the transport in a finally block");
    }

    /// <summary>
    /// Chocolatey Confirm design §3 item 1 (changed deliberately): the protocol-2 transport and the route dispatcher are composed
    /// only in <c>ChocolateyClientUpdateComposition.cs</c>, exactly once each, over one production (parameterless) Desktop-role
    /// capability reader shared by both. No other source composes them, in either the explicit or the target-typed form.
    /// </summary>
    [TestMethod]
    public void ProtocolTwoTransportAndDispatcherAreNotComposedAndNeverFallBackToProtocolOne()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        string execution = Path.Combine(src, "PCSteward.Execution");
        foreach (string path in Sources(src))
        {
            string source = File.ReadAllText(path);
            if (path.EndsWith(Path.Combine("PCSteward.Execution", "ChocolateyClientUpdateComposition.cs"), StringComparison.Ordinal))
            {
                Assert.AreEqual(1, Regex.Count(source, @"WindowsReviewedHelperTransport transport = new\(capability\);"), "one protocol-2 transport over the shared reader");
                Assert.AreEqual(1, Regex.Count(source, @"new ChocolateyRuntimeLimitsRouteDispatcher\(capability, transport\)"), "one route dispatcher over the same reader");
                Assert.AreEqual(1, Regex.Count(source, @"OwnerOnlyDeploymentCapabilityReader capability = new\(\);"), "one parameterless reader");
                continue;
            }
            Assert.IsFalse(Regex.IsMatch(source, @"new\s+WindowsReviewedHelperTransport\s*\(|WindowsReviewedHelperTransport\s+\w+\s*=\s*new\s*\("),
                $"{path} composes the protocol-2 transport (only the client composition may)");
            Assert.IsFalse(Regex.IsMatch(source, @"new\s+ChocolateyRuntimeLimitsRouteDispatcher\s*\(|ChocolateyRuntimeLimitsRouteDispatcher\s+\w+\s*=\s*new\s*\("),
                $"{path} composes the route dispatcher (only the client composition may)");
        }
        string transport = File.ReadAllText(Path.Combine(execution, "ReviewedHelperProtocol2Transport.cs"));
        string dispatcher = File.ReadAllText(Path.Combine(execution, "ChocolateyRuntimeLimitsRouteDispatcher.cs"));
        StringAssert.Contains(transport, "\"--protocol\", ReviewedUpdateWireCodec.ProtocolVersion,");
        foreach (string source in new[] { transport, dispatcher })
        {
            foreach (string forbidden in new[]
            {
                "ElevatedHelperProtocol.Version", "ElevatedHelperProtocol.CreateRequest", "ElevatedHelperProtocol.TryReadResponse", "ImmutableUpdateJob",
                "Sqlite", "Registry", "GetEnvironmentVariable", "ExpandEnvironmentVariables", "HttpClient", "CurrentDirectory", "MemoryCache", "Lazy<",
            })
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"protocol-2 adapter contains {forbidden}");
            foreach (Match member in Regex.Matches(source, @"\bEnvironment\.\w+"))
                Assert.AreEqual("Environment.ProcessId", member.Value);
        }
        // The dispatcher performs no I/O of its own: only the capability seam and the transport.
        foreach (string forbidden in new[] { "System.IO", "File.", "Directory.", "Process", "NamedPipe", "SafeFileHandle", "HeldHelperBinding" })
            Assert.IsFalse(dispatcher.Contains(forbidden, StringComparison.Ordinal), $"dispatcher contains {forbidden}");
        // Supports checks route shape and EvidenceId before the seam (A.4 step 1: no file I/O first). Task 50 activation
        // (ADR-031 owner decision 7): the shape check is the route-discriminated form (package arm or exact client arm).
        int digest = dispatcher.IndexOf("TryGetRouteProfileDigest(operation, out _, out _) &&", StringComparison.Ordinal);
        int evaluate = dispatcher.IndexOf("capability.Evaluate(operation.RouteContract)", StringComparison.Ordinal);
        Assert.IsTrue(digest > 0 && evaluate > digest);
    }

    [TestMethod]
    public void PolicyAndSeamSourcesArePureAndReusableByTheHelper()
    {
        string root = FindRepositoryRoot();
        string execution = Path.Combine(root, "src", "PCSteward.Execution");
        foreach (string file in new[] { "DeploymentCapabilityPolicy.cs", "IHelperProtocolCapability.cs" })
        {
            string source = File.ReadAllText(Path.Combine(execution, file));
            foreach (string forbidden in new[]
            {
                "Sqlite", "Microsoft.Data.Sqlite", "Registry", "Environment.", "HttpClient", "System.IO", "File.", "Directory.", "FileStream",
                "DllImport", "LibraryImport", "Microsoft.Win32", "Process", "SafeFileHandle", "SpecialFolder",
            })
                Assert.IsFalse(source.Contains(forbidden, StringComparison.Ordinal), $"{file} contains {forbidden}");
        }
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "src", "PCSteward.ElevatedHelper", "PCSteward.ElevatedHelper.csproj")),
            "../PCSteward.Execution/PCSteward.Execution.csproj");
    }

    [TestMethod]
    public void EverySupportsCallInTheWorkflowIsExceptionWrapped()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Application", "ReviewedUpdateDispatchWorkflow.cs"));
        Assert.AreEqual(1, Regex.Count(source, @"dispatcher\.Supports\("));
        StringAssert.Contains(source, "try { return dispatcher.Supports(operation); }");
        Assert.AreEqual(2, Regex.Count(source, @"!StillSupported\(operation\)"));
    }

    [TestMethod]
    public void TheRouteConfinedProfileRuleIsAConjunctOfTheSharedMatches()
    {
        // A source pin of the conjunct; since the Task 50 activation it is also observable through Matches (AdapterGatesPartATests).
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Application", "UpdateDispatchAdmission.cs"));
        int matches = source.IndexOf("public bool Matches(ReviewedUpdateOperation operation, DateTimeOffset now) =>", StringComparison.Ordinal);
        Assert.IsTrue(matches >= 0);
        string body = source[matches..source.IndexOf(';', matches)];
        StringAssert.Contains(body, "ProfileEvidenceMatches(operation) &&");
    }

    [TestMethod]
    public void FreshEvidenceProducersNeverReadTheFrozenEvidenceId()
    {
        // N5 added the Chocolatey Profile V2 producer; the guard covers it and any later producer.
        Assert.IsTrue(Regex.IsMatch("internal sealed class P : IReviewedUpdateDispatchEvidenceSource",
            @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIReviewedUpdateDispatchEvidenceSource\b"));
        string src = Path.Combine(FindRepositoryRoot(), "src");
        List<string> producers = [];
        foreach (string path in Sources(src))
        {
            string source = File.ReadAllText(path);
            if (!Regex.IsMatch(source, @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIReviewedUpdateDispatchEvidenceSource\b")) continue;
            producers.Add(Path.GetFileName(path));
            Assert.IsFalse(Regex.IsMatch(source, @"\.\s*EvidenceId\b"), $"{path} reads an EvidenceId; F′ must come from fresh observation");
        }
        CollectionAssert.Contains(producers, "ChocolateyProfileEvidenceSource.cs", "the scan is not vacuous");
    }

    private static IEnumerable<string> Sources(string src) => Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
