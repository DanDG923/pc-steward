using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Execution;

/// <summary>
/// Runs an approved whole-PC WinGet job on the elevated helper. Phase D stage D4b, stage 3 (design note §8): protocol 1 is retired, so
/// the only path is the job's frozen <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation on the protocol-2 helper
/// (<see cref="RunReviewedAsync"/>). The exchange surface has no executable, command, path, argument, environment, or output-path input.
/// </summary>
public sealed class ElevatedUpdateRunner
{
    private readonly IWingetMachineReviewedExchange reviewedExchange;
    private readonly IUpdateExecutionEventSink events;
    private readonly Func<DateTimeOffset> utcNow;

    internal ElevatedUpdateRunner(
        IWingetMachineReviewedExchange reviewedExchange,
        IUpdateExecutionEventSink? events = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.reviewedExchange = reviewedExchange ?? throw new ArgumentNullException(nameof(reviewedExchange));
        this.events = events ?? NullUpdateExecutionEventSink.Instance;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// D4b stage 2c (design §5.4): runs an approved whole-PC WinGet job only through its frozen
    /// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation on the protocol-2 helper. The operation must be admitted and
    /// bound to this exact job, and the job must still match the current evidence. WinGet's documented restart exit codes are reported
    /// as a restart (Task 51); inventory still decides.
    /// </summary>
    public async Task<UpdateExecutionResult> RunReviewedAsync(
        ImmutableUpdateJob job,
        ReviewedUpdateOperation reviewed,
        UpdateJobExecutionEvidence currentEvidence,
        CancellationToken cancellationToken,
        UpdateItemProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(currentEvidence);
        DateTimeOffset startedAt = utcNow();
        if (!job.HasValidDigest)
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.IntegrityCheckFailed, "job-integrity-check-failed");
        if (!job.Matches(currentEvidence))
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.CurrentEvidenceMismatch, "current-update-evidence-mismatch");
        if (!WingetMachineReviewedRoute.Admits(reviewed) || !reviewed.BindsJob(job))
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.ReviewedModeUnavailable, "reviewed-machine-route-unavailable");
        if (cancellationToken.IsCancellationRequested)
            return Complete(job, startedAt, null, UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "operation-cancelled");

        Write(job, startedAt, UpdateExecutionEventSeverity.Information, "update.elevation-preparing", "Preparing protected helper validation.");
        // The protocol-2 transport reports no progress of its own: elevation is requested now, and the helper had the work once it
        // replied with anything but a not-started result.
        progress?.ElevationRequested();
        ReviewedUpdateDispatchObservation observation;
        try
        {
            observation = await reviewedExchange.RunAsync(reviewed, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The exchange may have started the helper: never claim that nothing ran, and keep the owned work unresolved.
            observation = new(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Failed, null, "dispatch-interrupted; invocation-unknown",
                Pending, false, false, false, false, ExecutionUncertain: true);
        }
        if (observation.Invocation != ReviewedUpdateInvocation.NotStarted) progress?.HandedToHelper();
        (UpdateExecutionStatus status, UpdateExecutionDenial denial, string fallback) = MapReviewed(observation);
        string diagnostic = string.IsNullOrWhiteSpace(observation.Diagnostic) ? fallback : $"{fallback}; {observation.Diagnostic}";
        if (status is UpdateExecutionStatus.RestartReported or UpdateExecutionStatus.ProviderFailed && observation.ExitCode is { } code &&
            WingetRestartExitCodes.DiagnosticPrefix(code) is { Length: > 0 } restartPrefix)
            diagnostic = $"{restartPrefix}; {diagnostic}";
        return Complete(job, startedAt, observation.ExitCode, status, denial, diagnostic, observation.OwnedRoot);
    }

    /// <summary>
    /// The protocol-2 observation as a runner status. Work that may have started but did not settle is a helper failure whose owned
    /// root stays unresolved; a refusal before anything ran is a denial; an exit of 0 awaits inventory; WinGet's restart codes report a
    /// restart; any other exit is a provider failure.
    /// </summary>
    internal static (UpdateExecutionStatus Status, UpdateExecutionDenial Denial, string Fallback) MapReviewed(ReviewedUpdateDispatchObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Invocation == ReviewedUpdateInvocation.Unknown || observation.ExecutionUncertain)
            return (UpdateExecutionStatus.HelperFailed, UpdateExecutionDenial.None, "elevated-helper-unsettled");
        if (observation.Invocation == ReviewedUpdateInvocation.NotStarted)
            return observation.Status switch
            {
                ReviewedUpdateProviderStatus.Refused => (UpdateExecutionStatus.Denied, UpdateExecutionDenial.ReviewedModeUnavailable, "helper-refused-before-update"),
                ReviewedUpdateProviderStatus.ElevationDenied => (UpdateExecutionStatus.ElevationDenied, UpdateExecutionDenial.None, "user-declined-elevation"),
                ReviewedUpdateProviderStatus.Cancelled => (UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "operation-cancelled"),
                ReviewedUpdateProviderStatus.TimedOut => (UpdateExecutionStatus.TimedOut, UpdateExecutionDenial.None, "elevated-helper-timed-out"),
                _ => (UpdateExecutionStatus.HelperFailed, UpdateExecutionDenial.None, "elevated-helper-failed"),
            };
        return observation.Status switch
        {
            ReviewedUpdateProviderStatus.Completed when observation.ExitCode == 0 =>
                (UpdateExecutionStatus.AwaitingVerification, UpdateExecutionDenial.None, "helper-completed-awaiting-verification"),
            ReviewedUpdateProviderStatus.Completed or ReviewedUpdateProviderStatus.ProviderFailed
                when observation.ExitCode is { } exit && exit != 0 && WingetRestartExitCodes.ReportsFinishedPendingRestart(exit) =>
                (UpdateExecutionStatus.RestartReported, UpdateExecutionDenial.None, "provider-reported-restart"),
            ReviewedUpdateProviderStatus.Completed or ReviewedUpdateProviderStatus.ProviderFailed =>
                (UpdateExecutionStatus.ProviderFailed, UpdateExecutionDenial.None, "provider-failed"),
            ReviewedUpdateProviderStatus.Cancelled => (UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "operation-cancelled"),
            ReviewedUpdateProviderStatus.TimedOut => (UpdateExecutionStatus.TimedOut, UpdateExecutionDenial.None, "elevated-helper-timed-out"),
            _ => (UpdateExecutionStatus.HelperFailed, UpdateExecutionDenial.None, "elevated-helper-failed"),
        };
    }

    private static readonly Task Pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    private UpdateExecutionResult Complete(
        ImmutableUpdateJob job,
        DateTimeOffset startedAt,
        int? exitCode,
        UpdateExecutionStatus status,
        UpdateExecutionDenial denial,
        string diagnostic,
        Task? ownedCompletion = null)
    {
        DateTimeOffset endedAt = utcNow();
        string sanitized = NonElevatedUpdateRunner.Sanitize(diagnostic);
        UpdateExecutionEventSeverity severity = status switch
        {
            UpdateExecutionStatus.AwaitingVerification or UpdateExecutionStatus.RestartReported => UpdateExecutionEventSeverity.Information,
            UpdateExecutionStatus.ElevationDenied or UpdateExecutionStatus.Cancelled or UpdateExecutionStatus.Denied => UpdateExecutionEventSeverity.Warning,
            _ => UpdateExecutionEventSeverity.Error
        };
        Write(job, endedAt, severity, $"update.{status.ToString().ToLowerInvariant()}", sanitized);
        return new UpdateExecutionResult(
            job.Id,
            job.CorrelationId,
            startedAt,
            endedAt,
            job.Selection.Provider,
            job.Selection.PackageId,
            job.Selection.InstalledVersion,
            job.Selection.CandidateVersion,
            exitCode,
            status,
            denial,
            sanitized,
            ownedCompletion);
    }

    private void Write(
        ImmutableUpdateJob job,
        DateTimeOffset occurredAt,
        UpdateExecutionEventSeverity severity,
        string eventId,
        string message) =>
        events.Write(new UpdateExecutionEvent(
            job.Id,
            job.CorrelationId,
            occurredAt,
            severity,
            eventId,
            job.Selection.Provider,
            job.Selection.PackageId,
            job.Selection.CandidateVersion,
            NonElevatedUpdateRunner.Sanitize(message)));
}
