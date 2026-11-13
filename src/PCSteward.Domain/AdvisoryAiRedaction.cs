using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

/// <summary>
/// FR-AI-02/05 (ADR-028 "Consent, redaction and exact payload approval" item 3): redaction applied to every evidence text before
/// the owner sees a preview, again to every owner edit, and to every answer before it is shown or saved.
/// <para>
/// It first applies <see cref="DiagnosticTextSanitizer"/> (control, format and bidirectional characters, paths, links, account
/// SIDs, email addresses and the host-supplied account, domain and computer names), then the classes ADR-028 adds for text that
/// leaves the PC: secrets and keys (bearer tokens, <c>name=value</c> secrets, well-known key prefixes, JSON web tokens, product keys,
/// long hexadecimal or base64 runs), device instance paths, GUIDs and hardware addresses. All text is hostile data: nothing here
/// interprets it. Redaction is best effort, so the preview always says some secrets may remain.
/// </para>
/// </summary>
public static class AdvisoryAiRedaction
{
    public const string SecretPlaceholder = "[secret]";
    public const string IdPlaceholder = "[id]";

    /// <summary>The longest text accepted for redaction; anything longer is cut first and marked as cut.</summary>
    public const int MaximumInputScalars = 16 * 1024;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;

    private static readonly Regex Bearer = new(@"(?i)\bbearer\s+[A-Za-z0-9._~+/=\-]{6,}", Options, MatchTimeout);
    private static readonly Regex NamedSecret = new(
        @"(?i)\b(password|passwd|pwd|passphrase|secret|client[_\- ]?secret|token|access[_\- ]?token|refresh[_\- ]?token|api[_\- ]?key|access[_\- ]?key|private[_\- ]?key|licen[cs]e[_\- ]?key|product[_\- ]?key|serial[_\- ]?number|serial[_\- ]?no|serial|s/n)\s*[:=]\s*[^\s,;]+",
        Options, MatchTimeout);
    private static readonly Regex PrefixedKey = new(
        @"\b(?:sk|rk|pk)-[A-Za-z0-9_\-]{16,}|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_[A-Za-z0-9_]{20,}|\bAKIA[0-9A-Z]{16}\b|\bxox[abprs]-[A-Za-z0-9\-]{10,}|\bAIza[0-9A-Za-z_\-]{30,}",
        Options, MatchTimeout);
    private static readonly Regex JsonWebToken = new(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]*", Options, MatchTimeout);
    private static readonly Regex ProductKey = new(@"\b[A-Za-z0-9]{5}(?:-[A-Za-z0-9]{5}){4}\b", Options, MatchTimeout);
    private static readonly Regex DeviceInstancePath = new(
        @"(?i)\b(?:USB|USBSTOR|PCI|PCIIDE|HID|HDAUDIO|SWD|ACPI|SCSI|STORAGE|BTH|BTHENUM|BTHLE|BTHLEDEVICE|DISPLAY|MONITOR|ROOT|NVME|SD|UMB|WPDBUSENUMROOT)\\[^\s""'<>|]+",
        Options, MatchTimeout);
    private static readonly Regex Guid = new(@"\{?\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b\}?", Options, MatchTimeout);
    private static readonly Regex HardwareAddress = new(@"\b[0-9A-Fa-f]{2}(?:[:\-][0-9A-Fa-f]{2}){5}\b", Options, MatchTimeout);
    private static readonly Regex LongHex = new(@"\b[0-9A-Fa-f]{32,}\b", Options, MatchTimeout);
    private static readonly Regex LongBase64 = new(@"[A-Za-z0-9+/_\-]{40,}={0,2}", Options, MatchTimeout);
    private static readonly Regex Spaces = new(@"[ \t]+", Options, MatchTimeout);

    /// <summary>
    /// Redacts one evidence text, collapses all whitespace (line breaks included) to single spaces and cuts it to
    /// <paramref name="maximumScalars"/> Unicode scalars. Applying it to its own output changes nothing.
    /// </summary>
    public static DiagnosticSanitizedText SanitizeEvidence(string? text, int maximumScalars, DiagnosticRedactionContext? context = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumScalars, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumScalars, MaximumInputScalars);
        if (string.IsNullOrWhiteSpace(text)) return new(null, false);
        (string bounded, bool cut) = Bound(text);
        string? cleaned = DiagnosticTextSanitizer.Sanitize(bounded, MaximumInputScalars, context).Value;
        if (cleaned is null) return new(null, false);
        string value = RedactSecrets(cleaned);
        value = Spaces.Replace(value, " ").Trim();
        if (value.Length == 0) return new(null, false);
        DiagnosticSanitizedText result = Truncate(value, maximumScalars);
        return result with { Truncated = result.Truncated || cut };
    }

    /// <summary>
    /// FR-AI-05 (ADR-028): redacts an answer for display and saving. The same classes as evidence are removed, line by line, so the
    /// answer keeps its paragraphs; runs of blank lines become one. The result is plain text: it is never parsed as markup, links are
    /// already replaced and nothing in it is ever run or opened.
    /// </summary>
    public static DiagnosticSanitizedText SanitizeAnswer(string? text, int maximumScalars, DiagnosticRedactionContext? context = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumScalars, 2);
        if (string.IsNullOrWhiteSpace(text)) return new(null, false);
        List<string> lines = [];
        bool blankPending = false;
        foreach (string line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split(['\n', '\r', '\u2028', '\u2029', '\u0085']))
        {
            string? clean = line.Length == 0 ? null : SanitizeEvidence(line.Length > MaximumInputScalars ? line[..MaximumInputScalars] : line, MaximumInputScalars, context).Value;
            if (clean is null)
            {
                blankPending = lines.Count > 0;
                continue;
            }
            if (blankPending) lines.Add(string.Empty);
            blankPending = false;
            lines.Add(clean);
        }
        if (lines.Count == 0) return new(null, false);
        return Truncate(string.Join('\n', lines), maximumScalars);
    }

    private static string RedactSecrets(string value)
    {
        value = Bearer.Replace(value, "Bearer " + SecretPlaceholder);
        value = NamedSecret.Replace(value, "$1=" + SecretPlaceholder);
        value = PrefixedKey.Replace(value, SecretPlaceholder);
        value = JsonWebToken.Replace(value, SecretPlaceholder);
        value = DeviceInstancePath.Replace(value, IdPlaceholder);
        value = Guid.Replace(value, IdPlaceholder);
        value = ProductKey.Replace(value, SecretPlaceholder);
        value = HardwareAddress.Replace(value, IdPlaceholder);
        value = LongHex.Replace(value, SecretPlaceholder);
        value = LongBase64.Replace(value, SecretPlaceholder);
        return value;
    }

    private static (string Value, bool Cut) Bound(string text)
    {
        int scalars = 0;
        int index = 0;
        while (index < text.Length)
        {
            if (scalars == MaximumInputScalars) return (text[..index], true);
            index += char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;
            scalars++;
        }
        return (text, false);
    }

    private static DiagnosticSanitizedText Truncate(string value, int maximumScalars)
    {
        int scalars = 0;
        foreach (Rune _ in value.EnumerateRunes()) scalars++;
        if (scalars <= maximumScalars) return new(value, false);
        StringBuilder cut = new();
        int kept = 0;
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (kept == maximumScalars - 1) break;
            cut.Append(rune.ToString());
            kept++;
        }
        return new(cut.ToString().TrimEnd() + DiagnosticTextSanitizer.Ellipsis, true);
    }
}
