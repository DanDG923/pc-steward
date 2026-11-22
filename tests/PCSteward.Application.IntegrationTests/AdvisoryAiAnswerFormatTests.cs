using PCSteward.Application;

namespace PCSteward.Application.IntegrationTests;

/// <summary>First live test (Task 71): an answer's Markdown marks are shown as formatting, never as links or anything clickable.</summary>
[TestClass]
public sealed class AdvisoryAiAnswerFormatTests
{
    private const string LiveShape =
        "### What the evidence shows\n" +
        "- At **23:11:56 UTC on November 14**, the Application log recorded an **application crash** (event 1000).\n" +
        "- Four seconds later, it recorded an **error report** (event 1001).\n" +
        "\n" +
        "### Possible explanation\n" +
        "The error report **may relate to the crash four seconds earlier**. That timing fits\n" +
        "a crash followed by reporting.\n" +
        "\n" +
        "This evidence does **not** identify the application.";

    private static readonly bool[] FirstBulletBoldness = [false, true, false, true, false];

    private static readonly AdvisoryAiAnswerBlockKind[] LiveShapeKinds =
    [
        AdvisoryAiAnswerBlockKind.Heading, AdvisoryAiAnswerBlockKind.Bullet, AdvisoryAiAnswerBlockKind.Bullet,
        AdvisoryAiAnswerBlockKind.Heading, AdvisoryAiAnswerBlockKind.Paragraph, AdvisoryAiAnswerBlockKind.Paragraph
    ];

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void TheFirstLiveAnswerIsShownWithoutItsMarks()
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(LiveShape);
        CollectionAssert.AreEqual(LiveShapeKinds, blocks.Select(block => block.Kind).ToArray());
        Assert.AreEqual("What the evidence shows", blocks[0].PlainText);
        Assert.AreEqual("•", blocks[1].Marker);
        CollectionAssert.AreEqual(FirstBulletBoldness, blocks[1].Spans.Select(span => span.Bold).ToArray());
        Assert.AreEqual("23:11:56 UTC on November 14", blocks[1].Spans[1].Text);
        Assert.AreEqual("The error report may relate to the crash four seconds earlier. That timing fits a crash followed by reporting.", blocks[4].PlainText,
            "lines of one paragraph join with a space");
        string plain = AdvisoryAiAnswerFormat.ToPlainText(blocks);
        Assert.IsFalse(plain.Contains("**", StringComparison.Ordinal) || plain.Contains("###", StringComparison.Ordinal), plain);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("See [the guide](https://example.invalid/x) now.", DisplayName = "a link")]
    [DataRow("![image](https://example.invalid/i.png)", DisplayName = "an image")]
    [DataRow("Run `sfc /scannow` <b>here</b>", DisplayName = "code and HTML")]
    [DataRow("An unmatched ** mark stays", DisplayName = "an unmatched bold mark")]
    [DataRow("2 * 3 * 4 = 24", DisplayName = "stars that aren't bold")]
    public void AnythingElseIsShownExactlyAsWritten(string text)
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(text);
        Assert.HasCount(1, blocks);
        Assert.AreEqual(AdvisoryAiAnswerBlockKind.Paragraph, blocks[0].Kind);
        Assert.AreEqual(text, blocks[0].PlainText);
        Assert.IsFalse(blocks[0].Spans.Any(span => span.Bold));
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ListsKeepTheirNumbersAndNesting()
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse("1. First\n2) Second\n   - Under it\n\t\t\t\t\t\t\t\t- Deep\n---\n* Star item\n# Big\n__Bold__ start");
        Assert.AreEqual("1.", blocks[0].Marker);
        Assert.AreEqual("2.", blocks[1].Marker);
        Assert.AreEqual(1, blocks[2].Level);
        Assert.AreEqual(AdvisoryAiAnswerFormat.MaximumLevel, blocks[3].Level);
        Assert.AreEqual("Star item", blocks[4].PlainText, "the rule line is dropped and a star bullet is a bullet");
        Assert.AreEqual(AdvisoryAiAnswerBlockKind.Heading, blocks[5].Kind);
        Assert.IsTrue(blocks[6].Spans[0].Bold);
        Assert.HasCount(7, blocks);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("Pattern: `**/logs/**`", DisplayName = "stars in inline code")]
    [DataRow("Use ``__init__ and `x` __`` here", DisplayName = "underscores in a double-backtick span")]
    [DataRow("An unmatched ` backtick and **bold** stay apart", DisplayName = "an unmatched backtick")]
    public void MarksInsideInlineCodeStayAsWritten(string text)
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(text);
        Assert.HasCount(1, blocks);
        bool hasBold = text.Contains("**bold**", StringComparison.Ordinal);
        Assert.AreEqual(hasBold ? text.Replace("**bold**", "bold", StringComparison.Ordinal) : text, blocks[0].PlainText);
        Assert.AreEqual(hasBold, blocks[0].Spans.Any(span => span.Bold));
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void BoldAroundInlineCodeKeepsTheCode()
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse("Run **`sfc /scannow` first** then wait");
        Assert.AreEqual("Run `sfc /scannow` first then wait", blocks[0].PlainText);
        Assert.AreEqual("`sfc /scannow` first", blocks[0].Spans.Single(span => span.Bold).Text);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AFencedBlockIsShownLineForLineExactlyAsWritten()
    {
        const string Fenced = "```text\n---\n# not a heading\n- not a bullet\n**not bold**\n\n  indented\n````";
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse("Before **it**\n" + Fenced + "\nAfter **it**");
        Assert.HasCount(3, blocks);
        Assert.AreEqual(AdvisoryAiAnswerBlockKind.Literal, blocks[1].Kind);
        Assert.AreEqual(Fenced, blocks[1].PlainText, "fences, rule, marks, the blank line and indentation all stay");
        Assert.IsFalse(blocks[1].Spans.Any(span => span.Bold));
        Assert.IsTrue(blocks[2].Spans.Any(span => span.Bold), "formatting resumes after the closing fence");
        StringAssert.Contains(AdvisoryAiAnswerFormat.ToPlainText(blocks), Fenced);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnUnclosedFenceKeepsTheRestOfTheAnswerAsWritten()
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse("~~~\n**a**\n```\n---\n- b");
        Assert.HasCount(1, blocks);
        Assert.AreEqual(AdvisoryAiAnswerBlockKind.Literal, blocks[0].Kind);
        Assert.AreEqual("~~~\n**a**\n```\n---\n- b", blocks[0].PlainText, "a fence of the other kind doesn't close it");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnAnswerWithTooManyBlocksIsShownAsPlainTextWithANotice()
    {
        // Task 71C review 71C-03: 4,096 one-word list items would make over 12,000 runs in the dialog. Past the block budget the
        // answer is two blocks: the notice, then the whole answer as written in one run.
        string answer = string.Join("\n", Enumerable.Range(0, 4_096).Select(item => "- item " + item));
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(answer);
        Assert.HasCount(2, blocks);
        Assert.AreEqual(AdvisoryAiAnswerFormat.TooComplexNotice, blocks[0].PlainText);
        Assert.IsTrue(blocks[0].Spans.Single().Bold);
        Assert.AreEqual(answer, blocks[1].PlainText);
        Assert.HasCount(1, blocks[1].Spans);
        Assert.HasCount(AdvisoryAiAnswerFormat.MaximumBlocks, AdvisoryAiAnswerFormat.Parse(string.Join("\n", Enumerable.Repeat("- x", AdvisoryAiAnswerFormat.MaximumBlocks))),
            "an answer at the budget is still formatted");
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow(1_998, true, DisplayName = "1,998 code lines and two fence lines: 4,000 elements with the closing mark")]
    [DataRow(1_999, false, DisplayName = "1,999 code lines and two fence lines: over the budget")]
    public void ACodeBlockIsChargedForEveryLineAndLineBreakTheDialogMakes(int lines, bool formatted)
    {
        // Codex review of ce0eaf3, C5: the dialog makes one run per code line and one line break between lines, so a 2,500-line
        // block would make 5,000 elements while counted as 2,500.
        string answer = "```\n" + string.Join("\n", Enumerable.Range(0, lines).Select(line => "x" + line)) + "\n```";
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(answer);
        if (formatted) Assert.AreEqual(AdvisoryAiAnswerBlockKind.Literal, blocks.Single().Kind);
        else Assert.AreEqual(AdvisoryAiAnswerFormat.TooComplexNotice, blocks[0].PlainText);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnOverlongAnswerIsCutForDisplayAndSaysSo()
    {
        string answer = new('a', AdvisoryAiAnswerFormat.MaximumFormattedCharacters + 10);
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(answer);
        Assert.HasCount(2, blocks);
        Assert.AreEqual(AdvisoryAiAnswerFormat.TooComplexNotice + AdvisoryAiAnswerFormat.ShortenedNotice, blocks[0].PlainText);
        StringAssert.Contains(blocks[0].PlainText, "65,536");
        Assert.AreEqual(AdvisoryAiAnswerFormat.MaximumFormattedCharacters, blocks[1].PlainText.Length);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void UnmatchedBackticksAreReadInTimeProportionalToTheText()
    {
        // Task 71C review 71C-03: runs of every length from 1 to 300 backticks, none closed, once took time in the square of the text.
        string line = string.Concat(Enumerable.Range(1, 300).Select(length => new string('`', length) + "a"));
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(line + " **bold**");
        timer.Stop();
        Assert.HasCount(1, blocks);
        Assert.AreEqual(line + " bold", blocks[0].PlainText);
        Assert.IsTrue(blocks[0].Spans[^1].Bold);
        Assert.IsLessThan(2_000, timer.ElapsedMilliseconds);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void AnIndentedFenceInAListStaysCodeAfterRedaction()
    {
        // Codex review of #140: redaction now keeps each line's indentation, so a fence nested in a list item is indented by four
        // spaces. It is still read as a fence, and nothing in it is read as a list item.
        const string answer = "1. Run this:\n    ```bash\n    - keep-this\n        echo  hi\n    ```\n2. Done.";
        string redacted = PCSteward.Domain.AdvisoryAiRedaction.SanitizeAnswer(answer, 4000).Value!;
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(redacted);
        CollectionAssert.AreEqual(IndentedFenceKinds, blocks.Select(block => block.Kind).ToArray());
        Assert.AreEqual("    ```bash\n    - keep-this\n        echo hi\n    ```", blocks[1].PlainText);
        Assert.AreEqual("2.", blocks[2].Marker);
    }

    private static readonly AdvisoryAiAnswerBlockKind[] IndentedFenceKinds =
        [AdvisoryAiAnswerBlockKind.Numbered, AdvisoryAiAnswerBlockKind.Literal, AdvisoryAiAnswerBlockKind.Numbered];

    [TestMethod]
    [TestCategory("FR-AI-05")]
    [DataRow("<span title=\"**ready**\">status</span>", DisplayName = "an attribute")]
    [DataRow("<!-- **note** -->", DisplayName = "a comment")]
    [DataRow("</__b__>", DisplayName = "a closing tag")]
    [DataRow("<span title=\"A > **ready**\">status</span>", DisplayName = "a quoted > in an attribute (Codex review of ce0eaf3, C4)")]
    [DataRow("<span title='A <b> **ready**'>status</span>", DisplayName = "a single-quoted < in an attribute")]
    [DataRow("<!-- don't make **this** bold -->", DisplayName = "an apostrophe in a comment (independent review of 4056e3f, L3)")]
    [DataRow("<span title = \"A > **ready**\">status</span>", DisplayName = "a quoted value after spaces around =")]
    public void MarksInsideAnHtmlTagStayAsWritten(string html)
    {
        // Task 71C review 71C-R3: HTML is never interpreted, and its source isn't changed by bold matching either.
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(html);
        Assert.HasCount(1, blocks);
        Assert.AreEqual(html, blocks[0].PlainText);
        Assert.IsFalse(blocks[0].Spans.Any(span => span.Bold), html);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void BoldOutsideATagOrAfterAnUnclosedAngleIsStillBold()
    {
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse("Use <b>tags</b> and **this** if 2 <3 or **that**");
        CollectionAssert.AreEqual(BoldOutsideTags, blocks[0].Spans.Where(span => span.Bold).Select(span => span.Text).ToArray());
        Assert.AreEqual("Use <b>tags</b> and this if 2 <3 or that", blocks[0].PlainText);
    }

    private static readonly string[] BoldOutsideTags = ["this", "that"];

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void ManyUnclosedTagsAreReadInOnePass()
    {
        string answer = string.Concat(Enumerable.Repeat("<a **b** ", 6_000));
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        IReadOnlyList<AdvisoryAiAnswerBlock> blocks = AdvisoryAiAnswerFormat.Parse(answer);
        Assert.IsLessThan(2.0, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds);
        Assert.IsNotEmpty(blocks);
    }

    [TestMethod]
    [TestCategory("FR-AI-05")]
    public void NoAnswerIsNoBlocks()
    {
        Assert.HasCount(0, AdvisoryAiAnswerFormat.Parse(null));
        Assert.HasCount(0, AdvisoryAiAnswerFormat.Parse("\n\n  \n"));
    }
}
