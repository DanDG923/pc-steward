using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, TM-03/04/07/13; Chocolatey Confirm design §2 (C1, C2, C4, C6, C8), §3 item 2 and §5
/// items 2–4: the one-use review session of the Chocolatey client self-update, over the real reviewed dispatch workflow and the
/// real SQLite store (built with the production helper version grammar). The F′ evidence source, the dispatcher and the client
/// verifier are fakes; the composed client-only dispatcher is <c>ClientRouteOnlyDispatcherTests</c> (Security.FixtureTests).
/// Every row runs under both admission values through the test-only factory, so the locked and the enabled builds are both pinned.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateReviewSessionTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Source = "2.7.5";
    private const string Target = "2.8.0";
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('C', 64);
    private static readonly string[] Listed = [Source, Target];

    [TestMethod]
    public async Task AnAdmittedSessionFreezesTheExactClientShapeAndRunsTheWorkflowOnce()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.NotPrepared, session.Stage);
        Assert.IsNull(session.Operation);
        Assert.IsNull(session.ExpiresAt);
        Assert.IsFalse(session.IsReviewLive);

        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None));
        Assert.AreEqual(1, fixture.Observations);
        Assert.AreEqual(Source, fixture.LastObservedRelease, "the review observes exactly the verified release");
        ReviewedUpdateOperation frozen = session.Operation!;
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, frozen.RouteContract);
        Assert.AreEqual(ReviewedProviderGuardContract.ChocolateyClientSelfUpdate, frozen.ProviderGuardContract);
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(frozen));
        Assert.AreEqual(ReviewedUpdateOperation.ChocolateyClientPackageId, frozen.Selection.PackageId);
        Assert.AreEqual(Source, frozen.Selection.InstalledVersion);
        Assert.AreEqual(Target, frozen.Selection.CandidateVersion);
        Assert.AreEqual(UpdateProvider.Chocolatey, frozen.Selection.Provider);
        Assert.AreEqual(UpdateSource.ChocolateyCommunity, frozen.Selection.Source);
        Assert.AreEqual(UpdateScope.Machine, frozen.Selection.Scope);
        Assert.AreEqual(ChocolateyClientUpdateReviewSession.StableProductKey, frozen.Selection.StableProductKey);
        Assert.AreEqual(ChocolateyClientUpdateReviewSession.MappingEvidenceVersion, frozen.Selection.MappingEvidenceVersion);
        Assert.AreEqual(ChocolateyClientUpdateReviewSession.NativeInstanceId, frozen.NativeInstanceId);
        Assert.AreEqual(ChocolateyClientUpdateReviewSession.InstallationContextId, frozen.InstallationContextId);
        Assert.AreEqual(User, frozen.RequestingUserSid);
        Assert.AreEqual(Evidence, frozen.EvidenceId);
        Assert.AreEqual(ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), frozen.Mode);
        Assert.AreEqual(fixture.Clock.Now, frozen.ReviewedAt);
        Assert.AreEqual(frozen.ReviewedAt + TimeSpan.FromMinutes(30), session.ExpiresAt);
        Assert.IsTrue(session.IsReviewLive);
        Assert.AreEqual(1, fixture.Saves, "the frozen operation is saved once, before anything is confirmed");
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None), "prepared once");
        Assert.AreEqual(1, fixture.Observations);

        RecordingProgress progress = new();
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(fixture.CheckStartedAt, progress, CancellationToken.None);
        Assert.IsTrue(run.Started);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.None, run.Refusal);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, run.Result!.Disposition, run.Result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Success, run.Result.Outcome, run.Result.Message);
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, run.Result.Route);
        Assert.AreEqual(1, fixture.Dispatcher.Calls);
        Assert.AreSame(frozen, fixture.Dispatcher.LastOperation, "the dispatcher receives exactly the saved, reviewed operation");
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Finished, session.Stage);
        Assert.IsFalse(session.IsReviewLive, "a used review is not live");
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdatePhase.CheckingAgain }, progress.Phases.ToArray());

        // Spent: every later press is refused and nothing runs again.
        ChocolateyClientUpdateRunResult again = await session.ConfirmAsync(fixture.CheckStartedAt, progress, CancellationToken.None);
        Assert.IsFalse(again.Started);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, again.Refusal);
        Assert.AreEqual(1, fixture.Dispatcher.Calls);

        // Live text equals history: the recorded row is the one this session ran.
        IReadOnlyList<ReviewedUpdateHistoryRecord> history = await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None);
        ReviewedUpdateHistoryView recorded = ChocolateyClientUpdateRunPresentation.RecordedView(history, frozen.JobId)!;
        Assert.IsNotNull(recorded);
        ChocolateyClientUpdateRunView view = ChocolateyClientUpdateRunPresentation.Present(run, recorded);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.SucceededHeading, view.Heading);
        Assert.IsTrue(view.Succeeded);
        Assert.AreEqual(recorded.Status, view.Lines[0]);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.Lines[1]);
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(history, fixture.CheckStartedAt), "a verified success never blocks");
    }

    [TestMethod]
    public async Task ALockedSessionNeverObservesSavesOrRuns()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyClientUpdateReviewSession session = fixture.Session(admitted: false);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(Source, CancellationToken.None));
        Assert.AreEqual(0, fixture.Observations);
        Assert.AreEqual(0, fixture.Saves);
        Assert.IsNull(session.Operation);
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, run.Refusal);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
        Assert.AreEqual(0, (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task ThePublicConstructorFollowsTheCompiledAdmission()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyClientUpdateReviewSession session = new(fixture.ObserveAsync, fixture.SaveAsync, fixture.ReadHistoryAsync, fixture.Workflow(),
            new ChocolateyClientUpdatePhaseSignal(), () => ReviewedRouteSplitTokenState.SplitTokenLimited, Listed, fixture.Clock);
        ChocolateyClientUpdateReviewStage expected = ChocolateyClientUpdateAdmission.IsOwnerApproved
            ? ChocolateyClientUpdateReviewStage.Ready
            : ChocolateyClientUpdateReviewStage.PrepareFailed;
        Assert.AreEqual(expected, await session.PrepareAsync(Source, CancellationToken.None));
        Assert.AreEqual(ChocolateyClientUpdateAdmission.IsOwnerApproved ? 1 : 0, fixture.Observations);
    }

    [TestMethod]
    [DataRow("no-evidence")]
    [DataRow("source")]
    [DataRow("target")]
    [DataRow("stale")]
    [DataRow("future")]
    [DataRow("evidence-id")]
    [DataRow("unlisted")]
    [DataRow("newest")]
    [DataRow("malformed")]
    [DataRow("observer-fault")]
    [DataRow("save-fault")]
    [DataRow("other-saved")]
    [DataRow("cancelled")]
    public async Task PreparationRefusesEveryMismatchAndStartsNothing(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        string verified = change switch { "unlisted" => "2.6.0", "newest" => Target, "malformed" => "2.7.5; rm", _ => Source };
        fixture.Evidence = change switch
        {
            "no-evidence" => null,
            "source" => fixture.Evidence! with { SourceVersion = "2.6.0" },
            "target" => fixture.Evidence! with { TargetVersion = "2.9.0" },
            "stale" => fixture.Evidence! with { ObservedAt = fixture.Clock.Now - TimeSpan.FromMinutes(3) },
            "future" => fixture.Evidence! with { ObservedAt = fixture.Clock.Now + TimeSpan.FromSeconds(1) },
            "evidence-id" => fixture.Evidence! with { ProfileEvidenceId = "review:1" },
            _ => fixture.Evidence,
        };
        fixture.ObserverFault = change == "observer-fault";
        fixture.SaveFault = change == "save-fault";
        fixture.SaveOther = change == "other-saved";
        using CancellationTokenSource cancel = new();
        if (change == "cancelled") cancel.Cancel();

        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(verified, cancel.Token));
        Assert.IsNull(session.Operation);
        Assert.IsFalse(session.IsReviewLive);
        if (change is "unlisted" or "newest" or "malformed") Assert.AreEqual(0, fixture.Observations, "no offer: nothing is observed");
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(Source, CancellationToken.None), "a failed session stays failed");
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, run.Refusal);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
        Assert.AreEqual(0, (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Count, "nothing was claimed");
    }

    [TestMethod]
    [DataRow("check-too-old", ChocolateyClientUpdateRefusal.CheckTooOld)]
    [DataRow("check-in-future", ChocolateyClientUpdateRefusal.CheckTooOld)]
    [DataRow("no-split-token", ChocolateyClientUpdateRefusal.SplitTokenNotLimited)]
    [DataRow("probe-fault", ChocolateyClientUpdateRefusal.SplitTokenNotLimited)]
    [DataRow("earlier-unverified", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("history-unreadable", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("history-fault", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("review-expired", ChocolateyClientUpdateRefusal.ReviewExpired)]
    [DataRow("cancelled", ChocolateyClientUpdateRefusal.ReviewExpired)]
    public async Task EveryPressTimeConditionIsCheckedAgainAndARefusalSpendsTheSession(string change, ChocolateyClientUpdateRefusal expected)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None));
        DateTimeOffset checkStartedAt = fixture.CheckStartedAt;
        switch (change)
        {
            case "check-too-old": checkStartedAt = fixture.Clock.Now - ChocolateyClientUpdateReviewSession.MaximumCheckAge; break;
            case "check-in-future": checkStartedAt = fixture.Clock.Now + TimeSpan.FromSeconds(1); break;
            case "no-split-token": fixture.SplitToken = ReviewedRouteSplitTokenState.NoSplitToken; break;
            case "probe-fault": fixture.ProbeFault = true; break;
            case "earlier-unverified": fixture.History = [Record(UpdateDispatchOutcome.Unverified, UpdateDispatchSettlementState.OwnedWorkStopped, fixture.Clock.Now)]; break;
            case "history-unreadable": fixture.HistoryUnreadable = true; break;
            case "history-fault": fixture.HistoryFault = true; break;
            case "review-expired": fixture.Clock.Now += TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1); checkStartedAt = fixture.Clock.Now - TimeSpan.FromMinutes(1); break;
        }
        using CancellationTokenSource cancel = new();
        if (change == "cancelled") cancel.Cancel();

        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(checkStartedAt, null, cancel.Token);
        Assert.IsFalse(run.Started);
        Assert.AreEqual(expected, run.Refusal);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Refused, session.Stage);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(session.Operation!.JobId, CancellationToken.None), "nothing was claimed");
        ChocolateyClientUpdateRunView view = ChocolateyClientUpdateRunPresentation.Present(run, null);
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.NotStartedHeading, view.Heading);
        StringAssert.StartsWith(view.Lines.Single(), ChocolateyClientUpdateRunPresentation.NothingStartedPrefix);
        Assert.IsFalse(view.Succeeded);

        // A refusal spends the session even once the condition clears: a new attempt needs a new review.
        fixture.SplitToken = ReviewedRouteSplitTokenState.SplitTokenLimited;
        fixture.ProbeFault = fixture.HistoryUnreadable = fixture.HistoryFault = false;
        fixture.History = [];
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, (await session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
    }

    [TestMethod]
    public async Task TheCheckIsFreshForJustUnderTenMinutes()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(TimeSpan.FromMinutes(10), ChocolateyClientUpdateReviewSession.MaximumCheckAge, "owner decision 1");
        Assert.IsTrue(session.IsCheckFresh(fixture.Clock.Now));
        Assert.IsTrue(session.IsCheckFresh(fixture.Clock.Now - TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(1)));
        Assert.IsFalse(session.IsCheckFresh(fixture.Clock.Now - TimeSpan.FromMinutes(10)));
        Assert.IsFalse(session.IsCheckFresh(fixture.Clock.Now + TimeSpan.FromTicks(1)), "a check in the future is not fresh");

        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None));
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(fixture.Clock.Now - TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1), null, CancellationToken.None);
        Assert.IsTrue(run.Started, run.Refusal.ToString());
    }

    [TestMethod]
    public async Task AnEarlierClientUpdateRecordedBeforeTheCheckStartedDoesNotBlock()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.History = [Record(UpdateDispatchOutcome.Unverified, UpdateDispatchSettlementState.OwnedWorkStopped, fixture.CheckStartedAt - TimeSpan.FromSeconds(1))];
        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None));
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None);
        Assert.IsTrue(run.Started, "a verified check started after the earlier result clears it (design §2 R3)");
    }

    [TestMethod]
    public async Task ASecondPressWhileTheFirstRunsIsRefusedAndOnlyOneRunHappens()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Dispatcher.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None));

        Task<ChocolateyClientUpdateRunResult> first = session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None);
        await fixture.Dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Running, session.Stage);
        ChocolateyClientUpdateRunResult second = await session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, second.Refusal);
        Assert.IsFalse(second.Started);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Running, session.Stage, "the refused press leaves the run alone");

        fixture.Dispatcher.Release.SetResult();
        ChocolateyClientUpdateRunResult run = await first.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsTrue(run.Started);
        Assert.AreEqual(1, fixture.Dispatcher.Calls);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Finished, session.Stage);
    }

    [TestMethod]
    public async Task ANotVerifiedRunIsRecordedAndBlocksTheNextUpdateUntilAFreshCheck()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Verified = false;
        ChocolateyClientUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Source, CancellationToken.None));
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(fixture.CheckStartedAt, null, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, run.Result!.Outcome, run.Result.Message);

        IReadOnlyList<ReviewedUpdateHistoryRecord> history = await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None);
        ChocolateyClientUpdateRunView view = ChocolateyClientUpdateRunPresentation.Present(run, ChocolateyClientUpdateRunPresentation.RecordedView(history, session.Operation!.JobId));
        Assert.AreEqual(ChocolateyClientUpdateRunPresentation.NotVerifiedHeading, view.Heading);
        Assert.IsFalse(view.Succeeded);
        // Owner decision 2 (C6): the next review is blocked until a verified check that started after this result.
        Assert.IsTrue(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(history, fixture.CheckStartedAt));
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(history, fixture.Clock.Now + TimeSpan.FromSeconds(1)));
    }

    private static ReviewedUpdateHistoryRecord Record(UpdateDispatchOutcome outcome, UpdateDispatchSettlementState settlement, DateTimeOffset occurredAt)
    {
        ReviewedUpdateOperation operation = ClientOperation(occurredAt - TimeSpan.FromMinutes(2));
        return new(operation.JobId, operation, UpdateDispatchState.Final, occurredAt - TimeSpan.FromMinutes(1),
            new ReviewedUpdateTerminalRecord(outcome, occurredAt, 0, settlement, "fixture"), true);
    }

    private static ReviewedUpdateOperation ClientOperation(DateTimeOffset reviewedAt)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, ReviewedUpdateOperation.ChocolateyClientPackageId, Source, Target, UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-client-v1")).Confirm(reviewedAt);
        return new(job, User, "choco:chocolatey", "machine:chocolatey", Evidence, reviewedAt - TimeSpan.FromSeconds(5),
            UpdateRouteContract.ChocolateyClientSelfUpdateV1, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateDispatchObservation Settled() =>
        new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, "launch-started", Task.CompletedTask, true, true, true,
            false, false, HelperCheck: RecordedHelperCandidateCheck.NotObserved,
            ClientRunFault: ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.None, false));

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private sealed class RecordingProgress : IProgress<ChocolateyClientUpdatePhase>
    {
        private readonly object sync = new();
        private readonly List<ChocolateyClientUpdatePhase> phases = [];
        internal IReadOnlyList<ChocolateyClientUpdatePhase> Phases { get { lock (sync) return phases.ToArray(); } }
        public void Report(ChocolateyClientUpdatePhase value) { lock (sync) phases.Add(value); }
    }

    private sealed class ClientDispatcher : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        internal ReviewedUpdateOperation? LastOperation;
        internal TaskCompletionSource? Release;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(ReviewedUpdateOperation operation) => operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1;
        public async Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            LastOperation = ticket.Operation;
            Entered.TrySetResult();
            if (Release is { } release) await release.Task.ConfigureAwait(false);
            return Settled();
        }
    }

    private sealed class FreshEvidence(TimeProvider clock) : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult<FreshUpdateDispatchEvidence?>(new(operation.Selection, operation.RequestingUserSid, operation.NativeInstanceId,
                operation.InstallationContextId, operation.EvidenceId, clock.GetUtcNow(), operation.RouteContract, operation.Mode, true, true));
    }

    private sealed class ClientVerifier(Fixture fixture) : IReviewedUpdateVerifier, IReviewedClientUpdateVerifier
    {
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            throw new AssertFailedException("the client route never uses the package verifier");
        public Task<PostUpdateInventoryResult> VerifyClientAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
            ReviewedClientRunFault runFault, CancellationToken cancellationToken) =>
            Task.FromResult(new PostUpdateInventoryResult(fixture.Verified, false));
    }

    /// <summary>A wall clock whose monotonic timestamp moves with it, so the review lifetime can be expired.</summary>
    internal sealed class SteppedClock : TimeProvider
    {
        internal DateTimeOffset Now = Start;
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Now.UtcTicks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-client-session-{Guid.NewGuid():N}.db");

        private Fixture()
        {
            Store = new(path, new ChocolateyHelperVersionGrammar());
            Dispatcher = new();
            Evidence = new(Source, Target, User, ChocolateyClientUpdateReviewSessionTests.Evidence, Clock.Now - TimeSpan.FromSeconds(5));
        }

        internal SqliteDatabase Store { get; }
        internal SteppedClock Clock { get; } = new();
        internal ClientDispatcher Dispatcher { get; }
        internal DateTimeOffset CheckStartedAt => Clock.Now - TimeSpan.FromMinutes(1);
        internal ChocolateyClientReviewEvidence? Evidence;
        internal bool ObserverFault, SaveFault, SaveOther, ProbeFault, HistoryUnreadable, HistoryFault, Verified = true;
        internal ReviewedRouteSplitTokenState SplitToken = ReviewedRouteSplitTokenState.SplitTokenLimited;
        internal IReadOnlyList<ReviewedUpdateHistoryRecord> History = [];
        internal int Observations, Saves;
        internal string? LastObservedRelease;

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal ChocolateyClientUpdateReviewSession Session(bool admitted = true) =>
            ChocolateyClientUpdateReviewSession.CreateWithAdmissionForTests(admitted, ObserveAsync, SaveAsync, ReadHistoryAsync, Workflow(),
                new ChocolateyClientUpdatePhaseSignal(), Probe, Listed, Clock);

        internal ReviewedUpdateDispatchWorkflow Workflow() =>
            new((work, _, ct) => work(Owner(), ct), new FreshEvidence(Clock), Dispatcher, new ClientVerifier(this), Store, Clock);

        internal Task<ChocolateyClientReviewEvidence?> ObserveAsync(string release, CancellationToken cancellationToken)
        {
            Observations++;
            LastObservedRelease = release;
            return ObserverFault ? Task.FromException<ChocolateyClientReviewEvidence?>(new IOException("observation failed")) : Task.FromResult(Evidence);
        }

        internal async Task<ReviewedUpdateDraft> SaveAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
        {
            Saves++;
            if (SaveFault) throw new IOException("disk full");
            if (!SaveOther) return await Store.SaveReviewedOperationAsync(operation, cancellationToken);
            // A store that hands back another (valid) client operation than the one frozen: the session must refuse it.
            return await Store.SaveReviewedOperationAsync(ClientOperation(operation.ReviewedAt), cancellationToken);
        }

        internal Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?> ReadHistoryAsync(CancellationToken cancellationToken)
        {
            if (HistoryFault) return Task.FromException<IReadOnlyList<ReviewedUpdateHistoryRecord>?>(new IOException("history unreadable"));
            return Task.FromResult(HistoryUnreadable ? null : History);
        }

        private ReviewedRouteSplitTokenState Probe() => ProbeFault ? throw new InvalidOperationException("probe failed") : SplitToken;

        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
