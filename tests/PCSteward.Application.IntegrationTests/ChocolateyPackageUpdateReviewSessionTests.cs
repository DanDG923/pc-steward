using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-HIS-01, TM-03/04/07/13; Chocolatey package Confirm addendum §3 (C1, C2, C4, C6, C8) and §5: the
/// one-use review session of a Chocolatey Community package update, over the real reviewed dispatch workflow and the real SQLite
/// store (built with the production helper version grammar). The intent check, the F′ evidence source, the dispatcher and the
/// verifier are fakes; the composed package-only dispatcher is <c>ChocolateyPackageUpdateCompositionTests</c> (Security.FixtureTests).
/// Rows run under both admission values through the test-only factory, so the locked and the enabled builds are both pinned.
/// </summary>
[TestClass]
public sealed class ChocolateyPackageUpdateReviewSessionTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private const string Package = "ripgrep";
    private const string Installed = "14.1.0";
    private const string Candidate = "15.2.0";
    private static readonly DateTimeOffset Start = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('D', 64);

    [TestMethod]
    public async Task AnAdmittedSessionFreezesTheExactPackageShapeFromTheCheckAndRunsTheWorkflowOnce()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.NotPrepared, session.Stage);
        Assert.IsNull(session.Operation);
        Assert.IsNull(session.Intent);
        Assert.IsNull(session.CheckStartedAt);
        Assert.IsFalse(session.IsCheckFresh);

        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None));
        Assert.AreEqual(1, fixture.Checks);
        Assert.AreEqual(Package, fixture.LastCheckedPackage, "the check asks about exactly the selected package");
        ReviewedUpdateOperation frozen = session.Operation!;
        Assert.AreEqual(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, frozen.RouteContract);
        Assert.AreEqual(ReviewedProviderGuardContract.CommunityScriptRuntimeLimits, frozen.ProviderGuardContract);
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, frozen.CandidateProvenance);
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(frozen));
        Assert.AreEqual(Package, frozen.Selection.PackageId);
        Assert.AreEqual(Installed, frozen.Selection.InstalledVersion);
        Assert.AreEqual(Candidate, frozen.Selection.CandidateVersion);
        Assert.AreEqual(UpdateProvider.Chocolatey, frozen.Selection.Provider);
        Assert.AreEqual(UpdateSource.ChocolateyCommunity, frozen.Selection.Source);
        Assert.AreEqual(UpdateScope.Machine, frozen.Selection.Scope);
        Assert.AreEqual(UpdateChannel.Stable, frozen.Selection.Channel);
        Assert.AreEqual(ReviewedArchitectureBinding.ProviderScriptSelected, frozen.Selection.Architecture.Binding);
        Assert.AreEqual("choco:" + Package, frozen.Selection.StableProductKey);
        Assert.AreEqual(ChocolateyPackageUpdateReviewSession.MappingEvidenceVersion, frozen.Selection.MappingEvidenceVersion);
        Assert.AreEqual("choco:" + Package, frozen.NativeInstanceId);
        Assert.AreEqual(ChocolateyPackageUpdateReviewSession.InstallationContextId, frozen.InstallationContextId);
        Assert.AreEqual(User, frozen.RequestingUserSid);
        Assert.AreEqual(Evidence, frozen.EvidenceId);
        Assert.AreEqual(fixture.Intent!.ObservedAt, frozen.EvidenceObservedAt);
        Assert.AreEqual(ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), frozen.Mode);
        Assert.AreEqual(fixture.Clock.Now, frozen.ReviewedAt);
        Assert.AreEqual(frozen.ReviewedAt + TimeSpan.FromMinutes(30), session.ExpiresAt);
        Assert.AreSame(fixture.Intent, session.Intent);
        Assert.AreEqual(Fixture.CheckStartedAt, session.CheckStartedAt);
        Assert.IsTrue(session.IsReviewLive);
        Assert.IsTrue(session.IsCheckFresh);
        Assert.AreEqual(1, fixture.Saves, "the frozen operation is saved once, before anything is confirmed");
        Assert.IsTrue(ChocolateyReviewDisclosurePresentation.Present(frozen, session.Intent, ReviewedRouteSplitTokenState.SplitTokenLimited).CanConfirm,
            "the Profile V2 disclosure matches the frozen review");
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None), "prepared once");
        Assert.AreEqual(1, fixture.Checks);

        RecordingProgress progress = new();
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(progress, CancellationToken.None);
        Assert.IsTrue(run.Started, run.Refusal.ToString());
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, run.Result!.Disposition, run.Result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Success, run.Result.Outcome, run.Result.Message);
        Assert.AreEqual(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, run.Result.Route);
        Assert.AreEqual(1, fixture.Dispatcher.Calls);
        Assert.AreSame(frozen, fixture.Dispatcher.LastOperation, "the dispatcher receives exactly the saved, reviewed operation");
        Assert.AreEqual(1, fixture.Verifications);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Finished, session.Stage);
        Assert.IsFalse(session.IsReviewLive, "a used review is not live");
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdatePhase.CheckingAgain }, progress.Phases.ToArray());

        ChocolateyClientUpdateRunResult again = await session.ConfirmAsync(progress, CancellationToken.None);
        Assert.IsFalse(again.Started);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, again.Refusal);
        Assert.AreEqual(1, fixture.Dispatcher.Calls);

        // Live text equals history.
        IReadOnlyList<ReviewedUpdateHistoryRecord> history = await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None);
        ReviewedUpdateHistoryView recorded = ChocolateyClientUpdateRunPresentation.RecordedView(history, frozen.JobId)!;
        Assert.IsNotNull(recorded);
        ChocolateyClientUpdateRunView view = ChocolateyPackageUpdateRunPresentation.Present(run, recorded);
        Assert.AreEqual(ChocolateyPackageUpdateRunPresentation.SucceededHeading, view.Heading);
        Assert.IsTrue(view.Succeeded);
        Assert.AreEqual(recorded.Status, view.Lines[0]);
        Assert.IsFalse(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention(history, Fixture.CheckStartedAt), "a verified success never blocks");
    }

    [TestMethod]
    public async Task ALockedSessionNeverChecksSavesOrRuns()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyPackageUpdateReviewSession session = fixture.Session(admitted: false);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(Package, CancellationToken.None));
        Assert.AreEqual(0, fixture.Checks);
        Assert.AreEqual(0, fixture.Saves);
        Assert.IsNull(session.Operation);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, (await session.ConfirmAsync(null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
        Assert.AreEqual(0, (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task ThePublicConstructorFollowsTheCompiledAdmission()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyPackageUpdateReviewSession session = new(fixture.CheckAsync, fixture.SaveAsync, fixture.ReadHistoryAsync, fixture.Workflow(),
            new ChocolateyClientUpdatePhaseSignal(), () => ReviewedRouteSplitTokenState.SplitTokenLimited, fixture.Clock);
        ChocolateyClientUpdateReviewStage expected = ChocolateyPackageUpdateAdmission.IsOwnerApproved
            ? ChocolateyClientUpdateReviewStage.Ready
            : ChocolateyClientUpdateReviewStage.PrepareFailed;
        Assert.AreEqual(expected, await session.PrepareAsync(Package, CancellationToken.None));
        Assert.AreEqual(ChocolateyPackageUpdateAdmission.IsOwnerApproved ? 1 : 0, fixture.Checks);
    }

    [TestMethod]
    [DataRow("chocolatey")]
    [DataRow("Chocolatey")]
    [DataRow("chocolatey.extension")]
    [DataRow("")]
    [DataRow(" ")]
    public async Task AClientIdentityOrAnEmptySelectionIsNeverChecked(string selected)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Intent = fixture.Intent! with { PackageId = selected };
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.IsFalse(ChocolateyPackageUpdateReviewSession.IsSelectablePackage(selected));
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(selected, CancellationToken.None));
        Assert.AreEqual(0, fixture.Checks, "a client identity is never even checked on the package route");
        Assert.AreEqual(0, fixture.Saves);
    }

    [TestMethod]
    [DataRow("no-intent")]
    [DataRow("other-package")]
    [DataRow("package-case")]
    [DataRow("trusted-provenance")]
    [DataRow("resolved-before-check")]
    [DataRow("observed-before-resolved")]
    [DataRow("observed-in-future")]
    [DataRow("resolved-too-long-ago")]
    [DataRow("evidence-id")]
    [DataRow("not-newer")]
    [DataRow("check-fault")]
    [DataRow("save-fault")]
    [DataRow("other-saved")]
    [DataRow("cancelled")]
    public async Task PreparationRefusesEveryMismatchAndStartsNothing(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyReviewIntentFacts intent = fixture.Intent!;
        fixture.Intent = change switch
        {
            "no-intent" => null,
            "other-package" => intent with { PackageId = "fd" },
            "package-case" => intent with { PackageId = "RipGrep" },
            "trusted-provenance" => intent with { CandidateProvenance = ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired },
            "resolved-before-check" => intent with { IntentResolvedAt = Fixture.CheckStartedAt - TimeSpan.FromTicks(1) },
            "observed-before-resolved" => intent with { IntentResolvedAt = Start + TimeSpan.FromSeconds(2), ObservedAt = Start + TimeSpan.FromSeconds(1) },
            "observed-in-future" => intent with { ObservedAt = Start + TimeSpan.FromMinutes(1) },
            "evidence-id" => intent with { ProfileEvidenceId = "review:1" },
            "not-newer" => intent with { CandidateVersion = Installed },
            _ => intent,
        };
        // A check that took longer than the disclosure's two-minute rule allows: the review is never frozen.
        fixture.CheckDuration = change == "resolved-too-long-ago" ? TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5);
        fixture.CheckFault = change == "check-fault";
        fixture.SaveFault = change == "save-fault";
        fixture.SaveOther = change == "other-saved";
        using CancellationTokenSource cancel = new();
        if (change == "cancelled") cancel.Cancel();

        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(Package, cancel.Token));
        Assert.IsNull(session.Operation);
        Assert.IsNull(session.Intent);
        Assert.IsFalse(session.IsReviewLive);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.PrepareFailed, await session.PrepareAsync(Package, CancellationToken.None), "a failed session stays failed");
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, (await session.ConfirmAsync(null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
        Assert.AreEqual(0, (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Count, "nothing was claimed");
    }

    [TestMethod]
    [DataRow("check-too-old", ChocolateyClientUpdateRefusal.CheckTooOld)]
    [DataRow("no-split-token", ChocolateyClientUpdateRefusal.SplitTokenNotLimited)]
    [DataRow("probe-fault", ChocolateyClientUpdateRefusal.SplitTokenNotLimited)]
    [DataRow("earlier-package-unverified", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("earlier-client-unverified", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("history-unreadable", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("history-fault", ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention)]
    [DataRow("review-expired", ChocolateyClientUpdateRefusal.CheckTooOld)]
    [DataRow("cancelled", ChocolateyClientUpdateRefusal.ReviewExpired)]
    public async Task EveryPressTimeConditionIsCheckedAgainAndARefusalSpendsTheSession(string change, ChocolateyClientUpdateRefusal expected)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None));
        switch (change)
        {
            case "check-too-old": fixture.Clock.Now = session.CheckStartedAt!.Value + ChocolateyPackageUpdateReviewSession.MaximumCheckAge; break;
            case "no-split-token": fixture.SplitToken = ReviewedRouteSplitTokenState.NoSplitToken; break;
            case "probe-fault": fixture.ProbeFault = true; break;
            case "earlier-package-unverified": fixture.History = [Record(PackageOperation, fixture.Clock.Now)]; break;
            case "earlier-client-unverified": fixture.History = [Record(ClientOperation, fixture.Clock.Now)]; break;
            case "history-unreadable": fixture.HistoryUnreadable = true; break;
            case "history-fault": fixture.HistoryFault = true; break;
            // The 30-minute review always outlives the 10-minute check, so the check's age refuses first.
            case "review-expired": fixture.Clock.Now += TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1); break;
        }
        using CancellationTokenSource cancel = new();
        if (change == "cancelled") cancel.Cancel();

        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(null, cancel.Token);
        Assert.IsFalse(run.Started);
        Assert.AreEqual(expected, run.Refusal);
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Refused, session.Stage);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(session.Operation!.JobId, CancellationToken.None), "nothing was claimed");
        ChocolateyClientUpdateRunView view = ChocolateyPackageUpdateRunPresentation.Present(run, null);
        Assert.AreEqual(ChocolateyPackageUpdateRunPresentation.NotStartedHeading, view.Heading);
        StringAssert.StartsWith(view.Lines.Single(), ChocolateyPackageUpdateRunPresentation.NothingStartedPrefix);
        Assert.IsFalse(view.Succeeded);

        fixture.SplitToken = ReviewedRouteSplitTokenState.SplitTokenLimited;
        fixture.ProbeFault = fixture.HistoryUnreadable = fixture.HistoryFault = false;
        fixture.History = [];
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, (await session.ConfirmAsync(null, CancellationToken.None)).Refusal);
        Assert.AreEqual(0, fixture.Dispatcher.Calls);
    }

    [TestMethod]
    public async Task TheCheckIsFreshForJustUnderTenMinutes()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        Assert.AreEqual(TimeSpan.FromMinutes(10), ChocolateyPackageUpdateReviewSession.MaximumCheckAge, "owner decision 1");
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None));
        DateTimeOffset started = session.CheckStartedAt!.Value;
        fixture.Clock.Now = started + TimeSpan.FromMinutes(10) - TimeSpan.FromTicks(1);
        Assert.IsTrue(session.IsCheckFresh);
        fixture.Clock.Now = started + TimeSpan.FromMinutes(10);
        Assert.IsFalse(session.IsCheckFresh);
        fixture.Clock.Now = started - TimeSpan.FromTicks(1);
        Assert.IsFalse(session.IsCheckFresh, "a clock moved back before the check is not fresh");

        fixture.Clock.Now = started + TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1);
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(null, CancellationToken.None);
        Assert.IsTrue(run.Started, run.Refusal.ToString());
    }

    [TestMethod]
    public async Task AnEarlierUpdateRecordedBeforeTheCheckStartedDoesNotBlockButAWingetRowNeverCounts()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.History = [Record(PackageOperation, Fixture.CheckStartedAt - TimeSpan.FromSeconds(1)), Record(WinGetOperation, fixture.Clock.Now + TimeSpan.FromMinutes(1))];
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None));
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(null, CancellationToken.None);
        Assert.IsTrue(run.Started, "a check started after the earlier result clears it, and WinGet rows are not Chocolatey");
    }

    [TestMethod]
    public void AttentionFailsClosedAndSkipsDeclinedOrNotStartedJobs()
    {
        DateTimeOffset check = Start;
        Assert.IsTrue(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention(null, check), "unreadable history");
        ReviewedUpdateHistoryRecord unverifiedRow = Record(PackageOperation, check + TimeSpan.FromMinutes(1)) with { IntegrityVerified = false };
        Assert.IsTrue(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention([unverifiedRow], check), "a row whose integrity cannot be verified");
        ReviewedUpdateHistoryRecord declined = Record(PackageOperation, check + TimeSpan.FromMinutes(1), UpdateDispatchOutcome.ElevationDenied, UpdateDispatchSettlementState.OwnedWorkStopped);
        ReviewedUpdateHistoryRecord notStarted = Record(PackageOperation, check + TimeSpan.FromMinutes(1), UpdateDispatchOutcome.Refused, UpdateDispatchSettlementState.NotInvoked);
        Assert.IsFalse(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention([declined, notStarted], check), "owner decision 2");
        ReviewedUpdateHistoryRecord claimed = Record(PackageOperation, check + TimeSpan.FromMinutes(1)) with { State = UpdateDispatchState.DispatchClaimed, Terminal = null };
        Assert.IsTrue(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention([claimed], check), "an unfinished job");
    }

    [TestMethod]
    public async Task ASecondPressWhileTheFirstRunsIsRefusedAndOnlyOneRunHappens()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Dispatcher.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None));

        Task<ChocolateyClientUpdateRunResult> first = session.ConfirmAsync(null, CancellationToken.None);
        await fixture.Dispatcher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Running, session.Stage);
        ChocolateyClientUpdateRunResult second = await session.ConfirmAsync(null, CancellationToken.None);
        Assert.AreEqual(ChocolateyClientUpdateRefusal.NotReady, second.Refusal);
        Assert.IsFalse(second.Started);

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
        ChocolateyPackageUpdateReviewSession session = fixture.Session();
        Assert.AreEqual(ChocolateyClientUpdateReviewStage.Ready, await session.PrepareAsync(Package, CancellationToken.None));
        ChocolateyClientUpdateRunResult run = await session.ConfirmAsync(null, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, run.Result!.Outcome, run.Result.Message);

        IReadOnlyList<ReviewedUpdateHistoryRecord> history = await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None);
        ChocolateyClientUpdateRunView view = ChocolateyPackageUpdateRunPresentation.Present(run, ChocolateyClientUpdateRunPresentation.RecordedView(history, session.Operation!.JobId));
        Assert.AreEqual(ChocolateyPackageUpdateRunPresentation.NotVerifiedHeading, view.Heading);
        Assert.IsFalse(view.Succeeded);
        Assert.IsTrue(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention(history, Fixture.CheckStartedAt));
        Assert.IsFalse(ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention(history, fixture.Clock.Now + TimeSpan.FromSeconds(1)));
        // It also blocks the client screen's C6, which reads only client rows: a package row never blocks it.
        Assert.IsFalse(ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(history, Fixture.CheckStartedAt));
    }

    private static ReviewedUpdateHistoryRecord Record(Func<DateTimeOffset, ReviewedUpdateOperation> build, DateTimeOffset occurredAt,
        UpdateDispatchOutcome outcome = UpdateDispatchOutcome.Unverified, UpdateDispatchSettlementState settlement = UpdateDispatchSettlementState.OwnedWorkStopped)
    {
        ReviewedUpdateOperation operation = build(occurredAt - TimeSpan.FromMinutes(2));
        return new(operation.JobId, operation, UpdateDispatchState.Final, occurredAt - TimeSpan.FromMinutes(1),
            new ReviewedUpdateTerminalRecord(outcome, occurredAt, 0, settlement, "fixture"), true);
    }

    private static ReviewedUpdateOperation PackageOperation(DateTimeOffset reviewedAt)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + Package, UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, Package, Installed, Candidate, UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable,
            "chocolatey-package-v1")).Confirm(reviewedAt);
        return new(job, User, "choco:" + Package, "machine:chocolatey", Evidence, reviewedAt - TimeSpan.FromSeconds(5),
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation ClientOperation(DateTimeOffset reviewedAt)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, ReviewedUpdateOperation.ChocolateyClientPackageId, "2.7.5", "2.8.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-client-v1")).Confirm(reviewedAt);
        return new(job, User, "choco:chocolatey", "machine:chocolatey", Evidence, reviewedAt - TimeSpan.FromSeconds(5),
            UpdateRouteContract.ChocolateyClientSelfUpdateV1, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateOperation WinGetOperation(DateTimeOffset reviewedAt)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet,
            UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(reviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", reviewedAt, UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private static ReviewedUpdateDispatchObservation Settled() =>
        new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, "launch-started", Task.CompletedTask, true, true, true,
            false, false, HelperCheck: RecordedHelperCandidateCheck.FromDesktopDerived(RecordedHelperCheckKind.Confirmed, RecordedHelperRefusalReason.None,
                Installed, Candidate));

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false, reconciled: true);

    private sealed class RecordingProgress : IProgress<ChocolateyClientUpdatePhase>
    {
        private readonly object sync = new();
        private readonly List<ChocolateyClientUpdatePhase> phases = [];
        internal IReadOnlyList<ChocolateyClientUpdatePhase> Phases { get { lock (sync) return phases.ToArray(); } }
        public void Report(ChocolateyClientUpdatePhase value) { lock (sync) phases.Add(value); }
    }

    private sealed class PackageDispatcher : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        internal ReviewedUpdateOperation? LastOperation;
        internal TaskCompletionSource? Release;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Supports(ReviewedUpdateOperation operation) => operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3;
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

    private sealed class PackageVerifier(Fixture fixture) : IReviewedUpdateVerifier
    {
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken)
        {
            fixture.Verifications++;
            return Task.FromResult(new PostUpdateInventoryResult(fixture.Verified, false));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"pcsteward-package-session-{Guid.NewGuid():N}.db");

        private Fixture()
        {
            Store = new(path, new ChocolateyHelperVersionGrammar());
            Dispatcher = new();
            // Placeholder times: CheckAsync stamps the real ones inside the check.
            Intent = new(Package, Installed, Candidate, User, ChocolateyPackageUpdateReviewSessionTests.Evidence, ReviewedCandidateProvenance.UserCacheIntent,
                false, default, default);
        }

        internal SqliteDatabase Store { get; }
        internal ChocolateyClientUpdateReviewSessionTests.SteppedClock Clock { get; } = new();
        internal PackageDispatcher Dispatcher { get; }
        /// <summary>The check starts at the fixture's start time; the clock then moves on by <see cref="CheckDuration"/>.</summary>
        internal static DateTimeOffset CheckStartedAt => Start;
        internal TimeSpan CheckDuration = TimeSpan.FromSeconds(5);
        internal ChocolateyReviewIntentFacts? Intent;
        internal bool CheckFault, SaveFault, SaveOther, ProbeFault, HistoryUnreadable, HistoryFault, Verified = true;
        internal ReviewedRouteSplitTokenState SplitToken = ReviewedRouteSplitTokenState.SplitTokenLimited;
        internal IReadOnlyList<ReviewedUpdateHistoryRecord> History = [];
        internal int Checks, Saves, Verifications;
        internal string? LastCheckedPackage;

        internal static async Task<Fixture> CreateAsync()
        {
            Fixture result = new();
            await result.Store.MigrateAsync(CancellationToken.None);
            return result;
        }

        internal ChocolateyPackageUpdateReviewSession Session(bool admitted = true) =>
            ChocolateyPackageUpdateReviewSession.CreateWithAdmissionForTests(admitted, CheckAsync, SaveAsync, ReadHistoryAsync, Workflow(),
                new ChocolateyClientUpdatePhaseSignal(), Probe, Clock);

        internal ReviewedUpdateDispatchWorkflow Workflow() =>
            new((work, _, ct) => work(Owner(), ct), new FreshEvidence(Clock), Dispatcher, new PackageVerifier(this), Store, Clock);

        /// <summary>Resolves at the check's start, observes a second later, then lets the check take <see cref="CheckDuration"/>.</summary>
        internal Task<ChocolateyReviewIntentFacts?> CheckAsync(string packageId, CancellationToken cancellationToken)
        {
            Checks++;
            LastCheckedPackage = packageId;
            if (CheckFault) return Task.FromException<ChocolateyReviewIntentFacts?>(new IOException("check failed"));
            DateTimeOffset resolved = Clock.Now;
            ChocolateyReviewIntentFacts? facts = Intent is null ? null : Intent with
            {
                IntentResolvedAt = Intent.IntentResolvedAt == default ? resolved : Intent.IntentResolvedAt,
                ObservedAt = Intent.ObservedAt == default ? resolved + TimeSpan.FromSeconds(1) : Intent.ObservedAt,
            };
            Clock.Now += CheckDuration;
            if (facts is not null && Intent is not null && Intent.IntentResolvedAt == default && Intent.ObservedAt == default) Intent = facts;
            return Task.FromResult(facts);
        }

        internal async Task<ReviewedUpdateDraft> SaveAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
        {
            Saves++;
            if (SaveFault) throw new IOException("disk full");
            if (!SaveOther) return await Store.SaveReviewedOperationAsync(operation, cancellationToken);
            return await Store.SaveReviewedOperationAsync(PackageOperation(operation.ReviewedAt), cancellationToken);
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
