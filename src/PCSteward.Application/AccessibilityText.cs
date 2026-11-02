namespace PCSteward.Application;

/// <summary>
/// AC-UX-01–09 (best effort, ADR-021), FR-UI-02 (Task 73 Phase 5; concept study §2 foundation 10): portable accessibility rules
/// the Desktop applies. Accessible names start with the visible label (WCAG 2.5.3 label in name). Presentation only: nothing here
/// decides trust, approves, starts or changes anything.
/// </summary>
public static class AccessibilityText
{
    /// <summary>
    /// The accessible name for a control whose visible label is <paramref name="visibleLabel"/>: the label itself, or the label
    /// followed by <paramref name="detail"/>. A trailing ellipsis ("Ignore…") is dropped from the spoken start, so speech users
    /// can say what they see. The result always starts with the label's words.
    /// </summary>
    public static string LabelInName(string visibleLabel, string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(visibleLabel);
        if (string.IsNullOrWhiteSpace(detail)) return visibleLabel;
        return $"{SpokenLabel(visibleLabel)} {detail.Trim()}";
    }

    /// <summary>True when <paramref name="accessibleName"/> starts with the words of <paramref name="visibleLabel"/>.</summary>
    public static bool StartsWithLabel(string accessibleName, string visibleLabel) =>
        !string.IsNullOrWhiteSpace(accessibleName) && !string.IsNullOrWhiteSpace(visibleLabel) &&
        accessibleName.StartsWith(SpokenLabel(visibleLabel), StringComparison.Ordinal);

    private static string SpokenLabel(string visibleLabel) => visibleLabel.Trim().TrimEnd('…').TrimEnd();
}

/// <summary>The three keyboard regions F6 moves between (research §2 item 2: "F6 cycles major panes").</summary>
public enum FocusRegion
{
    Navigation,
    Page,
    OperationBand
}

/// <summary>
/// AC-UX-01/02 (Task 73 Phase 5): the order F6 and Shift+F6 move focus in — navigation pane, page content, then the operation
/// band when it is shown — wrapping at either end. Regions that are not available (for example a hidden band) are skipped.
/// </summary>
public static class FocusRegionCycle
{
    private static readonly FocusRegion[] Order = [FocusRegion.Navigation, FocusRegion.Page, FocusRegion.OperationBand];

    public static FocusRegion? Next(FocusRegion? current, IReadOnlyCollection<FocusRegion> available, bool backwards)
    {
        ArgumentNullException.ThrowIfNull(available);
        FocusRegion[] regions = Order.Where(available.Contains).ToArray();
        if (regions.Length == 0) return null;
        int index = current is { } region ? Array.IndexOf(regions, region) : -1;
        if (index < 0) return backwards ? regions[^1] : regions[0];
        int step = backwards ? -1 : 1;
        return regions[(index + step + regions.Length) % regions.Length];
    }
}

/// <summary>
/// AC-UX-09, FR-UPD-01 (Task 73 Phase 5): when a keyboard shortcut may act. F5 ("Scan again" or "Check for updates") runs only
/// when the shell is shown, nothing is running, no update review is open and no dialog is open — never during active work.
/// Shortcuts only navigate, move focus or start the same read-only scan as the visible button; none of them can confirm a review,
/// approve an update or skip a confirmation.
/// </summary>
public static class ShortcutGuard
{
    public static bool CanStartScan(bool shellShown, bool hasActiveOperation, bool reviewOpen, bool dialogOpen) =>
        shellShown && !hasActiveOperation && !reviewOpen && !dialogOpen;
}

/// <summary>
/// AC-UX-09 (Task 73 Phase 5; research §2 item 10): the short sentences a screen reader hears when work finishes (a UIA
/// notification) and the stable keys that decide when the progress live region speaks, so elapsed-time ticks never repeat it.
/// </summary>
public static class OperationAnnouncements
{
    public static string ScanFinished(bool succeeded, bool cancelled, int appCount) =>
        cancelled ? "Scan cancelled. Your last completed scan is kept."
        : succeeded ? $"Scan finished. {DisplayFormat.Plural(appCount, "app")} found on this PC."
        : "The scan didn't finish. Your last completed scan is kept.";

    public static string UpdateCheckFinished(int readyCount, int needsReviewCount) =>
        $"Update check finished. {DisplayFormat.Plural(readyCount, "update")} ready to review, {needsReviewCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)} need{(needsReviewCount == 1 ? "s" : string.Empty)} your review.";

    public static string ReviewReady(string appName) =>
        $"Review ready for {appName}. Check the details; nothing changes until you choose the update button. Cancel is focused.";

    public static string JobFinished(string appName, string headline) => $"{appName}: {headline}";

    public static string Completed(string title, string? message = null) =>
        string.IsNullOrWhiteSpace(message) ? $"{title}." : $"{title}. {message}";

    /// <summary>
    /// The state part of the operation band text — what is happening, without the elapsed time — so the live region is raised
    /// only when the state changes, not on every progress tick.
    /// </summary>
    public static string StateKey(string operationLabel)
    {
        ArgumentNullException.ThrowIfNull(operationLabel);
        int elapsed = operationLabel.LastIndexOf(" · ", StringComparison.Ordinal);
        return elapsed > 0 && IsElapsed(operationLabel[(elapsed + 3)..]) ? operationLabel[..elapsed] : operationLabel;
    }

    private static bool IsElapsed(string text) =>
        text.Length >= 4 && text.All(character => char.IsDigit(character) || character == ':') && text.Contains(':', StringComparison.Ordinal);
}
