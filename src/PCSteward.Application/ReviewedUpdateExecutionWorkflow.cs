using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Fresh, typed evidence prepared by inventory before an update can be reviewed.</summary>
public sealed record UpdateSelectionPreparation(UpdateJobSelection? Selection, string Message);

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
    IRunningApplicationDetector runningApplications)
{
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
}
