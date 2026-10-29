using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
[TestCategory("FR-UPD-14")]
public sealed class UpdateExecutionLifetimeTests
{
    [TestMethod]
    public void TelemetrySealAndLeaseDisposalCannotReleaseUnfinishedOwnedWork()
    {
        UpdateExecutionLifetime lifetime = new();
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateExecutionLease? lease = lifetime.TryAcquire();
        Assert.IsNotNull(lease);
        lease.TrackCompletion(pending.Task);
        UpdateProgressMailbox progress = new(Guid.NewGuid(), Guid.NewGuid(), UpdateScope.User);
        progress.SealProducer();
        lease.Dispose();
        Assert.IsNull(lifetime.TryAcquire(), "Only observed owned completion may release the safety hold.");
        pending.SetResult();
        using UpdateExecutionLease? next = lifetime.TryAcquire();
        Assert.IsNotNull(next);
    }

    [TestMethod]
    public async Task ConcurrentAdmissionHasExactlyOneOwner()
    {
        UpdateExecutionLifetime lifetime = new();
        UpdateExecutionLease?[] attempts = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Run(lifetime.TryAcquire)));
        Assert.AreEqual(1, attempts.Count(lease => lease is not null));
        foreach (UpdateExecutionLease? lease in attempts) lease?.Dispose();
        using UpdateExecutionLease? next = lifetime.TryAcquire();
        Assert.IsNotNull(next);
    }

    [TestMethod]
    public void StaleLeaseCannotClearTheNextOwnerOrAttachNewWork()
    {
        UpdateExecutionLifetime lifetime = new();
        UpdateExecutionLease? first = lifetime.TryAcquire();
        Assert.IsNotNull(first);
        first.Dispose();
        using UpdateExecutionLease? second = lifetime.TryAcquire();
        Assert.IsNotNull(second);
        first.Dispose();
        Assert.IsNull(lifetime.TryAcquire());
        Assert.Throws<ObjectDisposedException>(() => first.TrackCompletion(Task.CompletedTask));
    }

    [TestMethod]
    public void FaultedCompletionIsObservedAndDoesNotLeaveAnArtificialHold()
    {
        UpdateExecutionLifetime lifetime = new();
        TaskCompletionSource pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateExecutionLease? lease = lifetime.TryAcquire();
        Assert.IsNotNull(lease);
        lease.TrackCompletion(pending.Task);
        lease.Dispose();
        Assert.IsNull(lifetime.TryAcquire());
        pending.SetException(new IOException("synthetic owned completion failure"));
        using UpdateExecutionLease? next = lifetime.TryAcquire();
        Assert.IsNotNull(next);
        _ = pending.Task.Exception;
    }
}
