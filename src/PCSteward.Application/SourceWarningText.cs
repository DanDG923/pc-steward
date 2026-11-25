using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Owner's PC, 2026-11-21 (0.1.18): MEGAsync stayed "PC Steward can't tell how this app was installed" because WinGet's detailed read
/// of its package was not used, and nothing said which check dropped it. A source connector now records, for each package whose
/// detailed read it did not use, one warning that names the package and the check. It is shown only on that app's details, and it
/// never changes what is offered: a package without the detailed read stays a review item, as before.
/// </summary>
public static class SourceWarningText
{
    private const string DetailUnusedMarker = ":detail-unused:";
    private const string DuplicateUpdateIdMarker = ":duplicate-update-id:";
    private const string DuplicateInstalledIdMarker = ":duplicate-installed-id:";
    private const int MaximumReasonLength = 160;

    /// <summary>The most per-package warnings one source refresh records.</summary>
    public const int MaximumDetailUnusedWarnings = 16;

    /// <summary>The most duplicate package-ID warnings of each kind one source refresh records.</summary>
    public const int MaximumDuplicateIdWarnings = 16;

    /// <summary>
    /// <c>{source}:detail-unused:{package}:{reason}</c>. The reason keeps only letters, digits and <c>. + _ -</c>, so it can't hold a
    /// path, a colon or free text, and is cut at 160 characters.
    /// </summary>
    public static string DetailUnused(string sourceId, string packageId, string reason)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(reason);
        string clean = new([.. reason.Where(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '+' or '_' or '-').Take(MaximumReasonLength)]);
        return $"{sourceId.Trim()}{DetailUnusedMarker}{packageId.Trim()}:{(clean.Length == 0 ? "unknown" : clean)}";
    }

    /// <summary>Reads a <see cref="DetailUnused"/> warning back into its package and reason.</summary>
    public static bool TryReadDetailUnused(string? warning, out string packageId, out string reason)
    {
        packageId = reason = string.Empty;
        int marker = warning?.IndexOf(DetailUnusedMarker, StringComparison.Ordinal) ?? -1;
        if (warning is null || marker <= 0) return false;
        string rest = warning[(marker + DetailUnusedMarker.Length)..];
        int separator = rest.LastIndexOf(':');
        if (separator <= 0 || separator == rest.Length - 1) return false;
        packageId = rest[..separator];
        reason = rest[(separator + 1)..];
        return true;
    }

    /// <summary>What the details page says for a package whose detailed read wasn't used, with the check's code to quote in a report.</summary>
    public static string DescribeDetailUnused(string reason) =>
        $"WinGet's detailed answer for this package wasn't used, so its install scope, architecture and channel aren't known (check: {reason}).";

    /// <summary><c>{source}:duplicate-update-id:{package}</c>, for a validated WinGet package ID listed more than once in update output.</summary>
    public static string DuplicateUpdateId(string sourceId, string packageId) =>
        BuildPackageWarning(sourceId, DuplicateUpdateIdMarker, packageId);

    /// <summary><c>{source}:duplicate-installed-id:{package}</c>, for a validated WinGet package ID listed more than once in installed output.</summary>
    public static string DuplicateInstalledId(string sourceId, string packageId) =>
        BuildPackageWarning(sourceId, DuplicateInstalledIdMarker, packageId);

    /// <summary>Reads a <see cref="DuplicateUpdateId"/> warning back into its validated package ID.</summary>
    public static bool TryReadDuplicateUpdateId(string? warning, out string packageId) =>
        TryReadPackageWarning(warning, DuplicateUpdateIdMarker, out packageId);

    /// <summary>Reads a <see cref="DuplicateInstalledId"/> warning back into its validated package ID.</summary>
    public static bool TryReadDuplicateInstalledId(string? warning, out string packageId) =>
        TryReadPackageWarning(warning, DuplicateInstalledIdMarker, out packageId);

    /// <summary>What the details page says for a package WinGet listed more than once in update output.</summary>
    public static string DescribeDuplicateUpdateId() =>
        "WinGet listed this package more than once, so PC Steward can't tell which update applies.";

    /// <summary>What the details page says for a package WinGet listed more than once as installed.</summary>
    public static string DescribeDuplicateInstalledId() =>
        "WinGet lists more than one installed copy of this package, so PC Steward doesn't offer its update.";

    private static string BuildPackageWarning(string sourceId, string marker, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(packageId);
        string cleanSource = sourceId.Trim();
        if (cleanSource.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("The source ID cannot contain a colon.", nameof(sourceId));
        string cleanPackage = packageId.Trim();
        if (!UpdateJobSelection.IsWinGetPackageId(cleanPackage))
            throw new ArgumentException("The package ID is outside the allowlisted WinGet format.", nameof(packageId));
        return $"{cleanSource}{marker}{cleanPackage}";
    }

    private static bool TryReadPackageWarning(string? warning, string marker, out string packageId)
    {
        packageId = string.Empty;
        if (warning is null) return false;
        int markerIndex = warning.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0 || warning.AsSpan(0, markerIndex).IndexOf(':') >= 0) return false;
        string candidate = warning[(markerIndex + marker.Length)..];
        if (!UpdateJobSelection.IsWinGetPackageId(candidate)) return false;
        packageId = candidate;
        return true;
    }
}
