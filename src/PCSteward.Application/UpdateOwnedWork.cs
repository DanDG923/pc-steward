namespace PCSteward.Application;

public sealed class UpdateExecutionLifetime
{
    private readonly object sync = new();
    private object? active;
    private Task? pending;
    private Task? observer;
    internal Task OwnedCompletion { get { lock (sync) return pending ?? Task.CompletedTask; } }

    public UpdateExecutionLease? TryAcquire()
    {
        lock (sync)
        {
            ClearCompleted();
            if (active is not null || pending is not null) return null;
            active = new object();
            return new(this, active);
        }
    }

    internal void Track(UpdateExecutionLease lease, Task completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(!ReferenceEquals(active, lease.Token), lease);
            ClearCompleted();
            if (pending is not null && !ReferenceEquals(pending, completion))
                throw new InvalidOperationException("An admission retains one combined owned-completion root.");
            pending = completion;
            observer = completion.ContinueWith(static (task, state) =>
                ((UpdateExecutionLifetime)state!).ObserveCompletion(task), this,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    internal void Release(UpdateExecutionLease lease)
    {
        lock (sync)
        {
            if (ReferenceEquals(active, lease.Token)) active = null;
            ClearCompleted();
        }
    }

    private void ObserveCompletion(Task task)
    {
        if (task.IsFaulted) _ = task.Exception;
        lock (sync)
        {
            if (ReferenceEquals(pending, task)) ClearCompleted();
        }
    }

    private void ClearCompleted()
    {
        if (pending is not { IsCompleted: true }) return;
        if (pending.IsFaulted) _ = pending.Exception;
        pending = null;
        if (observer is { IsFaulted: true }) _ = observer.Exception;
        observer = null;
    }
}

public sealed class UpdateExecutionLease : IDisposable
{
    private readonly UpdateExecutionLifetime owner;
    private int disposed;
    internal UpdateExecutionLease(UpdateExecutionLifetime owner, object token) { this.owner = owner; Token = token; }
    internal object Token { get; }
    public void TrackCompletion(Task completion)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        owner.Track(this, completion);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0) owner.Release(this);
        GC.SuppressFinalize(this);
    }
}
