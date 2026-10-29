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
        ElevatedUpdateRunner runner = new(new OwnedTransport(exit.Task));
        UpdateJobSelection selection = new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", "1.0.0", "1.1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(DateTimeOffset.UtcNow);
        UpdateJobExecutionEvidence evidence = new(selection, true, true);
        UpdateExecutionResult result = await runner.RunAsync(job, evidence, CancellationToken.None);
        Assert.AreEqual(UpdateExecutionStatus.TimedOut, result.Status);
        Assert.AreSame(exit.Task, result.OwnedCompletion);
        IApprovedUpdateExecutor executor = new ApprovedUpdateExecutor(new NonElevatedUpdateRunner(new NeverUserExecutor()), runner);
        UpdateProviderAttempt attempt = await executor.ExecuteAsync(job, evidence, CancellationToken.None);
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

    private sealed class OwnedTransport(Task completion) : IElevatedHelperTransport
    {
        public Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            Task.FromResult(new ElevatedHelperResult(ElevatedHelperOutcome.TimedOut, null,
                "helper-exit-unconfirmed", completion));
    }
    private sealed class NeverUserExecutor : IWingetUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Machine work cannot fall back to the user executor.");
    }
}
