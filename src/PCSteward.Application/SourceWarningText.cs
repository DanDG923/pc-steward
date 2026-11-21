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
    private const int MaximumReasonLength = 160;

    /// <summary>The most per-package warnings one source refresh records.</summary>
    public const int MaximumDetailUnusedWarnings = 16;

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
}
