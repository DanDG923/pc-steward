using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15/01; TM-03/04/07/13; AC-UPD-07: the real <see cref="ReviewedUpdateDispatchWorkflow"/> consuming observations
/// produced by the protocol-2 interpreter from real codec frames. The workflow still admits only WinGetUserExactV3
/// (<c>ReviewedUpdateReview.HasSupportedMode</c>), so the workflow runs a WinGet carrier operation while the interpreter
/// judges a separate frozen ChocolateyMachineRuntimeLimitsV3 operation. That proves only the composition of the observation
/// semantics, not route admission. The in-memory store mirrors the one-use settlement consumption.
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
        byte[] frame = Protocol2InterpreterFixture.Frame(frozen, facts, reviewed);
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

        public Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership,
            DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            if (State is not null) return Task.FromResult<UpdateDispatchClaim?>(null);
            State = UpdateDispatchState.DispatchClaimed;
            return Task.FromResult<UpdateDispatchClaim?>(new(Guid.NewGuid(), Guid.NewGuid(), draft, evidence, claimedAt));
        }

        public Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal,
            UpdateDispatchSettlement settlement, CancellationToken cancellationToken)
        {
            if (State != UpdateDispatchState.DispatchClaimed || !settlement.TryConsumeFor(claim, ownership) ||
                (terminal.Outcome == UpdateDispatchOutcome.Uncertain) == settlement.ProvesSettlement)
                throw new InvalidOperationException("Settlement does not match the terminal outcome.");
            State = terminal.Outcome == UpdateDispatchOutcome.Uncertain ? UpdateDispatchState.Uncertain : UpdateDispatchState.Final;
            Terminal = terminal;
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
