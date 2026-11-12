using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// Codex security scan finding 7 (2026-11-12): every GitHub Actions dependency in every workflow is pinned to a full commit,
/// with the tag it had when it was pinned in a comment, so a moved upstream tag cannot change what runs in CI or in a release
/// job. Dependabot (github-actions ecosystem) proposes reviewed SHA updates.
/// </summary>
[TestClass]
public sealed class WorkflowActionPinTests
{
    private static readonly Regex Uses = new(@"^\s*-?\s*uses:\s*(?<reference>\S+)(?<rest>.*)$", RegexOptions.Multiline);
    private static readonly Regex Pinned = new(@"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}\z");
    private static readonly Regex TagComment = new(@"\A\s+# v\d+(\.\d+)*\s*\z");

    [TestMethod]
    public void EveryWorkflowActionIsPinnedToAFullCommitWithItsTag()
    {
        string folder = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), ".github", "workflows");
        string[] workflows = Directory.GetFiles(folder, "*.yml").Concat(Directory.GetFiles(folder, "*.yaml")).ToArray();
        Assert.IsNotEmpty(workflows);
        int checkedUses = 0;
        foreach (string path in workflows)
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (Match use in Uses.Matches(text))
            {
                string reference = use.Groups["reference"].Value;
                // A local action (./path) is part of this commit.
                if (reference.StartsWith("./", StringComparison.Ordinal)) continue;
                Assert.IsTrue(Pinned.IsMatch(reference), $"{Path.GetFileName(path)}: {reference} is not pinned to a full commit");
                Assert.IsTrue(TagComment.IsMatch(use.Groups["rest"].Value), $"{Path.GetFileName(path)}: {reference} has no '# vX.Y.Z' tag comment");
                checkedUses++;
            }
        }
        Assert.IsGreaterThan(0, checkedUses);
    }

    [TestMethod]
    public void TheSameActionIsPinnedToTheSameCommitEverywhere()
    {
        string folder = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), ".github", "workflows");
        Dictionary<string, HashSet<string>> commits = new(StringComparer.Ordinal);
        foreach (string path in Directory.GetFiles(folder, "*.yml"))
        {
            foreach (Match use in Uses.Matches(File.ReadAllText(path)))
            {
                string reference = use.Groups["reference"].Value;
                int at = reference.IndexOf('@', StringComparison.Ordinal);
                if (at < 0) continue;
                string action = reference[..at];
                if (!commits.TryGetValue(action, out HashSet<string>? set)) commits[action] = set = new(StringComparer.Ordinal);
                set.Add(reference[(at + 1)..]);
            }
        }
        foreach ((string action, HashSet<string> set) in commits)
            Assert.HasCount(1, set, $"{action} is pinned to more than one commit across workflows");
    }
}
