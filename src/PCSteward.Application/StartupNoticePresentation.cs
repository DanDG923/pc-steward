namespace PCSteward.Application;

/// <summary>
/// Task 73 Phase 4 (audit P-L5; FR-HIS-05, AC-UX-05 best effort): copy for the startup busy, reset-recovery and startup-failure
/// windows. Presentation only: the windows' start-up decisions are unchanged, and nothing here retries, resets or deletes anything.
/// Every diagnostic is passed through <see cref="SanitizedDiagnosticBundle.Sanitize"/> (paths, passwords and tokens removed) before
/// it is shown or copied, and the current user's profile folder is redacted first so a profile name with spaces cannot leak.
/// </summary>
public static class StartupNoticeText
{
    public const string CopyDetailsLabel = "Copy details";
    public const string CloseLabel = "Close";
    public const string DetailsCopied = "Details copied to the clipboard.";
    public const string DetailsNotCopied = "The clipboard is busy, so the details weren't copied. Select the text and copy it instead.";

    /// <summary>The startup-failure diagnostic: the exception type and its sanitized message.</summary>
    public static string ExceptionDetails(string typeName, string message, string? userProfile = null) =>
        $"{Safe(typeName, userProfile)}: {Safe(message, userProfile)}";

    /// <summary>
    /// The reset-recovery details: one line per remaining item, "Category: Reason", followed by its sanitized description when one
    /// was recorded.
    /// </summary>
    public static string ResidualDetails(IEnumerable<(string Category, string Reason, string Detail)> residuals, string? userProfile = null)
    {
        ArgumentNullException.ThrowIfNull(residuals);
        return string.Join("\n", residuals.Select(item =>
        {
            string detail = Safe(item.Detail, userProfile);
            return detail.Length == 0
                ? $"{Safe(item.Category, userProfile)}: {Safe(item.Reason, userProfile)}"
                : $"{Safe(item.Category, userProfile)}: {Safe(item.Reason, userProfile)} — {detail}";
        }));
    }

    private static string Safe(string? value, string? userProfile)
    {
        string text = value ?? string.Empty;
        string profile = userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) text = text.Replace(profile.TrimEnd('\\', '/'), "[profile]", StringComparison.OrdinalIgnoreCase);
        return SanitizedDiagnosticBundle.Sanitize(text);
    }
}
