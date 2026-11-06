using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13, ADR-031 design §7.1 C (portable): the client self-update's stage-C decision over the strict
/// <c>list chocolatey --exact</c> outcome and the compiled offer rule — Matched only when the listed version is the stage-B
/// source release and the list's offer is the frozen candidate; every other row is the reviewed refusal or Undetermined.
/// </summary>
[TestClass]
public sealed class ChocolateyClientStageCMappingTests
{
    private static readonly string Fingerprint = ReviewedUpdateWireCodecFixture.Fingerprint;

    [TestMethod]
    public void OnlyTheSourceReleaseWithTheFrozenOfferMatches()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        ChocolateyCandidateValues matched = Map(source, target.Version, List("chocolatey|" + source.Version));
        Assert.AreEqual(target.Version, ((ChocolateyCandidateValues.Matched)matched).Candidate);

        AssertRefused(Map(source, target.Version, List("chocolatey|" + list.Releases[1].Version)), ChocolateyCandidateRefusal.InstalledMismatch,
            list.Releases[1].Version, null);
        AssertRefused(Map(source, target.Version, List("")), ChocolateyCandidateRefusal.InstalledMismatch, null, null);
        AssertRefused(Map(source, list.Releases[1].Version, List("chocolatey|" + source.Version)), ChocolateyCandidateRefusal.CandidateMismatch,
            source.Version, target.Version);
        // The newest listed release has no strictly newer offer.
        AssertRefused(Map(target, "9.0.0", List("chocolatey|" + target.Version)), ChocolateyCandidateRefusal.CandidateMismatch, target.Version, null);
        AssertRefused(Map(source, target.Version, List("chocolatey|" + source.Version + "\nchocolatey|" + source.Version)),
            ChocolateyCandidateRefusal.PreflightQueryFailed, source.Version, null);
        AssertRefused(Map(source, target.Version, List("git|" + source.Version)), ChocolateyCandidateRefusal.PreflightQueryFailed, source.Version, null);
        AssertRefused(Map(source, target.Version, List("chocolatey|" + source.Version, exit: 1)), ChocolateyCandidateRefusal.PreflightQueryFailed,
            source.Version, null);
    }

    [TestMethod]
    public void AnInterruptedOrUnsettledListIsUndetermined()
    {
        (_, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        ChocolateyCandidateValues cancelled = Map(source, target.Version, List("chocolatey|" + source.Version, cancelled: true));
        Assert.IsTrue(cancelled is ChocolateyCandidateValues.Undetermined { Cancelled: true }, cancelled.ToString());
        ChocolateyCandidateValues unsettled = Map(source, target.Version, List("chocolatey|" + source.Version, settled: false));
        Assert.IsTrue(unsettled is ChocolateyCandidateValues.Undetermined { AggregateSettlementConfirmed: false }, unsettled.ToString());
    }

    [TestMethod]
    public void TheCallerMustHaveObservedTheSourceRelease()
    {
        (_, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChocolateyClientStageCMapping.Map(source, target.Version, Fingerprint, target.Version, List("chocolatey|" + source.Version)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ChocolateyClientStageCMapping.Map(source, source.Version, "abc", target.Version, List("chocolatey|" + source.Version)));
    }

    private static ChocolateyCandidateValues Map(ChocolateyReviewedRelease installed, string frozenCandidate,
        ChocolateyQueryOutcome<ChocolateyInstalledRecord> list) =>
        ChocolateyClientStageCMapping.Map(installed, installed.Version, Fingerprint, frozenCandidate, list);

    private static ChocolateyQueryOutcome<ChocolateyInstalledRecord> List(string output, int exit = 0, bool cancelled = false, bool settled = true) =>
        ChocolateyQueryParser.ParseListExact(new NativeProcessObservation(NativeInvocation.Started, exit, true, true, true, false, settled, cancelled,
            false, false, false, output.Length == 0 ? "" : output + "\n", ""), "chocolatey");

    private static void AssertRefused(ChocolateyCandidateValues values, ChocolateyCandidateRefusal reason, string? installed, string? candidate)
    {
        ChocolateyCandidateValues.Refused refused = values as ChocolateyCandidateValues.Refused ?? throw new AssertFailedException(values.ToString());
        Assert.AreEqual(reason, refused.Reason);
        Assert.AreEqual(installed, refused.Installed);
        Assert.AreEqual(candidate, refused.Candidate);
        Assert.AreEqual(Fingerprint, refused.Fingerprint, "field 18 is the stage-B fingerprint (N1)");
    }
}
