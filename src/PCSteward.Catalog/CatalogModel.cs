namespace PCSteward.Catalog;

/// <summary>Provider kinds a catalog entry may name. Catalog identities are data, never execution authority (ADR-023).</summary>
public enum CatalogProviderKind { WinGet, Chocolatey }

/// <summary>Sources a catalog entry may name. `msstore` is refused until a source-agreement review exists (Task 45 §2.5).</summary>
public enum CatalogSource { WinGet, ChocolateyCommunity }

public enum CatalogScope { User, Machine }

public enum CatalogChannel { Stable, Preview }

public sealed class CatalogProviderIdentity
{
    internal CatalogProviderIdentity(CatalogProviderKind provider, CatalogSource source, string packageId, IReadOnlyList<CatalogScope> scopes, bool isDefault)
    {
        Provider = provider;
        Source = source;
        PackageId = packageId;
        Scopes = scopes;
        IsDefault = isDefault;
    }

    public CatalogProviderKind Provider { get; }
    public CatalogSource Source { get; }
    public string PackageId { get; }
    public IReadOnlyList<CatalogScope> Scopes { get; }
    public bool IsDefault { get; }
}

/// <summary>Deterministic detection evidence. Provider identity is always available through <see cref="CatalogEntry.Providers"/>.</summary>
public sealed class CatalogDetection
{
    internal CatalogDetection(string? arpDisplayNamePattern, string? arpPublisherPattern, string? msixFamilyName)
    {
        ArpDisplayNamePattern = arpDisplayNamePattern;
        ArpPublisherPattern = arpPublisherPattern;
        MsixFamilyName = msixFamilyName;
    }

    public string? ArpDisplayNamePattern { get; }
    public string? ArpPublisherPattern { get; }
    public string? MsixFamilyName { get; }
}

public sealed class CatalogEntry
{
    internal CatalogEntry(
        string id,
        string name,
        string publisher,
        string category,
        string? summary,
        Uri? homepage,
        IReadOnlyList<CatalogProviderIdentity> providers,
        CatalogDetection detection,
        IReadOnlyList<string> processNames,
        bool selfUpdating,
        bool restartLikely,
        CatalogChannel channel,
        bool automationAllowed,
        bool isUserAdded = false)
    {
        Id = id;
        Name = name;
        Publisher = publisher;
        Category = category;
        Summary = summary;
        Homepage = homepage;
        Providers = providers;
        Detection = detection;
        ProcessNames = processNames;
        SelfUpdating = selfUpdating;
        RestartLikely = restartLikely;
        Channel = channel;
        AutomationAllowed = automationAllowed;
        IsUserAdded = isUserAdded;
    }

    public string Id { get; }
    public string Name { get; }
    public string Publisher { get; }
    public string Category { get; }
    public string? Summary { get; }

    /// <summary>Display only. Never fetched or executed.</summary>
    public Uri? Homepage { get; }
    public IReadOnlyList<CatalogProviderIdentity> Providers { get; }
    public CatalogDetection Detection { get; }
    public IReadOnlyList<string> ProcessNames { get; }
    public bool SelfUpdating { get; }
    public bool RestartLikely { get; }
    public CatalogChannel Channel { get; }
    public bool AutomationAllowed { get; }

    /// <summary>True for an entry added through the reviewed Add-from-search flow (FR-CAT-06), not bundled.</summary>
    public bool IsUserAdded { get; }
    public CatalogProviderIdentity DefaultProvider => Providers.Single(provider => provider.IsDefault);
}

/// <summary>A validated, immutable catalog snapshot.</summary>
public sealed class PersonalCatalog
{
    internal PersonalCatalog(string catalogVersion, string sha256, IReadOnlyList<CatalogEntry> entries)
    {
        CatalogVersion = catalogVersion;
        Sha256 = sha256;
        Entries = entries;
    }

    public string CatalogVersion { get; }

    /// <summary>Lower-case hex SHA-256 of the exact catalog bytes that were validated.</summary>
    public string Sha256 { get; }
    public IReadOnlyList<CatalogEntry> Entries { get; }
}

/// <summary>Either a valid catalog or the stable error codes that disabled it (fail closed, FR-CAT-02).</summary>
public sealed class CatalogLoadResult
{
    private CatalogLoadResult(PersonalCatalog? catalog, string? sha256, IReadOnlyList<string> errors)
    {
        Catalog = catalog;
        Sha256 = sha256;
        Errors = errors;
    }

    public PersonalCatalog? Catalog { get; }
    public string? Sha256 { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool IsLoaded => Catalog is not null;

    internal static CatalogLoadResult Loaded(PersonalCatalog catalog) => new(catalog, catalog.Sha256, []);
    internal static CatalogLoadResult Disabled(string? sha256, IEnumerable<string> errors) => new(null, sha256, errors.Distinct(StringComparer.Ordinal).ToArray());
}
