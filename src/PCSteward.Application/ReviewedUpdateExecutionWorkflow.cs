using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Fresh, typed evidence prepared by inventory before an update can be reviewed.</summary>
public sealed record UpdateSelectionPreparation(UpdateJobSelection? Selection, string Message);

public sealed record UpdateReviewPreparation(UpdateJobReview? Review, string Message);

public sealed record ReviewedUpdateExecutionResult(bool Started, string Message, UpdateBatchResult? Batch);

/// <summary>
/// Keeps the UI out of the execution-authority boundary. It can only review a selection prepared
/// from fresh inventory evidence, and it obtains that evidence again immediately before execution.
/// </summary>
public sealed class ReviewedUpdateExecutionWorkflow(
    Func<string, CancellationToken, Task<UpdateSelectionPreparation>> prepareSelection,
    UpdateJobApprovalService approvals,
    SequentialUpdateBatchCoordinator coordinator)
{
    private readonly Func<string, CancellationToken, Task<UpdateSelectionPreparation>> prepareSelection = prepareSelection ?? throw new ArgumentNullException(nameof(prepareSelection));
    private readonly UpdateJobApprovalService approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
    private readonly SequentialUpdateBatchCoordinator coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));

    public async Task<UpdateReviewPreparation> BeginReviewAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        UpdateSelectionPreparation preparation = await prepareSelection(stableProductKey, cancellationToken).ConfigureAwait(false);
        if (preparation.Selection is null) return new(null, preparation.Message);
        return new(UpdateJobApprovalService.BeginReview(Guid.NewGuid(), Guid.NewGuid(), preparation.Selection), preparation.Message);
    }

    public async Task<ReviewedUpdateExecutionResult> ConfirmAndRunAsync(UpdateJobReview review, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        UpdateSelectionPreparation preparation = await prepareSelection(review.Selection.StableProductKey, cancellationToken).ConfigureAwait(false);
        if (preparation.Selection is null || !review.Selection.EqualsEvidence(preparation.Selection))
        {
            return new(false, "The approved update evidence changed or is no longer fresh. No update was started.", null);
        }

        ImmutableUpdateJob job = await approvals.ConfirmAsync(review, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        UpdateBatchResult batch = await coordinator.RunAsync([new UpdateBatchItem(job, new UpdateJobExecutionEvidence(preparation.Selection, true, true))], cancellationToken).ConfigureAwait(false);
        UpdateHistoryEntry? final = batch.FinalEntries.SingleOrDefault();
        string message = final is null
            ? "The reviewed update did not produce a final result."
            : $"Update result: {final.Status}. {final.Diagnostic}";
        return new(true, message, batch);
    }
}

/// <summary>Never closes processes. The desktop asks the user to close an application before review.</summary>
public sealed class NoRunningApplicationDetector : IRunningApplicationDetector
{
    public Task<bool> IsRunningAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        return Task.FromResult(false);
    }
}
