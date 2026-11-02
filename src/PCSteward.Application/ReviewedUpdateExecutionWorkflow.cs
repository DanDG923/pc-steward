using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Fresh, typed evidence prepared by inventory before an update can be reviewed.</summary>
public sealed record UpdateSelectionPreparation(UpdateJobSelection? Selection, string Message, LiveInventoryObservation? Observation = null);

public sealed record UpdateReviewPreparation(UpdateJobReview? Review, string Message, RunningApplicationDetection RunningApplication,
    UpdateProgressMailbox? Progress = null);

public sealed record ReviewedUpdateExecutionResult(bool Started, string Message, UpdateBatchResult? Batch);

/// <summary>
/// Keeps the UI out of the execution-authority boundary. It can only review a selection prepared
/// from fresh inventory evidence, and it obtains that evidence again immediately before execution.
/// </summary>
public sealed class ReviewedUpdateExecutionWorkflow(
    Func<string, CancellationToken, Task<UpdateSelectionPreparation>> prepareSelection,
    UpdateJobApprovalService approvals,
    SequentialUpdateBatchCoordinator coordinator,
    IRunningApplicationDetector runningApplications,
    Func<DateTimeOffset>? utcNow = null)
{
    /// <summary>
    /// The owner-accepted frozen batch review ceiling (V2 planning decisions, 2026-10-29): each item must be dispatched within 30 elapsed
    /// minutes of its review. An unstarted item past it is never renewed silently; it needs a fresh review.
    /// </summary>
    public static readonly TimeSpan MaximumBatchDispatchAge = TimeSpan.FromMinutes(30);

    /// <summary>The most apps one batch review may hold (bounded; the Ready group is far smaller in practice).</summary>
    public const int MaximumBatchItems = 256;

    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    /// <summary>
    /// How long a batch waits for the previous item's owned work to settle before the next item asks for admission. Past it, the
    /// existing admission check decides, and a refusal stops the remaining items (fail-closed); nothing is ever forced.
    /// </summary>
    internal TimeSpan OwnedWorkSettleLimit { get; init; } = TimeSpan.FromMinutes(2);
    private readonly Func<string, CancellationToken, Task<UpdateSelectionPreparation>> prepareSelection = prepareSelection ?? throw new ArgumentNullException(nameof(prepareSelection));
    private readonly UpdateJobApprovalService approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    private readonly SequentialUpdateBatchCoordinator coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    private readonly IRunningApplicationDetector runningApplications = runningApplications ?? throw new ArgumentNullException(nameof(runningApplications));

    public async Task<UpdateReviewPreparation> BeginReviewAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        UpdateSelectionPreparation preparation = await prepareSelection(stableProductKey, cancellationToken).ConfigureAwait(false);
        if (preparation.Selection is null) return new(null, preparation.Message, RunningApplicationDetection.NotDetected);
        UpdateJobReview review = UpdateJobApprovalService.BeginReview(Guid.NewGuid(), Guid.NewGuid(), preparation.Selection);
        RunningApplicationDetection running = await runningApplications.DetectAsync(review.Selection, cancellationToken).ConfigureAwait(false);
        return new(review, preparation.Message, running,
            new UpdateProgressMailbox(review.JobId, review.CorrelationId, review.Selection.Scope));
    }

    public async Task<ReviewedUpdateExecutionResult> ConfirmAndRunAsync(UpdateJobReview review, bool runningApplicationWarningAcknowledged, CancellationToken cancellationToken, UpdateProgressMailbox? progress = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (progress is not null && !progress.Matches(review.JobId, review.CorrelationId, review.Selection.Scope))
            throw new ArgumentException("Progress must belong to this review.", nameof(progress));
        UpdateSelectionPreparation preparation = await prepareSelection(review.Selection.StableProductKey, cancellationToken).ConfigureAwait(false);
        if (preparation.Selection is null || !review.Selection.EqualsEvidence(preparation.Selection))
        {
            return new(false, "The approved update evidence changed or is no longer fresh. No update was started.", null);
        }

        RunningApplicationDetection running = await runningApplications.DetectAsync(preparation.Selection, cancellationToken).ConfigureAwait(false);
        if (running.AppearsToBeRunning && !runningApplicationWarningAcknowledged)
        {
            return new(false, $"Package {preparation.Selection.PackageId} appears to be running. Review the target-specific warning and explicitly confirm before installing; PC Steward did not close it or start the provider. {running.CoverageDisclosure}", null);
        }

        ImmutableUpdateJob job = await approvals.ConfirmAsync(review, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        UpdateBatchResult batch = await coordinator.RunAsync(
            [new UpdateBatchItem(job, new UpdateJobExecutionEvidence(preparation.Selection, true, true), runningApplicationWarningAcknowledged, progress)],
            cancellationToken).ConfigureAwait(false);
        if (batch.AdmissionRefused)
            return new(false, "The update was not started: another update owns execution or previous owned work is still unresolved.", batch);
        if (batch.AcknowledgementRequired is { AppearsToBeRunning: true } lateRunning)
        {
            return new(
                false,
                $"Package {preparation.Selection.PackageId} started running after confirmation (detected process: {string.Join(", ", lateRunning.ProcessNames)}). PC Steward stopped before the provider. Open a new review and explicitly acknowledge the target-specific warning to continue; PC Steward did not close it. {lateRunning.CoverageDisclosure}",
                batch);
        }
        UpdateHistoryEntry? final = batch.FinalEntries.SingleOrDefault();
        string message = final is null
            ? "The reviewed update did not produce a final result."
            : $"Update result: {final.Status}. {final.Diagnostic}";
        return new(true, message, batch);
    }

    /// <summary>
    /// SPEC §9.13 G4, FR-UPD-10, AC-UPD-01 (Task 73 Phase 6): reviews a frozen set of products. Each key goes through the existing
    /// single-item <see cref="BeginReviewAsync"/>, so each item gets its own fresh evidence, immutable review, running-app detection and
    /// progress mailbox. An item that can't be prepared is excluded with its explanation; nothing is approved or started here.
    /// </summary>
    public async Task<UpdateBatchReviewPreparation> BeginBatchReviewAsync(IReadOnlyList<string> stableProductKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stableProductKeys);
        if (stableProductKeys.Count is 0 or > MaximumBatchItems) throw new ArgumentOutOfRangeException(nameof(stableProductKeys), $"A batch review holds 1 to {MaximumBatchItems} apps.");
        if (stableProductKeys.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Every batch item needs a product key.", nameof(stableProductKeys));
        if (stableProductKeys.Distinct(StringComparer.Ordinal).Count() != stableProductKeys.Count) throw new ArgumentException("A batch review cannot hold the same app twice.", nameof(stableProductKeys));
        // The dispatch ceiling is measured from before the first item is prepared, so it can only be shorter, never longer.
        DateTimeOffset reviewedAt = utcNow();
        List<UpdateBatchReviewItem> items = [];
        List<UpdateBatchExclusion> excluded = [];
        foreach (string key in stableProductKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateReviewPreparation preparation;
            try { preparation = await BeginReviewAsync(key, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { excluded.Add(new(key, "PC Steward could not prepare this update, so it isn't included.")); continue; }
            if (preparation.Review is null || !string.Equals(preparation.Review.Selection.StableProductKey, key, StringComparison.Ordinal))
            {
                excluded.Add(new(key, preparation.Review is null ? preparation.Message : "The prepared update didn't match this app, so it isn't included."));
                continue;
            }
            items.Add(new(key, preparation));
        }
        return new(items, excluded, reviewedAt);
    }

    /// <summary>
    /// SPEC §9.13 G4, FR-UPD-10/11, AC-UPD-09/10 (Task 73 Phase 6): runs a confirmed frozen batch one app at a time. Each item goes through
    /// the existing <see cref="ConfirmAndRunAsync"/> immediately before it starts, so its evidence is revalidated, its running-app
    /// acknowledgement re-checked and its own immutable job approved and (for machine scope) elevated on its own — fail-closed: a changed
    /// item is not started. <paramref name="stop"/> is read only between items; it never cancels the running item. Earlier results are
    /// kept as recorded. An unexpected fault or a refused execution admission stops the remaining items, which are reported not started.
    /// </summary>
    public async Task<UpdateBatchRunResult> ConfirmAndRunBatchAsync(UpdateBatchReviewPreparation review, UpdateBatchStop stop,
        Action<UpdateBatchItemNotice>? notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(stop);
        List<UpdateBatchItemResult> results = [];
        UpdateBatchItemDisposition? halt = null;
        for (int index = 0; index < review.Items.Count; index++)
        {
            UpdateBatchReviewItem item = review.Items[index];
            UpdateJobReview jobReview = item.Preparation.Review!;
            if (halt is null && !stop.IsRequested && index > 0) await SettlePreviousOwnedWorkAsync(cancellationToken).ConfigureAwait(false);
            TimeSpan age = utcNow() - review.ReviewedAt;
            UpdateBatchItemDisposition? skipped = halt ?? (stop.IsRequested || cancellationToken.IsCancellationRequested ? UpdateBatchItemDisposition.StoppedBeforeStart
                : age < TimeSpan.Zero || age > MaximumBatchDispatchAge ? UpdateBatchItemDisposition.ReviewExpired : null);
            if (skipped is { } notStarted)
            {
                results.Add(new(index, item.StableProductKey, jobReview.Selection, new(false, NotStartedMessage(notStarted), null), notStarted));
                Notify(notice, new(index, true, results[^1]));
                continue;
            }
            Notify(notice, new(index, false, null));
            ReviewedUpdateExecutionResult result;
            try
            {
                result = await ConfirmAndRunAsync(jobReview, item.Preparation.RunningApplication.AppearsToBeRunning, cancellationToken, item.Preparation.Progress).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Never Verified: it may or may not have run, and nothing checked it. Stop before the next app.
                result = new(true, "The reviewed update did not produce a recorded outcome. Execution or verification may have been interrupted. No verified success was recorded.", null);
                halt = UpdateBatchItemDisposition.StoppedAfterEarlierProblem;
            }
            if (result.Batch is { AdmissionRefused: true }) halt = UpdateBatchItemDisposition.StoppedAfterEarlierProblem;
            results.Add(new(index, item.StableProductKey, jobReview.Selection, result, UpdateBatchItemDisposition.Ran));
            Notify(notice, new(index, true, results[^1]));
        }
        return new(results, stop.IsRequested);
    }

    private async Task SettlePreviousOwnedWorkAsync(CancellationToken cancellationToken)
    {
        Task owned = coordinator.OwnedWorkCompletion;
        if (owned.IsCompleted) return;
        try { await owned.WaitAsync(OwnedWorkSettleLimit, cancellationToken).ConfigureAwait(false); }
        catch (TimeoutException) { /* Admission decides: still-owned work refuses the next item, which stops the batch. */ }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* Read as stopped below. */ }
        catch (Exception) { /* A faulted owned task is observed by the execution lifetime; admission decides. */ }
    }

    private static string NotStartedMessage(UpdateBatchItemDisposition disposition) => disposition switch
    {
        UpdateBatchItemDisposition.ReviewExpired => "The review was more than 30 minutes old when this app's turn came, so it wasn't started. Review it again.",
        UpdateBatchItemDisposition.StoppedAfterEarlierProblem => "An earlier update in this batch didn't finish normally, so PC Steward stopped before this app.",
        _ => "You stopped the remaining updates before this app started."
    };

    private static void Notify(Action<UpdateBatchItemNotice>? notice, UpdateBatchItemNotice value)
    {
        try { notice?.Invoke(value); }
        catch (Exception) { /* A presentation callback can never change or interrupt a recorded outcome. */ }
    }
}

/// <summary>One reviewable item of a frozen batch: its own fresh review, running-app detection and progress mailbox.</summary>
public sealed record UpdateBatchReviewItem(string StableProductKey, UpdateReviewPreparation Preparation);

/// <summary>An app the user selected that couldn't be reviewed, with the reason in words. It is not part of the frozen set.</summary>
public sealed record UpdateBatchExclusion(string StableProductKey, string Message);

/// <summary>A frozen batch review (SPEC §9.13 G4). <see cref="ReviewedAt"/> starts the 30-minute dispatch ceiling.</summary>
public sealed record UpdateBatchReviewPreparation(IReadOnlyList<UpdateBatchReviewItem> Items, IReadOnlyList<UpdateBatchExclusion> Excluded, DateTimeOffset ReviewedAt);

/// <summary>Why a batch item ended the way it did. Only <see cref="Ran"/> reached the existing per-item confirm-and-run path.</summary>
public enum UpdateBatchItemDisposition
{
    Ran,
    StoppedBeforeStart,
    ReviewExpired,
    StoppedAfterEarlierProblem
}

/// <summary>One item's result, in batch order. <see cref="Result"/> is the existing single-item result; it is authoritative.</summary>
public sealed record UpdateBatchItemResult(int Index, string StableProductKey, UpdateJobSelection Selection, ReviewedUpdateExecutionResult Result,
    UpdateBatchItemDisposition Disposition);

/// <summary>Every item's result in batch order, and whether the user asked to stop.</summary>
public sealed record UpdateBatchRunResult(IReadOnlyList<UpdateBatchItemResult> Items, bool StopRequested);

/// <summary>A presentation notice: item <see cref="Index"/> is starting, or has finished with <see cref="Result"/>.</summary>
public sealed record UpdateBatchItemNotice(int Index, bool Finished, UpdateBatchItemResult? Result);

/// <summary>
/// FR-UPD-11, AC-UPD-10: "stop after this app". It is read only before an item starts, so it never cancels an installer that is
/// already running. Requesting it is idempotent and thread-safe.
/// </summary>
public sealed class UpdateBatchStop
{
    private int requested;
    public bool IsRequested => Volatile.Read(ref requested) == 1;
    public void Request() => Interlocked.Exchange(ref requested, 1);
}
