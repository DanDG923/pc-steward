using PCSteward.Application;
using PCSteward.Execution;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

[TestClass]
[TestCategory("FR-UPD-14")]
public sealed class HelperProcessOwnershipTests
{
    [TestMethod]
    public async Task HelperOwnershipReachesTheApplicationAdmissionBoundary()
    {
        TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ElevatedUpdateRunner runner = new(new OwnedExchange(exit.Task));
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = WingetMachineReviewedDispatchTests.Frozen();
        UpdateJobExecutionEvidence evidence = new(job.Selection, true, true);
        UpdateExecutionResult result = await runner.RunReviewedAsync(job, operation, evidence, CancellationToken.None);
        Assert.AreEqual(UpdateExecutionStatus.TimedOut, result.Status);
        Assert.AreSame(exit.Task, result.OwnedCompletion);
        IApprovedUpdateExecutor executor = new ApprovedUpdateExecutor(new NonElevatedUpdateRunner(new NeverUserExecutor()), runner);
        UpdateProviderAttempt attempt = await executor.ExecuteAsync(job, operation, evidence, new UpdateItemProgress(job, null), CancellationToken.None);
        Assert.AreSame(exit.Task, attempt.OwnedCompletion);
        Assert.IsNotNull(executor.ExecutionLifetime);
        exit.SetResult();
    }

    [TestMethod]
    public async Task TransportCompletionDoesNotDisposeAnUnexitedHelperOrClearAdmission()
    {
        TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int disposed = 0;
        HelperProcessWork work = new(exit.Task, () => Interlocked.Increment(ref disposed));
        UpdateExecutionLifetime lifetime = new();
        UpdateExecutionLease? admission = lifetime.TryAcquire();
        Assert.IsNotNull(admission);
        admission.TrackCompletion(work.OwnedCompletion);
        work.SealTransport();
        admission.Dispose();
        Assert.AreEqual(0, disposed);
        Assert.IsFalse(work.OwnedCompletion.IsCompleted);
        Assert.IsNull(lifetime.TryAcquire());
        exit.SetResult();
        await work.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, disposed);
        using UpdateExecutionLease? next = lifetime.TryAcquire();
        Assert.IsNotNull(next);
    }

    [TestMethod]
    public async Task ExitDoesNotDisposeMetadataWhileTransportStillUsesTheProcess()
    {
        int disposed = 0;
        HelperProcessWork work = new(Task.CompletedTask, () => disposed++);
        Assert.AreEqual(0, disposed);
        Assert.IsFalse(work.OwnedCompletion.IsCompleted);
        work.SealTransport();
        work.SealTransport();
        await work.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.AreEqual(1, disposed);
    }

    [TestMethod]
    public async Task FailedExitObservationCannotBeReinterpretedAsObservedProcessExit()
    {
        Task failure = Task.FromException(new IOException("synthetic exit observation failed"));
        int disposed = 0;
        HelperProcessWork work = new(failure, () => disposed++);
        work.SealTransport();
        Assert.AreEqual(0, disposed);
        Task first = await Task.WhenAny(work.OwnedCompletion, Task.Delay(50));
        Assert.AreNotSame(work.OwnedCompletion, first);
        Assert.AreEqual(0, disposed);
        _ = failure.Exception;
    }

    private sealed class OwnedExchange(Task completion) : IWingetMachineReviewedExchange
    {
        public Task<ReviewedUpdateDispatchObservation> RunAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken) =>
            Task.FromResult(new ReviewedUpdateDispatchObservation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.TimedOut, null,
                "helper-exit-unconfirmed", completion, false, false, false, false, false));
    }
    private sealed class NeverUserExecutor : IWingetReviewedUserUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ReviewedUpdateOperation operation, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Machine work cannot fall back to the user executor.");
    }
}
