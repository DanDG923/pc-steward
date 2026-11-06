using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §3.1 (portable): the compiled release table equals the reviewed JSON
/// evidence file exactly, so an edit to either side fails here. The JSON is read only by this test, never at runtime.
/// </summary>
[TestClass]
public sealed class ChocolateyReleaseManifestTests
{
    private static readonly string[] TopLevel = ["format", "release", "asset", "assetLength", "assetSha256", "pathMapping", "provenance", "files"];
    private static readonly string[] EntryFields = ["path", "length", "sha256"];
    private static readonly string[] MandatoryTools = ["tools/checksum.exe", "tools/shimgen.exe", "tools/7z.exe"];

    [TestMethod]
    public void TheCompiledTableEqualsTheReviewedJsonExactly()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(JsonPath()));
        JsonElement root = document.RootElement;
        CollectionAssert.AreEquivalent(TopLevel, root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.Format, root.GetProperty("format").GetString());
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.Release, root.GetProperty("release").GetString());
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.Asset, root.GetProperty("asset").GetString());
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.AssetLength, root.GetProperty("assetLength").GetInt64());
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.AssetSha256, root.GetProperty("assetSha256").GetString());

        ChocolateyReleaseFile[] json = [.. root.GetProperty("files").EnumerateArray().Select(entry =>
        {
            CollectionAssert.AreEqual(EntryFields, entry.EnumerateObject().Select(property => property.Name).ToArray());
            return new ChocolateyReleaseFile(entry.GetProperty("path").GetString()!, entry.GetProperty("length").GetInt64(),
                entry.GetProperty("sha256").GetString()!);
        })];
        Assert.AreEqual(56, json.Length);
        CollectionAssert.AreEqual(json, ChocolateyReleaseManifest2_7_5.Files.ToArray(), "same entries in the same order");
    }

    [TestMethod]
    public void TheTableAgreesWithTheReleasePinsAndIsWellFormed()
    {
        IReadOnlyList<ChocolateyReleaseFile> files = ChocolateyReleaseManifest2_7_5.Files;
        string compiledAsset = ChocolateyReleaseManifest2_7_5.AssetSha256;
        Assert.AreEqual(ChocolateyClientReleasePolicy.AssetSha256, compiledAsset);
        Assert.AreEqual(ChocolateyClientReleasePolicy.EngineSha256, files.Single(file => file.Path == "choco.exe").Sha256);
        Assert.AreEqual(12_022_880, files.Single(file => file.Path == "choco.exe").Length);
        Assert.AreEqual(15_258_758, files.Sum(file => file.Length), "Profile V2 total release-tree bytes");
        Assert.AreEqual(files.Count, files.Select(file => file.Path.ToUpperInvariant()).Distinct().Count(), "no case aliases");
        foreach (ChocolateyReleaseFile file in files)
        {
            Assert.IsTrue(file.Sha256.Length == 64 && file.Sha256.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F'), file.Path);
            Assert.IsTrue(file.Length >= 0, file.Path);
            string[] parts = file.Path.Split('/');
            Assert.IsTrue(parts.Length == 1 || parts[0] is "helpers" or "tools", file.Path);
            Assert.IsTrue(ChocolateyProfileRules.IsReleasePath(file.Path), file.Path);
        }
        // The mandatory protected tools whose absence would permit an environment fallback (Profile V2).
        foreach (string tool in MandatoryTools)
            Assert.IsTrue(files.Any(file => file.Path == tool), tool);
    }

    private static string JsonPath()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx")))
                return Path.Combine(current.FullName, "docs", "review", "TASK_50_CHOCOLATEY_RELEASE_2_7_5.json");
        throw new InvalidOperationException("Repository root was not found.");
    }
}
