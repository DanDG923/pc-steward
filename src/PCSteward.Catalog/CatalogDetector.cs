using System.Text.RegularExpressions;

namespace PCSteward.Catalog;

/// <summary>One installed instance as seen by catalog detection. Values come from the local inventory and are untrusted.</summary>
public sealed record CatalogInstalledRecord(
    string DisplayName,
    string? Publisher,
    string Provider,
    string? ProviderIdentity,
    string? SourceId,
    string? PackageFamilyName = null);

public enum CatalogDetectionKind
{
    /// <summary>No catalog entry matched.</summary>
    None,

    /// <summary>WinGet correlation from the `winget` source, Chocolatey registration, or an MSIX package family.</summary>
    ProviderIdentity,

    /// <summary>The anchored display-name and publisher patterns both matched.</summary>
    NameAndPublisher,

    /// <summary>The display name matched but publisher evidence is missing. Never Exact (FR-CAT-05).</summary>
    NameOnly,

    /// <summary>More than one entry matched with the strongest evidence. Never Exact.</summary>
    Ambiguous
}

public sealed record CatalogDetectionResult(CatalogDetectionKind Kind, IReadOnlyList<string> EntryIds)
{
    public static CatalogDetectionResult NoMatch { get; } = new(CatalogDetectionKind.None, []);

    /// <summary>Only provider identity or name-and-publisher evidence may become a catalog mapping.</summary>
    public bool IsExact => Kind is CatalogDetectionKind.ProviderIdentity or CatalogDetectionKind.NameAndPublisher;
}

/// <summary>
/// Deterministic catalog detection (FR-CAT-05, ADR-023). Provider identity is preferred; anchored
/// display-name patterns are a fallback that is Exact only with matching publisher evidence.
/// </summary>
public sealed class CatalogDetector
{
    private readonly (CatalogEntry Entry, Regex? Name, Regex? Publisher)[] entries;

    public CatalogDetector(PersonalCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        entries = [.. catalog.Entries.Select(entry => (
            entry,
            entry.Detection.ArpDisplayNamePattern is null ? null : CatalogPatterns.Compile(entry.Detection.ArpDisplayNamePattern),
            entry.Detection.ArpPublisherPattern is null ? null : CatalogPatterns.Compile(entry.Detection.ArpPublisherPattern)))];
    }

    public CatalogDetectionResult Detect(CatalogInstalledRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string[] byIdentity = [.. entries.Where(item => IdentityMatches(item.Entry, record)).Select(item => item.Entry.Id).Distinct(StringComparer.Ordinal)];
        if (byIdentity.Length == 1) return new(CatalogDetectionKind.ProviderIdentity, byIdentity);
        if (byIdentity.Length > 1) return new(CatalogDetectionKind.Ambiguous, byIdentity);

        List<(string Id, bool PublisherConfirmed)> byName = [];
        foreach ((CatalogEntry entry, Regex? name, Regex? publisher) in entries)
        {
            if (name is null || !CatalogPatterns.IsMatch(name, record.DisplayName)) continue;
            if (publisher is null || record.Publisher is null) { byName.Add((entry.Id, false)); continue; }
            if (CatalogPatterns.IsMatch(publisher, record.Publisher)) byName.Add((entry.Id, true));
        }

        if (byName.Count == 0) return CatalogDetectionResult.NoMatch;
        if (byName.Count > 1) return new(CatalogDetectionKind.Ambiguous, [.. byName.Select(item => item.Id)]);
        return new(byName[0].PublisherConfirmed ? CatalogDetectionKind.NameAndPublisher : CatalogDetectionKind.NameOnly, [byName[0].Id]);
    }

    private static bool IdentityMatches(CatalogEntry entry, CatalogInstalledRecord record)
    {
        if (string.Equals(record.Provider, "msix", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Detection.MsixFamilyName is not null &&
                string.Equals(entry.Detection.MsixFamilyName, record.PackageFamilyName, StringComparison.OrdinalIgnoreCase);
        }

        if (record.ProviderIdentity is null) return false;
        return entry.Providers.Any(provider => provider.Provider switch
        {
            // Only WinGet correlation from the trusted `winget` source is identity evidence; `msstore` and
            // unknown provenance are not (ADR-023, Task 45 §2.5).
            CatalogProviderKind.WinGet => string.Equals(record.Provider, "winget", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.SourceId, "winget", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(provider.PackageId, record.ProviderIdentity, StringComparison.OrdinalIgnoreCase),
            CatalogProviderKind.Chocolatey => string.Equals(record.Provider, "chocolatey", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(provider.PackageId, record.ProviderIdentity, StringComparison.OrdinalIgnoreCase),
            _ => false
        });
    }
}
