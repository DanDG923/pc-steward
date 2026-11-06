using PCSteward.Application;

namespace PCSteward.Storage;

/// <summary>What a desktop data operation may do while an unresolved update barrier exists.</summary>
internal enum DataOperationKind
{
    /// <summary>Changes state: requires a clear reconciliation (no DispatchClaimed/Uncertain barrier).</summary>
    Mutating,
    /// <summary>Reads state: reconciliation runs, but an unresolved barrier does not hide read-only UI.</summary>
    Read,
    /// <summary>Factory-reset marker authority (first-run acknowledgement): gate only; FactoryReset validates its own marker.</summary>
    ResetControl,
}

/// <summary>
/// Desktop facade over the common current-user gate (<see cref="CurrentUserDataOperationSession"/>: Global SID gate
/// then legacy Local v1). Every acquisition reconciles unfinished dispatches. Mutating work fails closed on any unresolved
/// barrier, pending/invalid reset marker or missing reconciliation; reads stay available. Bootstrap passes its held session
/// through <see cref="EnterBootstrap"/> so the initial load reuses it instead of acquiring recursively.
/// </summary>
public static class DataOperationLease
{
    private static Func<OperationOwnership, CancellationToken, Task<bool>>? reconciliation;
    [ThreadStatic] private static BootstrapScope? ambient;

    /// <summary>Sets the process-wide reconciliation callback once (normally <see cref="UpdateDispatchReconciliation.ReconcileAsync"/>).</summary>
    public static void ConfigureReconciliation(Func<OperationOwnership, CancellationToken, Task<bool>> reconcile)
    {
        ArgumentNullException.ThrowIfNull(reconcile);
        if (Interlocked.CompareExchange(ref reconciliation, reconcile, null) is not null)
            throw new InvalidOperationException("Operation reconciliation is already configured for this process.");
    }

    /// <summary>Makes a bootstrap-held session ambient for the current thread until disposed on that thread.</summary>
    public static IDisposable EnterBootstrap(CurrentUserDataOperationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ambient is not null) throw new InvalidOperationException("A bootstrap data-operation scope is already active on this thread.");
        BootstrapScope scope = new(session);
        ambient = scope;
        return scope;
    }

    /// <summary>Reconciles the ambient bootstrap session; returns whether no unresolved barrier remains.</summary>
    public static bool ReconcileBootstrap(CancellationToken cancellationToken)
    {
        BootstrapScope scope = ambient ?? throw new InvalidOperationException("No bootstrap data-operation scope is active on this thread.");
        Func<OperationOwnership, CancellationToken, Task<bool>> reconcile = reconciliation
            ?? throw new InvalidOperationException("Operation reconciliation is not configured; state access is unavailable.");
        bool clear = scope.Session.Reconcile(reconcile, cancellationToken);
        scope.ReconciledClear = clear;
        return clear;
    }

    public static void RunSynchronous(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RunOnCurrentThread(_ => { operation(); return true; }, DataOperationKind.Mutating, ProductionAcquire, ProductionMarkerClear, reconciliation, CancellationToken.None);
    }

    public static void RunReadSynchronous(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RunOnCurrentThread(_ => { operation(); return true; }, DataOperationKind.Read, ProductionAcquire, ProductionMarkerClear, reconciliation, CancellationToken.None);
    }

    /// <summary>For FactoryReset marker-authority steps only; the marker itself is validated by FactoryReset.</summary>
    public static T RunResetControlSynchronous<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RunOnCurrentThread(_ => operation(), DataOperationKind.ResetControl, ProductionAcquire, ProductionMarkerClear, reconciliation, CancellationToken.None);
    }

    public static Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        RunAsync(async token => { await operation(token).ConfigureAwait(false); return true; }, cancellationToken);

    public static Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        RunOnDedicatedThreadAsync(operation, DataOperationKind.Mutating, cancellationToken);

    public static Task<T> RunReadAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        RunOnDedicatedThreadAsync(operation, DataOperationKind.Read, cancellationToken);

    /// <summary>
    /// FR-UPD-15, TM-04/13; Chocolatey Confirm design §2 C7: the <see cref="ReviewedUpdateGateRunner"/> of the reviewed dispatch
    /// workflow. The work runs as a mutating data operation on its own thread: the common current-user gate is acquired without
    /// waiting (another update, batch, scan or data operation refuses it), unfinished dispatches are reconciled first and any
    /// unresolved barrier or pending reset refuses it, and the reconciled ownership is passed to the work. Work is never started
    /// after <paramref name="reviewDeadlineUtc"/>; started work is never cancelled by the deadline, so it reaches its recorded result.
    /// </summary>
    public static Task<ReviewedUpdateDispatchResult> RunReviewedUpdateAsync(
        Func<OperationOwnership, CancellationToken, Task<ReviewedUpdateDispatchResult>> work, DateTimeOffset reviewDeadlineUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        Func<OperationOwnership, CancellationToken, Task<bool>>? reconcile = reconciliation;
        return Task.Factory.StartNew(() => RunOnCurrentThread(ownership =>
            {
                if (DateTimeOffset.UtcNow > reviewDeadlineUtc)
                    throw new InvalidOperationException("The review expired before the update could start; no update was started.");
                return work(ownership, cancellationToken).GetAwaiter().GetResult();
            }, DataOperationKind.Mutating, ProductionAcquire, ProductionMarkerClear, reconcile, cancellationToken),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static Task<T> RunOnDedicatedThreadAsync<T>(Func<CancellationToken, Task<T>> operation, DataOperationKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Func<OperationOwnership, CancellationToken, Task<bool>>? reconcile = reconciliation;
        // Native ownership is thread-affine: acquire, await and release on one dedicated thread.
        return Task.Factory.StartNew(() => RunOnCurrentThread(_ => operation(cancellationToken).GetAwaiter().GetResult(), kind,
            ProductionAcquire, ProductionMarkerClear, reconcile, cancellationToken), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private static CurrentUserDataOperationSession? ProductionAcquire(TimeSpan wait, CancellationToken ct) => CurrentUserDataOperationSession.TryAcquire(wait, ct);
    private static bool ProductionMarkerClear() => FactoryReset.GetMarkerState() == FactoryResetMarkerState.None;

    internal static T RunOnCurrentThread<T>(Func<OperationOwnership, T> work, DataOperationKind kind,
        Func<TimeSpan, CancellationToken, CurrentUserDataOperationSession?> acquire, Func<bool> markerClear,
        Func<OperationOwnership, CancellationToken, Task<bool>>? reconcile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ambient is { } scope)
        {
            // Reuse the bootstrap session explicitly (the session also refuses nested acquisition on its thread).
            if (kind != DataOperationKind.ResetControl && !markerClear()) throw ResetPending();
            if (kind == DataOperationKind.Mutating && scope.ReconciledClear != true) throw Unresolved();
            return work(scope.Session.Context);
        }
        CurrentUserDataOperationSession? acquired;
        try { acquired = acquire(TimeSpan.Zero, cancellationToken); }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or UnauthorizedAccessException or IOException)
        {
            // An untrusted or unopenable gate fails closed with the same handled refusal as contention.
            throw new InvalidOperationException("The PC Steward operation gate is unavailable; no state was opened or changed.", exception);
        }
        using CurrentUserDataOperationSession session = acquired
            ?? throw new InvalidOperationException("Another PC Steward data operation is still active.");
        if (kind != DataOperationKind.ResetControl)
        {
            if (!markerClear()) throw ResetPending();
            if (reconcile is null) throw new InvalidOperationException("Operation reconciliation is not configured; state access is unavailable.");
            bool clear;
            try { clear = session.Reconcile(reconcile, cancellationToken); }
            catch (Exception exception) when (kind == DataOperationKind.Read && exception is not OperationCanceledException and not OutOfMemoryException)
            {
                clear = false; // Unreadable barrier state never clears mutation, but reads still report what they can.
            }
            if (kind == DataOperationKind.Mutating && !clear) throw Unresolved();
            if (!markerClear()) throw ResetPending();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return work(session.Context);
    }

    internal static IDisposable EnterBootstrapForTesting(CurrentUserDataOperationSession session, bool reconciledClear)
    {
        IDisposable scope = EnterBootstrap(session);
        ambient!.ReconciledClear = reconciledClear;
        return scope;
    }

    private static InvalidOperationException ResetPending() => new("Factory reset recovery is pending; PC Steward will not open or change prior state.");
    private static InvalidOperationException Unresolved() => new("An unresolved update requires explicit safe reconciliation before PC Steward changes local state.");

    private sealed class BootstrapScope(CurrentUserDataOperationSession session) : IDisposable
    {
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        internal CurrentUserDataOperationSession Session { get; } = session;
        internal bool? ReconciledClear { get; set; }

        public void Dispose()
        {
            if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("The bootstrap scope must end on its owning thread.");
            if (ReferenceEquals(ambient, this)) ambient = null;
        }
    }
}
