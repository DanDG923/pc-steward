using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Execution;

internal enum ElevatedHelperOutcome
{
    Completed,
    UserDenied,
    Cancelled,
    TimedOut,
    OutputLimitExceeded,
    ProviderFailed,
    Failed
}

internal sealed record ElevatedHelperResult(
    ElevatedHelperOutcome Outcome,
    int? ExitCode,
    string SanitizedOutput,
    Task? OwnedCompletion = null);

internal interface IElevatedHelperTransport
{
    Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
    Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
        ExecuteAsync(job, cancellationToken);
}

/// <summary>
/// Revalidates an approved machine-scope job before requesting the one-shot helper.
/// The transport surface has no executable, command, path, argument, environment, or output-path input.
/// </summary>
public sealed class ElevatedUpdateRunner
{
    private readonly IElevatedHelperTransport transport;
    private readonly IWingetMachineReviewedExchange? reviewedExchange;
    private readonly IUpdateExecutionEventSink events;
    private readonly Func<DateTimeOffset> utcNow;

    public ElevatedUpdateRunner()
        : this(new WindowsElevatedHelperTransport(), null, null)
    {
    }

    internal ElevatedUpdateRunner(
        IElevatedHelperTransport transport,
        IUpdateExecutionEventSink? events = null,
        Func<DateTimeOffset>? utcNow = null,
        IWingetMachineReviewedExchange? reviewedExchange = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.reviewedExchange = reviewedExchange;
        this.events = events ?? NullUpdateExecutionEventSink.Instance;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<UpdateExecutionResult> RunAsync(
        ImmutableUpdateJob job,
        UpdateJobExecutionEvidence currentEvidence,
        CancellationToken cancellationToken,
        UpdateItemProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(currentEvidence);
        DateTimeOffset startedAt = utcNow();

        if (!job.HasValidDigest)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.IntegrityCheckFailed, "job-integrity-check-failed");
        }

        if (!job.Matches(currentEvidence))
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.CurrentEvidenceMismatch, "current-update-evidence-mismatch");
        }

        // ADR-025 transition amendment (stage C4, Task 54 R2): protocol 1 carries machine-scope WinGet updates only; Chocolatey
        // updates run on their own reviewed protocol-2 route and never reach this runner's helper.
        if (job.Selection.Provider != UpdateProvider.WinGet)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedProvider, "unsupported-provider");
        }

        if (job.Selection.Scope != UpdateScope.Machine)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedScope, "elevated-helper-rejects-non-machine-scope");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "operation-cancelled");
        }

        Write(job, startedAt, UpdateExecutionEventSeverity.Information, "update.elevation-preparing", "Preparing protected helper validation.");
        ElevatedHelperResult helperResult;
        try
        {
            helperResult = await transport.ExecuteAsync(job, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            helperResult = new ElevatedHelperResult(ElevatedHelperOutcome.Cancelled, null, "operation-cancelled");
        }

        // Task 51: only a WinGet job's documented restart exit codes are restart evidence. Chocolatey keeps its own route contract,
        // under which a restart exit code is a failure (protocol-2 fact extension §9 O4).
        bool wingetExit = job.Selection.Provider == UpdateProvider.WinGet &&
            helperResult.Outcome is (ElevatedHelperOutcome.Completed or ElevatedHelperOutcome.ProviderFailed) &&
            helperResult.ExitCode is { } exitCode && exitCode != 0;
        (UpdateExecutionStatus status, string fallback) = helperResult.Outcome switch
        {
            ElevatedHelperOutcome.Completed when helperResult.ExitCode == 0 =>
                (UpdateExecutionStatus.AwaitingVerification, "helper-completed-awaiting-verification"),
            _ when wingetExit && WingetRestartExitCodes.ReportsFinishedPendingRestart(helperResult.ExitCode!.Value) =>
                (UpdateExecutionStatus.RestartReported, "provider-reported-restart"),
            ElevatedHelperOutcome.Completed or ElevatedHelperOutcome.ProviderFailed =>
                (UpdateExecutionStatus.ProviderFailed, "provider-failed"),
            ElevatedHelperOutcome.UserDenied =>
                (UpdateExecutionStatus.ElevationDenied, "user-declined-elevation"),
            ElevatedHelperOutcome.Cancelled =>
                (UpdateExecutionStatus.Cancelled, "operation-cancelled"),
            ElevatedHelperOutcome.TimedOut =>
                (UpdateExecutionStatus.TimedOut, "elevated-helper-timed-out"),
            ElevatedHelperOutcome.OutputLimitExceeded =>
                (UpdateExecutionStatus.OutputLimitExceeded, "provider-output-limit-exceeded"),
            _ =>
                (UpdateExecutionStatus.HelperFailed, "elevated-helper-failed")
        };

        string diagnostic = string.IsNullOrWhiteSpace(helperResult.SanitizedOutput)
            ? fallback
            : helperResult.SanitizedOutput;
        if (wingetExit && WingetRestartExitCodes.DiagnosticPrefix(helperResult.ExitCode!.Value) is { Length: > 0 } restartPrefix)
            diagnostic = $"{restartPrefix}; {diagnostic}";
        return Complete(job, startedAt, helperResult.ExitCode, status, UpdateExecutionDenial.None, diagnostic, helperResult.OwnedCompletion);
    }

    /// <summary>
    /// D4b stage 2c (design §5.4): runs an approved whole-PC WinGet job only through its frozen
    /// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation on the protocol-2 helper, never protocol 1. The operation
    /// must be admitted, bound to this exact job, and the job must still match the current evidence; a runner composed without the
    /// reviewed exchange refuses. WinGet's documented restart exit codes are reported as a restart (Task 51); inventory still decides.
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
        if (reviewedExchange is null || !WingetMachineReviewedRoute.Admits(reviewed) || !reviewed.BindsJob(job))
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
