using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Passive reviewed data for comparison/history; this store never grants live consent or a dispatch permit.</summary>
public interface IReviewedUpdateOperationStore
{
    Task SaveReviewedOperationAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken);
    Task<ReviewedUpdateOperation?> GetReviewedOperationAsync(Guid jobId, CancellationToken cancellationToken);
}
