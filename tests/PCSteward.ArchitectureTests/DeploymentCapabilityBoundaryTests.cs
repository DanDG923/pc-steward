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
         "DeploymentCapabilityInputs", "DeploymentObservedFile", "DeploymentLedger", "HeldHelperBinding", "OwnerOnlyDeploymentCapabilityReader"];

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
        // Implementations of the seam exist only in Execution (none yet in this slice).
        foreach (string path in Sources(src).Where(path => !path.Contains($"{Path.DirectorySeparatorChar}PCSteward.Execution{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            Assert.IsFalse(Regex.IsMatch(File.ReadAllText(path), @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIHelperProtocolCapability\b"), path);
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
        // The runtime-limit route is not admitted yet, so behaviour tests cannot observe this conjunct through Matches alone.
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Application", "UpdateDispatchAdmission.cs"));
        int matches = source.IndexOf("public bool Matches(ReviewedUpdateOperation operation, DateTimeOffset now) =>", StringComparison.Ordinal);
        Assert.IsTrue(matches >= 0);
        string body = source[matches..source.IndexOf(';', matches)];
        StringAssert.Contains(body, "ProfileEvidenceMatches(operation) &&");
    }

    [TestMethod]
    public void FreshEvidenceProducersNeverReadTheFrozenEvidenceId()
    {
        // No production producer exists yet (the Profile V2 observer is deferred); the guard applies as soon as one is added.
        Assert.IsTrue(Regex.IsMatch("internal sealed class P : IReviewedUpdateDispatchEvidenceSource",
            @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIReviewedUpdateDispatchEvidenceSource\b"));
        string src = Path.Combine(FindRepositoryRoot(), "src");
        foreach (string path in Sources(src))
        {
            string source = File.ReadAllText(path);
            if (!Regex.IsMatch(source, @"\b(class|record|struct)\s+\w+[^{;]*:[^{;]*\bIReviewedUpdateDispatchEvidenceSource\b")) continue;
            Assert.IsFalse(Regex.IsMatch(source, @"\.\s*EvidenceId\b"), $"{path} reads an EvidenceId; F′ must come from fresh observation");
        }
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
