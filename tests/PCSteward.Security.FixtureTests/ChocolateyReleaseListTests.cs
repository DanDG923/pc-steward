using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §3.1, §3.3 and §3a (portable): the compiled reviewed release list, the
/// offer rule (listed installed → newest strictly newer listed only; never a downgrade; nothing for an unlisted client) and
/// the content-bound leftover rule. Multi-entry lists are test-only fixtures; production holds exactly 2.7.5.
/// </summary>
[TestClass]
public sealed class ChocolateyReleaseListTests
{
    [TestMethod]
    public void TheProductionListHoldsExactlyTheReviewed275Release()
    {
        ChocolateyReviewedRelease release = ChocolateyReleaseList.Listed.Releases.Single();
        Assert.AreEqual("2.7.5", release.Version);
        Assert.AreEqual("2.7.5.0", release.EngineFileVersion);
        Assert.AreEqual(ChocolateyClientReleasePolicy.Version, release.EngineFileVersion);
        Assert.AreEqual(ChocolateyClientReleasePolicy.AssetSha256, release.AssetSha256);
        Assert.AreEqual(ChocolateyClientReleasePolicy.EngineSha256, release.EngineSha256);
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.AssetLength, release.AssetLength);
        CollectionAssert.AreEqual(ChocolateyReleaseManifest2_7_5.Files.ToArray(), release.Files.ToArray());
        Assert.AreEqual(ChocolateyReleaseManifest2_7_5.Files.Single(file => file.Path == "helpers/Chocolatey.PowerShell.dll"), release.PowerShellModule);
        Assert.IsTrue(ChocolateyClientReleasePolicy.IsListedEngine(ChocolateyClientReleasePolicy.EngineSha256));
        Assert.IsTrue(ChocolateyClientReleasePolicy.IsListedAsset(ChocolateyClientReleasePolicy.AssetSha256));
        Assert.IsFalse(ChocolateyClientReleasePolicy.IsListedEngine(new string('A', 64)));
        Assert.IsFalse(ChocolateyClientReleasePolicy.IsListedEngine(null));
        Assert.IsFalse(ChocolateyClientReleasePolicy.IsListedAsset(ChocolateyClientReleasePolicy.EngineSha256));
        // With one listed release nothing is ever offered, even after admission.
        Assert.IsNull(ChocolateyReleaseList.Listed.OfferTarget(release));
    }

    [TestMethod]
    public void AMultiEntryListSelectsByEngineAndVersion()
    {
        ChocolateyReleaseList list = Three();
        Assert.AreEqual(3, list.Releases.Count);
        Assert.AreEqual("2.0.0", list.FindByEngine(Digest('2'))!.Version);
        Assert.AreEqual("3.0.0", list.FindByVersion("3.0.0")!.Version);
        Assert.IsNull(list.FindByEngine(Digest('9')));
        Assert.IsNull(list.FindByVersion("2.0"));
        Assert.IsTrue(list.ContainsAsset(Digest('B')));
        Assert.IsFalse(ChocolateyClientReleasePolicy.IsListedEngine(Digest('2')), "a fixture release never becomes a production listing");
    }

    [TestMethod]
    public void TheOfferIsTheNewestStrictlyNewerListedReleaseAndNeverADowngrade()
    {
        ChocolateyReleaseList list = Three();
        ChocolateyReviewedRelease oldest = list.Releases[0], middle = list.Releases[1], newest = list.Releases[2];
        Assert.AreSame(newest, list.OfferTarget(oldest));
        Assert.AreSame(newest, list.OfferTarget(middle));
        Assert.IsNull(list.OfferTarget(newest), "no equal or older target");
        Assert.IsNull(list.OfferTarget(null));
        // A release of another list (an unlisted installed client from this list's view) is never offered anything.
        ChocolateyReviewedRelease foreign = Three().Releases[0];
        Assert.IsFalse(list.Contains(foreign));
        Assert.IsNull(list.OfferTarget(foreign));
        foreach (ChocolateyReviewedRelease installed in list.Releases)
            if (list.OfferTarget(installed) is { } target)
                Assert.IsTrue(ChocolateyReleaseList.IsNewer(target, installed));
    }

    [TestMethod]
    public void TheLeftoverRuleAcceptsOnlyTheReviewedNameWithAListedReleasesBytes()
    {
        ChocolateyReleaseList list = Three();
        ChocolateyReleaseFile module = list.Releases[0].PowerShellModule!.Value;
        Assert.AreSame(list.Releases[0], list.MatchLeftover(@"helpers\Chocolatey.PowerShell.dll.old", module.Length, module.Sha256));
        Assert.AreSame(list.Releases[0], list.MatchLeftover(@"HELPERS\chocolatey.powershell.DLL.OLD", module.Length, module.Sha256), "NTFS case");
        Assert.IsNull(list.MatchLeftover(@"helpers\Chocolatey.PowerShell.dll.old", module.Length + 1, module.Sha256));
        Assert.IsNull(list.MatchLeftover(@"helpers\Chocolatey.PowerShell.dll.old", module.Length, Digest('F')));
        Assert.IsNull(list.MatchLeftover(@"helpers\chocolateyInstaller.psm1.old", module.Length, module.Sha256), "closed name set");
        Assert.IsNull(list.MatchLeftover(@"tools\Chocolatey.PowerShell.dll.old", module.Length, module.Sha256));
        Assert.IsNull(list.MatchLeftover(@"helpers\Chocolatey.PowerShell.dll", module.Length, module.Sha256));
        // The real production leftover from a previous unlisted release has other bytes and refuses.
        Assert.IsNull(ChocolateyReleaseList.Listed.MatchLeftover(@"helpers\Chocolatey.PowerShell.dll.old", 40_000, Digest('C')));
        ChocolateyReleaseFile real = ChocolateyReleaseList.Listed.Releases[0].PowerShellModule!.Value;
        Assert.IsNotNull(ChocolateyReleaseList.Listed.MatchLeftover(@"helpers\Chocolatey.PowerShell.dll.old", real.Length, real.Sha256));
    }

    [TestMethod]
    public void EveryMalformedListRefusesAtConstruction()
    {
        ChocolateyReleaseEntry one = Entry("1.0.0", '1', 'a');
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([Entry("2.0.0", '2', 'b'), one]), "descending");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one, Entry("1.0.0", '2', 'b')]), "equal versions");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one, Entry("2.0.0", '1', 'b')]), "engine reused");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one, Entry("2.0.0", '2', 'a')]), "asset reused");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([Entry("2.0.0-beta", '2', 'b')]), "prerelease");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([Entry("2.0", '2', 'b')]), "not normalized");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one with { EngineFileVersion = "1.0.1.0" }]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one with { EngineFileVersion = "1.0.0" }]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one with { AssetSha256 = "abc" }]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one with { AssetLength = 0 }]));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests([one with { Files = [.. one.Files.Skip(1)] }]), "no engine");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests(
            [one with { Files = [.. one.Files, new ChocolateyReleaseFile("lib/x.nupkg", 1, Digest('1'))] }]), "outside the release tree");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReleaseList.CreateForTests(
            [one with { Files = [.. one.Files, one.Files[0] with { Path = "CHOCO.EXE" }] }]), "case alias");
    }

    internal static ChocolateyReleaseList Three() => ChocolateyReleaseList.CreateForTests(
        [Entry("1.0.0", '1', 'a'), Entry("2.0.0", '2', 'b'), Entry("3.0.0", '3', 'c')]);

    internal static ChocolateyReleaseEntry Entry(string version, char engine, char asset) =>
        new(version, version + ".0", "chocolatey." + version + ".nupkg", 1000, Digest(char.ToUpperInvariant(asset)),
        [
            new ChocolateyReleaseFile("choco.exe", 10, Digest(engine)),
            new ChocolateyReleaseFile("helpers/Chocolatey.PowerShell.dll", 20 + engine, Digest((char)('4' + engine - '1'))),
            new ChocolateyReleaseFile("tools/7z.exe", 30, Digest('E')),
        ]);

    internal static string Digest(char value) => new(value, 64);
}
