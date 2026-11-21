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
    public void EveryCheckoutDropsItsTokenAndEverySdkComesFromGlobalJson()
    {
        // Mythos whole-app review MY-07: no checkout keeps the job's token in its Git settings while repository code runs, and no
        // job picks a .NET SDK by a floating version instead of global.json.
        string folder = Path.Combine(CompiledIlScanner.FindRepositoryRoot(), ".github", "workflows");
        int checkouts = 0, sdks = 0;
        foreach (string path in Directory.GetFiles(folder, "*.yml").Concat(Directory.GetFiles(folder, "*.yaml")))
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.IsFalse(text.Contains("dotnet-version:", StringComparison.Ordinal), $"{Path.GetFileName(path)} picks an SDK by version");
            foreach (Match use in Uses.Matches(text))
            {
                string reference = use.Groups["reference"].Value;
                string step = StepAfter(text, use.Index + use.Length);
                if (reference.StartsWith("actions/checkout@", StringComparison.Ordinal))
                {
                    StringAssert.Contains(step, "persist-credentials: false", $"{Path.GetFileName(path)}: a checkout keeps its token");
                    checkouts++;
                }
                else if (reference.StartsWith("actions/setup-dotnet@", StringComparison.Ordinal))
                {
                    StringAssert.Contains(step, "global-json-file:", $"{Path.GetFileName(path)}: setup-dotnet without global.json");
                    sdks++;
                }
            }
        }
        Assert.IsGreaterThan(0, checkouts);
        Assert.IsGreaterThan(0, sdks);
    }

    // The rest of one step: from the end of its uses: line up to the next step (a line whose first non-space character is "-").
    private static string StepAfter(string text, int start)
    {
        Match next = Regex.Match(text[start..], @"\n\s*-\s");
        return next.Success ? text[start..(start + next.Index)] : text[start..];
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
