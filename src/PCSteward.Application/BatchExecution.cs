using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Fresh evidence for one approved operation immediately before it starts (ADR-017 v0.3).</summary>
public interface IOperationRevalidator
{
    Task<UpdateJobExecutionEvidence?> RevalidateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
}

/// <summary>Runs one approved operation through provider-owned construction. Exit evidence is never Success.</summary>
public interface IOperationExecutor
{
    Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken);
}

/// <summary>Reasons the desktop may withhold an elevated item. They can only stop an item, never add one.</summary>
public static class BatchSkipReasons
{
    public const string RunningApplication = "running-application";
    public const string DesktopRevalidationFailed = "desktop-revalidation-failed";
}

public enum ElevatedItemDisposition
{
    /// <summary>The helper reported a result for the item.</summary>
    Reported,
    /// <summary>The session ended while the item may have been running: verification is required.</summary>
    Interrupted,
    /// <summary>The item certainly did not start (UAC declined, launch failure, or the session ended first).</summary>
    NotStarted
}

public sealed record ElevatedItemOutcome(Guid JobId, ElevatedItemDisposition Disposition, UpdateProviderAttemptStatus Status, bool RestartPending, int? ExitCode, string Diagnostic);

public sealed record ElevatedBatchOutcome(IReadOnlyList<ElevatedItemOutcome> Items);

/// <summary>FR-BAT-05: one elevated helper session (one UAC prompt) for every elevated item of a batch.</summary>
public interface IElevatedBatchExecutor
{
    Task<ElevatedBatchOutcome> ExecuteAsync(
        ApprovedOperationBatch batch,
        IReadOnlyDictionary<Guid, string> skipReasons,
        Action<Guid>? itemStarted,
        CancellationToken cancelRemaining,
        CancellationToken abort);
}

/// <summary>Per-run consent from the review sheet (FR-BAT-08). Automation never carries consent.</summary>
public sealed record RunningApplicationConsent(bool CloseForThisRun = false, bool ForceCloseForThisRun = false);

public enum RunningApplicationAction { Proceed, Skip, Close }

/// <summary>
/// FR-BAT-08 / AC-V3-07: a running app is skipped by default. It is closed only with per-app consent (a
/// stored CloseAppConsent policy) or per-run consent, gracefully and with a bounded wait. Forced termination
/// needs a second per-run consent. Automation never closes or force-terminates an app.
/// </summary>
public static class RunningApplicationPolicy
{
    public static readonly TimeSpan GracefulWait = TimeSpan.FromSeconds(30);

    public static RunningApplicationAction Decide(RunningApplicationDetection detection, bool perAppCloseConsent, RunningApplicationConsent runConsent, bool automation)
    {
        ArgumentNullException.ThrowIfNull(detection);
        ArgumentNullException.ThrowIfNull(runConsent);
        if (!detection.AppearsToBeRunning) return RunningApplicationAction.Proceed;
        if (automation) return RunningApplicationAction.Skip;
        return perAppCloseConsent || runConsent.CloseForThisRun ? RunningApplicationAction.Close : RunningApplicationAction.Skip;
    }

    public static bool AllowsForce(RunningApplicationConsent runConsent, bool automation) =>
        !automation && runConsent is { ForceCloseForThisRun: true };
}

/// <summary>Closes the named processes of the current user's session. Returns true only when none remain.</summary>
public interface IRunningApplicationCloser
{
    Task<bool> CloseAsync(IReadOnlyCollection<string> processNames, bool allowForce, TimeSpan gracefulWait, CancellationToken cancellationToken);
}

/// <summary>Never closes anything: the safe default where no closer is composed.</summary>
public sealed class NoRunningApplicationCloser : IRunningApplicationCloser
{
    public static NoRunningApplicationCloser Instance { get; } = new();
    public Task<bool> CloseAsync(IReadOnlyCollection<string> processNames, bool allowForce, TimeSpan gracefulWait, CancellationToken cancellationToken) => Task.FromResult(false);
}

public sealed record BatchRunOptions(RunningApplicationConsent RunConsent, IReadOnlyDictionary<string, ProductUpdatePolicy> Policies, bool Automation = false)
{
    public static BatchRunOptions Default { get; } = new(new(), new Dictionary<string, ProductUpdatePolicy>());
}

public sealed record BatchItemProgress(Guid JobId, string StableProductKey, UpdateHistoryStatus Status);

/// <summary>FR-BAT-07: final outcomes grouped for the completion summary.</summary>
public sealed record BatchCompletionSummary(
    IReadOnlyList<UpdateHistoryEntry> Succeeded,
    IReadOnlyList<UpdateHistoryEntry> RestartRequired,
    IReadOnlyList<UpdateHistoryEntry> Failed,
    IReadOnlyList<UpdateHistoryEntry> NotVerified,
    IReadOnlyList<UpdateHistoryEntry> Skipped)
{
    public static BatchCompletionSummary From(IEnumerable<UpdateHistoryEntry> finalEntries)
    {
        UpdateHistoryEntry[] entries = [.. finalEntries];
        return new(
            [.. entries.Where(entry => entry.Status == UpdateHistoryStatus.Success)],
            [.. entries.Where(entry => entry.Status == UpdateHistoryStatus.RestartRequired)],
            [.. entries.Where(entry => entry.Status is UpdateHistoryStatus.SourceFailed or UpdateHistoryStatus.DownloadFailed or UpdateHistoryStatus.InstallerFailed or UpdateHistoryStatus.ElevationDenied or UpdateHistoryStatus.TimedOut or UpdateHistoryStatus.VerificationFailed or UpdateHistoryStatus.ProductFailed)],
            [.. entries.Where(entry => entry.Status is UpdateHistoryStatus.Inconclusive or UpdateHistoryStatus.VerificationRequired)],
            [.. entries.Where(entry => entry.Status is UpdateHistoryStatus.Skipped or UpdateHistoryStatus.Cancelled)]);
    }

    /// <summary>Failed and not-verified items, for a Retry failed that starts a new reviewed batch.</summary>
    public IReadOnlyList<string> RetryCandidates => [.. Failed.Concat(NotVerified).Select(entry => entry.StableProductKey).Distinct(StringComparer.Ordinal)];
}

public sealed record BatchRunResult(bool Started, string Message, IReadOnlyList<UpdateHistoryEntry> Entries, BatchCompletionSummary Summary);

/// <summary>
/// FR-BAT-04–08, FR-INS-05/07, AC-V3-04/06/07: runs one approved batch. It checks integrity and expiry, allows
/// one active run, and runs unelevated items in the desktop first, then every elevated item in one helper
/// session. Each item passes the running-app policy and revalidation immediately before it starts, and a
/// provider exit leads only to verification. Cancel remaining is observed only between items.
/// </summary>
public sealed class BatchExecutionCoordinator
{
    private readonly IOperationRevalidator revalidator;
    private readonly IOperationExecutor unelevatedExecutor;
    private readonly IElevatedBatchExecutor? elevatedExecutor;
    private readonly IPostUpdateInventory verifier;
    private readonly IUpdateHistoryStore history;
    private readonly IRunningApplicationDetector runningApplications;
    private readonly IRunningApplicationCloser closer;
    private readonly Func<DateTimeOffset> utcNow;
    private int activeRun;

    public BatchExecutionCoordinator(
        IOperationRevalidator revalidator,
        IOperationExecutor unelevatedExecutor,
        IElevatedBatchExecutor? elevatedExecutor,
        IPostUpdateInventory verifier,
        IUpdateHistoryStore history,
        IRunningApplicationDetector runningApplications,
        IRunningApplicationCloser? closer = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        this.revalidator = revalidator ?? throw new ArgumentNullException(nameof(revalidator));
        this.unelevatedExecutor = unelevatedExecutor ?? throw new ArgumentNullException(nameof(unelevatedExecutor));
        this.elevatedExecutor = elevatedExecutor;
        this.verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        this.history = history ?? throw new ArgumentNullException(nameof(history));
        this.runningApplications = runningApplications ?? throw new ArgumentNullException(nameof(runningApplications));
        this.closer = closer ?? NoRunningApplicationCloser.Instance;
        this.utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<BatchRunResult> RunAsync(ApprovedOperationBatch batch, BatchRunOptions options, IProgress<BatchItemProgress>? progress, CancellationToken cancelRemaining, CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(options);
        if (Interlocked.CompareExchange(ref activeRun, 1, 0) != 0)
        {
            return NotStarted("Another operation batch is running. Only one may run at a time.");
        }

        try
        {
            if (!batch.HasValidDigest) return NotStarted("The approved batch failed its integrity check. Nothing was started.");
            if (!batch.IsUsableAt(utcNow())) return NotStarted("The approved batch has expired. Review it again to continue.");
            await history.RecoverInterruptedAsync(CancellationToken.None).ConfigureAwait(false);
            Run run = new(this, options, progress);

            foreach (ImmutableUpdateJob job in batch.UnelevatedItems)
            {
                if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
                {
                    await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
                    continue;
                }

                await RunUnelevatedAsync(run, job, cancelRemaining, abort).ConfigureAwait(false);
            }

            await RunElevatedAsync(run, batch, cancelRemaining, abort).ConfigureAwait(false);
            UpdateHistoryEntry[] finals = [.. run.FinalEntries];
            return new(true, $"{finals.Length} item(s) finished.", [.. run.Entries], BatchCompletionSummary.From(finals));
        }
        finally
        {
            Volatile.Write(ref activeRun, 0);
        }
    }

    private async Task RunUnelevatedAsync(Run run, ImmutableUpdateJob job, CancellationToken cancelRemaining, CancellationToken abort)
    {
        string? blocked = await ApplyRunningApplicationPolicyAsync(run.Options, job, abort).ConfigureAwait(false);
        if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
        {
            await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
            return;
        }
        if (blocked is not null)
        {
            await run.RecordAsync(job, UpdateHistoryStatus.Skipped, null, blocked).ConfigureAwait(false);
            return;
        }

        UpdateJobExecutionEvidence? evidence = await TryRevalidateAsync(job, abort).ConfigureAwait(false);
        if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
        {
            await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
            return;
        }
        if (evidence is null || !job.Matches(evidence))
        {
            await run.RecordAsync(job, UpdateHistoryStatus.Skipped, null, "revalidation-failed; review-again").ConfigureAwait(false);
            return;
        }

        await run.RecordAsync(job, UpdateHistoryStatus.Running, null, "provider-execution-started").ConfigureAwait(false);
        if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
        {
            await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
            return;
        }
        ProviderExitAssessment outcome;
        try
        {
            outcome = await unelevatedExecutor.ExecuteAsync(job, evidence, abort).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            await run.RecordAsync(job, UpdateHistoryStatus.VerificationRequired, null, "provider-start-state-interrupted; verification-required").ConfigureAwait(false);
            return;
        }
        catch (Exception)
        {
            outcome = new(UpdateProviderAttemptStatus.ProductFailed, false, "product-could-not-start-approved-operation");
        }

        await FinishAsync(run, job, outcome, abort).ConfigureAwait(false);
    }

    private async Task RunElevatedAsync(Run run, ApprovedOperationBatch batch, CancellationToken cancelRemaining, CancellationToken abort)
    {
        IReadOnlyList<ImmutableUpdateJob> elevated = batch.ElevatedItems;
        if (elevated.Count == 0) return;
        if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
        {
            foreach (ImmutableUpdateJob job in elevated) await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
            return;
        }

        if (elevatedExecutor is null)
        {
            foreach (ImmutableUpdateJob job in elevated) await run.RecordAsync(job, UpdateHistoryStatus.ProductFailed, null, "standalone-package-machine-scope-disabled").ConfigureAwait(false);
            return;
        }

        // Desktop pre-pass: running apps are handled as the user, and items that already fail revalidation
        // are withheld so a batch with nothing left to run never raises a UAC prompt.
        Dictionary<Guid, string> skipReasons = [];
        foreach (ImmutableUpdateJob job in elevated)
        {
            if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
            {
                foreach (ImmutableUpdateJob pending in elevated) await run.CancelAsync(pending, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
                return;
            }

            if (await ApplyRunningApplicationPolicyAsync(run.Options, job, abort).ConfigureAwait(false) is not null)
            {
                skipReasons[job.Id] = BatchSkipReasons.RunningApplication;
                continue;
            }

            UpdateJobExecutionEvidence? evidence = await TryRevalidateAsync(job, abort).ConfigureAwait(false);
            if (evidence is null || !job.Matches(evidence)) skipReasons[job.Id] = BatchSkipReasons.DesktopRevalidationFailed;
        }

        // The last awaited precheck is still before the UAC boundary.
        if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
        {
            foreach (ImmutableUpdateJob job in elevated) await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
            return;
        }

        if (skipReasons.Count == elevated.Count)
        {
            foreach (ImmutableUpdateJob job in elevated) await run.RecordAsync(job, UpdateHistoryStatus.Skipped, null, SkipDiagnostic(skipReasons[job.Id])).ConfigureAwait(false);
            return;
        }

        // Recorded before UAC so that a desktop crash during the session recovers to Verification required.
        foreach (ImmutableUpdateJob job in elevated.Where(job => !skipReasons.ContainsKey(job.Id)))
        {
            await run.RecordAsync(job, UpdateHistoryStatus.Running, null, "elevated-session-requested").ConfigureAwait(false);
        }

        // History persistence can also yield before the executor is called.
        if (cancelRemaining.IsCancellationRequested || abort.IsCancellationRequested)
        {
            foreach (ImmutableUpdateJob job in elevated) await run.CancelAsync(job, abort.IsCancellationRequested ? "run-aborted-before-start" : "cancel-remaining-before-start").ConfigureAwait(false);
            return;
        }

        Dictionary<Guid, ImmutableUpdateJob> byId = elevated.ToDictionary(job => job.Id);
        ElevatedBatchOutcome session;
        try
        {
            session = await elevatedExecutor.ExecuteAsync(batch, skipReasons, jobId => { if (byId.TryGetValue(jobId, out ImmutableUpdateJob? started)) run.Report(started, UpdateHistoryStatus.Running); }, cancelRemaining, abort).ConfigureAwait(false);
        }
        catch (Exception)
        {
            session = new([.. elevated.Select(job => new ElevatedItemOutcome(job.Id, ElevatedItemDisposition.Interrupted, UpdateProviderAttemptStatus.ProductFailed, false, null, "elevated-session-failed"))]);
        }

        Dictionary<Guid, ElevatedItemOutcome> outcomes = [];
        foreach (ElevatedItemOutcome outcome in session.Items) outcomes.TryAdd(outcome.JobId, outcome);
        foreach (ImmutableUpdateJob job in elevated)
        {
            if (!outcomes.TryGetValue(job.Id, out ElevatedItemOutcome? outcome))
            {
                // Missing evidence is treated as a possible start, never as Success or a clean skip.
                outcome = new(job.Id, ElevatedItemDisposition.Interrupted, UpdateProviderAttemptStatus.ProductFailed, false, null, "elevated-result-missing");
            }

            switch (outcome.Disposition)
            {
                case ElevatedItemDisposition.Interrupted:
                    await run.RecordAsync(job, UpdateHistoryStatus.VerificationRequired, outcome.ExitCode, "elevated-session-interrupted; verification-required").ConfigureAwait(false);
                    break;
                case ElevatedItemDisposition.NotStarted:
                    await run.RecordAsync(job, Map(outcome.Status), outcome.ExitCode, skipReasons.TryGetValue(job.Id, out string? reason) ? SkipDiagnostic(reason) : outcome.Diagnostic).ConfigureAwait(false);
                    break;
                default:
                    await FinishAsync(run, job, new(outcome.Status, outcome.RestartPending, outcome.Diagnostic, outcome.ExitCode), abort).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task FinishAsync(Run run, ImmutableUpdateJob job, ProviderExitAssessment outcome, CancellationToken abort)
    {
        if (!outcome.AwaitsVerification)
        {
            await run.RecordAsync(job, Map(outcome.Status), outcome.ExitCode, outcome.Diagnostic).ConfigureAwait(false);
            return;
        }

        try
        {
            PostUpdateInventoryResult verification = await verifier.ReInventoryAsync(job, abort).ConfigureAwait(false);
            (UpdateHistoryStatus status, string diagnostic) = verification.ExpectedVersionDetected
                ? (UpdateHistoryStatus.Success, "target-version-verified-by-post-update-inventory")
                : verification.RestartPending || outcome.RestartPending
                    ? (UpdateHistoryStatus.RestartRequired, "provider-reported-reliable-restart-pending-state")
                    : (UpdateHistoryStatus.Inconclusive, "provider-succeeded-but-target-version-was-not-detected");
            await run.RecordAsync(job, status, outcome.ExitCode, diagnostic).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            await run.RecordAsync(job, UpdateHistoryStatus.VerificationRequired, outcome.ExitCode, "verification-interrupted-after-provider-completed").ConfigureAwait(false);
        }
        catch (Exception)
        {
            await run.RecordAsync(job, UpdateHistoryStatus.VerificationFailed, outcome.ExitCode, "post-update-inventory-verification-failed").ConfigureAwait(false);
        }
    }

    private async Task<string?> ApplyRunningApplicationPolicyAsync(BatchRunOptions options, ImmutableUpdateJob job, CancellationToken abort)
    {
        RunningApplicationDetection detection;
        try
        {
            detection = await runningApplications.DetectAsync(job.Selection, abort).ConfigureAwait(false);
        }
        catch (Exception) when (!abort.IsCancellationRequested)
        {
            // Unknown is treated as running: the safe default is to skip, never to close.
            return "running-application-check-failed";
        }

        bool perAppConsent = options.Policies.TryGetValue(job.Selection.StableProductKey, out ProductUpdatePolicy? policy) && policy.CloseAppConsent;
        RunningApplicationAction action = RunningApplicationPolicy.Decide(detection, perAppConsent, options.RunConsent, options.Automation);
        if (action == RunningApplicationAction.Proceed) return null;
        if (action == RunningApplicationAction.Skip) return "running-application-skipped";
        bool closed;
        try
        {
            closed = await closer.CloseAsync(detection.ProcessNames, RunningApplicationPolicy.AllowsForce(options.RunConsent, options.Automation), RunningApplicationPolicy.GracefulWait, abort).ConfigureAwait(false);
        }
        catch (Exception) when (!abort.IsCancellationRequested)
        {
            closed = false;
        }

        return closed ? null : "running-application-did-not-close";
    }

    private async Task<UpdateJobExecutionEvidence?> TryRevalidateAsync(ImmutableUpdateJob job, CancellationToken abort)
    {
        try
        {
            return await revalidator.RevalidateAsync(job, abort).ConfigureAwait(false);
        }
        catch (Exception) when (!abort.IsCancellationRequested)
        {
            return null;
        }
    }

    private static BatchRunResult NotStarted(string message) => new(false, message, [], BatchCompletionSummary.From([]));

    private static string SkipDiagnostic(string reason) => reason == BatchSkipReasons.RunningApplication
        ? "running-application-skipped"
        : "revalidation-failed; review-again";

    internal static UpdateHistoryStatus Map(UpdateProviderAttemptStatus status) => status switch
    {
        UpdateProviderAttemptStatus.Cancelled => UpdateHistoryStatus.Cancelled,
        UpdateProviderAttemptStatus.Skipped => UpdateHistoryStatus.Skipped,
        UpdateProviderAttemptStatus.SourceFailed => UpdateHistoryStatus.SourceFailed,
        UpdateProviderAttemptStatus.DownloadFailed => UpdateHistoryStatus.DownloadFailed,
        UpdateProviderAttemptStatus.InstallerFailed => UpdateHistoryStatus.InstallerFailed,
        UpdateProviderAttemptStatus.ElevationDenied => UpdateHistoryStatus.ElevationDenied,
        UpdateProviderAttemptStatus.TimedOut => UpdateHistoryStatus.TimedOut,
        UpdateProviderAttemptStatus.AwaitingVerification => UpdateHistoryStatus.VerificationRequired,
        _ => UpdateHistoryStatus.ProductFailed
    };

    private sealed class Run(BatchExecutionCoordinator owner, BatchRunOptions options, IProgress<BatchItemProgress>? progress)
    {
        private readonly List<UpdateHistoryEntry> entries = [];
        private readonly Dictionary<Guid, UpdateHistoryEntry> finals = [];

        public BatchRunOptions Options { get; } = options;
        public IReadOnlyList<UpdateHistoryEntry> Entries => entries;
        public IEnumerable<UpdateHistoryEntry> FinalEntries => finals.Values.OrderBy(entry => entries.IndexOf(entry));

        public Task CancelAsync(ImmutableUpdateJob job, string diagnostic) => RecordAsync(job, UpdateHistoryStatus.Cancelled, null, diagnostic);

        public void Report(ImmutableUpdateJob job, UpdateHistoryStatus status) => progress?.Report(new(job.Id, job.Selection.StableProductKey, status));

        public async Task RecordAsync(ImmutableUpdateJob job, UpdateHistoryStatus status, int? exitCode, string diagnostic)
        {
            UpdateHistoryEntry entry = new(
                Guid.NewGuid(), job.Id, job.CorrelationId, job.Selection.StableProductKey, job.Selection.Provider, job.Selection.PackageId,
                job.Selection.InstalledVersion ?? UpdateHistoryEntry.NotInstalledVersion, job.Selection.CandidateVersion, owner.utcNow(), status, exitCode,
                SanitizedDiagnosticBundle.Sanitize(diagnostic));
            await owner.history.RecordAsync(entry, CancellationToken.None).ConfigureAwait(false);
            entries.Add(entry);
            if (status is not (UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.RunningApplicationWarning)) finals[job.Id] = entry;
            Report(job, status);
        }
    }
}

