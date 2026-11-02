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
        // INFO: a leftover of another listed release is named (its own verdict and detail), never verified.
        Assert.AreEqual(ChocolateyClientUpdateVerdict.LeftoverNotSource, Check(Observation(target, leftoverOf: list.Releases[1]), source, target));
        Assert.AreEqual(PostUpdateVerificationDetail.ClientLeftoverNotSource,
            ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.LeftoverNotSource).Detail);
        // A residue whose bytes are not those of the release it names (or of the source) is never accepted.
        ChocolateyReleaseFile module = source.PowerShellModule!.Value;
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target,
            residue: new(ChocolateyReleaseList.PowerShellModuleLeftover, source, module.Length + 1, module.Sha256)), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, Check(Observation(target,
            residue: new(@"helpers\other.dll.old", source, module.Length, module.Sha256)), source, target));

        // MEDIUM-2b: the verified leftover is a distinct note, not a detail and not a plain Verified.
        PostUpdateInventoryResult noted = ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.VerifiedWithLeftover);
        Assert.IsTrue(noted.ExpectedVersionDetected);
        Assert.IsFalse(noted.RestartPending);
        Assert.AreEqual(PostUpdateVerificationDetail.None, noted.Detail);
        Assert.AreEqual(PostUpdateVerificationNote.ClientReviewedLeftover, noted.Note);
        Assert.AreEqual(PostUpdateVerificationNote.None, ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.Verified).Note);
    }

    [TestMethod]
    public void ALeftoverSharedByTwoListedReleasesVerifiesAgainstTheSourceBytes()
    {
        // MEDIUM-1: releases 1.0.0 and 2.0.0 ship the same Chocolatey.PowerShell.dll, so the observer's disclosure match
        // (first listed) is 1.0.0 while the job's source is 2.0.0; the postcheck compares the bytes with the source directly.
        ChocolateyReleaseFile shared = new("helpers/Chocolatey.PowerShell.dll", 77, ChocolateyReleaseListTests.Digest('7'));
        ChocolateyReleaseList list = ChocolateyReleaseList.CreateForTests(
        [
            WithModule(ChocolateyReleaseListTests.Entry("1.0.0", '1', 'a'), shared),
            WithModule(ChocolateyReleaseListTests.Entry("2.0.0", '2', 'b'), shared),
            ChocolateyReleaseListTests.Entry("3.0.0", '3', 'c'),
        ]);
        ChocolateyReviewedRelease first = list.Releases[0], source = list.Releases[1], target = list.Releases[2];
        ChocolateyReviewedRelease? matched = list.MatchLeftover(ChocolateyReleaseList.PowerShellModuleLeftover, shared.Length, shared.Sha256);
        Assert.AreSame(first, matched, "the disclosure match is the first listed release with those bytes");
        ChocolateyReleaseResidue residue = new(ChocolateyReleaseList.PowerShellModuleLeftover, matched!, shared.Length, shared.Sha256);
        Assert.AreEqual(ChocolateyClientUpdateVerdict.VerifiedWithLeftover, Check(Observation(target, residue: residue), source, target));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.VerifiedWithLeftover, Check(Observation(target, residue: residue), first, target));
    }

    [TestMethod]
    public void AnIncompleteHoldReleaseIsNeverVerified()
    {
        // LOW-2 (design §2 R3): the binding's recorded partial release forces Unverified with its own detail.
        (_, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = Releases();
        Assert.AreEqual(ChocolateyClientUpdateVerdict.ReleaseIncomplete,
            ChocolateyClientUpdatePostcheck.Evaluate(Observation(target), source, target, User, holdReleaseIncomplete: true));
        Assert.AreEqual(ChocolateyClientUpdateVerdict.ReleaseIncomplete,
            ChocolateyClientUpdatePostcheck.Evaluate(Observation(target, leftoverOf: source), source, target, User, holdReleaseIncomplete: true));
        PostUpdateInventoryResult result = ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.ReleaseIncomplete);
        Assert.IsFalse(result.ExpectedVersionDetected);
        Assert.AreEqual(PostUpdateVerificationDetail.ClientReleaseIncomplete, result.Detail);
    }

    private static ChocolateyReleaseEntry WithModule(ChocolateyReleaseEntry entry, ChocolateyReleaseFile module) =>
        entry with { Files = [.. entry.Files.Select(file => file.Path == module.Path ? module : file)] };

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
                     ChocolateyClientUpdateVerdict.UnlistedLeftover, ChocolateyClientUpdateVerdict.NotFinished,
                     ChocolateyClientUpdateVerdict.LeftoverNotSource, ChocolateyClientUpdateVerdict.ReleaseIncomplete })
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
        ChocolateyClientUpdatePostcheck.Evaluate(result, source, target, User, holdReleaseIncomplete: false);

    private static (ChocolateyReleaseList, ChocolateyReviewedRelease, ChocolateyReviewedRelease) Releases()
    {
        ChocolateyReleaseList list = ChocolateyReleaseListTests.Three();
        return (list, list.Releases[0], list.Releases[2]);
    }

    private static ChocolateyProfileResult.Observed Observation(ChocolateyReviewedRelease release, string? installed = null, string? archive = null,
        string sid = User, string? extension = null, ChocolateyReviewedRelease? leftoverOf = null, ChocolateyReleaseResidue? residue = null)
    {
        Assert.IsTrue(ChocolateyConfigurationParser.TryRead(Encoding.UTF8.GetBytes("<chocolatey></chocolatey>"), out ChocolateyConfigurationObservation? configuration));
        Assert.IsTrue(ChocolateyNativeVersion.TryParse(installed ?? release.Version, out ChocolateyNativeVersion? version));
        ChocolateyCommonProfileData common = new(sid, Identity(1), [], Identity(2), configuration!, "chocolatey", version!, Identity(4),
            archive ?? release.AssetSha256, null, release.AssetSha256);
        ChocolateyClientStateData state = new(release.EngineSha256, new string('A', 64), new string('B', 64), extension);
        residue ??= leftoverOf is null ? null : new(ChocolateyReleaseList.PowerShellModuleLeftover, leftoverOf,
            leftoverOf.PowerShellModule!.Value.Length, leftoverOf.PowerShellModule.Value.Sha256);
        return ChocolateyProfileResult.Observed.Create(ChocolateyProfileObservation.Create(common, ChocolateyCommonProfileFingerprint.Compute(common),
            state, version!, false, null, release, residue));
    }

    private static string Identity(byte value) => ChocolateyCommonProfileFingerprint.FormatFileIdentity(1, Enumerable.Repeat(value, 16).ToArray());
}
