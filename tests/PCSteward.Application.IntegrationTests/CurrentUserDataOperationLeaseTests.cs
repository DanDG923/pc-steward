using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class CurrentUserDataOperationLeaseTests
{
    [TestMethod]
    public async Task PendingResetPreflightRefusesStateOpenBeforeReconciliation()
    {
        int reconciliation = 0;
        int work = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentUserDataOperationLease.RunForTestingAsync(Guid.NewGuid(),
            (_, _) => { reconciliation++; return Task.FromResult(true); }, (_, _) => Task.FromResult(++work),
            TimeSpan.Zero, null, CancellationToken.None, statePreflight: () => false));
        Assert.AreEqual(0, reconciliation);
        Assert.AreEqual(0, work);
    }
    [TestMethod]
    public async Task DedicatedOwnerThreadKeepsNativeOwnershipAcrossAsyncWorkAndReleasesIt()
    {
        Guid id = Guid.NewGuid();
        OperationOwnership? captured = null;
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int reconciliations = 0;
        Task<int> work = CurrentUserDataOperationLease.RunForTestingAsync(id, (context, _) =>
        {
            captured = context;
            reconciliations++;
            return Task.FromResult(true);
        }, async (context, _) =>
        {
            Assert.AreSame(captured, context);
            started.SetResult();
            await release.Task;
            Assert.IsTrue(context.IsActive);
            return 42;
        }, TimeSpan.Zero, null, CancellationToken.None);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentUserDataOperationLease.RunForTestingAsync(id,
                (_, _) => Task.FromResult(true), (_, _) => Task.FromResult(1), TimeSpan.Zero, null, CancellationToken.None));
        }
        finally { release.TrySetResult(); }
        Assert.AreEqual(42, await work);
        Assert.AreEqual(1, reconciliations);
        Assert.IsNotNull(captured);
        Assert.IsFalse(captured.IsActive);
        Assert.AreEqual(2, await CurrentUserDataOperationLease.RunForTestingAsync(id, (_, _) => Task.FromResult(true), (_, _) => Task.FromResult(2), TimeSpan.Zero, null, CancellationToken.None));
    }

    [TestMethod]
    public async Task UncertainReconciliationBlocksTheOperation()
    {
        int invocations = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentUserDataOperationLease.RunForTestingAsync(Guid.NewGuid(),
            (_, _) => Task.FromResult(false), (_, _) => Task.FromResult(++invocations), TimeSpan.Zero, null, CancellationToken.None));
        Assert.AreEqual(0, invocations);
    }

    [TestMethod]
    public async Task ExpiryWhileReconcilingDoesNotRenewApprovalOrInvokeWork()
    {
        int invocations = 0;
        int reconciliations = 0;
        ManualClock clock = new();
        DateTimeOffset deadline = clock.GetUtcNow().AddMinutes(1);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentUserDataOperationLease.RunForTestingAsync(Guid.NewGuid(),
            (_, _) => { reconciliations++; clock.Now = deadline.AddTicks(1); return Task.FromResult(true); }, (_, _) => Task.FromResult(++invocations), TimeSpan.Zero, deadline, CancellationToken.None, clock));
        Assert.AreEqual(1, reconciliations);
        Assert.AreEqual(0, invocations);
    }

    [TestMethod]
    public async Task CancellationWhileReconcilingPreventsWork()
    {
        using CancellationTokenSource cancellation = new();
        int invocations = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => CurrentUserDataOperationLease.RunForTestingAsync(Guid.NewGuid(),
            (_, _) => { cancellation.Cancel(); return Task.FromResult(true); }, (_, _) => Task.FromResult(++invocations), TimeSpan.Zero, null, cancellation.Token));
        Assert.AreEqual(0, invocations);
    }

    [TestMethod]
    public async Task BusyLegacyMutexReleasesThePartiallyAcquiredGlobalGate()
    {
        Guid id = Guid.NewGuid();
        using ManualResetEventSlim ready = new();
        using ManualResetEventSlim release = new();
        Exception? failure = null;
        Thread holder = new(() =>
        {
            try
            {
                using Mutex legacy = new(false, CurrentUserDataOperationSession.LegacyNameForTesting(id));
                if (!legacy.WaitOne(0)) throw new InvalidOperationException("Fixture legacy gate is busy.");
                try { ready.Set(); release.Wait(); }
                finally { legacy.ReleaseMutex(); }
            }
            catch (Exception exception) { failure = exception; ready.Set(); }
        });
        holder.Start();
        try
        {
            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(10)));
            Assert.IsNull(failure);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentUserDataOperationLease.RunForTestingAsync(id,
                (_, _) => Task.FromResult(true), (_, _) => Task.FromResult(1), TimeSpan.Zero, null, CancellationToken.None));
        }
        finally { release.Set(); Assert.IsTrue(holder.Join(TimeSpan.FromSeconds(10))); }
        Assert.AreEqual(2, await CurrentUserDataOperationLease.RunForTestingAsync(id, (_, _) => Task.FromResult(true), (_, _) => Task.FromResult(2), TimeSpan.Zero, null, CancellationToken.None));
    }

    [TestMethod]
    public void AbandonedLegacyOwnershipIsRecoveryOnlyUntilReconciled()
    {
        Guid id = Guid.NewGuid();
        using Mutex retained = new(false, CurrentUserDataOperationSession.LegacyNameForTesting(id));
        Exception? failure = null;
        Thread abandoned = new(() =>
        {
            try { using Mutex held = new(false, CurrentUserDataOperationSession.LegacyNameForTesting(id)); if (!held.WaitOne(0)) throw new InvalidOperationException("Fixture mutex is busy."); }
            catch (Exception exception) { failure = exception; }
        });
        abandoned.Start();
        Assert.IsTrue(abandoned.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(failure);
        using CurrentUserDataOperationSession? session = CurrentUserDataOperationSession.TryAcquireForTesting(id, TimeSpan.Zero, CancellationToken.None);
        Assert.IsNotNull(session);
        Assert.IsTrue(session.Context.RecoveryRequired);
    }

    private sealed class ManualClock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public void WrongThreadDisposalDoesNotReleaseOrInvalidateOwnership()
    {
        using CurrentUserDataOperationSession? session = CurrentUserDataOperationSession.TryAcquireForTesting(Guid.NewGuid(), TimeSpan.Zero, CancellationToken.None);
        Assert.IsNotNull(session);
        Exception? failure = null;
        Thread wrongThread = new(() => { try { session.Dispose(); } catch (Exception exception) { failure = exception; } });
        wrongThread.Start();
        Assert.IsTrue(wrongThread.Join(TimeSpan.FromSeconds(10)));
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        Assert.IsTrue(session.Context.IsActive);
    }

    [TestMethod]
    public async Task RecoveryAndOperationFailuresReleaseBothNativeObjects()
    {
        Guid id = Guid.NewGuid();
        await Assert.ThrowsExactlyAsync<IOException>(() => CurrentUserDataOperationLease.RunForTestingAsync(id,
            (_, _) => throw new IOException("fixture reconciliation failure"), (_, _) => Task.FromResult(1), TimeSpan.Zero, null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<IOException>(() => CurrentUserDataOperationLease.RunForTestingAsync<int>(id,
            (_, _) => Task.FromResult(true), (_, _) => throw new IOException("fixture work failure"), TimeSpan.Zero, null, CancellationToken.None));
        Assert.AreEqual(3, await CurrentUserDataOperationLease.RunForTestingAsync(id,
            (_, _) => Task.FromResult(true), (_, _) => Task.FromResult(3), TimeSpan.Zero, null, CancellationToken.None));
    }

    [TestMethod]
    public async Task ClockRollbackDuringRecoveryRefusesWork()
    {
        ManualClock clock = new();
        int invocations = 0;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => CurrentUserDataOperationLease.RunForTestingAsync(Guid.NewGuid(),
            (_, _) => { clock.Now -= TimeSpan.FromMinutes(1); return Task.FromResult(true); }, (_, _) => Task.FromResult(++invocations),
            TimeSpan.Zero, null, CancellationToken.None, clock));
        Assert.AreEqual(0, invocations);
    }

    [TestMethod]
    public async Task SuccessfulReconciliationClearsRecoveryOnlyStatusBeforeWork()
    {
        Guid id = Guid.NewGuid();
        using CurrentUserOperationGate retainedGlobal = CurrentUserOperationGate.OpenForTesting(id);
        using Mutex retainedLegacy = new(false, CurrentUserDataOperationSession.LegacyNameForTesting(id));
        CurrentUserDataOperationSession? abandonedSession = null;
        Exception? failure = null;
        Thread abandon = new(() =>
        {
            try { abandonedSession = CurrentUserDataOperationSession.TryAcquireForTesting(id, TimeSpan.Zero, CancellationToken.None); }
            catch (Exception exception) { failure = exception; }
        });
        abandon.Start();
        Assert.IsTrue(abandon.Join(TimeSpan.FromSeconds(10)));
        Assert.IsNull(failure);
        Assert.IsNotNull(abandonedSession);
        bool reconciled = false;
        Assert.AreEqual(1, await CurrentUserDataOperationLease.RunForTestingAsync(id, (context, _) =>
        {
            Assert.IsTrue(context.RecoveryRequired);
            reconciled = true;
            return Task.FromResult(true);
        }, (context, _) =>
        {
            Assert.IsTrue(reconciled);
            Assert.IsFalse(context.RecoveryRequired);
            return Task.FromResult(1);
        }, TimeSpan.Zero, null, CancellationToken.None));
        GC.KeepAlive(abandonedSession);
    }
}
