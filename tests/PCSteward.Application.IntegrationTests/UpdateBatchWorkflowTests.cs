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

        harness.Now = Start.AddMinutes(-1);
        UpdateBatchRunResult clockWentBack = await harness.Workflow.ConfirmAndRunBatchAsync(review, new UpdateBatchStop(), null, CancellationToken.None);
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

    private sealed class Harness
    {
        public Harness()
        {
            Workflow = new ReviewedUpdateExecutionWorkflow(Prepare, new UpdateJobApprovalService(Store),
                new SequentialUpdateBatchCoordinator(Executor, Inventory, History, Detector), Detector, () => Now);
            Detector.Running = Running;
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
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<string> Keys { get; } = [];
        public Dictionary<string, UpdateProviderAttemptStatus> Statuses { get; } = [];
        public Action<string, CancellationToken>? OnExecute { get; set; }
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken)
        {
            string key = job.Selection.StableProductKey;
            Keys.Add(key);
            OnExecute?.Invoke(key, cancellationToken);
            UpdateProviderAttemptStatus status = Statuses.GetValueOrDefault(key, UpdateProviderAttemptStatus.AwaitingVerification);
            return Task.FromResult(new UpdateProviderAttempt(status, status == UpdateProviderAttemptStatus.AwaitingVerification ? 0 : 1, "provider"));
        }
    }

    private sealed class Inventory : IPostUpdateInventory
    {
        public HashSet<string> NotDetected { get; } = [];
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            Task.FromResult(new PostUpdateInventoryResult(!NotDetected.Contains(job.Selection.StableProductKey), false));
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
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(Running.Contains(selection.StableProductKey) ? new RunningApplicationDetection(true, [selection.PackageId]) : RunningApplicationDetection.NotDetected);
    }
}
