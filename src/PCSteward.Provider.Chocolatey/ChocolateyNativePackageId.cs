namespace PCSteward.Provider.Chocolatey;

/// <summary>Bounded ASCII subset of the pinned NuGet package-ID policy, not registration or trust evidence.</summary>
internal static class ChocolateyNativePackageId
{
    internal static bool IsSupported(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 100) return false;
        bool needsWord = true;
        foreach (char character in id)
        {
            if (char.IsAsciiLetterOrDigit(character) || character == '_') needsWord = false;
            else if (character is '.' or '-' && !needsWord) needsWord = true;
            else return false;
        }
        return !needsWord;
    }
}
