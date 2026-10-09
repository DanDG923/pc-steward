using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Reviewed user catalog additions (FR-CAT-06). Rows are untrusted and revalidated on every read.</summary>
public interface IUserCatalogStore
{
    Task<IReadOnlyCollection<UserCatalogEntryRecord>> GetUserCatalogEntriesAsync(CancellationToken cancellationToken);
    Task SaveUserCatalogEntryAsync(UserCatalogEntryRecord entry, DateTimeOffset createdAt, CancellationToken cancellationToken);
    Task<bool> DeleteUserCatalogEntryAsync(string entryId, CancellationToken cancellationToken);
}

/// <summary>FR-CAT-04 / §27.3 catalog states.</summary>
public enum CatalogAppState
{
    NotInstalled,
    UpToDate,
    UpdateAvailable,
    ReviewRequired,
    InstalledNotManaged,
    Unavailable,
    Skipped,
    Pinned,
    Ignored
}

public sealed record CatalogProductMatch(string StableProductKey, CatalogDetectionResult Detection, DeclaredPackageMapping? Mapping);

public sealed record CatalogEntryStatus(CatalogEntry Entry, CatalogAppState State, IReadOnlyList<string> ProductKeys);

public sealed record CatalogOverview(string CatalogVersion, IReadOnlyList<CatalogEntryStatus> Entries, IReadOnlyList<string> RejectedUserEntries);

/// <summary>
/// Turns deterministic catalog detection into declared <see cref="MappingOrigin.Catalog"/> mappings and
/// catalog states (FR-CAT-04/05, FR-INS-06, ADR-023/024). Only Exact detection creates a mapping, and the
/// mapping still has to pass the normal trusted-source validation in <see cref="ProductPackageMatching"/>.
/// </summary>
public static class PersonalCatalogMatching
{
    public const string WinGetSourceId = "winget";
    public const string ChocolateySourceId = "chocolatey-community";

    public static IReadOnlyList<CatalogProductMatch> Match(
        PersonalCatalog catalog,
        IReadOnlyCollection<CanonicalProduct> products,
        IReadOnlyCollection<InstallationInstance> instances,
        IReadOnlyDictionary<string, ProductUpdatePolicy>? policies = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(instances);
        CatalogDetector detector = new(catalog);
        Dictionary<string, CatalogEntry> entries = catalog.Entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);
        ILookup<Guid, InstallationInstance> byProduct = instances.ToLookup(instance => instance.ProductId);
        List<CatalogProductMatch> matches = [];
        foreach (CanonicalProduct product in products.OrderBy(product => product.StableKey, StringComparer.Ordinal))
        {
            InstallationInstance[] productInstances = [.. byProduct[product.Id]];
            CatalogDetectionResult[] detections = productInstances.Length == 0
                ? [detector.Detect(new(product.DisplayName, null, "unknown", null, null))]
                : [.. productInstances.Select(instance => detector.Detect(Record(product, instance)))];
            CatalogDetectionResult combined = Combine(detections);
            if (combined.Kind == CatalogDetectionKind.None) continue;

            DeclaredPackageMapping? mapping = null;
            if (combined.IsExact)
            {
                CatalogEntry entry = entries[combined.EntryIds[0]];
                ProductUpdatePolicy policy = policies?.GetValueOrDefault(product.StableKey) ?? new();
                CatalogProviderIdentity? provider = combined.Kind == CatalogDetectionKind.ProviderIdentity
                    ? IdentityProvider(entry, productInstances)
                    : PreferredOrDefault(entry, policy.PreferredProvider);
                if (provider is not null)
                {
                    mapping = new(product.StableKey, SourceId(provider), provider.PackageId, MappingOrigin.Catalog);
                }
            }

            matches.Add(new(product.StableKey, combined, mapping));
        }

        return matches;
    }

    /// <summary>
    /// Catalog states. Several matched products take the most attention-worthy state, in this order:
    /// UpdateAvailable, ReviewRequired, Unavailable, InstalledNotManaged, Skipped, Pinned, Ignored, UpToDate.
    /// </summary>
    public static IReadOnlyList<CatalogEntryStatus> Status(PersonalCatalog catalog, IReadOnlyCollection<CatalogProductMatch> matches, UpdateIntelligenceSummary intelligence)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(intelligence);
        Dictionary<string, UpdateIntelligenceItem> items = intelligence.Items.ToDictionary(item => item.StableProductKey, StringComparer.Ordinal);
        List<CatalogEntryStatus> statuses = [];
        foreach (CatalogEntry entry in catalog.Entries)
        {
            CatalogProductMatch[] related = [.. matches.Where(match => match.Detection.EntryIds.Contains(entry.Id, StringComparer.Ordinal))];
            string[] keys = [.. related.Select(match => match.StableProductKey)];
            CatalogAppState[] states = [.. related.Select(match => match.Detection.IsExact
                ? StateFor(items.GetValueOrDefault(match.StableProductKey))
                : CatalogAppState.ReviewRequired)];
            CatalogAppState state = states.Length == 0 ? CatalogAppState.NotInstalled : states.MinBy(Priority);
            statuses.Add(new(entry, state, keys));
        }

        return statuses;
    }

    internal static CatalogDetectionResult Combine(IReadOnlyCollection<CatalogDetectionResult> detections)
    {
        CatalogDetectionResult[] ambiguous = [.. detections.Where(detection => detection.Kind == CatalogDetectionKind.Ambiguous)];
        string[] identity = [.. detections.Where(detection => detection.Kind == CatalogDetectionKind.ProviderIdentity).SelectMany(detection => detection.EntryIds).Distinct(StringComparer.Ordinal)];
        string[] namePublisher = [.. detections.Where(detection => detection.Kind == CatalogDetectionKind.NameAndPublisher).SelectMany(detection => detection.EntryIds).Distinct(StringComparer.Ordinal)];
        string[] nameOnly = [.. detections.Where(detection => detection.Kind == CatalogDetectionKind.NameOnly).SelectMany(detection => detection.EntryIds).Distinct(StringComparer.Ordinal)];
        string[] every = [.. identity.Concat(namePublisher).Concat(nameOnly).Concat(ambiguous.SelectMany(detection => detection.EntryIds)).Distinct(StringComparer.Ordinal)];
        if (ambiguous.Length != 0 || identity.Length > 1 || (identity.Length == 0 && namePublisher.Length > 1)) return new(CatalogDetectionKind.Ambiguous, every);
        // An identity match that another instance names as a different entry is a conflict, not a choice.
        if (identity.Length == 1 && every.Length > 1) return new(CatalogDetectionKind.Ambiguous, every);
        if (identity.Length == 1) return new(CatalogDetectionKind.ProviderIdentity, identity);
        if (namePublisher.Length == 1 && every.Length == 1) return new(CatalogDetectionKind.NameAndPublisher, namePublisher);
        if (namePublisher.Length == 1) return new(CatalogDetectionKind.Ambiguous, every);
        if (nameOnly.Length == 1) return new(CatalogDetectionKind.NameOnly, nameOnly);
        return nameOnly.Length > 1 ? new(CatalogDetectionKind.Ambiguous, nameOnly) : CatalogDetectionResult.NoMatch;
    }

    // Persistence stores a missing publisher as an empty string; treat it as missing so a name match stays
    // NameOnly (review required, and an install blocker) instead of silently becoming "not detected".
    private static CatalogInstalledRecord Record(CanonicalProduct product, InstallationInstance instance) => new(
        product.DisplayName,
        string.IsNullOrWhiteSpace(instance.Publisher) ? null : instance.Publisher,
        instance.Provider,
        instance.ProviderIdentity,
        instance.SourceId,
        string.Equals(instance.Provider, "msix", StringComparison.OrdinalIgnoreCase) ? product.StableKey : null);

    private static CatalogProviderIdentity? IdentityProvider(CatalogEntry entry, IReadOnlyCollection<InstallationInstance> instances)
    {
        // ADR-024: an installed app keeps the provider that manages it.
        foreach (CatalogProviderIdentity provider in entry.Providers)
        {
            bool managed = instances.Any(instance => provider.Provider switch
            {
                CatalogProviderKind.WinGet => string.Equals(instance.Provider, "winget", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(instance.SourceId, WinGetSourceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(instance.ProviderIdentity, provider.PackageId, StringComparison.OrdinalIgnoreCase),
                CatalogProviderKind.Chocolatey => string.Equals(instance.Provider, "chocolatey", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(instance.ProviderIdentity, provider.PackageId, StringComparison.OrdinalIgnoreCase),
                _ => false
            });
            if (managed) return provider;
        }

        // MSIX family detection names no managing provider; use the catalog default.
        return entry.DefaultProvider;
    }

    private static CatalogProviderIdentity PreferredOrDefault(CatalogEntry entry, string? preferred)
    {
        CatalogProviderKind? kind = preferred switch { "winget" => CatalogProviderKind.WinGet, "chocolatey" => CatalogProviderKind.Chocolatey, _ => null };
        return entry.Providers.FirstOrDefault(provider => provider.Provider == kind) ?? entry.DefaultProvider;
    }

    private static string SourceId(CatalogProviderIdentity provider) => provider.Source switch
    {
        CatalogSource.WinGet => WinGetSourceId,
        CatalogSource.ChocolateyCommunity => ChocolateySourceId,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static CatalogAppState StateFor(UpdateIntelligenceItem? item) => item?.Status switch
    {
        UpdateIntelligenceStatus.VerifiedUpdate => CatalogAppState.UpdateAvailable,
        UpdateIntelligenceStatus.Current => CatalogAppState.UpToDate,
        UpdateIntelligenceStatus.ReviewRequired => CatalogAppState.ReviewRequired,
        UpdateIntelligenceStatus.FailedUnavailable => CatalogAppState.Unavailable,
        UpdateIntelligenceStatus.Skipped => CatalogAppState.Skipped,
        UpdateIntelligenceStatus.Pinned => CatalogAppState.Pinned,
        UpdateIntelligenceStatus.Ignored => CatalogAppState.Ignored,
        _ => CatalogAppState.InstalledNotManaged
    };

    private static int Priority(CatalogAppState state) => state switch
    {
        CatalogAppState.UpdateAvailable => 0,
        CatalogAppState.ReviewRequired => 1,
        CatalogAppState.Unavailable => 2,
        CatalogAppState.InstalledNotManaged => 3,
        CatalogAppState.Skipped => 4,
        CatalogAppState.Pinned => 5,
        CatalogAppState.Ignored => 6,
        CatalogAppState.UpToDate => 7,
        _ => 8
    };
}
