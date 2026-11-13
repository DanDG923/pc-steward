using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Fresh, typed evidence prepared by inventory before an update can be reviewed.</summary>
public sealed record UpdateSelectionPreparation(UpdateJobSelection? Selection, string Message, LiveInventoryObservation? Observation = null,
    WingetArchitectureProofRequest? ArchitectureProof = null);

public sealed record UpdateReviewPreparation(UpdateJobReview? Review, string Message, RunningApplicationDetection RunningApplication,
    UpdateProgressMailbox? Progress = null);

public sealed record ReviewedUpdateExecutionResult(bool Started, string Message, UpdateBatchResult? Batch)
{
    /// <summary>True when nothing was approved or started because the review was past the 30-minute dispatch ceiling.</summary>
    public bool ReviewExpired { get; init; }

    /// <summary>True when cancellation arrived before the provider could start, so nothing was started (Task 52).</summary>
    public bool StoppedBeforeStart { get; init; }
}

/// <summary>
/// Keeps the UI out of the execution-authority boundary. It can only review a selection prepared
/// from fresh inventory evidence, and it obtains that evidence again immediately before execution.
/// <paramref name="prepareSelections"/> (optional) prepares a frozen batch for review from one shared fresh inventory observation; without
/// it each batch item is prepared through <paramref name="prepareSelection"/>. Dispatch always re-prepares each item on its own.
/// Stage C4 (Task 54 R1, FR-UPD-15): a per-user WinGet review binds its installer mode (<see cref="WingetUserReviewedRoute.BindMode"/>);
/// the mode is checked again against the fresh re-preparation before approval, and the approved job is frozen with it into the
/// per-user reviewed operation the runner requires. <paramref name="requestingUserSid"/> reads the current Windows account's SID for
/// that operation; without it no per-user update starts. Chocolatey updates are not reviewed here; they have their own reviewed route.
/// Phase D stage D4b: a machine-scope WinGet review binds Silent, the one HKLM registration's digest from the fresh inventory and the
/// installer evidence of its own <c>winget download</c> (<paramref name="machineInstallerEvidence"/>, <see cref="WingetMachineReviewedRoute"/>);
/// Confirm checks the registration again and freezes the whole-PC reviewed operation the elevated runner requires. Without the evidence
/// source a machine-scope WinGet update is not offered: stage 3 retired helper protocol 1 (ADR-025 transition amendment, closed).
/// D4b 32-bit-registered apps (design note §3.3–3.4): a preparation that carries an architecture proof request is reviewed only when
/// <paramref name="architectureProof"/> proves the x64 architecture; its digest binds the 32-bit view the entry was read from, and the
/// uninstall entry the target manifest declares goes with the approved job to the post-update check.
/// </summary>
public sealed class ReviewedUpdateExecutionWorkflow(
    Func<string, CancellationToken, Task<UpdateSelectionPreparation>> prepareSelection,
    UpdateJobApprovalService approvals,
    SequentialUpdateBatchCoordinator coordinator,
    IRunningApplicationDetector runningApplications,
    Func<DateTimeOffset>? utcNow = null,
    Func<IReadOnlyList<string>, IProgress<int>?, CancellationToken, Task<IReadOnlyList<UpdateSelectionPreparation>>>? prepareSelections = null,
    Func<string?>? requestingUserSid = null,
    IWingetMachineInstallerEvidenceSource? machineInstallerEvidence = null,
    IWingetArchitectureProofSource? architectureProof = null)
{
    /// <summary>Stage C4 (ADR-025 transition amendment): Chocolatey is never reviewed or run on this route or on protocol 1.</summary>
    public const string ChocolateyNotOnThisRouteMessage = "Chocolatey updates use their own review, so this update can't be started from here.";

    /// <summary>Why an app that couldn't be prepared isn't in the frozen set.</summary>
    public const string CouldNotPrepareMessage = "PC Steward could not prepare this update, so it isn't included.";

    /// <summary>L2 (Task 73 Phase 6 review): a one-app review past the dispatch ceiling. Nothing was approved or started.</summary>
    public const string SingleReviewExpiredMessage = "This review is more than 30 minutes old. Review again to update.";

    /// <summary>A batch app whose turn came after the dispatch ceiling. Nothing was approved or started for it.</summary>
    public const string BatchReviewExpiredMessage = "The review was more than 30 minutes old when this app's turn came, so it wasn't started. Review it again.";

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

    private readonly Func<IReadOnlyList<string>, IProgress<int>?, CancellationToken, Task<IReadOnlyList<UpdateSelectionPreparation>>>? prepareSelections = prepareSelections;
    private readonly Func<string?>? requestingUserSid = requestingUserSid;
    private readonly IWingetMachineInstallerEvidenceSource? machineInstallerEvidence = machineInstallerEvidence;
    private readonly IWingetArchitectureProofSource? architectureProof = architectureProof;

    public async Task<UpdateReviewPreparation> BeginReviewAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        UpdateSelectionPreparation preparation = await prepareSelection(stableProductKey, cancellationToken).ConfigureAwait(false);
        return await ReviewPreparedSelectionAsync(preparation, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns fresh prepared evidence into an immutable review, its running-app detection and its own progress mailbox.</summary>
    private async Task<UpdateReviewPreparation> ReviewPreparedSelectionAsync(UpdateSelectionPreparation preparation, CancellationToken cancellationToken)
    {
        if (preparation.Selection is null) return new(null, preparation.Message, RunningApplicationDetection.NotDetected);
        if (!TryBindReviewMode(preparation.Selection, out ReviewedInstallerMode? mode, out string refusal))
            return new(null, refusal, RunningApplicationDetection.NotDetected);
        UpdateJobReview review;
        if (UsesMachineRoute(preparation.Selection))
        {
            // D4b stage 2b: the registration from this fresh inventory, then the installer evidence from the review's own download.
            WingetArchitectureProofRequest? proofRequest = preparation.ArchitectureProof;
            if (proofRequest is not null && architectureProof is null)
                return new(null, InventoryWorkflow.ArchitectureNotConfirmedMessage, RunningApplicationDetection.NotDetected);
            if (preparation.Observation is not { } observation ||
                WingetMachineReviewedRoute.RegistrationDigest(preparation.Selection, observation, proofRequest is not null) is not { } registration)
                return new(null, WingetMachineReviewedRoute.RegistrationNotFoundMessage, RunningApplicationDetection.NotDetected);
            WingetMachineInstallerCheck check = await machineInstallerEvidence!.CheckAsync(preparation.Selection, cancellationToken).ConfigureAwait(false);
            if (check.Evidence is not { } installer || !TryMachineEvidenceId(installer, registration, out string evidenceId))
                return new(null, check.Message, RunningApplicationDetection.NotDetected);
            if (proofRequest is not null)
            {
                // Design note §3.3: after the target download and before anything is bound, prove x64 from the installed version (P1)
                // or from WinGet's installer selection for both versions (P2). No proof, no review.
                WingetArchitectureProofResult proof = await architectureProof!.ProveAsync(preparation.Selection, proofRequest, cancellationToken).ConfigureAwait(false);
                if (proof is not { Proven: true })
                    return new(null, proof?.Message ?? InventoryWorkflow.ArchitectureNotConfirmedMessage, RunningApplicationDetection.NotDetected);
            }
            review = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), preparation.Selection, mode)
            {
                EvidenceId = evidenceId,
                EvidenceObservedAt = observation.ObservedAt,
                // Design note §3.4: only for these apps, the entry the target's installer declares; without one (Build Tools) the
                // reviewed key at exactly the target version. The post-update check reads it instead of comparing the registry view.
                ExpectedEntry = proofRequest is null ? null
                    : installer.DeclaredEntry ?? new ExpectedUninstallEntry(proofRequest.RegistrationKey, preparation.Selection.CandidateVersion),
                ArchitectureProven = proofRequest is not null,
            };
        }
        else
        {
            review = UpdateJobApprovalService.BeginReview(Guid.NewGuid(), Guid.NewGuid(), preparation.Selection, mode);
        }
        RunningApplicationDetection running = await runningApplications.DetectAsync(review.Selection, cancellationToken).ConfigureAwait(false);
        return new(review, preparation.Message, running,
            new UpdateProgressMailbox(review.JobId, review.CorrelationId, review.Selection.Scope));
    }

    /// <summary>
    /// Stage C4: the installer mode a review of <paramref name="selection"/> binds. A WinGet user-scope selection binds its reviewed
    /// mode or is refused; a WinGet machine-scope selection binds none (protocol-1 transition) unless the whole-PC route's installer
    /// evidence is composed (D4b stage 2b), when it binds that route's mode or is refused; Chocolatey is refused here.
    /// </summary>
    private bool TryBindReviewMode(UpdateJobSelection selection, out ReviewedInstallerMode? mode, out string refusal)
    {
        mode = null;
        refusal = string.Empty;
        if (selection.Provider != UpdateProvider.WinGet) { refusal = ChocolateyNotOnThisRouteMessage; return false; }
        if (selection.Scope == UpdateScope.Machine)
        {
            // D4b stage 3: protocol 1 is retired, so a machine-scope WinGet update runs only on the whole-PC route, and only when its
            // review evidence source is composed.
            mode = machineInstallerEvidence is null ? null : WingetMachineReviewedRoute.BindMode(selection);
            if (mode is null) refusal = WingetMachineReviewedRoute.NotOnThisRouteMessage;
            return mode is not null;
        }
        mode = WingetUserReviewedRoute.BindMode(selection);
        if (mode is null) refusal = WingetUserReviewedRoute.NoReviewedModeMessage;
        return mode is not null;
    }

    /// <summary>D4b: a WinGet machine-scope selection, which is reviewed only on the whole-PC route (stage 3 retired protocol 1).</summary>
    private static bool UsesMachineRoute(UpdateJobSelection selection) =>
        selection.Provider == UpdateProvider.WinGet && selection.Scope == UpdateScope.Machine;

    private static bool TryMachineEvidenceId(WingetMachineInstallerEvidence installer, string registration, out string evidenceId)
    {
        evidenceId = string.Empty;
        try
        {
            evidenceId = new WinGetMachineEvidence(installer.InstallerType, registration, installer.ManifestDigest, installer.InstallerSha256).ToIdentity();
            return ReviewedUpdateOperation.TryGetWinGetMachineEvidence(evidenceId, out _);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Stage C4: the mode bound at review still applies to the fresh re-preparation.</summary>
    private bool ReviewedModeStillApplies(UpdateJobReview review, UpdateJobSelection fresh) =>
        TryBindReviewMode(fresh, out ReviewedInstallerMode? mode, out _) && mode == review.InstallerMode;

    /// <summary>
    /// D4b stage 2b: the fresh inventory's registration digest equals the one in the review's <c>wgm1</c> evidence. An app whose
    /// architecture the review proved must still be prepared for that proof, and the reverse (design note §3.3).
    /// </summary>
    private static bool RegistrationStillApplies(UpdateJobReview review, UpdateSelectionPreparation preparation) =>
        preparation.Selection is { } fresh && (preparation.ArchitectureProof is not null) == review.ArchitectureProven &&
        WingetMachineReviewedRoute.RegistrationDigest(fresh, preparation.Observation, review.ArchitectureProven) is { } digest &&
        ReviewedUpdateOperation.TryGetWinGetMachineEvidence(review.EvidenceId, out WinGetMachineEvidence? evidence) && evidence is not null &&
        string.Equals(digest, evidence.RegistrationDigest, StringComparison.Ordinal);

    private string? ReadRequestingUserSid()
    {
        try { return requestingUserSid?.Invoke(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    /// <summary>The message for an item whose cancellation arrived before its provider could start (Task 52).</summary>
    public const string StoppedBeforeStartMessage = "The update was stopped before it started, so nothing was changed.";

    /// <summary>
    /// Re-prepares fresh evidence and approves and runs <paramref name="review"/> only if it is unchanged. When <paramref name="reviewedAt"/>
    /// is given (a frozen batch review), the 30-minute dispatch ceiling is checked again after the re-preparation, immediately before
    /// approval: a review that expired meanwhile is never approved or started (review L2). When <paramref name="acknowledgedRunningProcesses"/>
    /// is given, the acknowledgement covers only those processes: a process detected now that the owner wasn't warned about is a new
    /// warning, and the item is not started (Task 52). Cancellation before approval starts nothing and says so.
    /// </summary>
    public async Task<ReviewedUpdateExecutionResult> ConfirmAndRunAsync(UpdateJobReview review, bool runningApplicationWarningAcknowledged, CancellationToken cancellationToken, UpdateProgressMailbox? progress = null,
        DateTimeOffset? reviewedAt = null, IReadOnlyCollection<string>? acknowledgedRunningProcesses = null)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (progress is not null && !progress.Matches(review.JobId, review.CorrelationId, review.Selection.Scope))
            throw new ArgumentException("Progress must belong to this review.", nameof(progress));
        UpdateSelectionPreparation preparation;
        RunningApplicationDetection running;
        ImmutableUpdateJob job;
        ReviewedUpdateOperation? reviewedOperation = null;
        try
        {
            preparation = await prepareSelection(review.Selection.StableProductKey, cancellationToken).ConfigureAwait(false);
            if (preparation.Selection is null || !review.Selection.EqualsEvidence(preparation.Selection))
            {
                return new(false, "The approved update evidence changed or is no longer fresh. No update was started.", null);
            }

            // Stage C4 (Task 54 R1): the installer mode bound at review is checked again against the fresh evidence, before approval.
            if (!ReviewedModeStillApplies(review, preparation.Selection))
                return new(false, review.Selection.Provider == UpdateProvider.WinGet ? WingetUserReviewedRoute.ModeChangedMessage : ChocolateyNotOnThisRouteMessage, null);
            bool machineRoute = UsesMachineRoute(review.Selection);
            // D4b stage 2b: the whole-PC registration bound at review must still be the one in the fresh inventory.
            if (machineRoute && !RegistrationStillApplies(review, preparation))
                return new(false, WingetMachineReviewedRoute.RegistrationChangedMessage, null);
            string? requester = null;
            if ((review.Selection.Scope == UpdateScope.User || machineRoute) && string.IsNullOrWhiteSpace(requester = ReadRequestingUserSid()))
                return new(false, WingetUserReviewedRoute.RequesterUnavailableMessage, null);

            running = await runningApplications.DetectAsync(preparation.Selection, cancellationToken).ConfigureAwait(false);
            if (!RunningApplicationAcknowledgement.Covers(runningApplicationWarningAcknowledged, acknowledgedRunningProcesses, running))
            {
                string changed = runningApplicationWarningAcknowledged
                    ? $"It is now running as {string.Join(", ", running.ProcessNames)}, which wasn't part of the warning you confirmed. "
                    : string.Empty;
                return new(false, $"Package {preparation.Selection.PackageId} appears to be running. {changed}Review the target-specific warning and explicitly confirm before installing; PC Steward did not close it or start the provider. {running.CoverageDisclosure}", null);
            }

            // Review L2: re-preparation can take a while, so the dispatch ceiling is read again here, immediately before approval.
            if (reviewedAt is { } reviewed && IsPastDispatchCeiling(reviewed, utcNow()))
                return new(false, SingleReviewExpiredMessage, null) { ReviewExpired = true };

            // ADR-025: the approval (and so any helper request built from it) is made just in time, when this item's turn comes.
            job = await approvals.ConfirmAsync(review, utcNow(), cancellationToken).ConfigureAwait(false);
            if (job.Selection.Scope == UpdateScope.User)
            {
                // Stage C4: freeze the approved job with the reviewed mode; the runner runs nothing user-scope without it. A saved
                // approval alone starts nothing and is never replayed.
                LiveInventoryObservation? observation = preparation.Observation;
                reviewedOperation = WingetUserReviewedRoute.Freeze(job, review.InstallerMode, requester,
                    observation is null ? $"review:{review.CorrelationId:D}" : $"inventory:{observation.CorrelationId:D}",
                    observation?.ObservedAt ?? job.ApprovedAt);
                if (reviewedOperation is null) return new(false, WingetUserReviewedRoute.NoReviewedModeMessage, null);
            }
            else if (machineRoute)
            {
                // D4b: freeze the approved job with the reviewed mode and wgm1 evidence; the runner runs nothing machine-scope without
                // it (stage 3). A saved approval alone starts nothing and is never replayed.
                reviewedOperation = WingetMachineReviewedRoute.Freeze(job, review.InstallerMode, requester, review.EvidenceId,
                    review.EvidenceObservedAt);
                if (reviewedOperation is null) return new(false, WingetMachineReviewedRoute.NotOnThisRouteMessage, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Task 52: nothing reached the provider. A saved approval alone starts nothing and is never replayed.
            return new(false, StoppedBeforeStartMessage, null) { StoppedBeforeStart = true };
        }
        UpdateBatchResult batch = await coordinator.RunAsync(
            [new UpdateBatchItem(job, new UpdateJobExecutionEvidence(preparation.Selection, true, true), runningApplicationWarningAcknowledged, progress,
                acknowledgedRunningProcesses, reviewedOperation, review.ExpectedEntry)],
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
    /// SPEC §9.13 G4, FR-UPD-10, AC-UPD-01 (Task 73 Phase 6): reviews a frozen set of products. Each item gets its own fresh evidence,
    /// immutable review, running-app detection and progress mailbox. An item that can't be prepared is excluded with its explanation;
    /// nothing is approved or started here.
    /// </summary>
    public Task<UpdateBatchReviewPreparation> BeginBatchReviewAsync(IReadOnlyList<string> stableProductKeys, CancellationToken cancellationToken) =>
        BeginBatchReviewAsync(stableProductKeys, null, cancellationToken);

    /// <summary>
    /// As <see cref="BeginBatchReviewAsync(IReadOnlyList{string}, CancellationToken)"/>, reporting "n of N prepared" to
    /// <paramref name="progress"/>. Review M2: when the batch preparer is composed, every item is validated against ONE fresh inventory
    /// observation instead of a full scan per item. <see cref="UpdateBatchReviewPreparation.ReviewedAt"/> is taken once preparation has
    /// finished, when the sheet can be shown, so preparation never uses up the 30-minute dispatch ceiling. Cancelling
    /// <paramref name="cancellationToken"/> abandons the preparation; nothing was approved or started.
    /// </summary>
    public async Task<UpdateBatchReviewPreparation> BeginBatchReviewAsync(IReadOnlyList<string> stableProductKeys, IProgress<UpdateBatchReviewProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stableProductKeys);
        if (stableProductKeys.Count is 0 or > MaximumBatchItems) throw new ArgumentOutOfRangeException(nameof(stableProductKeys), $"A batch review holds 1 to {MaximumBatchItems} apps.");
        if (stableProductKeys.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Every batch item needs a product key.", nameof(stableProductKeys));
        if (stableProductKeys.Distinct(StringComparer.Ordinal).Count() != stableProductKeys.Count) throw new ArgumentException("A batch review cannot hold the same app twice.", nameof(stableProductKeys));
        string[] keys = [.. stableProductKeys];
        int total = keys.Length;
        void Report(int prepared)
        {
            try { progress?.Report(new(Math.Clamp(prepared, 0, total), total)); }
            catch (Exception) { /* A presentation callback can never change or interrupt the review. */ }
        }
        Report(0);
        IReadOnlyList<UpdateSelectionPreparation?> selections = await PrepareSelectionsAsync(keys, Report, cancellationToken).ConfigureAwait(false);
        List<UpdateBatchReviewItem> items = [];
        List<UpdateBatchExclusion> excluded = [];
        for (int index = 0; index < total; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = keys[index];
            UpdateReviewPreparation preparation;
            try
            {
                if (selections[index] is not { } selection) { excluded.Add(new(key, CouldNotPrepareMessage)); continue; }
                preparation = await ReviewPreparedSelectionAsync(selection, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { excluded.Add(new(key, CouldNotPrepareMessage)); continue; }
            if (preparation.Review is null || !string.Equals(preparation.Review.Selection.StableProductKey, key, StringComparison.Ordinal))
            {
                excluded.Add(new(key, preparation.Review is null ? preparation.Message : "The prepared update didn't match this app, so it isn't included."));
                continue;
            }
            items.Add(new(key, preparation));
        }
        // Review M2: the ceiling starts when the sheet can be shown, after every item was prepared; dispatch still re-prepares each item.
        return new(items, excluded, utcNow());
    }

    /// <summary>One prepared selection per key, in order; null means that key's preparation faulted.</summary>
    private async Task<IReadOnlyList<UpdateSelectionPreparation?>> PrepareSelectionsAsync(string[] keys, Action<int> report, CancellationToken cancellationToken)
    {
        if (prepareSelections is not null)
        {
            IReadOnlyList<UpdateSelectionPreparation> shared = await prepareSelections(keys, new CallbackProgress(report), cancellationToken).ConfigureAwait(false);
            // A preparer that doesn't answer every key, in order, is not trusted for any of them.
            if (shared is null || shared.Count != keys.Length) throw new InvalidOperationException("The batch preparation did not return one result per app.");
            return [.. shared];
        }
        UpdateSelectionPreparation?[] prepared = new UpdateSelectionPreparation?[keys.Length];
        for (int index = 0; index < keys.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { prepared[index] = await prepareSelection(keys[index], cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { prepared[index] = null; }
            report(index + 1);
        }
        return prepared;
    }

    private sealed class CallbackProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private static bool IsPastDispatchCeiling(DateTimeOffset reviewedAt, DateTimeOffset now)
    {
        TimeSpan age = now - reviewedAt;
        // A clock that went backwards fails closed.
        return age < TimeSpan.Zero || age > MaximumBatchDispatchAge;
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
        // Review L6: a frozen batch review is confirmed once. A second run is refused before anything is re-prepared or approved.
        if (!review.TryClaimForRun()) throw new InvalidOperationException("This batch review has already been confirmed. Start a new review.");
        bool single = review.Items.Count == 1;
        List<UpdateBatchItemResult> results = [];
        UpdateBatchItemDisposition? halt = null;
        for (int index = 0; index < review.Items.Count; index++)
        {
            UpdateBatchReviewItem item = review.Items[index];
            UpdateJobReview jobReview = item.Preparation.Review!;
            if (halt is null && !stop.IsRequested && index > 0) await SettlePreviousOwnedWorkAsync(cancellationToken).ConfigureAwait(false);
            UpdateBatchItemDisposition? skipped = halt ?? (stop.IsRequested || cancellationToken.IsCancellationRequested ? UpdateBatchItemDisposition.StoppedBeforeStart
                : IsPastDispatchCeiling(review.ReviewedAt, utcNow()) ? UpdateBatchItemDisposition.ReviewExpired : null);
            if (skipped is { } notStarted)
            {
                results.Add(new(index, item.StableProductKey, jobReview.Selection, NotStartedResult(notStarted, single), notStarted));
                Notify(notice, new(index, true, results[^1]));
                continue;
            }
            Notify(notice, new(index, false, null));
            ReviewedUpdateExecutionResult result;
            try
            {
                RunningApplicationDetection warned = item.Preparation.RunningApplication;
                result = await ConfirmAndRunAsync(jobReview, warned.AppearsToBeRunning, cancellationToken, item.Preparation.Progress, review.ReviewedAt,
                    warned.AppearsToBeRunning ? warned.ProcessNames : []).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Never Verified: it may or may not have run, and nothing checked it. Stop before the next app.
                result = new(true, "The reviewed update did not produce a recorded outcome. Execution or verification may have been interrupted. No verified success was recorded.", null);
                halt = UpdateBatchItemDisposition.StoppedAfterEarlierProblem;
            }
            if (result.StoppedBeforeStart)
            {
                results.Add(new(index, item.StableProductKey, jobReview.Selection, NotStartedResult(UpdateBatchItemDisposition.StoppedBeforeStart, single), UpdateBatchItemDisposition.StoppedBeforeStart));
                Notify(notice, new(index, true, results[^1]));
                continue;
            }
            if (result.ReviewExpired)
            {
                // Expired during this item's own re-preparation (review L2): never approved, never started.
                results.Add(new(index, item.StableProductKey, jobReview.Selection, NotStartedResult(UpdateBatchItemDisposition.ReviewExpired, single), UpdateBatchItemDisposition.ReviewExpired));
                Notify(notice, new(index, true, results[^1]));
                continue;
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

    private static ReviewedUpdateExecutionResult NotStartedResult(UpdateBatchItemDisposition disposition, bool single) =>
        new(false, NotStartedMessage(disposition, single), null) { ReviewExpired = disposition == UpdateBatchItemDisposition.ReviewExpired };

    private static string NotStartedMessage(UpdateBatchItemDisposition disposition, bool single) => disposition switch
    {
        UpdateBatchItemDisposition.ReviewExpired => single ? SingleReviewExpiredMessage : BatchReviewExpiredMessage,
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

/// <summary>
/// A frozen batch review (SPEC §9.13 G4). <see cref="ReviewedAt"/> starts the 30-minute dispatch ceiling. It can be confirmed and
/// run once (review L6); copies made with <c>with</c> share the same single use.
/// </summary>
public sealed record UpdateBatchReviewPreparation(IReadOnlyList<UpdateBatchReviewItem> Items, IReadOnlyList<UpdateBatchExclusion> Excluded, DateTimeOffset ReviewedAt)
{
    private readonly SingleUse runClaim = new();

    /// <summary>When an app that hasn't started yet stops being startable from this review (UTC).</summary>
    public DateTimeOffset ExpiresAt => ReviewedAt + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge;

    /// <summary>True only for the first run of this review.</summary>
    internal bool TryClaimForRun() => runClaim.TryClaim();

    private sealed class SingleUse
    {
        private int claimed;
        public bool TryClaim() => Interlocked.Exchange(ref claimed, 1) == 0;
    }
}

/// <summary>Batch review preparation progress: <see cref="Prepared"/> of <see cref="Total"/> apps prepared.</summary>
public sealed record UpdateBatchReviewProgress(int Prepared, int Total);

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
