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

    [TestMethod]
    [Description("FR-UI-01/04/05 (item 19): on a wide window an app opened from Updates or Installed apps shows its details beside the list; below about 1,200 units, and from any other page, details open as a page as before.")]
    public void DetailsOpenBesideAListOnAWideWindow()
    {
        Assert.IsTrue(PageLayout.ShowsDetailsBeside(ShellPage.InstalledApps, 1200));
        Assert.IsTrue(PageLayout.ShowsDetailsBeside(ShellPage.Updates, 2328), "the owner's display: 3840 x 2160 at 150% is 2560 wide, less the navigation pane");
        Assert.IsFalse(PageLayout.ShowsDetailsBeside(ShellPage.InstalledApps, 1199.5), "below the width everything behaves as before");
        Assert.IsFalse(PageLayout.ShowsDetailsBeside(ShellPage.Updates, 0), "a window that has not been laid out yet");
        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            bool list = page is ShellPage.Updates or ShellPage.InstalledApps;
            Assert.AreEqual(list, PageLayout.IsListPage(page), page.ToString());
            Assert.AreEqual(list, PageLayout.ShowsDetailsBeside(page, 4000), page.ToString());
        }
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 19): the details column is a little over a third of the content area, never narrower than 420 or wider than 620, so the list keeps most of the width.")]
    public void TheDetailsColumnLeavesTheListMostOfTheWidth()
    {
        Assert.AreEqual(432d, PageLayout.DetailsPaneWidth(1200));
        Assert.AreEqual(576d, PageLayout.DetailsPaneWidth(1600));
        Assert.AreEqual(620d, PageLayout.DetailsPaneWidth(2328));
        Assert.AreEqual(420d, PageLayout.DetailsPaneWidth(900), "never narrower than its minimum");
        foreach (double width in new[] { 1200d, 1432d, 1688d, 2328d, 3608d })
        {
            double pane = PageLayout.DetailsPaneWidth(width);
            Assert.IsTrue(pane >= PageLayout.DetailsPaneMinimumWidth && pane <= PageLayout.DetailsPaneMaximumWidth, width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.IsTrue(width - pane >= 760, $"the list keeps at least 760 units at {width}");
        }
        Assert.AreEqual("Close details", PageLayout.CloseDetailsLabel);
        Assert.AreEqual("Details for Discord", PageLayout.DetailsPaneName("Discord"));
        Assert.AreEqual("Details for Discord are shown beside the list.", PageLayout.DetailsShownAnnouncement("Discord"));
    }
}
