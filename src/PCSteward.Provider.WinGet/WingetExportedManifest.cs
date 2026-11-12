using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>Why an exported manifest was refused (D4b design §7 and §5 H7b). <see cref="None"/> only on success.</summary>
internal enum WingetManifestRefusal
{
    None,
    Malformed,
    IdentityMismatch,
    InstallerMismatch,
    ScopeNotMachine,
    NestedInstaller,
    DependenciesDeclared,
    AgreementsDeclared,
    /// <summary>The download folder did not hold exactly one manifest and one installer as plain files.</summary>
    FolderUnexpected,
    /// <summary>The downloaded installer's SHA-256 is not the one its manifest declares.</summary>
    InstallerHashMismatch,
}

/// <summary>
/// The facts read from an accepted manifest: the SHA-256 of its exact bytes, the installer SHA-256 it declares (uppercase hex) and its
/// installer type (one of the route's types).
/// </summary>
internal sealed record WingetManifestFacts(string ManifestDigest, string InstallerSha256, WinGetMachineInstallerType InstallerType);

/// <summary>
/// The values an exported manifest must carry: the WinGet text forms of the identity and the selected installer. A null
/// <see cref="InstallerType"/> (the review, which lets WinGet choose) accepts any type the route supports; the helper passes the
/// reviewed type, which must match exactly.
/// </summary>
internal sealed record WingetManifestExpectation(string PackageId, string PackageVersion, string Architecture, string? InstallerType);

/// <summary>
/// Phase D stage D4b design §7 and §5 H7b: a bounded, strict reader for the single-installer merged manifest that
/// <c>winget download</c> exports (WinGet 1.29.380 <c>YamlWriter::OutputYamlFile</c> through libyaml's block emitter). It does not
/// implement YAML. It recognises the emitter's layout and refuses anything else:
/// <list type="bullet">
/// <item>The file is UTF-8 with no byte-order mark, at most <see cref="MaximumBytes"/>, LF or CRLF line ends used consistently, and no
/// control character or tab.</item>
/// <item>Each line in column 0 is a root key (<c>Name:</c> or <c>Name: value</c>, each root key once), an item of the root key above
/// it (<c>- …</c>), or an optional document marker (<c>---</c> first, <c>...</c> last). Everything indented belongs to the root key
/// above it.</item>
/// <item><c>PackageIdentifier</c>, <c>PackageVersion</c> and <c>ManifestType: merged</c> are root keys with single-line values, and
/// <c>Installers</c> holds exactly one item. That item's own fields are its first line after <c>- </c> and the lines indented by
/// exactly two spaces. Each field appears at most once, and <c>Architecture</c>, <c>InstallerType</c>, <c>Scope: machine</c> and
/// <c>InstallerSha256</c> are required with single-line values.</item>
/// <item>Fail closed: any line anywhere whose first word is a dependency or agreement key (<c>Dependencies:</c>,
/// <c>PackageDependencies:</c>, <c>Agreements:</c> and so on) refuses the manifest, even when it is really part of a long folded
/// value. A false match only refuses; a real key is always such a line.</item>
/// </list>
/// The manifest digest is the SHA-256 of the exact bytes, so the review and the helper compare the same file content.
/// </summary>
internal static partial class WingetExportedManifest
{
    /// <summary>The largest exported manifest accepted (1 MiB).</summary>
    internal const int MaximumBytes = 1024 * 1024;

    private const int MaximumLines = 20_000;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly string[] DependencyKeys =
        ["Dependencies", "PackageDependencies", "WindowsFeatures", "WindowsLibraries", "ExternalDependencies"];

    private static readonly string[] AgreementKeys = ["Agreements", "Agreement", "AgreementLabel", "AgreementUrl"];

    // WinGet's exporter writes installer fields only inside the one Installers item; a root-level copy (a manifest default) would
    // let two values disagree, so it refuses.
    private static readonly string[] InstallerOnlyRootKeys =
        ["Architecture", "InstallerType", "NestedInstallerType", "NestedInstallerFiles", "InstallerSha256", "Scope", "InstallerUrl"];

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    /// <summary>Reads <paramref name="bytes"/> against <paramref name="expected"/>; facts are non-null exactly when the refusal is None.</summary>
    internal static WingetManifestRefusal Read(ReadOnlySpan<byte> bytes, WingetManifestExpectation expected, out WingetManifestFacts? facts)
    {
        ArgumentNullException.ThrowIfNull(expected);
        facts = null;
        if (bytes.IsEmpty || bytes.Length > MaximumBytes || bytes.StartsWith(ByteOrderMark)) return WingetManifestRefusal.Malformed;
        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return WingetManifestRefusal.Malformed;
        }
        if (!TrySplitLines(text, out List<string> lines)) return WingetManifestRefusal.Malformed;

        // Fail closed before anything else: a dependency or agreement key anywhere refuses, whatever the structure.
        foreach (string line in lines)
        {
            string word = FirstWord(line);
            if (DependencyKeys.Contains(word, StringComparer.Ordinal)) return WingetManifestRefusal.DependenciesDeclared;
            if (AgreementKeys.Contains(word, StringComparer.Ordinal)) return WingetManifestRefusal.AgreementsDeclared;
        }

        if (!TryReadRoot(lines, out Dictionary<string, RootKey> root) || InstallerOnlyRootKeys.Any(root.ContainsKey))
            return WingetManifestRefusal.Malformed;
        if (!root.TryGetValue("PackageIdentifier", out RootKey? id) || !root.TryGetValue("PackageVersion", out RootKey? version) ||
            !root.TryGetValue("ManifestType", out RootKey? type) || !root.TryGetValue("Installers", out RootKey? installers) ||
            !root.TryGetValue("ManifestVersion", out RootKey? manifestVersion) ||
            !TrySingleLineValue(id, out string idText) || !TrySingleLineValue(version, out string versionText) ||
            !TrySingleLineValue(type, out string typeText) || !TrySingleLineValue(manifestVersion, out _) ||
            installers.InlineValue is not null || !string.Equals(typeText, "merged", StringComparison.Ordinal))
            return WingetManifestRefusal.Malformed;
        if (!string.Equals(idText, expected.PackageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(versionText, expected.PackageVersion, StringComparison.Ordinal))
            return WingetManifestRefusal.IdentityMismatch;

        if (!TryReadSingleInstaller(installers, out Dictionary<string, string?> fields)) return WingetManifestRefusal.Malformed;
        if (fields.ContainsKey("NestedInstallerType") || fields.ContainsKey("NestedInstallerFiles")) return WingetManifestRefusal.NestedInstaller;
        // WinGet omits Scope when the manifest does not declare it; strict machine scope (design Q1) refuses that.
        if (!fields.ContainsKey("Scope")) return WingetManifestRefusal.ScopeNotMachine;
        if (!TryField(fields, "Architecture", out string architecture) || !TryField(fields, "InstallerType", out string installerType) ||
            !TryField(fields, "InstallerSha256", out string sha256) || !TryField(fields, "Scope", out string scope) ||
            sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit))
            return WingetManifestRefusal.Malformed;
        if (!string.Equals(architecture, expected.Architecture, StringComparison.Ordinal) ||
            (expected.InstallerType is not null && !string.Equals(installerType, expected.InstallerType, StringComparison.Ordinal)) ||
            !WinGetMachineEvidence.TryParseInstallerType(installerType, out WinGetMachineInstallerType parsedType))
            return WingetManifestRefusal.InstallerMismatch;
        if (!string.Equals(scope, "machine", StringComparison.Ordinal)) return WingetManifestRefusal.ScopeNotMachine;

        facts = new(Convert.ToHexString(SHA256.HashData(bytes)), sha256.ToUpperInvariant(), parsedType);
        return WingetManifestRefusal.None;
    }

    private sealed class RootKey(string? inlineValue)
    {
        internal string? InlineValue { get; } = inlineValue;
        internal List<string> Body { get; } = [];
    }

    private static bool TrySplitLines(string text, out List<string> lines)
    {
        lines = [];
        string[] raw = text.Split('\n');
        // The emitter ends the stream with a line end, so the last split element is empty.
        if (raw.Length < 2 || raw[^1].Length != 0 || raw.Length - 1 > MaximumLines) return false;
        bool crlf = raw[0].EndsWith('\r');
        for (int index = 0; index < raw.Length - 1; index++)
        {
            string line = raw[index];
            if (line.EndsWith('\r') != crlf) return false;
            if (crlf) line = line[..^1];
            if (line.Any(static character => char.IsControl(character))) return false;
            lines.Add(line);
        }
        if (lines.Count > 0 && lines[0] == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1] == "...") lines.RemoveAt(lines.Count - 1);
        return lines.Count > 0;
    }

    /// <summary>The leading key-like word of a line after its indentation and any <c>- </c> markers, or empty.</summary>
    private static string FirstWord(string line)
    {
        ReadOnlySpan<char> rest = line.AsSpan().TrimStart(' ');
        while (rest.StartsWith("- ")) rest = rest[2..].TrimStart(' ');
        int colon = rest.IndexOf(':');
        if (colon <= 0 || (colon + 1 < rest.Length && rest[colon + 1] != ' ')) return string.Empty;
        ReadOnlySpan<char> word = rest[..colon];
        foreach (char character in word)
            if (!char.IsAsciiLetterOrDigit(character)) return string.Empty;
        return word.ToString();
    }

    private static bool TryReadRoot(List<string> lines, out Dictionary<string, RootKey> root)
    {
        root = new(StringComparer.Ordinal);
        RootKey? current = null;
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == ' ' || line.StartsWith("- ", StringComparison.Ordinal))
            {
                // Indented content, an item, or a blank line inside a folded value: it belongs to the root key above it.
                if (current is null) return false;
                current.Body.Add(line);
                continue;
            }
            if (!TryKeyLine(line, out string key, out string? value) || !root.TryAdd(key, current = new RootKey(value))) return false;
        }
        return root.Count > 0;
    }

    /// <summary>A whole line that is exactly <c>Key:</c> or <c>Key: value</c> with an ASCII letter-or-digit key.</summary>
    private static bool TryKeyLine(string line, out string key, out string? value)
    {
        Match match = KeyLine().Match(line);
        key = match.Success ? match.Groups["key"].Value : string.Empty;
        value = match.Success && match.Groups["value"].Success ? match.Groups["value"].Value : null;
        return match.Success;
    }

    /// <summary>The root key's value, which must be inline and not continue on the next lines.</summary>
    private static bool TrySingleLineValue(RootKey key, out string value)
    {
        value = string.Empty;
        return key.InlineValue is not null && key.Body.Count == 0 && TryScalar(key.InlineValue, out value);
    }

    /// <summary>
    /// The one <c>Installers</c> item: its first line starts with <c>- </c> in column 0 and no other line does; its fields are that first
    /// line and the lines indented by exactly two spaces; deeper lines belong to the field above them.
    /// </summary>
    private static bool TryReadSingleInstaller(RootKey installers, out Dictionary<string, string?> fields)
    {
        fields = new(StringComparer.Ordinal);
        List<string> body = installers.Body;
        if (body.Count == 0 || !body[0].StartsWith("- ", StringComparison.Ordinal) ||
            body.Skip(1).Any(static line => line.StartsWith("- ", StringComparison.Ordinal)))
            return false;
        string? lastField = null;
        bool lastFieldContinues = false;
        for (int index = 0; index < body.Count; index++)
        {
            string line = index == 0 ? body[0][2..] : body[index];
            bool fieldLine = index == 0 || (line.StartsWith("  ", StringComparison.Ordinal) && line.Length > 2 && line[2] != ' ' && line[2] != '-');
            if (fieldLine)
            {
                if (!TryKeyLine(index == 0 ? line : line[2..], out string key, out string? value) || !fields.TryAdd(key, value)) return false;
                lastField = key;
                lastFieldContinues = false;
                continue;
            }
            // A nested or continuation line: it must be indented under a field.
            if (lastField is null || !(line.Length == 0 || line.StartsWith("  ", StringComparison.Ordinal))) return false;
            if (!lastFieldContinues && fields[lastField] is not null)
            {
                // The field's inline value continues: it is no longer a single-line value.
                fields[lastField] = null;
            }
            lastFieldContinues = true;
        }
        return true;
    }

    private static bool TryField(Dictionary<string, string?> fields, string key, out string value)
    {
        value = string.Empty;
        return fields.TryGetValue(key, out string? inline) && inline is not null && TryScalar(inline, out value);
    }

    /// <summary>A single-line scalar: plain, or single- or double-quoted with nothing that needs unescaping.</summary>
    private static bool TryScalar(string raw, out string value)
    {
        value = string.Empty;
        if (raw.Length == 0 || raw.Length > 512) return false;
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
        {
            value = raw[1..^1];
            return !value.Contains('\'', StringComparison.Ordinal) && value.Length > 0;
        }
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
        {
            value = raw[1..^1];
            return !value.Contains('"', StringComparison.Ordinal) && !value.Contains('\\', StringComparison.Ordinal) && value.Length > 0;
        }
        if (!PlainScalar().IsMatch(raw)) return false;
        value = raw;
        return true;
    }

    [GeneratedRegex(@"^(?<key>[A-Za-z][A-Za-z0-9]{0,63}):(?: (?<value>\S.*))?\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyLine();

    /// <summary>The plain values this reader compares: identifiers, versions, architecture, type, scope and hex digests.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,255}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PlainScalar();
}
