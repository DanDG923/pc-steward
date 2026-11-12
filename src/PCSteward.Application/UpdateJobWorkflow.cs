using PCSteward.Domain;

namespace PCSteward.Application;

public interface IUpdateJobStore
{
    Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
    Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken);
}

public sealed class UpdateJobApprovalService(IUpdateJobStore store)
{
    private readonly IUpdateJobStore store = store ?? throw new ArgumentNullException(nameof(store));

    public static UpdateJobReview BeginReview(Guid jobId, Guid correlationId, UpdateJobSelection selection) =>
        new(jobId, correlationId, selection);

    /// <summary>Stage C4: a review with the installer mode bound at review time (null when the route has no reviewed mode).</summary>
    public static UpdateJobReview BeginReview(Guid jobId, Guid correlationId, UpdateJobSelection selection, ReviewedInstallerMode? installerMode) =>
        new(jobId, correlationId, selection, installerMode);

    public async Task<ImmutableUpdateJob> ConfirmAsync(UpdateJobReview review, DateTimeOffset approvedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        ImmutableUpdateJob job = review.Confirm(approvedAt);
        await store.SaveApprovedJobAsync(job, cancellationToken).ConfigureAwait(false);
        return job;
    }
}

public enum UpdateExecutionEventSeverity { Information, Warning, Error }
public sealed record UpdateExecutionEvent(
    Guid JobId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    UpdateExecutionEventSeverity Severity,
    string EventId,
    UpdateProvider Provider,
    string PackageId,
    string TargetVersion,
    string Message);

public interface IUpdateExecutionEventSink
{
    void Write(UpdateExecutionEvent updateEvent);
}

public sealed class NullUpdateExecutionEventSink : IUpdateExecutionEventSink
{
    public static NullUpdateExecutionEventSink Instance { get; } = new();
    public void Write(UpdateExecutionEvent updateEvent) { }
}
