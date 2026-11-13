using Microsoft.Management.Deployment;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// D7 design §3 item 1: what WinGet's structured API reported for one per-user update: the catalog, the exact package, how many catalog
/// versions carry exactly the target version text, and the installer <see cref="PackageVersionInfo.GetApplicableInstaller"/> chose with
/// user scope and the reviewed architecture. The installer's type, nested type, scope and elevation requirement are the projected enum
/// names, so the rule is testable without WinGet.
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
    string? ElevationRequirement);

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
    /// ID, exactly one catalog version with the target version text, and one applicable installer. MSIX and Store installers keep the C4
    /// evidence. A vendor type needs a declared user scope, unless WinGet places it itself (portable, or a zip around a portable app),
    /// exactly as the command line's <c>--scope user</c> selects. Anything else is refused.
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
        if (observation.InstallerType is "Msix" or "MSStore") return WingetUserInstallerCheck.Msix();
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
            _ => null,
        };
        if (type is not { } bound) return WingetUserInstallerCheck.Refused(WingetUserInstallerText.UnsupportedTypeMessage);
        bool placedByWinGet = bound == WinGetUserInstallerType.Portable ||
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
        InstallOptions install = new() { PackageInstallScope = PackageInstallScope.User };
        install.AllowedArchitectures.Clear();
        install.AllowedArchitectures.Add(architecture);
        PackageInstallerInfo? installer = versionInfo.GetApplicableInstaller(install);
        return new(selection.PackageId, predefined, trusted, exact, matches.Length, result.WasLimitExceeded, package.Id, 1,
            installer?.InstallerType.ToString(), installer?.NestedInstallerType.ToString(), installer?.Scope.ToString(),
            installer?.ElevationRequirement.ToString());
    }
}
