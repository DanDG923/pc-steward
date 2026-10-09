using PCSteward.Domain;

namespace PCSteward.Application;

public interface IUpdateJobStore
{
    Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
    Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken);
}

/// <summary>FR-BAT-04: approved batches are stored with their ordered jobs and rechecked on every read.</summary>
public interface IOperationBatchStore
{
    Task SaveApprovedBatchAsync(ApprovedOperationBatch batch, CancellationToken cancellationToken);
    Task<ApprovedOperationBatch?> GetApprovedBatchAsync(Guid batchId, CancellationToken cancellationToken);
}

/// <summary>FR-INS-03: acceptance of one exact agreement set for one exact package version.</summary>
public interface IAgreementAcceptanceStore
{
    Task RecordAgreementAcceptanceAsync(AgreementAcceptance acceptance, CancellationToken cancellationToken);
    Task<bool> HasAgreementAcceptanceAsync(UpdateSource source, string packageId, string version, string agreementHash, CancellationToken cancellationToken);
}

/// <summary>
/// FR-BAT-04 / FR-INS-03: confirming a reviewed batch. An item that carries an agreement hash is approvable
/// only when the user's acceptance of exactly that agreement set and version was recorded first.
/// </summary>
public sealed class OperationBatchApprovalService(IOperationBatchStore batches, IAgreementAcceptanceStore agreements)
{
    private readonly IOperationBatchStore batches = batches ?? throw new ArgumentNullException(nameof(batches));
    private readonly IAgreementAcceptanceStore agreements = agreements ?? throw new ArgumentNullException(nameof(agreements));

    public async Task<ApprovedOperationBatch> ConfirmAsync(OperationBatchReview review, DateTimeOffset approvedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        foreach (UpdateJobSelection selection in review.Items.Select(item => item.Selection).Where(selection => selection.AgreementHash is not null))
        {
            if (!await agreements.HasAgreementAcceptanceAsync(selection.Source, selection.PackageId, selection.CandidateVersion, selection.AgreementHash!, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"The agreements for {selection.PackageId} {selection.CandidateVersion} have not been accepted.");
            }
        }

        ApprovedOperationBatch batch = review.Confirm(approvedAt);
        await batches.SaveApprovedBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        return batch;
    }

    /// <summary>Called only after the review sheet showed the agreements whose hash is recorded.</summary>
    public Task RecordAcceptanceAsync(UpdateSource source, string packageId, string version, string agreementHash, DateTimeOffset acceptedAt, CancellationToken cancellationToken) =>
        agreements.RecordAgreementAcceptanceAsync(new AgreementAcceptance(Guid.NewGuid(), source, packageId, version, agreementHash, acceptedAt), cancellationToken);
}

public sealed class UpdateJobApprovalService(IUpdateJobStore store)
{
    private readonly IUpdateJobStore store = store ?? throw new ArgumentNullException(nameof(store));

    public static UpdateJobReview BeginReview(Guid jobId, Guid correlationId, UpdateJobSelection selection) =>
        new(jobId, correlationId, selection);

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
