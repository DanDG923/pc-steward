namespace PCSteward.Provider.Chocolatey;

internal enum ChocolateyTreeContentPolicy { Protected, NonLoadingLogContents }

/// <summary>Classification only. The native observer must independently prove ancestry, identity, reparse and access facts.</summary>
internal static class ChocolateyTreeCoveragePolicy
{
    internal static ChocolateyTreeContentPolicy Classify(string relativePath, bool isDirectory)
    {
        string[] parts = Parts(relativePath);
        return !isDirectory && parts.Length > 1 && parts[0].Equals("logs", StringComparison.OrdinalIgnoreCase)
            ? ChocolateyTreeContentPolicy.NonLoadingLogContents : ChocolateyTreeContentPolicy.Protected;
    }

    internal static bool MayBeAbsent(string relativePath)
    {
        string[] parts = Parts(relativePath);
        return parts.Length == 1 && parts[0].ToUpperInvariant() is "HOOKS" or "EXTENSIONS" or "LICENSE" or "LIB-BKP" or "LIB-BAD";
    }

    private static string[] Parts(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (relativePath.Length == 0) return [];
        if (relativePath.Length > 4096 || relativePath.Any(character => char.IsControl(character) || character is ':' or '"' or '*' or '?' or '<' or '>' or '|'))
            throw new ArgumentException("A bounded relative native tree path is required.", nameof(relativePath));
        string[] parts = relativePath.Replace('\\', '/').Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
            throw new ArgumentException("Ambiguous native tree path.", nameof(relativePath));
        return parts;
    }
}
