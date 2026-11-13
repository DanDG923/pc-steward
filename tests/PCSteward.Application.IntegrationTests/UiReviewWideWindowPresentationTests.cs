using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, item 19 (FR-UI-01/05): a wide window used properly. Pages of text and switches read as one column; list
/// pages still fill the window. Presentation only.
/// </summary>
[TestClass]
public sealed class UiReviewWideWindowPresentationTests
{
    private static readonly ShellPage[] ReadingPages = [ShellPage.Settings, ShellPage.About, ShellPage.Decisions, ShellPage.Privacy, ShellPage.Storage, ShellPage.Scan, ShellPage.Onboarding];

    [TestMethod]
    [Description("FR-UI-01/05 (item 19): text-and-switch pages are a column of about 1,000 units; list pages have no maximum width.")]
    public void TextPagesReadAsOneColumnAndListPagesFillTheWindow()
    {
        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            bool reading = ReadingPages.Contains(page);
            Assert.AreEqual(reading, PageLayout.IsReadingPage(page), page.ToString());
            Assert.AreEqual(reading ? 1068 : double.PositiveInfinity, PageLayout.MaximumWidth(page, 68), page.ToString());
        }
        Assert.AreEqual(1000d, PageLayout.MaximumWidth(ShellPage.Settings, 0), "the column itself is 1,000 units");
        Assert.AreEqual(1000d, PageLayout.MaximumWidth(ShellPage.Settings, -5), "padding never narrows the column");
        // The pages the owner asked to fill a maximised window still do.
        foreach (ShellPage list in new[] { ShellPage.Home, ShellPage.Updates, ShellPage.InstalledApps, ShellPage.Activity, ShellPage.Detail, ShellPage.UpdateJob })
            Assert.IsTrue(double.IsPositiveInfinity(PageLayout.MaximumWidth(list, 68)), list.ToString());
    }
}
