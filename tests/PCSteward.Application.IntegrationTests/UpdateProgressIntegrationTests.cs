using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
[TestCategory("FR-UPD-14")]
public sealed class UpdateProgressIntegrationTests
{
    [TestMethod]
    public async Task UnreadMailboxRetainsOnlyRecordedVerifiedTerminalAndDoesNotBlockNextRun()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        History history = new();
        Executor executor = new((_, _, progress, _) =>
        {
            Assert.AreEqual(2, mailbox.Counters.Attempts, "Queued and preparing precede dispatch.");
            progress.ProcessStarted();
            return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "fixture"));
        });
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), history, new Detector());
        UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.Success, result.FinalEntries.Single().Status);
        Assert.AreEqual(UpdateHistoryStatus.Success, mailbox.RetainedTerminal!.RecordedStatus);
        Assert.AreEqual(UpdateProgressOfferResult.Closed, mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing));
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? terminal));
        Assert.AreEqual(1U, terminal!.Sequence);
        Assert.AreEqual(UpdateProgressPhase.Final, terminal.Phase);
        ImmutableUpdateJob next = Job();
        await coordinator.RunAsync([Item(next, Mailbox(next))], CancellationToken.None);
        Assert.AreEqual(2, executor.Calls);
    }

    [TestMethod]
    public async Task TerminalWaitsForDurableHistoryAndRecordingFailureNeverFabricatesSuccess()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        History history = new(async entry =>
        {
            if (entry.Status != UpdateHistoryStatus.Success) return;
            reached.SetResult();
            await release.Task;
            throw new IOException("synthetic persistence failure");
        });
        SequentialUpdateBatchCoordinator coordinator = new(new Executor(Started), new Inventory(), history, new Detector());
        Task<UpdateBatchResult> run = coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsNull(mailbox.RetainedTerminal);
        release.SetResult();
        await Assert.ThrowsAsync<IOException>(async () => await run);
        Assert.AreEqual(UpdateProgressCompletion.RecordingFailed, mailbox.RetainedTerminal!.Completion);
        Assert.IsNull(mailbox.RetainedTerminal.RecordedStatus);
        Assert.IsFalse(history.Entries.Any(entry => entry.Status == UpdateHistoryStatus.Success));
    }

    [TestMethod]
    public async Task InternalCapacityFaultCancelsItemWithoutCallerTokenAndStopsFurtherDispatch()
    {
        ImmutableUpdateJob first = Job();
        ImmutableUpdateJob second = Job();
        UpdateProgressMailbox mailbox = Mailbox(first);
        int cancelled = 0;
        TaskCompletionSource cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Executor executor = new(async (job, _, progress, token) =>
        {
            progress.ProcessStarted();
            using CancellationTokenRegistration registration = token.Register(() =>
            {
                Interlocked.Increment(ref cancelled);
                cancellationObserved.TrySetResult();
            });
            for (int index = 0; index < 256; index++) mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            return new(UpdateProviderAttemptStatus.Cancelled, null, "operation-cancelled");
        });
        History history = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), history, new Detector());
        UpdateBatchResult result = await coordinator.RunAsync([Item(first, mailbox), Item(second, Mailbox(second))], CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, executor.Calls);
        Assert.AreEqual(1, cancelled);
        UpdateHistoryEntry interrupted = result.FinalEntries.Single(entry => entry.JobId == first.Id);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, interrupted.Status);
        StringAssert.Contains(interrupted.Diagnostic, "progress-capacity-fault");
        Assert.IsFalse(interrupted.Diagnostic.Contains("user-cancelled", StringComparison.Ordinal));
        Assert.IsFalse(history.Entries.Any(entry => entry.JobId == second.Id && entry.Status == UpdateHistoryStatus.Running));
        Assert.IsTrue(mailbox.RetainedTerminal!.Degraded);
    }

    [TestMethod]
    public async Task ExceptionAfterObservedStartRetainsUncertainty()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        SequentialUpdateBatchCoordinator coordinator = new(new Executor((_, _, progress, _) =>
        {
            progress.ProcessStarted();
            throw new IOException("synthetic failure after start");
        }), new Inventory(), new History(), new Detector());
        UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, result.FinalEntries.Single().Status);
        Assert.IsFalse(result.FinalEntries.Single().Diagnostic.Contains("could-not-start", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ForeignMailboxRefusesBeforeHistoryOrDispatch()
    {
        History history = new();
        Executor executor = new(Started);
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), history, new Detector());
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await coordinator.RunAsync([Item(Job(), Mailbox(Job()))], CancellationToken.None));
        Assert.IsEmpty(history.Entries);
        Assert.AreEqual(0, executor.Calls);
    }

    [TestMethod]
    public async Task LateWarningCompletesAsAcknowledgementAndNeverInvokesProvider()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        Executor executor = new(Started);
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), new History(), new Detector(true));
        await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(UpdateProgressCompletion.AcknowledgementRequired, mailbox.RetainedTerminal!.Completion);
        Assert.IsNull(mailbox.RetainedTerminal.RecordedStatus);
    }

    [TestMethod]
    public async Task CapacityFaultDuringPreparationCannotLaunch()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        Executor executor = new(Started);
        History history = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), history,
            new DelegateDetector(async token =>
            {
                for (int index = 0; index < 256; index++) mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return RunningApplicationDetection.NotDetected;
            }));
        UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(0, executor.Calls);
        Assert.AreEqual(UpdateHistoryStatus.Cancelled, result.FinalEntries.Single().Status);
        StringAssert.Contains(result.FinalEntries.Single().Diagnostic, "progress-capacity-fault; not-started");
        Assert.IsFalse(history.Entries.Any(entry => entry.Status == UpdateHistoryStatus.Running));
    }

    [TestMethod]
    public async Task CapacityFaultDuringVerificationCannotBecomeSuccess()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        SequentialUpdateBatchCoordinator coordinator = new(new Executor(Started), new DelegateInventory(_ =>
        {
            for (int index = 0; index < 256; index++) mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing);
            return Task.FromResult(new PostUpdateInventoryResult(true, false));
        }), new History(), new Detector());
        UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, result.FinalEntries.Single().Status);
        StringAssert.Contains(result.FinalEntries.Single().Diagnostic, "progress-capacity-fault");
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, mailbox.RetainedTerminal!.RecordedStatus);
    }

    [TestMethod]
    public async Task OffersDuringFinalHistoryWriteAreClosedAndCannotRetroactivelyCancelSuccess()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        History history = new(entry =>
        {
            if (entry.Status == UpdateHistoryStatus.Success)
            {
                Assert.IsNull(mailbox.RetainedTerminal);
                for (int index = 0; index < 300; index++)
                    Assert.AreEqual(UpdateProgressOfferResult.Closed, mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing));
                Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
            }
            return Task.CompletedTask;
        });
        SequentialUpdateBatchCoordinator coordinator = new(new Executor(Started), new Inventory(), history, new Detector());
        UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.Success, result.FinalEntries.Single().Status);
        Assert.AreEqual(UpdateHistoryStatus.Success, mailbox.RetainedTerminal!.RecordedStatus);
        Assert.IsFalse(mailbox.RetainedTerminal.Degraded);
    }

    [TestMethod]
    public async Task ReviewMailboxIsForwardedThroughFreshConfirmationAndCannotBeSubstituted()
    {
        ImmutableUpdateJob selectionFixture = Job();
        Store store = new();
        Executor executor = new(Started);
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), new History(), new Detector());
        ReviewedUpdateExecutionWorkflow workflow = new((_, _) => Task.FromResult(new UpdateSelectionPreparation(selectionFixture.Selection, "fresh")),
            new UpdateJobApprovalService(store), coordinator, new Detector());
        UpdateReviewPreparation review = await workflow.BeginReviewAsync(selectionFixture.Selection.StableProductKey, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None, Mailbox(Job())));
        Assert.AreEqual(0, store.Approvals);
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(review.Review!, false, CancellationToken.None, review.Progress);
        Assert.IsTrue(result.Started);
        Assert.AreEqual(1, store.Approvals);
        Assert.AreEqual(UpdateHistoryStatus.Success, review.Progress!.RetainedTerminal!.RecordedStatus);
        Assert.AreEqual(review.Review!.JobId, review.Progress.RetainedTerminal.JobId);
    }

    [TestMethod]
    public async Task InvalidMilestoneIsReportedOnceAndThrowingDiagnosticSinkCannotReplaceRecordedOutcome()
    {
        foreach (bool throwFromSink in new[] { false, true })
        {
            ImmutableUpdateJob job = Job();
            UpdateProgressMailbox mailbox = Mailbox(job);
            Sink sink = new(throwFromSink);
            Executor executor = new((_, _, progress, _) =>
            {
                progress.HandedToHelper(); // Forbidden on this user route: reject, never cancel.
                progress.ProcessStarted();
                return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "fixture"));
            });
            SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), new History(), new Detector(), events: sink);
            UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None);
            Assert.AreEqual(UpdateHistoryStatus.Success, result.FinalEntries.Single().Status);
            Assert.IsTrue(mailbox.HasDegraded);
            Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
            Assert.AreEqual(1, sink.Calls);
            Assert.AreEqual(UpdateExecutionEventSeverity.Warning, sink.Last!.Severity);
            Assert.AreEqual("update.progress-rejected", sink.Last.EventId);
        }
    }

    [TestMethod]
    public async Task UnfinishedInternalCancellationCallbackRetainsAdmissionAfterTelemetryDetaches()
    {
        ImmutableUpdateJob job = Job();
        UpdateProgressMailbox mailbox = Mailbox(job);
        using ManualResetEventSlim releaseCallback = new();
        TaskCompletionSource callbackStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingCallbackExecutor executor = new(mailbox, releaseCallback, callbackStarted);
        History history = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, new Inventory(), history, new Detector());
        Task retained = Task.CompletedTask;
        try
        {
            UpdateBatchResult result = await coordinator.RunAsync([Item(job, mailbox)], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            await callbackStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            retained = executor.ExecutionLifetime.OwnedCompletion;
            Assert.IsFalse(retained.IsCompleted);
            Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, result.FinalEntries.Single().Status);
            mailbox.SealProducer();
            using UpdateExecutionLease? refused = executor.ExecutionLifetime.TryAcquire();
            Assert.IsNull(refused, "Telemetry detachment cannot release unfinished cancellation work.");
        }
        finally
        {
            releaseCallback.Set();
            await retained.WaitAsync(TimeSpan.FromSeconds(5));
        }
        using UpdateExecutionLease? resumed = executor.ExecutionLifetime.TryAcquire();
        Assert.IsNotNull(resumed);
    }

    private static Task<UpdateProviderAttempt> Started(ImmutableUpdateJob job, UpdateJobExecutionEvidence evidence, UpdateItemProgress progress, CancellationToken token)
    {
        progress.ProcessStarted();
        return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "fixture"));
    }
    private static ImmutableUpdateJob Job() => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(),
        new UpdateJobSelection("winget:contoso.fixture", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Fixture", "1.0", "2.0",
            UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);
    private static UpdateProgressMailbox Mailbox(ImmutableUpdateJob job) => new(job.Id, job.CorrelationId, job.Selection.Scope);
    private static UpdateBatchItem Item(ImmutableUpdateJob job, UpdateProgressMailbox mailbox) => new(job, new(job.Selection, true, true), Progress: mailbox);
    private sealed class Executor(Func<ImmutableUpdateJob, UpdateJobExecutionEvidence, UpdateItemProgress, CancellationToken, Task<UpdateProviderAttempt>> run) : IApprovedUpdateExecutor
    {
        public int Calls { get; private set; }
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence evidence, CancellationToken token) =>
            ExecuteAsync(job, evidence, new UpdateItemProgress(job, null), token);
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence evidence, UpdateItemProgress progress, CancellationToken token)
        {
            Calls++;
            return run(job, evidence, progress, token);
        }
    }
    private sealed class Inventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken token) => Task.FromResult(new PostUpdateInventoryResult(true, false));
    }
    private sealed class DelegateInventory(Func<CancellationToken, Task<PostUpdateInventoryResult>> run) : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken token) => run(token);
    }
    private sealed class DelegateDetector(Func<CancellationToken, Task<RunningApplicationDetection>> run) : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken token) => run(token);
    }
    private sealed class Store : IUpdateJobStore
    {
        public int Approvals { get; private set; }
        public Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken token) { Approvals++; return Task.CompletedTask; }
        public Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken token) => Task.FromResult<ImmutableUpdateJob?>(null);
    }
    private sealed class Sink(bool fail) : IUpdateExecutionEventSink
    {
        public int Calls { get; private set; }
        public UpdateExecutionEvent? Last { get; private set; }
        public void Write(UpdateExecutionEvent updateEvent)
        {
            if (updateEvent.EventId.StartsWith("update.progress-", StringComparison.Ordinal))
            {
                Calls++;
                Last = updateEvent;
            }
            if (fail) throw new IOException("synthetic diagnostic sink failure");
        }
    }
    private sealed class BlockingCallbackExecutor(UpdateProgressMailbox mailbox, ManualResetEventSlim release, TaskCompletionSource started) : IApprovedUpdateExecutor
    {
        public UpdateExecutionLifetime ExecutionLifetime { get; } = new();
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence evidence, CancellationToken token) =>
            ExecuteAsync(job, evidence, new UpdateItemProgress(job, mailbox), token);
        public async Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence evidence, UpdateItemProgress progress, CancellationToken token)
        {
            progress.ProcessStarted();
            CancellationTokenRegistration registration = token.Register(() => { started.TrySetResult(); release.Wait(); });
            for (int index = 0; index < 256; index++) mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            // Registration disposal can itself await a running callback. Its root stays owned too.
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Task cleanup = registration.DisposeAsync().AsTask();
            return new(UpdateProviderAttemptStatus.Cancelled, null, "operation-cancelled", cleanup);
        }
    }
    private sealed class Detector(bool running = false) : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken token) =>
            Task.FromResult(new RunningApplicationDetection(running, running ? ["fixture"] : []));
    }
    private sealed class History(Func<UpdateHistoryEntry, Task>? beforeRecord = null) : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public async Task RecordAsync(UpdateHistoryEntry entry, CancellationToken token)
        {
            if (beforeRecord is not null) await beforeRecord(entry);
            Entries.Add(entry);
        }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string key, CancellationToken token) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(Entries);
        public Task RecoverInterruptedAsync(CancellationToken token) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken token) => Task.CompletedTask;
    }
}
