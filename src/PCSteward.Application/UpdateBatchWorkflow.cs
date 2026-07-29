using System.Text.Json;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Application;

public enum UpdateHistoryStatus
{
    Queued,
    Running,
    RunningApplicationWarning,
    VerificationRequired,
    Success,
    RestartRequired,
    Inconclusive,
    Cancelled,
    SourceFailed,
    DownloadFailed,
    InstallerFailed,
    ElevationDenied,
    TimedOut,
    VerificationFailed,
    ProductFailed
}

/// <summary>Append-only update history. It contains presentation-safe diagnostics only.</summary>
public sealed record UpdateHistoryEntry(
    Guid EventId,
    Guid JobId,
    Guid CorrelationId,
    string StableProductKey,
    UpdateProvider Provider,
    string PackageId,
    string InstalledVersion,
    string TargetVersion,
    DateTimeOffset OccurredAt,
    UpdateHistoryStatus Status,
    int? ExitCode,
    string Diagnostic);

public sealed record UpdateHistoryRetention(int MaximumEventsPerJob, DateTimeOffset RemoveVerboseBefore);

public interface IUpdateHistoryStore
{
    Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken);
    Task RecoverInterruptedAsync(CancellationToken cancellationToken);
    Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken);
}

public enum UpdateProviderAttemptStatus
{
    AwaitingVerification,
    Cancelled,
    SourceFailed,
    DownloadFailed,
    InstallerFailed,
    ElevationDenied,
    TimedOut,
    ProductFailed
}

/// <summary>Structured provider evidence; no process authority or command data crosses this boundary.</summary>
public sealed record UpdateProviderAttempt(UpdateProviderAttemptStatus Status, int? ExitCode, string SanitizedDiagnostic);

public interface IApprovedUpdateExecutor
{
    Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken);
}

public sealed record PostUpdateInventoryResult(bool ExpectedVersionDetected, bool RestartPending);

public interface IPostUpdateInventory
{
    Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
}

public sealed record RunningApplicationDetection(bool AppearsToBeRunning, IReadOnlyCollection<string> ProcessNames)
{
    public static RunningApplicationDetection NotDetected { get; } = new(false, []);
}

public interface IRunningApplicationDetector
{
    Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken);
}

public sealed record UpdateBatchItem(
    ImmutableUpdateJob Job,
    UpdateJobExecutionEvidence CurrentEvidence,
    bool RunningApplicationWarningAcknowledged = false);
public sealed record UpdateBatchResult(
    IReadOnlyCollection<UpdateHistoryEntry> Entries,
    RunningApplicationDetection? AcknowledgementRequired = null)
{
    public IReadOnlyCollection<UpdateHistoryEntry> FinalEntries => Entries
        .Where(entry => entry.Status is not UpdateHistoryStatus.Queued and not UpdateHistoryStatus.Running and not UpdateHistoryStatus.RunningApplicationWarning)
        .ToArray();
}

/// <summary>
/// Runs reviewed jobs one at a time. A cancellation token is observed before every unstarted item;
/// previously recorded outcomes are never rewritten and started work remains verification-required after interruption.
/// </summary>
public sealed class SequentialUpdateBatchCoordinator(
    IApprovedUpdateExecutor executor,
    IPostUpdateInventory inventory,
    IUpdateHistoryStore history,
    IRunningApplicationDetector runningApplications,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly IApprovedUpdateExecutor executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly IPostUpdateInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IUpdateHistoryStore history = history ?? throw new ArgumentNullException(nameof(history));
    private readonly IRunningApplicationDetector runningApplications = runningApplications ?? throw new ArgumentNullException(nameof(runningApplications));
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UpdateBatchResult> RunAsync(IReadOnlyCollection<UpdateBatchItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(item => item.Job is null || item.CurrentEvidence is null)) throw new ArgumentException("Each batch item needs immutable job and current evidence.", nameof(items));
        if (items.Select(item => item.Job.Id).Distinct().Count() != items.Count) throw new ArgumentException("A batch cannot contain the same job twice.", nameof(items));

        await history.RecoverInterruptedAsync(CancellationToken.None).ConfigureAwait(false);
        List<UpdateHistoryEntry> recorded = [];
        for (int index = 0; index < items.Count; index++)
        {
            UpdateBatchItem item = items.ElementAt(index);
            if (cancellationToken.IsCancellationRequested)
            {
                for (int cancelled = index; cancelled < items.Count; cancelled++)
                {
                    UpdateHistoryEntry entry = Entry(items.ElementAt(cancelled).Job, UpdateHistoryStatus.Cancelled, null, "user-cancelled-before-start");
                    await RecordAsync(entry, recorded, CancellationToken.None).ConfigureAwait(false);
                }
                break;
            }

            RunningApplicationDetection running = await runningApplications.DetectAsync(item.Job.Selection, cancellationToken).ConfigureAwait(false);
            if (running.AppearsToBeRunning)
            {
                await RecordAsync(Entry(item.Job, UpdateHistoryStatus.RunningApplicationWarning, null, "target-application-appears-to-be-running; no-close-action-requested"), recorded, cancellationToken).ConfigureAwait(false);
                if (!item.RunningApplicationWarningAcknowledged)
                {
                    return new(recorded, running);
                }
            }

            await RecordAsync(Entry(item.Job, UpdateHistoryStatus.Queued, null, "reviewed-job-queued"), recorded, cancellationToken).ConfigureAwait(false);
            await RecordAsync(Entry(item.Job, UpdateHistoryStatus.Running, null, "provider-execution-started"), recorded, cancellationToken).ConfigureAwait(false);
            UpdateProviderAttempt attempt;
            try
            {
                attempt = await executor.ExecuteAsync(item.Job, item.CurrentEvidence, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                attempt = new(UpdateProviderAttemptStatus.Cancelled, null, "user-cancelled");
            }
            catch (Exception)
            {
                attempt = new(UpdateProviderAttemptStatus.ProductFailed, null, "product-could-not-start-approved-update");
            }

            if (attempt.Status != UpdateProviderAttemptStatus.AwaitingVerification)
            {
                UpdateHistoryStatus finalStatus = attempt.Status == UpdateProviderAttemptStatus.Cancelled ? UpdateHistoryStatus.VerificationRequired : Map(attempt.Status);
                string diagnostic = attempt.Status == UpdateProviderAttemptStatus.Cancelled ? "provider-start-state-interrupted; verification-required" : attempt.SanitizedDiagnostic;
                await RecordAsync(Entry(item.Job, finalStatus, attempt.ExitCode, diagnostic), recorded, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            try
            {
                PostUpdateInventoryResult verification = await inventory.ReInventoryAsync(item.Job, cancellationToken).ConfigureAwait(false);
                UpdateHistoryStatus status = verification.ExpectedVersionDetected
                    ? UpdateHistoryStatus.Success
                    : verification.RestartPending ? UpdateHistoryStatus.RestartRequired : UpdateHistoryStatus.Inconclusive;
                string diagnostic = status switch
                {
                    UpdateHistoryStatus.Success => "target-version-verified-by-post-update-inventory",
                    UpdateHistoryStatus.RestartRequired => "provider-reported-reliable-restart-pending-state",
                    _ => "provider-succeeded-but-target-version-was-not-detected"
                };
                await RecordAsync(Entry(item.Job, status, attempt.ExitCode, diagnostic), recorded, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await RecordAsync(Entry(item.Job, UpdateHistoryStatus.VerificationRequired, attempt.ExitCode, "verification-interrupted-after-provider-completed"), recorded, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await RecordAsync(Entry(item.Job, UpdateHistoryStatus.VerificationFailed, attempt.ExitCode, "post-update-inventory-verification-failed"), recorded, CancellationToken.None).ConfigureAwait(false);
            }
        }

        return new(recorded);
    }

    private async Task RecordAsync(UpdateHistoryEntry entry, List<UpdateHistoryEntry> entries, CancellationToken cancellationToken)
    {
        await history.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        entries.Add(entry);
    }

    private UpdateHistoryEntry Entry(ImmutableUpdateJob job, UpdateHistoryStatus status, int? exitCode, string diagnostic) => new(
        Guid.NewGuid(), job.Id, job.CorrelationId, job.Selection.StableProductKey, job.Selection.Provider,
        job.Selection.PackageId, job.Selection.InstalledVersion, job.Selection.CandidateVersion, utcNow(), status, exitCode,
        SanitizedDiagnosticBundle.Sanitize(diagnostic));

    private static UpdateHistoryStatus Map(UpdateProviderAttemptStatus status) => status switch
    {
        UpdateProviderAttemptStatus.Cancelled => UpdateHistoryStatus.Cancelled,
        UpdateProviderAttemptStatus.SourceFailed => UpdateHistoryStatus.SourceFailed,
        UpdateProviderAttemptStatus.DownloadFailed => UpdateHistoryStatus.DownloadFailed,
        UpdateProviderAttemptStatus.InstallerFailed => UpdateHistoryStatus.InstallerFailed,
        UpdateProviderAttemptStatus.ElevationDenied => UpdateHistoryStatus.ElevationDenied,
        UpdateProviderAttemptStatus.TimedOut => UpdateHistoryStatus.TimedOut,
        _ => UpdateHistoryStatus.ProductFailed
    };
}

/// <summary>Produces a bounded JSON diagnostic bundle suitable for copying, never an environment dump.</summary>
public static partial class SanitizedDiagnosticBundle
{
    private const int MaximumEntries = 256;
    private const int MaximumTextLength = 4096;

    public static string Create(IReadOnlyCollection<UpdateHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        object payload = new
        {
            schema = "pc-steward-update-diagnostics-v1",
            entries = entries.OrderByDescending(entry => entry.OccurredAt).Take(MaximumEntries).Select(entry => new
            {
                entry.JobId,
                entry.CorrelationId,
                entry.StableProductKey,
                provider = entry.Provider.ToString(),
                entry.PackageId,
                entry.InstalledVersion,
                entry.TargetVersion,
                entry.OccurredAt,
                status = entry.Status.ToString(),
                entry.ExitCode,
                diagnostic = Sanitize(entry.Diagnostic)
            })
        };
        return JsonSerializer.Serialize(payload);
    }

    public static string Sanitize(string? value)
    {
        string safe = value ?? string.Empty;
        safe = SensitiveValue().Replace(safe, "$1=[redacted]");
        safe = WindowsPath().Replace(safe, "[path]");
        safe = new string(safe.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        safe = Whitespace().Replace(safe, " ").Trim();
        return safe[..Math.Min(safe.Length, MaximumTextLength)];
    }

    [GeneratedRegex(@"(?i)\b(password|token|secret|licen[cs]e[-_ ]?key)\s*[:=]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValue();

    [GeneratedRegex(@"(?i)(?:[A-Z]:\\|\\\\)[^\s\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
