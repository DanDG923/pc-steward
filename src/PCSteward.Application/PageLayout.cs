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
}
