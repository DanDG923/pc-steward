using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PCSteward.Application;

/// <summary>First live test (Task 71): how one block of an AI answer is shown.</summary>
public enum AdvisoryAiAnswerBlockKind
{
    Paragraph,
    Heading,
    Bullet,
    Numbered,

    /// <summary>A fenced code block, fences included, shown line for line exactly as written with nothing in it read as formatting.</summary>
    Literal
}

/// <summary>One run of answer text, shown in bold or not. Text is always shown as text: nothing in it is ever a link or a command.</summary>
public sealed record AdvisoryAiAnswerSpan(string Text, bool Bold);

/// <summary>
/// One block of an AI answer. <see cref="Marker"/> is PC Steward's own bullet or the item's number ("•", "2."); <see cref="Level"/> is
/// the list nesting, 0 to <see cref="AdvisoryAiAnswerFormat.MaximumLevel"/>.
/// </summary>
public sealed record AdvisoryAiAnswerBlock(AdvisoryAiAnswerBlockKind Kind, IReadOnlyList<AdvisoryAiAnswerSpan> Spans, string? Marker = null, int Level = 0)
{
    public string PlainText => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// First live test (Task 71): the model answers in Markdown, which was shown with its marks (### and **). This reads a small, fixed
/// subset into display blocks: headings, paragraphs, bullet and numbered items, and bold text. It runs only on the already-redacted
/// answer and never makes anything clickable: links, images, code and HTML stay as literal text, and anything it doesn't recognise is
/// shown exactly as written. It never drops answer text other than the marks it reads. Code is never read for marks: a fenced block
/// is one <see cref="AdvisoryAiAnswerBlockKind.Literal"/> block (its lines joined with "\n"), and stars or underscores inside an inline
/// code span stay as written.
/// </summary>
public static partial class AdvisoryAiAnswerFormat
{
    public const int MaximumLevel = 3;

    /// <summary>
    /// Task 71C review 71C-03: the longest answer that is formatted. A complete answer within the reviewed output limit is far shorter;
    /// anything longer is shown as plain text, cut to this length, with <see cref="TooComplexNotice"/>.
    /// </summary>
    public const int MaximumFormattedCharacters = 65_536;

    /// <summary>Task 71C review 71C-03: the most display blocks, and runs of text across them, one formatted answer may make.</summary>
    public const int MaximumBlocks = 1_000;

    public const int MaximumRuns = 4_000;

    /// <summary>Task 71C review 71C-03: the time formatting may take in all, on top of each pattern's own match timeout.</summary>
    public static TimeSpan FormattingBudget { get; } = TimeSpan.FromMilliseconds(250);

    public const string TooComplexNotice = "This answer is too long or too complex to format, so it's shown as plain text.";

    public static string ShortenedNotice { get; } = string.Create(CultureInfo.InvariantCulture,
        $" Only its first {MaximumFormattedCharacters:N0} characters are shown here.");

    public static IReadOnlyList<AdvisoryAiAnswerBlock> Parse(string? answer)
    {
        if (string.IsNullOrEmpty(answer)) return [];
        if (answer.Length > MaximumFormattedCharacters) return AsPlainText(answer);
        try
        {
            return Blocks(answer, Stopwatch.GetTimestamp() + (long)(FormattingBudget.TotalSeconds * Stopwatch.Frequency));
        }
        catch (RegexMatchTimeoutException)
        {
            // Formatting is only a convenience: the whole answer is shown as written rather than lost.
            return AsPlainText(answer);
        }
        catch (TimeoutException)
        {
            // Task 71C review 71C-03: over the time, block or run budget. The dialog then makes a few runs, never thousands.
            return AsPlainText(answer);
        }
    }

    /// <summary>
    /// Task 71C review 71C-03: the answer as written, in one run after a notice that says why it isn't formatted, cut to
    /// <see cref="MaximumFormattedCharacters"/> (the notice says so when it is).
    /// </summary>
    private static List<AdvisoryAiAnswerBlock> AsPlainText(string answer)
    {
        bool shortened = answer.Length > MaximumFormattedCharacters;
        string shown = shortened ? answer[..MaximumFormattedCharacters] : answer;
        if (shortened && char.IsHighSurrogate(shown[^1])) shown = shown[..^1];
        return
        [
            new(AdvisoryAiAnswerBlockKind.Paragraph, [new(TooComplexNotice + (shortened ? ShortenedNotice : string.Empty), true)]),
            new(AdvisoryAiAnswerBlockKind.Paragraph, [new(shown, false)])
        ];
    }

    private static List<AdvisoryAiAnswerBlock> Blocks(string answer, long deadline)
    {
        List<AdvisoryAiAnswerBlock> blocks = [];
        StringBuilder paragraph = new();
        int runs = 0;

        void Add(AdvisoryAiAnswerBlock block)
        {
            blocks.Add(block);
            runs += block.Spans.Count + (block.Marker is null ? 0 : 1) + (block.Kind == AdvisoryAiAnswerBlockKind.Literal ? block.PlainText.Count(character => character == '\n') : 0);
            if (blocks.Count > MaximumBlocks || runs > MaximumRuns) throw new TimeoutException("The answer has too many blocks to format.");
        }

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            Add(new(AdvisoryAiAnswerBlockKind.Paragraph, Inline(paragraph.ToString(), deadline)));
            paragraph.Clear();
        }

        List<string>? literal = null;
        char fenceCharacter = '\0';
        int fenceLength = 0;

        void FlushLiteral()
        {
            if (literal is null) return;
            Add(new(AdvisoryAiAnswerBlockKind.Literal, [new(string.Join("\n", literal), false)]));
            literal = null;
        }

        foreach (string raw in answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            CheckTime(deadline);
            if (literal is not null)
            {
                literal.Add(raw);
                if (IsClosingFence(raw, fenceCharacter, fenceLength)) FlushLiteral();
                continue;
            }
            Match fence = FenceLine().Match(raw);
            if (fence.Success)
            {
                FlushParagraph();
                fenceCharacter = fence.Groups["fence"].Value[0];
                fenceLength = fence.Groups["fence"].Length;
                literal = [raw];
                continue;
            }
            string line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                FlushParagraph();
                continue;
            }
            Match heading = HeadingLine().Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                Add(new(AdvisoryAiAnswerBlockKind.Heading, Inline(heading.Groups["text"].Value, deadline)));
                continue;
            }
            if (RuleLine().IsMatch(line))
            {
                FlushParagraph();
                continue;
            }
            Match bullet = BulletLine().Match(line);
            if (bullet.Success)
            {
                FlushParagraph();
                Add(new(AdvisoryAiAnswerBlockKind.Bullet, Inline(bullet.Groups["text"].Value, deadline), "•", LevelOf(bullet.Groups["indent"].Value)));
                continue;
            }
            Match numbered = NumberedLine().Match(line);
            if (numbered.Success)
            {
                FlushParagraph();
                string number = int.Parse(numbered.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                Add(new(AdvisoryAiAnswerBlockKind.Numbered, Inline(numbered.Groups["text"].Value, deadline), number + ".", LevelOf(numbered.Groups["indent"].Value)));
                continue;
            }
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }
        FlushParagraph();
        FlushLiteral(); // an unclosed fence runs to the end of the answer, as in CommonMark: nothing after it is read for marks
        return blocks;
    }

    private static void CheckTime(long deadline)
    {
        if (Stopwatch.GetTimestamp() > deadline) throw new TimeoutException("Formatting the answer took too long.");
    }

    private static bool IsClosingFence(string line, char character, int length)
    {
        string trimmed = line.TrimEnd();
        int start = 0;
        // Codex review of #140: answers now keep their indentation, so a fence inside a list item may be indented by any amount.
        while (start < trimmed.Length && trimmed[start] is ' ' or '\t') start++;
        int run = 0;
        while (start + run < trimmed.Length && trimmed[start + run] == character) run++;
        return run >= length && start + run == trimmed.Length;
    }

    /// <summary>The answer as plain text without Markdown marks, for copying or a screen reader.</summary>
    public static string ToPlainText(IReadOnlyList<AdvisoryAiAnswerBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return string.Join("\n", blocks.Select(block => (block.Marker is null ? string.Empty : new string(' ', 2 * block.Level) + block.Marker + " ") + block.PlainText));
    }

    private static int LevelOf(string indent)
    {
        int columns = indent.Sum(character => character == '\t' ? 4 : 1);
        return Math.Min(MaximumLevel, columns / 2);
    }

    /// <summary>
    /// Bold runs marked by a matching pair of ** or __ on one line. An unmatched mark stays as literal text, and so does any mark inside
    /// an inline code span or an HTML tag: they are masked before bold is matched, so their stars and underscores can neither open,
    /// close nor be lost.
    /// </summary>
    private static List<AdvisoryAiAnswerSpan> Inline(string text, long deadline)
    {
        List<AdvisoryAiAnswerSpan> spans = [];
        int position = 0;
        foreach (Match bold in BoldRun().Matches(MaskLiteralSpans(text)))
        {
            CheckTime(deadline);
            if (spans.Count > MaximumRuns) throw new TimeoutException("The answer has too many runs to format.");
            if (bold.Index > position) spans.Add(new(text[position..bold.Index], false));
            Group inner = bold.Groups["text"];
            spans.Add(new(text.Substring(inner.Index, inner.Length), true));
            position = bold.Index + bold.Length;
        }
        if (position < text.Length) spans.Add(new(text[position..], false));
        return spans.Count == 0 ? [new(string.Empty, false)] : spans;
    }

    /// <summary>
    /// A copy of the text, the same length, with the contents of each inline code span (a run of N backticks closed by the next run of
    /// exactly N) replaced by a letter, so no mark inside code is matched. Backticks with no matching run stay as they are. Task 71C
    /// review 71C-03: each run's closing partner is found in one backward pass, so this takes time in proportion to the text.
    /// </summary>
    private static string MaskLiteralSpans(string text) => MaskHtmlTags(MaskCodeSpans(text));

    /// <summary>
    /// Task 71C review 71C-R3: a copy of the text, the same length, with the inside of each HTML tag (from a "&lt;" followed by a
    /// letter, "/", "!" or "?" to the next "&gt;", with no other "&lt;" between) replaced by a letter, so an attribute such as
    /// title="**ready**" is shown exactly as written. HTML is never interpreted: it is only kept literal. One forward pass.
    /// </summary>
    private static string MaskHtmlTags(string text)
    {
        if (!text.Contains('<', StringComparison.Ordinal)) return text;
        char[] masked = text.ToCharArray();
        for (int index = 0; index + 1 < masked.Length; index++)
        {
            if (masked[index] != '<' || !(char.IsAsciiLetter(masked[index + 1]) || masked[index + 1] is '/' or '!' or '?')) continue;
            int close = index + 1;
            while (close < masked.Length && masked[close] is not ('<' or '>')) close++;
            if (close == masked.Length) break;
            if (masked[close] == '>')
                for (int inside = index + 1; inside < close; inside++) masked[inside] = 'x';
            // The scan resumes at the closing mark, or at the next "<", so each character is looked at a bounded number of times.
            index = close - 1;
        }
        return new string(masked);
    }

    private static string MaskCodeSpans(string text)
    {
        if (!text.Contains('`', StringComparison.Ordinal)) return text;
        List<(int Start, int Length)> runs = [];
        for (int index = 0; index < text.Length;)
        {
            if (text[index] != '`') { index++; continue; }
            int length = RunLength(text, index);
            runs.Add((index, length));
            index += length;
        }
        int[] next = new int[runs.Count];
        Dictionary<int, int> following = [];
        for (int run = runs.Count - 1; run >= 0; run--)
        {
            next[run] = following.TryGetValue(runs[run].Length, out int partner) ? partner : -1;
            following[runs[run].Length] = run;
        }
        char[] masked = text.ToCharArray();
        for (int run = 0; run < runs.Count;)
        {
            if (next[run] < 0) { run++; continue; }
            (int start, int length) = runs[run];
            for (int inside = start + length; inside < runs[next[run]].Start; inside++) masked[inside] = 'x';
            run = next[run] + 1;
        }
        return new string(masked);
    }

    private static int RunLength(string text, int start)
    {
        int end = start;
        while (end < text.Length && text[end] == '`') end++;
        return end - start;
    }

    [GeneratedRegex(@"^[ \t]*(?<fence>`{3,}|~{3,})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^ {0,3}(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"^(?<indent>[ \t]*)[-*+][ \t]+(?<text>.+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<number>[0-9]{1,3})[.)][ \t]+(?<text>.+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"(?<mark>\*\*|__)(?<text>\S(?:.*?\S)?)\k<mark>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex BoldRun();
}
