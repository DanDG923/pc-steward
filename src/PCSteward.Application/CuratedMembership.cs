using PCSteward.Catalog;

namespace PCSteward.Application;

/// <summary>CRD-01: durable membership only; neither installation nor automatic execution authority.</summary>
public sealed record ManagedAppMembership(string CatalogEntryId, DateTimeOffset AddedAt);

/// <summary>Restrictive transactional persistence. Use the validated service boundary for catalog-facing mutations.</summary>
public interface ICuratedMembershipStore
{
    Task<IReadOnlyList<ManagedAppMembership>> GetManagedAppsAsync(CancellationToken cancellationToken);
    Task AddManagedAppAsync(string catalogEntryId, DateTimeOffset addedAt, CancellationToken cancellationToken);
    Task RemoveManagedAppAndDisableAutomaticAsync(string catalogEntryId, IReadOnlyList<string> uniquelyAssociatedPolicyKeys, CancellationToken cancellationToken);
}

/// <summary>A read revalidated against the composed catalog. Withheld rows remain visible, with no actionable identity.</summary>
public sealed record CuratedMembershipEntry(ManagedAppMembership Membership, CatalogEntry? Entry, string? WithheldReason)
{
    public bool IsValidated => Entry is not null && WithheldReason is null;
}

/// <summary>
/// CRD-01/04/11: membership orchestration. The catalog supplier must freshly validate bundled bytes and compose
/// current retained user rows on every call; null means catalog actions are disabled. No execution port is accepted.
/// Desktop composition supplies its existing data-operation lease outside this service (Task 12).
/// </summary>
public sealed class CuratedMembershipService
{
    private readonly (ICuratedMembershipStore Store, Func<CancellationToken, Task<PersonalCatalog?>> Catalog,
        IInventorySnapshotStore Inventory, Func<string, bool> Active, Func<DateTimeOffset> Clock) dependencies;

    public CuratedMembershipService(ICuratedMembershipStore store, Func<CancellationToken, Task<PersonalCatalog?>> getComposedCatalogAsync,
        IInventorySnapshotStore inventory, Func<string, bool> isInActiveWork, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(getComposedCatalogAsync);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(isInActiveWork);
        dependencies = (store, getComposedCatalogAsync, inventory, isInActiveWork, utcNow ?? (() => DateTimeOffset.UtcNow));
    }

    // Expected-red checkpoint: dependencies compile, but membership validation/mutation is deliberately inert.
    public async Task<IReadOnlyList<CuratedMembershipEntry>> GetManagedAppsAsync(CancellationToken cancellationToken)
    {
        _ = await dependencies.Catalog(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ManagedAppMembership> rows = await dependencies.Store.GetManagedAppsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Select(row => new CuratedMembershipEntry(row, null, "Membership validation is not implemented.")).ToArray();
    }

    public Task AddAsync(string catalogEntryId, CancellationToken cancellationToken) => ObserveAsync(catalogEntryId, cancellationToken);
    public Task RemoveAsync(string catalogEntryId, CancellationToken cancellationToken) => ObserveAsync(catalogEntryId, cancellationToken);

    private async Task ObserveAsync(string catalogEntryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = await dependencies.Catalog(cancellationToken).ConfigureAwait(false);
        _ = await dependencies.Inventory.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        _ = await dependencies.Inventory.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        _ = dependencies.Active(catalogEntryId);
        _ = dependencies.Clock();
        cancellationToken.ThrowIfCancellationRequested();
    }
}
