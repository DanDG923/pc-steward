using System.Globalization;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Task 73 Phase 4 (ADR-030 foundation 6; FR-DAT-01/03/06–15, FR-HIS-05, AC-UX-07/08, SPEC §12.5/§17.3; audit P-H4, P-M7, P-L5):
/// Settings cards, the save model, Data &amp; reset cards, Copy diagnostic and the startup notices. Portable and deterministic (fixed
/// culture and UTC zone). Nothing here deletes, schedules or starts anything.
/// </summary>
[TestClass]
public sealed class SettingsDataResetPresentationTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Cleared = new(2026, 10, 30, 9, 15, 0, TimeSpan.Zero);
    // UI polish phase 4 (approved target T-Settings): Decisions and About are in the navigation pane; Data & reset opens from Privacy & data.
    private static readonly string[] SettingsSections = ["Update checks", "Scan scope", "Schedule", "Privacy & data", "AI explanations"];
    private static readonly string[] Cadences = ["Off", "Daily", "Weekly"];
    private static readonly string[] CardTitles = ["Scan data", "Downloaded update files", "Source metadata", "Logs and history", "Mappings, pins and ignores"];
    private static readonly string[] ActionLabels = ["Clear scan data", "Clear downloaded files", "Clear source metadata", "Clear logs", "Clear update history", "Clear mappings and policies"];
    private static readonly string[] ScanFacts = ["Size", "Items", "Last cleared"];
    private static readonly string[] ScanFactValues = ["About 2 MB", "1,234 items", "30 Oct 2026 · 09:15"];
    private static readonly string[] DownloadFacts = ["Size", "Can be freed", "In use", "Last cleaned"];
    private static readonly string[] SavedCalls = ["register Weekly", "save Weekly"];
    private static readonly string[] RefusedCalls = ["register Weekly"];
    private static readonly string[] RestoredCalls = ["register Weekly", "save Weekly", "register Daily"];
    private static readonly string[] DownloadFactValues = ["40 MB", "30 MB", "1 file kept for an active update", "Never"];

    private static DataResetInputs Inputs(bool busy = false, long recoverable = 30L * 1024 * 1024, int active = 1, int records = 3, bool historyUnavailable = false) =>
        new(new ScanDataSummary(2L * 1024 * 1024, Cleared, 1234), 40L * 1024 * 1024, recoverable, 4, active, null,
            "Cached — WinGet checked 2 hours ago.", records, 500, historyUnavailable, 2, busy);

    [TestMethod]
    [Description("SPEC §17.3; FR-DAT-06/08/12; AC-UX-07 (audit P-H4): separate cards in order, the three exact labels kept, factory reset separate from routine cleanup.")]
    public void DataResetShowsSeparateCardsWithTheExactLabels()
    {
        DataResetPagePresentation page = DataResetPresenter.Build(Inputs(), Culture, Utc);

        CollectionAssert.AreEqual(CardTitles, page.Cards.Select(card => card.Title).ToArray());
        CollectionAssert.AreEqual(ActionLabels, page.Cards.SelectMany(card => card.Actions).Select(action => action.Label).ToArray());
        Assert.AreEqual("Clear scan data", page.Cards[0].Actions.Single().Label);
        Assert.AreEqual("Clear downloaded files", page.Cards[1].Actions.Single().Label);
        Assert.AreEqual("Factory reset PC Steward", page.FactoryReset.Label);
        Assert.AreEqual("Danger zone", page.FactoryReset.Heading);
        Assert.AreEqual("RESET", page.FactoryReset.ConfirmationPhrase);
        Assert.AreEqual("RESET", DataResetPresenter.FactoryResetConfirmationPhrase);
        Assert.IsFalse(page.Cards.SelectMany(card => card.Actions).Any(action => action.Label.Contains("Factory reset", StringComparison.Ordinal)), "factory reset is never beside routine cleanup");
        Assert.AreEqual(page.Cards.SelectMany(card => card.Actions).Count(), page.Cards.SelectMany(card => card.Actions).Select(action => action.Action).Distinct().Count(), "each action maps to one existing operation");
        foreach (DataActionPresentation action in page.Cards.SelectMany(card => card.Actions).Append(new(DataAction.ClearScanData, page.FactoryReset.Label, page.FactoryReset.AccessibleName, true, null)))
            StringAssert.StartsWith(action.AccessibleName, action.Label, "label-in-name");
    }

    [TestMethod]
    [Description("FR-DAT-07/10/11; SPEC §12.5: each card states size and last-cleaned time with DisplayFormat, and explicit Deletes and Keeps text; the provider-cache boundary is disclosed.")]
    public void DataResetCardsStateSizeLastCleanedAndScope()
    {
        DataResetPagePresentation page = DataResetPresenter.Build(Inputs(), Culture, Utc);

        DataCardPresentation scan = page.Cards[0];
        CollectionAssert.AreEqual(ScanFacts, scan.Facts.Select(fact => fact.Label).ToArray());
        Assert.AreEqual("About 2 MB", scan.Facts[0].Value);
        Assert.AreEqual(DisplayFormat.DateTime(Cleared, Culture, Utc), scan.Facts[2].Value);
        CollectionAssert.AreEqual(ScanFactValues[2..], scan.Facts.Skip(2).Select(fact => fact.Value).ToArray());
        StringAssert.Contains(scan.Deletes, "raw provider records");
        foreach (string kept in new[] { "Settings", "update history", "downloaded files", "mappings, pins and ignores" }) StringAssert.Contains(scan.Keeps, kept);

        DataCardPresentation downloads = page.Cards[1];
        CollectionAssert.AreEqual(DownloadFacts, downloads.Facts.Select(fact => fact.Label).ToArray());
        CollectionAssert.AreEqual(DownloadFactValues, downloads.Facts.Select(fact => fact.Value).ToArray());
        StringAssert.Contains(downloads.Keeps, "Windows, WinGet, Microsoft Store and other provider caches");
        StringAssert.Contains(page.ProviderCacheBoundary, "never deletes Windows, WinGet, Microsoft Store or other providers' caches");

        Assert.AreEqual(DataResetPresenter.NotMeasured, page.Cards[2].Facts[0].Value, "sizes PC Steward does not record are not invented");
        Assert.AreEqual("Cached — WinGet checked 2 hours ago.", page.Cards[2].Facts[1].Value);
        Assert.AreEqual("3 records", page.Cards[3].Facts[1].Value);
        Assert.AreEqual("500 or more", DataResetPresenter.Build(Inputs(records: 500), Culture, Utc).Cards[3].Facts[1].Value);
        Assert.AreEqual("Couldn't be read just now", DataResetPresenter.Build(Inputs(historyUnavailable: true), Culture, Utc).Cards[3].Facts[1].Value);
        Assert.AreEqual("Decisions", page.Cards[4].Facts[0].Label);
        Assert.AreEqual("2 active (expired ignores are also removed)", page.Cards[4].Facts[0].Value, "the count says the clear also removes expired ignores");
        StringAssert.Contains(page.Cards[4].Deletes, "including expired ignores");
        foreach (DataCardPresentation card in page.Cards)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.Deletes), card.Title);
            Assert.IsFalse(string.IsNullOrWhiteSpace(card.Keeps), card.Title);
        }
        StringAssert.Contains(page.FactoryReset.Keeps, "installed apps, provider caches and your exports");
        StringAssert.Contains(page.FactoryReset.Afterwards, "typing RESET");
        StringAssert.Contains(page.FactoryReset.Afterwards, "next time it starts");
        Assert.IsFalse(DataResetPresenter.Build(Inputs(active: 0), Culture, Utc).Cards[1].Facts.Any(fact => fact.Label == "In use"));
    }

    [TestMethod]
    [Description("FR-DAT-14; AC-UX-07/08: every data control, including factory reset, is unavailable during active work and says why.")]
    public void DataControlsAreDisabledDuringActiveWork()
    {
        DataResetPagePresentation busy = DataResetPresenter.Build(Inputs(busy: true), Culture, Utc);
        Assert.IsTrue(busy.HasActiveWork);
        foreach (DataActionPresentation action in busy.Cards.SelectMany(card => card.Actions))
        {
            Assert.IsFalse(action.IsEnabled, action.Label);
            Assert.AreEqual(DataResetPresenter.ActiveWorkReason, action.UnavailableReason, action.Label);
        }
        Assert.IsFalse(busy.FactoryReset.IsEnabled);
        Assert.AreEqual(DataResetPresenter.ActiveWorkReason, busy.FactoryReset.UnavailableReason);
        StringAssert.Contains(busy.Availability, "unavailable");

        DataResetPagePresentation idle = DataResetPresenter.Build(Inputs(), Culture, Utc);
        Assert.IsTrue(idle.Cards.SelectMany(card => card.Actions).All(action => action.IsEnabled && action.UnavailableReason is null));
        Assert.IsTrue(idle.FactoryReset.IsEnabled);

        // Unchanged rule: Clear downloaded files needs inactive files to clear.
        DataActionPresentation empty = DataResetPresenter.Build(Inputs(recoverable: 0), Culture, Utc).Cards[1].Actions.Single();
        Assert.IsFalse(empty.IsEnabled);
        StringAssert.Contains(empty.UnavailableReason, "Nothing to clear");
    }

    [TestMethod]
    [Description("FR-DAT-10: the Settings entry card and Storage page use the same human-readable sizes.")]
    public void StorageSummaryUsesHumanReadableSizes()
    {
        IReadOnlyList<DetailFact> summary = DataResetPresenter.StorageSummary(Inputs(), Culture);
        Assert.AreEqual("About 2 MB · 1,234 items", summary[0].Value);
        Assert.AreEqual("40 MB · 30 MB can be freed", summary[1].Value);
    }

    [TestMethod]
    [Description("Audit P-M7 (owner decision, independent review): one save model — settings apply immediately except the Schedule card, which saves with its one Save schedule button; Settings sections in one column in the planned order.")]
    public void SettingsUseOneSaveModelAndGroupedSections()
    {
        Assert.AreEqual(SettingsSaveModel.AppliesImmediatelyExceptSchedule, Enum.GetValues<SettingsSaveModel>().Single(), "there is exactly one save model");
        CollectionAssert.AreEqual(SettingsSections, SettingsPresenter.Sections.ToArray());
        CollectionAssert.AreEqual(Cadences, SettingsPresenter.CadenceOptions.ToArray());
        StringAssert.Contains(SettingsPresenter.SaveModelText, "save as soon as you make them, except the schedule");
        Assert.AreEqual("Save schedule", SettingsPresenter.SaveScheduleLabel);
        StringAssert.EndsWith(SettingsPresenter.ScheduleDescription, "Changes to the schedule apply when you select Save schedule.");
        Assert.AreEqual("Turning this on shows what is shared and asks you first.", SettingsPresenter.SourceConsentNote);
        Assert.AreEqual("Look for updates online", SettingsPresenter.SourceSettingLabel, "UI review item 8: the setting is named for what it does");
        Assert.AreEqual("Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.", SettingsPresenter.ExportWarning);
        Assert.AreEqual("Data & reset", ShellNavigation.TitleOf(ShellPage.Privacy), "the data subpage is named Data & reset (SPEC §17.3)");
        Assert.AreEqual(ShellPage.Settings, ShellNavigation.ParentOf(ShellPage.Privacy));
        // UI polish phase 4 (approved target T-Settings): off is a choice, stated calmly; on gives the source evidence in words.
        StringAssert.StartsWith(SettingsPresenter.SourceRowDescription(false, "ignored"), "Off. PC Steward only lists what is installed.");
        Assert.IsFalse(SettingsPresenter.SourceRowDescription(false, "ignored").Contains("ignored", StringComparison.Ordinal));
        Assert.AreEqual("On. Live results from your update sources.", SettingsPresenter.SourceRowDescription(true, "Live results from your update sources."));
        StringAssert.Contains(SettingsPresenter.PageDescription, "except the schedule");
    }

    [TestMethod]
    [Description("Audit P-M7; independent review fix 3; UI review item 16: the requested schedule is the normalized record; Off always means not scheduled and Daily or Weekly means scheduled; no cadence or weekday selection fails closed (nothing to save).")]
    public void RequestedScheduleIsTheSameNormalizedRecord()
    {
        TimeSpan nine = new(9, 30, 0);
        Assert.AreEqual(new ScheduledCheckSettings(ScheduleCadence.Manual, false, true, DayOfWeek.Monday, new TimeOnly(9, 30)), SettingsPresenter.RequestedSchedule(0, true, (int)DayOfWeek.Monday, nine));
        Assert.AreEqual(new ScheduledCheckSettings(ScheduleCadence.Daily, true, false, DayOfWeek.Friday, new TimeOnly(9, 30)), SettingsPresenter.RequestedSchedule(1, false, (int)DayOfWeek.Friday, nine));
        Assert.AreEqual(new ScheduledCheckSettings(ScheduleCadence.Weekly, true, false, DayOfWeek.Sunday, new TimeOnly(9, 30)), SettingsPresenter.RequestedSchedule(2, false, (int)DayOfWeek.Sunday, nine));
        Assert.AreEqual(DayOfWeek.Saturday, SettingsPresenter.RequestedSchedule(2, false, 6, nine)!.WeeklyDay, "the weekday index is the DayOfWeek value");
        foreach (ScheduleCadence cadence in Enum.GetValues<ScheduleCadence>())
            Assert.AreEqual(cadence, SettingsPresenter.CadenceAt(SettingsPresenter.CadenceIndex(cadence)));

        // Fail closed: a missing selection never becomes Off or Monday.
        Assert.IsNull(SettingsPresenter.RequestedSchedule(-1, true, (int)DayOfWeek.Monday, nine), "no cadence selection");
        Assert.IsNull(SettingsPresenter.RequestedSchedule(3, true, (int)DayOfWeek.Monday, nine), "out-of-range cadence");
        Assert.IsNull(SettingsPresenter.RequestedSchedule(2, true, -1, nine), "no weekday selection");
        Assert.IsNull(SettingsPresenter.RequestedSchedule(1, true, 7, nine), "out-of-range weekday");
        Assert.IsNull(SettingsPresenter.RequestedSchedule(1, true, 1, TimeSpan.FromHours(24)), "out-of-range time");
        Assert.AreEqual("Choose a schedule before saving.", SettingsPresenter.ScheduleNotChosen);
    }

    [TestMethod]
    [Description("UI review item 16 (FR-UI-01/05): the time and the notifications choice show for Daily and Weekly, the day only for Weekly, and none of them for Off.")]
    public void ScheduleControlsShownFollowTheChoice()
    {
        Assert.AreEqual((false, false, false), SettingsPresenter.ScheduleControlsShown(0));
        Assert.AreEqual((false, false, false), SettingsPresenter.ScheduleControlsShown(-1));
        Assert.AreEqual((true, false, true), SettingsPresenter.ScheduleControlsShown(1));
        Assert.AreEqual((true, true, true), SettingsPresenter.ScheduleControlsShown(2));
    }

    [TestMethod]
    [Description("UI review item 16 (FR-UI-01/05, FR-DAT-01): Save schedule is offered only when what the controls ask for would change what runs.")]
    public void SaveScheduleIsOfferedOnlyAfterAChange()
    {
        TimeSpan eight = new(8, 0, 0);
        ScheduledCheckSettings daily = new(ScheduleCadence.Daily, true, false, DayOfWeek.Monday, new TimeOnly(8, 0));
        ScheduledCheckSettings weekly = new(ScheduleCadence.Weekly, true, true, DayOfWeek.Thursday, new TimeOnly(8, 0));
        ScheduledCheckSettings paused = daily with { IsEnabled = false };

        // What is shown for the saved record asks for no change.
        foreach (ScheduledCheckSettings saved in new[] { ScheduledCheckSettings.Manual, daily, weekly, paused })
        {
            ScheduleCardPresentation shown = SettingsPresenter.Schedule(saved, ScheduledCheckStatus.NotScheduled, false, Culture, Utc);
            Assert.IsFalse(SettingsPresenter.HasScheduleChange(saved, SettingsPresenter.RequestedSchedule(shown.CadenceIndex, shown.NotificationsEnabled, (int)shown.WeeklyDay, shown.LocalStartTime.ToTimeSpan())), saved.ToString());
        }
        Assert.AreEqual(0, SettingsPresenter.ShownCadenceIndex(paused), "a check saved while paused reads as Off");

        Assert.IsTrue(SettingsPresenter.HasScheduleChange(ScheduledCheckSettings.Manual, SettingsPresenter.RequestedSchedule(1, false, 1, eight)), "turning it on");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(paused, SettingsPresenter.RequestedSchedule(1, false, 1, eight)), "turning a paused check on");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(daily, SettingsPresenter.RequestedSchedule(0, false, 1, eight)), "turning it off");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(daily, SettingsPresenter.RequestedSchedule(2, false, 1, eight)), "daily to weekly");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(daily, SettingsPresenter.RequestedSchedule(1, false, 1, new TimeSpan(8, 1, 0))), "a new time");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(daily, SettingsPresenter.RequestedSchedule(1, true, 1, eight)), "notifications");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(weekly, SettingsPresenter.RequestedSchedule(2, true, (int)DayOfWeek.Friday, eight)), "a new day for a weekly check");
        Assert.IsFalse(SettingsPresenter.HasScheduleChange(daily, SettingsPresenter.RequestedSchedule(1, false, (int)DayOfWeek.Friday, eight)), "the day does not count for a daily check");
        Assert.IsFalse(SettingsPresenter.HasScheduleChange(ScheduledCheckSettings.Manual, SettingsPresenter.RequestedSchedule(0, true, 3, new TimeSpan(7, 0, 0))), "two schedules that are off are the same");
        Assert.IsTrue(SettingsPresenter.HasScheduleChange(daily, null), "with nothing valid chosen the button is there to say what is missing");
        Assert.AreEqual("Not saved yet. Select Save schedule to apply this change.", SettingsPresenter.ScheduleUnsavedText);
        StringAssert.Contains(SettingsPresenter.ScheduleOutOfStepText, "Select Save schedule");
    }

    [TestMethod]
    [Description("Independent review fix 5 (FR-DAT-01): a Task Scheduler refusal is its own outcome; a failed settings save re-registers the previous schedule, and says so honestly if that also fails.")]
    public async Task ScheduleSaveDistinguishesSchedulerRefusalAndRestoresThePreviousTask()
    {
        ScheduledCheckSettings previous = new(ScheduleCadence.Daily, true, false, DayOfWeek.Monday, new TimeOnly(8, 0));
        ScheduledCheckSettings requested = new(ScheduleCadence.Weekly, true, false, DayOfWeek.Friday, new TimeOnly(18, 0));
        List<string> calls = [];
        Func<ScheduledCheckSettings, CancellationToken, Task> Register(params bool[] failures)
        {
            int call = 0;
            return (settings, _) =>
            {
                calls.Add($"register {settings.Cadence}");
                bool fail = call < failures.Length && failures[call];
                call++;
                return fail ? Task.FromException(new InvalidOperationException("Windows Task Scheduler rejected the current-user scan-only task.")) : Task.CompletedTask;
            };
        }
        Func<ScheduledCheckSettings, CancellationToken, Task> Save(bool fail) => (settings, _) =>
        {
            calls.Add($"save {settings.Cadence}");
            return fail ? Task.FromException(new IOException("database is locked")) : Task.CompletedTask;
        };

        Assert.AreEqual(ScheduleSaveOutcome.Saved, await ScheduleSaveOperation.SaveAsync(previous, requested, Register(), Save(false), CancellationToken.None));
        CollectionAssert.AreEqual(SavedCalls, calls);

        calls.Clear();
        Assert.AreEqual(ScheduleSaveOutcome.SchedulerRefused, await ScheduleSaveOperation.SaveAsync(previous, requested, Register(true), Save(false), CancellationToken.None));
        CollectionAssert.AreEqual(RefusedCalls, calls, "a refused registration saves nothing");

        calls.Clear();
        Assert.AreEqual(ScheduleSaveOutcome.SettingsNotSavedTaskRestored, await ScheduleSaveOperation.SaveAsync(previous, requested, Register(), Save(true), CancellationToken.None));
        CollectionAssert.AreEqual(RestoredCalls, calls, "the previous schedule is registered again");

        calls.Clear();
        Assert.AreEqual(ScheduleSaveOutcome.SettingsNotSavedTaskChanged, await ScheduleSaveOperation.SaveAsync(previous, requested, Register(false, true), Save(true), CancellationToken.None));

        Assert.IsNull(SettingsPresenter.ScheduleSaveResult(ScheduleSaveOutcome.Saved));
        Assert.AreEqual("The schedule was not changed: Windows Task Scheduler refused the change. Your saved settings were left unchanged.", SettingsPresenter.ScheduleSaveResult(ScheduleSaveOutcome.SchedulerRefused)!.Body);
        StringAssert.Contains(SettingsPresenter.ScheduleSaveResult(ScheduleSaveOutcome.SettingsNotSavedTaskRestored)!.Body, "put back as it was");
        ScheduleSaveMessage changed = SettingsPresenter.ScheduleSaveResult(ScheduleSaveOutcome.SettingsNotSavedTaskChanged)!;
        StringAssert.Contains(changed.Body, "The Windows scheduled task was changed");
        StringAssert.Contains(changed.Body, "Select Save schedule to try again");

        // Cancellation is not reported as a scheduler refusal.
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ScheduleSaveOperation.SaveAsync(previous, requested, (_, _) => Task.FromException(new OperationCanceledException()), Save(false), CancellationToken.None));
    }

    [TestMethod]
    [Description("Audit P-M7; UI review item 16: with Off the time, day and notifications are not shown; a check saved while paused reads as Off; while saving every schedule control is unavailable.")]
    public void ScheduleCardShowsOnlyTheControlsThatApply()
    {
        ScheduledCheckStatus status = new(Cleared, "Completed", 0);
        ScheduleCardPresentation manual = SettingsPresenter.Schedule(ScheduledCheckSettings.Manual, status, false, Culture, Utc);
        Assert.AreEqual(0, manual.CadenceIndex);
        Assert.IsFalse(manual.ShowsTime || manual.ShowsWeekday || manual.ShowsNotifications);
        Assert.IsFalse(manual.IsScheduled);
        Assert.IsTrue(manual.AreControlsEnabled);
        Assert.AreEqual("Off. PC Steward checks only when you start a scan.", manual.Summary);
        Assert.AreEqual(DisplayFormat.DateTime(Cleared, Culture, Utc), manual.Status[1].Value);

        // A stored manual cadence with the enable flag set still reads as not scheduled (Normalize).
        Assert.IsFalse(SettingsPresenter.Schedule(ScheduledCheckSettings.Manual with { IsEnabled = true }, status, false, Culture, Utc).IsScheduled);

        ScheduleCardPresentation daily = SettingsPresenter.Schedule(new(ScheduleCadence.Daily, true, true, DayOfWeek.Monday, new TimeOnly(8, 5)), status, false, Culture, Utc);
        Assert.AreEqual(1, daily.CadenceIndex);
        Assert.IsTrue(daily.ShowsTime && daily.ShowsNotifications && daily.IsScheduled);
        Assert.IsFalse(daily.ShowsWeekday);
        Assert.AreEqual("Checks this PC every day at 08:05.", daily.Summary);

        ScheduleCardPresentation weekly = SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, true, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, false, Culture, Utc);
        Assert.AreEqual(2, weekly.CadenceIndex);
        Assert.IsTrue(weekly.ShowsWeekday && weekly.ShowsTime);
        Assert.AreEqual("Checks this PC every Thursday at 18:00.", weekly.Summary);

        // A weekly check saved while paused reads as Off and keeps its day and time for when it is chosen again.
        ScheduleCardPresentation paused = SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, false, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, false, Culture, Utc);
        Assert.AreEqual(0, paused.CadenceIndex);
        Assert.IsFalse(paused.IsScheduled || paused.ShowsTime || paused.ShowsWeekday || paused.ShowsNotifications);
        StringAssert.StartsWith(paused.Summary, "Off.");
        Assert.AreEqual(DayOfWeek.Thursday, paused.WeeklyDay);
        Assert.AreEqual(new TimeOnly(18, 0), paused.LocalStartTime);

        ScheduleCardPresentation saving = SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, true, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, true, Culture, Utc);
        Assert.IsFalse(saving.AreControlsEnabled || saving.IsSaveEnabled);

        // Save schedule is available when idle and unavailable during other active work (independent review fix 6).
        Assert.IsTrue(weekly.IsSaveEnabled);
        ScheduleCardPresentation otherWork = SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, true, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, false, Culture, Utc, otherWorkActive: true);
        Assert.IsFalse(otherWork.IsSaveEnabled);
        Assert.AreEqual("Not yet", SettingsPresenter.Schedule(ScheduledCheckSettings.Manual, ScheduledCheckStatus.NotScheduled, false, Culture, Utc).Status[1].Value);
    }

    [TestMethod]
    [Description("FR-HIS-05: Copy diagnostic copies the existing sanitized bundle; the confirmation counts what the bundle holds.")]
    public void CopyDiagnosticUsesTheSanitizedBundle()
    {
        DiagnosticCopyPresentation available = SettingsPresenter.DiagnosticCopy(historyUnavailable: false);
        Assert.AreEqual("Copy diagnostic", available.Label);
        // WCAG 2.5.3 (label in name): speech users say the visible words, so the accessible name starts with them.
        StringAssert.StartsWith(available.AccessibleName, available.Label);
        Assert.IsTrue(available.IsEnabled);
        StringAssert.Contains(available.Description, "Paths, passwords and tokens are removed");
        Assert.IsFalse(SettingsPresenter.DiagnosticCopy(historyUnavailable: true).IsEnabled);

        UpdateHistoryEntry[] entries = Enumerable.Range(0, 300).Select(index => new UpdateHistoryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "key", UpdateProvider.WinGet, "Vendor.App", "1", "2",
            Cleared.AddMinutes(index), UpdateHistoryStatus.ProductFailed, 1, @"token=secret C:\Users\Alice\setup.exe")).ToArray();
        string bundle = SanitizedDiagnosticBundle.Create(entries);
        using JsonDocument document = JsonDocument.Parse(bundle);
        Assert.AreEqual(SettingsPresenter.DiagnosticBundleMaximumEntries, document.RootElement.GetProperty("entries").GetArrayLength(), "the confirmation's cap matches the bundle");
        Assert.IsFalse(bundle.Contains("secret", StringComparison.Ordinal));
        Assert.IsFalse(bundle.Contains("Alice", StringComparison.Ordinal));
        StringAssert.Contains(SettingsPresenter.DiagnosticCopied(300), "256 update records");
        StringAssert.Contains(SettingsPresenter.DiagnosticCopied(1), "1 update record)");
        StringAssert.Contains(SettingsPresenter.DiagnosticCopied(0), "no update records");
    }

    [TestMethod]
    [Description("Audit P-L5; independent review fix 2 (FR-HIS-05): startup notices keep a sanitized diagnostic; user names and paths are redacted before display and copy.")]
    public void StartupNoticesSanitizeTheirDiagnostic()
    {
        const string message = @"SQLite Error 14: unable to open C:\Users\Alice\AppData\Local\PCSteward\pcsteward.db token=abc123";
        string failure = StartupNoticeText.ExceptionDetails("SqliteException", message, userProfile: @"C:\Users\Alice");
        Assert.IsFalse(failure.Contains("Alice", StringComparison.OrdinalIgnoreCase), failure);
        Assert.IsFalse(failure.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase), failure);
        Assert.IsFalse(failure.Contains("abc123", StringComparison.Ordinal), failure);
        StringAssert.StartsWith(failure, "SqliteException: SQLite Error 14: unable to open ");
        // Without the profile hint the shared sanitizer still removes the path.
        Assert.IsFalse(StartupNoticeText.ExceptionDetails("SqliteException", message, userProfile: "").Contains("Alice", StringComparison.Ordinal));
        // A profile name with a space is redacted too.
        Assert.IsFalse(StartupNoticeText.ExceptionDetails("IOException", @"Locked: C:\Users\Alice Smith\AppData\Local\PCSteward\logs", @"C:\Users\Alice Smith").Contains("Smith", StringComparison.Ordinal));

        string residual = StartupNoticeText.ResidualDetails([("Logs", "Locked", @"C:\Users\Alice\AppData\Local\PCSteward\pcsteward.db is in use.")], userProfile: @"C:\Users\Alice");
        Assert.IsFalse(residual.Contains("Alice", StringComparison.OrdinalIgnoreCase), residual);
        StringAssert.StartsWith(residual, "Logs: Locked — ");

        Assert.AreEqual("IOException: The file is locked.", StartupNoticeText.ExceptionDetails("IOException", "The file is locked."));
        // Final review L4: dialog technical details (for example "Factory reset was not started") use the same redaction.
        string dialogDetail = StartupNoticeText.SanitizedDetails(@"Access denied: C:\Users\Alice Smith\AppData\Local\PCSteward\reset.marker password=hunter2", @"C:\Users\Alice Smith");
        Assert.IsFalse(dialogDetail.Contains("Smith", StringComparison.Ordinal), dialogDetail);
        Assert.IsFalse(dialogDetail.Contains(@"C:\Users", StringComparison.OrdinalIgnoreCase), dialogDetail);
        Assert.IsFalse(dialogDetail.Contains("hunter2", StringComparison.Ordinal), dialogDetail);
        StringAssert.StartsWith(dialogDetail, "Access denied: ");
        Assert.AreEqual("The file is locked.", StartupNoticeText.SanitizedDetails("The file is locked."));
        Assert.AreEqual(string.Empty, StartupNoticeText.SanitizedDetails(null));
        Assert.AreEqual("Reset marker: InvalidMarker — The reset control state is invalid.\nLogs: Locked",
            StartupNoticeText.ResidualDetails([("Reset marker", "InvalidMarker", "The reset control state is invalid."), ("Logs", "Locked", "")]));
        Assert.AreEqual("Copy details", StartupNoticeText.CopyDetailsLabel);
        Assert.AreEqual("Close", StartupNoticeText.CloseLabel);
    }
}
