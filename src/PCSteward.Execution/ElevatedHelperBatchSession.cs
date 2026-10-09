using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>
/// ADR-018 protocol v2, helper side (FR-BAT-05/06, AC-V3-06): runs one accepted batch sequentially.
/// Before each item it honours a desktop skip, then revalidates the item against fresh provider evidence and
/// builds the provider operation in code. CancelRemaining stops the next unstarted item; it never interrupts
/// a running provider. An overall deadline bounds the whole session. Every item gets exactly one result.
/// </summary>
internal sealed class ElevatedHelperBatchSession
{
    internal static readonly TimeSpan ItemBudget = TimeSpan.FromMinutes(25);
    internal static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(4);
    private readonly HelperBatchRequest request;
    private readonly string nonce;
    private readonly IOperationRevalidator revalidator;
    private readonly IOperationExecutor executor;
    private readonly IRunningApplicationDetector runningApplications;
    private readonly TimeSpan deadline;
    private int cancelRemaining;

    internal ElevatedHelperBatchSession(HelperBatchRequest request, string nonce, IOperationRevalidator revalidator, IOperationExecutor executor, TimeSpan? deadline = null, IRunningApplicationDetector? runningApplications = null)
    {
        this.runningApplications = runningApplications ?? new PackageDerivedRunningApplicationDetector(failClosed: true);
        this.request = request ?? throw new ArgumentNullException(nameof(request));
        this.nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));
        this.revalidator = revalidator ?? throw new ArgumentNullException(nameof(revalidator));
        this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        this.deadline = deadline ?? Deadline(request.Items.Count);
        if (this.deadline <= TimeSpan.Zero || this.deadline > MaximumDuration) throw new ArgumentOutOfRangeException(nameof(deadline));
    }

    /// <summary>The sum of per-item budgets, hard-capped (ADR-018 v2).</summary>
    internal static TimeSpan Deadline(int itemCount) => TimeSpan.FromTicks(Math.Min(ItemBudget.Ticks * Math.Max(1, itemCount), MaximumDuration.Ticks));

    internal bool CancelRemainingRequested => Volatile.Read(ref cancelRemaining) != 0;

    internal void RequestCancelRemaining() => Interlocked.Exchange(ref cancelRemaining, 1);

    /// <summary>
    /// Sends ItemStarted/ItemResult for every item and a final BatchComplete. <paramref name="abort"/> means the
    /// desktop is gone: the running provider is cancelled and nothing further is sent.
    /// </summary>
    internal async Task RunAsync(Func<byte[], Task> send, CancellationToken abort)
    {
        ArgumentNullException.ThrowIfNull(send);
        using CancellationTokenSource deadlineSource = new(deadline);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(abort, deadlineSource.Token);
        string? stopReason = null;
        foreach (HelperBatchItem item in request.Items)
        {
            if (abort.IsCancellationRequested) return;
            // A desktop skip was decided before elevation, so it is reported as such even after a cancel.
            if (item.SkipReason is not null)
            {
                await send(Result(item, UpdateProviderAttemptStatus.Skipped, null, false, "skipped-before-elevation: " + item.SkipReason)).ConfigureAwait(false);
                continue;
            }

            if (stopReason is null && deadlineSource.IsCancellationRequested) stopReason = "batch-deadline-exceeded";
            if (stopReason is null && CancelRemainingRequested) stopReason = "cancel-remaining-before-start";
            if (stopReason is not null)
            {
                await send(Result(item, UpdateProviderAttemptStatus.Cancelled, null, false, stopReason)).ConfigureAwait(false);
                continue;
            }

            UpdateJobExecutionEvidence? evidence;
            try
            {
                evidence = await revalidator.RevalidateAsync(item.Job, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                evidence = null;
            }

            // Revalidation is awaited and can complete after CancelRemaining arrived.
            if (abort.IsCancellationRequested) return;
            if (deadlineSource.IsCancellationRequested) stopReason = "batch-deadline-exceeded";
            else if (CancelRemainingRequested) stopReason = "cancel-remaining-before-start";
            if (stopReason is not null)
            {
                await send(Result(item, UpdateProviderAttemptStatus.Cancelled, null, false, stopReason)).ConfigureAwait(false);
                continue;
            }

            if (evidence is null || !item.Job.Matches(evidence))
            {
                string reason = deadlineSource.IsCancellationRequested ? "batch-deadline-exceeded" : "helper-revalidation-failed; review-again";
                await send(Result(item, UpdateProviderAttemptStatus.Skipped, null, false, reason)).ConfigureAwait(false);
                continue;
            }

            // The desktop all-clear predates UAC and all earlier providers. Recheck here, as a read-only
            // veto: the helper never inherits close consent or closes processes in its elevated context.
            string? runningApplicationBlock = null;
            try
            {
                if ((await runningApplications.DetectAsync(item.Job.Selection, linked.Token).ConfigureAwait(false)).AppearsToBeRunning)
                    runningApplicationBlock = "running-application-skipped";
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                runningApplicationBlock = "running-application-check-failed";
            }

            // The process snapshot can also yield. CancelRemaining still applies until ItemStarted.
            if (abort.IsCancellationRequested) return;
            if (deadlineSource.IsCancellationRequested) stopReason = "batch-deadline-exceeded";
            else if (CancelRemainingRequested) stopReason = "cancel-remaining-before-start";
            if (stopReason is not null)
            {
                await send(Result(item, UpdateProviderAttemptStatus.Cancelled, null, false, stopReason)).ConfigureAwait(false);
                continue;
            }
            if (runningApplicationBlock is not null)
            {
                await send(Result(item, UpdateProviderAttemptStatus.Skipped, null, false, runningApplicationBlock)).ConfigureAwait(false);
                continue;
            }

            await send(ElevatedHelperProtocolV2.CreateItemStarted(request.BatchId, nonce, item.Sequence, item.Job.Id)).ConfigureAwait(false);
            ProviderExitAssessment outcome;
            try
            {
                outcome = await executor.ExecuteAsync(item.Job, evidence, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException) when (deadlineSource.IsCancellationRequested)
            {
                outcome = new(UpdateProviderAttemptStatus.TimedOut, false, "batch-deadline-exceeded");
            }
            catch (Exception)
            {
                outcome = new(UpdateProviderAttemptStatus.ProductFailed, false, "product-could-not-start-approved-operation");
            }

            // A started item never ends as Skipped or Cancelled; it has a provider outcome.
            if (outcome.Status is UpdateProviderAttemptStatus.Skipped or UpdateProviderAttemptStatus.Cancelled or UpdateProviderAttemptStatus.ElevationDenied)
            {
                outcome = new(UpdateProviderAttemptStatus.ProductFailed, false, outcome.Diagnostic, outcome.ExitCode);
            }

            await send(Result(item, outcome.Status, outcome.ExitCode, outcome.RestartPending && outcome.AwaitsVerification, outcome.Diagnostic)).ConfigureAwait(false);
        }

        if (!abort.IsCancellationRequested)
        {
            await send(ElevatedHelperProtocolV2.CreateBatchComplete(request.BatchId, nonce, request.Items.Count)).ConfigureAwait(false);
        }
    }

    private byte[] Result(HelperBatchItem item, UpdateProviderAttemptStatus status, int? exitCode, bool restartPending, string diagnostic) =>
        ElevatedHelperProtocolV2.CreateItemResult(request.BatchId, nonce, item.Sequence, item.Job.Id, status, exitCode, restartPending, diagnostic);
}

