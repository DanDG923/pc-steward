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

    public static IReadOnlyList<AdvisoryAiAnswerBlock> Parse(string? answer)
    {
        if (string.IsNullOrEmpty(answer)) return [];
        try
        {
            return Blocks(answer);
        }
        catch (RegexMatchTimeoutException)
        {
            // Formatting is only a convenience: the whole answer is shown as written rather than lost.
            return [new(AdvisoryAiAnswerBlockKind.Paragraph, [new(answer, false)])];
        }
    }

    private static List<AdvisoryAiAnswerBlock> Blocks(string answer)
    {
        List<AdvisoryAiAnswerBlock> blocks = [];
        StringBuilder paragraph = new();

        void FlushParagraph()
        {
            if (paragraph.Length == 0) return;
            blocks.Add(new(AdvisoryAiAnswerBlockKind.Paragraph, Inline(paragraph.ToString())));
            paragraph.Clear();
        }

        List<string>? literal = null;
        char fenceCharacter = '\0';
        int fenceLength = 0;

        void FlushLiteral()
        {
            if (literal is null) return;
            blocks.Add(new(AdvisoryAiAnswerBlockKind.Literal, [new(string.Join("\n", literal), false)]));
            literal = null;
        }

        foreach (string raw in answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
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
                blocks.Add(new(AdvisoryAiAnswerBlockKind.Heading, Inline(heading.Groups["text"].Value)));
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
                blocks.Add(new(AdvisoryAiAnswerBlockKind.Bullet, Inline(bullet.Groups["text"].Value), "•", LevelOf(bullet.Groups["indent"].Value)));
                continue;
            }
            Match numbered = NumberedLine().Match(line);
            if (numbered.Success)
            {
                FlushParagraph();
                string number = int.Parse(numbered.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                blocks.Add(new(AdvisoryAiAnswerBlockKind.Numbered, Inline(numbered.Groups["text"].Value), number + ".", LevelOf(numbered.Groups["indent"].Value)));
                continue;
            }
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line.Trim());
        }
        FlushParagraph();
        FlushLiteral(); // an unclosed fence runs to the end of the answer, as in CommonMark: nothing after it is read for marks
        return blocks;
    }

    private static bool IsClosingFence(string line, char character, int length)
    {
        string trimmed = line.TrimEnd();
        int start = 0;
        while (start < trimmed.Length && start < 3 && trimmed[start] == ' ') start++;
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
    /// an inline code span: the code is masked before bold is matched, so its stars and underscores can neither open, close nor be lost.
    /// </summary>
    private static List<AdvisoryAiAnswerSpan> Inline(string text)
    {
        List<AdvisoryAiAnswerSpan> spans = [];
        int position = 0;
        foreach (Match bold in BoldRun().Matches(MaskCodeSpans(text)))
        {
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
    /// exactly N) replaced by a letter, so no mark inside code is matched. Backticks with no matching run stay as they are.
    /// </summary>
    private static string MaskCodeSpans(string text)
    {
        if (!text.Contains('`', StringComparison.Ordinal)) return text;
        char[] masked = text.ToCharArray();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] != '`') { index++; continue; }
            int open = RunLength(text, index);
            int search = index + open;
            int close = -1;
            while (search < text.Length)
            {
                if (text[search] != '`') { search++; continue; }
                int run = RunLength(text, search);
                if (run == open) { close = search; break; }
                search += run;
            }
            if (close < 0) { index += open; continue; }
            for (int inside = index + open; inside < close; inside++) masked[inside] = 'x';
            index = close + open;
        }
        return new string(masked);
    }

    private static int RunLength(string text, int start)
    {
        int end = start;
        while (end < text.Length && text[end] == '`') end++;
        return end - start;
    }

    [GeneratedRegex(@"^ {0,3}(?<fence>`{3,}|~{3,})", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"^ {0,3}#{1,6}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HeadingLine();

    [GeneratedRegex(@"^ {0,3}(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RuleLine();

    [GeneratedRegex(@"^(?<indent>[ \t]*)[-*+][ \t]+(?<text>.+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BulletLine();

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<number>[0-9]{1,3})[.)][ \t]+(?<text>.+)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NumberedLine();

    [GeneratedRegex(@"(?<mark>\*\*|__)(?<text>\S(?:.*?\S)?)\k<mark>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex BoldRun();
}
