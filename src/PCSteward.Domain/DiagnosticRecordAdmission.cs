using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PCSteward.Domain;

/// <summary>
/// FR-DIA-01/03: what a reader hands over before admission. Text here is untrusted source data; nothing in it is stored, shown
/// or exported until <see cref="DiagnosticRecordAdmission"/> has validated and sanitized it.
/// </summary>
public sealed record DiagnosticRecordCandidate(
    DiagnosticSourceKind Source,
    string? SourceRecordId,
    DiagnosticOccurrence Occurrence,
    DiagnosticEventKind Kind,
    DiagnosticOutcomeKind Outcome,
    int? EventId = null,
    long? ResultCode = null,
    string? Subject = null,
    string? Detail = null,
    string? Summary = null);

/// <summary>FR-DIA-03 (ADR-010/016): device and account names to remove from text, supplied by the host, never stored.</summary>
public sealed record DiagnosticRedactionContext(IReadOnlyList<string> IdentityTokens)
{
    public static DiagnosticRedactionContext None { get; } = new([]);
}

/// <summary>FR-DIA-03: a sanitized text value and whether it was cut to fit.</summary>
public readonly record struct DiagnosticSanitizedText(string? Value, bool Truncated);

/// <summary>
/// FR-DIA-03 (ADR-010/016/027): treats hostile source text as data. Removes control, format and bidirectional characters,
/// collapses whitespace, replaces paths, links, account SIDs, email addresses and supplied device/account names with fixed
/// placeholders, then cuts to a Unicode-scalar limit without splitting a character. It never interprets the text.
/// </summary>
public static class DiagnosticTextSanitizer
{
    public const string PathPlaceholder = "[path]";
    public const string LinkPlaceholder = "[link]";
    public const string AccountPlaceholder = "[account]";
    public const string EmailPlaceholder = "[email]";
    public const string IdentityPlaceholder = "[redacted]";
    public const string Ellipsis = "…";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.NonBacktracking;
    private static readonly Regex Link = new(@"[A-Za-z][A-Za-z0-9+.\-]*://\S+", Options, MatchTimeout);
    private static readonly Regex UncPath = new(@"\\\\[^\\/\s]+(?:[\\/](?:[^\\/\r\n""<>|:*?]*[\\/])*[^\\/\s""<>|:*?]*)?", Options, MatchTimeout);
    private static readonly Regex DrivePath = new(@"[A-Za-z]:[\\/](?:[^\\/\r\n""<>|:*?]*[\\/])*[^\\/\s""<>|:*?]*", Options, MatchTimeout);
    // Same structure as a drive path: folders may contain spaces, the last segment ends at whitespace.
    private static readonly Regex VariablePath = new(@"%[A-Za-z_][A-Za-z0-9_]*%(?:[\\/](?:[^\\/\r\n""<>|:*?]*[\\/])*[^\\/\s""<>|:*?]*)?", Options, MatchTimeout);
    private static readonly Regex Sid = new(@"S-1-[0-9]+(?:-[0-9]+)+", Options | RegexOptions.IgnoreCase, MatchTimeout);
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", Options, MatchTimeout);
    private static readonly Regex Whitespace = new(@"\s+", Options, MatchTimeout);

    public static DiagnosticSanitizedText Sanitize(string? text, int maximumScalars, DiagnosticRedactionContext? context = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumScalars, 2);
        if (string.IsNullOrWhiteSpace(text)) return new(null, false);

        StringBuilder cleaned = new(Math.Min(text.Length, 16 * 1024));
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (cleaned.Length >= 16 * 1024) break;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) cleaned.Append(' ');
            else if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned) continue;
            else if (rune == Rune.ReplacementChar) cleaned.Append('?');
            else cleaned.Append(rune.ToString());
        }

        // Structured values go first, so an identity token can never break a path, link, SID or address apart before it is
        // recognised (a one-letter account name "c" would otherwise turn "C:\Users\..." into text no path pattern matches).
        string value = cleaned.ToString();
        value = Link.Replace(value, LinkPlaceholder);
        value = UncPath.Replace(value, PathPlaceholder);
        value = DrivePath.Replace(value, PathPlaceholder);
        value = VariablePath.Replace(value, PathPlaceholder);
        value = Sid.Replace(value, AccountPlaceholder);
        value = Email.Replace(value, EmailPlaceholder);
        foreach (string token in (context ?? DiagnosticRedactionContext.None).IdentityTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(token => token.Length)
            .ThenBy(token => token, StringComparer.Ordinal))
            value = ReplaceWholeWord(value, token);
        value = Whitespace.Replace(value, " ").Trim();
        if (value.Length == 0) return new(null, false);
        return Truncate(value, maximumScalars);
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
        return new(cut.ToString().TrimEnd() + Ellipsis, true);
    }

    private static string ReplaceWholeWord(string value, string token)
    {
        StringBuilder result = new(value.Length);
        int index = 0;
        while (index < value.Length)
        {
            int found = value.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            int end = found + token.Length;
            bool startBoundary = found == 0 || !char.IsLetterOrDigit(value[found - 1]);
            bool endBoundary = end == value.Length || !char.IsLetterOrDigit(value[end]);
            result.Append(value, index, found - index);
            result.Append(startBoundary && endBoundary ? IdentityPlaceholder : value.Substring(found, token.Length));
            index = end;
        }
        result.Append(value, index, value.Length - index);
        return result.ToString();
    }
}

/// <summary>Why a candidate was not admitted. Rejected candidates still count against the matching-record limit (ADR-027 R3).</summary>
public enum DiagnosticRejection
{
    None,
    UndefinedValue,
    KindNotAllowedForSource,
    InvalidRecordId,
    InvalidEventId,
    InconsistentTime,
    TooLarge
}

/// <summary>FR-DIA-03: the admission result for one candidate.</summary>
public readonly record struct DiagnosticAdmission(DiagnosticRecord? Record, DiagnosticRejection Rejection, int SerializedBytes)
{
    public bool Admitted => Record is not null;
}

/// <summary>
/// FR-DIA-01/03 (ADR-027 Q1): validates a candidate against the closed source/kind/outcome sets, sanitizes every text field
/// before anything else sees it, and enforces the 4 KiB serialized-record limit. Oversized summaries are cut until the record
/// fits; a record that cannot fit without its summary is rejected.
/// </summary>
public static class DiagnosticRecordAdmission
{
    public static DiagnosticAdmission Admit(DiagnosticRecordCandidate candidate, DiagnosticRedactionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!Enum.IsDefined(candidate.Source) || !Enum.IsDefined(candidate.Kind) || !Enum.IsDefined(candidate.Outcome) ||
            candidate.Occurrence is null || !Enum.IsDefined(candidate.Occurrence.Quality))
            return Reject(DiagnosticRejection.UndefinedValue);
        if (!KindAllowed(candidate.Source, candidate.Kind)) return Reject(DiagnosticRejection.KindNotAllowedForSource);
        if (candidate.EventId is < 0 or > ushort.MaxValue) return Reject(DiagnosticRejection.InvalidEventId);
        bool hasTime = candidate.Occurrence.Utc is not null;
        bool needsTime = candidate.Occurrence.Quality is DiagnosticTimeQuality.Known or DiagnosticTimeQuality.Future or DiagnosticTimeQuality.Conflicting;
        if (hasTime != needsTime || (hasTime && candidate.Occurrence.Utc!.Value.Offset != TimeSpan.Zero)) return Reject(DiagnosticRejection.InconsistentTime);

        DiagnosticSanitizedText subject = DiagnosticTextSanitizer.Sanitize(candidate.Subject, DiagnosticCollectionPolicy.MaximumSubjectScalars, context);
        DiagnosticSanitizedText detail = DiagnosticTextSanitizer.Sanitize(candidate.Detail, DiagnosticCollectionPolicy.MaximumDetailScalars, context);
        DiagnosticSanitizedText summary = DiagnosticTextSanitizer.Sanitize(candidate.Summary, DiagnosticCollectionPolicy.MaximumSummaryScalars, context);

        bool weak = string.IsNullOrEmpty(candidate.SourceRecordId);
        if (!weak && !IsValidRecordId(candidate.SourceRecordId!)) return Reject(DiagnosticRejection.InvalidRecordId);

        DiagnosticRecord record = new(candidate.Source, weak ? string.Empty : candidate.SourceRecordId!, weak, candidate.Occurrence, candidate.Kind, candidate.Outcome,
            candidate.EventId, candidate.ResultCode, subject.Value, detail.Value, summary.Value, subject.Truncated || detail.Truncated || summary.Truncated);
        if (weak) record = record with { SourceRecordId = WeakIdentity(record) };

        int bytes = DiagnosticRecordJson.ByteCount(record);
        int summaryLimit = DiagnosticCollectionPolicy.MaximumSummaryScalars;
        int detailLimit = DiagnosticCollectionPolicy.MaximumDetailScalars;
        int subjectLimit = DiagnosticCollectionPolicy.MaximumSubjectScalars;
        while (bytes > DiagnosticCollectionPolicy.MaximumRecordBytes && (record.Summary ?? record.Detail ?? record.Subject) is not null)
        {
            // Shorten the least essential text first: summary, then detail, then subject. Each step halves the scalar limit.
            if (record.Summary is not null) record = record with { Summary = Shorten(record.Summary, summaryLimit /= 2), TextTruncated = true };
            else if (record.Detail is not null) record = record with { Detail = Shorten(record.Detail, detailLimit /= 2), TextTruncated = true };
            else record = record with { Subject = Shorten(record.Subject!, subjectLimit /= 2), TextTruncated = true };
            bytes = DiagnosticRecordJson.ByteCount(record);
        }
        return bytes > DiagnosticCollectionPolicy.MaximumRecordBytes ? Reject(DiagnosticRejection.TooLarge) : new(record, DiagnosticRejection.None, bytes);
    }

    private static string? Shorten(string value, int limit) => limit < 2 ? null : DiagnosticTextSanitizer.Sanitize(value, limit).Value;

    /// <summary>ADR-027 Q1: each source may only produce its own closed set of event kinds.</summary>
    public static bool KindAllowed(DiagnosticSourceKind source, DiagnosticEventKind kind) => source switch
    {
        DiagnosticSourceKind.PcStewardHistory => kind == DiagnosticEventKind.PcStewardUpdate,
        DiagnosticSourceKind.SystemEventLog => kind is DiagnosticEventKind.UnexpectedShutdown or DiagnosticEventKind.BugCheck,
        DiagnosticSourceKind.ApplicationEventLog => kind is DiagnosticEventKind.ApplicationCrash or DiagnosticEventKind.ApplicationHang or DiagnosticEventKind.ErrorReport,
        DiagnosticSourceKind.Reliability => kind is not (DiagnosticEventKind.PcStewardUpdate),
        DiagnosticSourceKind.WindowsUpdateHistory => kind == DiagnosticEventKind.WindowsUpdate,
        _ => false
    };

    /// <summary>
    /// FR-DIA-03: re-checks a record read back from storage. It must satisfy every admission rule and its text must already be in
    /// sanitized form, so a stored row cannot carry anything admission would have changed or refused.
    /// </summary>
    public static bool IsValidStored(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!Enum.IsDefined(record.Source) || !Enum.IsDefined(record.Kind) || !Enum.IsDefined(record.Outcome) || record.Occurrence is null ||
            !Enum.IsDefined(record.Occurrence.Quality) || !KindAllowed(record.Source, record.Kind) || !IsValidRecordId(record.SourceRecordId) ||
            record.EventId is < 0 or > ushort.MaxValue)
            return false;
        bool hasTime = record.Occurrence.Utc is not null;
        bool needsTime = record.Occurrence.Quality is DiagnosticTimeQuality.Known or DiagnosticTimeQuality.Future or DiagnosticTimeQuality.Conflicting;
        if (hasTime != needsTime || (hasTime && record.Occurrence.Utc!.Value.Offset != TimeSpan.Zero)) return false;
        return IsSanitized(record.Subject, DiagnosticCollectionPolicy.MaximumSubjectScalars) &&
            IsSanitized(record.Detail, DiagnosticCollectionPolicy.MaximumDetailScalars) &&
            IsSanitized(record.Summary, DiagnosticCollectionPolicy.MaximumSummaryScalars) &&
            DiagnosticRecordJson.ByteCount(record) <= DiagnosticCollectionPolicy.MaximumRecordBytes;
    }

    private static bool IsSanitized(string? value, int maximumScalars) =>
        value is null || string.Equals(DiagnosticTextSanitizer.Sanitize(value, maximumScalars).Value, value, StringComparison.Ordinal);

    public static bool IsValidRecordId(string value) =>
        value.Length is > 0 and <= DiagnosticCollectionPolicy.MaximumSourceRecordIdLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or ':' or '-' or '{' or '}');

    private static string WeakIdentity(DiagnosticRecord record)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(DiagnosticRecordJson.Serialize(record)));
        return "weak-" + Convert.ToHexStringLower(hash.AsSpan(0, 8));
    }

    private static DiagnosticAdmission Reject(DiagnosticRejection reason) => new(null, reason, 0);
}

/// <summary>
/// FR-DIA-03/05: the one canonical JSON projection of a record, used for size accounting, weak-identity hashing and export, so
/// the bytes counted are the bytes written. Field order is fixed; absent values are omitted.
/// </summary>
public static class DiagnosticRecordJson
{
    // Non-ASCII text is written as UTF-8 rather than \u escapes so the 4 KiB limit is spent on content. Quotes, backslashes and
    // control characters are still escaped. This JSON is stored in SQLite and saved as a file; it is never embedded in HTML or script.
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

    public static string Serialize(DiagnosticRecord record) => Encoding.UTF8.GetString(SerializeToUtf8(record));

    public static int ByteCount(DiagnosticRecord record) => SerializeToUtf8(record).Length;

    public static byte[] SerializeToUtf8(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            Write(writer, record);
        }
        return stream.ToArray();
    }

    public static void Write(Utf8JsonWriter writer, DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(record);
        writer.WriteStartObject();
        writer.WriteString("source", record.Source.ToString());
        writer.WriteString("id", record.SourceRecordId);
        if (record.WeakIdentity) writer.WriteBoolean("weakId", true);
        if (record.Occurrence.Utc is DateTimeOffset utc) writer.WriteString("utc", utc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        if (record.Occurrence.OriginalOffset is TimeSpan offset) writer.WriteNumber("offsetMinutes", (int)offset.TotalMinutes);
        writer.WriteString("time", record.Occurrence.Quality.ToString());
        writer.WriteString("kind", record.Kind.ToString());
        writer.WriteString("outcome", record.Outcome.ToString());
        if (record.EventId is int eventId) writer.WriteNumber("eventId", eventId);
        if (record.ResultCode is long code) writer.WriteNumber("code", code);
        if (record.Subject is not null) writer.WriteString("subject", record.Subject);
        if (record.Detail is not null) writer.WriteString("detail", record.Detail);
        if (record.Summary is not null) writer.WriteString("summary", record.Summary);
        if (record.TextTruncated) writer.WriteBoolean("textTruncated", true);
        writer.WriteEndObject();
    }

    private static readonly string[] KnownProperties = ["source", "id", "weakId", "utc", "offsetMinutes", "time", "kind", "outcome", "eventId", "code", "subject", "detail", "summary", "textTruncated"];

    /// <summary>
    /// FR-DIA-03: reads a stored record strictly. Unknown, duplicate or mistyped properties, numeric enum names and anything that does
    /// not serialize back to exactly the same bytes are refused, and the result must pass <see cref="DiagnosticRecordAdmission.IsValidStored"/>.
    /// </summary>
    public static bool TryParse(string? json, out DiagnosticRecord? record)
    {
        record = null;
        if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > DiagnosticCollectionPolicy.MaximumRecordBytes) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 2, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in root.EnumerateObject())
                if (!KnownProperties.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) return false;

            if (!TryEnum(root, "source", out DiagnosticSourceKind source) || !TryEnum(root, "time", out DiagnosticTimeQuality quality) ||
                !TryEnum(root, "kind", out DiagnosticEventKind kind) || !TryEnum(root, "outcome", out DiagnosticOutcomeKind outcome) ||
                !TryString(root, "id", required: true, out string? id))
                return false;
            DateTimeOffset? utc = null;
            if (root.TryGetProperty("utc", out JsonElement utcElement))
            {
                if (utcElement.ValueKind != JsonValueKind.String ||
                    !DateTime.TryParseExact(utcElement.GetString(), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed) ||
                    parsed.Kind != DateTimeKind.Utc)
                    return false;
                utc = new DateTimeOffset(parsed);
            }
            TimeSpan? offset = null;
            if (root.TryGetProperty("offsetMinutes", out JsonElement offsetElement))
            {
                if (offsetElement.ValueKind != JsonValueKind.Number || !offsetElement.TryGetInt32(out int minutes) || minutes is < -14 * 60 or > 14 * 60) return false;
                offset = TimeSpan.FromMinutes(minutes);
            }
            if (!TryBool(root, "weakId", out bool weak) || !TryBool(root, "textTruncated", out bool truncated) ||
                !TryInt(root, "eventId", out int? eventId) || !TryLong(root, "code", out long? code) ||
                !TryString(root, "subject", required: false, out string? subject) || !TryString(root, "detail", required: false, out string? detail) ||
                !TryString(root, "summary", required: false, out string? summary))
                return false;

            DiagnosticRecord candidate = new(source, id!, weak, new(utc, offset, quality), kind, outcome, eventId, code, subject, detail, summary, truncated);
            if (!DiagnosticRecordAdmission.IsValidStored(candidate) || !string.Equals(Serialize(candidate), json, StringComparison.Ordinal)) return false;
            record = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryEnum<TEnum>(JsonElement root, string name, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String &&
            Enum.GetNames<TEnum>().Contains(element.GetString(), StringComparer.Ordinal) && Enum.TryParse(element.GetString(), false, out value);
    }

    private static bool TryString(JsonElement root, string name, bool required, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element)) return !required;
        if (element.ValueKind != JsonValueKind.String) return false;
        value = element.GetString();
        return true;
    }

    private static bool TryBool(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out JsonElement element)) return true;
        if (element.ValueKind != JsonValueKind.True) return false;
        value = true;
        return true;
    }

    private static bool TryInt(JsonElement root, string name, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element)) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool TryLong(JsonElement root, string name, out long? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement element)) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out long parsed)) return false;
        value = parsed;
        return true;
    }
}
