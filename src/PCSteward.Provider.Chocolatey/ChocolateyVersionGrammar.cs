namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// Narrow public facade over the pinned native version parser, so other components (the protocol-2 codec) can validate
/// a reported normalized version without broad access to Chocolatey internals. Grammar check only; never trust or authority.
/// </summary>
public static class ChocolateyVersionGrammar
{
    /// <summary>True only for a non-empty string of at most <paramref name="maximumLength"/> characters that equals its own native normalized form.</summary>
    public static bool IsNormalized(string? value, int maximumLength) =>
        value is not null && value.Length > 0 && value.Length <= maximumLength &&
        ChocolateyNativeVersion.TryParse(value, out ChocolateyNativeVersion? version) &&
        string.Equals(version!.Normalized, value, StringComparison.Ordinal);
}
