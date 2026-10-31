namespace PCSteward.Application;

/// <summary>Read/recovery admission callback. It never grants ownership, recreates approval or replays dispatch.</summary>
public sealed class UpdateDispatchReconciliation(IUpdateDispatchStore store, TimeProvider? clock = null)
{
    private readonly IUpdateDispatchStore store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    public async Task<bool> ReconcileAsync(OperationOwnership ownership, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!ownership.CanReconcile) throw new InvalidOperationException("Reconciliation requires current production-gate ownership.");
        await store.RecoverUnfinishedDispatchesAsync(ownership, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return !await store.HasUnresolvedDispatchesAsync(ownership, cancellationToken).ConfigureAwait(false);
    }
}
