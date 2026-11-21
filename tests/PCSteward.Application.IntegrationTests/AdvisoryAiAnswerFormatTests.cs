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
    public void NoAnswerIsNoBlocks()
    {
        Assert.HasCount(0, AdvisoryAiAnswerFormat.Parse(null));
        Assert.HasCount(0, AdvisoryAiAnswerFormat.Parse("\n\n  \n"));
    }
}
