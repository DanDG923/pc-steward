using PCSteward.Application;
using PCSteward.Domain;

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
    string SanitizedOutput);

internal interface IElevatedHelperTransport
{
    Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
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
        CancellationToken cancellationToken)
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

        if (!job.Selection.IsPlainUpgrade)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedOperation, "operation-requires-reviewed-batch");
        }

        if (job.Selection.Provider is not (UpdateProvider.WinGet or UpdateProvider.Chocolatey))
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedProvider, "unsupported-provider");
        }

        if (job.Selection.Scope != UpdateScope.Machine)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedScope, "elevated-helper-rejects-non-machine-scope");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "user-cancelled");
        }

        Write(job, startedAt, UpdateExecutionEventSeverity.Information, "update.elevation-requested", "Elevation requested at provider execution.");
        ElevatedHelperResult helperResult;
        try
        {
            helperResult = await transport.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            helperResult = new ElevatedHelperResult(ElevatedHelperOutcome.Cancelled, null, "user-cancelled");
        }

        (UpdateExecutionStatus status, string fallback) = helperResult.Outcome switch
        {
            ElevatedHelperOutcome.Completed when helperResult.ExitCode == 0 =>
                (UpdateExecutionStatus.AwaitingVerification, "helper-completed-awaiting-verification"),
            ElevatedHelperOutcome.Completed or ElevatedHelperOutcome.ProviderFailed =>
                (UpdateExecutionStatus.ProviderFailed, "provider-failed"),
            ElevatedHelperOutcome.UserDenied =>
                (UpdateExecutionStatus.ElevationDenied, "user-declined-elevation"),
            ElevatedHelperOutcome.Cancelled =>
                (UpdateExecutionStatus.Cancelled, "user-cancelled"),
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
        return Complete(job, startedAt, helperResult.ExitCode, status, UpdateExecutionDenial.None, diagnostic);
    }

    private UpdateExecutionResult Complete(
        ImmutableUpdateJob job,
        DateTimeOffset startedAt,
        int? exitCode,
        UpdateExecutionStatus status,
        UpdateExecutionDenial denial,
        string diagnostic)
    {
        DateTimeOffset endedAt = utcNow();
        string sanitized = NonElevatedUpdateRunner.Sanitize(diagnostic);
        UpdateExecutionEventSeverity severity = status switch
        {
            UpdateExecutionStatus.AwaitingVerification => UpdateExecutionEventSeverity.Information,
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
            sanitized);
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
