using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Fresh, typed evidence prepared by inventory before an update can be reviewed.</summary>
public sealed record UpdateSelectionPreparation(UpdateJobSelection? Selection, string Message);

public sealed record UpdateReviewPreparation(UpdateJobReview? Review, string Message, RunningApplicationDetection RunningApplication);

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
        return new(review, preparation.Message, running);
    }

    public async Task<ReviewedUpdateExecutionResult> ConfirmAndRunAsync(UpdateJobReview review, bool runningApplicationWarningAcknowledged, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(review);
        UpdateSelectionPreparation preparation = await prepareSelection(review.Selection.StableProductKey, cancellationToken).ConfigureAwait(false);
        if (preparation.Selection is null || !review.Selection.EqualsEvidence(preparation.Selection))
        {
            return new(false, "The approved update evidence changed or is no longer fresh. No update was started.", null);
        }

        RunningApplicationDetection running = await runningApplications.DetectAsync(preparation.Selection, cancellationToken).ConfigureAwait(false);
        if (running.AppearsToBeRunning && !runningApplicationWarningAcknowledged)
        {
            return new(false, $"Package {preparation.Selection.PackageId} appears to be running. Review the target-specific warning and explicitly confirm before installing; PC Steward did not close it or start the provider.", null);
        }

        ImmutableUpdateJob job = await approvals.ConfirmAsync(review, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        UpdateBatchResult batch = await coordinator.RunAsync(
            [new UpdateBatchItem(job, new UpdateJobExecutionEvidence(preparation.Selection, true, true), runningApplicationWarningAcknowledged)],
            cancellationToken).ConfigureAwait(false);
        if (batch.AcknowledgementRequired is { AppearsToBeRunning: true } lateRunning)
        {
            return new(
                false,
                $"Package {preparation.Selection.PackageId} started running after confirmation (detected process: {string.Join(", ", lateRunning.ProcessNames)}). PC Steward stopped before the provider. Open a new review and explicitly acknowledge the target-specific warning to continue; PC Steward did not close it.",
                batch);
        }
        UpdateHistoryEntry? final = batch.FinalEntries.SingleOrDefault();
        string message = final is null
            ? "The reviewed update did not produce a final result."
            : $"Update result: {final.Status}. {final.Diagnostic}";
        return new(true, message, batch);
    }
}

/// <summary>Returns a bounded package-derived process-name snapshot and never closes a process.</summary>
public sealed class PackageDerivedRunningApplicationDetector : IRunningApplicationDetector
{
    private const int MaximumProcesses = 4096;
    private const int MaximumProcessNameLength = 64;
    private static readonly HashSet<string> AmbiguousSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "app", "application", "client", "desktop", "installer", "launcher", "setup", "update", "updater"
    };
    private readonly Func<IReadOnlyCollection<string>> processNames;
    private readonly bool failClosed;

    public PackageDerivedRunningApplicationDetector() : this(failClosed: false) { }

    /// <summary>Strict snapshots reject incomplete evidence instead of authorizing an elevated start.</summary>
    public PackageDerivedRunningApplicationDetector(bool failClosed) : this(() => SnapshotProcessNames(failClosed), failClosed) { }

    internal PackageDerivedRunningApplicationDetector(Func<IReadOnlyCollection<string>> processNames, bool failClosed = false)
    {
        this.processNames = processNames ?? throw new ArgumentNullException(nameof(processNames));
        this.failClosed = failClosed;
    }

    public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> candidates = new(StringComparer.OrdinalIgnoreCase);
        AddCandidate(candidates, selection.PackageId);
        string suffix = selection.PackageId.Split('.').Last();
        if (!AmbiguousSuffixes.Contains(suffix)) AddCandidate(candidates, suffix);
        IReadOnlyCollection<string> snapshot = processNames();
        if (failClosed && (snapshot.Count > MaximumProcesses || snapshot.Any(string.IsNullOrEmpty)))
            throw new InvalidOperationException("The running-application snapshot is incomplete.");
        string[] matches = snapshot
            .Take(MaximumProcesses)
            .Where(name => candidates.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult(matches.Length == 0
            ? RunningApplicationDetection.NotDetected
            : new RunningApplicationDetection(true, matches));
    }

    private static void AddCandidate(HashSet<string> candidates, string value)
    {
        string candidate = value.Trim();
        if (candidate.Length is >= 4 and <= MaximumProcessNameLength &&
            candidate.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_'))
        {
            candidates.Add(candidate);
        }
    }

    private static List<string> SnapshotProcessNames(bool failClosed)
    {
        System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcesses();
        List<string> names = [];
        try
        {
            if (failClosed && processes.Length > MaximumProcesses)
                throw new InvalidOperationException("The running-application snapshot exceeds its bound.");
            foreach (System.Diagnostics.Process process in processes.Take(MaximumProcesses))
            {
                try
                {
                    string name = process.ProcessName;
                    if (name.Length is > 0 and <= MaximumProcessNameLength) names.Add(name);
                    else if (failClosed && name.Length == 0) throw new InvalidOperationException("A process name could not be inspected.");
                }
                catch (InvalidOperationException) when (!failClosed) { }
                catch (System.ComponentModel.Win32Exception) when (!failClosed) { }
                catch (NotSupportedException) when (!failClosed) { }
            }
        }
        finally
        {
            foreach (System.Diagnostics.Process process in processes) process.Dispose();
        }
        return names;
    }
}

