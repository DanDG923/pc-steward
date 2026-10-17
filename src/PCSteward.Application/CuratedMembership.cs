using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// CRD-01: durable membership only; neither installation nor automatic execution authority. A stored timestamp that
/// cannot be parsed is surfaced as <see cref="DateTimeOffset.MinValue"/> so the row stays visible but withheld.
/// </summary>
public sealed record ManagedAppMembership(string CatalogEntryId, DateTimeOffset AddedAt);

/// <summary>
/// CRD-04 / FR-AUT-01: the restrictive stable-decision marker keyed to a catalog-entry ID. Any stored marker, whatever
/// its timestamp or expiry, forces automatic updates Off for that exact key. It never enables anything.
/// </summary>
public static class CuratedAutomaticUpdateRestriction
{
    public const string Kind = "CuratedAutomaticUpdatesOff";
    public const string Value = "Off";
}

/// <summary>
/// Restrictive transactional persistence. Adding records the Off marker in the same transaction; removing deletes the
/// membership, records the Off marker and deletes active automatic-update On rows for the exact supplied keys only.
/// Callers outside the validated <see cref="CuratedMembershipService"/> must not treat stored rows as validated.
/// </summary>
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
    private const string WithheldReason = "Membership is malformed, reserved or absent from the current validated catalog. Management actions are withheld.";

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

    /// <summary>
    /// The read entry point for later read models (Task 4). Every stored row is returned; only rows that exactly name a
    /// manageable entry of a freshly validated catalog carry an <see cref="CuratedMembershipEntry.Entry"/>. Orphaned,
    /// malformed, reserved and rejected-user rows stay visible with a withheld reason.
    /// </summary>
    public async Task<IReadOnlyList<CuratedMembershipEntry>> GetManagedAppsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ManagedAppMembership> rows = await dependencies.Store.GetManagedAppsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PersonalCatalog? catalog = await dependencies.Catalog(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return rows.Select(row =>
        {
            CatalogEntry? entry = row.AddedAt == DateTimeOffset.MinValue ? null : FindManageableEntry(catalog, row.CatalogEntryId);
            return new CuratedMembershipEntry(row, entry, entry is null ? WithheldReason : null);
        }).ToArray();
    }

    /// <summary>Adds management only. It never installs, and the new membership defaults automatic updates Off.</summary>
    public async Task AddAsync(string catalogEntryId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogEntryId);
        _ = await ValidateMutationAsync(catalogEntryId, cancellationToken).ConfigureAwait(false);
        DateTimeOffset addedAt = dependencies.Clock();
        EnsureInactive(catalogEntryId, cancellationToken);
        // Last check precedes the write. No check follows the committed write: reporting cancellation after a
        // commit would misstate persisted state.
        await dependencies.Store.AddManagedAppAsync(catalogEntryId, addedAt, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes management only. It never uninstalls and never deletes history, pins, skips or ignores.</summary>
    public async Task RemoveAsync(string catalogEntryId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalogEntryId);
        PersonalCatalog catalog = await ValidateMutationAsync(catalogEntryId, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = await dependencies.Inventory.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyCollection<InstallationInstance> instances = await dependencies.Inventory.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        string[] policyKeys = UniquelyAssociatedPolicyKeys(catalog, products, instances, catalogEntryId);
        EnsureInactive(catalogEntryId, cancellationToken);
        await dependencies.Store.RemoveManagedAppAndDisableAutomaticAsync(catalogEntryId, policyKeys, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// CRD-04 / FR-AUT-01: policy mutation needs identity evidence on every instance of a uniquely keyed product. Each
    /// instance must detect as <see cref="CatalogDetectionKind.ProviderIdentity"/> naming only this entry. Name and
    /// publisher evidence, which catalog detection may still treat as Exact, and any contradictory provider identity
    /// disqualify the product. Caller-supplied keys are never accepted.
    /// </summary>
    private static string[] UniquelyAssociatedPolicyKeys(PersonalCatalog catalog, IReadOnlyCollection<CanonicalProduct> products,
        IReadOnlyCollection<InstallationInstance> instances, string catalogEntryId)
    {
        CatalogDetector detector = new(catalog);
        ILookup<Guid, InstallationInstance> byProduct = instances.ToLookup(instance => instance.ProductId);
        List<string> keys = [];
        foreach (IGrouping<string, CanonicalProduct> group in products.GroupBy(product => product.StableKey, StringComparer.Ordinal))
        {
            CanonicalProduct[] sameKey = [.. group];
            if (sameKey.Length != 1) continue;
            CanonicalProduct product = sameKey[0];
            InstallationInstance[] own = [.. byProduct[product.Id]];
            bool associated = own.Length != 0 && own.All(instance =>
            {
                CatalogDetectionResult detection = detector.Detect(new(product.DisplayName,
                    string.IsNullOrWhiteSpace(instance.Publisher) ? null : instance.Publisher,
                    instance.Provider, instance.ProviderIdentity, instance.SourceId,
                    string.Equals(instance.Provider, "msix", StringComparison.OrdinalIgnoreCase) ? product.StableKey : null));
                return detection.Kind == CatalogDetectionKind.ProviderIdentity &&
                    detection.EntryIds.SequenceEqual([catalogEntryId], StringComparer.Ordinal);
            });
            if (associated) keys.Add(group.Key);
        }

        return [.. keys];
    }

    private async Task<PersonalCatalog> ValidateMutationAsync(string catalogEntryId, CancellationToken cancellationToken)
    {
        EnsureInactive(catalogEntryId, cancellationToken);
        PersonalCatalog? catalog = await dependencies.Catalog(cancellationToken).ConfigureAwait(false);
        EnsureInactive(catalogEntryId, cancellationToken);
        if (catalog is null || FindManageableEntry(catalog, catalogEntryId) is null)
            throw new InvalidOperationException("Membership requires an exact manageable entry in the current validated catalog.");
        return catalog;
    }

    private void EnsureInactive(string catalogEntryId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (dependencies.Active(catalogEntryId)) throw new InvalidOperationException("Membership cannot change while this app participates in active work.");
    }

    // The provider-setup entry is reserved for reviewed setup and is never a managed app.
    private static CatalogEntry? FindManageableEntry(PersonalCatalog? catalog, string catalogEntryId) =>
        string.Equals(catalogEntryId, ChocolateySetupPreparation.CatalogEntryId, StringComparison.Ordinal) ? null :
        catalog?.Entries.SingleOrDefault(entry => string.Equals(entry.Id, catalogEntryId, StringComparison.Ordinal));
}
