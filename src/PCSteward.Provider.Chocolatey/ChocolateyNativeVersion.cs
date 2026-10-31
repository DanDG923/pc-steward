using System.Globalization;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Bounded native version/store comparison data. It does not observe a registration or qualify a candidate.</summary>
internal sealed class ChocolateyNativeVersion
{
    private readonly Version numbers;
    private readonly string release;

    private ChocolateyNativeVersion(string original, Version numbers, string release, int releaseLabels)
    {
        Original = original;
        this.numbers = numbers;
        this.release = release;
        string suffix = release.Length == 0 ? string.Empty : "-" + release;
        Normalized = numbers.ToString(numbers.Revision > 0 ? 4 : 3) + suffix;
        NativeString = releaseLabels > 1 ? Normalized : Original;
    }
    internal string Original { get; }
    internal string Normalized { get; }
    internal string NativeString { get; }
    internal bool IsPrerelease => release.Length != 0;

    internal static bool TryParse(string text, out ChocolateyNativeVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > 128 || text.Contains('+', StringComparison.Ordinal) ||
            text.Any(character => !char.IsAscii(character) || char.IsWhiteSpace(character))) return false;
        int dash = text.IndexOf('-', StringComparison.Ordinal);
        string body = dash < 0 ? text : text[..dash];
        string release = dash < 0 ? string.Empty : text[(dash + 1)..];
        if (dash >= 0 && release.Length == 0) return false;
        string[] parts = body.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        int[] numbers = new int[4];
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit) ||
                !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        string[] labels = release.Length == 0 ? [] : release.Split('.');
        foreach (string label in labels)
            if (label.Length == 0 || !label.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ||
                label.Length > 1 && label[0] == '0' && label.All(char.IsAsciiDigit)) return false;
        version = new(text, new Version(numbers[0], numbers[1], numbers[2], numbers[3]), release, labels.Length);
        return true;
    }

    internal IReadOnlyList<string> GetStoreDirectoryNames(string id)
    {
        if (!ChocolateyNativePackageId.IsSupported(id) || IsDeviceName(id))
            throw new ArgumentException("A supported native package ID is required.", nameof(id));
        string suffix = release.Length == 0 ? string.Empty : "-" + release;
        List<string> names = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        void Add(string value)
        {
            string name = id + "." + value;
            if (name.Length > 255) throw new ArgumentException("The native store name exceeds a supported path component.", nameof(id));
            if (seen.Add(name)) names.Add(name);
        }
        Add(Normalized);
        Add(NativeString);
        Add(numbers.ToString(4) + suffix);
        if (numbers.Build == 0 && numbers.Revision == 0) Add(numbers.ToString(2) + suffix);
        return Array.AsReadOnly(names.ToArray());
    }

    private static bool IsDeviceName(string id)
    {
        string stem = id.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9';
    }
}
