using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

[TestClass]
[TestCategory("FR-UPD-14")]
[TestCategory("FR-UPD-03")]
public sealed class MachineProviderOwnershipTests
{
    [TestMethod]
    public async Task ProtectedProjectionRetainsOwnedWorkAcrossOperationCancellation()
    {
        TaskCompletionSource owned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenSource cancellation = new();
        IProtectedMachineUpdateExecutor executor = Executor(owned.Task);
        Task<ProviderProcessResult> operation = executor.ExecuteProtectedAsync(Job(), cancellation.Token);
        try
        {
            Assert.IsFalse(operation.IsCompleted, "A scalar protected result must not discard unfinished provider work.");
            cancellation.Cancel();
            Assert.IsFalse(operation.IsCompleted, "Operation cancellation cannot abandon the provider's cleanup root.");
        }
        finally { owned.TrySetResult(); }
        ProviderProcessResult result = await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(result.TimedOut);
        Assert.IsTrue(result.OutputIncomplete);
        Assert.IsTrue(result.ExecutionUncertain);
        Assert.AreEqual("partial", result.StandardOutput);
        Assert.IsFalse(typeof(ProviderProcessResult).GetProperties().Any(property => typeof(Task).IsAssignableFrom(property.PropertyType)));
    }

    [TestMethod]
    public async Task UnfinishedProviderWorkKeepsHelperExitAndDesktopAdmissionPending()
    {
        TaskCompletionSource owned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource helperExit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ProviderProcessResult> provider = ((IProtectedMachineUpdateExecutor)Executor(owned.Task)).ExecuteProtectedAsync(Job(), CancellationToken.None);
        // The real helper likewise awaits its protected provider before writing its response/returning.
        Task helper = FinishHelperAsync(provider, helperExit);
        int disposed = 0;
        HelperProcessWork helperWork = new(helperExit.Task, () => disposed++);
        helperWork.SealTransport();
        UpdateExecutionLifetime lifetime = new();
        UpdateExecutionLease? admission = lifetime.TryAcquire();
        Assert.IsNotNull(admission);
        admission.TrackCompletion(helperWork.OwnedCompletion);
        admission.Dispose();
        try
        {
            Assert.IsFalse(helperExit.Task.IsCompleted, "Provider-owned work must finish before normal helper exit.");
            Assert.IsFalse(helperWork.OwnedCompletion.IsCompleted);
            Assert.AreEqual(0, disposed);
            using UpdateExecutionLease? conflicting = lifetime.TryAcquire();
            Assert.IsNull(conflicting, "A second update must remain blocked while the helper owns unfinished provider work.");
        }
        finally
        {
            owned.TrySetResult();
            await helper.WaitAsync(TimeSpan.FromSeconds(5));
            await helperWork.OwnedCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.AreEqual(1, disposed);
        using UpdateExecutionLease? next = lifetime.TryAcquire();
        Assert.IsNotNull(next);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FaultOrCancellationOfOwnedCompletionIsObservedBeforeProjection(bool cancelled)
    {
        TaskCompletionSource owned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ProviderProcessResult> operation = ((IProtectedMachineUpdateExecutor)Executor(owned.Task)).ExecuteProtectedAsync(Job(), CancellationToken.None);
        try { Assert.IsFalse(operation.IsCompleted); }
        finally
        {
            if (cancelled) owned.TrySetCanceled();
            else owned.TrySetException(new IOException("synthetic owned cleanup failure"));
        }
        if (cancelled) await Assert.ThrowsAsync<OperationCanceledException>(async () => await operation);
        else await Assert.ThrowsAsync<IOException>(async () => await operation);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AbsentOrCompletedOwnershipPreservesExistingProtectedResult(bool completed)
    {
        IProtectedMachineUpdateExecutor executor = Executor(completed ? Task.CompletedTask : null);
        Task<ProviderProcessResult> operation = executor.ExecuteProtectedAsync(Job(), CancellationToken.None);
        Assert.IsTrue(operation.IsCompletedSuccessfully);
        ProviderProcessResult result = await operation;
        Assert.AreEqual(-1, result.ExitCode);
        Assert.AreEqual("partial", result.StandardOutput);
        Assert.AreEqual("fixture", result.StandardError);
        Assert.IsTrue(result.TimedOut);
        Assert.IsTrue(result.OutputIncomplete);
        Assert.IsTrue(result.ExecutionUncertain);
    }

    private static WingetMachineUpdateExecutor Executor(Task? completion) => new WingetMachineUpdateExecutor(
        new FixtureProcess(new(-1, "partial", "fixture", true, OutputIncomplete: true, OwnedCompletion: completion, ExecutionUncertain: true)));
    private static ImmutableUpdateJob Job() => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(),
        new("winget:contoso.fixture", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Fixture", "1.0", "2.0",
            UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);
    private static async Task FinishHelperAsync(Task<ProviderProcessResult> provider, TaskCompletionSource exit)
    {
        try { await provider; }
        finally { exit.TrySetResult(); }
    }
    private sealed class FixtureProcess(WingetProcessResult result) : IWingetProcess
    {
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) =>
            throw new AssertFailedException("The metadata process route is forbidden in this synthetic upgrade fixture.");
        public Task<WingetProcessResult> RunUpgradeAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken token) =>
            Task.FromResult(result);
    }
}
