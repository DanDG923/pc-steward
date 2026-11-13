using System.Globalization;

namespace PCSteward.Application;

/// <summary>How Settings changes are saved (Task 73 Phase 4; audit P-M7; independent review owner decision).</summary>
public enum SettingsSaveModel
{
    /// <summary>
    /// Every control saves as soon as it is changed, as in Windows 11 Settings, except the Schedule card: a schedule change
    /// registers or removes a Windows Task Scheduler task, so it applies only when the person selects its one "Save schedule"
    /// button. No other card has a Save button.
    /// </summary>
    AppliesImmediatelyExceptSchedule
}

/// <summary>How a "Save schedule" attempt ended (independent review fix 5).</summary>
public enum ScheduleSaveOutcome
{
    /// <summary>The Windows task and the saved settings both match the requested schedule.</summary>
    Saved,
    /// <summary>Windows Task Scheduler refused the change; nothing was saved.</summary>
    SchedulerRefused,
    /// <summary>The task changed but the settings could not be saved, so the previous task was registered again.</summary>
    SettingsNotSavedTaskRestored,
    /// <summary>The task changed, the settings could not be saved and the previous task could not be registered again.</summary>
    SettingsNotSavedTaskChanged
}

/// <summary>The message shown after a schedule save that did not fully succeed.</summary>
public sealed record ScheduleSaveMessage(string Title, string Body);

/// <summary>
/// The Schedule section (Task 73 Phase 4; audit P-M7; UI review 2026-11-13, item 16): one "Check automatically" choice of Off,
/// Daily or Weekly, with the time, the day and the notifications choice shown only when they apply.
/// </summary>
public sealed record ScheduleCardPresentation(
    int CadenceIndex,
    bool IsScheduled,
    bool NotificationsEnabled,
    DayOfWeek WeeklyDay,
    TimeOnly LocalStartTime,
    bool AreControlsEnabled,
    bool ShowsTime,
    bool ShowsWeekday,
    bool ShowsNotifications,
    bool IsSaveEnabled,
    string Summary,
    IReadOnlyList<DetailFact> Status);

/// <summary>The "Copy diagnostic" action (FR-HIS-05).</summary>
public sealed record DiagnosticCopyPresentation(string Label, string AccessibleName, string Description, bool IsEnabled, string? UnavailableReason);

/// <summary>
/// Task 73 Phase 4 (ADR-030 concept A; audit P-M7, §6 item 10; FR-DAT-01/03, FR-HIS-05, SPEC §17): the Settings page as grouped
/// cards in one column, with one save model: controls apply immediately except the Schedule card, which has one "Save schedule"
/// button. Presentation only: building these values saves, schedules, enables or starts nothing.
/// </summary>
public static class SettingsPresenter
{
    /// <summary>
    /// The one save model. Source and AppX choices save per control (with the existing disclosure first). Schedule changes register
    /// or remove a Windows Task Scheduler task, so they apply only through the explicit "Save schedule" button, which saves the whole
    /// schedule record through the register-then-save operation; a failure leaves the saved schedule unchanged.
    /// </summary>
    public const SettingsSaveModel SaveModel = SettingsSaveModel.AppliesImmediatelyExceptSchedule;

    /// <summary>The one sentence that tells people how saving works.</summary>
    public const string SaveModelText = "Changes save as soon as you make them, except the schedule, which you save with Save schedule.";

    /// <summary>The Schedule card's one Save button (the only Save button in Settings).</summary>
    public const string SaveScheduleLabel = "Save schedule";

    /// <summary>The Schedule card description.</summary>
    public const string ScheduleDescription = "A scheduled check only scans this PC. It never approves or installs an update, and nothing stays running in the background. Changes to the schedule apply when you select Save schedule.";

    /// <summary>Said in place of the saved schedule while the controls show a change that has not been saved (item 16).</summary>
    public const string ScheduleUnsavedText = "Not saved yet. Select Save schedule to apply this change.";

    /// <summary>Said when the Windows task was changed but the settings were not, so saving again brings the two back in step.</summary>
    public const string ScheduleOutOfStepText = "The Windows scheduled task doesn't match what is shown here. Select Save schedule to put it right.";

    /// <summary>Shown instead of saving when the cadence or weekday has no selection (fail closed).</summary>
    public const string ScheduleNotChosen = "Choose a schedule before saving.";

    /// <summary>
    /// The sections on the Settings page, in order (single column). UI polish phase 4 (approved target T-Settings): Decisions and
    /// About are left to the navigation pane, and Data &amp; reset opens from Privacy &amp; data.
    /// </summary>
    public static IReadOnlyList<string> Sections { get; } = ["Update checks", "Scan scope", "Schedule", "Privacy & data"];

    /// <summary>The Settings page description (approved target T-Settings).</summary>
    public const string PageDescription = "Choose what PC Steward looks at, when it checks and what it keeps. Changes save straight away, except the schedule.";

    /// <summary>
    /// The "Look for updates online" row's description (UI review 2026-11-13, item 8): off says what PC Steward does without it and
    /// that turning it on asks first; on gives the same freshness sentence Home shows.
    /// </summary>
    public static string SourceRowDescription(bool enabled, string freshness) => enabled
        ? $"On. {freshness}"
        : "Off. PC Steward only lists what is installed. Turning this on shows what is shared and asks you first.";

    /// <summary>The name of the one setting that lets PC Steward ask WinGet and Chocolatey about updates.</summary>
    public const string SourceSettingLabel = "Look for updates online";

    /// <summary>The cadence choices, in the cadence list's order.</summary>
    public static IReadOnlyList<string> CadenceOptions { get; } = ["Off", "Daily", "Weekly"];

    /// <summary>The source disclosure reminder shown beside the source consent toggle.</summary>
    public const string SourceConsentNote = "Turning this on shows what is shared and asks you first.";

    /// <summary>The identifying-data warning shown before exports and diagnostics (FR-DAT-03).</summary>
    public const string ExportWarning = "Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.";

    public static int CadenceIndex(ScheduleCadence cadence) => cadence switch { ScheduleCadence.Daily => 1, ScheduleCadence.Weekly => 2, _ => 0 };

    public static ScheduleCadence CadenceAt(int index) => index switch { 1 => ScheduleCadence.Daily, 2 => ScheduleCadence.Weekly, _ => ScheduleCadence.Manual };

    /// <summary>
    /// The schedule record requested by the current control values (item 16: one choice, so Daily or Weekly means scheduled and
    /// Off means not scheduled; there is no separate switch). Normalized so Off is always saved as not scheduled. Fails closed:
    /// with no cadence selection, no weekday selection (index -1) or an out-of-range time it returns <see langword="null"/> and
    /// nothing may be saved. The weekday index is the <see cref="DayOfWeek"/> value (Sunday = 0).
    /// </summary>
    public static ScheduledCheckSettings? RequestedSchedule(int cadenceIndex, bool notificationsEnabled, int weekdayIndex, TimeSpan localStartTime)
    {
        if (cadenceIndex < 0 || cadenceIndex >= CadenceOptions.Count) return null;
        if (weekdayIndex < (int)DayOfWeek.Sunday || weekdayIndex > (int)DayOfWeek.Saturday) return null;
        if (localStartTime < TimeSpan.Zero || localStartTime >= TimeSpan.FromDays(1)) return null;
        return new ScheduledCheckSettings(CadenceAt(cadenceIndex), cadenceIndex is 1 or 2, notificationsEnabled, (DayOfWeek)weekdayIndex, TimeOnly.FromTimeSpan(localStartTime)).Normalize();
    }

    /// <summary>
    /// Which schedule controls are shown for the selected choice: the time and the notifications choice for Daily and Weekly, the
    /// day only for Weekly, and none of them for Off (or no selection).
    /// </summary>
    public static (bool Time, bool Weekday, bool Notifications) ScheduleControlsShown(int cadenceIndex)
    {
        bool scheduled = cadenceIndex is 1 or 2;
        return (scheduled, cadenceIndex == 2, scheduled);
    }

    /// <summary>The choice shown for a saved record: a daily or weekly check that was saved while paused reads as Off.</summary>
    public static int ShownCadenceIndex(ScheduledCheckSettings saved)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ScheduledCheckSettings settings = saved.Normalize();
        return settings.IsEnabled ? CadenceIndex(settings.Cadence) : 0;
    }

    /// <summary>
    /// True when what the controls ask for differs from the saved schedule in a way that changes what runs, so "Save schedule" is
    /// shown only then (item 16). Two schedules that are both off are the same; the day counts only for a weekly check. With no
    /// valid request it is true, so the button is there to say what is missing.
    /// </summary>
    public static bool HasScheduleChange(ScheduledCheckSettings saved, ScheduledCheckSettings? requested)
    {
        ArgumentNullException.ThrowIfNull(saved);
        if (requested is null) return true;
        ScheduledCheckSettings was = saved.Normalize(), now = requested.Normalize();
        if (!was.IsEnabled && !now.IsEnabled) return false;
        if (was.IsEnabled != now.IsEnabled || was.Cadence != now.Cadence) return true;
        if (was.LocalStartTime != now.LocalStartTime || was.NotificationsEnabled != now.NotificationsEnabled) return true;
        return now.Cadence == ScheduleCadence.Weekly && was.WeeklyDay != now.WeeklyDay;
    }

    /// <summary>The message after a schedule save that did not fully succeed; <see langword="null"/> when it was saved.</summary>
    public static ScheduleSaveMessage? ScheduleSaveResult(ScheduleSaveOutcome outcome) => outcome switch
    {
        ScheduleSaveOutcome.Saved => null,
        ScheduleSaveOutcome.SchedulerRefused => new("Schedule was not changed", "The schedule was not changed: Windows Task Scheduler refused the change. Your saved settings were left unchanged."),
        ScheduleSaveOutcome.SettingsNotSavedTaskRestored => new("Schedule was not changed", "The schedule was not changed: PC Steward couldn't save its settings, so the Windows scheduled task was put back as it was. Make the change again to retry."),
        _ => new("Schedule needs attention", "The Windows scheduled task was changed, but PC Steward couldn't save its settings or put the task back as it was. Select Save schedule to try again.")
    };

    /// <summary>
    /// The Schedule section for the saved settings. A daily or weekly check saved while paused reads as Off, with its day and time
    /// kept in the controls for when it is chosen again. While a change is being saved every schedule control is unavailable, and
    /// "Save schedule" is also unavailable while other work (a scan, source check, update or cleanup) is active.
    /// </summary>
    public static ScheduleCardPresentation Schedule(ScheduledCheckSettings saved, ScheduledCheckStatus status, bool isSaving, CultureInfo? culture = null, TimeZoneInfo? zone = null, bool otherWorkActive = false)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(status);
        ScheduledCheckSettings settings = saved.Normalize();
        int shown = ShownCadenceIndex(settings);
        (bool showsTime, bool showsWeekday, bool showsNotifications) = ScheduleControlsShown(shown);
        string time = settings.LocalStartTime.ToString(DisplayFormat.TimePattern, culture ?? CultureInfo.CurrentCulture);
        string summary = shown switch
        {
            1 => $"Checks this PC every day at {time}.",
            2 => $"Checks this PC every {(culture ?? CultureInfo.CurrentCulture).DateTimeFormat.GetDayName(settings.WeeklyDay)} at {time}.",
            _ => "Off. PC Steward checks only when you start a scan."
        };
        DetailFact[] facts =
        [
            new("Task status", status.LastResult),
            new("Last completed", status.LastCompletedAt is { } completed ? DisplayFormat.DateTime(completed, culture, zone) : "Not yet")
        ];
        return new(shown, settings.IsEnabled, settings.NotificationsEnabled, settings.WeeklyDay, settings.LocalStartTime,
            AreControlsEnabled: !isSaving,
            ShowsTime: showsTime,
            ShowsWeekday: showsWeekday,
            ShowsNotifications: showsNotifications,
            IsSaveEnabled: !isSaving && !otherWorkActive,
            summary, facts);
    }

    /// <summary>
    /// FR-HIS-05: copy the existing sanitized diagnostic bundle (<see cref="SanitizedDiagnosticBundle.Create"/>) of recent update
    /// outcomes. It is unavailable only when the recorded history could not be read.
    /// </summary>
    public static DiagnosticCopyPresentation DiagnosticCopy(bool historyUnavailable) => new(
        "Copy diagnostic",
        "Copy diagnostic (a sanitized summary of recent update outcomes)",
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
