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
    private readonly IUpdateExecutionEventSink events;
    private readonly Func<DateTimeOffset> utcNow;

    public ElevatedUpdateRunner()
        : this(new WindowsElevatedHelperTransport(), null, null)
    {
    }

    internal ElevatedUpdateRunner(
        IElevatedHelperTransport transport,
        IUpdateExecutionEventSink? events = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
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
