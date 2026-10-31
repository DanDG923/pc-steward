using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01/08/09/14; TM-03/04/06/07/13: composed review → claim → one dispatch → settlement → final, with fakes only.</summary>
[TestClass]
public sealed class ReviewedUpdateDispatchWorkflowTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(true, false, UpdateDispatchOutcome.Success)]
    [DataRow(false, true, UpdateDispatchOutcome.RestartRequired)]
    [DataRow(false, false, UpdateDispatchOutcome.Unverified)]
    public async Task SettledZeroExitIsClassifiedOnlyByPostUpdateVerification(bool expected, bool restart, UpdateDispatchOutcome outcome)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        FakeVerifier verifier = new(new(expected, restart));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher, verifier: verifier).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.AreEqual(1, dispatcher.Calls);
        Assert.AreSame(draft.Operation, dispatcher.LastTicket!.Operation);
        Assert.AreEqual(draft.Operation.JobId, dispatcher.LastTicket.Claim.Draft.Operation.JobId);
        Assert.AreEqual(1, verifier.Calls);
        Assert.AreEqual(draft.Operation.Mode, result.Mode);
        Assert.AreEqual(UpdateRouteContract.WinGetUserExactV3, result.Route);
        Assert.AreEqual(ReviewedProviderGuardContract.LegacyUnqualified, result.GuardContract);
        Assert.IsNull(result.Disclosure);
    }

    [TestMethod]
    public async Task UnsupportedRouteNeverEntersTheGateOrClaims()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0), supports: false);
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, result.Disposition);
        Assert.AreEqual(0, fixture.GateCalls);
        Assert.AreEqual(0, dispatcher.Calls);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("mode")]
    [DataRow("route")]
    [DataRow("selection")]
    [DataRow("native")]
    [DataRow("not-actionable")]
    [DataRow("stale")]
    public async Task ChangedFreshEvidenceIsRejectedBeforeClaimAndSpendsTheApproval(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        LiveApprovedUpdate approval = fixture.Confirm(draft);
        ReviewedUpdateDispatchWorkflow workflow = fixture.Workflow(dispatcher, evidence: new FakeEvidence(op => Fresh(op, change)));
        ReviewedUpdateDispatchResult first = await workflow.RunAsync(approval, CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, first.Disposition);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        ReviewedUpdateDispatchResult retry = await fixture.Workflow(dispatcher).RunAsync(approval, CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, retry.Disposition);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task MissingFreshEvidenceDoesNotClaim()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher, evidence: new FakeEvidence(_ => null)).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, result.Disposition);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task ExpiredReviewNeverDispatches()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        LiveApprovedUpdate approval = fixture.Confirm(draft);
        fixture.Clock.Now = ReviewedAt.AddMinutes(31);
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(approval, CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, result.Disposition);
        Assert.AreEqual(0, fixture.GateCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task BusyOrUnreconciledGateAndCancellationAreNotStarted()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        ReviewedUpdateDispatchResult busy = await fixture.Workflow(dispatcher, gate: (_, _, _) => throw new InvalidOperationException("Another PC Steward data operation is active."))
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, busy.Disposition);
        StringAssert.Contains(busy.Message, "Another PC Steward data operation is active.");

        ReviewedUpdateDraft second = await fixture.SaveAsync();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(fixture.Confirm(second), cancelled.Token);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, result.Disposition);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task ReviewDeadlineBoundsTheGateWait()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        DateTimeOffset? observed = null;
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0)),
            gate: (work, deadline, ct) => { observed = deadline; return work(Owner(), ct); }).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedAt.AddMinutes(30), observed);
        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome);
    }

    [TestMethod]
    public async Task LostPermitAfterCommittedClaimRetainsTheBarrierWithoutDispatch()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        AfterClaimStore store = new(fixture.Store, () => fixture.Clock.Now = ReviewedAt.AddMinutes(31));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher, store: store).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStartedClaimRetained, result.Disposition);
        Assert.AreEqual(0, dispatcher.Calls);
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("throws")]
    [DataRow("unknown")]
    [DataRow("pending")]
    [DataRow("faulted")]
    [DataRow("no-exit")]
    [DataRow("io")]
    [DataRow("eof")]
    [DataRow("kill-failed")]
    [DataRow("uncertain")]
    [DataRow("not-started-but-uncertain")]
    public async Task UnresolvedInvocationOrOwnedWorkIsUncertainAndKeepsTheBarrier(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeDispatcher dispatcher = new(_ => change switch
        {
            "throws" => throw new IOException("transport lost"),
            "unknown" => new(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Completed, 0, "lost", Task.CompletedTask, true, true, true, false, false),
            "pending" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { OwnedRoot = pending.Task },
            "faulted" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { OwnedRoot = Task.FromException(new IOException("exit observation failed")) },
            "no-exit" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { DirectExitObserved = false },
            "io" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { OwnedIoComplete = false },
            "eof" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { PipeEndOfFile = false },
            "kill-failed" => Settled(ReviewedUpdateProviderStatus.TimedOut, null) with { TerminationFailed = true },
            "uncertain" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { ExecutionUncertain = true },
            _ => ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, "refused") with { ExecutionUncertain = true }
        });
        FakeVerifier verifier = new(new(true, false));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher, verifier: verifier).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Uncertain, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Uncertain, result.Outcome);
        Assert.AreEqual(0, verifier.Calls);
        Assert.AreEqual(UpdateDispatchState.Uncertain, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.IsFalse(pending.Task.IsCompleted, "The workflow must not await an unsettled owned root.");
        pending.TrySetResult();
        Assert.AreEqual(UpdateDispatchState.Uncertain, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(ReviewedUpdateProviderStatus.ProviderFailed, 1, UpdateDispatchOutcome.Failed)]
    [DataRow(ReviewedUpdateProviderStatus.Completed, 3010, UpdateDispatchOutcome.Failed)]
    [DataRow(ReviewedUpdateProviderStatus.TimedOut, null, UpdateDispatchOutcome.Failed)]
    [DataRow(ReviewedUpdateProviderStatus.Cancelled, null, UpdateDispatchOutcome.Unverified)]
    public async Task SettledNonSuccessIsNeverVerifiedSuccess(ReviewedUpdateProviderStatus status, int? exit, UpdateDispatchOutcome outcome)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeVerifier verifier = new(new(true, false));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(status, exit)), verifier: verifier)
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(0, verifier.Calls);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow(ReviewedUpdateProviderStatus.Refused, UpdateDispatchOutcome.Failed)]
    [DataRow(ReviewedUpdateProviderStatus.ElevationDenied, UpdateDispatchOutcome.Failed)]
    [DataRow(ReviewedUpdateProviderStatus.Cancelled, UpdateDispatchOutcome.Cancelled)]
    public async Task ProvenNotStartedRefusalIsFinalWithoutVerification(ReviewedUpdateProviderStatus status, UpdateDispatchOutcome outcome)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeVerifier verifier = new(new(true, false));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => ReviewedUpdateDispatchObservation.NotStarted(status, "helper-refused-before-update")), verifier: verifier)
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(0, verifier.Calls);
    }

    [TestMethod]
    public async Task VerificationFailureAfterSettlementIsUnverifiedNotSuccess()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0)),
            verifier: new FakeVerifier(null)).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome);
    }

    [TestMethod]
    public async Task FinalWriteFailureIsReportedUncertainAndTheClaimRemains()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0)),
            store: new FailingFinalStore(fixture.Store)).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Uncertain, result.Disposition);
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task OneApprovalDispatchesAtMostOnce()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        LiveApprovedUpdate approval = fixture.Confirm(draft);
        ReviewedUpdateDispatchWorkflow workflow = fixture.Workflow(dispatcher);
        Assert.AreEqual(UpdateDispatchOutcome.Success, (await workflow.RunAsync(approval, CancellationToken.None)).Outcome);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, (await workflow.RunAsync(approval, CancellationToken.None)).Disposition);
        Assert.AreEqual(1, dispatcher.Calls);
        Assert.IsEmpty(typeof(ReviewedUpdateDispatchTicket).GetConstructors());
    }

    private static ReviewedUpdateDispatchObservation Settled(ReviewedUpdateProviderStatus status, int? exit) =>
        new(ReviewedUpdateInvocation.Started, status, exit, "fixture", Task.CompletedTask, true, true, true, false, false);

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private static FreshUpdateDispatchEvidence? Fresh(ReviewedUpdateOperation op, string? change = null) => new(
        change == "selection" ? new(op.Selection.StableProductKey, op.Selection.Provider, op.Selection.Source, op.Selection.PackageId, "0.9", "1.1", op.Selection.Scope, op.Selection.Architecture, op.Selection.Channel, op.Selection.MappingEvidenceVersion) : op.Selection,
        op.RequestingUserSid, change == "native" ? "other:1" : op.NativeInstanceId, op.InstallationContextId, "fresh:1",
        change == "stale" ? ReviewedAt.AddMinutes(-3) : ReviewedAt.AddMinutes(1),
        change == "route" ? UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 : op.RouteContract,
        change == "mode" ? ReviewedInstallerMode.Selected(InstallerMode.Interactive) : op.Mode, change != "not-actionable", true);

    private sealed class FakeDispatcher(Func<ReviewedUpdateDispatchTicket, ReviewedUpdateDispatchObservation> dispatch, bool supports = true) : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        internal ReviewedUpdateDispatchTicket? LastTicket;
        public bool Supports(ReviewedUpdateOperation operation) => supports;
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Calls++;
            LastTicket = ticket;
            return Task.FromResult(dispatch(ticket));
        }
    }

    private sealed class FakeEvidence(Func<ReviewedUpdateOperation, FreshUpdateDispatchEvidence?> observe) : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult(observe(operation));
    }

    private sealed class FakeVerifier(PostUpdateInventoryResult? result) : IReviewedUpdateVerifier
    {
        internal int Calls;
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken)
        {
            Calls++;
            return result is null ? Task.FromException<PostUpdateInventoryResult>(new IOException("re-inventory failed")) : Task.FromResult(result);
        }
    }

    private class DelegatingStore(IUpdateDispatchStore inner) : IUpdateDispatchStore
    {
        public virtual Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership, DateTimeOffset claimedAt, CancellationToken cancellationToken) =>
            inner.TryClaimAsync(draft, evidence, ownership, claimedAt, cancellationToken);
        public virtual Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal, UpdateDispatchSettlement settlement, CancellationToken cancellationToken) =>
            inner.RecordFinalAsync(claim, ownership, terminal, settlement, cancellationToken);
        public Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now, CancellationToken cancellationToken) =>
            inner.RecoverUnfinishedDispatchesAsync(ownership, now, cancellationToken);
        public Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) => inner.GetDispatchStateAsync(jobId, cancellationToken);
        public Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken) => inner.HasUnresolvedDispatchesAsync(ownership, cancellationToken);
    }

    private sealed class AfterClaimStore(IUpdateDispatchStore inner, Action afterCommit) : DelegatingStore(inner)
    {
        public override async Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership, DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            UpdateDispatchClaim? claim = await base.TryClaimAsync(draft, evidence, ownership, claimedAt, cancellationToken);
            afterCommit();
            return claim;
        }
    }

    private sealed class FailingFinalStore(IUpdateDispatchStore inner) : DelegatingStore(inner)
    {
        public override Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal, UpdateDispatchSettlement settlement, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("disk full"));
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = ReviewedAt.AddMinutes(1);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-task50-dispatch-{Guid.NewGuid():N}.db");
        private Fixture() => Store = new(path);
        internal SqliteDatabase Store { get; }
        internal Clock Clock { get; } = new();
        internal int GateCalls;

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal Task<ReviewedUpdateDraft> SaveAsync()
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
                "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
            return Store.SaveReviewedOperationAsync(new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1),
                UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent)), CancellationToken.None);
        }

        internal LiveApprovedUpdate Confirm(ReviewedUpdateDraft draft) => new ReviewedUpdateReview(draft, Clock).Confirm();

        internal ReviewedUpdateDispatchWorkflow Workflow(IReviewedUpdateRouteDispatcher dispatcher, IReviewedUpdateDispatchEvidenceSource? evidence = null,
            IReviewedUpdateVerifier? verifier = null, IUpdateDispatchStore? store = null, ReviewedUpdateGateRunner? gate = null) =>
            new(gate ?? ((work, _, ct) => { ct.ThrowIfCancellationRequested(); GateCalls++; return work(Owner(), ct); }),
                evidence ?? new FakeEvidence(op => Fresh(op)), dispatcher, verifier ?? new FakeVerifier(new(true, false)), store ?? Store, Clock);

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
