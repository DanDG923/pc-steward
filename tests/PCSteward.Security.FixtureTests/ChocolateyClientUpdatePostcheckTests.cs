using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/13, AC-UPD-07; ADR-031 design mapping §3.6 and §3a (portable): the client
/// self-update postcheck is Verified only on an exact target-release observation (release, registration, asset digest,
/// engine, requester); a leftover only with the job's source release bytes; the unlisted leftover is named; everything else
/// is Unverified, and restart-pending is never reported.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdatePostcheckTests
{
    private const string User = "S-1-5-21-11-22-33-1001";

    [TestMethod]
    public void OnlyAnExactTargetReleaseObservationIsVerified()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = Releases();
        Assert.AreEqual(ChocolateyClientUpdateVerdict.Verified, Check(Observation(target), source, target));
        PostUpdateInventoryResult verified = ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.Verified);
        Assert.IsTrue(verified.ExpectedVersionDetected);
        Assert.IsFalse(verified.RestartPending);
        Assert.AreEqual(PostUpdateVerificationDetail.None, verified.Detail);

        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(source), source, target), "still the source release");
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(list.Releases[1]), source, target), "another listed release");
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target, installed: "2.0.0"), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target, archive: new string('9', 64)), source, target),
            "the registered archive is not the reviewed asset");
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target, sid: "S-1-5-21-11-22-33-1002"), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target, extension: new string('E', 64)), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target), target, source), "a downgrade never verifies");
        ChocolateyReviewedRelease foreign = ChocolateyReleaseListTests.Three().Releases[2];
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(foreign), source, foreign), "mixed lists");
    }

    [TestMethod]
    public void ALeftoverVerifiesOnlyWithTheSourceReleaseBytes()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = Releases();
        Assert.AreEqual(ChocolateyClientUpdateVerdict.VerifiedWithLeftover, Check(Observation(target, leftoverOf: source), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target, leftoverOf: list.Releases[1]), source, target));
        Assert.IsTrue(ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.VerifiedWithLeftover).ExpectedVersionDetected);
    }

    [TestMethod]
    public void RefusalsAndInterruptsAreNeverVerified()
    {
        (_, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = Releases();
        Assert.AreEqual(ChocolateyClientUpdateVerdict.UnlistedLeftover, Check(ChocolateyProfileResult.Refused.Create(
            ChocolateyProfileRefusal.MachineInputsUnsupported, null, null, ChocolateyProfileRefusalDetail.UnlistedReleaseLeftover), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(ChocolateyProfileResult.Refused.Create(
            ChocolateyProfileRefusal.MachineInputsUnsupported, null, null), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(ChocolateyProfileResult.Refused.Create(
            ChocolateyProfileRefusal.InstalledMismatch, "1.0.0", null), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotFinished, Check(ChocolateyProfileResult.TimedOut.Create(), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotFinished, Check(ChocolateyProfileResult.Cancelled.Create(), source, target));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, "1.0.0", null,
            ChocolateyProfileRefusalDetail.UnlistedReleaseLeftover), "the detail applies only to MachineInputsUnsupported");

        foreach (ChocolateyClientUpdateVerdict verdict in new[] { ChocolateyClientUpdateVerdict.NotTargetRelease,
                     ChocolateyClientUpdateVerdict.UnlistedLeftover, ChocolateyClientUpdateVerdict.NotFinished })
        {
            PostUpdateInventoryResult result = ChocolateyClientUpdatePostcheck.ToResult(verdict);
            Assert.IsFalse(result.ExpectedVersionDetected, verdict.ToString());
            Assert.IsFalse(result.RestartPending, verdict.ToString());
            Assert.AreNotEqual(PostUpdateVerificationDetail.None, result.Detail, verdict.ToString());
        }
        Assert.AreEqual(PostUpdateVerificationDetail.ClientLeftoverNotListed,
            ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.UnlistedLeftover).Detail);
    }

    private static ChocolateyClientUpdateVerdict Check(ChocolateyProfileResult result, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) =>
        ChocolateyClientUpdatePostcheck.Evaluate(result, source, target, User);

    private static (ChocolateyReleaseList, ChocolateyReviewedRelease, ChocolateyReviewedRelease) Releases()
    {
        ChocolateyReleaseList list = ChocolateyReleaseListTests.Three();
        return (list, list.Releases[0], list.Releases[2]);
    }

    private static ChocolateyProfileResult.Observed Observation(ChocolateyReviewedRelease release, string? installed = null, string? archive = null,
        string sid = User, string? extension = null, ChocolateyReviewedRelease? leftoverOf = null)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes("<chocolatey></chocolatey>"), out ChocolateyConfigurationObservation? configuration));
        Assert.IsTrue(ChocolateyNativeVersion.TryParse(installed ?? release.Version, out ChocolateyNativeVersion? version));
        ChocolateyCommonProfileData common = new(sid, Identity(1), [], Identity(2), configuration!, "chocolatey", version!, Identity(4),
            archive ?? release.AssetSha256, null, release.AssetSha256);
        ChocolateyClientStateData state = new(release.EngineSha256, new string('A', 64), new string('B', 64), extension);
        ChocolateyReleaseResidue? residue = leftoverOf is null ? null : new(ChocolateyReleaseList.PowerShellModuleLeftover, leftoverOf);
        return ChocolateyProfileResult.Observed.Create(ChocolateyProfileObservation.Create(common, ChocolateyCommonProfileFingerprint.Compute(common),
            state, version!, false, null, release, residue));
    }

    private static string Identity(byte value) => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat(value, 16).ToArray());
}
