using System.Globalization;

namespace PCSteward.SelfUpdate;

/// <summary>
/// A strict release version: <c>MAJOR.MINOR.PATCH</c> with an optional lowercase pre-release suffix such as
/// <c>-private-beta</c> or <c>-beta.2</c>. No leading zeros, no build metadata, at most 64 characters. Ordering follows
/// Semantic Versioning 2.0 precedence. Anything else is refused rather than guessed.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    private const int MaximumLength = 64;
    private readonly string[] preRelease;

    private ReleaseVersion(int major, int minor, int patch, string[] preRelease, string text)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        this.preRelease = preRelease;
        Text = text;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string Text { get; }

    public static bool TryParse(string? text, out ReleaseVersion? version)
    {
        version = null;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumLength) return false;
        int dash = text.IndexOf('-', StringComparison.Ordinal);
        string core = dash < 0 ? text : text[..dash];
        string[] parts = core.Split('.');
        if (parts.Length != 3 || !TryNumber(parts[0], out int major) || !TryNumber(parts[1], out int minor) || !TryNumber(parts[2], out int patch))
            return false;
        string[] identifiers = [];
        if (dash >= 0)
        {
            identifiers = text[(dash + 1)..].Split('.');
            foreach (string identifier in identifiers)
            {
                if (identifier.Length == 0 || !identifier.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return false;
                if (identifier.All(char.IsAsciiDigit) && identifier.Length > 1 && identifier[0] == '0') return false;
            }
        }
        version = new(major, minor, patch, identifiers, text);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;
        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;
        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (preRelease.Length == 0 && other.preRelease.Length == 0) return 0;
        if (preRelease.Length == 0) return 1;
        if (other.preRelease.Length == 0) return -1;
        for (int index = 0; index < Math.Min(preRelease.Length, other.preRelease.Length); index++)
        {
            result = CompareIdentifier(preRelease[index], other.preRelease[index]);
            if (result != 0) return result;
        }
        return preRelease.Length.CompareTo(other.preRelease.Length);
    }

    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);

    public override string ToString() => Text;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator ==(ReleaseVersion? left, ReleaseVersion? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(ReleaseVersion? left, ReleaseVersion? right) => !(left == right);

    private static int CompareIdentifier(string left, string right)
    {
        bool leftNumeric = left.All(char.IsAsciiDigit);
        bool rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            int lengthOrder = left.Length.CompareTo(right.Length);
            return lengthOrder != 0 ? lengthOrder : string.CompareOrdinal(left, right);
        }
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }

    private static bool TryNumber(string text, out int value)
    {
        value = 0;
        if (text.Length is 0 or > 6 || !text.All(char.IsAsciiDigit) || (text.Length > 1 && text[0] == '0')) return false;
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
