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
    ProductFailed,
    VerificationRequired
}

/// <summary>Structured provider evidence; no process authority or command data crosses this boundary.</summary>
public sealed record UpdateProviderAttempt(UpdateProviderAttemptStatus Status, int? ExitCode, string SanitizedDiagnostic,
    Task? OwnedCompletion = null);

public interface IApprovedUpdateExecutor
{
    UpdateExecutionLifetime? ExecutionLifetime => null;
    Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken);
    Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence,
        UpdateItemProgress progress, CancellationToken cancellationToken) => ExecuteAsync(job, currentEvidence, cancellationToken);
}

/// <summary>
/// Protocol-2 fact extension rev 3 §4.3 (L5): <see cref="Detail"/> is appended for the reviewed Chocolatey verifier. A detail
/// other than <see cref="PostUpdateVerificationDetail.None"/> means the client's own files or registrations changed or could
/// not be checked, so nothing was run to verify; the reviewed workflow records it as Unverified whatever the other fields say.
/// <see cref="Note"/> (ADR-031 design §3a) is a disclosure on a verified result only; it never makes a result verified.
/// </summary>
public sealed record PostUpdateInventoryResult(bool ExpectedVersionDetected, bool RestartPending,
    PostUpdateVerificationDetail Detail = PostUpdateVerificationDetail.None, PostUpdateVerificationNote Note = PostUpdateVerificationNote.None);

/// <summary>
/// Verification detail; anything but <see cref="None"/> is Unverified. ADR-031 (Chocolatey client self-update):
/// <see cref="ClientLeftoverNotListed"/> — a leftover whose bytes match no reviewed release (design §3a, named, never deleted);
/// <see cref="ClientLeftoverNotSource"/> — the leftover matches a reviewed release other than the one the update started from
/// (named, never deleted); <see cref="ClientReleaseIncomplete"/> — releasing the helper's file holds after the update started
/// failed part-way (design §2 R3).
/// </summary>
public enum PostUpdateVerificationDetail { None, ClientChanged, ClientUnverifiable, ClientLeftoverNotListed, ClientLeftoverNotSource, ClientReleaseIncomplete }

/// <summary>
/// A disclosure carried with a <b>verified</b> result (ADR-031 design §3a). <see cref="ClientReviewedLeftover"/>: a Chocolatey
/// client self-update verified with the reviewed leftover of the source release present. Only the client self-update route
/// may carry a note; the reviewed workflow treats any note elsewhere, or an undefined one, as Unverified.
/// </summary>
public enum PostUpdateVerificationNote { None, ClientReviewedLeftover }

public interface IPostUpdateInventory
{
    Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
}

public sealed record UpdateBatchItem(
    ImmutableUpdateJob Job,
    UpdateJobExecutionEvidence CurrentEvidence,
    bool RunningApplicationWarningAcknowledged = false,
    UpdateProgressMailbox? Progress = null);
public sealed record UpdateBatchResult(
    IReadOnlyCollection<UpdateHistoryEntry> Entries,
    RunningApplicationDetection? AcknowledgementRequired = null,
    bool AdmissionRefused = false)
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
    Func<DateTimeOffset>? utcNow = null,
    IUpdateExecutionEventSink? events = null)
{
    private readonly IApprovedUpdateExecutor executor = executor ?? throw new ArgumentNullException(nameof(executor));
    private readonly IPostUpdateInventory inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    private readonly IUpdateHistoryStore history = history ?? throw new ArgumentNullException(nameof(history));
    private readonly IRunningApplicationDetector runningApplications = runningApplications ?? throw new ArgumentNullException(nameof(runningApplications));
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly IUpdateExecutionEventSink events = events ?? NullUpdateExecutionEventSink.Instance;

    /// <summary>
    /// Task 73 Phase 6: the owned work (for example a helper process still exiting) that the last admitted item left behind, or a
    /// completed task. A batch waits for it before the next item so that item isn't refused admission; it grants nothing.
    /// </summary>
    internal Task OwnedWorkCompletion => executor.ExecutionLifetime?.OwnedCompletion ?? Task.CompletedTask;

    public async Task<UpdateBatchResult> RunAsync(IReadOnlyCollection<UpdateBatchItem> items, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Any(item => item.Job is null || item.CurrentEvidence is null)) throw new ArgumentException("Each batch item needs immutable job and current evidence.", nameof(items));
        if (items.Select(item => item.Job.Id).Distinct().Count() != items.Count) throw new ArgumentException("A batch cannot contain the same job twice.", nameof(items));
        if (items.Any(item => item.Progress is not null && !item.Progress.Matches(item.Job)))
            throw new ArgumentException("Progress must match the immutable job and correlation.", nameof(items));
        List<UpdateHistoryEntry> recorded = [];
        bool batchFaulted = false;
        bool recovered = false;
        try
        {
            foreach (UpdateBatchItem item in items)
            {
                if (batchFaulted || cancellationToken.IsCancellationRequested)
                {
                    await RecordFinalAsync(item, UpdateHistoryStatus.Cancelled, null,
                        batchFaulted ? "not-started-after-progress-capacity-fault" : "user-cancelled-before-start", recorded).ConfigureAwait(false);
                    continue;
                }
                UpdateExecutionLifetime? lifetime = executor.ExecutionLifetime;
                using UpdateExecutionLease? admission = lifetime?.TryAcquire();
                if (lifetime is not null && admission is null)
                {
                    foreach (UpdateBatchItem blocked in items.SkipWhile(candidate => !ReferenceEquals(candidate, item)))
                        await RecordFinalAsync(blocked, UpdateHistoryStatus.ProductFailed, null,
                            "not-started-update-execution-busy-or-owned-work-unresolved", recorded).ConfigureAwait(false);
                    return new(recorded, AdmissionRefused: true);
                }
                if (!recovered)
                {
                    await history.RecoverInterruptedAsync(CancellationToken.None).ConfigureAwait(false);
                    recovered = true;
                }
                UpdateProgressCancellation cancellation = new(item.Progress, cancellationToken);
                Task ownedCompletion = Task.CompletedTask;
                UpdateItemProgress progress = new(item.Job, item.Progress);
                try
                {
                    item.Progress?.Offer(item.Job.Id, item.Job.CorrelationId, UpdateProgressPhase.Queued);
                    item.Progress?.Offer(item.Job.Id, item.Job.CorrelationId, UpdateProgressPhase.Preparing);
                    try
                    {
                        RunningApplicationDetection running = await runningApplications.DetectAsync(item.Job.Selection, cancellation.Token).ConfigureAwait(false);
                        if (running.AppearsToBeRunning)
                        {
                            await RecordAsync(Entry(item.Job, UpdateHistoryStatus.RunningApplicationWarning, null,
                                "target-application-appears-to-be-running; no-close-action-requested"), recorded, cancellation.Token).ConfigureAwait(false);
                            if (!item.RunningApplicationWarningAcknowledged)
                            {
                                item.Progress?.Complete(null, UpdateProgressCompletion.AcknowledgementRequired);
                                return new(recorded, running);
                            }
                        }
                        await RecordAsync(Entry(item.Job, UpdateHistoryStatus.Queued, null, "reviewed-job-queued"), recorded, cancellation.Token).ConfigureAwait(false);
                        await RecordAsync(Entry(item.Job, UpdateHistoryStatus.Running, null, "approved-execution-requested"), recorded, cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
                    {
                        item.Progress?.StopOffers();
                        batchFaulted = cancellation.CapacityFaulted;
                        await RecordFinalAsync(item, UpdateHistoryStatus.Cancelled, null,
                            batchFaulted ? "progress-capacity-fault; not-started" : "user-cancelled-before-start", recorded).ConfigureAwait(false);
                        continue;
                    }
                    if (cancellation.CapacityFaulted)
                    {
                        batchFaulted = true;
                        await RecordFinalAsync(item, UpdateHistoryStatus.Cancelled, null, "progress-capacity-fault; not-started", recorded).ConfigureAwait(false);
                        continue;
                    }
                    UpdateProviderAttempt attempt;
                    try
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        attempt = await executor.ExecuteAsync(item.Job, item.CurrentEvidence, progress, cancellation.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
                    {
                        attempt = new(UpdateProviderAttemptStatus.Cancelled, null, "operation-cancelled");
                    }
                    catch (Exception)
                    {
                        attempt = progress.InvocationPossible
                            ? new(UpdateProviderAttemptStatus.VerificationRequired, null, "execution-interrupted-after-possible-start; verification-required")
                            : new(UpdateProviderAttemptStatus.ProductFailed, null, "product-could-not-start-approved-update");
                    }
                    ownedCompletion = attempt.OwnedCompletion ?? Task.CompletedTask;
                    UpdateHistoryStatus status;
                    string diagnostic;
                    if (attempt.Status != UpdateProviderAttemptStatus.AwaitingVerification)
                    {
                        status = attempt.Status == UpdateProviderAttemptStatus.Cancelled ? UpdateHistoryStatus.VerificationRequired : Map(attempt.Status);
                        diagnostic = attempt.Status == UpdateProviderAttemptStatus.Cancelled
                            ? "provider-start-state-interrupted; verification-required" : attempt.SanitizedDiagnostic;
                        if (attempt.Status == UpdateProviderAttemptStatus.ProductFailed && progress.InvocationPossible)
                        {
                            status = UpdateHistoryStatus.VerificationRequired;
                            diagnostic = $"execution-interrupted-after-possible-start; verification-required; {diagnostic}";
                        }
                    }
                    else
                    {
                        // Inventory owns durable work too: await it; never abandon it behind a timeout wrapper.
                        if (progress.InvocationPossible)
                            item.Progress?.Offer(item.Job.Id, item.Job.CorrelationId, UpdateProgressPhase.Verifying);
                        try
                        {
                            PostUpdateInventoryResult verification = await inventory.ReInventoryAsync(item.Job, cancellation.Token).ConfigureAwait(false);
                            status = verification.ExpectedVersionDetected ? UpdateHistoryStatus.Success
                                : verification.RestartPending ? UpdateHistoryStatus.RestartRequired : UpdateHistoryStatus.Inconclusive;
                            diagnostic = status switch
                            {
                                UpdateHistoryStatus.Success => "target-version-verified-by-post-update-inventory",
                                UpdateHistoryStatus.RestartRequired => "provider-reported-reliable-restart-pending-state",
                                _ => "provider-succeeded-but-target-version-was-not-detected"
                            };
                        }
                        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
                        {
                            status = UpdateHistoryStatus.VerificationRequired;
                            diagnostic = "verification-interrupted-after-provider-completed";
                        }
                        catch (Exception)
                        {
                            status = UpdateHistoryStatus.VerificationFailed;
                            diagnostic = "post-update-inventory-verification-failed";
                        }
                    }
                    // Close admissions atomically before choosing the recorded outcome; late producers cannot cancel a committed result.
                    item.Progress?.StopOffers();
                    batchFaulted = cancellation.CapacityFaulted;
                    if (batchFaulted)
                    {
                        status = progress.InvocationPossible ? UpdateHistoryStatus.VerificationRequired : UpdateHistoryStatus.Cancelled;
                        diagnostic = progress.InvocationPossible
                            ? "progress-capacity-fault; verification-required" : "progress-capacity-fault; not-started";
                    }
                    await RecordFinalAsync(item, status, attempt.ExitCode, diagnostic, recorded).ConfigureAwait(false);
                }
                finally
                {
                    item.Progress?.StopOffers();
                    Task cancellationCompletion = cancellation.FinishAsync();
                    if (!cancellation.CapacityFaulted) await cancellationCompletion.ConfigureAwait(false);
                    Task combined = cancellationCompletion.IsCompletedSuccessfully ? ownedCompletion : Task.WhenAll(ownedCompletion, cancellationCompletion);
                    if (admission is not null) admission.TrackCompletion(combined);
                    else await combined.ConfigureAwait(false);
                }
            }
            return new(recorded);
        }
        finally
        {
            foreach (UpdateBatchItem item in items)
            {
                item.Progress?.Complete(null, UpdateProgressCompletion.RecordingFailed);
                item.Progress?.SealProducer();
                if (item.Progress is { } mailbox)
                {
                    UpdateProgressCounters counters = mailbox.Counters;
                    if (mailbox.HasDegraded || counters.ForeignRejected > 0)
                    {
                        WriteEvent(new(item.Job.Id, item.Job.CorrelationId, utcNow(), UpdateExecutionEventSeverity.Warning,
                            mailbox.CapacityFault.IsCompleted ? "update.progress-capacity-fault" : "update.progress-rejected",
                            item.Job.Selection.Provider, item.Job.Selection.PackageId, item.Job.Selection.CandidateVersion,
                            $"Local progress attempts={counters.Attempts}; invalid={counters.InvalidRejected}; foreign={counters.ForeignRejected}; coalesced={counters.Coalesced}."));
                    }
                }
            }
        }
    }

    private async Task RecordFinalAsync(UpdateBatchItem item, UpdateHistoryStatus status, int? exitCode, string diagnostic,
        List<UpdateHistoryEntry> recorded)
    {
        item.Progress?.StopOffers();
        await RecordAsync(Entry(item.Job, status, exitCode, diagnostic), recorded, CancellationToken.None).ConfigureAwait(false);
        item.Progress?.Complete(status, UpdateProgressCompletion.Recorded);
    }
    private async Task RecordAsync(UpdateHistoryEntry entry, List<UpdateHistoryEntry> entries, CancellationToken cancellationToken)
    {
        await history.RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        entries.Add(entry);
        UpdateExecutionEventSeverity severity = entry.Status switch
        {
            UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.Success => UpdateExecutionEventSeverity.Information,
            UpdateHistoryStatus.RunningApplicationWarning or UpdateHistoryStatus.VerificationRequired or UpdateHistoryStatus.RestartRequired or
                UpdateHistoryStatus.Inconclusive or UpdateHistoryStatus.Cancelled or UpdateHistoryStatus.ElevationDenied => UpdateExecutionEventSeverity.Warning,
            _ => UpdateExecutionEventSeverity.Error
        };
        WriteEvent(new(entry.JobId, entry.CorrelationId, entry.OccurredAt, severity, $"update.{entry.Status.ToString().ToLowerInvariant()}",
            entry.Provider, entry.PackageId, entry.TargetVersion, entry.Diagnostic));
    }

    private void WriteEvent(UpdateExecutionEvent updateEvent)
    {
        try { events.Write(updateEvent); }
        catch (Exception) { /* Diagnostic sink failure cannot replace a recorded outcome. */ }
    }

    private UpdateHistoryEntry Entry(ImmutableUpdateJob job, UpdateHistoryStatus status, int? exitCode, string diagnostic) => new(
        Guid.NewGuid(), job.Id, job.CorrelationId, job.Selection.StableProductKey, job.Selection.Provider,
        job.Selection.PackageId, job.Selection.InstalledVersion, job.Selection.CandidateVersion, utcNow(), status, exitCode,
        SanitizedDiagnosticBundle.Sanitize(diagnostic));

    private static UpdateHistoryStatus Map(UpdateProviderAttemptStatus status) => status switch
    {
        UpdateProviderAttemptStatus.Cancelled => UpdateHistoryStatus.Cancelled,
        UpdateProviderAttemptStatus.VerificationRequired => UpdateHistoryStatus.VerificationRequired,
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
        return Truncate(ReplaceLoneSurrogates(safe), MaximumTextLength);
    }

    /// <summary>Truncates to at most <paramref name="maximumLength"/> UTF-16 units without splitting a surrogate pair.</summary>
    public static string Truncate(string value, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumLength);
        if (value.Length <= maximumLength) return value;
        int length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length];
    }

    /// <summary>Replaces unpaired UTF-16 surrogates with U+FFFD so text always encodes as strict UTF-8.</summary>
    private static string ReplaceLoneSurrogates(string value)
    {
        char[]? buffer = null;
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (char.IsHighSurrogate(current) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])) { index++; continue; }
            if (!char.IsSurrogate(current)) continue;
            buffer ??= value.ToCharArray();
            buffer[index] = '\uFFFD';
        }
        return buffer is null ? value : new string(buffer);
    }

    [GeneratedRegex(@"(?i)\b(password|token|secret|licen[cs]e[-_ ]?key)\s*[:=]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveValue();

    [GeneratedRegex(@"(?i)(?:[A-Z]:\\|\\\\)[^\s\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
