using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// SPEC §9.13 G4 (Task 73 Phase 6): reviewed multi-select / Update all. A batch reviews a frozen set, freshly revalidates every item
/// immediately before it starts, explains excluded and stale items, stops only between items and preserves earlier results.
/// </summary>
[TestClass]
public sealed class UpdateBatchWorkflowTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly string[] KeysAC = ["a", "c"];
    private static readonly UpdateOutcomeKind[] VerifiedNotStartedVerified = [UpdateOutcomeKind.Verified, UpdateOutcomeKind.NotStarted, UpdateOutcomeKind.Verified];
    private static readonly string[] KeysABC = ["a", "b", "c"];
    private static readonly string[] KeysA = ["a"];
    private static readonly string[] KeysAB = ["a", "b"];
    private static readonly string[] KeysABCD = ["a", "b", "c", "d"];
    private static readonly UpdateOutcomeKind[] MixedOutcomes = [UpdateOutcomeKind.Verified, UpdateOutcomeKind.Failed, UpdateOutcomeKind.NotStarted, UpdateOutcomeKind.InstalledNotVerified];
    private static readonly string[] NoticeOrder = ["0:start", "0:end", "1:start", "1:end", "2:end"];
    private static readonly UpdateHistoryStatus[] RestartRestartRestartInconclusive = [UpdateHistoryStatus.RestartRequired, UpdateHistoryStatus.RestartRequired, UpdateHistoryStatus.RestartRequired, UpdateHistoryStatus.Inconclusive];
    private static readonly UpdateOutcomeKind[] RestartRestartRestartNotVerified = [UpdateOutcomeKind.RestartRequired, UpdateOutcomeKind.RestartRequired, UpdateOutcomeKind.RestartRequired, UpdateOutcomeKind.InstalledNotVerified];

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("AC-UPD-01")]
    public async Task BatchReviewPreparesEachItemFreshlyExplainsExclusionsAndApprovesNothing()
    {
        Harness harness = new();
        harness.Unavailable.Add("b");

        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);

        CollectionAssert.AreEqual(KeysAC, review.Items.Select(item => item.StableProductKey).ToArray());
        Assert.AreEqual("b", review.Excluded.Single().StableProductKey);
        StringAssert.Contains(review.Excluded.Single().Message, "no longer fresh");
        Assert.AreEqual(2, review.Items.Select(item => item.Preparation.Review!.JobId).Distinct().Count(), "each item has its own immutable review");
        Assert.IsTrue(review.Items.All(item => item.Preparation.Progress is not null && item.Preparation.Progress.Matches(item.Preparation.Review!.JobId, item.Preparation.Review.CorrelationId, item.Preparation.Review.Selection.Scope)));
        Assert.AreEqual(Start, review.ReviewedAt);
        Assert.IsEmpty(harness.Store.Jobs, "reviewing approves nothing");
        Assert.IsEmpty(harness.Executor.Keys, "reviewing starts nothing");
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task BatchReviewRejectsEmptyDuplicateAndOversizedSets()
    {
        Harness harness = new();
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => harness.Workflow.BeginBatchReviewAsync([], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => harness.Workflow.BeginBatchReviewAsync(["a", "a"], CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => harness.Workflow.BeginBatchReviewAsync(
            [.. Enumerable.Range(0, ReviewedUpdateExecutionWorkflow.MaximumBatchItems + 1).Select(index => $"k{index}")], CancellationToken.None));
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("AC-UPD-09")]
    public async Task ItemsRunOneAtATimeAndAChangedItemIsNotStarted()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);
        harness.Candidates["b"] = "9.0.0"; // b's evidence changes after review

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysAC, harness.Executor.Keys.ToArray(), "sequential, in review order; the changed item never reaches the provider");
        Assert.HasCount(2, harness.Store.Jobs, "only revalidated items are approved, each as its own immutable job");
        Assert.IsTrue(run.Items.All(item => item.Disposition == UpdateBatchItemDisposition.Ran));
        Assert.IsFalse(run.Items[1].Result.Started);
        StringAssert.Contains(run.Items[1].Result.Message, "evidence changed");
        Assert.IsTrue(harness.PrepareCalls.Values.All(calls => calls == 2), "every item was prepared at review and again just before its own start");
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => $"App {key}");
        CollectionAssert.AreEqual(VerifiedNotStartedVerified, completion.Items.Select(item => item.Outcome.Kind).ToArray());
        CollectionAssert.AreEqual(KeysABC, completion.Items.Select(item => item.StableProductKey).ToArray());
        Assert.AreEqual("2 verified · 1 not started", completion.Headline);
    }

    [TestMethod]
    [TestCategory("FR-UPD-11")]
    [TestCategory("AC-UPD-10")]
    public async Task StopIsReadBetweenItemsOnlyAndNeverCancelsTheRunningItem()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);
        UpdateBatchStop stop = new();
        harness.Executor.OnExecute = (key, token) =>
        {
            if (key == "a") stop.Request();
            Assert.IsFalse(token.IsCancellationRequested, "stop never cancels the item that is running");
        };

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, stop, null, CancellationToken.None);

        Assert.IsTrue(run.StopRequested);
        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray(), "the next unstarted items never run");
        Assert.AreEqual(UpdateHistoryStatus.Success, run.Items[0].Result.Batch!.FinalEntries.Single().Status, "the running item finished and was verified");
        Assert.IsTrue(run.Items.Skip(1).All(item => item.Disposition == UpdateBatchItemDisposition.StoppedBeforeStart && !item.Result.Started));
        Assert.HasCount(1, harness.Store.Jobs, "unstarted items are never approved");
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => key);
        Assert.AreEqual(UpdateOutcomeKind.Verified, completion.Items[0].Outcome.Kind, "the completed result is preserved");
        Assert.IsTrue(completion.Items.Skip(1).All(item => item.Outcome.Kind == UpdateOutcomeKind.NotStarted && item.Outcome.Summary.Contains("you stopped", StringComparison.Ordinal)));
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task AnItemPastTheThirtyMinuteDispatchCeilingNeedsAFreshReview()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        harness.Executor.OnExecute = (_, _) => harness.Now = Start + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge + TimeSpan.FromSeconds(1);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        Assert.AreEqual(UpdateBatchItemDisposition.Ran, run.Items[0].Disposition);
        Assert.AreEqual(UpdateBatchItemDisposition.ReviewExpired, run.Items[1].Disposition);
        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray());
        StringAssert.Contains(UpdateOutcomePresenter.ForBatch(run, key => key).Items[1].Outcome.Summary, "more than 30 minutes old");

        harness.Now = Start;
        harness.Executor.OnExecute = null;
        UpdateBatchReviewPreparation fresh = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        harness.Now = Start.AddMinutes(-1);
        UpdateBatchRunResult clockWentBack = await harness.Workflow.ConfirmAndRunBatchAsync(fresh, new UpdateBatchStop(), null, CancellationToken.None);
        Assert.IsTrue(clockWentBack.Items.All(item => item.Disposition == UpdateBatchItemDisposition.ReviewExpired), "a clock that went backwards fails closed");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public async Task ATargetThatStartsRunningAfterReviewNeedsTheExistingAcknowledgement()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);
        harness.Running.Add("b");

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysAC, harness.Executor.Keys.ToArray());
        Assert.IsFalse(run.Items[1].Result.Started);
        StringAssert.Contains(run.Items[1].Result.Message, "appears to be running");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public async Task ATargetRunningAtReviewIsAcknowledgedByConfirmingTheSheet()
    {
        Harness harness = new();
        harness.Running.Add("a");
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        Assert.IsTrue(review.Items[0].Preparation.RunningApplication.AppearsToBeRunning);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysAB, harness.Executor.Keys.ToArray(), "the warning shown on the sheet was acknowledged by Update N apps anyway");
    }

    [TestMethod]
    [TestCategory("AC-UPD-09")]
    [TestCategory("FR-UPD-09")]
    public async Task EachItemHasItsOwnHonestOutcomeAndLaterItemsStillRunAfterAFailure()
    {
        Harness harness = new();
        harness.Executor.Statuses["b"] = UpdateProviderAttemptStatus.InstallerFailed;
        harness.Executor.Statuses["c"] = UpdateProviderAttemptStatus.ElevationDenied;
        harness.Inventory.NotDetected.Add("d");
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c", "d"], CancellationToken.None);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => key);

        CollectionAssert.AreEqual(KeysABCD, harness.Executor.Keys.ToArray());
        CollectionAssert.AreEqual(MixedOutcomes,
            completion.Items.Select(item => item.Outcome.Kind).ToArray(), "Verified only from a verified Success; a provider zero without the target version is not verified");
        Assert.IsFalse(completion.ReturnsToReview, "one declined UAC prompt doesn't send the whole batch back to review");
        CollectionAssert.AreEqual(KeysABCD, harness.History.Entries.Select(entry => entry.StableProductKey).Distinct().ToArray(), "history records each item");
        Assert.AreEqual(4, completion.Groups.Count);
    }

    [TestMethod]
    [TestCategory("AC-UPD-10")]
    public async Task AnUnexpectedFaultStopsTheRemainingItemsAndNeverReadsAsVerified()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);
        harness.Throwing.Add("b");
        List<UpdateBatchItemNotice> notices = [];

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), notice =>
        {
            notices.Add(notice);
            throw new InvalidOperationException("presentation callbacks cannot change results");
        }, CancellationToken.None);
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => key);

        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray());
        Assert.AreEqual(UpdateOutcomeKind.ResultNotVerified, completion.Items[1].Outcome.Kind);
        Assert.AreEqual(UpdateBatchItemDisposition.StoppedAfterEarlierProblem, run.Items[2].Disposition);
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, completion.Items[2].Outcome.Kind);
        CollectionAssert.AreEqual(NoticeOrder,
            notices.Select(notice => $"{notice.Index}:{(notice.Finished ? "end" : "start")}").ToArray());
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task TheNextItemWaitsForThePreviousItemsOwnedWorkInsteadOfBeingRefused()
    {
        Harness harness = new();
        harness.Executor.Lifetime = new UpdateExecutionLifetime();
        harness.Executor.Owned = key => key == "a" ? Task.Delay(TimeSpan.FromMilliseconds(100)) : null;
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysAB, harness.Executor.Keys.ToArray(), "b starts once a's owned work has settled");
        Assert.IsTrue(run.Items.All(item => item.Result.Batch is { AdmissionRefused: false }));
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task UnsettledOwnedWorkRefusesTheNextItemAndStopsTheRestFailClosed()
    {
        Harness harness = new(TimeSpan.FromMilliseconds(50));
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Executor.Lifetime = new UpdateExecutionLifetime();
        harness.Executor.Owned = key => key == "a" ? never.Task : null;
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);
        never.SetResult();

        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray());
        Assert.IsTrue(run.Items[1].Result.Batch!.AdmissionRefused, "the existing admission check still refuses while owned work is unresolved");
        Assert.AreEqual(UpdateBatchItemDisposition.StoppedAfterEarlierProblem, run.Items[2].Disposition);
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => key);
        Assert.AreEqual(UpdateOutcomeKind.Verified, completion.Items[0].Outcome.Kind);
        Assert.IsTrue(completion.Items.Skip(1).All(item => item.Outcome.Kind == UpdateOutcomeKind.NotStarted));
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("AC-UPD-01")]
    public async Task BatchReviewSharesOneFreshPreparationReportsProgressAndStartsTheCeilingAfterPreparation()
    {
        // Review M2: preparing a large batch is slow; it must not use up the 30-minute dispatch ceiling before the sheet appears.
        Harness harness = new(sharedPreparer: true);
        harness.OnShared = (_, _) => harness.Now += TimeSpan.FromMinutes(10);
        RecordingProgress progress = new();

        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], progress, CancellationToken.None);

        Assert.AreEqual(1, harness.SharedCalls, "one shared fresh observation for the whole review");
        Assert.IsEmpty(harness.PrepareCalls, "no per-app scan at review time");
        CollectionAssert.AreEqual(ProgressZeroToThree, progress.Values.Select(value => $"{value.Prepared}/{value.Total}").ToArray());
        Assert.AreEqual(Start + TimeSpan.FromMinutes(30), review.ReviewedAt, "the ceiling starts once preparation has finished");
        Assert.AreEqual(review.ReviewedAt + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge, review.ExpiresAt);
        Assert.HasCount(3, review.Items);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysABC, harness.Executor.Keys.ToArray(), "no app expired: preparation time didn't count against the ceiling");
        Assert.IsTrue(KeysABC.All(key => harness.PrepareCalls[key] == 1), "each app is still re-prepared on its own just before it starts");
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task CancellingBatchPreparationApprovesAndStartsNothing()
    {
        Harness harness = new(sharedPreparer: true);
        using CancellationTokenSource cancellation = new();
        harness.OnShared = (key, _) => { if (key == "b") cancellation.Cancel(); };

        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], null, cancellation.Token));

        Assert.IsEmpty(harness.Store.Jobs);
        Assert.IsEmpty(harness.Executor.Keys);
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task ASharedPreparerThatDoesNotAnswerEveryAppIsNotTrusted()
    {
        Harness harness = new(sharedPreparer: true) { SharedOverride = keys => [new UpdateSelectionPreparation(Selection(keys[0], "1.1.0"), "fresh")] };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None));

        harness.SharedOverride = keys => [new UpdateSelectionPreparation(Selection("b", "1.1.0"), "fresh"), new UpdateSelectionPreparation(Selection("a", "1.1.0"), "fresh")];
        UpdateBatchReviewPreparation swapped = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        Assert.IsEmpty(swapped.Items, "a prepared selection for another app is never reviewed under this app");
        Assert.HasCount(2, swapped.Excluded);
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("AC-UPD-01")]
    public async Task ASingleAppReviewThatExpiresDuringItsReprepareIsNeverApprovedAndSaysReviewAgain()
    {
        // Review L2: the ceiling is re-read after the dispatch re-preparation, immediately before approval.
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a"], CancellationToken.None);
        harness.OnPrepare = (_, call) => { if (call == 2) harness.Now = Start + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge + TimeSpan.FromSeconds(1); };

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        Assert.AreEqual(UpdateBatchItemDisposition.ReviewExpired, run.Items.Single().Disposition);
        Assert.IsTrue(run.Items.Single().Result.ReviewExpired);
        Assert.AreEqual(ReviewedUpdateExecutionWorkflow.SingleReviewExpiredMessage, run.Items.Single().Result.Message);
        Assert.AreEqual("This review is more than 30 minutes old. Review again to update.", ReviewedUpdateExecutionWorkflow.SingleReviewExpiredMessage);
        Assert.IsEmpty(harness.Store.Jobs, "an expired review is never approved");
        Assert.IsEmpty(harness.Executor.Keys);
        UpdateJobItemOutcome outcome = UpdateOutcomePresenter.ForBatch(run, key => key).Items.Single();
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, outcome.Outcome.Kind);
        StringAssert.Contains(outcome.Outcome.Summary, "This review is more than 30 minutes old. Review again to update.");
        Assert.IsFalse(outcome.Outcome.Summary.Contains("turn came", StringComparison.Ordinal), "a one-app job has no turn");
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task ABatchAppThatExpiresDuringItsOwnReprepareIsNotStartedAndLaterAppsAreAlsoExpired()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);
        harness.OnPrepare = (key, call) => { if (key == "b" && call == 2) harness.Now = Start + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge + TimeSpan.FromSeconds(1); };

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray());
        Assert.HasCount(1, harness.Store.Jobs, "b expired after its re-preparation and before its approval");
        Assert.AreEqual(UpdateBatchItemDisposition.ReviewExpired, run.Items[1].Disposition);
        Assert.AreEqual(ReviewedUpdateExecutionWorkflow.BatchReviewExpiredMessage, run.Items[1].Result.Message);
        Assert.AreEqual(UpdateBatchItemDisposition.ReviewExpired, run.Items[2].Disposition);
        StringAssert.Contains(UpdateOutcomePresenter.ForBatch(run, key => key).Items[1].Outcome.Summary, "when this app's turn came");
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    public async Task ConfirmAndRunWithAnExpiredReviewTimeApprovesNothing()
    {
        Harness harness = new();
        UpdateReviewPreparation review = await harness.Workflow.BeginReviewAsync("a", CancellationToken.None);

        ReviewedUpdateExecutionResult expired = await harness.Workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None, review.Progress,
            Start - ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge - TimeSpan.FromSeconds(1));

        Assert.IsFalse(expired.Started);
        Assert.IsTrue(expired.ReviewExpired);
        Assert.IsEmpty(harness.Store.Jobs);
        Assert.IsEmpty(harness.Executor.Keys);
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("AC-UPD-01")]
    public async Task AFrozenBatchReviewRunsOnceAndASecondRunIsRefusedBeforeAnyApproval()
    {
        // Review L6.
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);
        int jobs = harness.Store.Jobs.Count;
        Dictionary<string, int> prepares = new(harness.PrepareCalls);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => harness.Workflow.ConfirmAndRunBatchAsync(review with { }, new UpdateBatchStop(), null, CancellationToken.None));

        Assert.AreEqual(jobs, harness.Store.Jobs.Count, "nothing more was approved");
        CollectionAssert.AreEquivalent(prepares.ToArray(), harness.PrepareCalls.ToArray(), "nothing was re-prepared");
        CollectionAssert.AreEqual(KeysAB, harness.Executor.Keys.ToArray());
    }

    [TestMethod]
    [TestCategory("AC-UPD-09")]
    [TestCategory("FR-UPD-09")]
    public async Task ADeclinedPermissionPromptForTheFirstAppLetsTheLaterAppsRun()
    {
        Harness harness = new();
        harness.Executor.Statuses["a"] = UpdateProviderAttemptStatus.ElevationDenied;
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => key);

        CollectionAssert.AreEqual(KeysABC, harness.Executor.Keys.ToArray());
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, completion.Items[0].Outcome.Kind);
        Assert.IsTrue(completion.Items.Skip(1).All(item => item.Outcome.Kind == UpdateOutcomeKind.Verified));
        Assert.IsFalse(completion.ReturnsToReview);
    }

    [TestMethod]
    [TestCategory("FR-UPD-11")]
    [TestCategory("AC-UPD-10")]
    public async Task AStopRequestedDuringTheSettleWaitSkipsTheNextApp()
    {
        Harness harness = new();
        UpdateBatchStop stop = new();
        harness.Executor.Lifetime = new UpdateExecutionLifetime();
        // a's owned work settles only after the stop request, so the stop arrives while the batch waits between apps.
        harness.Executor.Owned = key => key == "a" ? Task.Run(async () => { await Task.Delay(100); stop.Request(); }) : null;
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, stop, null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray());
        Assert.IsTrue(run.StopRequested);
        Assert.IsTrue(run.Items.Skip(1).All(item => item.Disposition == UpdateBatchItemDisposition.StoppedBeforeStart));
        Assert.HasCount(1, harness.Store.Jobs);
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [TestCategory("FR-HIS-03")]
    public async Task TheSettleTimeoutHistoryEntryReadsAsNotStartedInActivityAndHome()
    {
        // Review L5: presentation only; the recorded ProductFailed entry is unchanged.
        Harness harness = new(TimeSpan.FromMilliseconds(50));
        TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Executor.Lifetime = new UpdateExecutionLifetime();
        harness.Executor.Owned = key => key == "a" ? never.Task : null;
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);

        await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);
        never.SetResult();

        UpdateHistoryEntry refused = harness.History.Entries.Last(entry => entry.StableProductKey == "b");
        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, refused.Status, "the record is unchanged");
        Assert.AreEqual(UpdateOutcomePresenter.AdmissionRefusedDiagnostic, refused.Diagnostic);
        UpdateJobRecord job = UpdateJobHistory.Group(harness.History.Entries).Single(record => record.StableProductKey == "b");
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, job.Outcome.Kind);
        Assert.AreEqual(UpdateOutcomePresenter.NotStartedLabel, job.Outcome.Label);
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(job, "1.0.0"), "Home doesn't list a not-started app as a problem");
        StringAssert.StartsWith(UpdateJobHistory.FailureClass(refused.Status, refused.Diagnostic), "Not started");
        Assert.AreEqual(UpdateOutcomeKind.Failed, UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.ProductFailed, "other", null, "1.1.0").Kind, "other product failures stay Failed");
        Assert.AreEqual("PC Steward could not run the update", UpdateJobHistory.FailureClass(UpdateHistoryStatus.ProductFailed, "other"));
    }

    [TestMethod]
    [TestCategory("FR-UPD-09")]
    [TestCategory("AC-UPD-07")]
    [TestCategory("AC-UPD-08")]
    [Description("Task 51: a typed provider restart reads as Restart required, with or without the target version detected, and never as Verified; a zero exit without the target stays not verified.")]
    public async Task ATypedProviderRestartIsRecordedAsRestartRequiredNeverVerified()
    {
        Harness harness = new();
        harness.Executor.Statuses["a"] = UpdateProviderAttemptStatus.RestartReported;
        harness.Executor.Statuses["b"] = UpdateProviderAttemptStatus.RestartReported;
        harness.Inventory.NotDetected.Add("b");
        harness.Executor.Statuses["c"] = UpdateProviderAttemptStatus.RestartReported;
        harness.Inventory.Failing.Add("c");
        harness.Inventory.NotDetected.Add("d");
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c", "d"], CancellationToken.None);

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        UpdateHistoryEntry[] finals = [.. run.Items.Select(item => item.Result.Batch!.FinalEntries.Single())];
        CollectionAssert.AreEqual(RestartRestartRestartInconclusive, finals.Select(entry => entry.Status).ToArray());
        StringAssert.StartsWith(finals[0].Diagnostic, "provider-reported-restart-required; target-version-detected-by-post-update-inventory");
        StringAssert.StartsWith(finals[1].Diagnostic, "provider-reported-restart-required; target-version-not-yet-detected");
        StringAssert.StartsWith(finals[2].Diagnostic, "provider-reported-restart-required; post-update-inventory-did-not-finish");
        Assert.AreEqual(unchecked((int)0x8A150109), finals[0].ExitCode, "the provider's exit code is recorded (AC-UPD-11)");
        UpdateJobCompletion completion = UpdateOutcomePresenter.ForBatch(run, key => key);
        CollectionAssert.AreEqual(RestartRestartRestartNotVerified, completion.Items.Select(item => item.Outcome.Kind).ToArray());
        Assert.IsTrue(completion.RestartRequired);
        UpdateJobRecord restart = UpdateJobHistory.Group(harness.History.Entries).Single(job => job.StableProductKey == "a");
        Assert.AreEqual(UpdateOutcomeKind.RestartRequired, restart.Outcome.Kind);
        Assert.AreEqual("None — restart pending", UpdateJobHistory.FailureClass(restart.Final.Status));
    }

    [TestMethod]
    [TestCategory("FR-UPD-09")]
    [TestCategory("AC-UPD-07")]
    [Description("Task 51: a restart needed before install applied nothing: it is Failed with a restart explanation, never Restart required.")]
    public void ARestartNeededBeforeInstallIsAFailureWithARestartExplanation()
    {
        UpdateOutcomePresentation before = UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.InstallerFailed,
            $"{UpdateOutcomePresenter.RestartBeforeInstallDiagnostic}; output", unchecked((int)0x8A15010A), "1.1.0");
        Assert.AreEqual(UpdateOutcomeKind.Failed, before.Kind);
        StringAssert.Contains(before.Summary, "needs Windows to restart before it can install");
        StringAssert.Contains(before.Summary, "nothing was updated");
        UpdateOutcomePresentation other = UpdateOutcomePresenter.ForStatus(UpdateHistoryStatus.InstallerFailed, "restart text from output", 1, "1.1.0");
        Assert.AreEqual("The installer reported an error (exit code 1).", other.Summary, "untyped text never changes the explanation");
    }

    [TestMethod]
    [TestCategory("FR-UPD-05")]
    [TestCategory("FR-UPD-06")]
    [Description("Task 52: an acknowledgement covers only the processes the owner was warned about; a new process at the item's turn is a new warning.")]
    public async Task ANewRunningProcessAtTheItemsTurnNeedsANewAcknowledgement()
    {
        Harness harness = new();
        harness.Running.Add("a");
        harness.Running.Add("b");
        harness.Detector.Processes["a"] = ["contoso-a"];
        harness.Detector.Processes["b"] = ["contoso-b"];
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        harness.Detector.Processes["a"] = ["CONTOSO-A"];
        harness.Detector.Processes["b"] = ["contoso-b", "contoso-b-updater"];

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray(), "the same warned process (any case) is covered; a new one is not");
        Assert.IsFalse(run.Items[1].Result.Started);
        StringAssert.Contains(run.Items[1].Result.Message, "contoso-b-updater");
        StringAssert.Contains(run.Items[1].Result.Message, "wasn't part of the warning you confirmed");
        Assert.HasCount(1, harness.Store.Jobs, "the item with a new warning is never approved");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    [Description("Task 52: the coordinator's last check before the provider applies the same binding as the item's own check.")]
    public async Task TheCoordinatorsLastCheckAlsoNeedsTheWarnedProcesses()
    {
        History history = new();
        RunningDetector detector = new() { Running = ["a"] };
        detector.Processes["a"] = ["contoso-a", "contoso-a-helper"];
        RecordingExecutor executor = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), history, detector);
        ImmutableUpdateJob job = UpdateJobApprovalService.BeginReview(Guid.NewGuid(), Guid.NewGuid(), Selection("a", "1.1.0")).Confirm(Start);
        UpdateJobExecutionEvidence evidence = new(job.Selection, true, true);

        UpdateBatchResult refused = await coordinator.RunAsync([new UpdateBatchItem(job, evidence, true, null, ["contoso-a"])], CancellationToken.None);
        Assert.IsNotNull(refused.AcknowledgementRequired);
        Assert.IsEmpty(executor.Keys);

        ImmutableUpdateJob second = UpdateJobApprovalService.BeginReview(Guid.NewGuid(), Guid.NewGuid(), Selection("a", "1.1.0")).Confirm(Start);
        UpdateBatchResult covered = await coordinator.RunAsync([new UpdateBatchItem(second, new(second.Selection, true, true), true, null, ["contoso-a", "Contoso-A-Helper"])], CancellationToken.None);
        Assert.IsNull(covered.AcknowledgementRequired);
        CollectionAssert.AreEqual(KeysA, executor.Keys.ToArray());

        Assert.IsTrue(RunningApplicationAcknowledgement.Covers(false, null, RunningApplicationDetection.NotDetected), "nothing running needs no acknowledgement");
        Assert.IsFalse(RunningApplicationAcknowledgement.Covers(true, [], new RunningApplicationDetection(true, ["x"])), "an empty warned list covers nothing");
        Assert.IsTrue(RunningApplicationAcknowledgement.Covers(true, null, new RunningApplicationDetection(true, ["x"])), "without a recorded list the earlier contract applies");
    }

    [TestMethod]
    [TestCategory("FR-UPD-11")]
    [TestCategory("AC-UPD-10")]
    [Description("Task 52: cancellation during the next item's re-preparation, its running-app check or between items starts and approves nothing for it, and never reads as a possible run.")]
    [DataRow("prepare")]
    [DataRow("detect")]
    [DataRow("between")]
    public async Task CancellationBeforeTheProviderStartsNothingAndSaysSo(string boundary)
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b"], CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        switch (boundary)
        {
            case "prepare": harness.OnPrepare = (key, call) => { if (key == "b" && call == 2) cancellation.Cancel(); }; break;
            case "detect": harness.Detector.OnDetect = key => { if (key == "b") cancellation.Cancel(); }; break;
            default: harness.Executor.OnExecute = (key, _) => { if (key == "a") cancellation.Cancel(); }; break;
        }

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, cancellation.Token);

        CollectionAssert.AreEqual(KeysA, harness.Executor.Keys.ToArray(), "b never reached the provider");
        Assert.AreEqual(UpdateBatchItemDisposition.StoppedBeforeStart, run.Items[1].Disposition);
        Assert.IsFalse(run.Items[1].Result.Started);
        Assert.AreEqual(UpdateOutcomeKind.NotStarted, UpdateOutcomePresenter.ForBatch(run, key => key).Items[1].Outcome.Kind);
        Assert.HasCount(1, harness.Store.Jobs, "b was never approved");
        Assert.IsFalse(harness.History.Entries.Any(entry => entry.StableProductKey == "b"), "nothing about b was recorded as run");
    }

    [TestMethod]
    [TestCategory("FR-UPD-10")]
    [Description("ADR-025 / Task 52: the 30-minute ceiling is inclusive; one tick over needs a fresh review. A four-minute first installer doesn't expire the next app, whose approval is made just in time.")]
    public async Task TheDispatchCeilingIsExactAndASlowFirstInstallerDoesNotExpireTheNextApp()
    {
        Harness harness = new();
        UpdateBatchReviewPreparation review = await harness.Workflow.BeginBatchReviewAsync(["a", "b", "c"], CancellationToken.None);
        harness.Executor.OnExecute = (key, _) => harness.Now = key switch
        {
            "a" => Start + TimeSpan.FromMinutes(4),
            "b" => Start + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge + TimeSpan.FromTicks(1),
            _ => harness.Now
        };
        harness.OnPrepare = (key, call) => { if (key == "b" && call == 2) harness.Now = Start + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge; };

        UpdateBatchRunResult run = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);

        CollectionAssert.AreEqual(KeysAB, harness.Executor.Keys.ToArray(), "b dispatched at exactly 30 minutes; c was one tick over");
        Assert.AreEqual(Start, harness.Store.Jobs[0].ApprovedAt);
        Assert.AreEqual(Start + ReviewedUpdateExecutionWorkflow.MaximumBatchDispatchAge, harness.Store.Jobs[1].ApprovedAt,
            "b was approved at its own turn, after a's four-minute installer; nothing was approved ahead of time");
        Assert.AreEqual(UpdateBatchItemDisposition.ReviewExpired, run.Items[2].Disposition);
        Assert.HasCount(2, harness.Store.Jobs, "the expired item is never approved or renewed");
    }

    private static readonly string[] ProgressZeroToThree = ["0/3", "1/3", "2/3", "3/3"];

    private sealed class RecordingProgress : IProgress<UpdateBatchReviewProgress>
    {
        public List<UpdateBatchReviewProgress> Values { get; } = [];
        public void Report(UpdateBatchReviewProgress value) => Values.Add(value);
    }

    private sealed class Harness
    {
        public Harness(TimeSpan? settleLimit = null, bool sharedPreparer = false)
        {
            Workflow = new ReviewedUpdateExecutionWorkflow(Prepare, new UpdateJobApprovalService(Store),
                new SequentialUpdateBatchCoordinator(Executor, Inventory, History, Detector), Detector, () => Now,
                sharedPreparer ? PrepareShared : null, () => "S-1-5-21-11-22-33-1001")
            {
                OwnedWorkSettleLimit = settleLimit ?? TimeSpan.FromSeconds(30)
            };
            Detector.Running = Running;
        }

        /// <summary>Calls of the shared (one observation) batch preparer, and its per-key hook.</summary>
        public int SharedCalls { get; private set; }
        public Action<string, CancellationToken>? OnShared { get; set; }
        public Func<IReadOnlyList<string>, IReadOnlyList<UpdateSelectionPreparation>>? SharedOverride { get; set; }
        /// <summary>Runs on every single-key preparation (review fallback and every dispatch re-preparation).</summary>
        public Action<string, int>? OnPrepare { get; set; }

        private async Task<IReadOnlyList<UpdateSelectionPreparation>> PrepareShared(IReadOnlyList<string> keys, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            SharedCalls++;
            await Task.Yield();
            if (SharedOverride is not null) return SharedOverride(keys);
            List<UpdateSelectionPreparation> prepared = [];
            foreach (string key in keys)
            {
                OnShared?.Invoke(key, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                prepared.Add(Unavailable.Contains(key)
                    ? new UpdateSelectionPreparation(null, "The update evidence is no longer fresh.")
                    : new UpdateSelectionPreparation(Selection(key, Candidates.GetValueOrDefault(key, "1.1.0")), "fresh"));
                progress?.Report(prepared.Count);
            }
            return prepared;
        }

        public DateTimeOffset Now { get; set; } = Start;
        public HashSet<string> Unavailable { get; } = [];
        public HashSet<string> Throwing { get; } = [];
        public HashSet<string> Running { get; } = [];
        public Dictionary<string, string> Candidates { get; } = [];
        public Dictionary<string, int> PrepareCalls { get; } = [];
        public RecordingStore Store { get; } = new();
        public RecordingExecutor Executor { get; } = new();
        public Inventory Inventory { get; } = new();
        public History History { get; } = new();
        public RunningDetector Detector { get; } = new();
        public ReviewedUpdateExecutionWorkflow Workflow { get; }

        private Task<UpdateSelectionPreparation> Prepare(string key, CancellationToken cancellationToken)
        {
            PrepareCalls[key] = PrepareCalls.GetValueOrDefault(key) + 1;
            OnPrepare?.Invoke(key, PrepareCalls[key]);
            cancellationToken.ThrowIfCancellationRequested();
            if (Throwing.Contains(key)) throw new InvalidOperationException("unexpected");
            if (Unavailable.Contains(key)) return Task.FromResult(new UpdateSelectionPreparation(null, "The update evidence is no longer fresh."));
            return Task.FromResult(new UpdateSelectionPreparation(Selection(key, Candidates.GetValueOrDefault(key, "1.1.0")), "fresh"));
        }
    }

    private static UpdateJobSelection Selection(string key, string candidate) =>
        new(key, UpdateProvider.WinGet, UpdateSource.WinGet, $"Contoso.{key.ToUpperInvariant()}", "1.0.0", candidate, UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "source-match-v1");

    private sealed class RecordingStore : IUpdateJobStore
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Jobs.Add(job);
            return Task.CompletedTask;
        }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<string> Keys { get; } = [];
        public Dictionary<string, UpdateProviderAttemptStatus> Statuses { get; } = [];
        public Action<string, CancellationToken>? OnExecute { get; set; }
        public UpdateExecutionLifetime? Lifetime { get; set; }
        public Func<string, Task?>? Owned { get; set; }
        public UpdateExecutionLifetime? ExecutionLifetime => Lifetime;
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken)
        {
            string key = job.Selection.StableProductKey;
            Keys.Add(key);
            OnExecute?.Invoke(key, cancellationToken);
            UpdateProviderAttemptStatus status = Statuses.GetValueOrDefault(key, UpdateProviderAttemptStatus.AwaitingVerification);
            int exit = status switch
            {
                UpdateProviderAttemptStatus.AwaitingVerification => 0,
                UpdateProviderAttemptStatus.RestartReported => unchecked((int)0x8A150109),
                _ => 1
            };
            return Task.FromResult(new UpdateProviderAttempt(status, exit, "provider", Owned?.Invoke(key)));
        }
    }

    private sealed class Inventory : IPostUpdateInventory
    {
        public HashSet<string> NotDetected { get; } = [];
        public HashSet<string> Failing { get; } = [];
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            Failing.Contains(job.Selection.StableProductKey)
                ? throw new InvalidOperationException("inventory unavailable")
                : Task.FromResult(new PostUpdateInventoryResult(!NotDetected.Contains(job.Selection.StableProductKey), false));
    }

    private sealed class History : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RunningDetector : IRunningApplicationDetector
    {
        public HashSet<string> Running { get; set; } = [];
        /// <summary>Process names reported for a running key; by default the package ID.</summary>
        public Dictionary<string, string[]> Processes { get; } = [];
        public Action<string>? OnDetect { get; set; }
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
        {
            OnDetect?.Invoke(selection.StableProductKey);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Running.Contains(selection.StableProductKey)
                ? new RunningApplicationDetection(true, Processes.GetValueOrDefault(selection.StableProductKey) ?? [selection.PackageId])
                : RunningApplicationDetection.NotDetected);
        }
    }
}
