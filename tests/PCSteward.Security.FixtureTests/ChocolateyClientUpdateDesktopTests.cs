using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, AC-UPD-07, ADR-031 design §7.1 (F′) and §8.3 (W1): the desktop client
/// self-update producer and verifier over fixture client-mode observations and a fixture release list. F′ must select the
/// frozen source release; the verifier takes the interpreter-only run fault, applies it first (no observation is read for a
/// fault) and is Verified only on the exact target release. The production list (one release) yields no client evidence.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateDesktopTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private static readonly ReviewedClientRunFault None = ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.None, false);

    [TestMethod]
    public async Task FreshEvidenceSelectsTheSourceReleaseAndTheVerifierNeedsTheExactTarget()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        ReviewedUpdateOperation operation = ChocolateyClientUpdateCommandTests.Operation(source.Version, target.Version);
        ChocolateyDispatchBaselines baselines = new();
        ChocolateyProfileResult.Observed fresh = ChocolateyClientUpdatePostcheckTests.Observation(source);
        ChocolateyProfileEvidenceSource producer = ChocolateyProfileEvidenceSource.CreateClientForTests(
            (id, installed, _, _) => id == ReviewedUpdateOperation.ChocolateyClientPackageId && installed.Normalized == source.Version ? fresh : null,
            list, baselines, TimeProvider.System, Budget);

        FreshUpdateDispatchEvidence? evidence = await producer.ObserveAsync(operation, Owner(), CancellationToken.None);
        Assert.IsNotNull(evidence);
        Assert.AreEqual(ReviewedUpdateOperation.ProfileEvidencePrefix + fresh.Observation.Fingerprint, evidence.EvidenceId);
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, evidence.Route);

        int reads = 0;
        ChocolateyReviewedUpdateVerifier verifier = ChocolateyReviewedUpdateVerifier.CreateClientForTests((id, expected, _, _) =>
        {
            reads++;
            return expected.Normalized == target.Version ? ChocolateyClientUpdatePostcheckTests.Observation(target) : null;
        }, list, baselines, TimeProvider.System, Budget);
        PostUpdateInventoryResult verified = await verifier.VerifyClientAsync(operation, Owner(), None, CancellationToken.None);
        Assert.IsTrue(verified.ExpectedVersionDetected);
        Assert.IsFalse(verified.RestartPending);
        Assert.AreEqual(1, reads);
        // The baseline is one-use: a second verification is Unverifiable.
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable,
            (await verifier.VerifyClientAsync(operation, Owner(), None, CancellationToken.None)).Detail);
    }

    [TestMethod]
    public async Task EveryRunFaultIsAppliedBeforeAnyObservationAndNeverVerifies()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        ReviewedUpdateOperation operation = ChocolateyClientUpdateCommandTests.Operation(source.Version, target.Version);
        (ReviewedClientWatchdogStop Stop, bool Release, PostUpdateVerificationDetail Detail)[] rows =
        [
            (ReviewedClientWatchdogStop.SetupOverrideDuringRun, false, PostUpdateVerificationDetail.ClientSetupOverrideDuringRun),
            (ReviewedClientWatchdogStop.RootWritableDuringRun, true, PostUpdateVerificationDetail.ClientRootWritableDuringRun),
            (ReviewedClientWatchdogStop.WatchFailedDuringRun, false, PostUpdateVerificationDetail.ClientWatchFailedDuringRun),
            (ReviewedClientWatchdogStop.None, true, PostUpdateVerificationDetail.ClientReleaseIncomplete),
        ];
        foreach ((ReviewedClientWatchdogStop stop, bool release, PostUpdateVerificationDetail detail) in rows)
        {
            ChocolateyDispatchBaselines baselines = new();
            baselines.Record(operation, new ChocolateyDispatchBaseline(ReviewedUpdateOperation.ChocolateyClientPackageId, ChocolateyClientUpdatePostcheckTests.User,
                ChocolateyClientUpdatePostcheckTests.Observation(source).Observation.ClientState, source));
            int reads = 0;
            ChocolateyReviewedUpdateVerifier verifier = ChocolateyReviewedUpdateVerifier.CreateClientForTests((_, _, _, _) =>
            {
                reads++;
                return ChocolateyClientUpdatePostcheckTests.Observation(target);
            }, list, baselines, TimeProvider.System, Budget);
            PostUpdateInventoryResult result = await verifier.VerifyClientAsync(operation, Owner(),
                ReviewedClientRunFault.FromAcceptedHelperReply(stop, release), CancellationToken.None);
            Assert.IsFalse(result.ExpectedVersionDetected || result.RestartPending, detail.ToString());
            Assert.AreEqual(detail, result.Detail);
            Assert.AreEqual(0, reads, "a fault decides without reading the client");
        }
    }

    [TestMethod]
    public async Task AnythingButTheExactTargetOrAMatchingBaselineIsNotVerified()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        ReviewedUpdateOperation operation = ChocolateyClientUpdateCommandTests.Operation(source.Version, target.Version);
        ChocolateyClientStateData state = ChocolateyClientUpdatePostcheckTests.Observation(source).Observation.ClientState;

        async Task<PostUpdateInventoryResult> Verify(ChocolateyDispatchBaseline? baseline, Func<ChocolateyProfileResult?> observed)
        {
            ChocolateyDispatchBaselines baselines = new();
            if (baseline is not null) baselines.Record(operation, baseline);
            return await ChocolateyReviewedUpdateVerifier.CreateClientForTests((_, _, _, _) => observed(), list, baselines, TimeProvider.System, Budget)
                .VerifyClientAsync(operation, Owner(), None, CancellationToken.None);
        }

        ChocolateyDispatchBaseline good = new(ReviewedUpdateOperation.ChocolateyClientPackageId, ChocolateyClientUpdatePostcheckTests.User, state, source);
        Assert.AreEqual(PostUpdateVerificationDetail.ClientChanged, (await Verify(good, () => ChocolateyClientUpdatePostcheckTests.Observation(source))).Detail,
            "still the source release");
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable, (await Verify(good, () => null)).Detail, "no observation");
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable,
            (await Verify(null, () => ChocolateyClientUpdatePostcheckTests.Observation(target))).Detail, "no F′ baseline");
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable,
            (await Verify(good with { SourceRelease = list.Releases[1] }, () => ChocolateyClientUpdatePostcheckTests.Observation(target))).Detail,
            "a baseline naming another source");
        Assert.AreEqual(PostUpdateVerificationDetail.ClientUnverifiable,
            (await Verify(good with { SourceRelease = null }, () => ChocolateyClientUpdatePostcheckTests.Observation(target))).Detail,
            "a package baseline");
        Assert.IsTrue((await Verify(good, () => ChocolateyClientUpdatePostcheckTests.Observation(target))).ExpectedVersionDetected);
    }

    [TestMethod]
    public async Task FreshEvidenceRefusesAnotherReleaseAndTheProductionListOffersNothing()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease source, ChocolateyReviewedRelease target) = ChocolateyClientUpdatePostcheckTests.Releases();
        ReviewedUpdateOperation operation = ChocolateyClientUpdateCommandTests.Operation(source.Version, target.Version);
        ChocolateyDispatchBaselines baselines = new();
        // The observer selected another listed release (engine digest) at the same registered version: not the frozen source.
        ChocolateyProfileEvidenceSource other = ChocolateyProfileEvidenceSource.CreateClientForTests(
            (_, _, _, _) => ChocolateyClientUpdatePostcheckTests.Observation(list.Releases[1], installed: source.Version), list, baselines,
            TimeProvider.System, Budget);
        Assert.IsNull(await other.ObserveAsync(operation, Owner(), CancellationToken.None));
        // A frozen target that is not the list's offer (the middle release) is not the route shape.
        ReviewedUpdateOperation middle = ChocolateyClientUpdateCommandTests.Operation(source.Version, list.Releases[1].Version);
        ChocolateyProfileEvidenceSource producer = ChocolateyProfileEvidenceSource.CreateClientForTests(
            (_, _, _, _) => ChocolateyClientUpdatePostcheckTests.Observation(source), list, baselines, TimeProvider.System, Budget);
        Assert.IsNull(await producer.ObserveAsync(middle, Owner(), CancellationToken.None));
        Assert.IsFalse(baselines.TryTake(operation, out _));

        // The compiled list holds one release, so no client route target exists in production.
        Assert.AreEqual(1, ChocolateyReleaseList.Listed.Releases.Count);
        ReviewedUpdateOperation production = ChocolateyClientUpdateCommandTests.Operation(ChocolateyReleaseList.Listed.Releases[0].Version, "2.8.0");
        Assert.IsFalse(ChocolateyDesktopObservation.TryGetClientRouteTarget(production, ChocolateyReleaseList.Listed, out _, out _));
    }

    private static OperationOwnership Owner() =>
        new(ChocolateyClientUpdatePostcheckTests.User, OperationGateContract.NamePrefix + ChocolateyClientUpdatePostcheckTests.User, false, reconciled: true);
}
