using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class SequentialUpdateBatchTests
{
    private static readonly string[] ExpectedDetectedProcessNames = ["firefox"];

    [TestMethod]
    [TestCategory("FR-UPD-14")]
    public async Task UnresolvedOwnedCompletionRefusesTheNextUpdateBeforeAnyQueuedRow()
    {
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        InMemoryHistory history = new();
        LifetimeExecutor executor = new(pending.Task);
        SequentialUpdateBatchCoordinator coordinator = new(executor, new ThrowingInventory(), history, new RunningDetector(false));
        ImmutableUpdateJob first = Job("Contoso.Uncertain", "1.1.0");
        ImmutableUpdateJob second = Job("Contoso.Blocked", "1.1.0");
        UpdateBatchResult initial = await coordinator.RunAsync([Item(first)], CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, initial.FinalEntries.Single().Status);

        UpdateBatchResult blocked = await coordinator.RunAsync([Item(second)], CancellationToken.None);
        Assert.IsTrue(blocked.AdmissionRefused);
        Assert.HasCount(1, executor.Jobs);
        Assert.IsFalse(history.Entries.Any(entry => entry.JobId == second.Id && entry.Status == UpdateHistoryStatus.Queued));
        pending.SetResult();
        UpdateBatchResult resumed = await coordinator.RunAsync([Item(Job("Contoso.Next", "1.1.0"))], CancellationToken.None);
        Assert.IsFalse(resumed.AdmissionRefused);
        Assert.HasCount(2, executor.Jobs);
    }

    [TestMethod]
    [TestCategory("FR-UPD-14")]
    public async Task DispatchFailureDoesNotRecordAnObservedProviderStart()
    {
        InMemoryHistory history = new();
        ImmutableUpdateJob job = Job("Contoso.NeverStarted", "1.1.0");
        SequentialUpdateBatchCoordinator coordinator = new(
            new FailingAttemptExecutor(UpdateProviderAttemptStatus.ProductFailed),
            new ThrowingInventory(), history, new RunningDetector(false));

        UpdateBatchResult result = await coordinator.RunAsync([Item(job)], CancellationToken.None);

        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, result.FinalEntries.Single().Status);
        Assert.IsTrue(history.Entries.Any(entry => entry.Status == UpdateHistoryStatus.Running));
        Assert.IsFalse(history.Entries.Any(entry => entry.Diagnostic == "provider-execution-started"),
            "FR-UPD-14: dispatch intent cannot claim an observed provider start, especially when dispatch fails.");
    }

    [TestMethod]
    public async Task VerificationIsTheOnlyPathToSuccessAndBatchRemainsSequential()
    {
        InMemoryHistory history = new();
        RecordingExecutor executor = new();
        Dictionary<Guid, PostUpdateInventoryResult> verification = new();
        ImmutableUpdateJob success = Job("Contoso.Success", "1.1.0");
        ImmutableUpdateJob restart = Job("Contoso.Restart", "2.0.0");
        ImmutableUpdateJob inconclusive = Job("Contoso.Inconclusive", "3.0.0");
        verification[success.Id] = new(true, false);
        verification[restart.Id] = new(false, true);
        verification[inconclusive.Id] = new(false, false);
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(verification), history, new RunningDetector(false));

        UpdateBatchResult result = await coordinator.RunAsync([Item(success), Item(restart), Item(inconclusive)], CancellationToken.None);

        CollectionAssert.AreEqual(new[] { success.Id, restart.Id, inconclusive.Id }, executor.Jobs);
        CollectionAssert.AreEquivalent(new[] { UpdateHistoryStatus.Success, UpdateHistoryStatus.RestartRequired, UpdateHistoryStatus.Inconclusive }, result.FinalEntries.Select(entry => entry.Status).ToArray());
        Assert.IsFalse(result.FinalEntries.Any(entry => entry.Status == UpdateHistoryStatus.Success && entry.JobId != success.Id));
    }

    [TestMethod]
    public async Task CancellationStopsOnlyUnstartedItemsAndRunningWarningNeverExposesAForceCloseAction()
    {
        using CancellationTokenSource cancellation = new();
        InMemoryHistory history = new();
        ImmutableUpdateJob first = Job("Contoso.First", "1.1.0");
        ImmutableUpdateJob second = Job("Contoso.Second", "2.0.0");
        RecordingExecutor executor = new(() => cancellation.Cancel());
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(new() { [first.Id] = new(true, false) }), history, new RunningDetector(true));

        UpdateBatchResult result = await coordinator.RunAsync([Item(first, runningAcknowledged: true), Item(second, runningAcknowledged: true)], cancellation.Token);

        Assert.HasCount(1, executor.Jobs);
        Assert.AreEqual(first.Id, executor.Jobs.Single());
        CollectionAssert.AreEquivalent(new[] { UpdateHistoryStatus.Success, UpdateHistoryStatus.Cancelled }, result.FinalEntries.Select(entry => entry.Status).ToArray());
        Assert.IsTrue(history.Entries.Any(entry => entry.Status == UpdateHistoryStatus.RunningApplicationWarning));
        Assert.IsFalse(typeof(IRunningApplicationDetector).GetMethods().Any(method => method.Name.Contains("close", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task NewlyDetectedRunningTargetStopsBeforeQueueAndProviderUntilAcknowledged()
    {
        InMemoryHistory history = new();
        RecordingExecutor executor = new();
        ImmutableUpdateJob job = Job("Contoso.Widget", "1.1.0");
        SequentialUpdateBatchCoordinator coordinator = new(executor, new ThrowingInventory(), history, new RunningDetector(true));

        UpdateBatchResult result = await coordinator.RunAsync([Item(job)], CancellationToken.None);

        Assert.IsNotNull(result.AcknowledgementRequired);
        CollectionAssert.Contains(result.AcknowledgementRequired.ProcessNames.ToArray(), "fixture");
        Assert.HasCount(0, executor.Jobs);
        Assert.IsFalse(history.Entries.Any(entry => entry.Status is UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running));
        Assert.IsTrue(history.Entries.Any(entry => entry.Status == UpdateHistoryStatus.RunningApplicationWarning));
    }

    [TestMethod]
    public async Task ProductionRunningApplicationDetectorFindsARepresentativeBoundedPackageSuffix()
    {
        ImmutableUpdateJob job = Job("Mozilla.Firefox", "1.1.0");
        PackageDerivedRunningApplicationDetector detector = new(() => ["firefox", "unrelated"]);

        RunningApplicationDetection detected = await detector.DetectAsync(job.Selection, CancellationToken.None);

        Assert.IsTrue(detected.AppearsToBeRunning);
        CollectionAssert.AreEqual(ExpectedDetectedProcessNames, detected.ProcessNames.ToArray());
    }

    [TestMethod]
    public async Task AmbiguousGenericSuffixAndExitedProcessSnapshotDoNotCreateAWarning()
    {
        PackageDerivedRunningApplicationDetector ambiguous = new(() => ["App"]);
        PackageDerivedRunningApplicationDetector exited = new(() => []);

        RunningApplicationDetection ambiguousResult = await ambiguous.DetectAsync(Job("Contoso.App", "1.1.0").Selection, CancellationToken.None);
        RunningApplicationDetection exitedResult = await exited.DetectAsync(Job("Contoso.Widget", "1.1.0").Selection, CancellationToken.None);

        Assert.IsFalse(ambiguousResult.AppearsToBeRunning);
        Assert.IsFalse(exitedResult.AppearsToBeRunning);
        Assert.IsFalse(typeof(IRunningApplicationDetector).GetMethods().Any(method => method.Name.Contains("close", StringComparison.OrdinalIgnoreCase) || method.Name.Contains("kill", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task VerificationFailureAndDiagnosticsRemainDistinctAndSanitized()
    {
        InMemoryHistory history = new();
        ImmutableUpdateJob job = Job("Contoso.Widget", "1.1.0");
        SequentialUpdateBatchCoordinator coordinator = new(new RecordingExecutor(), new ThrowingInventory(), history, new RunningDetector(false));

        UpdateBatchResult result = await coordinator.RunAsync([Item(job)], CancellationToken.None);
        string bundle = SanitizedDiagnosticBundle.Create(result.Entries.Concat([new UpdateHistoryEntry(Guid.NewGuid(), job.Id, job.CorrelationId, job.Selection.StableProductKey, UpdateProvider.WinGet, job.Selection.PackageId, "1", "2", DateTimeOffset.UtcNow, UpdateHistoryStatus.SourceFailed, 1, "token=secret C:\\Users\\Alice\\x")]).ToArray());

        Assert.AreEqual(UpdateHistoryStatus.VerificationFailed, result.FinalEntries.Single().Status);
        StringAssert.Contains(bundle, "token=[redacted]");
        StringAssert.Contains(bundle, "[path]");
        Assert.IsFalse(bundle.Contains("secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancellationAfterProviderCompletionRemainsVerificationRequired()
    {
        using CancellationTokenSource cancellation = new();
        ImmutableUpdateJob job = Job("Contoso.Interrupted", "1.1.0");
        SequentialUpdateBatchCoordinator coordinator = new(new RecordingExecutor(), new CancellingInventory(cancellation), new InMemoryHistory(), new RunningDetector(false));

        UpdateBatchResult result = await coordinator.RunAsync([Item(job)], cancellation.Token);

        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, result.FinalEntries.Single().Status);
        Assert.AreNotEqual(UpdateHistoryStatus.Cancelled, result.FinalEntries.Single().Status);
    }

    [TestMethod]
    [DataRow(UpdateProviderAttemptStatus.SourceFailed, UpdateHistoryStatus.SourceFailed)]
    [DataRow(UpdateProviderAttemptStatus.DownloadFailed, UpdateHistoryStatus.DownloadFailed)]
    [DataRow(UpdateProviderAttemptStatus.InstallerFailed, UpdateHistoryStatus.InstallerFailed)]
    [DataRow(UpdateProviderAttemptStatus.ElevationDenied, UpdateHistoryStatus.ElevationDenied)]
    [DataRow(UpdateProviderAttemptStatus.TimedOut, UpdateHistoryStatus.TimedOut)]
    [DataRow(UpdateProviderAttemptStatus.ProductFailed, UpdateHistoryStatus.ProductFailed)]
    public async Task NegativeProviderResultsRetainTheirFailureCategory(UpdateProviderAttemptStatus attemptStatus, UpdateHistoryStatus expectedStatus)
    {
        InMemoryHistory history = new();
        ImmutableUpdateJob job = Job("Contoso.Negative", "1.1.0");
        SequentialUpdateBatchCoordinator coordinator = new(new FailingAttemptExecutor(attemptStatus), new ThrowingInventory(), history, new RunningDetector(false));

        UpdateBatchResult result = await coordinator.RunAsync([Item(job)], CancellationToken.None);

        Assert.AreEqual(expectedStatus, result.FinalEntries.Single().Status);
    }

    private static UpdateBatchItem Item(ImmutableUpdateJob job, bool runningAcknowledged = false) => new(job, new UpdateJobExecutionEvidence(job.Selection, true, true), runningAcknowledged);
    private static ImmutableUpdateJob Job(string packageId, string candidate) => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection($"winget:{packageId.ToLowerInvariant()}", UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "1.0.0", candidate, UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(DateTimeOffset.UtcNow);

    private sealed class RecordingExecutor(Action? afterExecution = null) : IApprovedUpdateExecutor
    {
        public List<Guid> Jobs { get; } = [];
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) { Jobs.Add(job.Id); afterExecution?.Invoke(); return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "provider-completed")); }
    }
    private sealed class LifetimeExecutor(Task completion) : IApprovedUpdateExecutor
    {
        public UpdateExecutionLifetime ExecutionLifetime { get; } = new();
        internal List<Guid> Jobs { get; } = [];
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken)
        {
            Jobs.Add(job.Id);
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.Cancelled, null,
                "synthetic-possible-invocation", completion));
        }
    }
    private sealed class FailingAttemptExecutor(UpdateProviderAttemptStatus status) : IApprovedUpdateExecutor { public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) => Task.FromResult(new UpdateProviderAttempt(status, 9, "provider-negative-result")); }
    private sealed class Inventory(Dictionary<Guid, PostUpdateInventoryResult> results) : IPostUpdateInventory { public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(results[job.Id]); }
    private sealed class ThrowingInventory : IPostUpdateInventory { public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => throw new IOException("inventory failed"); }
    private sealed class CancellingInventory(CancellationTokenSource cancellation) : IPostUpdateInventory { public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) { cancellation.Cancel(); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(new PostUpdateInventoryResult(false, false)); } }
    private sealed class RunningDetector(bool running) : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(running ? new RunningApplicationDetection(true, ["fixture"]) : RunningApplicationDetection.NotDetected);
    }
    private sealed class InMemoryHistory : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
