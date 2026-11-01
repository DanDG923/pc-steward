namespace PCSteward.Application;

/// <summary>
/// Task 73 Phase 4 (audit P-L5; FR-HIS-05, AC-UX-05 best effort): copy for the startup busy, reset-recovery and startup-failure
/// windows. Presentation only: the windows' start-up decisions are unchanged, and nothing here retries, resets or deletes anything.
/// </summary>
public static class StartupNoticeText
{
    public const string CopyDetailsLabel = "Copy details";
    public const string CloseLabel = "Close";
    public const string DetailsCopied = "Details copied to the clipboard.";
    public const string DetailsNotCopied = "The clipboard is busy, so the details weren't copied. Select the text and copy it instead.";

    /// <summary>The startup-failure diagnostic: the exception type and message, exactly as before.</summary>
    public static string ExceptionDetails(string typeName, string message) => $"{typeName}: {message}";

    /// <summary>
    /// The reset-recovery details: one line per remaining item, "Category: Reason", followed by its plain description when one was
    /// recorded.
    /// </summary>
    public static string ResidualDetails(IEnumerable<(string Category, string Reason, string Detail)> residuals)
    {
        ArgumentNullException.ThrowIfNull(residuals);
        return string.Join("\n", residuals.Select(item => string.IsNullOrWhiteSpace(item.Detail)
            ? $"{item.Category}: {item.Reason}"
            : $"{item.Category}: {item.Reason} — {item.Detail}"));
    }
}
