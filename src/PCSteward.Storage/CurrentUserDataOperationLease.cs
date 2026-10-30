using PCSteward.Application;

namespace PCSteward.Storage;

public sealed class CurrentUserDataOperationSession : IDisposable
{
    internal const string LegacyMutexName = "Local\\PCSteward.DataOperation.v1";
    private readonly CurrentUserOperationGate gate;
    private readonly OperationGateOwnership globalOwnership;
    private readonly Mutex legacy;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool disposed;

    private CurrentUserDataOperationSession(CurrentUserOperationGate gate, OperationGateOwnership globalOwnership, Mutex legacy)
    {
        this.gate = gate;
        this.globalOwnership = globalOwnership;
        this.legacy = legacy;
    }
    public OperationOwnership Context => globalOwnership.Context;
    public static CurrentUserDataOperationSession? TryAcquire(TimeSpan wait, CancellationToken ct) => Acquire(CurrentUserOperationGate.Open(), LegacyMutexName, wait, ct);
    internal static CurrentUserDataOperationSession? TryAcquireForTesting(Guid testId, TimeSpan wait, CancellationToken ct) => Acquire(CurrentUserOperationGate.OpenForTesting(testId), LegacyNameForTesting(testId), wait, ct);
    internal static string LegacyNameForTesting(Guid testId) => testId == Guid.Empty ? throw new ArgumentException("A test identity is required.") : LegacyMutexName + ".test." + testId.ToString("N");

    private static CurrentUserDataOperationSession? Acquire(CurrentUserOperationGate gate, string legacyName, TimeSpan wait, CancellationToken ct)
    {
        OperationGateOwnership? ownership = null;
        Mutex? legacy = null;
        bool legacyHeld = false;
        bool transferred = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            ownership = gate.TryAcquire(wait, ct);
            if (ownership is null) return null;
            legacy = new(false, legacyName);
            try { legacyHeld = legacy.WaitOne(0); }
            catch (AbandonedMutexException) { legacyHeld = true; ownership.Context.RequireRecovery(); }
            if (!legacyHeld) return null;
            ct.ThrowIfCancellationRequested();
            CurrentUserDataOperationSession result = new(gate, ownership, legacy);
            transferred = true;
            return result;
        }
        finally
        {
            if (!transferred)
            {
                if (legacyHeld) legacy!.ReleaseMutex();
                legacy?.Dispose();
                ownership?.Dispose();
                gate.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != ownerThread) throw new InvalidOperationException("Native operation ownership must be released on the acquiring thread.");
        if (disposed) return;
        try { legacy.ReleaseMutex(); }
        finally
        {
            legacy.Dispose();
            globalOwnership.Dispose();
            gate.Dispose();
            disposed = true;
        }
    }

    public bool Reconcile(Func<OperationOwnership, CancellationToken, Task<bool>> reconcile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reconcile);
        if (Environment.CurrentManagedThreadId != ownerThread || disposed) throw new InvalidOperationException("Reconciliation requires the acquiring session owner.");
        ct.ThrowIfCancellationRequested();
        Context.RequireRecovery();
        bool clear = reconcile(Context, ct).GetAwaiter().GetResult();
        ct.ThrowIfCancellationRequested();
        if (clear) Context.MarkReconciled();
        return clear;
    }
}

public static class CurrentUserDataOperationLease
{
    public static Task<T> RunAsync<T>(Func<OperationOwnership, CancellationToken, Task<bool>> reconcile,
        Func<OperationOwnership, CancellationToken, Task<T>> operation, TimeSpan wait, DateTimeOffset? reviewDeadlineUtc, CancellationToken ct) =>
        RunCoreAsync(reconcile, operation, wait, reviewDeadlineUtc, TimeProvider.System, CurrentUserDataOperationSession.TryAcquire,
            () => FactoryReset.GetMarkerState() == FactoryResetMarkerState.None, ct);

    internal static Task<T> RunForTestingAsync<T>(Guid testId, Func<OperationOwnership, CancellationToken, Task<bool>> reconcile,
        Func<OperationOwnership, CancellationToken, Task<T>> operation, TimeSpan wait, DateTimeOffset? reviewDeadlineUtc, CancellationToken ct, TimeProvider? clock = null, Func<bool>? statePreflight = null) =>
        RunCoreAsync(reconcile, operation, wait, reviewDeadlineUtc, clock ?? TimeProvider.System, (timeout, token) => CurrentUserDataOperationSession.TryAcquireForTesting(testId, timeout, token), statePreflight ?? (() => true), ct);

    private static Task<T> RunCoreAsync<T>(Func<OperationOwnership, CancellationToken, Task<bool>> reconcile,
        Func<OperationOwnership, CancellationToken, Task<T>> operation, TimeSpan wait, DateTimeOffset? reviewDeadlineUtc,
        TimeProvider clock, Func<TimeSpan, CancellationToken, CurrentUserDataOperationSession?> acquire, Func<bool> statePreflight, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reconcile);
        ArgumentNullException.ThrowIfNull(operation);
        if (wait < TimeSpan.Zero || wait > TimeSpan.FromMinutes(30)) throw new ArgumentOutOfRangeException(nameof(wait));
        DateTimeOffset startedUtc = clock.GetUtcNow();
        long startedTimestamp = clock.GetTimestamp();
        TimeSpan? reviewRemaining = reviewDeadlineUtc - startedUtc;
        void CheckAdmission()
        {
            ct.ThrowIfCancellationRequested();
            DateTimeOffset now = clock.GetUtcNow();
            TimeSpan elapsed = clock.GetElapsedTime(startedTimestamp);
            if (now < startedUtc || elapsed < TimeSpan.Zero || (reviewDeadlineUtc.HasValue &&
                (now > reviewDeadlineUtc || elapsed > reviewRemaining)))
                throw new InvalidOperationException("Review expired or the clock changed while waiting; a fresh review is required.");
        }
        return Task.Factory.StartNew(() =>
        {
            CheckAdmission();
            TimeSpan boundedWait = wait;
            if (reviewDeadlineUtc.HasValue)
            {
                TimeSpan remaining = reviewDeadlineUtc.Value - clock.GetUtcNow();
                if (remaining < boundedWait) boundedWait = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
            using CurrentUserDataOperationSession session = acquire(boundedWait, ct) ?? throw new InvalidOperationException("Another PC Steward data operation is active.");
            CheckAdmission();
            if (!statePreflight()) throw new InvalidOperationException("Factory reset recovery is pending; ordinary state access is unavailable.");
            if (!session.Reconcile(reconcile, ct))
                throw new InvalidOperationException("An unresolved operation requires explicit safe reconciliation before further work.");
            CheckAdmission();
            if (!statePreflight()) throw new InvalidOperationException("Factory reset recovery began during reconciliation; ordinary state access is unavailable.");
            return operation(session.Context, ct).GetAwaiter().GetResult();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
