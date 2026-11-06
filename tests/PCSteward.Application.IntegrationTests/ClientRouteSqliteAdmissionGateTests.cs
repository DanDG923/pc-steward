using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, TM-03/04/07/13; ADR-031 design §8.6/§8.7 admission gate 1, durable-store variant
/// (independent review M2): the <c>ChocolateyClientSelfUpdateV1</c> workflow through the real SQLite store
/// (<see cref="SqliteDatabase"/> as <see cref="IUpdateDispatchStore"/>, built with the production helper version grammar) and
/// read back through its real history reader (<see cref="IReviewedUpdateHistoryReader"/>). Exercised for this route: the
/// canonical save and restore of a client operation, the store's own admission site (<c>evidence.Matches</c> →
/// <c>HasSupportedMode</c> before the claim CAS), the claim CAS, <c>RecordFinalAsync</c> with its settlement invariants and the
/// strict history row checks. Since the Task 50 activation (ADR-031 owner decision 7) the production review gate admits the
/// exact client shape, so no test scope is needed; the store still refuses a claim whose fresh evidence does not match the
/// frozen profile evidence. The dispatcher,
/// fresh evidence and verifier are fakes here (the helper endpoint, interpreter, F′ producer and client verifier run in the
/// portable gate, <c>ClientRouteWorkflowAdmissionGateTests</c>); each fake observation has the post-review-M1 shape of an
/// accepted client reply (a run-fault value and no helper candidate check).
/// </summary>
[TestClass]
public sealed class ClientRouteSqliteAdmissionGateTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Source = "2.7.5";
    private const string Target = "2.8.0";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('C', 64);

    [TestMethod]
    public async Task TheStoreAdmitsOnlyMatchingFreshEvidenceAndRoundTripsTheClientOperation()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, draft.Operation.RouteContract);
        Assert.AreEqual(fixture.Operation.Digest, draft.Operation.Digest, "the canonical save restores the same client operation");
        Assert.IsTrue(fixture.Operation.HasSameIdentity(draft.Operation));

        // The store's own admission site: fresh evidence whose profile digest differs from the frozen one is refused before
        // the claim CAS, so nothing is claimed (activation admits the route shape, never mismatching evidence).
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(draft.Operation), "activation: the exact client shape is admitted");
        FreshUpdateDispatchEvidence stale = Fresh(draft.Operation, ReviewedUpdateOperation.ProfileEvidencePrefix + new string('D', 64));
        Assert.IsNull(await fixture.Store.TryClaimAsync(draft, stale, Owner(), fixture.Clock.GetUtcNow(), CancellationToken.None));
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.AreEqual(0, (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Count);

        FreshUpdateDispatchEvidence evidence = Fresh(draft.Operation);
        UpdateDispatchClaim? claim = await fixture.Store.TryClaimAsync(draft, evidence, Owner(), fixture.Clock.GetUtcNow(), CancellationToken.None);
        Assert.IsNotNull(claim, "matching fresh evidence commits the claim");
        Assert.AreEqual(draft.Operation.Digest, claim.Draft.Operation.Digest);
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        ReviewedUpdateHistoryRecord claimed = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
        Assert.IsTrue(claimed.IntegrityVerified, "the claimed client row restores strictly");
        Assert.AreEqual(draft.Operation.Digest, claimed.Operation!.Digest);
        Assert.IsTrue(await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
    }

    [TestMethod]
    public async Task NoneWithTheExactTargetIsSuccessThroughTheDurableStoreAndHistory()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        FakeVerifier verifier = new(new(true, false));
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryRecord record, ReviewedUpdateHistoryView view) =
            await fixture.RunAsync(Settled(ReviewedClientWatchdogStop.None, false), verifier);

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome, result.Message);
        Assert.AreEqual(ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage, result.Message);
        Assert.AreEqual(1, verifier.ClientCalls);
        Assert.AreEqual(UpdateDispatchState.Final, record.State);
        Assert.AreEqual(UpdateDispatchSettlementState.OwnedWorkStopped, record.Terminal!.Settlement);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.VerificationDetail);
        Assert.IsFalse(view.BlocksOtherUpdates);
    }

    [TestMethod]
    public async Task AWatchdogStopIsUnverifiedWithItsRecoveryTextThroughTheDurableStoreAndHistory()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        FakeVerifier verifier = new(new(true, false));
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryRecord record, ReviewedUpdateHistoryView view) =
            await fixture.RunAsync(Settled(ReviewedClientWatchdogStop.SetupOverrideDuringRun, false), verifier);

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage, result.Message);
        Assert.AreEqual(0, verifier.ClientCalls, "a reported fault is never verified");
        Assert.AreEqual(UpdateDispatchState.Final, record.State);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage, view.VerificationDetail);
        StringAssert.Contains(view.VerificationDetail, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
        Assert.IsFalse(view.BlocksOtherUpdates);
    }

    [TestMethod]
    public async Task ALostLaunchIsUncertainWithTheCautionAndKeepsTheDurableBarrier()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        FakeVerifier verifier = new(new(true, false));
        // A rejected helper reply: Unknown, unsettled, no run-fault value.
        ReviewedUpdateDispatchObservation lost = new(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Failed, null,
            "helper-response-rejected", Task.CompletedTask, false, false, false, false, true, HelperCheck: RecordedHelperCandidateCheck.NotObserved);
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryRecord record, ReviewedUpdateHistoryView view) = await fixture.RunAsync(lost, verifier);

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Uncertain, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Uncertain, result.Outcome, result.Message);
        StringAssert.EndsWith(result.Message, " " + ChocolateyClientUpdatePresentation.LostTrackCautionMessage);
        Assert.AreEqual(0, verifier.ClientCalls);
        Assert.AreEqual(UpdateDispatchState.Uncertain, record.State);
        Assert.AreEqual(UpdateDispatchSettlementState.OwnedWorkUnsettled, record.Terminal!.Settlement);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.LostTrackCautionMessage, view.VerificationDetail);
        Assert.IsTrue(view.BlocksOtherUpdates, "the durable barrier stays until reconciliation");
    }

    private static ReviewedUpdateDispatchObservation Settled(ReviewedClientWatchdogStop stop, bool releaseIncomplete) =>
        new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, "launch-started", Task.CompletedTask, true, true, true,
            false, false, HelperCheck: RecordedHelperCandidateCheck.NotObserved,
            ClientRunFault: ReviewedClientRunFault.FromAcceptedHelperReply(stop, releaseIncomplete));

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static ReviewedUpdateOperation ClientOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, ReviewedUpdateOperation.ChocolateyClientPackageId, Source, Target, UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:chocolatey", "machine:choco", Evidence, ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyClientSelfUpdateV1,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    /// <summary>The F′ evidence shape for the client route: the exact profile identity, observed after review and within the window.</summary>
    private static FreshUpdateDispatchEvidence Fresh(ReviewedUpdateOperation op, string? evidenceId = null) => new(op.Selection, op.RequestingUserSid, op.NativeInstanceId,
        op.InstallationContextId, evidenceId ?? op.EvidenceId, ReviewedAt.AddMinutes(1), op.RouteContract, op.Mode, true, true);

    private sealed class ClientDispatcher(ReviewedUpdateDispatchObservation observation) : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        public bool Supports(ReviewedUpdateOperation operation) => operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1;
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(observation);
        }
    }

    private sealed class FakeEvidence : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult<FreshUpdateDispatchEvidence?>(Fresh(operation));
    }

    /// <summary>The client route verifies only through <see cref="IReviewedClientUpdateVerifier"/>; the plain path must never be used.</summary>
    private sealed class FakeVerifier(PostUpdateInventoryResult result) : IReviewedUpdateVerifier, IReviewedClientUpdateVerifier
    {
        internal int ClientCalls;
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            throw new AssertFailedException("the client route never uses the package verifier");
        public Task<PostUpdateInventoryResult> VerifyClientAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
            ReviewedClientRunFault runFault, CancellationToken cancellationToken)
        {
            ClientCalls++;
            Assert.IsTrue(runFault.IsNone, "only a None run fault reaches verification");
            return Task.FromResult(result);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-client-gate-{Guid.NewGuid():N}.db");
        private Fixture() => Store = new(path, new ChocolateyHelperVersionGrammar());

        /// <summary>The production shape: dispatch store and history reader with the helper version grammar.</summary>
        internal SqliteDatabase Store { get; }
        internal ReviewedUpdateOperation Operation { get; } = ClientOperation();
        internal FixedClock Clock { get; } = new(ReviewedAt.AddMinutes(1));

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal Task<ReviewedUpdateDraft> SaveAsync() => Store.SaveReviewedOperationAsync(Operation, CancellationToken.None);

        /// <summary>
        /// Save, review, confirm and run against the SQLite store (the route is admitted in production), then read the single row back through
        /// the SQLite history reader and present it. Checks the strict row restore and that live text equals the recorded text.
        /// </summary>
        internal async Task<(ReviewedUpdateDispatchResult, ReviewedUpdateHistoryRecord, ReviewedUpdateHistoryView)> RunAsync(
            ReviewedUpdateDispatchObservation observation, FakeVerifier verifier)
        {
            ReviewedUpdateDraft draft = await SaveAsync();
            ClientDispatcher dispatcher = new(observation);
            ReviewedUpdateDispatchWorkflow workflow = new((work, _, ct) => work(Owner(), ct), new FakeEvidence(), dispatcher, verifier, Store, Clock);
            ReviewedUpdateDispatchResult result = await workflow.RunAsync(new ReviewedUpdateReview(draft, Clock).Confirm(), CancellationToken.None);
            Assert.AreEqual(1, dispatcher.Calls, result.Message);
            Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, result.Route);
            Assert.IsNull(result.HelperCheck, "no helper check is recorded for the client route");
            Assert.IsFalse(result.Message.Contains(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, StringComparison.Ordinal), result.Message);

            ReviewedUpdateHistoryRecord record = (await Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            Assert.IsTrue(record.IntegrityVerified, "the SQLite history reader restores the client row strictly");
            Assert.AreEqual(draft.Operation.JobId, record.JobId);
            Assert.AreEqual(draft.Operation.Digest, record.Operation!.Digest, "canonical round trip of the client operation");
            Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, record.Operation.RouteContract);
            Assert.IsNotNull(record.Terminal, "the terminal parsed with the production grammar");
            Assert.AreEqual(result.Outcome, record.Terminal.Outcome);
            Assert.AreEqual(result.Message, record.Terminal.Diagnostic, "live text equals the recorded text");
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, record.Terminal.HelperCheck);
            Assert.AreEqual(record.State, await Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
            ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
            Assert.AreEqual(ChocolateyClientUpdatePresentation.HistoryRouteText, view.Route);
            Assert.AreEqual(view.BlocksOtherUpdates, await Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
            return (result, record, view);
        }

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
