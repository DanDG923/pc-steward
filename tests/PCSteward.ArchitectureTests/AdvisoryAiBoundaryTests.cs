using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// FR-AI-01/03/04/05 (ADR-014 extension in ADR-028 "Decision", Task 67): advisory AI is capability limited. The Domain AI rules are
/// pure (no network, credential, file, registry or process access). The scheduled host, the elevated helper and update execution
/// have no AI send or credential capability and never name the provider route. The provider origin is compiled in exactly one place.
/// In this build no model is reviewed and paid requests are disabled; changing either needs the Task 69 evidence and Task 70 review.
/// </summary>
[TestClass]
public sealed class AdvisoryAiBoundaryTests
{
    private static readonly string[] NoAiProjects = ["PCSteward.ScheduledHost", "PCSteward.ElevatedHelper", "PCSteward.Execution"];
    private static readonly string[] OriginFiles = ["src/PCSteward.Domain/AdvisoryAiRequest.cs"];

    [TestMethod]
    public void DomainAiRulesArePure()
    {
        string code = string.Join(Environment.NewLine, Sources("PCSteward.Domain").Where(path => Path.GetFileName(path).StartsWith("AdvisoryAi", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        Assert.IsGreaterThan(1000, code.Length, "the Domain AI files exist");
        foreach (string forbidden in new[]
        {
            "HttpClient", "System.Net", "Socket", "WebRequest", "Registry", "Process", "File.", "Directory.", "FileStream", "Environment.",
            "DllImport", "LibraryImport", "CredRead", "CredWrite", "CredDelete", "ProtectedData", "unsafe", "Task.Run", "Thread"
        })
            Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), $"Domain AI code uses {forbidden}");
    }

    [TestMethod]
    public void TheScheduledHostHelperAndExecutionHaveNoAiCapability()
    {
        foreach (string project in NoAiProjects)
        {
            string code = string.Join(Environment.NewLine, Sources(project).Select(File.ReadAllText));
            foreach (string forbidden in new[] { "AdvisoryAi", "api.openai.com", "OpenAI", "PCSteward/OpenAI" })
                Assert.IsFalse(code.Contains(forbidden, StringComparison.Ordinal), $"{project} names {forbidden}");
            string projectFile = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", project, project + ".csproj"));
            Assert.IsFalse(projectFile.Contains("OpenAI", StringComparison.OrdinalIgnoreCase), $"{project} references an AI package or project");
            Assert.IsFalse(projectFile.Contains("AdvisoryAi", StringComparison.OrdinalIgnoreCase), $"{project} references the AI adapters");
        }
    }

    [TestMethod]
    public void TheProviderOriginIsCompiledInExactlyOnePlace()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        string[] hits = [.. Directory.EnumerateDirectories(Path.Combine(root, "src")).SelectMany(directory => Sources(Path.GetFileName(directory)))
            .Where(path => File.ReadAllText(path).Contains("api.openai.com", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))];
        CollectionAssert.AreEqual(OriginFiles, hits);
        string route = File.ReadAllText(Path.Combine(root, "src", "PCSteward.Domain", "AdvisoryAiRequest.cs"));
        Assert.AreEqual(1, Regex.Count(route, "api\\.openai\\.com"));
        StringAssert.Contains(route, "new(\"https://api.openai.com/v1/responses\", UriKind.Absolute)");
    }

    [TestMethod]
    public void TheRequestBodyHasNoToolConversationOrStorageField()
    {
        string code = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Domain", "AdvisoryAiRequest.cs"));
        string[] written = [.. Regex.Matches(code, @"writer\.Write(?:String|Number|Boolean)\(""([a-z_]+)""").Select(match => match.Groups[1].Value).Distinct()];
        foreach (string field in new[] { "model", "instructions", "input", "max_output_tokens", "store", "truncation", "service_tier" })
            CollectionAssert.Contains(written, field);
        foreach (string forbidden in new[] { "tools", "tool_choice", "previous_response_id", "conversation", "background", "stream", "metadata", "user", "include", "prompt_cache_key", "safety_identifier" })
            CollectionAssert.DoesNotContain(written, forbidden);
        StringAssert.Contains(code, "writer.WriteBoolean(\"store\", false);");
    }

    [TestMethod]
    public void NoModelIsReviewedAndPaidRequestsAreDisabledInThisBuild()
    {
        string code = File.ReadAllText(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", "PCSteward.Domain", "AdvisoryAiPolicy.cs"));
        StringAssert.Contains(code, "public static IReadOnlyList<AdvisoryAiModelProfile> Reviewed { get; } = [];");
        StringAssert.Contains(code, "public static AdvisoryAiPaidRequestMode PaidRequests => AdvisoryAiPaidRequestMode.Disabled;");
    }

    private static IEnumerable<string> Sources(string project) =>
        Directory.EnumerateFiles(Path.Combine(CompiledIlScanner.FindRepositoryRoot(), "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
}
