using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design mapping §3.2 and §3a: observer step 2 over a fixture release list. The release
/// is selected by the engine digest and carried in the observation (fingerprint field 4 is its asset digest); the one
/// reviewed self-upgrade leftover is accepted only in client mode (the client self-update) with a listed release's bytes and is
/// disclosed, never held; a leftover with other bytes refuses with the disclosed detail only when it was the sole step 1–7
/// failure; a package observation refuses every leftover, as before (MEDIUM-2a). A Helper-role client observation refuses
/// while a pinned setup override name is set (design §2 R5). Windows-only.
/// </summary>
[TestClass]
public sealed class ChocolateyReleaseListObserverTests
{
    private const string Module = "helpers/Chocolatey.PowerShell.dll";
    private const string Leftover = @"helpers\Chocolatey.PowerShell.dll.old";
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);
    private static readonly byte[] OldModule = Encoding.UTF8.GetBytes("fixture:previous-release-module");

    [TestMethod]
    public void TheEngineDigestSelectsTheListedReleaseAndItsAssetFeedsTheFingerprint()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build().Lock();
        ChocolateyReleaseList list = TwoReleases(fixture);
        using ChocolateyProfileObservation observation = Observed(Observe(token, fixture, list, ChocolateyObserverRole.Desktop));
        Assert.AreSame(list.Releases[1], observation.Release);
        Assert.AreEqual("2.0.0", observation.Release!.Version);
        Assert.AreEqual(list.Releases[1].AssetSha256, observation.Common.ReleaseAssetSha256);
        Assert.AreEqual(ChocolateyCommonProfileFingerprint.Compute(observation.Common), observation.Fingerprint);
        Assert.IsNull(observation.ReleaseResidue);
    }

    [TestMethod]
    public void AnEngineMatchingNoListedReleaseRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build().Lock();
        ChocolateyReleaseList other = ChocolateyReleaseListTests.Three();
        Refused(Observe(token, fixture, other, ChocolateyObserverRole.Desktop), ChocolateyProfileRefusalDetail.None);
    }

    [TestMethod]
    public void ALeftoverWithAListedReleasesBytesIsADisclosedFindingAndIsNeverHeld()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build(leftover: OldModule).Lock();
        ChocolateyReleaseList list = TwoReleases(fixture);
        using (ChocolateyProfileObservation desktop = Observed(ObserveClient(token, fixture, list, ChocolateyObserverRole.Desktop)))
        {
            Assert.AreSame(list.Releases[1], desktop.Release);
            Assert.AreEqual(Leftover, desktop.ReleaseResidue!.RelativePath);
            Assert.AreSame(list.Releases[0], desktop.ReleaseResidue.MatchedRelease);
            Assert.AreEqual(OldModule.Length, desktop.ReleaseResidue.Length);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(OldModule)), desktop.ReleaseResidue.Sha256);
            Assert.IsTrue(desktop.ReleaseResidue.IsModuleOf(list.Releases[0]));
        }
        // As the N2 helper positive: the helper cache is an existing protected system directory (the one seam substitution).
        fixture.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
        using ChocolateyProfileObservation helper = Observed(ObserveClient(token, fixture, list, ChocolateyObserverRole.Helper));
        Assert.IsNotNull(helper.ReleaseResidue);
        Assert.IsFalse(helper.HeldClient!.ReleaseFiles.Any(file => file.RelativePath.EndsWith(".old", StringComparison.OrdinalIgnoreCase)),
            "the leftover is never held");
    }

    [TestMethod]
    public void ALeftoverWithUnlistedBytesRefusesWithTheDisclosedDetail()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build(leftover: Encoding.UTF8.GetBytes("fixture:unlisted-module")).Lock();
        Refused(ObserveClient(token, fixture, TwoReleases(fixture), ChocolateyObserverRole.Desktop), ChocolateyProfileRefusalDetail.UnlistedReleaseLeftover);
    }

    [TestMethod]
    public void APackageObservationRefusesEveryLeftoverWithoutTheDetail()
    {
        // MEDIUM-2a: until the package route can disclose a leftover, package mode refuses it like any unlisted file.
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using (N2ProfileFixture listed = Build(leftover: OldModule).Lock())
            Refused(Observe(token, listed, TwoReleases(listed), ChocolateyObserverRole.Desktop), ChocolateyProfileRefusalDetail.None);
        using N2ProfileFixture unlisted = Build(leftover: Encoding.UTF8.GetBytes("fixture:unlisted-module")).Lock();
        Refused(Observe(token, unlisted, TwoReleases(unlisted), ChocolateyObserverRole.Desktop), ChocolateyProfileRefusalDetail.None);
    }

    [TestMethod]
    public void AHelperClientObservationRefusesWhileASetupOverrideNameIsSet()
    {
        // Design §2 R5: stage B and the stage-D re-observation refuse with the specific detail; the Desktop role is unaffected.
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build().Lock();
        fixture.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
        ChocolateyReleaseList list = TwoReleases(fixture);
        ChocolateySetupEnvironmentReading[] refused =
        [
            ChocolateySetupEnvironmentReading.Of([ChocolateySetupEnvironmentReader.AllowCurrentUserName], []),
            ChocolateySetupEnvironmentReading.Of([ChocolateySetupEnvironmentReader.AllowInsecureRootName], []),
            ChocolateySetupEnvironmentReading.Of([ChocolateySetupEnvironmentReader.InstallRootName], []),
            ChocolateySetupEnvironmentReading.Of([], [ChocolateySetupEnvironmentReader.AllowCurrentUserName]),
            ChocolateySetupEnvironmentReading.Unreadable,
        ];
        foreach (ChocolateySetupEnvironmentReading reading in refused)
            Refused(ObserveClient(token, fixture, list, ChocolateyObserverRole.Helper, reading), ChocolateyProfileRefusalDetail.SetupAccessOverride);
        using (ChocolateyProfileObservation desktop = Observed(ObserveClient(token, fixture, list, ChocolateyObserverRole.Desktop, refused[0])))
            Assert.IsNull(desktop.HeldClient);
        using ChocolateyProfileObservation helper = Observed(ObserveClient(token, fixture, list, ChocolateyObserverRole.Helper,
            ChocolateySetupEnvironmentReading.Of([], [])));
        Assert.AreEqual(ChocolateyBindingPurpose.ClientSelfUpdate, helper.HeldClient!.Purpose);
    }

    [TestMethod]
    public void AnUnlistedLeftoverWithAnotherFailureRefusesWithoutTheDetail()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build(leftover: Encoding.UTF8.GetBytes("fixture:unlisted-module"));
        fixture.WriteConfiguration("<features><feature name='checksumFiles' enabled='false' setExplicitly='true'/></features>");
        fixture.Lock();
        Refused(Observe(token, fixture, TwoReleases(fixture), ChocolateyObserverRole.Desktop), ChocolateyProfileRefusalDetail.None);
    }

    [TestMethod]
    public void AnyOtherOldFileStillRefuses()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = Build();
        File.WriteAllBytes(fixture.RootPath(@"helpers\chocolateyInstaller.psm1.old"), OldModule);
        fixture.Lock();
        Refused(Observe(token, fixture, TwoReleases(fixture), ChocolateyObserverRole.Desktop), ChocolateyProfileRefusalDetail.None);
    }

    private static N2ProfileFixture Build(byte[]? leftover = null)
    {
        N2ProfileFixture fixture = new();
        fixture.ReleaseContent[Module] = "fixture:current-release-module";
        fixture.Build();
        // The fixture tree is listed as 2.0.0, so a client-mode observation needs the client registration at 2.0.0.
        fixture.WritePackage(@"lib\chocolatey\chocolatey.nupkg", "chocolatey", "2.0.0");
        if (leftover is not null) File.WriteAllBytes(fixture.RootPath(Leftover), leftover);
        return fixture;
    }

    /// <summary>An older listed release (other engine, the <see cref="OldModule"/> bytes) and the fixture's own tree as 2.0.0.</summary>
    private static ChocolateyReleaseList TwoReleases(N2ProfileFixture fixture) => ChocolateyReleaseList.CreateForTests(
    [
        new ChocolateyReleaseEntry("1.0.0", "1.0.0.0", "chocolatey.1.0.0.nupkg", 1000, new string('A', 64),
        [
            new ChocolateyReleaseFile("choco.exe", 7, new string('1', 64)),
            new ChocolateyReleaseFile(Module, OldModule.Length, Convert.ToHexString(SHA256.HashData(OldModule))),
        ]),
        fixture.ReleaseEntry("2.0.0", new string('B', 64)),
    ]);

    private static ChocolateyProfileResult Observe(RequesterToken token, N2ProfileFixture fixture, ChocolateyReleaseList list,
        ChocolateyObserverRole role) =>
        ChocolateyProfileObserver.ObserveForTests(token, role, N2ProfileFixture.Target, N2ProfileFixture.Version("1.2.0"), null, Budget,
            fixture.Seam(list), CancellationToken.None);

    private static ChocolateyProfileResult ObserveClient(RequesterToken token, N2ProfileFixture fixture, ChocolateyReleaseList list,
        ChocolateyObserverRole role, ChocolateySetupEnvironmentReading? setup = null)
    {
        ChocolateyObserverTestSeam seam = fixture.Seam(list);
        return ChocolateyProfileObserver.ObserveClientForTests(token, role, N2ProfileFixture.Version("2.0.0"), null, Budget,
            setup is null ? seam : seam.WithSetupEnvironment(setup), CancellationToken.None);
    }

    private static ChocolateyProfileObservation Observed(ChocolateyProfileResult result) =>
        result is ChocolateyProfileResult.Observed observed ? observed.Observation
            : throw new AssertFailedException($"expected Observed, got {result}");

    private static void Refused(ChocolateyProfileResult result, ChocolateyProfileRefusalDetail detail)
    {
        if (result is ChocolateyProfileResult.Observed observed) observed.Observation.Dispose();
        ChocolateyProfileResult.Refused refused = result as ChocolateyProfileResult.Refused
            ?? throw new AssertFailedException($"expected Refused, got {result.GetType().Name}");
        Assert.AreEqual(ChocolateyProfileRefusal.MachineInputsUnsupported, refused.Reason);
        Assert.AreEqual(detail, refused.Detail);
    }
}
