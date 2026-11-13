namespace PCSteward.Application;

/// <summary>
/// UI review 2026-11-13 (item 19, first part; FR-UI-01/05): how wide a page's column may be. Pages that are lists (Home, Updates,
/// Installed apps, Activity, an app's details and an update in progress) fill the window, as the owner asked for on a maximised
/// window. Pages that are text and switches read as one column of about 1,000 units, so on a very wide display a setting's label
/// stays next to its control instead of a screen away from it. Presentation only.
/// </summary>
public static class PageLayout
{
    /// <summary>The widest a reading page's content is, in the same units as the rest of the layout.</summary>
    public const double ReadingColumnWidth = 1000;

    /// <summary>True for a page that is text, switches and short rows, where a line across a whole wide display is hard to follow.</summary>
    public static bool IsReadingPage(ShellPage page) =>
        page is ShellPage.Settings or ShellPage.About or ShellPage.Decisions or ShellPage.Privacy or ShellPage.Storage or ShellPage.Scan or ShellPage.Onboarding;

    /// <summary>
    /// The widest the page's column may be, including the padding at its sides; infinity for a page that fills the window. A
    /// window narrower than this is unaffected: the column is as wide as the window, exactly as before.
    /// </summary>
    public static double MaximumWidth(ShellPage page, double sidePadding) =>
        IsReadingPage(page) ? ReadingColumnWidth + Math.Max(0, sidePadding) : double.PositiveInfinity;

    // UI review 2026-11-13 (item 19, second part): on a wide window a list keeps its place and the selected app's details open
    // beside it, so the owner never leaves the list and comes back. Below the width here an app's details open as a page, as before.

    /// <summary>The narrowest content area (the window less the navigation pane) that shows an app's details beside its list.</summary>
    public const double DetailsBesideMinimumWidth = 1200;

    /// <summary>The narrowest and widest the details column is.</summary>
    public const double DetailsPaneMinimumWidth = 420;
    public const double DetailsPaneMaximumWidth = 620;

    public const string CloseDetailsLabel = "Close details";

    /// <summary>True for the pages that are a list of apps: Updates and Installed apps.</summary>
    public static bool IsListPage(ShellPage page) => page is ShellPage.Updates or ShellPage.InstalledApps;

    /// <summary>Whether an app opened from <paramref name="page"/> shows its details beside the list, given the content area's width.</summary>
    public static bool ShowsDetailsBeside(ShellPage page, double contentWidth) => IsListPage(page) && contentWidth >= DetailsBesideMinimumWidth;

    /// <summary>The details column's width: a little over a third of the content area, within its narrowest and widest.</summary>
    public static double DetailsPaneWidth(double contentWidth) =>
        Math.Clamp(Math.Floor(contentWidth * 0.36), DetailsPaneMinimumWidth, DetailsPaneMaximumWidth);

    /// <summary>The details column's accessible name.</summary>
    public static string DetailsPaneName(string appName) => $"Details for {appName}";

    /// <summary>What a screen reader is told when the details open beside the list, because focus stays on the list.</summary>
    public static string DetailsShownAnnouncement(string appName) => $"Details for {appName} are shown beside the list.";

    /// <summary>
    /// FR-POL-03/05: where the line that confirms a saved choice (with Undo) is shown. It goes in the details column when that
    /// column is showing the app the choice was made for, so it is beside the action; otherwise it goes on the page as before.
    /// </summary>
    public static bool NoticeGoesInDetails(bool detailsShownBeside, string? detailsAppKey, string noticeAppKey) =>
        detailsShownBeside && string.Equals(detailsAppKey, noticeAppKey, StringComparison.Ordinal);
}
