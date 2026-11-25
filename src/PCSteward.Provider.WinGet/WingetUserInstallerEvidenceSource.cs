using Microsoft.Management.Deployment;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// D7 design §3 item 1: what WinGet's structured API reported for one per-user update: the catalog, the exact package, how many catalog
/// versions carry exactly the target version text, and the installer <see cref="PackageVersionInfo.GetApplicableInstaller"/> chose with
/// user scope and the reviewed architecture. The installer's type, nested type, scope and elevation requirement are the projected enum
/// names, so the rule is testable without WinGet. <see cref="InstalledType"/> is WinGet's own record of how the installed app was
/// installed (its <c>InstallerType</c> metadata, such as <c>msix</c> or <c>exe</c>), or null when WinGet didn't say.
/// </summary>
internal sealed record WinGetUserInstallerObservation(
    string RequestedPackageId,
    bool CatalogIsPredefined,
    bool CatalogIsTrusted,
    bool ExactIdMatch,
    int MatchCount,
    bool WasLimitExceeded,
    string PackageId,
    int TargetVersionMatches,
    string? InstallerType,
    string? NestedInstallerType,
    string? Scope,
    string? ElevationRequirement,
    string? InstalledType = null);

internal interface IWinGetUserInstallerQuery
{
    /// <summary>Reads the applicable per-user installer of <paramref name="selection"/>'s target; null when it can't.</summary>
    Task<WinGetUserInstallerObservation?> QueryAsync(UpdateJobSelection selection, CancellationToken cancellationToken);
}

/// <summary>The design §3 rules on plain values.</summary>
internal static class WingetUserInstallerRules
{
    /// <summary>
    /// The check for <paramref name="observation"/>: the trusted predefined <c>winget</c> catalog, one exact package with the selection's
    /// ID, exactly one catalog version with the target version text, and one applicable installer. An MSIX installer is bound as msix; a
    /// Store-type installer keeps the C4 evidence. A vendor type needs a declared user scope, unless WinGet places it itself (MSIX,
    /// portable, or a zip around a portable app), exactly as the command line's <c>--scope user</c> selects. Anything else is refused.
    /// </summary>
    internal static WingetUserInstallerCheck Evaluate(WinGetUserInstallerObservation? observation, UpdateJobSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        WingetUserInstallerCheck unreadable = WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnreadableMessage);
        if (observation is null || !observation.CatalogIsPredefined || !observation.CatalogIsTrusted || !observation.ExactIdMatch ||
            observation.MatchCount != 1 || observation.WasLimitExceeded || observation.TargetVersionMatches != 1 ||
            !string.Equals(observation.RequestedPackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.PackageId, selection.PackageId, StringComparison.OrdinalIgnoreCase) ||
            observation.InstallerType is null)
            return unreadable;
        // Owner's PC, 2026-11-25 (Claude, installed as an MSIX package): an upgrade only uses an installer of a type compatible with how
        // the app was installed. The review would otherwise bind an installer WinGet then refuses (0x8A15002B, no applicable update).
        if (!IsCompatibleWithInstalledType(observation.InstalledType, observation.InstallerType, observation.NestedInstallerType))
            return WingetUserInstallerCheck.Refused(WingetUserInstallerText.DifferentInstallTechnologyMessage);
        if (observation.InstallerType is "MSStore") return WingetUserInstallerCheck.Msix();
        WinGetUserInstallerType? type = observation.InstallerType switch
        {
            "Msi" => WinGetUserInstallerType.Msi,
            "Wix" => WinGetUserInstallerType.Wix,
            "Exe" => WinGetUserInstallerType.Exe,
            "Inno" => WinGetUserInstallerType.Inno,
            "Nullsoft" => WinGetUserInstallerType.Nullsoft,
            "Burn" => WinGetUserInstallerType.Burn,
            "Portable" => WinGetUserInstallerType.Portable,
            "Zip" => WinGetUserInstallerType.Zip,
            "Msix" => WinGetUserInstallerType.Msix,
            _ => null,
        };
        if (type is not { } bound) return WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnsupportedTypeMessage);
        bool placedByWinGet = bound is WinGetUserInstallerType.Portable or WinGetUserInstallerType.Msix ||
            (bound == WinGetUserInstallerType.Zip && observation.NestedInstallerType == "Portable");
        if (bound == WinGetUserInstallerType.Zip && observation.NestedInstallerType is "Msix" or "MSStore" or "Font" or "Unknown" or null)
            return WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnsupportedTypeMessage);
        if (observation.Scope != "User" && !placedByWinGet) return unreadable;
        WinGetUserElevation? elevation = observation.ElevationRequirement switch
        {
            "Unknown" => WinGetUserElevation.Unknown,
            "ElevationProhibited" => WinGetUserElevation.Prohibited,
            "ElevationRequired" => WinGetUserElevation.Required,
            "ElevatesSelf" => WinGetUserElevation.ElevatesSelf,
            _ => null,
        };
        return elevation is { } read ? WingetUserInstallerCheck.Vendor(new WinGetUserInstaller(bound, read)) : unreadable;
    }

    /// <summary>
    /// WinGet's install-technology rule (winget-cli <c>IsInstallerTypeCompatible</c>, used by its upgrade's installed-type filter): the
    /// same type, or two types from one family: exe, Inno, Nullsoft and Burn; MSI and WiX; MSIX and Store. A zip is judged by the
    /// installer inside it. An installed type WinGet didn't report applies no filter, as in WinGet.
    /// </summary>
    internal static bool IsCompatibleWithInstalledType(string? installedType, string? installerType, string? nestedInstallerType)
    {
        if (string.IsNullOrWhiteSpace(installedType) || Normalized(installedType) is "UNKNOWN") return true;
        string? effective = string.Equals(installerType, "Zip", StringComparison.OrdinalIgnoreCase) ? nestedInstallerType : installerType;
        if (string.IsNullOrWhiteSpace(effective)) return false;
        string installed = Normalized(installedType), offered = Normalized(effective);
        if (offered is "UNKNOWN") return false;
        if (string.Equals(installed, offered, StringComparison.Ordinal)) return true;
        string? family = Family(installed);
        return family is not null && string.Equals(family, Family(offered), StringComparison.Ordinal);
    }

    /// <summary>
    /// The installer types, as projected enum names, to ask WinGet for when its first choice doesn't fit <paramref name="installedType"/>:
    /// the installed type first, then the rest of its family. Empty when WinGet reported no installed type.
    /// </summary>
    internal static IReadOnlyList<string> CompatibleInstallerTypes(string? installedType)
    {
        if (string.IsNullOrWhiteSpace(installedType)) return [];
        string installed = Normalized(installedType);
        string[] family = Family(installed) switch
        {
            "EXE" => ["Exe", "Inno", "Nullsoft", "Burn"],
            "MSI" => ["Msi", "Wix"],
            "MSIX" => ["Msix", "MSStore"],
            _ => [],
        };
        string? own = ProjectedName(installed);
        if (own is null) return family;
        return [own, .. family.Where(name => !string.Equals(name, own, StringComparison.Ordinal))];
    }

    /// <summary>WinGet writes installer types in lower case (<c>msix</c>) and the projection in Pascal case (<c>Msix</c>).</summary>
    private static string Normalized(string type) => type.Trim().ToUpperInvariant() switch
    {
        "APPX" => "MSIX",
        var upper => upper,
    };

    private static string? Family(string normalized) => normalized switch
    {
        "EXE" or "INNO" or "NULLSOFT" or "BURN" => "EXE",
        "MSI" or "WIX" => "MSI",
        "MSIX" or "MSSTORE" => "MSIX",
        _ => null,
    };

    private static string? ProjectedName(string normalized) => normalized switch
    {
        "EXE" => "Exe",
        "INNO" => "Inno",
        "NULLSOFT" => "Nullsoft",
        "BURN" => "Burn",
        "MSI" => "Msi",
        "WIX" => "Wix",
        "MSIX" => "Msix",
        "MSSTORE" => "MSStore",
        "PORTABLE" => "Portable",
        _ => null,
    };
}

/// <summary>
/// D7 design §3 item 1: the per-user installer read for the desktop's review, with a bounded budget. Nothing is downloaded; a read that
/// fails, times out or is ambiguous refuses the review.
/// </summary>
public sealed class WingetUserInstallerEvidenceSource : IWingetUserInstallerEvidenceSource
{
    private readonly IWinGetUserInstallerQuery query;

    public WingetUserInstallerEvidenceSource() : this(new WinGetComUserInstallerQuery()) { }

    internal WingetUserInstallerEvidenceSource(IWinGetUserInstallerQuery query) => this.query = query ?? throw new ArgumentNullException(nameof(query));

    /// <summary>How long one read may take; past it nothing can be reviewed.</summary>
    internal TimeSpan QueryBudget { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<WingetUserInstallerCheck> ReadAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Provider != UpdateProvider.WinGet || selection.Scope != UpdateScope.User || selection.Source != UpdateSource.WinGet)
            return WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnreadableMessage);
        WinGetUserInstallerObservation? observation;
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(QueryBudget);
            Task<WinGetUserInstallerObservation?> read = Task.Run(() => query.QueryAsync(selection, timeout.Token), CancellationToken.None);
            observation = await read.WaitAsync(QueryBudget, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnreadableMessage);
        }
        return WingetUserInstallerRules.Evaluate(observation, selection);
    }
}

/// <summary>
/// Through WinGet's structured API: the predefined <c>winget</c> catalog connected on its own, one exact-ID search, the exact target
/// version, then <see cref="PackageVersionInfo.GetApplicableInstaller"/> with <see cref="PackageInstallScope.User"/> (strict, as the
/// command line's <c>--scope user</c>) and only the reviewed architecture allowed (as <c>--architecture</c>).
/// </summary>
internal sealed class WinGetComUserInstallerQuery : IWinGetUserInstallerQuery
{
    public async Task<WinGetUserInstallerObservation?> QueryAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Windows.System.ProcessorArchitecture architecture = selection.Architecture switch
        {
            UpdateArchitecture.X64 => Windows.System.ProcessorArchitecture.X64,
            UpdateArchitecture.X86 => Windows.System.ProcessorArchitecture.X86,
            UpdateArchitecture.Arm64 => Windows.System.ProcessorArchitecture.Arm64,
            UpdateArchitecture.Neutral => Windows.System.ProcessorArchitecture.Neutral,
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        PackageManager manager = new();
        PackageCatalogReference reference = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
        PackageCatalogInfo info = reference.Info;
        bool predefined = info.Origin == PackageCatalogOrigin.Predefined && string.Equals(info.Name, "winget", StringComparison.OrdinalIgnoreCase);
        bool trusted = info.TrustLevel == PackageCatalogTrustLevel.Trusted;
        ConnectResult connection = await reference.ConnectAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (connection.Status != ConnectResultStatus.Ok) return null;

        FindPackagesOptions options = new() { ResultLimit = 2 };
        options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = selection.PackageId });
        FindPackagesResult result = await connection.PackageCatalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != FindPackagesResultStatus.Ok) return null;
        MatchResult[] matches = [.. result.Matches];
        if (matches.Length != 1 || matches[0].CatalogPackage is not { } package)
            return new(selection.PackageId, predefined, trusted, false, matches.Length, result.WasLimitExceeded, string.Empty, 0, null, null, null, null);
        PackageMatchFilter criteria = matches[0].MatchCriteria;
        bool exact = criteria.Field == PackageMatchField.Id && criteria.Option == PackageFieldMatchOption.Equals &&
            string.Equals(criteria.Value, selection.PackageId, StringComparison.OrdinalIgnoreCase);

        cancellationToken.ThrowIfCancellationRequested();
        PackageVersionId[] target = [.. package.AvailableVersions.Where(id => string.Equals(id.Version, selection.CandidateVersion, StringComparison.Ordinal))];
        if (target.Length != 1)
            return new(selection.PackageId, predefined, trusted, exact, matches.Length, result.WasLimitExceeded, package.Id, target.Length, null, null, null, null);
        PackageVersionInfo versionInfo = package.GetPackageVersionInfo(target[0]);
        InstallOptions Options(PackageInstallerType? type)
        {
            InstallOptions install = new() { PackageInstallScope = PackageInstallScope.User };
            install.AllowedArchitectures.Clear();
            install.AllowedArchitectures.Add(architecture);
            if (type is { } requested) install.InstallerType = requested;
            return install;
        }
        PackageInstallerInfo? installer = versionInfo.GetApplicableInstaller(Options(null));
        // The structured API's choice ignores how the app is installed; WinGet's upgrade doesn't (its installed-type filter). When the
        // first choice doesn't fit WinGet's installed type, ask again for each type that does, as the upgrade would choose among them.
        string? installedType = installer is null ? null : await ReadInstalledTypeAsync(manager, reference, selection.PackageId, cancellationToken).ConfigureAwait(false);
        if (installer is not null && !WingetUserInstallerRules.IsCompatibleWithInstalledType(installedType, installer.InstallerType.ToString(), installer.NestedInstallerType.ToString()))
        {
            foreach (string name in WingetUserInstallerRules.CompatibleInstallerTypes(installedType))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Enum.TryParse(name, out PackageInstallerType type) && versionInfo.GetApplicableInstaller(Options(type)) is { } compatible &&
                    WingetUserInstallerRules.IsCompatibleWithInstalledType(installedType, compatible.InstallerType.ToString(), compatible.NestedInstallerType.ToString()))
                {
                    installer = compatible;
                    break;
                }
            }
        }
        return new(selection.PackageId, predefined, trusted, exact, matches.Length, result.WasLimitExceeded, package.Id, 1,
            installer?.InstallerType.ToString(), installer?.NestedInstallerType.ToString(), installer?.Scope.ToString(),
            installer?.ElevationRequirement.ToString(), installedType);
    }

    /// <summary>
    /// WinGet's own record of how the installed package was installed (<see cref="PackageVersionMetadataField.InstallerType"/> of the
    /// installed version, through the same composite catalog shape as the scan's compatibility read); null when it can't be read, or
    /// when exactly one installed package doesn't match. Null applies no filter, as WinGet applies none to an unknown installed type.
    /// </summary>
    private static async Task<string?> ReadInstalledTypeAsync(PackageManager manager, PackageCatalogReference reference, string packageId,
        CancellationToken cancellationToken)
    {
        try
        {
            CreateCompositePackageCatalogOptions compositeOptions = new()
            {
                CompositeSearchBehavior = CompositeSearchBehavior.RemotePackagesFromAllCatalogs,
                InstalledScope = PackageInstallScope.Any
            };
            compositeOptions.Catalogs.Add(reference);
            ConnectResult connection = await manager.CreateCompositePackageCatalog(compositeOptions).ConnectAsync().AsTask(cancellationToken).ConfigureAwait(false);
            if (connection.Status != ConnectResultStatus.Ok) return null;
            FindPackagesOptions options = new() { ResultLimit = 2 };
            options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = packageId });
            FindPackagesResult result = await connection.PackageCatalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
            if (result.Status != FindPackagesResultStatus.Ok) return null;
            MatchResult[] matches = [.. result.Matches];
            if (matches.Length != 1 || matches[0].CatalogPackage?.InstalledVersion is not { } installed ||
                !string.Equals(matches[0].CatalogPackage.Id, packageId, StringComparison.OrdinalIgnoreCase))
                return null;
            string type = installed.GetMetadata(PackageVersionMetadataField.InstallerType);
            return string.IsNullOrWhiteSpace(type) || type.Length > 32 ? null : type.Trim();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
