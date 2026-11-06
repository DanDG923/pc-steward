using PCSteward.Domain;

namespace PCSteward.Application;

public enum ReviewedUpdateInvocation { Unknown, NotStarted, Started }

/// <summary>Provider/helper status as observed by a dispatcher. None of these values asserts verified success.</summary>
public enum ReviewedUpdateProviderStatus { Completed, ProviderFailed, Refused, ElevationDenied, Cancelled, TimedOut, Failed }

/// <summary>
/// Native observations returned by a route dispatcher. The owned root is an immutable snapshot that the workflow never
/// awaits again; unknown invocation is always unsettled. No member is execution authority or a verified outcome.
/// <see cref="HelperCheck"/> (null means NotObserved) is desktop-derived history data only: the workflow records it when
/// consistent and never lets it change status, settlement or outcome. <see cref="ClientRunFault"/> (ADR-031 design §8.3) is
/// set by the protocol-2 interpreter for every accepted <c>ChocolateyClientSelfUpdateV1</c> reply and never otherwise; the
/// workflow uses it only to keep or lower an outcome and to retain the fixed recovery warnings, never to raise one.
/// </summary>
public sealed record ReviewedUpdateDispatchObservation(
    ReviewedUpdateInvocation Invocation,
    ReviewedUpdateProviderStatus Status,
    int? ExitCode,
    string Diagnostic,
    Task OwnedRoot,
    bool DirectExitObserved,
    bool OwnedIoComplete,
    bool PipeEndOfFile,
    bool TerminationFailed,
    bool ExecutionUncertain,
    bool RestartReported = false,
    RecordedHelperCandidateCheck? HelperCheck = null,
    ReviewedClientRunFault? ClientRunFault = null)
{
    /// <summary>Maximum diagnostic characters carried into a terminal record (the wire diagnostic bound).</summary>
    public const int MaximumDiagnosticLength = 2048;

    /// <summary>Positive proof that nothing was invoked; only this form may settle as NotInvoked.</summary>
    public static ReviewedUpdateDispatchObservation NotStarted(ReviewedUpdateProviderStatus status, string diagnostic) =>
        new(ReviewedUpdateInvocation.NotStarted, status, null, diagnostic, Task.CompletedTask, false, false, false, false, false);
}

/// <summary>
/// Passive carrier of the exact committed claim and frozen operation, issued by the workflow only after the one-use
/// permit was consumed. It is not restorable, persisted or helper request authority by itself.
/// </summary>
public sealed class ReviewedUpdateDispatchTicket
{
    internal ReviewedUpdateDispatchTicket(UpdateDispatchClaim claim)
    {
        Claim = claim;
    }

    public UpdateDispatchClaim Claim { get; }
    public ReviewedUpdateOperation Operation => Claim.Draft.Operation;
}

/// <summary>
/// One closed route adapter. <see cref="Supports"/> is the compiled route check plus, for helper routes, the adapter's
/// protected deployment-capability seam; never eligibility evidence; must not throw (the workflow nevertheless treats an
/// exception as unsupported). The native runner or helper behind <see cref="DispatchAsync"/> must independently revalidate.
/// </summary>
public interface IReviewedUpdateRouteDispatcher
{
    bool Supports(ReviewedUpdateOperation operation);
    Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken);
}

/// <summary>Trusted live preparation immediately before claim. Returned data alone grants nothing.</summary>
public interface IReviewedUpdateDispatchEvidenceSource
{
    Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken);
}

/// <summary>Post-update re-inventory for the frozen operation, used only after positive settlement.</summary>
public interface IReviewedUpdateVerifier
{
    Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken);
}

/// <summary>
/// ADR-031 design §8.3: the verifier of <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/>. The workflow verifies
/// that route only through <see cref="VerifyClientAsync"/>, passing the interpreter-only <see cref="ReviewedClientRunFault"/>
/// of the accepted reply; a verifier without this interface leaves the route Unverified. Implemented beside
/// <see cref="IReviewedUpdateVerifier"/> by the Chocolatey verifier (it does not derive from it).
/// </summary>
public interface IReviewedClientUpdateVerifier
{
    Task<PostUpdateInventoryResult> VerifyClientAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        ReviewedClientRunFault runFault, CancellationToken cancellationToken);
}

public enum ReviewedUpdateDispatchDisposition { NotStarted, NotStartedClaimRetained, Finalized, Uncertain }

/// <summary>
/// Presentation-safe result that keeps the frozen reviewed mode/guard/disclosure with the outcome. <see cref="HelperCheck"/>
/// is set only to a helper check that was actually persisted (null otherwise), so live text equals history.
/// </summary>
public sealed record ReviewedUpdateDispatchResult(
    ReviewedUpdateDispatchDisposition Disposition,
    UpdateDispatchOutcome? Outcome,
    string Message,
    ReviewedInstallerMode Mode,
    UpdateRouteContract Route,
    ReviewedProviderGuardContract GuardContract,
    string? Disclosure,
    RecordedHelperCandidateCheck? HelperCheck = null);

/// <summary>Runs work under the common current-user gate with mandatory reconciliation, bounded by the review deadline.</summary>
public delegate Task<ReviewedUpdateDispatchResult> ReviewedUpdateGateRunner(
    Func<OperationOwnership, CancellationToken, Task<ReviewedUpdateDispatchResult>> work,
    DateTimeOffset reviewDeadlineUtc,
    CancellationToken cancellationToken);

/// <summary>
/// FR-UPD-15/01; TM-03/04/07/13: composes live review, fresh dispatch evidence, durable claim, one-use permit, one
/// dispatch, native settlement, finalization and verification. It never replays, retries, renews a review or awaits
/// an unsettled owned root, and a dispatcher result never becomes Success without settlement and verification.
/// </summary>
public sealed class ReviewedUpdateDispatchWorkflow
{
    private static readonly TimeSpan MaximumReviewAge = TimeSpan.FromMinutes(30);
    private const int MaximumTerminalMessageLength = 4000;
    /// <summary>Fixed marker recorded when a helper check failed <see cref="RecordedHelperCandidateCheck.IsConsistentWith"/>.</summary>
    public const string HelperCheckNotRecordedDiagnostic = "helper-check-not-recorded";
    /// <summary>Fact extension rev 3 §4.3: the fixed message for a verification detail other than None.</summary>
    public const string ClientNotVerifiedMessage =
        "Chocolatey's own files or registration changed, or could not be checked, so PC Steward did not run it to verify the update; the result is unverified.";
    /// <summary>Fact extension rev 3 §4.3 (L5): the runtime-limit Chocolatey route never infers a restart.</summary>
    public const string RestartContractViolationMessage =
        "The verifier reported a restart-pending state, which this Chocolatey route never infers; the result is unverified.";
    /// <summary>Fixed verification messages, recognised by history (N6) to show fixed verification text.</summary>
    public const string TargetVerifiedMessage = "Target version verified by post-update inventory.";
    /// <summary>ADR-031 design §3a: Success of a Chocolatey client self-update with the reviewed leftover disclosed.</summary>
    public const string TargetVerifiedWithLeftoverMessage = TargetVerifiedMessage + " " + ChocolateyClientUpdatePresentation.VerifiedLeftoverNote;
    public const string TargetNotDetectedMessage = "Provider completed but the target version was not detected.";
    public const string VerificationDidNotFinishMessage = "Provider completed but post-update verification did not finish.";
    public const string ExitCodeNotObservedMessage = "The provider ran but its exit code was not observed; the resulting version is unverified.";

    /// <summary>The fixed prefix recorded when started work was cancelled, timed out or failed before verification.</summary>
    public static string StoppedAfterStartMessage(ReviewedUpdateProviderStatus status) =>
        $"{status} after the provider started; the resulting version is unverified.";

    private readonly ReviewedUpdateGateRunner runUnderGate;
    private readonly IReviewedUpdateDispatchEvidenceSource evidence;
    private readonly IReviewedUpdateRouteDispatcher dispatcher;
    private readonly IReviewedUpdateVerifier verifier;
    private readonly IUpdateDispatchStore store;
    private readonly UpdateDispatchAdmission admission;
    private readonly TimeProvider clock;

    public ReviewedUpdateDispatchWorkflow(ReviewedUpdateGateRunner runUnderGate, IReviewedUpdateDispatchEvidenceSource evidence,
        IReviewedUpdateRouteDispatcher dispatcher, IReviewedUpdateVerifier verifier, IUpdateDispatchStore store, TimeProvider? clock = null)
    {
        this.runUnderGate = runUnderGate ?? throw new ArgumentNullException(nameof(runUnderGate));
        this.evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
        this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.clock = clock ?? TimeProvider.System;
        admission = new(store, this.clock);
    }

    /// <summary>
    /// Runs one reviewed dispatch. Callers must invoke this off the UI thread: <see cref="IReviewedUpdateRouteDispatcher.Supports"/>
    /// may perform bounded synchronous I/O before the claim and again after the permit (adapter gates G3).
    /// </summary>
    public async Task<ReviewedUpdateDispatchResult> RunAsync(LiveApprovedUpdate approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ReviewedUpdateOperation operation = approval.Operation;
        if (!approval.IsCurrent)
            return Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null, "The review expired or was cancelled. Open a new review; no update was started.");
        // Adapter gates §A.2: the pre-claim route/capability seam uses the same exception-to-false wrapper as the post-permit
        // recheck, so a throwing seam gives NotStarted with no claim instead of escaping RunAsync.
        if (!ReviewedUpdateReview.HasSupportedMode(operation) || !StillSupported(operation))
            return Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null, "This reviewed provider, mode or route is unavailable. No update was started.");
        ReviewedUpdateDispatchResult? completed = null;
        try
        {
            return await runUnderGate(async (ownership, ct) => completed = await RunOwnedAsync(approval, ownership, ct).ConfigureAwait(false),
                operation.ReviewedAt + MaximumReviewAge, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (completed is not null)
        {
            // The owned work already produced (and recorded) its result; a failure while leaving the gate cannot rewrite it.
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return await AfterGateFailureAsync(operation, "The update was cancelled before dispatch.").ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Post-claim failures are finalized inside; anything reaching here happened before or without a usable claim.
            return await AfterGateFailureAsync(operation, SanitizedDiagnosticBundle.Sanitize(exception.Message)).ConfigureAwait(false);
        }
    }

    private async Task<ReviewedUpdateDispatchResult> AfterGateFailureAsync(ReviewedUpdateOperation operation, string reason)
    {
        UpdateDispatchState? state = null;
        bool lookupFailed = false;
        try { state = await store.GetDispatchStateAsync(operation.JobId, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { lookupFailed = true; }
        if (lookupFailed)
            return Result(operation, ReviewedUpdateDispatchDisposition.NotStartedClaimRetained, null,
                $"{reason} The dispatch state could not be read, so it is treated as a retained conflicting-work barrier until reconciliation.");
        return state switch
        {
            UpdateDispatchState.Final => Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null,
                $"{reason} This review was already dispatched and finalized; open a new review. No update was started."),
            null => Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null, $"{reason} No update was started."),
            _ => Result(operation, ReviewedUpdateDispatchDisposition.NotStartedClaimRetained, null,
                $"{reason} A dispatch claim exists and remains a conflicting-work barrier until reconciliation; it is never replayed."),
        };
    }

    private async Task<ReviewedUpdateDispatchResult> RunOwnedAsync(LiveApprovedUpdate approval, OperationOwnership ownership, CancellationToken cancellationToken)
    {
        ReviewedUpdateOperation operation = approval.Operation;
        cancellationToken.ThrowIfCancellationRequested();
        if (!approval.IsCurrent || !ownership.CanDispatchFor(operation))
            return Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null, "The review expired or operation ownership is not ready. No update was started.");
        FreshUpdateDispatchEvidence? fresh = await evidence.ObserveAsync(operation, ownership, cancellationToken).ConfigureAwait(false);
        if (fresh is null)
            return Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null, "Fresh update evidence is unavailable or no longer actionable. No update was started.");
        UpdateDispatchAdmissionResult admitted = await admission.ClaimAsync(approval, fresh, ownership, cancellationToken).ConfigureAwait(false);
        if (admitted is { Permit: null, CommittedClaim: { } orphan })
            return await FinalizeAsync(orphan, ownership, UpdateDispatchSettlement.BeforeInvocation(orphan, ownership), UpdateDispatchOutcome.Cancelled,
                null, "Review, ownership, fresh evidence or cancellation lapsed right after the claim committed; nothing was invoked.",
                RecordedHelperCandidateCheck.NotObserved).ConfigureAwait(false);
        if (admitted.Permit is not { } permit)
            return await AfterGateFailureAsync(operation, "The fresh evidence no longer matches the frozen review, or the one-use claim was not granted.").ConfigureAwait(false);

        UpdateDispatchClaim claim = permit.Claim;
        if (!permit.TryConsume(approval, ownership))
            return await FinalizeAsync(claim, ownership, UpdateDispatchSettlement.BeforeInvocation(claim, ownership), UpdateDispatchOutcome.Cancelled,
                null, "Review, ownership or fresh evidence lapsed immediately before launch; nothing was invoked.",
                RecordedHelperCandidateCheck.NotObserved).ConfigureAwait(false);
        if (!StillSupported(operation))
            return await FinalizeAsync(claim, ownership, UpdateDispatchSettlement.BeforeInvocation(claim, ownership), UpdateDispatchOutcome.Cancelled,
                null, "The provider route became unavailable immediately before launch; nothing was invoked.",
                RecordedHelperCandidateCheck.NotObserved).ConfigureAwait(false);

        ReviewedUpdateDispatchObservation observation;
        try
        {
            observation = await dispatcher.DispatchAsync(new ReviewedUpdateDispatchTicket(claim), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Dispatcher returned no observation.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            observation = new(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Failed, null,
                "dispatch-interrupted; invocation-unknown", Task.CompletedTask, false, false, false, false, true);
        }

        UpdateDispatchSettlement settlement = Settle(claim, ownership, observation);
        // Desktop-derived history data only; it is chosen after, and never feeds, settlement or classification.
        RecordedHelperCandidateCheck helperCheck = observation.HelperCheck ?? RecordedHelperCandidateCheck.NotObserved;
        if (!settlement.ProvesSettlement)
            return await FinalizeAsync(claim, ownership, settlement, UpdateDispatchOutcome.Uncertain, observation.ExitCode,
                UnsettledMessage(operation, observation), helperCheck).ConfigureAwait(false);
        if (settlement.State == UpdateDispatchSettlementState.NotInvoked)
            return await FinalizeAsync(claim, ownership, settlement, observation.Status switch
            {
                ReviewedUpdateProviderStatus.Cancelled => UpdateDispatchOutcome.Cancelled,
                ReviewedUpdateProviderStatus.ElevationDenied => UpdateDispatchOutcome.ElevationDenied,
                ReviewedUpdateProviderStatus.Refused => UpdateDispatchOutcome.Refused,
                _ => UpdateDispatchOutcome.Failed,
            }, null, WithClientRunFaultWarning(operation, observation, $"Nothing was invoked. {Bounded(observation.Diagnostic)}"), helperCheck)
                .ConfigureAwait(false);

        (UpdateDispatchOutcome outcome, string message) =
            await ClassifyAfterSettlementAsync(operation, ownership, observation, cancellationToken).ConfigureAwait(false);
        return await FinalizeAsync(claim, ownership, settlement, outcome, observation.ExitCode, WithLingeringMarker(operation, observation, message), helperCheck)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Design rev 6 rev-4 L4 (N6): when the frozen route is <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>
    /// (the only route whose helper reports the settle window), the observation says the update was started, and its
    /// diagnostic carries the helper's fixed <see cref="ReviewedUpdateLingeringProcesses.Token"/>, the recorded message
    /// starts with it, so history can show the fixed note beside every outcome and truncation never drops it (the verified
    /// path otherwise drops the diagnostic). A not-started path never carries it. Recorded text only; it never changes the
    /// outcome, settlement or barrier.
    /// </summary>
    internal static string WithLingeringMarker(ReviewedUpdateOperation operation, ReviewedUpdateDispatchObservation observation, string message) =>
        // ADR-031 design §6/§8: the client self-update's helper reports the same settle window, so it is marked too.
        operation.RouteContract is UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 or UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
        observation.Invocation == ReviewedUpdateInvocation.Started &&
        ReviewedUpdateLingeringProcesses.IsReported(observation.Diagnostic)
            ? ReviewedUpdateLingeringProcesses.Token + " " + message
            : message;

    /// <summary>
    /// ADR-031 design §8.3, downgrade-only lattice for a settled, started client self-update (applied before any provider-status
    /// mapping or verification): a helper-reported run fault other than None is Unverified with the fault's fixed recovery
    /// message; a missing value (no accepted reply reached the workflow with one) is Unverified with the fixed "not reported"
    /// message. Both moves are downward (Success/RestartRequired and Failed ⊐ Unverified) and verification is not attempted, so
    /// nothing here can produce Success. Null for any other route or for the None value (normal classification, whose
    /// verification for this route goes through <see cref="IReviewedClientUpdateVerifier"/> with the same value).
    /// </summary>
    internal static (UpdateDispatchOutcome Outcome, string Message)? ClientRunFaultOutcome(ReviewedUpdateOperation operation,
        ReviewedUpdateDispatchObservation observation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(observation);
        if (operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1) return null;
        if (observation.ClientRunFault is not { } fault) return (UpdateDispatchOutcome.Unverified, ChocolateyClientUpdatePresentation.RunFaultNotReportedMessage);
        return ChocolateyClientUpdatePresentation.RunFaultMessage(fault) is { } message ? (UpdateDispatchOutcome.Unverified, message) : null;
    }

    /// <summary>
    /// ADR-031 design §8.3: on paths whose outcome the field never changes (Uncertain; and, defensively, NotInvoked, which S14c
    /// makes impossible), a client self-update run fault other than None still appends its fixed recovery message, so no
    /// required warning is dropped. Every other route and the None value leave the message unchanged.
    /// </summary>
    internal static string WithClientRunFaultWarning(ReviewedUpdateOperation operation, ReviewedUpdateDispatchObservation observation, string message) =>
        operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 && observation.ClientRunFault is { } fault &&
        ChocolateyClientUpdatePresentation.RunFaultMessage(fault) is { } warning
            ? message + " " + warning
            : message;

    /// <summary>
    /// The recorded message of an unsettled (Uncertain) dispatch: the fixed prefix and bounded diagnostic, a client run fault's
    /// recovery message (<see cref="WithClientRunFaultWarning"/>), otherwise the lost-track caution
    /// (<see cref="WithClientLostTrackCaution"/>), and the lingering marker. Never changes the outcome.
    /// </summary>
    internal static string UnsettledMessage(ReviewedUpdateOperation operation, ReviewedUpdateDispatchObservation observation) =>
        WithLingeringMarker(operation, observation, WithClientLostTrackCaution(operation, observation, WithClientRunFaultWarning(operation, observation,
            $"Owned work or invocation is unresolved; conflicting work stays blocked and nothing is replayed. {Bounded(observation.Diagnostic)}")));

    /// <summary>
    /// Independent review M1 (ADR-031 design §8.6), Uncertain path only: when a client self-update's launch may have started
    /// (invocation Unknown or Started) but no watchdog stop or release fault reached the workflow (run fault None or missing,
    /// for example a helper timeout, throw or unknown result after launch, or a lost reply), the watchdog's verdict is unknown,
    /// so the fixed <see cref="ChocolateyClientUpdatePresentation.LostTrackCautionMessage"/> is appended. A reported fault keeps
    /// its own message (<see cref="WithClientRunFaultWarning"/>); every other route and a not-started invocation are unchanged.
    /// </summary>
    internal static string WithClientLostTrackCaution(ReviewedUpdateOperation operation, ReviewedUpdateDispatchObservation observation, string message) =>
        operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
        observation.Invocation is ReviewedUpdateInvocation.Unknown or ReviewedUpdateInvocation.Started &&
        (observation.ClientRunFault is null || observation.ClientRunFault.IsNone)
            ? message + " " + ChocolateyClientUpdatePresentation.LostTrackCautionMessage
            : message;

    /// <summary>Every <see cref="IReviewedUpdateRouteDispatcher.Supports"/> call goes through here; any exception means unsupported.</summary>
    private bool StillSupported(ReviewedUpdateOperation operation)
    {
        try { return dispatcher.Supports(operation); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static UpdateDispatchSettlement Settle(UpdateDispatchClaim claim, OperationOwnership ownership, ReviewedUpdateDispatchObservation observation)
    {
        bool provenNotStarted = observation.Invocation == ReviewedUpdateInvocation.NotStarted && !observation.ExecutionUncertain &&
            !observation.TerminationFailed && observation.ExitCode is null && observation.OwnedRoot is { IsCompletedSuccessfully: true };
        if (provenNotStarted) return UpdateDispatchSettlement.BeforeInvocation(claim, ownership);
        return UpdateDispatchSettlement.FromObservedWork(claim, ownership, observation.OwnedRoot ?? Task.FromException(new InvalidOperationException("missing owned root")),
            observation.DirectExitObserved, observation.OwnedIoComplete, observation.PipeEndOfFile, observation.TerminationFailed,
            observation.ExecutionUncertain || observation.Invocation != ReviewedUpdateInvocation.Started);
    }

    /// <summary>
    /// The outcome of positively settled, invoked work: the client self-update's downgrade-only run-fault rule
    /// (<see cref="ClientRunFaultOutcome"/>) first, then the provider-status mapping and verification. Internal so the client
    /// route's lattice can be exercised directly by tests.
    /// </summary>
    internal async Task<(UpdateDispatchOutcome Outcome, string Message)> ClassifyAfterSettlementAsync(ReviewedUpdateOperation operation,
        OperationOwnership ownership, ReviewedUpdateDispatchObservation observation, CancellationToken cancellationToken) =>
        ClientRunFaultOutcome(operation, observation) ??
            await ClassifySettledAsync(operation, ownership, observation, cancellationToken).ConfigureAwait(false);

    private async Task<(UpdateDispatchOutcome, string)> ClassifySettledAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        ReviewedUpdateDispatchObservation observation, CancellationToken cancellationToken)
    {
        string diagnostic = Bounded(observation.Diagnostic);
        string exit = observation.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
        switch (observation.Status)
        {
            case ReviewedUpdateProviderStatus.ElevationDenied:
                return (UpdateDispatchOutcome.ElevationDenied, $"Administrator approval was declined. {diagnostic}");
            case ReviewedUpdateProviderStatus.Refused:
                return (UpdateDispatchOutcome.Refused, $"The update was refused before changing the application. {diagnostic}");
            case ReviewedUpdateProviderStatus.ProviderFailed:
                return (UpdateDispatchOutcome.Failed, $"The provider reported failure (exit {exit}). {diagnostic}");
            case ReviewedUpdateProviderStatus.Cancelled or ReviewedUpdateProviderStatus.TimedOut or ReviewedUpdateProviderStatus.Failed:
                // Started work may have partly applied; never report it as not updated.
                return (UpdateDispatchOutcome.Unverified, $"{StoppedAfterStartMessage(observation.Status)} {diagnostic}");
        }
        // A restart report is honoured only with the standard reboot-required codes; any other non-zero exit is failure.
        bool restartReported = observation.RestartReported && observation.ExitCode is 3010 or 1641;
        if (observation.RestartReported && !restartReported && observation.ExitCode == 0)
            return (UpdateDispatchOutcome.Unverified, $"The provider reported a restart with a success exit code; the inconsistent result is unverified. {diagnostic}");
        if (observation.ExitCode is null)
            return (UpdateDispatchOutcome.Unverified, $"{ExitCodeNotObservedMessage} {diagnostic}");
        if (observation.ExitCode != 0 && !restartReported)
            return (UpdateDispatchOutcome.Failed, $"The provider exited with code {exit}. {diagnostic}");
        try
        {
            PostUpdateInventoryResult verification = await VerifyAsync(operation, ownership, observation, cancellationToken).ConfigureAwait(false);
            if (RejectVerification(operation, verification) is { } rejected) return rejected;
            // AC-UPD-07: a non-zero (restart-requested) provider result is never Success, even if the version is detected.
            if (restartReported)
                return verification.RestartPending
                    ? (UpdateDispatchOutcome.RestartRequired, "The installer requested a restart and a reliable restart-pending state was detected.")
                    : (UpdateDispatchOutcome.Unverified, verification.ExpectedVersionDetected
                        ? "The target version was detected, but the installer's restart request could not be confirmed as a reliable restart-pending state."
                        : "The installer reported that a restart is needed, but neither the target version nor a reliable restart-pending state was verified.");
            return verification.ExpectedVersionDetected
                ? (UpdateDispatchOutcome.Success, verification.Note == PostUpdateVerificationNote.ClientReviewedLeftover
                    ? TargetVerifiedWithLeftoverMessage : TargetVerifiedMessage)
                : verification.RestartPending
                    ? (UpdateDispatchOutcome.RestartRequired, "Provider completed; a reliable restart-pending state was detected.")
                    : (UpdateDispatchOutcome.Unverified, TargetNotDetectedMessage);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (UpdateDispatchOutcome.Unverified, VerificationDidNotFinishMessage);
        }
    }

    /// <summary>
    /// The route's verification. ADR-031 design §8.3: a client self-update is verified only through
    /// <see cref="IReviewedClientUpdateVerifier.VerifyClientAsync"/> with the accepted reply's run-fault value (None here, since
    /// <see cref="ClientRunFaultOutcome"/> already lowered every other value); a missing value or a verifier without that
    /// interface is Unverified with the fixed "not reported" detail, never Success.
    /// </summary>
    private Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        ReviewedUpdateDispatchObservation observation, CancellationToken cancellationToken)
    {
        if (operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1)
            return verifier.VerifyAsync(operation, ownership, cancellationToken);
        return verifier is IReviewedClientUpdateVerifier client && observation.ClientRunFault is { } fault
            ? client.VerifyClientAsync(operation, ownership, fault, cancellationToken)
            : Task.FromResult(new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientUnverifiable));
    }

    /// <summary>
    /// Fact extension rev 3 §4.3 (L5), applied before any Success/RestartRequired mapping: a detail other than None (or an
    /// undefined one) is Unverified with the fixed message, never Success; a restart-pending result on
    /// <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/> is a contract violation and Unverified, never
    /// RestartRequired. A missing result is Unverified. Null means the verification may be mapped normally.
    /// </summary>
    internal static (UpdateDispatchOutcome Outcome, string Message)? RejectVerification(ReviewedUpdateOperation operation,
        PostUpdateInventoryResult? verification)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (verification is null) return (UpdateDispatchOutcome.Unverified, VerificationDidNotFinishMessage);
        if (verification.Detail != PostUpdateVerificationDetail.None)
            return (UpdateDispatchOutcome.Unverified, operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1
                ? ChocolateyClientUpdatePresentation.NotVerifiedMessage(verification.Detail) : ClientNotVerifiedMessage);
        // ADR-031 design §3a: a disclosure note exists only for the client self-update; any other note is a contract violation.
        if (verification.Note != PostUpdateVerificationNote.None &&
            (verification.Note != PostUpdateVerificationNote.ClientReviewedLeftover ||
             operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1))
            return (UpdateDispatchOutcome.Unverified, ClientNotVerifiedMessage);
        // ADR-031: a client self-update is Success only on the exact target manifest; restart-pending is never Success either.
        if (operation.RouteContract is UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 or UpdateRouteContract.ChocolateyClientSelfUpdateV1 &&
            verification.RestartPending)
            return (UpdateDispatchOutcome.Unverified, RestartContractViolationMessage);
        return null;
    }

    private async Task<ReviewedUpdateDispatchResult> FinalizeAsync(UpdateDispatchClaim claim, OperationOwnership ownership,
        UpdateDispatchSettlement settlement, UpdateDispatchOutcome outcome, int? exitCode, string message, RecordedHelperCandidateCheck helperCheck)
    {
        ReviewedUpdateOperation operation = claim.Draft.Operation;
        try
        {
            DateTimeOffset now = clock.GetUtcNow();
            // An inconsistent helper check is never written and never fails the write (which would change the outcome to
            // Uncertain); the fixed marker is a prefix so the truncation below can never drop it.
            RecordedHelperCandidateCheck recorded = helperCheck;
            if (!helperCheck.IsConsistentWith(operation, outcome, settlement.State))
            {
                recorded = RecordedHelperCandidateCheck.NotObserved;
                message = HelperCheckNotRecordedDiagnostic + " " + message;
            }
            string bounded = SanitizedDiagnosticBundle.Truncate(SanitizedDiagnosticBundle.Sanitize(message), MaximumTerminalMessageLength);
            UpdateDispatchTerminal terminal = new(outcome, now < claim.ClaimedAt ? claim.ClaimedAt : now, exitCode, bounded, recorded);
            await store.RecordFinalAsync(claim, ownership, terminal, settlement, CancellationToken.None).ConfigureAwait(false);
            return Result(operation, outcome == UpdateDispatchOutcome.Uncertain ? ReviewedUpdateDispatchDisposition.Uncertain : ReviewedUpdateDispatchDisposition.Finalized,
                outcome, terminal.Diagnostic) with { HelperCheck = recorded.Kind == RecordedHelperCheckKind.NotObserved ? null : recorded };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Result(operation, ReviewedUpdateDispatchDisposition.Uncertain, UpdateDispatchOutcome.Uncertain,
                "The outcome could not be recorded; the committed dispatch claim remains a conflicting-work barrier. " + SanitizedDiagnosticBundle.Sanitize(message));
        }
    }

    private static string Bounded(string? diagnostic)
    {
        return SanitizedDiagnosticBundle.Truncate(SanitizedDiagnosticBundle.Sanitize(diagnostic), ReviewedUpdateDispatchObservation.MaximumDiagnosticLength);
    }

    private static ReviewedUpdateDispatchResult Result(ReviewedUpdateOperation operation, ReviewedUpdateDispatchDisposition disposition,
        UpdateDispatchOutcome? outcome, string message) =>
        new(disposition, outcome, SanitizedDiagnosticBundle.Sanitize(message), operation.Mode, operation.RouteContract,
            operation.ProviderGuardContract, ReviewedUpdateProviderDisclosure.GetRiskText(operation));
}
