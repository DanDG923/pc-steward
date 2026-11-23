using System.Buffers;
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
    string? Summary = null)
{
    /// <summary>
    /// A matching source record the reader could not read. Admission always rejects it (its event ID is out of range), so it is
    /// counted as rejected and the source is reported as partial instead of being silently dropped.
    /// </summary>
    public static DiagnosticRecordCandidate Unreadable(DiagnosticSourceKind source) =>
        new(source, null, DiagnosticOccurrence.Unknown, DiagnosticEventKind.PcStewardUpdate, DiagnosticOutcomeKind.Unknown, EventId: -1);
}

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
    // Independent review of 4d2e238, Q7: a file link names a path, so its folder names may hold spaces as a drive path's may, and
    // it is removed the way a drive path is: whole folders, then a last part that runs to whitespace, so a query string or fragment
    // goes with it as it did with the general link pattern (Codex review of #146). Quoted paths are removed before links, so a quoted
    // command line holding a file link is removed whole before this pattern could take its closing quote.
    // Independent review of 1e57444, R5: a host may come before the drive (file://localhost/C:/...), and a host may be an IPv6
    // address in brackets or carry a port (Codex review of #147); otherwise it holds no colon, so "file:file:///C:/..." is not
    // read as a host named "file:". A user name and password may come before the host (independent review of 5526f3d, S2). R6
    // is left as it is: the pattern
    // also matches inside a longer word ("profile:C:\..." becomes "pro[link]"), which only removes more, and a scheme such as
    // "logfile:///C:/My Documents/x.txt" would otherwise go to the general link pattern, which ends at the first space.
    private static readonly Regex FileLink = new(@"[Ff][Ii][Ll][Ee]:[\\/]*(?:[A-Za-z]:[\\/]|(?:[^\\/\s""<>|*?:@]+(?::[^\\/\s""<>|*?:@]*)?@)?(?:\[[^\]\s\\/]*\]|[^\\/\s""<>|*?:]+)(?::[0-9]+)?[\\/](?:[A-Za-z]:[\\/])?)(?:[^\\/\r\n""<>|:*?]*[\\/])*(?<last>\S*)", Options, MatchTimeout);
    // A quoted path is removed up to its closing quote, so a file or folder name with spaces cannot leave a suffix behind.
    private static readonly Regex DoubleQuotedPath = new(@"""(?:[A-Za-z]:[\\/]|\\\\|%[A-Za-z_][A-Za-z0-9_]*%)[^""\r\n]*""", Options, MatchTimeout);
    // Independent review of cd04620, P7: an apostrophe followed by a letter or digit (O'Brien) is part of a single-quoted path, so the
    // rest of the path isn't left visible after the first one. Independent review of 4d2e238, Q6: so is a doubled apostrophe
    // (PowerShell's O''Brien), and so is one before a letter outside the Basic Multilingual Plane (Codex review of bf637b5, 71C-04).
    // The rest of the line after an apostrophe before a space ('C:\Music\Guns N' Roses\x.mp3') is taken by RemoveSingleQuotedPaths.
    private static readonly Regex SingleQuotedPath = new(@"'(?:[A-Za-z]:[\\/]|\\\\|%[A-Za-z_][A-Za-z0-9_]*%|[Ff][Ii][Ll][Ee]:(?:[\\/]|[A-Za-z]:[\\/])|[A-Za-z][A-Za-z0-9+.\-]*://)(?:[^'\r\n]|'[\p{L}\p{N}\uD800-\uDBFF]|'')*'", Options, MatchTimeout);
    private static readonly Regex UncPath = new(@"\\\\[^\\/\s]+(?:[\\/](?:[^\\/\r\n""<>|:*?]*[\\/])*[^\\/\s""<>|:*?]*)?", Options, MatchTimeout);
    private static readonly Regex DrivePath = new(@"[A-Za-z]:[\\/](?:[^\\/\r\n""<>|:*?]*[\\/])*[^\\/\s""<>|:*?]*", Options, MatchTimeout);
    // Same structure as a drive path: folders may contain spaces, the last segment ends at whitespace.
    private static readonly Regex VariablePath = new(@"%[A-Za-z_][A-Za-z0-9_]*%(?:[\\/](?:[^\\/\r\n""<>|:*?]*[\\/])*[^\\/\s""<>|:*?]*)?", Options, MatchTimeout);
    private static readonly Regex Sid = new(@"S-1-[0-9]+(?:-[0-9]+)+", Options | RegexOptions.IgnoreCase, MatchTimeout);
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", Options, MatchTimeout);
    private static readonly Regex Whitespace = new(@"\s+", Options, MatchTimeout);

    public static DiagnosticSanitizedText Sanitize(string? text, int maximumScalars, DiagnosticRedactionContext? context = null) =>
        Sanitize(text, maximumScalars, context, 16 * 1024);

    /// <summary>
    /// The same sanitizer with a caller-chosen working bound (in UTF-16 units) for the cleaned text, which the public overload fixes at
    /// 16 Ki. Advisory AI passes a bound above the text's own length, so a long evidence item or answer line is cleaned whole and only
    /// its own scalar limit (which reports truncation) can shorten it.
    /// </summary>
    internal static DiagnosticSanitizedText Sanitize(string? text, int maximumScalars, DiagnosticRedactionContext? context, int maximumCleanedChars)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumScalars, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCleanedChars, 1);
        if (string.IsNullOrWhiteSpace(text)) return new(null, false);

        StringBuilder cleaned = new(Math.Min(text.Length, maximumCleanedChars));
        int boundary = -1;
        foreach (Rune rune in text.EnumerateRunes())
        {
            // Independent review of ce0eaf3, F1, and Codex review of ce0eaf3, C3: a cut inside a word or a path would leave the start
            // of a value the patterns below no longer recognise (part of an e-mail address, the computer name or a private folder name
            // after a space in a path). Past the bound, up to one longest path more is read, so the cut can be moved before any word
            // or path it would split.
            if (boundary < 0 && cleaned.Length >= maximumCleanedChars) boundary = cleaned.Length;
            if (boundary >= 0 && cleaned.Length >= boundary + MaximumPathLength) break;
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune) || category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) cleaned.Append(' ');
            else if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned) continue;
            else if (rune == Rune.ReplacementChar) cleaned.Append('?');
            else cleaned.Append(rune.ToString());
        }

        bool cutEarly = boundary >= 0 && cleaned.Length > boundary;
        string value = cleaned.ToString();
        if (cutEarly)
        {
            // Independent review of 4d2e238: the cut is checked, not only predicted. The kept text is cleaned and compared with the
            // whole text read so far, cleaned the same way, and the cut moves back until the first shows only what the second shows.
            string? whole = Redact(value, context, cutEarly: false);
            int cut = CheckedCut(value, SafeCutAtOrBefore(value, boundary), kept => Redact(value[..kept], context, cutEarly: true), whole);
            value = Redact(value[..cut], context, cutEarly: true);
        }
        else value = Redact(value, context, cutEarly: false);
        // A text whose only word ran past the working bound keeps nothing but is still marked cut (Codex review of #142).
        if (value.Length == 0) return new(null, cutEarly);
        if (!cutEarly) return Truncate(value, maximumScalars);
        // A text cut at the working bound is marked with the ellipsis too, inside the same scalar limit (Codex review of #142).
        DiagnosticSanitizedText early = Truncate(value, maximumScalars - 1);
        return early.Truncated ? early : new(early.Value + Ellipsis, true);
    }

    /// <summary>The cleaned text with paths, links, SIDs, addresses and the PC's names replaced and whitespace joined.</summary>
    private static string Redact(string value, DiagnosticRedactionContext? context, bool cutEarly)
    {
        // Structured values go first, so an identity token can never break a path, link, SID or address apart before it is
        // recognised (a one-letter account name "c" would otherwise turn "C:\Users\..." into text no path pattern matches).
        // A quoted path goes before links (Codex review of #146): a quoted command line may hold a link, and the link's pattern
        // would otherwise take the quote that closes it. A quoted path needs a drive, share or variable just after its quote, or a
        // single-quoted file link (Codex review of bf637b5, 71C-03), so a double-quoted file link (removed to its closing quote by
        // ReplaceLinks) and any other quoted link are left for the link patterns.
        value = RemoveDoubleQuotedPaths(value);
        value = RemoveSingleQuotedPaths(value);
        value = RemoveDelimitedValues(value);
        value = ReplaceLinks(value, FileLink);
        value = ReplaceLinks(value, Link);
        value = ReplacePaths(value, UncPath);
        value = ReplacePaths(value, DrivePath);
        value = ReplacePaths(value, VariablePath);
        value = Sid.Replace(value, AccountPlaceholder);
        value = Email.Replace(value, EmailPlaceholder);
        // Codex review of cd04620, 71C-04, and the independent review's P3: a cut between the words of a name of several words keeps
        // its first words, which no longer match the whole name, so they are dropped too when they end the text.
        if (cutEarly) value = DropPartialName(value, context);
        // Independent review of 4d2e238, Q8: runs of whitespace are joined before names are replaced, so a name of several words
        // written with two spaces, a tab or a line break between them is still found.
        value = Whitespace.Replace(value, " ");
        foreach (string token in (context ?? DiagnosticRedactionContext.None).IdentityTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => Whitespace.Replace(token.Trim(), " "))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(token => token.Length)
            .ThenBy(token => token, StringComparer.Ordinal))
            value = ReplaceWholeWord(value, token);
        return value.Trim();
    }

    /// <summary>
    /// Replaces each double-quoted path or file link with a placeholder inside the same quotes. When the last word inside the quotes
    /// is a link and the closing quote is followed at once by more of a word (a link holding a quote), the rest of that word goes
    /// too, so no part of the link is left after the placeholder; it stops before another path or link (Codex review of bf637b5,
    /// 71C-08), which is left to its own pattern.
    /// </summary>
    private static string RemoveDoubleQuotedPaths(string value)
    {
        Match match = DoubleQuotedPath.Match(value);
        if (!match.Success) return value;
        StringBuilder result = new(value.Length);
        int position = 0;
        int recoveries = 0;
        while (match.Success)
        {
            int end = match.Index + match.Length;
            bool opensAnother = end - 2 > match.Index && char.IsWhiteSpace(value[end - 2]) && StartsAPath(value, end);
            if (opensAnother && ++recoveries > MaximumQuoteRecoveries)
            {
                // Codex review of #147: each recovery below matches again, so text built to need one at every quote is not scanned
                // over and over. Past the limit the rest of the line goes with the path, which removes more and never less.
                end = LineEnd(value, match.Index);
                AppendPlaceholder(result.Append(value, position, match.Index - position), value, match.Index, '"');
                position = end;
                match = end < value.Length ? DoubleQuotedPath.Match(value, end) : Match.Empty;
                continue;
            }
            if (opensAnother)
            {
                // The quote that ends the match follows a space and opens another quoted path, so the match ran on past its own
                // close: it is matched again in the text before that quote.
                Match shorter = DoubleQuotedPath.Match(value, match.Index, end - 1 - match.Index);
                if (!shorter.Success || shorter.Index != match.Index)
                {
                    match = DoubleQuotedPath.Match(value, match.Index + 1);
                    continue;
                }
                match = shorter;
                end = match.Index + match.Length;
            }
            end = PastLinkTail(value, match.Index, end);
            AppendPlaceholder(result.Append(value, position, match.Index - position), value, match.Index, '"');
            position = end;
            match = end < value.Length ? DoubleQuotedPath.Match(value, end) : Match.Empty;
        }
        return result.Append(value, position, value.Length - position).ToString();
    }

    /// <summary>
    /// The owner's choice after Codex's and the independent review of bf637b5 (71C-04, U1): a single quote that opens a path or file
    /// link takes the rest of its line with it, unless the path closes and nothing after its close holds a backslash, a slash or
    /// another apostrophe. An apostrophe inside a file or folder name ('C:\Music\Guns N' Roses\x.mp3', or one before a letter
    /// outside the Basic Multilingual Plane) can make any closing quote a guess, and each narrower rule (Q6, R1, S1, T1, T2, U1,
    /// 71C-04) left part of a name in some shape, so the guess is no longer made: ordinary words after such a path on the same
    /// line are removed too, which removes more and never less. A path that closes with nothing like that after it ends at its
    /// closing quote, as before, with the rest of a link word that runs on (<see cref="PastLinkTail"/>).
    /// </summary>
    private static string RemoveSingleQuotedPaths(string value)
    {
        int opener = NextSingleQuoteOpener(value, 0);
        if (opener < 0) return value;
        StringBuilder result = new(value.Length);
        int position = 0;
        while (opener >= 0)
        {
            int end = SingleQuotedEnd(value, opener);
            AppendPlaceholder(result.Append(value, position, opener - position), value, opener, '\'');
            position = end;
            opener = NextSingleQuoteOpener(value, end);
        }
        return result.Append(value, position, value.Length - position).ToString();
    }

    /// <summary>
    /// Independent review of be08c02, V2: a path or link with a space in its last part, inside a delimiter other than a straight
    /// quote (a backtick, typographic quotes, guillemets, brackets, an HTML-escaped quote, a Markdown link target), kept the words
    /// after the space, as an unquoted one does. When a path or link starts right after such an opening delimiter and its closing
    /// delimiter is on the same line, everything up to the close is removed, so the delimiter is read as a quote is. Brackets are
    /// matched with any brackets nested inside them (C:\Program Files (x86)\…). A typographic single quote or an escaped apostrophe can
    /// also be an apostrophe in a name, so it is read as a straight single quote is (<see cref="RemoveSingleQuotedPaths"/>): it takes
    /// the rest of the line unless it closes and nothing after its close holds a backslash, a slash or another such quote. With no
    /// close on the line, any other delimiter leaves the value to the unquoted patterns, as before.
    /// </summary>
    private static string RemoveDelimitedValues(string value)
    {
        DelimitedScan scan = new(value);
        if (!scan.Next(0, out DelimitedValue found)) return value;
        StringBuilder result = new(value.Length);
        int position = 0;
        do
        {
            result.Append(value, position, found.Start - position).Append(StartsALink(value.AsSpan(found.Start)) ? LinkPlaceholder : PathPlaceholder);
            position = found.End;
        }
        while (scan.Next(found.Resume, out found));
        return result.Append(value, position, value.Length - position).ToString();
    }

    /// <summary>How a delimiter finds its close: the first close; the close that matches it, counting nested brackets; or as a single quote.</summary>
    private enum DelimiterPairing
    {
        First,
        Nested,
        Apostrophe
    }

    private sealed record Delimiter(string Open, string[] Close, DelimiterPairing Pairing);

    private static readonly Delimiter[] Delimiters =
    [
        new("`", ["`"], DelimiterPairing.First),
        new("\u201C", ["\u201D", "\u201C"], DelimiterPairing.First),
        new("\u201E", ["\u201C", "\u201D"], DelimiterPairing.First),
        new("\u201D", ["\u201D"], DelimiterPairing.First),
        new("\u00AB", ["\u00BB"], DelimiterPairing.First),
        new("\u00BB", ["\u00AB"], DelimiterPairing.First),
        new("\u2039", ["\u203A"], DelimiterPairing.First),
        new("\u203A", ["\u2039"], DelimiterPairing.First),
        new("&quot;", ["&quot;"], DelimiterPairing.First),
        new("&#34;", ["&#34;"], DelimiterPairing.First),
        new("\u2018", ["\u2019", "\u2018"], DelimiterPairing.Apostrophe),
        new("\u201A", ["\u2018", "\u2019"], DelimiterPairing.Apostrophe),
        new("&apos;", ["&apos;"], DelimiterPairing.Apostrophe),
        new("&#39;", ["&#39;"], DelimiterPairing.Apostrophe),
        new("(", [")"], DelimiterPairing.Nested),
        new("[", ["]"], DelimiterPairing.Nested),
        new("{", ["}"], DelimiterPairing.Nested),
        new("<", [">"], DelimiterPairing.Nested),
        new("|", ["|"], DelimiterPairing.First)
    ];

    private static readonly SearchValues<char> DelimiterStarts = SearchValues.Create("`\u201C\u201E\u201D\u00AB\u00BB\u2039\u203A&\u2018\u201A([{<|");
    private static readonly SearchValues<char> Brackets = SearchValues.Create("()[]{}<>");

    /// <summary>A delimited path or link: its value starts at <see cref="Start"/>, its removal ends at <see cref="End"/>, and the scan resumes at <see cref="Resume"/>.</summary>
    private readonly record struct DelimitedValue(int Opener, int Start, int End, int Resume);

    /// <summary>
    /// Finds delimited paths and links in one text, left to right. Each line's nested brackets are matched once, and a delimiter with
    /// no close after one opener on a line is not looked for again on that line, so a whole scan stays linear in the text.
    /// </summary>
    private sealed class DelimitedScan(string value)
    {
        private readonly int[] noCloseBefore = Enumerable.Repeat(-1, Delimiters.Length).ToArray();
        private int lineStart = -1;
        private int lineEnd = -1;
        private int[]? bracketMatch;
        // For each close of a first-close delimiter, where its next occurrence on the line was found from (From) and where it is
        // (At, or -1 for none to the line's end). Values only move forward, so a found close is reused until it is passed and an
        // absent one is never searched for again on the line (Codex review of #152: repeated „C:\a“ values each searched the whole
        // rest of the line for the absent ”).
        private readonly Dictionary<string, (int From, int At)> nextClose = new(StringComparer.Ordinal);

        public bool Next(int from, out DelimitedValue found)
        {
            for (int index = NextStart(from); index >= 0; index = NextStart(index + 1))
            {
                for (int kind = 0; kind < Delimiters.Length; kind++)
                {
                    Delimiter delimiter = Delimiters[kind];
                    if (!value.AsSpan(index).StartsWith(delimiter.Open, StringComparison.Ordinal)) continue;
                    int start = index + delimiter.Open.Length;
                    // One space may stand between the delimiter and the value, as French guillemets are written (« C:\… »).
                    if (start < value.Length && value[start] is ' ' or '\u00A0' or '\u202F') start++;
                    if (!(StartsAPath(value, start) || StartsALink(value.AsSpan(start)))) break;
                    if (index >= lineEnd)
                    {
                        lineStart = value.AsSpan(0, index).LastIndexOfAny('\r', '\n') + 1;
                        lineEnd = LineEnd(value, index);
                        bracketMatch = null;
                        nextClose.Clear();
                    }
                    if (noCloseBefore[kind] > index) break;
                    (int end, int closeLength) = Close(delimiter, index, start);
                    if (end < 0)
                    {
                        // Only a first-close delimiter with no close at all: a later opener of nested brackets may still be matched.
                        if (delimiter.Pairing == DelimiterPairing.First) noCloseBefore[kind] = lineEnd;
                        break;
                    }
                    // A table bar that closes one cell opens the next (independent look at #152, P2: | C:\a b | C:\c d |).
                    found = new(index, start, end, delimiter.Open == "|" ? end : end + closeLength);
                    return true;
                }
            }
            found = default;
            return false;
        }

        private int NextStart(int from)
        {
            if (from >= value.Length) return -1;
            int next = value.AsSpan(from).IndexOfAny(DelimiterStarts);
            return next < 0 ? -1 : from + next;
        }

        /// <summary>
        /// Where the removal ends (the close, which is kept, or the line's end) and how long the close is; -1 when there is no close on
        /// the line.
        /// </summary>
        private (int End, int CloseLength) Close(Delimiter delimiter, int opener, int start)
        {
            ReadOnlySpan<char> line = value.AsSpan(start, lineEnd - start);
            switch (delimiter.Pairing)
            {
                case DelimiterPairing.Nested:
                    bracketMatch ??= MatchBrackets();
                    // The opener's own index: a space may stand between it and the value (Codex review of #152).
                    int match = bracketMatch[opener - lineStart];
                    return match < 0 ? (-1, 0) : (lineStart + match, 1);
                case DelimiterPairing.First:
                {
                    int best = -1, length = 0;
                    foreach (string close in delimiter.Close)
                    {
                        int at = NextClose(close, start);
                        if (at >= 0 && (best < 0 || at < best)) { best = at; length = close.Length; }
                    }
                    // Independent review of d16ab4c, Y1: a close right after a slash ends the value too (“https://host/Team Violet
                    // Site/”, |https://host/My Folder/|12/11/2026|), as a folder link is written. A close inside a path that goes on
                    // past it (|file:///C:/|Users/…) is broken text.
                    return best < 0 ? (-1, 0) : (start + best, length);
                }
                default:
                {
                    // Read as a single quote: the first close not glued to a letter or number after it, and only when nothing after it
                    // on the line holds a backslash, a slash or another such quote; otherwise the rest of the line.
                    for (int offset = 0; offset < line.Length; offset++)
                    {
                        foreach (string close in delimiter.Close)
                        {
                            if (!line[offset..].StartsWith(close, StringComparison.Ordinal)) continue;
                            int after = offset + close.Length;
                            if (after < line.Length && IsLetterOrNumber(line[after])) continue;
                            ReadOnlySpan<char> rest = line[after..];
                            bool clean = rest.IndexOfAny('\\', '/') < 0;
                            foreach (string other in delimiter.Close) clean &= rest.IndexOf(other, StringComparison.Ordinal) < 0;
                            clean &= rest.IndexOf(delimiter.Open, StringComparison.Ordinal) < 0;
                            return clean ? (start + offset, close.Length) : (lineEnd, 0);
                        }
                    }
                    return (lineEnd, 0);
                }
            }
        }

        /// <summary>The line offset from <paramref name="start"/> of the first <paramref name="close"/> on the current line, or -1.</summary>
        private int NextClose(string close, int start)
        {
            if (nextClose.TryGetValue(close, out (int From, int At) cached) && cached.From <= start && (cached.At < 0 || cached.At >= start))
                return cached.At < 0 ? -1 : cached.At - start;
            int at = value.AsSpan(start, lineEnd - start).IndexOf(close, StringComparison.Ordinal);
            nextClose[close] = (start, at < 0 ? -1 : start + at);
            return at;
        }

        /// <summary>For each opening bracket on the current line, the line offset of the bracket that closes it, or -1.</summary>
        private int[] MatchBrackets()
        {
            int[] matches = new int[lineEnd - lineStart];
            Array.Fill(matches, -1);
            Stack<int>[] open = [new(), new(), new(), new()];
            ReadOnlySpan<char> line = value.AsSpan(lineStart, lineEnd - lineStart);
            for (int offset = line.IndexOfAny(Brackets); offset >= 0;)
            {
                int kind = "([{<)]}>".IndexOf(line[offset], StringComparison.Ordinal);
                if (kind < 4) open[kind].Push(offset);
                else if (open[kind - 4].Count > 0) matches[open[kind - 4].Pop()] = offset;
                int next = offset + 1 < line.Length ? line[(offset + 1)..].IndexOfAny(Brackets) : -1;
                offset = next < 0 ? -1 : offset + 1 + next;
            }
            return matches;
        }
    }

    /// <summary>
    /// Where the removal of the single-quoted path or file link whose opening quote is at <paramref name="opener"/> ends: the end of
    /// its line when it doesn't close on it or a backslash, slash or apostrophe follows its close there; otherwise its close, or the
    /// end of a link word that runs on past it.
    /// </summary>
    private static int SingleQuotedEnd(string value, int opener)
    {
        int lineEnd = LineEnd(value, opener);
        Match match = SingleQuotedPath.Match(value, opener, lineEnd - opener);
        if (!match.Success || match.Index != opener) return lineEnd;
        int end = match.Index + match.Length;
        // A close glued to a letter or number after it is one the pattern settled for only because no later quote closes the path
        // ('file://server/share/Zz'abc.txt), so the path never closed and the rest of the line goes with it.
        if (end < lineEnd && IsLetterOrNumber(value[end])) return lineEnd;
        return value.AsSpan(end, lineEnd - end).IndexOfAny('\\', '/', '\'') >= 0 ? lineEnd : PastLinkTail(value, opener, end);
    }

    /// <summary>The first single quote at or after <paramref name="from"/> that a path or file link follows, or -1.</summary>
    private static int NextSingleQuoteOpener(string value, int from)
    {
        for (int index = value.IndexOf('\'', from); index >= 0; index = index + 1 < value.Length ? value.IndexOf('\'', index + 1) : -1)
            if (StartsAQuotedValue(value, index + 1)) return index;
        return -1;
    }

    /// <summary>
    /// Whether a drive, network or variable path, or a link, starts at <paramref name="index"/>. Independent review of be08c02, V1: a
    /// quoted web link may hold spaces as a quoted path may, so it is read to its closing quote too.
    /// </summary>
    private static bool StartsAQuotedValue(string value, int index) =>
        index < value.Length && (StartsAPath(value, index) || StartsALink(value.AsSpan(index)));

    private static bool StartsAFileLink(ReadOnlySpan<char> rest) =>
        rest.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && StartsALink(rest);

    /// <summary>
    /// Whether <paramref name="mark"/> can join two values in a list or command line (a comma, semicolon, bar, plus, quote, bracket
    /// and the like), so a path or link right after it inside a link's last part is a value of its own. A letter or number, or a
    /// mark that occurs inside a path or link itself (a slash, backslash, colon, question mark, star, hash or
    /// percent sign), never does: there it is more likely part of the same damaged link, which its own pattern removes further
    /// than a path pattern would.
    /// </summary>
    private static bool JoinsTwoValues(char mark) => !IsLetterOrNumber(mark) && mark is not ('/' or '\\' or ':' or '?' or '*' or '#' or '%');

    /// <summary>Whether <paramref name="text"/> is only ASCII letters, the head of a scheme glued to a link that follows.</summary>
    private static bool IsSchemeText(ReadOnlySpan<char> text)
    {
        foreach (char character in text)
            if (!char.IsAsciiLetter(character)) return false;
        return true;
    }

    /// <summary>
    /// Whether a path, a file link or a link whose scheme is letters only ("https://") starts at <paramref name="index"/> inside a
    /// link's last part. A scheme may hold "+", "." and "-", so "x.txt+file://" reads as one; only plain schemes are taken there, so
    /// the end of a file name is never mistaken for the start of another link.
    /// </summary>
    private static bool StartsAJoinedPathOrLink(string value, int index)
    {
        if (StartsAPath(value, index) || StartsAFileLink(value.AsSpan(index))) return true;
        int scheme = index;
        while (scheme < value.Length && scheme - index < 32 && char.IsAsciiLetter(value[scheme])) scheme++;
        return scheme > index && value.AsSpan(scheme).StartsWith("://", StringComparison.Ordinal);
    }

    /// <summary>Appends the quoted placeholder for the quoted value whose opening quote is at <paramref name="opener"/>.</summary>
    private static void AppendPlaceholder(StringBuilder result, string value, int opener, char quote) =>
        result.Append(quote).Append(StartsALink(value.AsSpan(opener + 1)) ? LinkPlaceholder : PathPlaceholder).Append(quote);

    /// <summary>Where the line holding <paramref name="index"/> ends: its first line break at or after it, or the text's end.</summary>
    private static int LineEnd(string value, int index)
    {
        int end = value.IndexOfAny(['\r', '\n'], index);
        return end < 0 ? value.Length : end;
    }

    /// <summary>The most times one text's double-quoted paths are matched again before the rest of a line is removed with its path.</summary>
    private const int MaximumQuoteRecoveries = 16;

    /// <summary>
    /// Whether the double quote at <paramref name="quote"/> can open a quoted value: it starts the text or follows anything but a
    /// letter or number (independent review of d16ab4c: a quote after +, &lt;, ! or another value's closing quote opens a link too). An
    /// escaped (\") or doubled ("") quote is judged by what comes before it (independent look at #152, P3: {\"url\":\"https://…\"}).
    /// A quote glued to the end of a word (-url"…, T1's exception) closes something instead.
    /// </summary>
    private static bool OpensAQuote(string value, int quote)
    {
        if (quote > 0 && value[quote - 1] is '\\' or '"') quote--;
        return quote == 0 || !IsLetterOrNumber(value[quote - 1]);
    }

    /// <summary>Whether the word holding <paramref name="index"/> starts with a double quote, looking back at most 256 characters.</summary>
    private static bool QuotedWordStart(string value, int index)
    {
        for (int back = index - 1; back >= 0 && index - back <= 256; back--)
        {
            if (value[back] == '"') return OpensAQuote(value, back);
            if (char.IsWhiteSpace(value[back])) return false;
        }
        return false;
    }

    /// <summary>The most values cut from one run of joined links before the rest of the run is removed as one link.</summary>
    private const int MaximumJoinedValues = 16;

    /// <summary>
    /// Codex review of bf637b5, 71C-03: replaces each file link (or, with <paramref name="pattern"/> set to <see cref="Link"/>, each
    /// other link) with the link placeholder. A link's last part runs to whitespace, so it could take the head of a second path or
    /// link joined to it with no space (file:///C:/a.txt,file:///C:/Private Folder/b.txt) and leave that one's later words; the
    /// match ends where another path or link starts after its last part's start, and that one is matched on its own.
    /// </summary>
    private static string ReplaceLinks(string value, Regex pattern)
    {
        Match match = pattern.Match(value);
        if (!match.Success) return value;
        StringBuilder result = new(value.Length);
        int position = 0;
        bool endedAtAnother = false;
        // Codex review of #151: each value cut from a link is matched again on its own, and that match runs to the end of the
        // joined run, so a long run of joined links (https://a/x,https://a/x,…) cost the square of its length. After
        // MaximumJoinedValues cuts in one run, the rest of the run is removed as one link, as it was before the cuts.
        int joined = 0;
        int runEnd = 0;
        while (match.Success)
        {
            if (match.Index >= runEnd) joined = 0;
            runEnd = Math.Max(runEnd, match.Index + match.Length);
            // A link cut short where a scheme glued to it began (…#tagfile:///C:/…) leaves that scheme's letters before the next
            // match, which starts at "file:"; they belong to the link that follows and go with it.
            if (endedAtAnother && match.Index > position && IsSchemeText(value.AsSpan(position, match.Index - position))) position = match.Index;
            endedAtAnother = false;
            Group last = match.Groups["last"];
            int end = match.Index + match.Length;
            // Codex review of bf637b5, 71C-03: a file link right after a double quote runs to the closing quote on its line, so a
            // last part holding spaces ("file:///C:/Shared/Quarterly Notes.txt") goes whole. (A single-quoted one is taken with
            // the single-quoted paths.)
            // Independent review of be08c02, V1: so does any other link ("https://host/My Folder/Notes.txt"), and a link inside a
            // quoted word that starts with the quote ("ms-word:ofe|u|https://host/My Folder/Notes.docx").
            // A file link takes any quote right before it, as since #151; another link only a quote that can open a value.
            if (match.Index > 0 && (value[match.Index - 1] == '"' && (last.Success || OpensAQuote(value, match.Index - 1)) || QuotedWordStart(value, match.Index)))
            {
                // The close and the line's end are looked for together, so a long line of quoted links is read once (Codex review
                // of #152).
                int closing = value.AsSpan(match.Index).IndexOfAny('"', '\r', '\n');
                if (closing >= 0 && value[match.Index + closing] != '"') closing = -1;
                // For any other link the quote only ever extends the match: a close inside the match's own word
                // ("https://ex"ample.org/…) leaves the word to the join rule below, as before. A file link ends at its close, as
                // since #151, and the next value is matched on its own; independent review of be08c02, X1: after
                // MaximumJoinedValues such cuts in one run the rest of the run goes with the link, so glued quotes can't make the
                // scan match the same run again and again.
                bool cutsTheMatch = closing >= 0 && match.Index + closing < end;
                if (closing >= 0 && (!cutsTheMatch || (last.Success && joined < MaximumJoinedValues)))
                {
                    if (cutsTheMatch) joined++;
                    end = match.Index + closing;
                    result.Append(value, position, match.Index - position).Append(LinkPlaceholder);
                    position = end;
                    match = end < value.Length ? pattern.Match(value, end) : Match.Empty;
                    continue;
                }
            }
            int tail = last.Success ? last.Index : match.Index + match.Value.IndexOf("://", StringComparison.Ordinal) + 3;
            for (int index = Math.Max(tail, match.Index + 1); joined < MaximumJoinedValues && index < end; index++)
            {
                if (JoinsTwoValues(value[index - 1]) && StartsAJoinedPathOrLink(value, index)) { end = index; endedAtAnother = true; joined++; break; }
            }
            result.Append(value, position, match.Index - position).Append(LinkPlaceholder);
            position = end;
            match = end < value.Length ? pattern.Match(value, end) : Match.Empty;
        }
        return result.Append(value, position, value.Length - position).ToString();
    }

    /// <summary>
    /// Replaces each unquoted path matched by <paramref name="pattern"/> with the placeholder. Independent review of 1e57444, R6: a
    /// path's last part ends at a colon, so when a second path follows with no space between (C:\Temp\a.log;C:\Users\...), the
    /// second path's drive letter is given back to it, and the second path is removed whole instead of leaving ":\Users\..." behind.
    /// </summary>
    private static string ReplacePaths(string value, Regex pattern)
    {
        Match match = pattern.Match(value);
        if (!match.Success) return value;
        StringBuilder result = new(value.Length);
        int position = 0;
        while (match.Success)
        {
            int end = match.Index + match.Length;
            if (end + 1 < value.Length && value[end] == ':' && value[end + 1] is '\\' or '/' && end - 1 > match.Index + 3
                && char.IsAsciiLetter(value[end - 1]) && !char.IsAsciiLetter(value[end - 2]))
            {
                end--;
            }
            result.Append(value, position, match.Index - position).Append(PathPlaceholder);
            position = end;
            match = end < value.Length ? pattern.Match(value, end) : Match.Empty;
        }
        return result.Append(value, position, value.Length - position).ToString();
    }

    /// <summary>Whether <paramref name="rest"/> starts with a link's scheme and "://", or with "file:" and a slash or drive.</summary>
    private static bool StartsALink(ReadOnlySpan<char> rest)
    {
        if (rest.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return rest.Length > 5 && (rest[5] is '\\' or '/' || StartsAPath(rest[5..]));
        if (rest.IsEmpty || !char.IsAsciiLetter(rest[0])) return false;
        for (int index = 1; index < rest.Length && index <= 64; index++)
        {
            char next = rest[index];
            if (next == ':') return rest[index..].StartsWith("://", StringComparison.Ordinal);
            if (!(char.IsAsciiLetterOrDigit(next) || next is '+' or '.' or '-')) return false;
        }
        return false;
    }

    /// <summary>
    /// A letter or number, counting either half of a surrogate pair as one (Codex review of bf637b5, 71C-04: a letter outside the
    /// Basic Multilingual Plane, such as U+10400, is two UTF-16 units), so such text is read as a word and never as a boundary.
    /// </summary>
    private static bool IsLetterOrNumber(char value) => char.IsLetter(value) || char.IsNumber(value) || char.IsSurrogate(value);

    /// <summary>
    /// The end of a quoted path, moved past the rest of a word when the quoted text's last word is a link that runs on, but never
    /// into another path or link.
    /// </summary>
    private static int PastLinkTail(string value, int start, int end)
    {
        if (end >= value.Length || char.IsWhiteSpace(value[end])) return end;
        int lastSpace = start;
        for (int index = end - 2; index > start; index--)
        {
            if (char.IsWhiteSpace(value[index])) { lastSpace = index; break; }
        }
        if (value.AsSpan(lastSpace, end - lastSpace).IndexOf("://", StringComparison.Ordinal) < 0) return end;
        int wordEnd = end;
        bool holdsLetterOrNumber = false;
        while (wordEnd < value.Length && !char.IsWhiteSpace(value[wordEnd]))
        {
            // Codex review of bf637b5, 71C-08: the word ends before another path or link joined to it ("…/x",file:///C:/…), which is
            // left whole for its own pattern.
            if (!IsLetterOrNumber(value[wordEnd - 1]) && (StartsAPath(value, wordEnd) || StartsALink(value.AsSpan(wordEnd)))) break;
            holdsLetterOrNumber |= char.IsLetter(value[wordEnd]) || char.IsNumber(value[wordEnd]);
            wordEnd++;
        }
        return holdsLetterOrNumber ? wordEnd : end;
    }

    /// <summary>The most times <see cref="CheckedCut"/> moves a cut back before it keeps nothing.</summary>
    private const int MaximumCutChecks = 24;

    /// <summary>
    /// Independent review of 1e57444, R3: the longest <see cref="CheckedCut"/> keeps trying before it keeps nothing, so a text built
    /// so that every try fails can't hold a cut for more than about this long plus one try.
    /// </summary>
    private static readonly TimeSpan MaximumCutCheckTime = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Independent review of 4d2e238 (the fix that ends the class): <paramref name="cut"/> in <paramref name="view"/>, or an earlier word
    /// boundary, such that the kept text, cleaned by <paramref name="cleanPrefix"/>, shows only what <paramref name="whole"/> (the whole
    /// text read past the cut, cleaned the same way) shows at its start. A cut that splits a path, a quoted value, a secret or a name
    /// changes how the kept text is cleaned, so its cleaned form is no longer the start of the whole one; the cut then moves back one
    /// word, then two, four and so on, and keeps nothing after <see cref="MaximumCutChecks"/> tries or <see cref="MaximumCutCheckTime"/>.
    /// This needs no knowledge of what cleaning removes, so a shape the predicted cut misses is still caught.
    /// </summary>
    internal static int CheckedCut(string view, int cut, Func<int, string?> cleanPrefix, string? whole)
    {
        // Independent review of 1e57444, R3: text built to fail every try could hold the check for seconds, so after a second of
        // failed tries nothing is kept, as after the last try.
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        int words = 1;
        for (int attempt = 0; ; attempt++)
        {
            if (cut == 0) return 0;
            bool shows = ShowsOnlyWhatTheWholeShows(cleanPrefix(cut), whole);
            // Codex review of bf637b5, 71C-06: the time limit holds for a try that matches too, so nothing is kept past it.
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > MaximumCutCheckTime) return 0;
            if (shows) return cut;
            if (attempt == MaximumCutChecks) return 0;
            for (int step = 0; step < words && cut > 0; step++) cut = StartOfLastWord(view, cut);
            cut = SafeCutAtOrBefore(view, cut);
            words *= 2;
        }
    }

    /// <summary>True when <paramref name="part"/> is empty, or is the start of <paramref name="whole"/> ending where it ends or at whitespace.</summary>
    internal static bool ShowsOnlyWhatTheWholeShows(string? part, string? whole)
    {
        string kept = (part ?? string.Empty).TrimEnd();
        if (kept.Length == 0) return true;
        string all = whole ?? string.Empty;
        return all.StartsWith(kept, StringComparison.Ordinal) && (all.Length == kept.Length || char.IsWhiteSpace(all[kept.Length]));
    }

    /// <summary>Where the last word before <paramref name="cut"/> starts, so a cut there drops it.</summary>
    private static int StartOfLastWord(string text, int cut)
    {
        while (cut > 0 && char.IsWhiteSpace(text[cut - 1])) cut--;
        while (cut > 0 && !char.IsWhiteSpace(text[cut - 1])) cut--;
        return cut;
    }

    /// <summary>
    /// The text without the first words of any name of several words in <paramref name="context"/> that it ends with, when a cut
    /// fell between them. Names are matched as the cleaned text shows them: whitespace of any kind separates their words.
    /// </summary>
    internal static string DropPartialName(string text, DiagnosticRedactionContext? context)
    {
        if (context is null) return text;
        string trimmed = text.TrimEnd();
        foreach (string token in context.IdentityTokens)
        {
            if (string.IsNullOrWhiteSpace(token)) continue;
            string[] words = token.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (int count = words.Length - 1; count >= 1; count--)
            {
                int start = StartOfWordsAtEnd(trimmed, words, count);
                if (start >= 0) return trimmed[..start].TrimEnd();
            }
        }
        return text;
    }

    /// <summary>
    /// Where the first <paramref name="count"/> of <paramref name="words"/> start when <paramref name="text"/> ends with them, at a
    /// boundary that isn't a letter or digit, or -1. Independent review of 4056e3f, L2: any run of whitespace between them counts (a tab,
    /// two spaces, a no-break space or a line break), as cleaning makes each one a single space.
    /// </summary>
    internal static int StartOfWordsAtEnd(string text, string[] words, int count)
    {
        int end = text.Length;
        for (int word = count - 1; word >= 0; word--)
        {
            if (word < count - 1)
            {
                int gapEnd = end;
                while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
                if (end == gapEnd) return -1;
            }
            string expected = words[word];
            if (end < expected.Length || !text.AsSpan(end - expected.Length, expected.Length).Equals(expected, StringComparison.OrdinalIgnoreCase)) return -1;
            end -= expected.Length;
        }
        return end == 0 || !char.IsLetterOrDigit(text[end - 1]) ? end : -1;
    }

    /// <summary>The longest Windows path, in UTF-16 units: no path that a cut could split starts or ends further than this from it.</summary>
    public const int MaximumPathLength = 32 * 1024;

    /// <summary>
    /// Codex review of ce0eaf3, C3: the end of a cut of <paramref name="text"/> at <paramref name="end"/> that splits no word and no
    /// path. A path may hold spaces (a quoted file name, or a folder name in an unquoted path), so cutting at one of them would leave
    /// the path's first words as a shorter path and its later words as plain text. The cut moves before a quoted path that is still
    /// open at it, and before any drive, network or variable path that runs across it, then back to a word boundary.
    /// </summary>
    public static int SafeCutAtOrBefore(string text, int end)
    {
        int cut = WordBoundaryAtOrBefore(text, end);
        for (int pass = 0; pass < 64 && cut > 0; pass++)
        {
            int moved = Math.Min(OpenQuotedPathStart(text, cut), PathAcross(text, cut));
            if (moved >= cut) break;
            cut = WordBoundaryAtOrBefore(text, moved);
        }
        return cut;
    }

    /// <summary>Where a quoted path still open at <paramref name="cut"/> starts (its opening quote), or <paramref name="cut"/>.</summary>
    private static int OpenQuotedPathStart(string text, int cut)
    {
        // A quoted path holds no line break. Independent review of 4d2e238, Q1 and Q3: each kind of quote is followed on its own from
        // the start of the line, as cleaning removes double-quoted paths before single-quoted ones, so a double-quoted path inside a
        // single-quoted command line is still seen; the earliest opener of either kind wins. A single quote that opens a path or
        // file link is removed as RemoveSingleQuotedPaths removes it, read on the whole line past the cut: when that runs past the
        // cut (the rest of the line goes, or the path is still open there), the cut moves before its opening quote.
        int lineStart = text.AsSpan(0, cut).LastIndexOfAny(['\r', '\n']) + 1;
        // Independent review of be08c02, V2: so is a path or link inside another delimiter whose removal runs past the cut.
        int delimited = cut;
        DelimitedScan scan = new(text);
        for (int from = lineStart; scan.Next(from, out DelimitedValue found) && found.Opener < cut; from = found.Resume)
        {
            if (found.End > cut) { delimited = found.Opener; break; }
        }
        int single = cut;
        for (int opener = NextSingleQuoteOpener(text, lineStart); opener >= 0 && opener < cut;)
        {
            int end = SingleQuotedEnd(text, opener);
            if (end > cut) { single = opener; break; }
            opener = NextSingleQuoteOpener(text, end);
        }
        return Math.Min(Math.Min(OpenDoubleQuotedPathStart(text, lineStart, cut), single), delimited);
    }

    private static int OpenDoubleQuotedPathStart(string text, int lineStart, int cut)
    {
        int open = -1;
        int last = -1;
        for (int index = lineStart; index < cut; index++)
        {
            if (text[index] != '"') continue;
            bool opener = StartsAQuotedValue(text, index + 1);
            if (opener) last = index;
            if (open < 0)
            {
                if (opener) open = index;
                continue;
            }
            open = -1;
        }
        // The fifth build's rule too: the last quote before the cut that is followed by a path start.
        if (last >= 0 && text.AsSpan(last + 1, cut - last - 1).IndexOf('"') < 0) open = open < 0 ? last : Math.Min(open, last);
        return open >= 0 ? open : cut;
    }

    private static bool StartsAPath(string text, int index) => StartsAPath(text.AsSpan(index));

    private static bool StartsAPath(ReadOnlySpan<char> rest)
    {
        if (rest.Length >= 3 && char.IsAsciiLetter(rest[0]) && rest[1] == ':' && rest[2] is '\\' or '/') return true;
        if (rest.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        if (rest.Length < 3 || rest[0] != '%' || !(char.IsAsciiLetter(rest[1]) || rest[1] == '_')) return false;
        for (int i = 2; i < rest.Length && i <= 256; i++)
        {
            if (rest[i] == '%') return true;
            if (!(char.IsAsciiLetterOrDigit(rest[i]) || rest[i] == '_')) return false;
        }
        return false;
    }

    /// <summary>Where the first drive, network or variable path running across <paramref name="cut"/> starts, or <paramref name="cut"/>.</summary>
    private static int PathAcross(string text, int cut)
    {
        int windowStart = Math.Max(0, cut - MaximumPathLength);
        ReadOnlySpan<char> window = text.AsSpan(windowStart, Math.Min(text.Length, cut + MaximumPathLength) - windowStart);
        int start = cut;
        foreach (Regex path in (ReadOnlySpan<Regex>)[FileLink, UncPath, DrivePath, VariablePath])
        {
            foreach (ValueMatch match in path.EnumerateMatches(window))
            {
                int matchStart = windowStart + match.Index;
                if (matchStart >= cut) break;
                if (matchStart + match.Length > cut) start = Math.Min(start, matchStart);
            }
        }
        return start;
    }

    /// <summary>
    /// Independent review of ce0eaf3, F1: the end of a cut of <paramref name="text"/> at <paramref name="end"/> that never falls inside
    /// a word. It is <paramref name="end"/> when the text ends there or a whitespace character sits on either side of it; otherwise the
    /// start of the word it would split (just after the last whitespace before it), or 0 when there is none, so nothing is kept.
    /// Redaction runs after the cut, so a value it recognises by its shape is never left as a prefix it no longer matches.
    /// </summary>
    public static int WordBoundaryAtOrBefore(string text, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(end);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, text.Length);
        if (end == text.Length || end == 0 || char.IsWhiteSpace(text[end]) || char.IsWhiteSpace(text[end - 1])) return end;
        for (int index = end - 1; index >= 0; index--)
            if (char.IsWhiteSpace(text[index])) return index + 1;
        return 0;
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
