namespace PCSteward.Domain;

/// <summary>
/// The Windows package family name format (MSIX design note §3.1): a package name of 3 to 50 letters, digits, dots and hyphens, an
/// underscore, then the 13-character publisher ID. Only names in this format are used as MSIX identity; anything else is refused.
/// </summary>
public static class PackageFamilyName
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string trimmed = value.Trim();
        int separator = trimmed.LastIndexOf('_');
        if (separator is < 3 or > 50 || trimmed.Length - separator - 1 != 13) return false;
        return trimmed[..separator].All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-') &&
            trimmed[(separator + 1)..].All(char.IsAsciiLetterOrDigit);
    }

}
