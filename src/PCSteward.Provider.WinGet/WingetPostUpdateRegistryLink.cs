using Microsoft.Management.Deployment;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>What WinGet's structured API reported, after an update, for the installed package of one exact ID.</summary>
internal sealed record WinGetInstalledLinkObservation(
    bool CatalogIsPredefined,
    bool CatalogIsTrusted,
    bool ExactIdMatch,
    int MatchCount,
    bool WasLimitExceeded,
    string PackageId,
    string InstalledVersion,
    IReadOnlyList<string> InstalledProductCodes);

internal interface IWinGetInstalledLinkQuery
{
    Task<WinGetInstalledLinkObservation?> QueryAsync(string sourceId, string packageId, CancellationToken cancellationToken);
}

/// <summary>
/// The post-update half of WinGet's own link between a package and a Windows uninstall entry. An MSI upgrade (GitHub CLI's WiX
/// installer, owner's PC 2026-11-13) replaces the uninstall entry with a new one under a new product code, so the product the
/// update was reviewed for is gone and a new registry product appears. This names that new product: the registry product key of
/// the single product code WinGet now reports for the exact package at exactly the target version. It adds no authority of its
/// own: the post-update check still needs WinGet's exact export and exactly one registry instance with the target version, scope
/// and architecture.
/// </summary>
public sealed class WingetPostUpdateRegistryLink
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private readonly IWinGetInstalledLinkQuery query;
    private readonly TimeSpan timeout;

    public WingetPostUpdateRegistryLink() : this(new WinGetComInstalledLinkQuery(), DefaultTimeout) { }

    internal WingetPostUpdateRegistryLink(IWinGetInstalledLinkQuery query, TimeSpan timeout)
    {
        this.query = query ?? throw new ArgumentNullException(nameof(query));
        this.timeout = timeout;
    }

    public async Task<string?> ResolveAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Selection.Provider != UpdateProvider.WinGet) return null;
        string? sourceId = job.Selection.Source switch
        {
            UpdateSource.WinGet => "winget",
            UpdateSource.MicrosoftStore => "msstore",
            _ => null
        };
        if (sourceId is null) return null;
        try
        {
            using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(timeout);
            WinGetInstalledLinkObservation? observation = await Task.Run(() => query.QueryAsync(sourceId, job.Selection.PackageId, budget.Token), CancellationToken.None)
                .WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return Evaluate(observation, job.Selection.PackageId, job.Selection.CandidateVersion);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal static string? Evaluate(WinGetInstalledLinkObservation? observation, string packageId, string targetVersion) =>
        observation is { CatalogIsPredefined: true, CatalogIsTrusted: true, ExactIdMatch: true, MatchCount: 1, WasLimitExceeded: false } &&
        string.Equals(observation.PackageId, packageId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(observation.InstalledVersion, targetVersion, StringComparison.Ordinal)
            ? WingetCandidateCompatibilityRules.RegistryProductKey(observation.InstalledProductCodes)
            : null;
}

/// <summary>The same composite catalog shape as <see cref="WinGetComStructuredCatalogQuery"/>, reading only the installed side.</summary>
internal sealed class WinGetComInstalledLinkQuery : IWinGetInstalledLinkQuery
{
    public async Task<WinGetInstalledLinkObservation?> QueryAsync(string sourceId, string packageId, CancellationToken cancellationToken)
    {
        PackageManager manager = new();
        PredefinedPackageCatalog predefined = sourceId switch
        {
            "winget" => PredefinedPackageCatalog.OpenWindowsCatalog,
            "msstore" => PredefinedPackageCatalog.MicrosoftStore,
            _ => throw new InvalidOperationException("Unsupported source.")
        };

        PackageCatalogReference reference = manager.GetPredefinedPackageCatalog(predefined);
        PackageCatalogInfo referenceInfo = reference.Info;
        bool isExpectedPredefinedCatalog =
            referenceInfo.Origin == PackageCatalogOrigin.Predefined &&
            string.Equals(referenceInfo.Name, sourceId, StringComparison.OrdinalIgnoreCase);

        CreateCompositePackageCatalogOptions compositeOptions = new()
        {
            CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
            InstalledScope = PackageInstallScope.Any
        };
        compositeOptions.Catalogs.Add(reference);
        PackageCatalogReference compositeReference = manager.CreateCompositePackageCatalog(compositeOptions);
        ConnectResult connection = await compositeReference.ConnectAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (connection.Status != ConnectResultStatus.Ok) return null;

        FindPackagesOptions options = new() { ResultLimit = 2 };
        options.Filters.Add(new PackageMatchFilter
        {
            Field = PackageMatchField.Id,
            Option = PackageFieldMatchOption.Equals,
            Value = packageId
        });
        FindPackagesResult result = await connection.PackageCatalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != FindPackagesResultStatus.Ok) return null;

        MatchResult[] matches = [.. result.Matches];
        MatchResult? only = matches.Length == 1 ? matches[0] : null;
        PackageVersionInfo? installed = only?.CatalogPackage?.InstalledVersion;
        if (only is null || installed is null) return null;
        PackageMatchFilter criteria = only.MatchCriteria;
        bool exactIdMatch =
            criteria.Field == PackageMatchField.Id &&
            criteria.Option == PackageFieldMatchOption.Equals &&
            string.Equals(criteria.Value, packageId, StringComparison.OrdinalIgnoreCase);
        return new(
            isExpectedPredefinedCatalog,
            referenceInfo.TrustLevel == PackageCatalogTrustLevel.Trusted,
            exactIdMatch,
            matches.Length,
            result.WasLimitExceeded,
            only.CatalogPackage.Id,
            installed.Version,
            [.. installed.ProductCodes]);
    }
}
