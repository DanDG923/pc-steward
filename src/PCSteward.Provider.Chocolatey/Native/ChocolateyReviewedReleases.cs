using System.Collections.ObjectModel;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 (accepted, Option 2) and its design mapping §3.1: one independently reviewed Chocolatey
/// client release. Private construction; obtained only from a <see cref="ChocolateyReleaseList"/>, whose production instance
/// (<see cref="ChocolateyReleaseList.Listed"/>) is compiled. Nothing here is ever read from remote metadata, the database or
/// a payload; a release is trusted only through the native checks that compare installed files with it.
/// </summary>
internal sealed class ChocolateyReviewedRelease
{
    internal const string EnginePath = "choco.exe";
    internal const string PowerShellModulePath = "helpers/Chocolatey.PowerShell.dll";

    private ChocolateyReviewedRelease(ChocolateyReleaseList list, ChocolateyNativeVersion version, string engineFileVersion, string asset,
        long assetLength, string assetSha256, IReadOnlyList<ChocolateyReleaseFile> files)
    {
        List = list;
        NativeVersion = version;
        EngineFileVersion = engineFileVersion;
        Asset = asset;
        AssetLength = assetLength;
        AssetSha256 = assetSha256;
        Files = files;
        ChocolateyReleaseFile engine = files.Single(file => file.Path == EnginePath);
        EngineSha256 = engine.Sha256;
        EngineLength = engine.Length;
        PowerShellModule = files.Where(file => file.Path == PowerShellModulePath).Select(static file => (ChocolateyReleaseFile?)file).SingleOrDefault();
    }

    /// <summary>The list this release belongs to (membership is by reference).</summary>
    internal ChocolateyReleaseList List { get; }

    /// <summary>The normalized release version (for example <c>2.7.5</c>), the version the <c>chocolatey</c> package registers.</summary>
    internal string Version => NativeVersion.Normalized;

    internal ChocolateyNativeVersion NativeVersion { get; }

    /// <summary>The root engine's PE file version (for example <c>2.7.5.0</c>), checked on the held engine handle.</summary>
    internal string EngineFileVersion { get; }

    internal string Asset { get; }

    internal long AssetLength { get; }

    /// <summary>The published release-asset digest; it identifies the compiled release-tree dataset (fingerprint field 4).</summary>
    internal string AssetSha256 { get; }

    internal string EngineSha256 { get; }

    internal long EngineLength { get; }

    /// <summary>The release's <c>helpers/Chocolatey.PowerShell.dll</c> entry, when it ships one (the §3a leftover base).</summary>
    internal ChocolateyReleaseFile? PowerShellModule { get; }

    /// <summary>The compiled release-tree table (root-relative, forward slashes).</summary>
    internal IReadOnlyList<ChocolateyReleaseFile> Files { get; }

    internal static ChocolateyReviewedRelease Create(ChocolateyReleaseList list, ChocolateyReleaseEntry entry)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(entry);
        if (!ChocolateyNativeVersion.TryParse(entry.Version, out ChocolateyNativeVersion? version) || version is null ||
            !string.Equals(version.Normalized, entry.Version, StringComparison.Ordinal) || version.IsPrerelease)
            throw new ArgumentException("A release needs a normalized stable native version.", nameof(entry));
        if (!IsFileVersion(entry.EngineFileVersion) || !entry.EngineFileVersion.StartsWith(entry.Version + ".", StringComparison.Ordinal) &&
            entry.EngineFileVersion != entry.Version)
            throw new ArgumentException("The engine file version must extend the release version.", nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Asset) || entry.AssetLength <= 0 || !IsDigest(entry.AssetSha256))
            throw new ArgumentException("A release needs its public asset identity.", nameof(entry));
        ChocolateyReleaseFile[] files = [.. entry.Files];
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (ChocolateyReleaseFile file in files)
            if (!ChocolateyProfileRules.IsReleasePath(file.Path) || file.Length < 0 || !IsDigest(file.Sha256) || !paths.Add(file.Path))
                throw new ArgumentException("A release table entry is malformed or duplicated.", nameof(entry));
        if (files.Count(file => file.Path == EnginePath) != 1)
            throw new ArgumentException("A release table needs exactly one root engine.", nameof(entry));
        return new ChocolateyReviewedRelease(list, version, entry.EngineFileVersion, entry.Asset, entry.AssetLength, entry.AssetSha256,
            new ReadOnlyCollection<ChocolateyReleaseFile>(files));
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool IsFileVersion(string? value) =>
        value is { Length: > 0 and <= 64 } && value.Split('.') is { Length: 4 } parts &&
        parts.All(part => part.Length is > 0 and <= 5 && part.All(char.IsAsciiDigit) && (part == "0" || part[0] != '0'));
}

/// <summary>Plain input to a release list (compiled data in production, fixture data in tests); it carries no authority.</summary>
internal sealed record ChocolateyReleaseEntry(string Version, string EngineFileVersion, string Asset, long AssetLength, string AssetSha256,
    IReadOnlyList<ChocolateyReleaseFile> Files);

/// <summary>A closed reviewed self-upgrade leftover the observer recognised (design §3a): the fixed path and the release it matches.</summary>
internal sealed record ChocolateyReleaseResidue(string RelativePath, ChocolateyReviewedRelease MatchedRelease);

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-031 design mapping §3.1, §3.3 and §3a: the compiled, immutable list of independently reviewed
/// Chocolatey client releases, strictly ascending by version with unique engine and asset digests. Production uses only
/// <see cref="Listed"/>, which today holds exactly 2.7.5 (<see cref="ChocolateyReleaseManifest2_7_5"/>). The list grows only
/// through the reviewed re-pin procedure; <see cref="CreateForTests"/> builds fixture lists and is banned from every
/// <c>src</c> caller (architecture test).
/// </summary>
internal sealed class ChocolateyReleaseList
{
    /// <summary>The closed reviewed leftover set (design §3a): exactly the one <c>helpers</c> name pinned setup.psm1 creates.</summary>
    internal const string PowerShellModuleLeftover = @"helpers\Chocolatey.PowerShell.dll.old";

    private readonly ChocolateyReviewedRelease[] releases;

    private ChocolateyReleaseList(IReadOnlyList<ChocolateyReleaseEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) throw new ArgumentException("A release list is never empty.", nameof(entries));
        releases = [.. entries.Select(entry => ChocolateyReviewedRelease.Create(this, entry))];
        for (int index = 1; index < releases.Length; index++)
            if (!IsNewer(releases[index], releases[index - 1]))
                throw new ArgumentException("Releases must be strictly ascending by version.", nameof(entries));
        if (releases.Select(release => release.EngineSha256).Distinct(StringComparer.Ordinal).Count() != releases.Length ||
            releases.Select(release => release.AssetSha256).Distinct(StringComparer.Ordinal).Count() != releases.Length)
            throw new ArgumentException("Engine and asset digests must be unique across the list.", nameof(entries));
        Releases = Array.AsReadOnly(releases);
    }

    /// <summary>The production list: every independently reviewed release, compiled. Today exactly 2.7.5.</summary>
    internal static ChocolateyReleaseList Listed { get; } = new(
    [
        new ChocolateyReleaseEntry(ChocolateyReleaseManifest2_7_5.Release, ChocolateyClientReleasePolicy.Version,
            ChocolateyReleaseManifest2_7_5.Asset, ChocolateyReleaseManifest2_7_5.AssetLength, ChocolateyReleaseManifest2_7_5.AssetSha256,
            ChocolateyReleaseManifest2_7_5.Files),
    ]);

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): a fixture list under the same validation.</summary>
    internal static ChocolateyReleaseList CreateForTests(IReadOnlyList<ChocolateyReleaseEntry> entries) => new(entries);

    /// <summary>Strictly ascending by version.</summary>
    internal IReadOnlyList<ChocolateyReviewedRelease> Releases { get; }

    /// <summary>The listed release whose root engine has exactly this SHA-256, or null.</summary>
    internal ChocolateyReviewedRelease? FindByEngine(string? engineSha256) =>
        engineSha256 is null ? null : releases.SingleOrDefault(release => string.Equals(release.EngineSha256, engineSha256, StringComparison.Ordinal));

    /// <summary>The listed release with exactly this normalized version, or null.</summary>
    internal ChocolateyReviewedRelease? FindByVersion(string? normalizedVersion) =>
        normalizedVersion is null ? null : releases.SingleOrDefault(release => string.Equals(release.Version, normalizedVersion, StringComparison.Ordinal));

    internal bool Contains(ChocolateyReviewedRelease? release) => release is not null && ReferenceEquals(release.List, this) && releases.Contains(release);

    internal bool ContainsAsset(string? assetSha256) =>
        assetSha256 is not null && releases.Any(release => string.Equals(release.AssetSha256, assetSha256, StringComparison.Ordinal));

    /// <summary>
    /// The ADR-031 offer rule (design §3.3): when <paramref name="installed"/> is a release of this list and a strictly newer
    /// listed release exists, the newest listed release; otherwise null. Never an equal or older release (no downgrade), and
    /// nothing for a release outside this list (an unlisted installed client is never offered an update).
    /// </summary>
    internal ChocolateyReviewedRelease? OfferTarget(ChocolateyReviewedRelease? installed)
    {
        if (!Contains(installed)) return null;
        ChocolateyReviewedRelease newest = releases[^1];
        return IsNewer(newest, installed!) ? newest : null;
    }

    /// <summary>Strictly newer by the same stable comparison rule the fixed upgrade builders use.</summary>
    internal static bool IsNewer(ChocolateyReviewedRelease candidate, ChocolateyReviewedRelease than)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(than);
        return ChocolateyStageCMapping.IsStableStrictlyNewer(than.Version, candidate.Version);
    }

    /// <summary>
    /// The §3a leftover rule: <paramref name="relativePath"/> is exactly <see cref="PowerShellModuleLeftover"/> (case-insensitive,
    /// as on NTFS) and its observed length and SHA-256 equal the <c>helpers/Chocolatey.PowerShell.dll</c> of a listed release.
    /// Returns that release, or null (the file then refuses as any other unlisted file).
    /// </summary>
    internal ChocolateyReviewedRelease? MatchLeftover(string relativePath, long length, string sha256)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(sha256);
        if (!relativePath.Equals(PowerShellModuleLeftover, StringComparison.OrdinalIgnoreCase)) return null;
        return releases.FirstOrDefault(release => release.PowerShellModule is { } module && module.Length == length &&
            string.Equals(module.Sha256, sha256, StringComparison.Ordinal));
    }
}
