using System.ComponentModel;
using System.Text.RegularExpressions;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Execution;

public enum UpdateExecutionStatus
{
    Denied,
    ElevationDenied,
    ElevationRequired,
    Cancelled,
    TimedOut,
    OutputLimitExceeded,
    ProductFailed,
    HelperFailed,
    ProviderFailed,
    AwaitingVerification,
    VerificationRequired
}

public enum UpdateExecutionDenial
{
    None,
    IntegrityCheckFailed,
    CurrentEvidenceMismatch,
    UnsupportedProvider,
    UnsupportedScope
}

public sealed record UpdateExecutionResult(
    Guid JobId,
    Guid CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    UpdateProvider Provider,
    string PackageId,
    string InstalledVersion,
    string TargetVersion,
    int? ExitCode,
    UpdateExecutionStatus Status,
    UpdateExecutionDenial Denial,
    string SanitizedOutput,
    Task? OwnedCompletion = null);

/// <summary>Runs only a confirmed user-scope job. Exit zero remains awaiting verification, never Success.</summary>
public sealed partial class NonElevatedUpdateRunner
{
    private const int MaximumDiagnosticLength = 4096;
    private readonly IWingetUpdateExecutor winget;
    private readonly IUpdateExecutionEventSink events;
    private readonly Func<DateTimeOffset> utcNow;

    public NonElevatedUpdateRunner(
        IWingetUpdateExecutor winget,
        IUpdateExecutionEventSink? events = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.winget = winget ?? throw new ArgumentNullException(nameof(winget));
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

        if (job.Selection.Provider != UpdateProvider.WinGet)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedProvider, "unsupported-provider");
        }

        if (job.Selection.Scope == UpdateScope.Machine)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.ElevationRequired, UpdateExecutionDenial.None, "elevation-required");
        }

        if (job.Selection.Scope != UpdateScope.User)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Denied, UpdateExecutionDenial.UnsupportedScope, "unsupported-scope");
        }

        if (cancellationToken.IsCancellationRequested)
            return Complete(job, startedAt, null, UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "operation-cancelled");

        Write(job, startedAt, UpdateExecutionEventSeverity.Information, "update.dispatch-requested", "Provider execution requested.");
        try
        {
            WingetProcessResult providerResult = await winget.ExecuteAsync(job, cancellationToken).ConfigureAwait(false);
            string output = Sanitize(string.Join(' ', providerResult.StandardOutput, providerResult.StandardError));
            if (providerResult.OutputLimitExceeded)
            {
                return Complete(job, startedAt, providerResult.ExitCode, UpdateExecutionStatus.OutputLimitExceeded, UpdateExecutionDenial.None, output, providerResult.OwnedCompletion);
            }

            if (providerResult.TimedOut)
            {
                return Complete(job, startedAt, providerResult.ExitCode, UpdateExecutionStatus.TimedOut, UpdateExecutionDenial.None, output, providerResult.OwnedCompletion);
            }

            if (providerResult.Cancelled)
                return Complete(job, startedAt, providerResult.ExitCode, UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None,
                    $"operation-cancelled; {output}", providerResult.OwnedCompletion);
            if (providerResult.OutputIncomplete || providerResult.ExecutionUncertain)
                return Complete(job, startedAt, providerResult.ExitCode, UpdateExecutionStatus.VerificationRequired, UpdateExecutionDenial.None,
                    $"provider-result-incomplete; verification-required; {output}", providerResult.OwnedCompletion);

            UpdateExecutionStatus status = providerResult.ExitCode == 0 ? UpdateExecutionStatus.AwaitingVerification : UpdateExecutionStatus.ProviderFailed;
            return Complete(job, startedAt, providerResult.ExitCode, status, UpdateExecutionDenial.None, output, providerResult.OwnedCompletion);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.Cancelled, UpdateExecutionDenial.None, "operation-cancelled");
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        {
            return Complete(job, startedAt, null, UpdateExecutionStatus.ProductFailed, UpdateExecutionDenial.None, exception.Message);
        }
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
        string sanitized = Sanitize(diagnostic);
        UpdateExecutionEventSeverity severity = status switch
        {
            UpdateExecutionStatus.AwaitingVerification => UpdateExecutionEventSeverity.Information,
            UpdateExecutionStatus.ProductFailed or UpdateExecutionStatus.HelperFailed or UpdateExecutionStatus.ProviderFailed or UpdateExecutionStatus.TimedOut or UpdateExecutionStatus.OutputLimitExceeded => UpdateExecutionEventSeverity.Error,
            _ => UpdateExecutionEventSeverity.Warning
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

    private void Write(ImmutableUpdateJob job, DateTimeOffset occurredAt, UpdateExecutionEventSeverity severity, string eventId, string message) =>
        events.Write(new UpdateExecutionEvent(
            job.Id,
            job.CorrelationId,
            occurredAt,
            severity,
            eventId,
            job.Selection.Provider,
            job.Selection.PackageId,
            job.Selection.CandidateVersion,
            Sanitize(message)));

    public static string Sanitize(string value)
    {
        string bounded = value ?? string.Empty;
        bounded = SensitiveValue().Replace(bounded, "$1=[redacted]");
        bounded = WindowsPath().Replace(bounded, "[path]");
        bounded = new string(bounded.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        bounded = Whitespace().Replace(bounded, " ").Trim();
        return bounded[..Math.Min(bounded.Length, MaximumDiagnosticLength)];
    }

    [GeneratedRegex(@"(?i)\b(password|token|secret|licen[cs]e[-_ ]?key)\s*[:=]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValue();

    [GeneratedRegex(@"(?i)(?:[A-Z]:\\|\\\\)[^\s""']+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
