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
    Numbered
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
/// shown exactly as written. It never drops answer text other than the marks it reads.
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

        foreach (string raw in answer.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
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
        return blocks;
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

    /// <summary>Bold runs marked by a matching pair of ** or __ on one line. An unmatched mark stays as literal text.</summary>
    private static List<AdvisoryAiAnswerSpan> Inline(string text)
    {
        List<AdvisoryAiAnswerSpan> spans = [];
        int position = 0;
        foreach (Match bold in BoldRun().Matches(text))
        {
            if (bold.Index > position) spans.Add(new(text[position..bold.Index], false));
            spans.Add(new(bold.Groups["text"].Value, true));
            position = bold.Index + bold.Length;
        }
        if (position < text.Length) spans.Add(new(text[position..], false));
        return spans.Count == 0 ? [new(string.Empty, false)] : spans;
    }

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
