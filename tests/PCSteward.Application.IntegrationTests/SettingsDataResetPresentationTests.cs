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
    private static readonly string[] SettingsSections = ["Update sources", "Scan scope", "Schedule", "Decisions", "Privacy & exports", "Data & reset", "About"];
    private static readonly string[] Cadences = ["Manual only", "Daily", "Weekly"];
    private static readonly string[] CardTitles = ["Scan data", "Downloaded update files", "Source metadata", "Logs and history", "Mappings, pins and ignores"];
    private static readonly string[] ActionLabels = ["Clear scan data", "Clear downloaded files", "Clear source metadata", "Clear logs", "Clear update history", "Clear mappings and policies"];
    private static readonly string[] ScanFacts = ["Size", "Items", "Last cleared"];
    private static readonly string[] ScanFactValues = ["About 2 MB", "1,234 items", "30 Oct 2026 · 09:15"];
    private static readonly string[] DownloadFacts = ["Size", "Can be freed", "In use", "Last cleaned"];
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
        Assert.AreEqual("2 decisions", page.Cards[4].Facts[0].Value);
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
    [Description("Audit P-M7: one save model (applies immediately); Settings sections in one column in the planned order.")]
    public void SettingsUseOneSaveModelAndGroupedSections()
    {
        Assert.AreEqual(SettingsSaveModel.AppliesImmediately, Enum.GetValues<SettingsSaveModel>().Single(), "there is exactly one save model");
        CollectionAssert.AreEqual(SettingsSections, SettingsPresenter.Sections.ToArray());
        CollectionAssert.AreEqual(Cadences, SettingsPresenter.CadenceOptions.ToArray());
        StringAssert.Contains(SettingsPresenter.SaveModelText, "save as soon as you make them");
        StringAssert.Contains(SettingsPresenter.SourceConsentNote, "never starts a scan");
        Assert.AreEqual("Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.", SettingsPresenter.ExportWarning);
    }

    [TestMethod]
    [Description("Audit P-M7: the requested schedule is the same normalized record the old Save built; Manual only always means not scheduled.")]
    public void RequestedScheduleIsTheSameNormalizedRecord()
    {
        TimeSpan nine = new(9, 30, 0);
        Assert.AreEqual(new ScheduledCheckSettings(ScheduleCadence.Manual, false, true, DayOfWeek.Monday, new TimeOnly(9, 30)), SettingsPresenter.RequestedSchedule(0, true, true, null, nine));
        Assert.AreEqual(new ScheduledCheckSettings(ScheduleCadence.Daily, true, false, DayOfWeek.Friday, new TimeOnly(9, 30)), SettingsPresenter.RequestedSchedule(1, true, false, DayOfWeek.Friday, nine));
        Assert.AreEqual(new ScheduledCheckSettings(ScheduleCadence.Weekly, false, false, DayOfWeek.Sunday, new TimeOnly(9, 30)), SettingsPresenter.RequestedSchedule(2, false, false, DayOfWeek.Sunday, nine));
        foreach (ScheduleCadence cadence in Enum.GetValues<ScheduleCadence>())
            Assert.AreEqual(cadence, SettingsPresenter.CadenceAt(SettingsPresenter.CadenceIndex(cadence)));
    }

    [TestMethod]
    [Description("Audit P-M7: with Manual only the time, weekday and enable controls are unavailable; while saving every schedule control is unavailable.")]
    public void ScheduleCardDisablesControlsThatDoNotApply()
    {
        ScheduledCheckStatus status = new(Cleared, "Completed", 0);
        ScheduleCardPresentation manual = SettingsPresenter.Schedule(ScheduledCheckSettings.Manual, status, false, Culture, Utc);
        Assert.AreEqual(0, manual.CadenceIndex);
        Assert.IsFalse(manual.IsTimeEnabled);
        Assert.IsFalse(manual.IsWeekdayEnabled);
        Assert.IsFalse(manual.IsEnableToggleEnabled);
        Assert.IsFalse(manual.IsEnabled);
        Assert.IsTrue(manual.IsCadenceEnabled);
        StringAssert.Contains(manual.Summary, "Not scheduled");
        Assert.AreEqual(DisplayFormat.DateTime(Cleared, Culture, Utc), manual.Status[1].Value);

        // A stored manual cadence with the enable flag set still reads as not scheduled (Normalize).
        Assert.IsFalse(SettingsPresenter.Schedule(ScheduledCheckSettings.Manual with { IsEnabled = true }, status, false, Culture, Utc).IsEnabled);

        ScheduleCardPresentation daily = SettingsPresenter.Schedule(new(ScheduleCadence.Daily, true, true, DayOfWeek.Monday, new TimeOnly(8, 5)), status, false, Culture, Utc);
        Assert.IsTrue(daily.IsTimeEnabled && daily.IsEnableToggleEnabled);
        Assert.IsFalse(daily.IsWeekdayEnabled);
        Assert.AreEqual("Checks this PC every day at 08:05.", daily.Summary);

        ScheduleCardPresentation weekly = SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, true, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, false, Culture, Utc);
        Assert.IsTrue(weekly.IsWeekdayEnabled);
        Assert.AreEqual("Checks this PC every Thursday at 18:00.", weekly.Summary);
        StringAssert.Contains(SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, false, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, false, Culture, Utc).Summary, "Paused");

        ScheduleCardPresentation saving = SettingsPresenter.Schedule(new(ScheduleCadence.Weekly, true, false, DayOfWeek.Thursday, new TimeOnly(18, 0)), status, true, Culture, Utc);
        Assert.IsFalse(saving.IsCadenceEnabled || saving.IsTimeEnabled || saving.IsWeekdayEnabled || saving.IsEnableToggleEnabled || saving.IsNotificationsToggleEnabled);
        Assert.AreEqual("Not yet", SettingsPresenter.Schedule(ScheduledCheckSettings.Manual, ScheduledCheckStatus.NotScheduled, false, Culture, Utc).Status[1].Value);
    }

    [TestMethod]
    [Description("FR-HIS-05: Copy diagnostic copies the existing sanitized bundle; the confirmation counts what the bundle holds.")]
    public void CopyDiagnosticUsesTheSanitizedBundle()
    {
        DiagnosticCopyPresentation available = SettingsPresenter.DiagnosticCopy(historyUnavailable: false);
        Assert.AreEqual("Copy diagnostic", available.Label);
        StringAssert.StartsWith(available.AccessibleName, "Copy");
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
    [Description("Audit P-L5: startup notices keep the exception diagnostic and list reset residuals in words.")]
    public void StartupNoticesKeepTheirDiagnostic()
    {
        Assert.AreEqual("IOException: The file is locked.", StartupNoticeText.ExceptionDetails("IOException", "The file is locked."));
        Assert.AreEqual("Reset marker: InvalidMarker — The reset control state is invalid.\nLogs: Locked",
            StartupNoticeText.ResidualDetails([("Reset marker", "InvalidMarker", "The reset control state is invalid."), ("Logs", "Locked", "")]));
        Assert.AreEqual("Copy details", StartupNoticeText.CopyDetailsLabel);
        Assert.AreEqual("Close", StartupNoticeText.CloseLabel);
    }
}
