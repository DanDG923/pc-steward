using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ReviewedUpdateExecutionWorkflowTests
{
    [TestMethod]
    public async Task ChangedEvidenceAfterReviewDoesNotCreateAJobOrRunTheProvider()
    {
        int calls = 0;
        RecordingStore store = new();
        RecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = CreateWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(Selection(++calls == 1 ? "1.1.0" : "1.2.0"), "fresh")), store, executor);

        UpdateReviewPreparation review = await workflow.BeginReviewAsync("winget:contoso.widget", CancellationToken.None);
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None);

        Assert.IsFalse(result.Started);
        Assert.HasCount(0, store.Jobs);
        Assert.HasCount(0, executor.Jobs);
        StringAssert.Contains(result.Message, "evidence changed");
    }

    [TestMethod]
    public async Task MatchingFreshEvidenceCreatesAnImmutableJobAndStillVerifiesBeforeSuccess()
    {
        RecordingStore store = new();
        RecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = CreateWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(Selection(), "fresh")), store, executor);

        UpdateReviewPreparation review = await workflow.BeginReviewAsync("winget:contoso.widget", CancellationToken.None);
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None);

        Assert.IsTrue(result.Started);
        Assert.HasCount(1, store.Jobs);
        Assert.HasCount(1, executor.Jobs);
        Assert.AreEqual(UpdateHistoryStatus.Success, result.Batch!.FinalEntries.Single().Status);
    }

    [TestMethod]
    public async Task RunningTargetIsVisibleBeforeConfirmationAndRequiresExplicitAcknowledgement()
    {
        RecordingStore store = new();
        RecordingExecutor executor = new();
        RunningDetector running = new(new RunningApplicationDetection(true, ["Widget"]));
        ReviewedUpdateExecutionWorkflow workflow = CreateWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(Selection(), "fresh")), store, executor, running);

        UpdateReviewPreparation review = await workflow.BeginReviewAsync("winget:contoso.widget", CancellationToken.None);
        ReviewedUpdateExecutionResult withoutAcknowledgement = await workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None);
        ReviewedUpdateExecutionResult acknowledged = await workflow.ConfirmAndRunAsync(review.Review!, true, CancellationToken.None);

        Assert.IsTrue(review.RunningApplication.AppearsToBeRunning);
        CollectionAssert.Contains(review.RunningApplication.ProcessNames.ToArray(), "Widget");
        Assert.IsFalse(withoutAcknowledgement.Started);
        StringAssert.Contains(withoutAcknowledgement.Message, "appears to be running");
        Assert.IsTrue(acknowledged.Started);
        Assert.HasCount(1, store.Jobs);
        Assert.HasCount(1, executor.Jobs);
    }

    [TestMethod]
    public async Task TargetStartingAfterReviewStopsBeforeApprovalUntilWarningIsAcknowledged()
    {
        RecordingStore store = new();
        RecordingExecutor executor = new();
        SequenceRunningDetector running = new(
            RunningApplicationDetection.NotDetected,
            new RunningApplicationDetection(true, ["Widget"]));
        ReviewedUpdateExecutionWorkflow workflow = CreateWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(Selection(), "fresh")), store, executor, running);

        UpdateReviewPreparation review = await workflow.BeginReviewAsync("winget:contoso.widget", CancellationToken.None);
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None);

        Assert.IsFalse(review.RunningApplication.AppearsToBeRunning);
        Assert.IsFalse(result.Started);
        Assert.HasCount(0, store.Jobs);
        Assert.HasCount(0, executor.Jobs);
    }

    [TestMethod]
    public async Task TargetFirstDetectedAtFinalPreProviderSnapshotRequiresANewTargetSpecificReview()
    {
        RecordingStore store = new();
        RecordingExecutor executor = new();
        SequenceRunningDetector running = new(
            RunningApplicationDetection.NotDetected,
            RunningApplicationDetection.NotDetected,
            new RunningApplicationDetection(true, ["Widget"]),
            new RunningApplicationDetection(true, ["Widget"]),
            new RunningApplicationDetection(true, ["Widget"]),
            new RunningApplicationDetection(true, ["Widget"]));
        ReviewedUpdateExecutionWorkflow workflow = CreateWorkflow((_, _) => Task.FromResult(new UpdateSelectionPreparation(Selection(), "fresh")), store, executor, running);

        UpdateReviewPreparation firstReview = await workflow.BeginReviewAsync("winget:contoso.widget", CancellationToken.None);
        ReviewedUpdateExecutionResult stopped = await workflow.ConfirmAndRunAsync(firstReview.Review!, false, CancellationToken.None);

        Assert.IsFalse(firstReview.RunningApplication.AppearsToBeRunning);
        Assert.IsFalse(stopped.Started);
        StringAssert.Contains(stopped.Message, "started running after confirmation");
        StringAssert.Contains(stopped.Message, "Widget");
        Assert.HasCount(0, executor.Jobs);

        UpdateReviewPreparation newReview = await workflow.BeginReviewAsync("winget:contoso.widget", CancellationToken.None);
        Assert.IsTrue(newReview.RunningApplication.AppearsToBeRunning);
        ReviewedUpdateExecutionResult acknowledged = await workflow.ConfirmAndRunAsync(newReview.Review!, true, CancellationToken.None);

        Assert.IsTrue(acknowledged.Started);
        Assert.HasCount(1, executor.Jobs);
        Assert.HasCount(2, store.Jobs);
    }

    private static ReviewedUpdateExecutionWorkflow CreateWorkflow(
        Func<string, CancellationToken, Task<UpdateSelectionPreparation>> prepare,
        RecordingStore store,
        RecordingExecutor executor,
        IRunningApplicationDetector? detector = null)
    {
        IRunningApplicationDetector running = detector ?? new RunningDetector(RunningApplicationDetection.NotDetected);
        SequentialUpdateBatchCoordinator coordinator = new(executor, new VerifiedInventory(), new InMemoryHistory(), running);
        return new ReviewedUpdateExecutionWorkflow(prepare, new UpdateJobApprovalService(store), coordinator, running);
    }

    private static UpdateJobSelection Selection(string candidate = "1.1.0") => new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", candidate, UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "source-match-v1");

    private sealed class RecordingStore : IUpdateJobStore
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { Jobs.Add(job); return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<ImmutableUpdateJob?>(Jobs.SingleOrDefault(job => job.Id == jobId));
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<Guid> Jobs { get; } = [];
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) { Jobs.Add(job.Id); return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed")); }
    }

    private sealed class VerifiedInventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(new PostUpdateInventoryResult(true, false));
    }

    private sealed class InMemoryHistory : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RunningDetector(RunningApplicationDetection result) : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class SequenceRunningDetector(params RunningApplicationDetection[] results) : IRunningApplicationDetector
    {
        private readonly Queue<RunningApplicationDetection> results = new(results);
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) => Task.FromResult(results.Dequeue());
    }
}
