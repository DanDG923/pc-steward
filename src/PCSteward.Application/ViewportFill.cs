namespace PCSteward.Application;

/// <summary>
/// AC-UX-03 (UI review 2026-11-13, fault 1): the arithmetic that grows a list or an empty state to the bottom of the window.
/// The Desktop supplies measurements of the page's content; <c>below</c> is the content that follows the card, never the blank
/// space under a page that is shorter than the window. Presentation only.
/// </summary>
public static class ViewportFill
{
    /// <summary>
    /// The height for a bounded list inside a card so the card ends at the bottom of the viewport: the viewport, less what is
    /// above the card, what follows it, and the card's own chrome around the list. Never less than <paramref name="minimumHeight"/>.
    /// </summary>
    public static double ListHeight(double viewportHeight, double top, double below, double chrome, double minimumHeight) =>
        Math.Max(minimumHeight, Math.Floor(viewportHeight - top - Math.Max(0, below) - Math.Max(0, chrome)));

    /// <summary>The minimum height for a card so it ends at the bottom of the viewport; zero when there is no room.</summary>
    public static double CardHeight(double viewportHeight, double top, double below) =>
        Math.Max(0, Math.Floor(viewportHeight - top - Math.Max(0, below)));
}
