using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PCSteward.Catalog;

namespace PCSteward.Catalog.UnitTests;

[TestClass]
public sealed class CatalogPatternsAndSeedTests
{
    private static readonly DateOnly SeedDate = new(2026, 10, 8);

    [TestMethod]
    [DataRow("^(a+)+$")]
    [DataRow("^(a|aa)*$")]
    [DataRow("^(ab){2,}$")]
    [DataRow(@"^(a*\\?)+$")]
    [DataRow(@"^(a+\\)+$")]
    [DataRow(@"^(a)\1$")]
    [DataRow(@"^(?<n>a)\k<n>$")]
    [DataRow("^(?=a).*$")]
    [DataRow("^(?i)a$")]
    [DataRow("^(?:a)$")]
    [DataRow("^(a$")]
    [DataRow("^a)$")]
    [DataRow("^[a$")]
    [DataRow("Mozilla$")]
    [DataRow("^Mozilla")]
    public void DangerousOrMalformedGrammarIsRefused(string pattern)
    {
        Assert.IsFalse(CatalogPatterns.IsAllowed(pattern), pattern);
        Assert.ThrowsExactly<ArgumentException>(() => CatalogPatterns.Compile(pattern));
    }

    [TestMethod]
    [DataRow(@"^Mozilla Firefox( \((x64|x86|arm64) [a-z]{2,3}(-[A-Z]{2})?\))?$", "Mozilla Firefox (x64 en-GB)")]
    [DataRow(@"^7-Zip \d{1,2}\.\d{2}( \((x64|arm64)\))?$", "7-Zip 24.09 (x64)")]
    [DataRow(@"^Notepad\+\+( \((32|64)-bit (x86|x64|ARM64)\))?$", "Notepad++ (64-bit x64)")]
    [DataRow(@"^WizTree v?[0-9.]{1,32}$", "WizTree v4.32")]
    [DataRow(@"^[)(]{1,3} literal$", "() literal")]
    [DataRow(@"^C:\\Apps\\Tool$", @"C:\Apps\Tool")]
    public void OrdinaryGrammarIsAllowedAndMatches(string pattern, string input)
    {
        Assert.IsTrue(CatalogPatterns.IsAllowed(pattern), pattern);
        Assert.IsTrue(CatalogPatterns.IsMatch(CatalogPatterns.Compile(pattern), input));
    }

    [TestMethod]
    public void MatchingIsCaseInsensitiveAndBounded()
    {
        Regex pattern = CatalogPatterns.Compile("^VLC media player$");
        Assert.IsTrue(CatalogPatterns.IsMatch(pattern, "vlc MEDIA player"));
        Assert.IsFalse(CatalogPatterns.IsMatch(pattern, null));
        Assert.IsFalse(CatalogPatterns.IsMatch(pattern, new string('a', CatalogPatterns.MaximumInputLength + 1)));
    }

    [TestMethod]
    public void TimeoutBoundsAPathologicalPatternThatBypassedValidation()
    {
        Regex pattern = new("^(a+)+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, CatalogPatterns.MatchTimeout);
        Stopwatch watch = Stopwatch.StartNew();
        bool matched = CatalogPatterns.IsMatch(pattern, new string('a', 40) + "!");
        watch.Stop();
        Assert.IsFalse(matched);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1), $"{watch.Elapsed.TotalMilliseconds} ms");
    }

    [TestMethod]
    [DataRow("7-Zip 24.09 (x64)", @"^7-Zip [0-9.]{1,32} \(x64\)$")]
    [DataRow("Notepad++ (64-bit x64)", @"^Notepad\+\+ \(64-bit x64\)$")]
    [DataRow("Everything 1.4.1.1026 (x64)", @"^Everything [0-9.]{1,32} \(x64\)$")]
    [DataRow("Steam", "^Steam$")]
    public void SeedNamePatternEscapesAndGeneralisesVersions(string displayName, string expected)
    {
        string pattern = CatalogSeedProposer.NamePattern(displayName);
        Assert.AreEqual(expected, pattern);
        Assert.IsTrue(CatalogPatterns.IsMatch(CatalogPatterns.Compile(pattern), displayName));
    }

    [TestMethod]
    public void SeedProposesOnlyExactWingetAndChocolateyMatchesAndValidates()
    {
        CatalogSeedInstance[] instances =
        [
            new("Mozilla Firefox (x64 en-GB)", "Mozilla", "registry", "Mozilla Firefox 131.0 (x64 en-GB)", null),
            new("Mozilla Firefox (x64 en-GB)", null, "winget", "Mozilla.Firefox", "winget"),
            new("Notepad++ (64-bit x64)", null, "winget", "Notepad++.Notepad++", "winget"),
            new("Store App", null, "winget", "9NBLGGH4NNS1", "msstore"),
            new("Unknown Source App", null, "winget", "Vendor.Unknown", null),
            new("git", null, "chocolatey", "git", null),
            new("Unrelated", "Someone", "registry", "Unrelated", null),
            new("Bad Id", null, "winget", "Bad Id & calc", "winget"),
            new("Mozilla Firefox (x64 en-GB)", null, "winget", "Mozilla.Firefox", "winget")
        ];

        CatalogSeedResult result = CatalogSeedProposer.Propose(instances, SeedDate);
        CatalogLoadResult proposal = CatalogValidator.Validate(Encoding.UTF8.GetBytes(result.ProposalJson));
        Assert.IsTrue(proposal.IsLoaded, string.Join(", ", proposal.Errors));
        Assert.AreEqual("2026.10.08.1", proposal.Catalog!.CatalogVersion);
        Assert.AreEqual(3, result.ProposedEntries);
        Assert.AreEqual(1, result.Skipped.Count, string.Join(", ", result.Skipped));

        CatalogEntry firefox = proposal.Catalog.Entries.Single(entry => entry.Id == "pcs:mozilla-firefox");
        Assert.AreEqual("Mozilla", firefox.Publisher);
        Assert.AreEqual("^Mozilla$", firefox.Detection.ArpPublisherPattern);
        Assert.AreEqual(@"^Mozilla Firefox \(x64 en-GB\)$", firefox.Detection.ArpDisplayNamePattern);
        Assert.AreEqual(CatalogSource.WinGet, firefox.DefaultProvider.Source);

        CatalogEntry notepad = proposal.Catalog.Entries.Single(entry => entry.DefaultProvider.PackageId == "Notepad++.Notepad++");
        Assert.IsNull(notepad.Detection.ArpPublisherPattern, "No registry publisher evidence means no publisher pattern.");
        Assert.AreEqual("Notepad++", notepad.Publisher);

        CatalogEntry git = proposal.Catalog.Entries.Single(entry => entry.DefaultProvider.Provider == CatalogProviderKind.Chocolatey);
        Assert.AreEqual(CatalogScope.Machine, git.DefaultProvider.Scopes.Single());
        Assert.IsNull(git.Detection.ArpDisplayNamePattern);
    }

    [TestMethod]
    public void SeedGivesCollidingSlugsUniqueIds()
    {
        CatalogSeedResult result = CatalogSeedProposer.Propose(
        [
            new("A", null, "winget", "Vendor.App", "winget"),
            new("B", null, "winget", "Vendor-App.X", "winget"),
            new("C", null, "chocolatey", "vendor-app", null)
        ], SeedDate);
        JsonArray entries = JsonNode.Parse(result.ProposalJson)!["entries"]!.AsArray();
        string[] ids = [.. entries.Select(entry => entry!["id"]!.GetValue<string>())];
        Assert.AreEqual(3, ids.Distinct(StringComparer.Ordinal).Count(), string.Join(", ", ids));
        Assert.IsTrue(CatalogValidator.Validate(Encoding.UTF8.GetBytes(result.ProposalJson)).IsLoaded);
    }
}
