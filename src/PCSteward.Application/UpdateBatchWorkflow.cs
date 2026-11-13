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
    VerificationRequired,

    /// <summary>
    /// Task 51 (FR-UPD-09): the provider's typed result (for WinGet, a documented restart exit code; never output text) says the update
    /// finished and Windows must restart to complete it. Post-update inventory still runs; the item is recorded as Restart required,
    /// never Success, whatever inventory finds.
    /// </summary>
    RestartReported
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

    /// <summary>
    /// Stage C4 (Task 54 R1): runs <paramref name="job"/> with the per-user reviewed operation frozen from it (null for machine scope,
    /// which has no reviewed mode yet). The production executor refuses a user-scope job without an admitted operation bound to it.
    /// </summary>
    Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
        UpdateItemProgress progress, CancellationToken cancellationToken) => ExecuteAsync(job, currentEvidence, progress, cancellationToken);
}

/// <summary>
/// Protocol-2 fact extension rev 3 §4.3 (L5): <see cref="Detail"/> is appended for the reviewed Chocolatey verifier. A detail
/// other than <see cref="PostUpdateVerificationDetail.None"/> means the client's own files or registrations changed or could
/// not be checked, so nothing was run to verify; the reviewed workflow records it as Unverified whatever the other fields say.
/// <see cref="Note"/> (ADR-031 design §3a) is a disclosure on a verified result only; it never makes a result verified.
/// </summary>
public sealed record PostUpdateInventoryResult(bool ExpectedVersionDetected, bool RestartPending,
    PostUpdateVerificationDetail Detail = PostUpdateVerificationDetail.None, PostUpdateVerificationNote Note = PostUpdateVerificationNote.None)
{
    /// <summary>
    /// D7 design §3 item 4: the names of the whole-PC uninstall entries that changed since the baseline the check was given (empty when
    /// none did), or null when no comparison was made (no baseline, or the check couldn't read a live observation).
    /// </summary>
    public IReadOnlyList<string>? WholePcChanges { get; init; }
}

/// <summary>
/// Verification detail; anything but <see cref="None"/> is Unverified. ADR-031 (Chocolatey client self-update):
/// <see cref="ClientLeftoverNotListed"/> — a leftover whose bytes match no reviewed release (design §3a, named, never deleted);
/// <see cref="ClientLeftoverNotSource"/> — the leftover matches a reviewed release other than the one the update started from
/// (named, never deleted); <see cref="ClientReleaseIncomplete"/> — releasing the helper's file holds after the update started
/// failed part-way (design §2 R3); <see cref="ClientSetupOverrideDuringRun"/>, <see cref="ClientRootWritableDuringRun"/> and
/// <see cref="ClientWatchFailedDuringRun"/> — the runtime watchdog terminated the update while it ran (design §2.1 R5;
/// Chocolatey may be partly updated, never repaired automatically).
/// </summary>
public enum PostUpdateVerificationDetail
{
    None, ClientChanged, ClientUnverifiable, ClientLeftoverNotListed, ClientLeftoverNotSource, ClientReleaseIncomplete,
    ClientSetupOverrideDuringRun, ClientRootWritableDuringRun, ClientWatchFailedDuringRun,
}

/// <summary>
/// A disclosure carried with a <b>verified</b> result (ADR-031 design §3a). <see cref="ClientReviewedLeftover"/>: a Chocolatey
/// client self-update verified with the reviewed leftover of the source release present. Only the client self-update route
/// may carry a note; the reviewed workflow treats any note elsewhere, or an undefined one, as Unverified.
/// </summary>
public enum PostUpdateVerificationNote { None, ClientReviewedLeftover }

/// <summary>
/// FR-UPD-08: re-inventory after the provider finished. Inventory proves the target version; it never infers a restart. Machine-wide
/// restart indicators are not bound to one job, so the batch takes restart evidence only from the provider's typed result
/// (<see cref="UpdateProviderAttemptStatus.RestartReported"/>, Task 51). <see cref="PostUpdateInventoryResult.RestartPending"/> stays for
/// verifiers that own reliable restart evidence of their own.
/// </summary>
public interface IPostUpdateInventory
{
    Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);

    /// <summary>
    /// As <see cref="ReInventoryAsync(ImmutableUpdateJob, CancellationToken)"/>, with the uninstall entry the review read from the
    /// target manifest (design note §3.4). Verifiers that don't use it check as before.
    /// </summary>
    Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, ExpectedUninstallEntry? expected, CancellationToken cancellationToken) =>
        ReInventoryAsync(job, cancellationToken);

    /// <summary>
    /// D7 design §3 item 4: as <see cref="ReInventoryAsync(ImmutableUpdateJob, ExpectedUninstallEntry?, CancellationToken)"/>, also
    /// comparing the whole-PC uninstall entries with <paramref name="wholePcBaseline"/> taken just before launch. Verifiers that can't
    /// compare leave <see cref="PostUpdateInventoryResult.WholePcChanges"/> null, which the coordinator never records as successful.
    /// </summary>
    Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, ExpectedUninstallEntry? expected, WholePcRegistrationSnapshot? wholePcBaseline,
        CancellationToken cancellationToken) =>
        ReInventoryAsync(job, expected, cancellationToken);
}

/// <summary>
/// One approved item. <see cref="AcknowledgedRunningProcesses"/> (Task 52), when given, binds the running-app acknowledgement to the
/// processes the owner was warned about: a later detection naming any other process is a new warning that needs a new acknowledgement.
/// </summary>
public sealed record UpdateBatchItem(
    ImmutableUpdateJob Job,
    UpdateJobExecutionEvidence CurrentEvidence,
    bool RunningApplicationWarningAcknowledged = false,
    UpdateProgressMailbox? Progress = null,
    IReadOnlyCollection<string>? AcknowledgedRunningProcesses = null,
    ReviewedUpdateOperation? Reviewed = null,
    ExpectedUninstallEntry? ExpectedEntry = null,
    WholePcRegistrationSnapshot? WholePcBaseline = null)
{
    /// <summary>True when <paramref name="running"/> is covered by this item's acknowledgement.</summary>
    public bool Acknowledges(RunningApplicationDetection running) =>
        RunningApplicationAcknowledgement.Covers(RunningApplicationWarningAcknowledged, AcknowledgedRunningProcesses, running);
}

/// <summary>
/// Task 52 (FR-UPD-05/06, ADR-025 late warning): an acknowledgement covers a running-app detection only when it was given and every
/// process now detected was among those the owner was warned about. Without a recorded process list only the acknowledgement itself is
/// read (the earlier single-item contract).
/// </summary>
public static class RunningApplicationAcknowledgement
{
    public static bool Covers(bool acknowledged, IReadOnlyCollection<string>? acknowledgedProcesses, RunningApplicationDetection running)
    {
        ArgumentNullException.ThrowIfNull(running);
        if (!running.AppearsToBeRunning) return true;
        if (!acknowledged) return false;
        if (acknowledgedProcesses is null) return true;
        HashSet<string> warned = new(acknowledgedProcesses.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.OrdinalIgnoreCase);
        return warned.Count != 0 && running.ProcessNames.All(name => warned.Contains(name));
    }
}
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
    /// <summary>The fixed diagnostic prefix of an item recorded as Restart required from a typed provider restart result (Task 51).</summary>
    public const string RestartReportedDiagnostic = "provider-reported-restart-required";

    /// <summary>
    /// D7 design §3 item 4: the diagnostic prefix of a per-user update after which whole-PC uninstall entries had changed (recorded as
    /// <see cref="UpdateHistoryStatus.VerificationFailed"/>, never Success), followed by whether the target version was found and the
    /// changed entries' names.
    /// </summary>
    public const string UnexpectedWholePcChangesDiagnostic = "unexpected-whole-pc-changes";

    /// <summary>D7 design §3 item 4: the diagnostic of a per-user update whose whole-PC comparison couldn't be made.</summary>
    public const string WholePcCheckUnavailableDiagnostic = "whole-pc-change-check-unavailable";

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
        if (items.Any(item => item.Reviewed is not null && !item.Reviewed.BindsJob(item.Job)))
            throw new ArgumentException("A reviewed operation must be frozen from its own approved job.", nameof(items));
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
                            if (!item.Acknowledges(running))
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
                        attempt = await executor.ExecuteAsync(item.Job, item.Reviewed, item.CurrentEvidence, progress, cancellation.Token).ConfigureAwait(false);
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
                    bool restartReported = attempt.Status == UpdateProviderAttemptStatus.RestartReported;
                    if (attempt.Status is not (UpdateProviderAttemptStatus.AwaitingVerification or UpdateProviderAttemptStatus.RestartReported))
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
                            PostUpdateInventoryResult verification = item.WholePcBaseline is null
                                ? await inventory.ReInventoryAsync(item.Job, item.ExpectedEntry, cancellation.Token).ConfigureAwait(false)
                                : await inventory.ReInventoryAsync(item.Job, item.ExpectedEntry, item.WholePcBaseline, cancellation.Token).ConfigureAwait(false);
                            if (restartReported)
                            {
                                // Task 51, AC-UPD-07: a typed provider restart result is never Success, even with the version detected.
                                status = UpdateHistoryStatus.RestartRequired;
                                diagnostic = verification.ExpectedVersionDetected
                                    ? $"{RestartReportedDiagnostic}; target-version-detected-by-post-update-inventory"
                                    : $"{RestartReportedDiagnostic}; target-version-not-yet-detected";
                            }
                            else
                            {
                                status = verification.ExpectedVersionDetected ? UpdateHistoryStatus.Success
                                    : verification.RestartPending ? UpdateHistoryStatus.RestartRequired : UpdateHistoryStatus.Inconclusive;
                                diagnostic = status switch
                                {
                                    UpdateHistoryStatus.Success => "target-version-verified-by-post-update-inventory",
                                    UpdateHistoryStatus.RestartRequired => "provider-reported-reliable-restart-pending-state",
                                    _ => "provider-succeeded-but-target-version-was-not-detected"
                                };
                            }
                            // D7 design §3 item 4: a per-user update that changed whole-PC entries, or whose comparison couldn't be made, is
                            // never recorded as successful. A restart result stays Restart required and names the changes.
                            if (item.WholePcBaseline is not null && verification.WholePcChanges is not { Count: 0 })
                            {
                                string found = verification.ExpectedVersionDetected ? "target-version-detected" : "target-version-not-detected";
                                string wholePc = verification.WholePcChanges is { } changes
                                    ? $"{UnexpectedWholePcChangesDiagnostic}; {found}; changed: {NameChanges(changes)}"
                                    : $"{WholePcCheckUnavailableDiagnostic}; {found}";
                                if (restartReported) diagnostic = $"{diagnostic}; {wholePc}";
                                else
                                {
                                    status = UpdateHistoryStatus.VerificationFailed;
                                    diagnostic = wholePc;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (cancellation.Token.IsCancellationRequested)
                        {
                            status = restartReported ? UpdateHistoryStatus.RestartRequired : UpdateHistoryStatus.VerificationRequired;
                            diagnostic = restartReported
                                ? $"{RestartReportedDiagnostic}; verification-interrupted-after-provider-completed"
                                : "verification-interrupted-after-provider-completed";
                        }
                        catch (Exception)
                        {
                            status = restartReported ? UpdateHistoryStatus.RestartRequired : UpdateHistoryStatus.VerificationFailed;
                            diagnostic = restartReported
                                ? $"{RestartReportedDiagnostic}; post-update-inventory-did-not-finish"
                                : "post-update-inventory-verification-failed";
                        }
                        if (restartReported && !string.IsNullOrWhiteSpace(attempt.SanitizedDiagnostic))
                            diagnostic = $"{diagnostic}; {attempt.SanitizedDiagnostic}";
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

    /// <summary>The changed entries' names for the history line: bounded, single-line, without separators.</summary>
    private static string NameChanges(IReadOnlyList<string> changes)
    {
        static string Clean(string name)
        {
            string text = new([.. name.Select(c => char.IsControl(c) || c == ';' ? ' ' : c)]);
            text = text.Trim();
            return text.Length <= 64 ? text : text[..64];
        }
        IEnumerable<string> named = changes.Take(WholePcRegistrationSnapshot.MaximumNamedChanges).Select(Clean);
        string more = changes.Count > WholePcRegistrationSnapshot.MaximumNamedChanges ? $" and {changes.Count - WholePcRegistrationSnapshot.MaximumNamedChanges} more" : string.Empty;
        return $"{string.Join(", ", named)}{more}";
    }

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
                // Codex security scan finding 6: history rows are owner-writable local state, so every copied string goes
                // through the same redactor and a field bound, not only the diagnostic text.
                StableProductKey = SanitizeField(entry.StableProductKey),
                provider = entry.Provider.ToString(),
                PackageId = SanitizeField(entry.PackageId),
                InstalledVersion = SanitizeField(entry.InstalledVersion),
                TargetVersion = SanitizeField(entry.TargetVersion),
                entry.OccurredAt,
                status = entry.Status.ToString(),
                entry.ExitCode,
                diagnostic = Sanitize(entry.Diagnostic)
            })
        };
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>The most UTF-16 units an identity or version field keeps in the diagnostic bundle.</summary>
    public const int MaximumFieldLength = 256;

    /// <summary>An identity or version field: the same redaction as <see cref="Sanitize"/>, bounded to <see cref="MaximumFieldLength"/>; null stays null.</summary>
    public static string? SanitizeField(string? value) => value is null ? null : Truncate(Sanitize(value), MaximumFieldLength);

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
