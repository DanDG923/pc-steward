using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15; TM-04/06/07: the desktop facade over the common current-user gate. Mutations fail closed on unresolved
/// update barriers, pending reset or missing reconciliation; reads stay available; bootstrap reuses its session.
/// Native fixtures use GUID-only gate names, never production mutexes or owner state.
/// </summary>
[TestClass]
public sealed class DataOperationLeaseTests
{
    [TestMethod]
    public void MutationRequiresAClearReconciliationButReadsStayAvailable()
    {
        Fixture fixture = new();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run(DataOperationKind.Mutating, reconcileClear: false));
        Assert.AreEqual(0, fixture.Work);
        Assert.AreEqual(1, fixture.Reconciliations);
        Assert.IsTrue(fixture.Run(DataOperationKind.Read, reconcileClear: false));
        Assert.AreEqual(1, fixture.Work);
        Assert.IsTrue(fixture.Run(DataOperationKind.Mutating, reconcileClear: true));
        Assert.AreEqual(2, fixture.Work);
    }

    [TestMethod]
    public void UnreadableBarrierStateBlocksMutationButNotReads()
    {
        Fixture fixture = new();
        Func<OperationOwnership, CancellationToken, Task<bool>> corrupt = (_, _) => Task.FromException<bool>(new InvalidOperationException("corrupt dispatch store"));
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run(DataOperationKind.Mutating, reconcile: corrupt));
        Assert.IsTrue(fixture.Run(DataOperationKind.Read, reconcile: corrupt));
        Assert.AreEqual(1, fixture.Work);
    }

    [TestMethod]
    [DataRow(nameof(DataOperationKind.Mutating))]
    [DataRow(nameof(DataOperationKind.Read))]
    public void PendingResetRefusesStateAccessBeforeReconciliation(string kindName)
    {
        DataOperationKind kind = Enum.Parse<DataOperationKind>(kindName);
        Fixture fixture = new();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run(kind, markerClear: false));
        Assert.AreEqual(0, fixture.Reconciliations);
        Assert.AreEqual(0, fixture.Work);
    }

    [TestMethod]
    [DataRow(nameof(DataOperationKind.Mutating))]
    [DataRow(nameof(DataOperationKind.Read))]
    public void MissingReconciliationConfigurationFailsClosed(string kindName)
    {
        DataOperationKind kind = Enum.Parse<DataOperationKind>(kindName);
        Fixture fixture = new();
        Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run(kind, unconfigured: true));
        Assert.AreEqual(0, fixture.Work);
    }

    [TestMethod]
    public void ResetControlUsesTheGateWithoutMarkerPreflightOrReconciliation()
    {
        Fixture fixture = new();
        Assert.IsTrue(fixture.Run(DataOperationKind.ResetControl, markerClear: false, unconfigured: true));
        Assert.AreEqual(0, fixture.Reconciliations);
        Assert.AreEqual(1, fixture.Work);
    }

    [TestMethod]
    public async Task ConcurrentHolderMakesEveryKindBusyAndReleaseRestoresAccess()
    {
        Fixture fixture = new();
        TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task holder = Task.Factory.StartNew(() =>
        {
            using CurrentUserDataOperationSession session = fixture.Acquire(TimeSpan.Zero, CancellationToken.None)!;
            held.SetResult();
            release.Task.GetAwaiter().GetResult();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await Task.WhenAny(held.Task, holder).WaitAsync(TimeSpan.FromSeconds(30));
            if (!held.Task.IsCompleted) await holder;
            foreach (DataOperationKind kind in Enum.GetValues<DataOperationKind>())
                Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run(kind));
            Assert.AreEqual(0, fixture.Work);
        }
        finally { release.TrySetResult(); }
        await holder;
        Assert.IsTrue(fixture.Run(DataOperationKind.Mutating));
    }

    [TestMethod]
    public void BootstrapScopeIsReusedWithoutRecursiveAcquisitionAndEndsWithItsOwner()
    {
        Fixture fixture = new();
        using CurrentUserDataOperationSession session = fixture.Acquire(TimeSpan.Zero, CancellationToken.None)!;
        int acquisitions = 0;
        using (DataOperationLease.EnterBootstrapForTesting(session, reconciledClear: false))
        {
            bool Run(DataOperationKind kind) => DataOperationLease.RunOnCurrentThread(owner => { Assert.AreSame(session.Context, owner); return true; }, kind,
                (_, _) => { acquisitions++; return null; }, () => true, (_, _) => { fixture.Reconciliations++; return Task.FromResult(true); }, CancellationToken.None);
            Assert.IsTrue(Run(DataOperationKind.Read));
            Assert.ThrowsExactly<InvalidOperationException>(() => Run(DataOperationKind.Mutating));
            Assert.ThrowsExactly<InvalidOperationException>(() => DataOperationLease.EnterBootstrap(session));
        }
        Assert.AreEqual(0, acquisitions);
        Assert.AreEqual(0, fixture.Reconciliations);
        using (DataOperationLease.EnterBootstrapForTesting(session, reconciledClear: true))
            Assert.IsTrue(DataOperationLease.RunOnCurrentThread(_ => true, DataOperationKind.Mutating, (_, _) => null, () => true, null, CancellationToken.None));
        // After the scope ends the facade acquires again. Native mutexes are re-entrant on their owning thread, so
        // contention is observed from another thread while the bootstrap session still holds the gate.
        // Block synchronously: the session must be released on this (acquiring) thread, so no await may move it.
        Exception? contended = Task.Factory.StartNew(() =>
        {
            try { fixture.Run(DataOperationKind.Read); return null; }
            catch (Exception exception) { return exception; }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();
        Assert.IsInstanceOfType<InvalidOperationException>(contended);
    }

    [TestMethod]
    public void NestedAcquisitionOnTheOwningThreadIsRefusedNotSilentlyReentrant()
    {
        Fixture fixture = new();
        using (CurrentUserDataOperationSession outer = fixture.Acquire(TimeSpan.Zero, CancellationToken.None)!)
        {
            Assert.IsNull(fixture.Acquire(TimeSpan.Zero, CancellationToken.None));
            Assert.IsNull(CurrentUserDataOperationSession.TryAcquireForTesting(Guid.NewGuid(), TimeSpan.Zero, CancellationToken.None));
            Assert.ThrowsExactly<InvalidOperationException>(() => fixture.Run(DataOperationKind.Read));
            Assert.AreEqual(0, fixture.Work);
        }
        Assert.IsTrue(fixture.Run(DataOperationKind.Mutating));
    }

    [TestMethod]
    public void ResetStartingDuringReconciliationRefusesWorkAfterReconciling()
    {
        Fixture fixture = new();
        int checks = 0;
        Assert.ThrowsExactly<InvalidOperationException>(() => DataOperationLease.RunOnCurrentThread(_ => { fixture.Work++; return true; },
            DataOperationKind.Read, fixture.Acquire, () => ++checks == 1, (_, _) => { fixture.Reconciliations++; return Task.FromResult(true); }, CancellationToken.None));
        Assert.AreEqual(2, checks);
        Assert.AreEqual(1, fixture.Reconciliations);
        Assert.AreEqual(0, fixture.Work);
    }

    [TestMethod]
    [DataRow("win32")]
    [DataRow("access")]
    [DataRow("io")]
    public void UnopenableGateFailsClosedAsAHandledRefusal(string failure)
    {
        int work = 0;
        InvalidOperationException refused = Assert.ThrowsExactly<InvalidOperationException>(() => DataOperationLease.RunOnCurrentThread(_ => ++work,
            DataOperationKind.Read, (_, _) => throw (failure switch
            {
                "win32" => new System.ComponentModel.Win32Exception(5),
                "access" => (Exception)new UnauthorizedAccessException("untrusted gate"),
                _ => new IOException("gate"),
            }), () => true, (_, _) => Task.FromResult(true), CancellationToken.None));
        Assert.IsNotNull(refused.InnerException);
        Assert.AreEqual(0, work);
    }

    private sealed class Fixture
    {
        private readonly Guid id = Guid.NewGuid();
        internal int Work;
        internal int Reconciliations;
        internal CurrentUserDataOperationSession? Acquire(TimeSpan wait, CancellationToken ct) => CurrentUserDataOperationSession.TryAcquireForTesting(id, wait, ct);

        internal bool Run(DataOperationKind kind, bool reconcileClear = true, bool markerClear = true, bool unconfigured = false,
            Func<OperationOwnership, CancellationToken, Task<bool>>? reconcile = null) =>
            DataOperationLease.RunOnCurrentThread(_ => { Work++; return true; }, kind, Acquire, () => markerClear,
                unconfigured ? null : reconcile ?? ((_, _) => { Reconciliations++; return Task.FromResult(reconcileClear); }), CancellationToken.None);
    }
}
