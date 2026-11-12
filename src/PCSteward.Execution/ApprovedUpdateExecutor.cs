using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>Adapts constrained runners to the batch workflow without exposing process construction.</summary>
public sealed class ApprovedUpdateExecutor : IApprovedUpdateExecutor
{
    private readonly NonElevatedUpdateRunner nonElevated;
    private readonly ElevatedUpdateRunner? elevated;
    public UpdateExecutionLifetime ExecutionLifetime { get; } = new();

    public ApprovedUpdateExecutor(NonElevatedUpdateRunner nonElevated)
        : this(nonElevated, null)
    {
    }

    public ApprovedUpdateExecutor(NonElevatedUpdateRunner nonElevated, ElevatedUpdateRunner? elevated)
    {
        this.nonElevated = nonElevated ?? throw new ArgumentNullException(nameof(nonElevated));
        this.elevated = elevated;
    }

    public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(job, null, currentEvidence, null, cancellationToken);

    public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence,
        UpdateItemProgress progress, CancellationToken cancellationToken) => ExecuteCoreAsync(job, null, currentEvidence, progress, cancellationToken);

    /// <summary>
    /// Stage C4: a user-scope job runs only with its per-user reviewed operation. Phase D stage D4b: a machine-scope job runs only with
    /// its frozen whole-PC operation, on the protocol-2 helper; without one nothing starts (stage 3 retired protocol 1).
    /// </summary>
    public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
        UpdateItemProgress progress, CancellationToken cancellationToken) => ExecuteCoreAsync(job, reviewed, currentEvidence, progress, cancellationToken);

    private async Task<UpdateProviderAttempt> ExecuteCoreAsync(ImmutableUpdateJob job, ReviewedUpdateOperation? reviewed, UpdateJobExecutionEvidence currentEvidence,
        UpdateItemProgress? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(currentEvidence);
        if (job.Selection.Scope == UpdateScope.Machine && elevated is null)
        {
            return new(UpdateProviderAttemptStatus.ProductFailed, null, "standalone-package-machine-scope-disabled");
        }
        // D4b stage 3: a machine-scope job runs only on its frozen operation's protocol-2 route. Protocol 1 is retired, so a job
        // without one starts nothing (ADR-025 transition amendment, closed).
        if (job.Selection.Scope == UpdateScope.Machine && reviewed is null)
            return new(UpdateProviderAttemptStatus.ProductFailed, null, "reviewed-machine-route-unavailable");
        UpdateExecutionResult result = job.Selection.Scope == UpdateScope.Machine
            ? await elevated!.RunReviewedAsync(job, reviewed!, currentEvidence, cancellationToken, progress).ConfigureAwait(false)
            : await nonElevated.RunAsync(job, reviewed, currentEvidence, cancellationToken, progress).ConfigureAwait(false);
        return new(Map(result.Status), result.ExitCode, result.SanitizedOutput, result.OwnedCompletion);
    }

    private static UpdateProviderAttemptStatus Map(UpdateExecutionStatus status) => status switch
    {
        UpdateExecutionStatus.AwaitingVerification => UpdateProviderAttemptStatus.AwaitingVerification,
        UpdateExecutionStatus.RestartReported => UpdateProviderAttemptStatus.RestartReported,
        UpdateExecutionStatus.VerificationRequired => UpdateProviderAttemptStatus.VerificationRequired,
        UpdateExecutionStatus.Cancelled => UpdateProviderAttemptStatus.Cancelled,
        UpdateExecutionStatus.ElevationDenied => UpdateProviderAttemptStatus.ElevationDenied,
        UpdateExecutionStatus.TimedOut => UpdateProviderAttemptStatus.TimedOut,
        UpdateExecutionStatus.ProviderFailed => UpdateProviderAttemptStatus.InstallerFailed,
        UpdateExecutionStatus.OutputLimitExceeded or UpdateExecutionStatus.ProductFailed or UpdateExecutionStatus.HelperFailed or UpdateExecutionStatus.Denied or UpdateExecutionStatus.ElevationRequired => UpdateProviderAttemptStatus.ProductFailed,
        _ => UpdateProviderAttemptStatus.ProductFailed
    };
}
