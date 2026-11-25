namespace PCSteward.Application;

/// <summary>
/// Owner's PC, 2026-11-25 (Claude, an MSIX package): WinGet compares versions by their numeric parts and ignores trailing zero parts,
/// so a manifest's <c>6.28899.6</c> and the <c>6.28899.6.0</c> that Windows registers for the MSIX package are one version. Only plain
/// dotted numbers count, and only trailing <c>.0</c> parts may differ; every other version text must match exactly.
/// </summary>
public static class WingetVersionText
{
    private const int MaximumLength = 128;

    /// <summary>True when <paramref name="left"/> and <paramref name="right"/> are the same WinGet version under that rule.</summary>
    public static bool SameVersion(string? left, string? right)
    {
        if (left is null || right is null) return false;
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        return TrimmedNumeric(left) is { } a && TrimmedNumeric(right) is { } b && string.Equals(a, b, StringComparison.Ordinal);
    }

    /// <summary>The version without trailing <c>.0</c> parts, or null when it isn't plain dotted numbers.</summary>
    private static string? TrimmedNumeric(string version)
    {
        if (version.Length is 0 or > MaximumLength) return null;
        string[] parts = version.Split('.');
        if (parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit))) return null;
        int count = parts.Length;
        while (count > 1 && parts[count - 1] == "0") count--;
        return string.Join('.', parts, 0, count);
    }
}
