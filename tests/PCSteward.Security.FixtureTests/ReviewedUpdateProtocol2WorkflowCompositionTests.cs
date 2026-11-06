using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15/01; TM-03/04/07/13; AC-UPD-07: the real <see cref="ReviewedUpdateDispatchWorkflow"/> consuming observations
/// produced by the protocol-2 interpreter from real codec frames. Written before the Task 50 activation, when the workflow
/// admitted only WinGetUserExactV3 (<c>ReviewedUpdateReview.HasSupportedMode</c>): the workflow runs a WinGet carrier
/// operation while the interpreter judges a separate frozen ChocolateyMachineRuntimeLimitsV3 operation. That proves only the
/// composition of the observation semantics, not route admission (pinned by <c>ChocolateyRouteActivationBoundaryTests</c>). The in-memory store mirrors the one-use settlement consumption.
/// </summary>
[TestClass]
public sealed class ReviewedUpdateProtocol2WorkflowCompositionTests
{
    private static readonly DateTimeOffset ReviewedAt = Protocol2InterpreterFixture.ReviewedAt;
    private const string Installed = Protocol2InterpreterFixture.Installed;
    private const string OtherCandidate = Protocol2InterpreterFixture.OtherCandidate;
    private static readonly string Fingerprint = Protocol2InterpreterFixture.Fingerprint;

    [TestMethod]
    [DataRow("matched-completed", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Success, 1)]
    [DataRow("matched-completed", false, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Unverified, 1)]
    [DataRow("matched-fingerprint-mismatch", true, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain, 0)]
    [DataRow("matched-candidate-mismatch-not-started", true, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain, 0)]
    [DataRow("settled-without-h", true, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain, 0)]
    [DataRow("request-written-no-response", true, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain, 0)]
    [DataRow("unknown-invocation", true, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain, 0)]
    [DataRow("refused-unsettled", true, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain, 0)]
    [DataRow("refused-stage-c-candidate-mismatch", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Refused, 0)]
    [DataRow("refused-preflight-output-limit", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Refused, 0)]
    [DataRow("started-exit0-cancelled", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Unverified, 0)]
    [DataRow("started-exit3010", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Failed, 0)]
    [DataRow("undetermined-timed-out", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Failed, 0)]
    [DataRow("matched-not-started-cancelled", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Cancelled, 0)]
    [DataRow("matched-not-started-launch-failure", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.Failed, 0)]
    [DataRow("elevation-declined", true, ReviewedUpdateDispatchDisposition.Finalized, UpdateDispatchOutcome.ElevationDenied, 0)]
    public async Task InterpretedObservationsReachOutcomesOnlyThroughSettlementAndVerification(string scenario, bool targetDetected,
        ReviewedUpdateDispatchDisposition disposition, UpdateDispatchOutcome outcome, int verifierCalls)
    {
        ReviewedUpdateOperation frozen = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation interpretation = Interpret(frozen, scenario);
        ReviewedUpdateDraft draft = new(CarrierOperation(), Guid.NewGuid());
        Clock clock = new();
        MemoryStore store = new();
        Dispatcher dispatcher = new(interpretation.Observation);
        Verifier verifier = new(new(targetDetected, false));
        ReviewedUpdateDispatchWorkflow workflow = new((work, _, ct) => work(Owner(), ct), new Evidence(), dispatcher, verifier, store, clock);

        ReviewedUpdateDispatchResult result = await workflow.RunAsync(new ReviewedUpdateReview(draft, clock).Confirm(), CancellationToken.None);

        Assert.AreEqual(disposition, result.Disposition, result.Message);
        Assert.AreEqual(outcome, result.Outcome, result.Message);
        Assert.AreEqual(1, dispatcher.Calls);
        Assert.AreEqual(verifierCalls, verifier.Calls);
        Assert.AreEqual(outcome == UpdateDispatchOutcome.Uncertain ? UpdateDispatchState.Uncertain : UpdateDispatchState.Final, store.State);
        Assert.AreEqual(outcome, store.Terminal!.Outcome);
        // Success exists only after positive settlement and a verified target; helper text never reaches the recorded message.
        Assert.IsTrue(outcome != UpdateDispatchOutcome.Success || (verifierCalls == 1 && targetDetected));
        Assert.IsFalse(result.Message.Contains(ReviewedHelperCandidateCheck.ConfirmedText, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ConfirmedCheckWithoutVerifiedTargetIsNeverSuccess()
    {
        ReviewedUpdateOperation frozen = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation interpretation = Interpret(frozen, "matched-completed");
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Confirmed, interpretation.CandidateCheck.Kind);
        foreach (PostUpdateInventoryResult? verification in new PostUpdateInventoryResult?[] { new(false, false), null })
        {
            ReviewedUpdateDraft draft = new(CarrierOperation(), Guid.NewGuid());
            Clock clock = new();
            ReviewedUpdateDispatchWorkflow workflow = new((work, _, ct) => work(Owner(), ct), new Evidence(),
                new Dispatcher(interpretation.Observation), new Verifier(verification), new MemoryStore(), clock);
            ReviewedUpdateDispatchResult result = await workflow.RunAsync(new ReviewedUpdateReview(draft, clock).Confirm(), CancellationToken.None);
            Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome);
        }
    }

    /// <summary>
    /// Adapter gates §B.2/§B.7: the observation carrying the history-only helper check reaches exactly the same disposition,
    /// outcome and settlement as the bare observation. The WinGet carrier can never hold a check (O-route), so every
    /// non-NotObserved value is recorded as NotObserved with the fixed marker, which also proves the inconsistent path.
    /// </summary>
    [TestMethod]
    [DataRow("matched-completed")]
    [DataRow("matched-fingerprint-mismatch")]
    [DataRow("settled-without-h")]
    [DataRow("refused-unsettled")]
    [DataRow("refused-stage-c-candidate-mismatch")]
    [DataRow("refused-preflight-output-limit")]
    [DataRow("started-exit0-cancelled")]
    [DataRow("started-exit3010")]
    [DataRow("undetermined-timed-out")]
    [DataRow("matched-not-started-cancelled")]
    [DataRow("matched-not-started-launch-failure")]
    [DataRow("elevation-declined")]
    public async Task HelperCheckCarriedToFinalizationNeverChangesTheOutcome(string scenario)
    {
        ReviewedUpdateProtocol2Interpretation interpretation = Interpret(Protocol2InterpreterFixture.Operation(), scenario);
        (ReviewedUpdateDispatchResult plain, MemoryStore plainStore) = await RunCarrierAsync(interpretation.Observation);
        (ReviewedUpdateDispatchResult carried, MemoryStore carriedStore) = await RunCarrierAsync(interpretation.WorkflowObservation);
        Assert.AreEqual(plain.Disposition, carried.Disposition);
        Assert.AreEqual(plain.Outcome, carried.Outcome);
        Assert.AreEqual(plainStore.State, carriedStore.State);
        Assert.AreEqual(plainStore.Terminal!.Outcome, carriedStore.Terminal!.Outcome);
        Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, carriedStore.Terminal.HelperCheck);
        Assert.IsNull(carried.HelperCheck);
        bool hadCheck = interpretation.CandidateCheck.Kind != ReviewedHelperCandidateCheckKind.NotObserved;
        Assert.AreEqual(hadCheck, carried.Message.StartsWith(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic + " ", StringComparison.Ordinal), carried.Message);
        Assert.IsFalse(plain.Message.Contains(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, StringComparison.Ordinal));
    }

    /// <summary>
    /// H1/L1 end to end (design rev 6 settle window, rev-4 L4): the real helper endpoint (fake item reporting that the launch
    /// job closed lingering descendants), the real codec frame, the desktop interpreter, the real workflow and the persisted
    /// terminal format, then history. The admitted WinGet carrier never records the Chocolatey helper's marker; for the
    /// runtime-limit route (run here without a route dispatcher) the workflow's own marker rule is applied to the
    /// same observation and recorded message, and history shows the fixed note only when the helper reported the token.
    /// </summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TheHelpersLingeringTokenReachesHistoryOnlyForTheRuntimeLimitRoute(bool lingering)
    {
        EndpointHarness harness = new();
        harness.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(
            new ChocolateyUpgradeResult.Observed(EndpointFixture.Started(0, lingering: lingering)));
        EndpointRun run = await harness.ExchangeAsync();
        ReviewedUpdateDispatchObservation observation = run.Interpretation.Observation;
        Assert.AreEqual(ReviewedUpdateInvocation.Started, observation.Invocation);
        Assert.AreEqual(lingering, ReviewedUpdateLingeringProcesses.IsReported(observation.Diagnostic), observation.Diagnostic);

        (ReviewedUpdateDispatchResult result, MemoryStore store) = await RunCarrierAsync(observation);
        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome, result.Message);
        Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(store.Payload, null, out ReviewedUpdateTerminalRecord? carrierTerminal));
        Assert.AreEqual(ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage, carrierTerminal!.Diagnostic, "L1: the WinGet route records no marker");
        ReviewedUpdateHistoryView carrierView = ReviewedUpdateHistoryPresentation.Present(new(store.Claim!.Draft.Operation.JobId, store.Claim.Draft.Operation,
            UpdateDispatchState.Final, store.Claim.ClaimedAt, carrierTerminal, true));
        Assert.IsNull(carrierView.LingeringProcessesNote);

        ReviewedUpdateOperation route = run.Operation;
        Assert.AreEqual(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, route.RouteContract);
        string recorded = ReviewedUpdateDispatchWorkflow.WithLingeringMarker(route, observation, carrierTerminal.Diagnostic);
        UpdateDispatchTerminal terminal = new(carrierTerminal.Outcome, carrierTerminal.OccurredAt, carrierTerminal.ExitCode, recorded,
            RecordedHelperCandidateCheck.NotObserved);
        Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(terminal.GetReservedPayload(), null, out ReviewedUpdateTerminalRecord? routeTerminal));
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(new(route.JobId, route, UpdateDispatchState.Final,
            ReviewedAt.AddMinutes(1), routeTerminal, true));
        Assert.AreEqual(lingering ? ReviewedUpdateLingeringProcesses.HistoryNote : null, view.LingeringProcessesNote);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.VerificationDetail);
        Assert.AreEqual(ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage, view.Diagnostic, "the visible diagnostic drops the marker");
        Assert.AreEqual(lingering, routeTerminal!.Diagnostic.StartsWith(ReviewedUpdateLingeringProcesses.Token + " ", StringComparison.Ordinal));
    }

    private static async Task<(ReviewedUpdateDispatchResult, MemoryStore)> RunCarrierAsync(ReviewedUpdateDispatchObservation observation)
    {
        ReviewedUpdateDraft draft = new(CarrierOperation(), Guid.NewGuid());
        Clock clock = new();
        MemoryStore store = new();
        ReviewedUpdateDispatchWorkflow workflow = new((work, _, ct) => work(Owner(), ct), new Evidence(), new Dispatcher(observation),
            new Verifier(new(true, false)), store, clock);
        return (await workflow.RunAsync(new ReviewedUpdateReview(draft, clock).Confirm(), CancellationToken.None), store);
    }

    private static ReviewedUpdateProtocol2Interpretation Interpret(ReviewedUpdateOperation frozen, string scenario)
    {
        ReviewedHelperDesktopObservation desktop = Protocol2InterpreterFixture.SettledDesktop;
        ReviewedHelperTransportOutcome outcome = ReviewedHelperTransportOutcome.ResponseFrameReceived;
        (UpdateWireFacts facts, ReviewedHelperFacts reviewed) = (Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        switch (scenario)
        {
            case "matched-fingerprint-mismatch":
                reviewed = Protocol2InterpreterFixture.Matched(fingerprint: Protocol2InterpreterFixture.OtherFingerprint);
                break;
            case "matched-candidate-mismatch-not-started":
                (facts, reviewed) = (Protocol2InterpreterFixture.NotStarted(), Protocol2InterpreterFixture.Matched(candidate: OtherCandidate));
                break;
            case "settled-without-h":
                desktop = desktop with { HelperExitObserved = false };
                break;
            case "request-written-no-response":
                outcome = ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse;
                break;
            case "unknown-invocation":
                facts = Protocol2InterpreterFixture.Unknown();
                break;
            case "refused-unsettled":
                (facts, reviewed) = (Protocol2InterpreterFixture.NotStarted(settled: false),
                    Protocol2InterpreterFixture.Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, null, null, null));
                break;
            case "refused-stage-c-candidate-mismatch":
                (facts, reviewed) = (Protocol2InterpreterFixture.NotStarted(),
                    Protocol2InterpreterFixture.Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, OtherCandidate, Fingerprint));
                break;
            case "refused-preflight-output-limit":
                (facts, reviewed) = (Protocol2InterpreterFixture.NotStarted(outputLimit: true),
                    Protocol2InterpreterFixture.Refused(ReviewedHelperRefusalReason.PreflightQueryFailed, Installed, null, Fingerprint));
                break;
            case "started-exit0-cancelled":
                facts = Protocol2InterpreterFixture.Started(cancelled: true);
                break;
            case "started-exit3010":
                facts = Protocol2InterpreterFixture.Started(exit: 3010);
                break;
            case "undetermined-timed-out":
                (facts, reviewed) = (Protocol2InterpreterFixture.NotStarted(timedOut: true), Protocol2InterpreterFixture.Undetermined());
                break;
            case "matched-not-started-cancelled":
                facts = Protocol2InterpreterFixture.NotStarted(cancelled: true);
                break;
            case "matched-not-started-launch-failure":
                facts = Protocol2InterpreterFixture.NotStarted();
                break;
            case "elevation-declined":
                outcome = ReviewedHelperTransportOutcome.ElevationDeclined;
                break;
        }
        // Pre-request rows carry no response frame (review 9 L1: a frame beside them fails closed).
        byte[] frame = outcome is ReviewedHelperTransportOutcome.ResponseFrameReceived or ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse
            ? Protocol2InterpreterFixture.Frame(frozen, facts, reviewed) : [];
        return ReviewedUpdateProtocol2Interpreter.Interpret(frozen, outcome, frame, Protocol2InterpreterFixture.Nonce, desktop);
    }

    private static ReviewedUpdateOperation CarrierOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "native:1", "context:1", "review:1", ReviewedAt.AddSeconds(-1),
            UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private static OperationOwnership Owner() =>
        new(Protocol2InterpreterFixture.User, OperationGateContract.NamePrefix + Protocol2InterpreterFixture.User, false, reconciled: true);

    private sealed class Dispatcher(ReviewedUpdateDispatchObservation observation) : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        public bool Supports(ReviewedUpdateOperation operation) => true;
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(observation);
        }
    }

    private sealed class Evidence : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation op, OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult<FreshUpdateDispatchEvidence?>(new(op.Selection, op.RequestingUserSid, op.NativeInstanceId, op.InstallationContextId,
                "fresh:1", ReviewedAt.AddMinutes(1), op.RouteContract, op.Mode, true, true));
    }

    private sealed class Verifier(PostUpdateInventoryResult? result) : IReviewedUpdateVerifier
    {
        internal int Calls;
        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken)
        {
            Calls++;
            return result is null ? Task.FromException<PostUpdateInventoryResult>(new IOException("re-inventory failed")) : Task.FromResult(result);
        }
    }

    private sealed class MemoryStore : IUpdateDispatchStore
    {
        internal UpdateDispatchState? State;
        internal UpdateDispatchTerminal? Terminal;
        internal UpdateDispatchClaim? Claim;
        /// <summary>The persisted terminal format, as the store would write it.</summary>
        internal byte[] Payload = [];

        public Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership,
            DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            if (State is not null) return Task.FromResult<UpdateDispatchClaim?>(null);
            State = UpdateDispatchState.DispatchClaimed;
            Claim = new(Guid.NewGuid(), Guid.NewGuid(), draft, evidence, claimedAt);
            return Task.FromResult<UpdateDispatchClaim?>(Claim);
        }

        public Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal,
            UpdateDispatchSettlement settlement, CancellationToken cancellationToken)
        {
            if (State != UpdateDispatchState.DispatchClaimed || !settlement.TryConsumeFor(claim, ownership) ||
                (terminal.Outcome == UpdateDispatchOutcome.Uncertain) == settlement.ProvesSettlement)
                throw new InvalidOperationException("Settlement does not match the terminal outcome.");
            State = terminal.Outcome == UpdateDispatchOutcome.Uncertain ? UpdateDispatchState.Uncertain : UpdateDispatchState.Final;
            Terminal = terminal;
            Payload = terminal.GetReservedPayload(settlement);
            return Task.CompletedTask;
        }

        public Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult(State);
        public Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult(State is UpdateDispatchState.DispatchClaimed or UpdateDispatchState.Uncertain);
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ReviewedAt.AddMinutes(1);
    }
}
