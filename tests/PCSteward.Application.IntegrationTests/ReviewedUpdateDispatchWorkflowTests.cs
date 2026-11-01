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

    /// <summary>Fact extension rev 3 §4.3 (L5): any verification detail other than None is Unverified with the fixed message, even beside a detected version or a restart.</summary>
    [TestMethod]
    [DataRow(true, false, PostUpdateVerificationDetail.ClientChanged)]
    [DataRow(true, true, PostUpdateVerificationDetail.ClientUnverifiable)]
    [DataRow(false, true, PostUpdateVerificationDetail.ClientChanged)]
    [DataRow(true, false, (PostUpdateVerificationDetail)99)]
    public async Task AVerificationDetailIsUnverifiedWithTheFixedMessageNeverSuccess(bool expected, bool restart, PostUpdateVerificationDetail detail)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        FakeVerifier verifier = new(new(expected, restart, detail));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher, verifier: verifier).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome);
        StringAssert.Contains(result.Message, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage);
        Assert.AreEqual(1, verifier.Calls);
    }

    /// <summary>
    /// Fact extension rev 3 §4.3 (L5): a restart-pending result on the runtime-limit Chocolatey route is a contract violation
    /// (Unverified, never RestartRequired). The workflow does not admit that route yet, so the pure guard is checked directly.
    /// </summary>
    [TestMethod]
    public void TheVerificationGuardRejectsARestartOnTheRuntimeLimitRouteOnly()
    {
        ReviewedUpdateOperation chocolatey = Chocolatey();
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(chocolatey, new(false, true)));
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(chocolatey, new(true, true)));
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.RejectVerification(chocolatey, null)?.Outcome);
        Assert.AreEqual((UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage),
            ReviewedUpdateDispatchWorkflow.RejectVerification(chocolatey, new(true, false, PostUpdateVerificationDetail.ClientChanged)));
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.RejectVerification(chocolatey, new(true, false)), "a clean verified target maps normally");
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.RejectVerification(chocolatey, new(false, false)), "a clean miss maps normally (Unverified)");
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        ReviewedUpdateOperation winget = new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1),
            UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.RejectVerification(winget, new(false, true)), "other routes keep their restart semantics");
    }

    private static ReviewedUpdateOperation Chocolatey(UpdateRouteContract route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git.install", "machine:choco",
            route == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ? "choco-profile-v2:" + new string('A', 64) : "evidence:1", ReviewedAt,
            route, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
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
    [DataRow("expired")]
    [DataRow("stale-evidence")]
    [DataRow("cancelled")]
    public async Task ClaimCommittedWithoutAPermitIsFinalizedAsNeverInvokedNotLeftUncertain(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        using CancellationTokenSource cancellation = new();
        AfterClaimStore store = new(fixture.Store, () =>
        {
            if (change == "expired") fixture.Clock.Now = ReviewedAt.AddMinutes(31);
            else if (change == "stale-evidence") fixture.Clock.Now = ReviewedAt.AddMinutes(4);
            else cancellation.Cancel();
        });
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher, store: store).RunAsync(fixture.Confirm(draft), cancellation.Token);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, dispatcher.Calls);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
        Assert.IsFalse(await fixture.Store.HasUnresolvedDispatchesAsync(Owner(), CancellationToken.None));
    }

    [TestMethod]
    public async Task OversizedDiagnosticsAreBoundedAndCannotBlockFinalization()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        string huge = new('x', 20_000);
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, huge)))
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Refused, result.Outcome);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));

        ReviewedUpdateDraft second = await fixture.SaveAsync();
        ReviewedUpdateDispatchResult started = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.ProviderFailed, 1) with { Diagnostic = huge }))
            .RunAsync(fixture.Confirm(second), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Failed, started.Outcome);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(second.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("lone-high")]
    [DataRow("lone-low")]
    [DataRow("astral-at-observation-bound")]
    [DataRow("astral-at-terminal-bound")]
    public async Task NonBmpOrMalformedUtf16DiagnosticsCannotBlockFinalization(string change)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        string diagnostic = change switch
        {
            "lone-high" => "helper refused \uD83D",
            "lone-low" => "\uDE00 helper refused",
            "astral-at-observation-bound" => new string('x', ReviewedUpdateDispatchObservation.MaximumDiagnosticLength - 1) + "\U0001F600tail",
            _ => new string('x', 3999) + "\U0001F600",
        };
        ReviewedUpdateDispatchResult refused = await fixture.Workflow(new FakeDispatcher(_ => ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, diagnostic)))
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, refused.Disposition, refused.Message);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));

        ReviewedUpdateDraft second = await fixture.SaveAsync();
        ReviewedUpdateDispatchResult failed = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.ProviderFailed, 1) with { Diagnostic = diagnostic }))
            .RunAsync(fixture.Confirm(second), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Failed, failed.Outcome, failed.Message);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(second.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public void SanitizedDiagnosticsAlwaysEncodeAsStrictUtf8AndKeepSurrogatePairsWhole()
    {
        System.Text.UTF8Encoding strict = new(false, true);
        foreach (string input in new[] { "a\uD83Db", "\uDE00", new string('y', 4095) + "\U0001F600", "ok \U0001F600 ok" })
        {
            string safe = SanitizedDiagnosticBundle.Sanitize(input);
            Assert.IsLessThanOrEqualTo(4096, safe.Length);
            _ = strict.GetByteCount(safe);
        }
        Assert.AreEqual("ok \U0001F600 ok", SanitizedDiagnosticBundle.Sanitize("ok \U0001F600 ok"));
        Assert.AreEqual("ab", SanitizedDiagnosticBundle.Truncate("ab\U0001F600", 3));
        // The terminal sanitizes first, so malformed input is repaired rather than rejected, and its payload always encodes.
        _ = new UpdateDispatchTerminal(UpdateDispatchOutcome.Failed, ReviewedAt, null, "bad \uD83D").GetReservedPayload();
    }

    [TestMethod]
    public async Task RestartReportWithASuccessExitCodeIsInconsistentAndNeverSuccess()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeVerifier verifier = new(new(true, false));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { RestartReported = true }), verifier: verifier)
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome);
        Assert.AreEqual(0, verifier.Calls);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(-1)]
    public async Task RestartReportWithANonRebootExitCodeIsStillFailure(int exit)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeVerifier verifier = new(new(true, false));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, exit) with { RestartReported = true }), verifier: verifier)
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Failed, result.Outcome);
        Assert.AreEqual(0, verifier.Calls);
    }

    [TestMethod]
    [DataRow(true, false, UpdateDispatchOutcome.Unverified)]
    [DataRow(true, true, UpdateDispatchOutcome.RestartRequired)]
    [DataRow(false, true, UpdateDispatchOutcome.RestartRequired)]
    [DataRow(false, false, UpdateDispatchOutcome.Unverified)]
    public async Task ProviderRestartReportIsVerifiedButNeverSuccess(bool expected, bool restart, UpdateDispatchOutcome outcome)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeVerifier verifier = new(new(expected, restart));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 3010) with { RestartReported = true }), verifier: verifier)
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(outcome, result.Outcome);
        Assert.AreEqual(1, verifier.Calls);
    }

    [TestMethod]
    public async Task SettledStartedWorkWithoutAnObservedExitCodeIsUnverified()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeVerifier verifier = new(new(true, false));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, null)), verifier: verifier)
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome);
        Assert.AreEqual(0, verifier.Calls);
    }

    [TestMethod]
    public async Task RouteSupportThrowingAfterPermitConsumptionFinalizesAsNeverInvoked()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        ThrowingSecondSupport dispatcher = new();
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, dispatcher.Dispatches);
        Assert.AreEqual(UpdateDispatchState.Final, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task StoreRejectsCancelledAfterStartAndExitCodesWithoutInvocation()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        OperationOwnership owner = Owner();
        UpdateDispatchClaim claim = (await fixture.Store.TryClaimAsync(draft, Fresh(draft.Operation)!, owner, fixture.Clock.GetUtcNow(), CancellationToken.None))!;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecordFinalAsync(claim, owner,
            new(UpdateDispatchOutcome.Cancelled, fixture.Clock.GetUtcNow(), null, "x"),
            UpdateDispatchSettlement.FromObservedWork(claim, owner, Task.CompletedTask, true, true, true, false, false), CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecordFinalAsync(claim, owner,
            new(UpdateDispatchOutcome.Failed, fixture.Clock.GetUtcNow(), 1, "x"), UpdateDispatchSettlement.BeforeInvocation(claim, owner), CancellationToken.None));
        Assert.AreEqual(UpdateDispatchState.DispatchClaimed, await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task UnreadableDispatchStateIsReportedAsARetainedBarrier()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0)),
            store: new UnreadableStateStore(fixture.Store), gate: (_, _, _) => throw new InvalidOperationException("busy"))
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStartedClaimRetained, result.Disposition);
    }

    [TestMethod]
    public async Task FailureWhileLeavingTheGateCannotRewriteARecordedResult()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0)),
            gate: async (work, _, ct) => { await work(Owner(), ct); throw new InvalidOperationException("release failed"); })
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition);
        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome);
    }

    [TestMethod]
    public async Task UnreconciledOwnershipNeverClaims()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        FakeDispatcher dispatcher = new(_ => Settled(ReviewedUpdateProviderStatus.Completed, 0));
        ReviewedUpdateDispatchResult result = await fixture.Workflow(dispatcher,
            gate: (work, _, ct) => work(new OperationOwnership(User, OperationGateContract.NamePrefix + User, true, reconciled: false), ct))
            .RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.NotStarted, result.Disposition);
        Assert.AreEqual(0, dispatcher.Calls);
        Assert.IsNull(await fixture.Store.GetDispatchStateAsync(draft.Operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task ChocolateyRoutesCannotReachDispatchEvenWhenAnAdapterClaimsSupport()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        foreach (UpdateRouteContract route in new[] { UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, UpdateRouteContract.ChocolateyMachineProvisionalV3 })
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
            ReviewedUpdateDraft draft = await fixture.Store.SaveReviewedOperationAsync(new(job, User, "choco:git.install", "machine:choco",
                route == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ? "choco-profile-v2:" + new string('A', 64) : "evidence:1", ReviewedAt, route,
                ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected()), CancellationToken.None);
            Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(draft.Operation));
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Confirm(draft));
        }
    }

    [TestMethod]
    public async Task StoreRejectsUnverifiedWithoutPositiveOwnedWorkSettlement()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        OperationOwnership owner = Owner();
        UpdateDispatchClaim claim = (await fixture.Store.TryClaimAsync(draft, Fresh(draft.Operation)!, owner, fixture.Clock.GetUtcNow(), CancellationToken.None))!;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.RecordFinalAsync(claim, owner,
            new(UpdateDispatchOutcome.Unverified, fixture.Clock.GetUtcNow(), null, "x"), UpdateDispatchSettlement.BeforeInvocation(claim, owner), CancellationToken.None));
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
    [DataRow(ReviewedUpdateProviderStatus.TimedOut, null, UpdateDispatchOutcome.Unverified)]
    [DataRow(ReviewedUpdateProviderStatus.Failed, null, UpdateDispatchOutcome.Unverified)]
    [DataRow(ReviewedUpdateProviderStatus.Cancelled, null, UpdateDispatchOutcome.Unverified)]
    [DataRow(ReviewedUpdateProviderStatus.ElevationDenied, null, UpdateDispatchOutcome.ElevationDenied)]
    [DataRow(ReviewedUpdateProviderStatus.Refused, null, UpdateDispatchOutcome.Refused)]
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
    [DataRow(ReviewedUpdateProviderStatus.Refused, UpdateDispatchOutcome.Refused)]
    [DataRow(ReviewedUpdateProviderStatus.ElevationDenied, UpdateDispatchOutcome.ElevationDenied)]
    [DataRow(ReviewedUpdateProviderStatus.Cancelled, UpdateDispatchOutcome.Cancelled)]
    [DataRow(ReviewedUpdateProviderStatus.Failed, UpdateDispatchOutcome.Failed)]
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

    // ---- Task 50 adapter gates Part B (§B.2, §B.7 Workflow): the helper check never changes the outcome ----

    [TestMethod]
    [DataRow("completed")]
    [DataRow("refused")]
    [DataRow("unsettled")]
    public async Task HelperCheckOfAnyKindLeavesOutcomeAndSettlementIdenticalAndInconsistentChecksAreNotRecorded(string scenario)
    {
        UpdateDispatchOutcome? baselineOutcome = null;
        UpdateDispatchSettlementState? baselineSettlement = null;
        foreach (RecordedHelperCandidateCheck? check in new RecordedHelperCandidateCheck?[]
        {
            null,
            RecordedHelperCandidateCheck.NotObserved,
            RecordedHelperCandidateCheck.FromDesktopDerived(RecordedHelperCheckKind.Confirmed, RecordedHelperRefusalReason.None, "1.0.0", "1.1.0"),
            RecordedHelperCandidateCheck.FromDesktopDerived(RecordedHelperCheckKind.Refused, RecordedHelperRefusalReason.CandidateMismatch, "1.0.0", "1.2.0"),
            RecordedHelperCandidateCheck.FromDesktopDerived(RecordedHelperCheckKind.Unconfirmed, RecordedHelperRefusalReason.None, null, null),
        })
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ReviewedUpdateDraft draft = await fixture.SaveAsync();
            ReviewedUpdateDispatchObservation observation = (scenario switch
            {
                "completed" => Settled(ReviewedUpdateProviderStatus.Completed, 0),
                "refused" => ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, "refused"),
                _ => new(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, "pending", new TaskCompletionSource().Task, true, true, true, false, false),
            }) with { HelperCheck = check };
            ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => observation)).RunAsync(fixture.Confirm(draft), CancellationToken.None);
            ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            baselineOutcome ??= result.Outcome;
            baselineSettlement ??= record.Terminal!.Settlement;
            Assert.AreEqual(baselineOutcome, result.Outcome);
            Assert.AreEqual(baselineOutcome, record.Terminal!.Outcome);
            Assert.AreEqual(baselineSettlement, record.Terminal.Settlement);
            Assert.IsTrue(record.IntegrityVerified);
            // The WinGet carrier route can never hold a helper check (O-route), so any value is recorded as NotObserved.
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, record.Terminal.HelperCheck);
            Assert.IsNull(result.HelperCheck);
            Assert.IsNull(ReviewedUpdateHistoryPresentation.Present(record).HelperReport);
            bool inconsistent = check is { Kind: not RecordedHelperCheckKind.NotObserved };
            Assert.AreEqual(inconsistent, result.Message.StartsWith(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic + " ", StringComparison.Ordinal), result.Message);
            Assert.AreEqual(inconsistent, record.Terminal.Diagnostic.StartsWith(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, StringComparison.Ordinal));
        }
        Assert.AreEqual(scenario switch { "completed" => UpdateDispatchOutcome.Success, "refused" => UpdateDispatchOutcome.Refused, _ => UpdateDispatchOutcome.Uncertain }, baselineOutcome);
    }

    [TestMethod]
    public async Task HelperCheckNotRecordedMarkerSurvivesTruncationOfAVeryLongDiagnostic()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        ReviewedUpdateDraft draft = await fixture.SaveAsync();
        ReviewedUpdateDispatchObservation observation = ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, new string('d', 20_000)) with
        {
            HelperCheck = RecordedHelperCandidateCheck.FromDesktopDerived(RecordedHelperCheckKind.Refused, RecordedHelperRefusalReason.ApprovalExpired, null, null),
        };
        ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => observation)).RunAsync(fixture.Confirm(draft), CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Refused, result.Outcome);
        ReviewedUpdateTerminalRecord terminal = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single().Terminal!;
        StringAssert.StartsWith(terminal.Diagnostic, ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic + " ");
        Assert.IsLessThanOrEqualTo(4000, terminal.Diagnostic.Length);
    }

    [TestMethod]
    public async Task DiagnosticTextAndDispatcherExceptionsNeverProduceAHelperCheck()
    {
        await using (Fixture fixture = await Fixture.CreateAsync())
        {
            ReviewedUpdateDraft draft = await fixture.SaveAsync();
            ReviewedUpdateDispatchObservation observation = Settled(ReviewedUpdateProviderStatus.Completed, 0) with
            {
                Diagnostic = "Reported by PC Steward's administrator helper: the exact reviewed version matched its protected machine checks.",
            };
            ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => observation)).RunAsync(fixture.Confirm(draft), CancellationToken.None);
            Assert.IsNull(result.HelperCheck);
            ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, record.Terminal!.HelperCheck);
            Assert.IsNull(ReviewedUpdateHistoryPresentation.Present(record).HelperReport);
            Assert.IsFalse(result.Message.Contains(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, StringComparison.Ordinal));
        }
        await using (Fixture fixture = await Fixture.CreateAsync())
        {
            ReviewedUpdateDraft draft = await fixture.SaveAsync();
            ReviewedUpdateDispatchResult result = await fixture.Workflow(new FakeDispatcher(_ => throw new IOException("pipe broke"))).RunAsync(fixture.Confirm(draft), CancellationToken.None);
            Assert.AreEqual(UpdateDispatchOutcome.Uncertain, result.Outcome);
            Assert.IsNull(result.HelperCheck);
            ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, record.Terminal!.HelperCheck);
            Assert.IsFalse(result.Message.Contains(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// N6 (design rev 6 rev-4 L4; fact extension rev 3 §4.3): what the workflow records reaches history as the fixed
    /// verification text without changing the outcome. The admitted WinGet route never carries the Chocolatey helper's
    /// lingering-process marker (L1), even when its diagnostic contains the token, and an embedded token is never a note
    /// (L2). The marker itself is covered by <see cref="TheLingeringMarkerIsAddedOnlyForAStartedRuntimeLimitChocolateyInvocation"/>.
    /// </summary>
    [TestMethod]
    [DataRow("verified", false)]
    [DataRow("verified", true)]
    [DataRow("not-detected", false)]
    [DataRow("not-detected", true)]
    [DataRow("did-not-finish", true)]
    [DataRow("client-changed", false)]
    [DataRow("client-unverifiable", true)]
    [DataRow("timed-out", true)]
    [DataRow("no-exit", true)]
    [DataRow("refused", true)]
    [DataRow("unsettled", true)]
    public async Task RecordedVerificationAndLingeringTokenReachHistoryWithoutChangingTheOutcome(string scenario, bool lingering)
    {
        (UpdateDispatchOutcome outcome, string? verification) = scenario switch
        {
            "verified" => (UpdateDispatchOutcome.Success, ReviewedUpdateHistoryPresentation.TargetVerifiedText),
            "not-detected" => (UpdateDispatchOutcome.Unverified, ReviewedUpdateHistoryPresentation.TargetNotDetectedText),
            "did-not-finish" => (UpdateDispatchOutcome.Unverified, ReviewedUpdateHistoryPresentation.VerificationDidNotFinishText),
            "client-changed" or "client-unverifiable" => (UpdateDispatchOutcome.Unverified, ReviewedUpdateDispatchWorkflow.ClientNotVerifiedMessage),
            "timed-out" => (UpdateDispatchOutcome.Unverified, ReviewedUpdateHistoryPresentation.StoppedAfterStartText),
            "no-exit" => (UpdateDispatchOutcome.Unverified, ReviewedUpdateHistoryPresentation.ExitCodeNotObservedText),
            "refused" => (UpdateDispatchOutcome.Refused, (string?)null),
            _ => (UpdateDispatchOutcome.Uncertain, (string?)null),
        };
        foreach (bool withToken in lingering ? new[] { false, true } : new[] { false })
        {
            await using Fixture fixture = await Fixture.CreateAsync();
            ReviewedUpdateDraft draft = await fixture.SaveAsync();
            string diagnostic = withToken ? "helper settle: " + ReviewedUpdateLingeringProcesses.Token : "fixture";
            ReviewedUpdateDispatchObservation observation = scenario switch
            {
                "timed-out" => Settled(ReviewedUpdateProviderStatus.TimedOut, null),
                "no-exit" => Settled(ReviewedUpdateProviderStatus.Completed, null),
                "refused" => ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, diagnostic),
                "unsettled" => Settled(ReviewedUpdateProviderStatus.Completed, 0) with { OwnedRoot = new TaskCompletionSource().Task },
                _ => Settled(ReviewedUpdateProviderStatus.Completed, 0),
            } with { Diagnostic = diagnostic };
            PostUpdateInventoryResult? result = scenario switch
            {
                "not-detected" => new(false, false),
                "did-not-finish" => null,
                "client-changed" => new(false, false, PostUpdateVerificationDetail.ClientChanged),
                "client-unverifiable" => new(false, false, PostUpdateVerificationDetail.ClientUnverifiable),
                _ => new(true, false),
            };
            ReviewedUpdateDispatchResult dispatched = await fixture.Workflow(new FakeDispatcher(_ => observation), verifier: new FakeVerifier(result))
                .RunAsync(fixture.Confirm(draft), CancellationToken.None);
            Assert.AreEqual(outcome, dispatched.Outcome, $"{scenario} token={withToken}");

            ReviewedUpdateHistoryRecord record = (await fixture.Store.GetReviewedUpdateHistoryAsync(10, CancellationToken.None)).Single();
            Assert.IsTrue(record.IntegrityVerified);
            Assert.AreEqual(outcome, record.Terminal!.Outcome);
            ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(record);
            Assert.AreEqual(verification, view.VerificationDetail, $"{scenario} token={withToken}");
            Assert.IsNull(view.LingeringProcessesNote, $"{scenario} token={withToken}");
            Assert.IsFalse(record.Terminal.Diagnostic.StartsWith(ReviewedUpdateLingeringProcesses.Token, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// L1 (design rev 6 rev-4 L4): the marker is added only for the runtime-limit Chocolatey route, only for a Started
    /// invocation, and only when the helper's diagnostic carries the whole token; history then shows the note, also behind
    /// the helper-check marker the workflow may prefix afterwards. The live workflow cannot run that route while
    /// <c>HasSupportedMode</c> admits only WinGet, so the marker rule is exercised directly.
    /// </summary>
    [TestMethod]
    public void TheLingeringMarkerIsAddedOnlyForAStartedRuntimeLimitChocolateyInvocation()
    {
        ReviewedUpdateOperation chocolatey = Chocolatey();
        const string Message = ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage;
        string token = ReviewedUpdateLingeringProcesses.Token;
        ReviewedUpdateDispatchObservation started = Settled(ReviewedUpdateProviderStatus.Completed, 0) with { Diagnostic = "launch-started; " + token };

        string marked = ReviewedUpdateDispatchWorkflow.WithLingeringMarker(chocolatey, started, Message);
        Assert.AreEqual(token + " " + Message, marked);
        foreach (ReviewedUpdateOperation other in new[]
                 {
                     WinGetOperation(), Chocolatey(UpdateRouteContract.ChocolateyMachineProvisionalV3),
                     Chocolatey(UpdateRouteContract.ChocolateyMachineRegistrationV3), Chocolatey(UpdateRouteContract.ChocolateyMachineRegistrationV2),
                 })
            Assert.AreEqual(Message, ReviewedUpdateDispatchWorkflow.WithLingeringMarker(other, started, Message), other.RouteContract.ToString());
        Assert.AreEqual(Message, ReviewedUpdateDispatchWorkflow.WithLingeringMarker(chocolatey, started with { Invocation = ReviewedUpdateInvocation.NotStarted }, Message));
        Assert.AreEqual(Message, ReviewedUpdateDispatchWorkflow.WithLingeringMarker(chocolatey, started with { Invocation = ReviewedUpdateInvocation.Unknown }, Message));
        Assert.AreEqual(Message, ReviewedUpdateDispatchWorkflow.WithLingeringMarker(chocolatey, started with { Diagnostic = "launch-started" }, Message));
        Assert.AreEqual(Message, ReviewedUpdateDispatchWorkflow.WithLingeringMarker(chocolatey, started with { Diagnostic = "launch-started; " + token + "-x" }, Message));

        foreach (string recorded in new[] { marked, ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic + " " + marked })
        {
            ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(new(chocolatey.JobId, chocolatey, UpdateDispatchState.Final, ReviewedAt,
                new(UpdateDispatchOutcome.Success, ReviewedAt.AddMinutes(1), 0, UpdateDispatchSettlementState.OwnedWorkStopped, recorded), true));
            Assert.AreEqual(ReviewedUpdateLingeringProcesses.HistoryNote, view.LingeringProcessesNote, recorded);
            Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.VerificationDetail, recorded);
            Assert.IsFalse(view.Diagnostic.Contains(token, StringComparison.Ordinal), "the note replaces the visible marker");
        }
    }

    private static ReviewedUpdateOperation WinGetOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1),
            UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
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

    private sealed class ThrowingSecondSupport : IReviewedUpdateRouteDispatcher
    {
        private int supportCalls;
        internal int Dispatches;
        public bool Supports(ReviewedUpdateOperation operation) => ++supportCalls == 1 ? true : throw new InvalidOperationException("route table unavailable");
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Dispatches++;
            return Task.FromResult(Settled(ReviewedUpdateProviderStatus.Completed, 0));
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
        public virtual Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) => inner.GetDispatchStateAsync(jobId, cancellationToken);
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

    private sealed class UnreadableStateStore(IUpdateDispatchStore inner) : DelegatingStore(inner)
    {
        public override Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromException<UpdateDispatchState?>(new InvalidOperationException("corrupt"));
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
