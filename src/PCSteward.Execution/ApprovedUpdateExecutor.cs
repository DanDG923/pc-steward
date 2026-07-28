using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>Adapts constrained runners to the batch workflow without exposing process construction.</summary>
public sealed class ApprovedUpdateExecutor(NonElevatedUpdateRunner nonElevated, ElevatedUpdateRunner elevated) : IApprovedUpdateExecutor
{
    private readonly NonElevatedUpdateRunner nonElevated = nonElevated ?? throw new ArgumentNullException(nameof(nonElevated));
    private readonly ElevatedUpdateRunner elevated = elevated ?? throw new ArgumentNullException(nameof(elevated));

    public async Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(currentEvidence);
        UpdateExecutionResult result = job.Selection.Scope == UpdateScope.Machine
            ? await elevated.RunAsync(job, currentEvidence, cancellationToken).ConfigureAwait(false)
            : await nonElevated.RunAsync(job, currentEvidence, cancellationToken).ConfigureAwait(false);
        return new(Map(result.Status), result.ExitCode, result.SanitizedOutput);
    }

    private static UpdateProviderAttemptStatus Map(UpdateExecutionStatus status) => status switch
    {
        UpdateExecutionStatus.AwaitingVerification => UpdateProviderAttemptStatus.AwaitingVerification,
        UpdateExecutionStatus.Cancelled => UpdateProviderAttemptStatus.Cancelled,
        UpdateExecutionStatus.ElevationDenied => UpdateProviderAttemptStatus.ElevationDenied,
        UpdateExecutionStatus.TimedOut => UpdateProviderAttemptStatus.TimedOut,
        UpdateExecutionStatus.ProviderFailed => UpdateProviderAttemptStatus.InstallerFailed,
        UpdateExecutionStatus.OutputLimitExceeded or UpdateExecutionStatus.ProductFailed or UpdateExecutionStatus.HelperFailed or UpdateExecutionStatus.Denied or UpdateExecutionStatus.ElevationRequired => UpdateProviderAttemptStatus.ProductFailed,
        _ => UpdateProviderAttemptStatus.ProductFailed
    };
}
