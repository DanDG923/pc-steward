using PCSteward.Domain;

namespace PCSteward.Application;

public enum ReviewedUpdateInvocation { Unknown, NotStarted, Started }

/// <summary>Provider/helper status as observed by a dispatcher. None of these values asserts verified success.</summary>
public enum ReviewedUpdateProviderStatus { Completed, ProviderFailed, Refused, ElevationDenied, Cancelled, TimedOut, Failed }

/// <summary>
/// Native observations returned by a route dispatcher. The owned root is an immutable snapshot that the workflow never
/// awaits again; unknown invocation is always unsettled. No member is execution authority or a verified outcome.
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
    bool RestartReported = false)
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
/// One closed route adapter. <see cref="Supports"/> is a compiled route/guard/mode/provider/source/scope check, never
/// eligibility evidence; the native runner or helper behind <see cref="DispatchAsync"/> must independently revalidate.
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

public enum ReviewedUpdateDispatchDisposition { NotStarted, NotStartedClaimRetained, Finalized, Uncertain }

/// <summary>Presentation-safe result that keeps the frozen reviewed mode/guard/disclosure with the outcome.</summary>
public sealed record ReviewedUpdateDispatchResult(
    ReviewedUpdateDispatchDisposition Disposition,
    UpdateDispatchOutcome? Outcome,
    string Message,
    ReviewedInstallerMode Mode,
    UpdateRouteContract Route,
    ReviewedProviderGuardContract GuardContract,
    string? Disclosure);

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

    public async Task<ReviewedUpdateDispatchResult> RunAsync(LiveApprovedUpdate approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ReviewedUpdateOperation operation = approval.Operation;
        if (!approval.IsCurrent)
            return Result(operation, ReviewedUpdateDispatchDisposition.NotStarted, null, "The review expired or was cancelled. Open a new review; no update was started.");
        if (!ReviewedUpdateReview.HasSupportedMode(operation) || !dispatcher.Supports(operation))
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
                null, "Review, ownership, fresh evidence or cancellation lapsed right after the claim committed; nothing was invoked.").ConfigureAwait(false);
        if (admitted.Permit is not { } permit)
            return await AfterGateFailureAsync(operation, "The fresh evidence no longer matches the frozen review, or the one-use claim was not granted.").ConfigureAwait(false);

        UpdateDispatchClaim claim = permit.Claim;
        if (!permit.TryConsume(approval, ownership))
            return await FinalizeAsync(claim, ownership, UpdateDispatchSettlement.BeforeInvocation(claim, ownership), UpdateDispatchOutcome.Cancelled,
                null, "Review, ownership or fresh evidence lapsed immediately before launch; nothing was invoked.").ConfigureAwait(false);
        if (!StillSupported(operation))
            return await FinalizeAsync(claim, ownership, UpdateDispatchSettlement.BeforeInvocation(claim, ownership), UpdateDispatchOutcome.Cancelled,
                null, "The provider route became unavailable immediately before launch; nothing was invoked.").ConfigureAwait(false);

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
        if (!settlement.ProvesSettlement)
            return await FinalizeAsync(claim, ownership, settlement, UpdateDispatchOutcome.Uncertain, observation.ExitCode,
                $"Owned work or invocation is unresolved; conflicting work stays blocked and nothing is replayed. {Bounded(observation.Diagnostic)}").ConfigureAwait(false);
        if (settlement.State == UpdateDispatchSettlementState.NotInvoked)
            return await FinalizeAsync(claim, ownership, settlement, observation.Status switch
            {
                ReviewedUpdateProviderStatus.Cancelled => UpdateDispatchOutcome.Cancelled,
                ReviewedUpdateProviderStatus.ElevationDenied => UpdateDispatchOutcome.ElevationDenied,
                ReviewedUpdateProviderStatus.Refused => UpdateDispatchOutcome.Refused,
                _ => UpdateDispatchOutcome.Failed,
            }, null, $"Nothing was invoked. {Bounded(observation.Diagnostic)}").ConfigureAwait(false);

        (UpdateDispatchOutcome outcome, string message) = await ClassifySettledAsync(operation, ownership, observation, cancellationToken).ConfigureAwait(false);
        return await FinalizeAsync(claim, ownership, settlement, outcome, observation.ExitCode, message).ConfigureAwait(false);
    }

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
                return (UpdateDispatchOutcome.Unverified, $"{observation.Status} after the provider started; the resulting version is unverified. {diagnostic}");
        }
        // A restart report is honoured only with the standard reboot-required codes; any other non-zero exit is failure.
        bool restartReported = observation.RestartReported && observation.ExitCode is 3010 or 1641;
        if (observation.ExitCode is null)
            return (UpdateDispatchOutcome.Unverified, $"The provider ran but its exit code was not observed; the resulting version is unverified. {diagnostic}");
        if (observation.ExitCode != 0 && !restartReported)
            return (UpdateDispatchOutcome.Failed, $"The provider exited with code {exit}. {diagnostic}");
        try
        {
            PostUpdateInventoryResult verification = await verifier.VerifyAsync(operation, ownership, cancellationToken).ConfigureAwait(false);
            // AC-UPD-07: a non-zero (restart-requested) provider result is never Success, even if the version is detected.
            if (restartReported)
                return verification.RestartPending
                    ? (UpdateDispatchOutcome.RestartRequired, "The installer requested a restart and a reliable restart-pending state was detected.")
                    : (UpdateDispatchOutcome.Unverified, verification.ExpectedVersionDetected
                        ? "The target version was detected, but the installer's restart request could not be confirmed as a reliable restart-pending state."
                        : "The installer reported that a restart is needed, but neither the target version nor a reliable restart-pending state was verified.");
            return verification.ExpectedVersionDetected
                ? (UpdateDispatchOutcome.Success, "Target version verified by post-update inventory.")
                : verification.RestartPending
                    ? (UpdateDispatchOutcome.RestartRequired, "Provider completed; a reliable restart-pending state was detected.")
                    : (UpdateDispatchOutcome.Unverified, "Provider completed but the target version was not detected.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (UpdateDispatchOutcome.Unverified, "Provider completed but post-update verification did not finish.");
        }
    }

    private async Task<ReviewedUpdateDispatchResult> FinalizeAsync(UpdateDispatchClaim claim, OperationOwnership ownership,
        UpdateDispatchSettlement settlement, UpdateDispatchOutcome outcome, int? exitCode, string message)
    {
        ReviewedUpdateOperation operation = claim.Draft.Operation;
        try
        {
            DateTimeOffset now = clock.GetUtcNow();
            string bounded = SanitizedDiagnosticBundle.Truncate(SanitizedDiagnosticBundle.Sanitize(message), MaximumTerminalMessageLength);
            UpdateDispatchTerminal terminal = new(outcome, now < claim.ClaimedAt ? claim.ClaimedAt : now, exitCode, bounded);
            await store.RecordFinalAsync(claim, ownership, terminal, settlement, CancellationToken.None).ConfigureAwait(false);
            return Result(operation, outcome == UpdateDispatchOutcome.Uncertain ? ReviewedUpdateDispatchDisposition.Uncertain : ReviewedUpdateDispatchDisposition.Finalized,
                outcome, terminal.Diagnostic);
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
