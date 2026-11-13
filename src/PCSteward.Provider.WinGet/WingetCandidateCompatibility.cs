using Microsoft.Management.Deployment;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// The install scope, architecture and release channel of one WinGet update, read from WinGet's structured COM API so that
/// update intelligence can compare them with the installed app (<c>ProductPackageMatching</c> compatibility reasons). The
/// display table names none of them. This is comparison evidence for the match only: it never selects what runs. The
/// reviewed job still binds the installed app's own scope and architecture, the reviewed routes check the installer again
/// at review, and the post-update check needs the exact version, scope and architecture.
/// <para><see cref="Publisher"/> is the catalog publisher of the update, so a same-name, same-publisher installed app can be matched
/// (owner decision 2026-11-13). <see cref="LinkedProductKey"/> is the product key of the one Windows uninstall entry that WinGet
/// itself links to the installed package (its product code), so that entry is matched to the package without a name guess. For an
/// MSIX app it is instead the product key of the one MSIX package family WinGet links to the installed package (MSIX design note §3.1),
/// so the app's own MSIX row is matched the same way.</para>
/// </summary>
internal sealed record WingetCandidateCompatibility(string Scope, string Architecture, string Channel, string? Publisher = null, string? LinkedProductKey = null);

/// <summary>What WinGet's structured API reported for one exact package ID. Plain values only, so the rules are testable.</summary>
internal sealed record WinGetCandidateCompatibilityObservation(
    string RequestedPackageId,
    bool CatalogIsPredefined,
    bool CatalogIsTrusted,
    bool ExactIdMatch,
    int MatchCount,
    bool WasLimitExceeded,
    string PackageId,
    string InstalledVersion,
    string CandidateVersion,
    string CandidateSourceId,
    bool IsUpdateAvailable,
    string? InstalledScope,
    bool HasApplicableInstaller,
    string InstallerScope,
    string InstallerArchitecture,
    string? Channel,
    string? CandidatePublisher = null,
    IReadOnlyList<string>? InstalledProductCodes = null,
    IReadOnlyList<string>? InstalledPackageFamilyNames = null,
    string? InstallerType = null);

internal interface IWinGetCandidateCompatibilityQuery
{
    /// <summary>Queries each package ID once against the one predefined catalog. A package that cannot be read is left out.</summary>
    Task<IReadOnlyList<WinGetCandidateCompatibilityObservation>> QueryAsync(
        string sourceId,
        IReadOnlyList<string> packageIds,
        CancellationToken cancellationToken);
}

internal static class WingetCandidateCompatibilityRules
{
    private const int MaximumVersionLength = 128;
    private const int MaximumTextLength = 255;

    /// <summary>
    /// Returns the compatibility evidence for one display-table update row, or null when WinGet's structured answer is not
    /// the same exact, trusted, single update with the same installed and available versions.
    /// <list type="bullet">
    /// <item>Scope: the scope of the installer WinGet would choose for this installed scope. When that installer does not say
    /// (scope Unknown), WinGet keeps the installed scope, so the scope WinGet recorded for the installed app is used; when that
    /// is unknown too, the scope stays Unknown.</item>
    /// <item>Architecture: the chosen installer's architecture. Neutral and anything else that isn't X86, X64 or Arm64 stays
    /// as reported or Unknown, so it can never silently match an installed architecture.</item>
    /// <item>Channel: WinGet's default channel is an empty channel, reported as Stable. Any named channel stays Unknown.</item>
    /// </list>
    /// </summary>
    public static WingetCandidateCompatibility? Evaluate(
        WinGetCandidateCompatibilityObservation? observation,
        string sourceId,
        string packageId,
        string installedVersion,
        string availableVersion)
    {
        if (observation is null ||
            !observation.CatalogIsPredefined ||
            !observation.CatalogIsTrusted ||
            !observation.ExactIdMatch ||
            observation.WasLimitExceeded ||
            observation.MatchCount != 1 ||
            !observation.IsUpdateAvailable ||
            !observation.HasApplicableInstaller ||
            !string.Equals(observation.RequestedPackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.PackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.CandidateSourceId, sourceId, StringComparison.OrdinalIgnoreCase) ||
            !IsBoundedVersion(observation.InstalledVersion) ||
            !IsBoundedVersion(observation.CandidateVersion) ||
            !string.Equals(observation.InstalledVersion, installedVersion, StringComparison.Ordinal) ||
            !string.Equals(observation.CandidateVersion, availableVersion, StringComparison.Ordinal))
        {
            return null;
        }

        string installerScope = Scope(observation.InstallerScope);
        string scope = installerScope != "Unknown" ? installerScope : Scope(observation.InstalledScope);
        // MSIX design note §3.2: WinGet records no installed scope for an MSIX app, and an MSIX installer that names no scope is
        // registered for the account that runs it. So an update WinGet links to exactly one installed package family, through an MSIX
        // installer with no scope of its own, is a per-user update.
        if (scope == "Unknown" && installerScope == "Unknown" && IsMsixInstaller(observation.InstallerType) &&
            MsixProductKey(observation.InstalledPackageFamilyNames) is not null && observation.InstalledProductCodes is not { Count: > 0 })
        {
            scope = "User";
        }
        return new(
            scope,
            Architecture(observation.InstallerArchitecture),
            string.IsNullOrEmpty(observation.Channel) ? "Stable" : "Unknown",
            BoundedText(observation.CandidatePublisher),
            InstalledProductKey(observation.InstalledProductCodes, observation.InstalledPackageFamilyNames));
    }

    /// <summary>
    /// The one local product WinGet links to the installed package: the registry product of its single product code when it reports
    /// no package family, or the MSIX product of its single package family when it reports no product code (MSIX design note §3.1).
    /// Both kinds of link at once, or anything malformed, give no key.
    /// </summary>
    internal static string? InstalledProductKey(IReadOnlyList<string>? productCodes, IReadOnlyList<string>? packageFamilyNames)
    {
        bool hasFamilies = packageFamilyNames is { Count: > 0 };
        bool hasCodes = productCodes is { Count: > 0 };
        if (hasFamilies && hasCodes) return null;
        return hasFamilies ? MsixProductKey(packageFamilyNames) : RegistryProductKey(productCodes);
    }

    /// <summary>
    /// The MSIX product key (<see cref="InventoryNormalization.ProductFingerprint"/> of provider <c>msix</c> and the package family
    /// name, which is how the MSIX inventory names a package) for WinGet's single installed package family. None, several or a
    /// family name outside the Windows format give no key.
    /// </summary>
    internal static string? MsixProductKey(IReadOnlyList<string>? packageFamilyNames)
    {
        if (packageFamilyNames is null || packageFamilyNames.Any(name => !PackageFamilyName.IsValid(name))) return null;
        string[] names = packageFamilyNames.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length != 1) return null;
        return InventoryNormalization.ProductFingerprint(new RawInstallEvidence("msix", names[0], names[0], null, null, "Unknown", "Unknown", null, "Application", "winget-com"));
    }

    /// <summary>
    /// The registry product key (<see cref="InventoryNormalization.ProductFingerprint"/> of provider <c>registry</c> and the uninstall
    /// key name) for WinGet's single installed product code. None, several or an unbounded code give no key.
    /// </summary>
    internal static string? RegistryProductKey(IReadOnlyList<string>? productCodes)
    {
        if (productCodes is null || productCodes.Any(code => BoundedText(code) is null)) return null;
        string[] codes = productCodes.Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (codes.Length != 1) return null;
        return InventoryNormalization.ProductFingerprint(new RawInstallEvidence("registry", codes[0], codes[0], null, null, "Unknown", "Unknown", null, "Application", "winget-com"));
    }

    /// <summary>
    /// Reads each package on its own: a package whose read throws (for example a manifest WinGet can't load) is left out, and the
    /// packages before and after it keep their observations. Only cancellation stops the whole read.
    /// </summary>
    public static async Task<IReadOnlyList<WinGetCandidateCompatibilityObservation>> CollectAsync(
        IReadOnlyList<string> packageIds,
        Func<string, Task<WinGetCandidateCompatibilityObservation?>> readOne,
        CancellationToken cancellationToken)
    {
        List<WinGetCandidateCompatibilityObservation> observations = [];
        foreach (string packageId in packageIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await readOne(packageId).ConfigureAwait(false) is { } observation) observations.Add(observation);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return observations;
    }

    private static string? BoundedText(string? value)
    {
        string? trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > MaximumTextLength || trimmed.Any(char.IsControl) ? null : trimmed;
    }

    private static bool IsMsixInstaller(string? value) => value?.Trim().ToUpperInvariant() is "MSIX";

    private static string Scope(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "USER" => "User",
        "MACHINE" or "SYSTEM" => "Machine",
        _ => "Unknown"
    };

    private static string Architecture(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "X86" => "X86",
        "X64" => "X64",
        "ARM64" => "Arm64",
        "NEUTRAL" => "Neutral",
        _ => "Unknown"
    };

    private static bool IsBoundedVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumVersionLength &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '+');
}

/// <summary>
/// Reads candidate compatibility through the same composite catalog shape as <see cref="WinGetComStructuredCatalogQuery"/>:
/// one predefined catalog, connected once, then one exact-ID search per package.
/// </summary>
internal sealed class WinGetComCandidateCompatibilityQuery : IWinGetCandidateCompatibilityQuery
{
    public async Task<IReadOnlyList<WinGetCandidateCompatibilityObservation>> QueryAsync(
        string sourceId,
        IReadOnlyList<string> packageIds,
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
        bool isTrusted = referenceInfo.TrustLevel == PackageCatalogTrustLevel.Trusted;

        CreateCompositePackageCatalogOptions compositeOptions = new()
        {
            CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
            InstalledScope = PackageInstallScope.Any
        };
        compositeOptions.Catalogs.Add(reference);
        PackageCatalogReference compositeReference = manager.CreateCompositePackageCatalog(compositeOptions);
        ConnectResult connection = await compositeReference.ConnectAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (connection.Status != ConnectResultStatus.Ok) return [];

        PackageCatalog catalog = connection.PackageCatalog;
        return await WingetCandidateCompatibilityRules.CollectAsync(
            packageIds,
            packageId => ReadOneAsync(catalog, packageId, isExpectedPredefinedCatalog, isTrusted, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<WinGetCandidateCompatibilityObservation?> ReadOneAsync(
        PackageCatalog catalog,
        string packageId,
        bool isExpectedPredefinedCatalog,
        bool isTrusted,
        CancellationToken cancellationToken)
    {
        FindPackagesOptions options = new() { ResultLimit = 2 };
        options.Filters.Add(new PackageMatchFilter
        {
            Field = PackageMatchField.Id,
            Option = PackageFieldMatchOption.Equals,
            Value = packageId
        });
        FindPackagesResult result = await catalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != FindPackagesResultStatus.Ok) return null;

        MatchResult[] matches = [.. result.Matches];
        MatchResult? only = matches.Length == 1 ? matches[0] : null;
        CatalogPackage? package = only?.CatalogPackage;
        PackageVersionInfo? installed = package?.InstalledVersion;
        PackageVersionInfo? candidate = package?.DefaultInstallVersion;
        if (only is null || package is null || installed is null || candidate is null) return null;

        PackageMatchFilter criteria = only.MatchCriteria;
        bool exactIdMatch =
            criteria.Field == PackageMatchField.Id &&
            criteria.Option == PackageFieldMatchOption.Equals &&
            string.Equals(criteria.Value, packageId, StringComparison.OrdinalIgnoreCase);
        string installedScope = installed.GetMetadata(PackageVersionMetadataField.InstalledScope);
        PackageInstallScope requestedScope = installedScope?.Trim().ToUpperInvariant() switch
        {
            "USER" => PackageInstallScope.UserOrUnknown,
            "MACHINE" or "SYSTEM" => PackageInstallScope.SystemOrUnknown,
            _ => PackageInstallScope.Any
        };
        PackageInstallerInfo? installer = candidate.GetApplicableInstaller(new InstallOptions { PackageInstallScope = requestedScope });
        return new(
            packageId,
            isExpectedPredefinedCatalog,
            isTrusted,
            exactIdMatch,
            matches.Length,
            result.WasLimitExceeded,
            package.Id,
            installed.Version,
            candidate.Version,
            candidate.PackageCatalog?.Info?.Name ?? string.Empty,
            package.IsUpdateAvailable,
            installedScope,
            installer is not null,
            installer is null ? "Unknown" : installer.Scope.ToString(),
            installer is null ? "Unknown" : installer.Architecture.ToString(),
            candidate.Channel,
            candidate.Publisher,
            [.. installed.ProductCodes],
            [.. installed.PackageFamilyNames],
            installer is null ? null : installer.InstallerType.ToString());
    }
}
