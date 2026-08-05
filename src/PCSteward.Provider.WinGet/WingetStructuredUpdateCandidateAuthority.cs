using Microsoft.Management.Deployment;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// Resolves executable update evidence from WinGet's official structured COM API.
/// Display-table output is deliberately outside this authority.
/// </summary>
public sealed class WingetStructuredUpdateCandidateAuthority : IStructuredUpdateCandidateAuthority
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);
    private const int MaximumIdentifierLength = 256;
    private const int MaximumVersionLength = 128;
    private readonly IWinGetStructuredCatalogQuery query;
    private readonly TimeSpan timeout;

    public WingetStructuredUpdateCandidateAuthority()
        : this(new WinGetComStructuredCatalogQuery(), DefaultTimeout)
    {
    }

    internal WingetStructuredUpdateCandidateAuthority(
        IWinGetStructuredCatalogQuery query,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        this.query = query;
        this.timeout = timeout;
    }

    public async Task<StructuredUpdateCandidateEvidence?> ResolveAsync(
        string sourceId,
        string packageId,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeSource(sourceId, out string normalizedSource) ||
            !IsBoundedIdentifier(packageId))
        {
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using CancellationTokenSource queryTimeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            queryTimeout.CancelAfter(timeout);
            Task<WinGetStructuredCatalogObservation?> queryTask = Task.Run(
                () => query.QueryAsync(normalizedSource, packageId, queryTimeout.Token),
                CancellationToken.None);
            WinGetStructuredCatalogObservation? observation = await queryTask
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);

            return IsExactTrustedUpdate(observation, normalizedSource, packageId)
                ? new StructuredUpdateCandidateEvidence(
                    normalizedSource,
                    observation!.PackageId,
                    observation.InstalledVersion,
                    observation.CandidateVersion)
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool IsExactTrustedUpdate(
        WinGetStructuredCatalogObservation? observation,
        string sourceId,
        string packageId)
    {
        if (observation is null ||
            !observation.CatalogIsPredefined ||
            !observation.CatalogIsTrusted ||
            !observation.ExactIdMatch ||
            observation.WasLimitExceeded ||
            observation.MatchCount != 1 ||
            !observation.IsUpdateAvailable ||
            !string.Equals(observation.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.CandidateSourceId, sourceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.PackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
            !IsBoundedVersion(observation.InstalledVersion) ||
            !IsBoundedVersion(observation.CandidateVersion))
        {
            return false;
        }

        return VersionComparison.Compare(
            new VersionComparisonRequest(
                observation.InstalledVersion,
                observation.CandidateVersion,
                false)).Outcome == VersionComparisonOutcome.Newer;
    }

    private static bool TryNormalizeSource(string sourceId, out string normalized)
    {
        if (string.Equals(sourceId, "winget", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "winget";
            return true;
        }

        if (string.Equals(sourceId, "msstore", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "msstore";
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static bool IsBoundedIdentifier(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumIdentifierLength &&
        value.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+');

    private static bool IsBoundedVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumVersionLength &&
        value.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+');
}

internal interface IWinGetStructuredCatalogQuery
{
    Task<WinGetStructuredCatalogObservation?> QueryAsync(
        string sourceId,
        string packageId,
        CancellationToken cancellationToken);
}

internal sealed record WinGetStructuredCatalogObservation(
    string SourceId,
    bool CatalogIsPredefined,
    bool CatalogIsTrusted,
    bool ExactIdMatch,
    int MatchCount,
    bool WasLimitExceeded,
    string PackageId,
    string InstalledVersion,
    string CandidateVersion,
    string CandidateSourceId,
    bool IsUpdateAvailable);

internal sealed class WinGetComStructuredCatalogQuery : IWinGetStructuredCatalogQuery
{
    public async Task<WinGetStructuredCatalogObservation?> QueryAsync(
        string sourceId,
        string packageId,
        CancellationToken cancellationToken)
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
        if (connection.Status != ConnectResultStatus.Ok)
        {
            return null;
        }

        PackageCatalog catalog = connection.PackageCatalog;
        FindPackagesOptions options = new()
        {
            ResultLimit = 2
        };
        options.Filters.Add(new PackageMatchFilter
        {
            Field = PackageMatchField.Id,
            Option = PackageFieldMatchOption.Equals,
            Value = packageId
        });

        FindPackagesResult result = await catalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != FindPackagesResultStatus.Ok)
        {
            return null;
        }

        MatchResult[] matches = [.. result.Matches];
        MatchResult? only = matches.Length == 1 ? matches[0] : null;
        CatalogPackage? package = only?.CatalogPackage;
        PackageVersionInfo? installed = package?.InstalledVersion;
        PackageVersionInfo? candidate = package?.DefaultInstallVersion;
        if (only is null || package is null || installed is null || candidate is null)
        {
            return new WinGetStructuredCatalogObservation(
                sourceId,
                isExpectedPredefinedCatalog,
                referenceInfo.TrustLevel == PackageCatalogTrustLevel.Trusted,
                false,
                matches.Length,
                result.WasLimitExceeded,
                package?.Id ?? string.Empty,
                installed?.Version ?? string.Empty,
                candidate?.Version ?? string.Empty,
                candidate?.PackageCatalog?.Info?.Name ?? string.Empty,
                package?.IsUpdateAvailable ?? false);
        }

        PackageMatchFilter criteria = only.MatchCriteria;
        bool exactIdMatch =
            criteria.Field == PackageMatchField.Id &&
            criteria.Option == PackageFieldMatchOption.Equals &&
            string.Equals(criteria.Value, packageId, StringComparison.OrdinalIgnoreCase);

        return new WinGetStructuredCatalogObservation(
            sourceId,
            isExpectedPredefinedCatalog,
            referenceInfo.TrustLevel == PackageCatalogTrustLevel.Trusted,
            exactIdMatch,
            matches.Length,
            result.WasLimitExceeded,
            package.Id,
            installed.Version,
            candidate.Version,
            candidate.PackageCatalog.Info.Name,
            package.IsUpdateAvailable);
    }
}
