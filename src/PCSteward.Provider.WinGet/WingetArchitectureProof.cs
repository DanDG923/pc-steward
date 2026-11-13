using Microsoft.Management.Deployment;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>
/// D4b 32-bit-registered apps, proof P2 (design note §3.3): what WinGet's structured API reported, for each requested version of one
/// exact package in the trusted <c>winget</c> catalog, about machine-scope installers for x64 and for x86. Plain values, so the rule is
/// testable.
/// </summary>
internal sealed record WinGetInstallerArchitectureObservation(
    string RequestedPackageId,
    bool CatalogIsPredefined,
    bool CatalogIsTrusted,
    bool ExactIdMatch,
    int MatchCount,
    bool WasLimitExceeded,
    string PackageId,
    IReadOnlyList<WinGetVersionArchitectures> Versions);

/// <summary>
/// One requested version: how many catalog versions had exactly that version text in the default channel, and whether WinGet would
/// choose a machine-scope installer when only x64, or only x86, is allowed.
/// </summary>
internal sealed record WinGetVersionArchitectures(string Version, int Matches, bool HasX64MachineInstaller, bool HasX86MachineInstaller);

internal interface IWinGetInstallerArchitectureQuery
{
    /// <summary>Reads <paramref name="versions"/> of <paramref name="packageId"/> from the <c>winget</c> catalog; null when it can't.</summary>
    Task<WinGetInstallerArchitectureObservation?> QueryAsync(string packageId, IReadOnlyList<string> versions, CancellationToken cancellationToken);
}

/// <summary>The design note §3.3–3.4 rules, on plain values.</summary>
internal static class WingetArchitectureProofRules
{
    private const int MaximumVersionLength = 128;
    private const int MaximumProductCodeLength = 255;

    /// <summary>
    /// P1: the installed version's x64 installer declares the uninstall entry this PC has. An <c>AppsAndFeaturesEntries</c> item that
    /// carries the entry's key as its product code must also carry the entry's DisplayVersion exactly. When no item names the key, the
    /// installer's own product code must be the key and WinGet's installed version must be the entry's DisplayVersion. A list that couldn't be read
    /// proves nothing.
    /// </summary>
    internal static bool ProvesProductCode(WingetManifestFacts installed, WingetArchitectureProofRequest request)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(request);
        if (installed.DeclaredEntries is not { } entries || !IsProductCode(request.RegistrationKey) || !IsBoundedVersion(request.RegistrationVersion))
            return false;
        WingetDeclaredEntry[] declaringKey = [.. entries.Where(entry => SameCode(entry.ProductCode, request.RegistrationKey))];
        // An item that names the key decides: its DisplayVersion must be the entry's.
        if (declaringKey.Length > 0)
            return declaringKey.Any(entry => string.Equals(entry.DisplayVersion, request.RegistrationVersion, StringComparison.Ordinal));
        return SameCode(installed.ProductCode, request.RegistrationKey) &&
            string.Equals(request.ProviderInstalledVersion, request.RegistrationVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// P2: the trusted catalog's single exact package has each requested version exactly once, and for each one WinGet chooses a
    /// machine installer when only x64 is allowed and none when only x86 is allowed. Anything missing or unexpected proves nothing.
    /// </summary>
    internal static bool ProvesSingleArchitecture(WinGetInstallerArchitectureObservation? observation, string packageId, IReadOnlyList<string> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        if (observation is null || !observation.CatalogIsPredefined || !observation.CatalogIsTrusted || !observation.ExactIdMatch ||
            observation.MatchCount != 1 || observation.WasLimitExceeded || versions.Count == 0 ||
            !string.Equals(observation.RequestedPackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observation.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (string version in versions)
        {
            WinGetVersionArchitectures[] read = [.. observation.Versions.Where(item => string.Equals(item.Version, version, StringComparison.Ordinal))];
            if (read.Length != 1 || read[0].Matches != 1 || !read[0].HasX64MachineInstaller || read[0].HasX86MachineInstaller) return false;
        }
        return true;
    }

    /// <summary>
    /// The one uninstall entry the target's installer declares it writes (design note §3.4): exactly one <c>AppsAndFeaturesEntries</c>
    /// item, with a product code and a DisplayVersion, consistent with the installer's own product code when it has one. Otherwise null.
    /// </summary>
    internal static ExpectedUninstallEntry? DeclaredEntry(WingetManifestFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.DeclaredEntries is not [{ ProductCode: { } code, DisplayVersion: { } version }] || !IsProductCode(code) || !IsBoundedVersion(version))
            return null;
        if (facts.ProductCode is not null && !SameCode(facts.ProductCode, code)) return null;
        return new(code.Trim(), version);
    }

    /// <summary>A plain version: starts with a letter or digit, at most 128 characters of letters, digits and <c>. - _ +</c>.</summary>
    internal static bool IsBoundedVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaximumVersionLength &&
        char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+');

    private static bool IsProductCode(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaximumProductCodeLength && !value.Any(char.IsControl);

    private static bool SameCode(string? left, string? right) =>
        IsProductCode(left) && IsProductCode(right) && string.Equals(left!.Trim(), right!.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// P2 through WinGet's structured API: the predefined <c>winget</c> catalog, connected on its own (no installed-package correlation is
/// needed), one exact-ID search, then each requested version's <see cref="PackageVersionInfo.GetApplicableInstaller"/> with machine
/// scope and one allowed architecture at a time.
/// </summary>
internal sealed class WinGetComInstallerArchitectureQuery : IWinGetInstallerArchitectureQuery
{
    public async Task<WinGetInstallerArchitectureObservation?> QueryAsync(string packageId, IReadOnlyList<string> versions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(versions);
        PackageManager manager = new();
        PackageCatalogReference reference = manager.GetPredefinedPackageCatalog(PredefinedPackageCatalog.OpenWindowsCatalog);
        PackageCatalogInfo info = reference.Info;
        bool predefined = info.Origin == PackageCatalogOrigin.Predefined && string.Equals(info.Name, "winget", StringComparison.OrdinalIgnoreCase);
        bool trusted = info.TrustLevel == PackageCatalogTrustLevel.Trusted;
        ConnectResult connection = await reference.ConnectAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (connection.Status != ConnectResultStatus.Ok) return null;

        FindPackagesOptions options = new() { ResultLimit = 2 };
        options.Filters.Add(new PackageMatchFilter { Field = PackageMatchField.Id, Option = PackageFieldMatchOption.Equals, Value = packageId });
        FindPackagesResult result = await connection.PackageCatalog.FindPackagesAsync(options).AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != FindPackagesResultStatus.Ok) return null;
        MatchResult[] matches = [.. result.Matches];
        if (matches.Length != 1 || matches[0].CatalogPackage is not { } package) return new(packageId, predefined, trusted, false, matches.Length, result.WasLimitExceeded, string.Empty, []);
        PackageMatchFilter criteria = matches[0].MatchCriteria;
        bool exact = criteria.Field == PackageMatchField.Id && criteria.Option == PackageFieldMatchOption.Equals &&
            string.Equals(criteria.Value, packageId, StringComparison.OrdinalIgnoreCase);

        PackageVersionId[] available = [.. package.AvailableVersions];
        List<WinGetVersionArchitectures> read = [];
        foreach (string version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageVersionId[] same = [.. available.Where(id => string.Equals(id.Version, version, StringComparison.Ordinal) && string.IsNullOrEmpty(id.Channel))];
            if (same.Length != 1)
            {
                read.Add(new(version, same.Length, false, false));
                continue;
            }
            PackageVersionInfo versionInfo = package.GetPackageVersionInfo(same[0]);
            read.Add(new(version, 1,
                HasMachineInstaller(versionInfo, Windows.System.ProcessorArchitecture.X64),
                HasMachineInstaller(versionInfo, Windows.System.ProcessorArchitecture.X86)));
        }
        return new(packageId, predefined, trusted, exact, matches.Length, result.WasLimitExceeded, package.Id, read);
    }

    private static bool HasMachineInstaller(PackageVersionInfo versionInfo, Windows.System.ProcessorArchitecture architecture)
    {
        InstallOptions options = new() { PackageInstallScope = PackageInstallScope.SystemOrUnknown };
        options.AllowedArchitectures.Clear();
        options.AllowedArchitectures.Add(architecture);
        PackageInstallerInfo? installer = versionInfo.GetApplicableInstaller(options);
        // An installer of another architecture than the only one allowed is still counted as found, so the x86 answer fails closed.
        return installer is not null && (architecture != Windows.System.ProcessorArchitecture.X64 || installer.Architecture == architecture);
    }
}
