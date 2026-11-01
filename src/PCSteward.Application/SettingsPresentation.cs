using System.Globalization;

namespace PCSteward.Application;

/// <summary>How Settings changes are saved (Task 73 Phase 4; audit P-M7).</summary>
public enum SettingsSaveModel
{
    /// <summary>Every control saves as soon as it is changed, as in Windows 11 Settings. No card has a Save button.</summary>
    AppliesImmediately
}

/// <summary>The Schedule card: control values and which controls are available (Task 73 Phase 4; audit P-M7).</summary>
public sealed record ScheduleCardPresentation(
    int CadenceIndex,
    bool IsEnabled,
    bool NotificationsEnabled,
    DayOfWeek WeeklyDay,
    TimeOnly LocalStartTime,
    bool IsCadenceEnabled,
    bool IsTimeEnabled,
    bool IsWeekdayEnabled,
    bool IsEnableToggleEnabled,
    bool IsNotificationsToggleEnabled,
    string Summary,
    IReadOnlyList<DetailFact> Status);

/// <summary>The "Copy diagnostic" action (FR-HIS-05).</summary>
public sealed record DiagnosticCopyPresentation(string Label, string AccessibleName, string Description, bool IsEnabled, string? UnavailableReason);

/// <summary>
/// Task 73 Phase 4 (ADR-030 concept A; audit P-M7, §6 item 10; FR-DAT-01/03, FR-HIS-05, SPEC §17): the Settings page as grouped
/// cards in one column, with one save model. Presentation only: building these values saves, schedules, enables or starts nothing.
/// </summary>
public static class SettingsPresenter
{
    /// <summary>
    /// The one save model. Source and AppX choices were already saved per control (with the existing disclosure first), so the
    /// schedule now follows the same rule: each change saves the whole schedule record through the same register-then-save
    /// operation the old "Save schedule" button used, and a failure leaves the saved schedule unchanged.
    /// </summary>
    public const SettingsSaveModel SaveModel = SettingsSaveModel.AppliesImmediately;

    /// <summary>The one sentence that tells people how saving works.</summary>
    public const string SaveModelText = "Changes save as soon as you make them. Nothing here starts a scan, a source check, a download or an update.";

    /// <summary>The cards on the Settings page, in order (single column).</summary>
    public static IReadOnlyList<string> Sections { get; } = ["Update sources", "Scan scope", "Schedule", "Decisions", "Privacy & exports", "Data & reset", "About"];

    /// <summary>The cadence choices, in RadioButtons order.</summary>
    public static IReadOnlyList<string> CadenceOptions { get; } = ["Manual only", "Daily", "Weekly"];

    /// <summary>The source disclosure reminder shown beside the source consent toggle.</summary>
    public const string SourceConsentNote = "Turning this on shows what is shared and asks for your approval first. It never starts a scan, a source check, a download or an update.";

    /// <summary>The identifying-data warning shown before exports and diagnostics (FR-DAT-03).</summary>
    public const string ExportWarning = "Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.";

    public static int CadenceIndex(ScheduleCadence cadence) => cadence switch { ScheduleCadence.Daily => 1, ScheduleCadence.Weekly => 2, _ => 0 };

    public static ScheduleCadence CadenceAt(int index) => index switch { 1 => ScheduleCadence.Daily, 2 => ScheduleCadence.Weekly, _ => ScheduleCadence.Manual };

    /// <summary>
    /// The schedule record requested by the current control values. This is the same record the old "Save schedule" button built,
    /// normalized, so "Manual only" always means not scheduled.
    /// </summary>
    public static ScheduledCheckSettings RequestedSchedule(int cadenceIndex, bool isEnabled, bool notificationsEnabled, DayOfWeek? weeklyDay, TimeSpan localStartTime) =>
        new ScheduledCheckSettings(CadenceAt(cadenceIndex), isEnabled, notificationsEnabled, weeklyDay ?? DayOfWeek.Monday, TimeOnly.FromTimeSpan(localStartTime)).Normalize();

    /// <summary>
    /// The Schedule card for the saved settings. "Manual only" and "Enable scheduled checks" stay two controls because they do not map
    /// one to one onto the saved record (a daily or weekly cadence can be saved while paused); with "Manual only" the time, weekday and
    /// enable controls are unavailable. While a change is being saved every schedule control is unavailable.
    /// </summary>
    public static ScheduleCardPresentation Schedule(ScheduledCheckSettings saved, ScheduledCheckStatus status, bool isSaving, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(status);
        ScheduledCheckSettings settings = saved.Normalize();
        bool scheduled = settings.Cadence != ScheduleCadence.Manual;
        string time = settings.LocalStartTime.ToString(DisplayFormat.TimePattern, culture ?? CultureInfo.CurrentCulture);
        string summary = settings.Cadence switch
        {
            ScheduleCadence.Manual => "Not scheduled. PC Steward checks only when you start a scan.",
            ScheduleCadence.Daily when settings.IsEnabled => $"Checks this PC every day at {time}.",
            ScheduleCadence.Weekly when settings.IsEnabled => $"Checks this PC every {settings.WeeklyDay} at {time}.",
            _ => "Paused. Turn on scheduled checks to create the check."
        };
        DetailFact[] facts =
        [
            new("Task status", status.LastResult),
            new("Last completed", status.LastCompletedAt is { } completed ? DisplayFormat.DateTime(completed, culture, zone) : "Not yet")
        ];
        return new(CadenceIndex(settings.Cadence), settings.IsEnabled, settings.NotificationsEnabled, settings.WeeklyDay, settings.LocalStartTime,
            IsCadenceEnabled: !isSaving,
            IsTimeEnabled: scheduled && !isSaving,
            IsWeekdayEnabled: settings.Cadence == ScheduleCadence.Weekly && !isSaving,
            IsEnableToggleEnabled: scheduled && !isSaving,
            IsNotificationsToggleEnabled: !isSaving,
            summary, facts);
    }

    /// <summary>
    /// FR-HIS-05: copy the existing sanitized diagnostic bundle (<see cref="SanitizedDiagnosticBundle.Create"/>) of recent update
    /// outcomes. It is unavailable only when the recorded history could not be read.
    /// </summary>
    public static DiagnosticCopyPresentation DiagnosticCopy(bool historyUnavailable) => new(
        "Copy diagnostic",
        "Copy a sanitized diagnostic of recent update outcomes",
        "Copies a sanitized summary of recent update outcomes (package IDs, versions, outcomes and exit codes) for a support report. Paths, passwords and tokens are removed and nothing is sent anywhere. It names installed apps, so share it only with people you trust.",
        !historyUnavailable,
        historyUnavailable ? "PC Steward couldn't read its recorded update history just now, so there is nothing to copy." : null);

    /// <summary>The most entries <see cref="SanitizedDiagnosticBundle.Create"/> includes (pinned by a test against the bundle itself).</summary>
    public const int DiagnosticBundleMaximumEntries = 256;

    /// <summary>The confirmation shown after the diagnostic was copied.</summary>
    public static string DiagnosticCopied(int updateRecords) => updateRecords == 0
        ? "Diagnostic copied to the clipboard. It contains no update records yet."
        : $"Diagnostic copied to the clipboard ({DisplayFormat.Plural(Math.Min(updateRecords, DiagnosticBundleMaximumEntries), "update record")}). Paste it into your report.";

    /// <summary>Shown when Windows would not accept the clipboard text.</summary>
    public const string DiagnosticCopyFailed = "The clipboard is busy, so the diagnostic wasn't copied. Try again.";
}
