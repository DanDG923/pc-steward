using System.Globalization;
using System.Text.Json;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-DIA-01–05, FR-UI-01 (Task 64 stage 4, ADR-027): what the Diagnostics page says and what the export file holds, with a fixed
/// clock, culture and UTC zone. The page never claims a cause or a clean result, lists every source, and exports exactly the
/// projected records it shows.
/// </summary>
[TestClass]
public sealed class DiagnosticsPresentationTests
{
    private static readonly DateTimeOffset Collected = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly string[] ExpectedDays = ["Yesterday", "10 Nov 2026"];
    private static readonly string[] ExpectedSourceOrder = ["PcStewardHistory", "ApplicationEventLog"];
    private static readonly string[] ExpectedExportProperties = ["format", "formatVersion", "policyVersion", "collectedAtUtc", "lookbackFromUtc", "expiresAtUtc", "sources", "records"];

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void TheConsentOffersEverySourceInCollectionOrderAndSaysWhatItReads()
    {
        CollectionAssert.AreEqual(Enum.GetValues<DiagnosticSourceKind>(), DiagnosticsPresenter.SourceChoices.Select(choice => choice.Source).ToArray());
        Assert.IsTrue(DiagnosticsPresenter.SourceChoices.All(choice => !string.IsNullOrWhiteSpace(choice.Label) && !string.IsNullOrWhiteSpace(choice.Reads)));
        StringAssert.Contains(DiagnosticsPresenter.ConsentIntro, "last 7 days");
        StringAssert.Contains(DiagnosticsPresenter.ConsentIntro, "without administrator permission");
        StringAssert.Contains(DiagnosticsPresenter.ConsentNotRead, "never reads message text");
        StringAssert.Contains(DiagnosticsPresenter.ConsentKeep, "30 days");
        StringAssert.Contains(DiagnosticsPresenter.ConsentKeep, "Nothing is sent anywhere");
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void EverySourceResultHasWordsAndPartialSaysWhy()
    {
        foreach (DiagnosticSourceOutcome outcome in Enum.GetValues<DiagnosticSourceOutcome>())
            Assert.IsFalse(string.IsNullOrWhiteSpace(DiagnosticsPresenter.Coverage(Source(DiagnosticSourceKind.SystemEventLog, outcome)).Result), outcome.ToString());
        DiagnosticsCoverageLine limit = DiagnosticsPresenter.Coverage(Source(DiagnosticSourceKind.ApplicationEventLog, DiagnosticSourceOutcome.Partial, DiagnosticSourceReason.SourceRecordLimit, kept: 375, allowed: 375));
        Assert.AreEqual("375 records, not all of them: only the newest 375 are kept", limit.Result);
        Assert.AreEqual(StatusTone.Attention, limit.Tone);
        DiagnosticsCoverageLine rejected = DiagnosticsPresenter.Coverage(Source(DiagnosticSourceKind.PcStewardHistory, DiagnosticSourceOutcome.Partial, DiagnosticSourceReason.InvalidRecordsRejected, kept: 2, rejected: 1));
        StringAssert.Contains(rejected.Result, "1 record couldn't be read safely");
        Assert.AreEqual("Not read: you left it out", DiagnosticsPresenter.Coverage(Source(DiagnosticSourceKind.Reliability, DiagnosticSourceOutcome.NotSelected, DiagnosticSourceReason.NotSelectedByOwner)).Result);
        Assert.AreEqual("Not read: Windows did not allow it for this account", DiagnosticsPresenter.Coverage(Source(DiagnosticSourceKind.Reliability, DiagnosticSourceOutcome.Denied, DiagnosticSourceReason.AccessDenied)).Result);
        foreach (DiagnosticEventKind kind in Enum.GetValues<DiagnosticEventKind>()) Assert.IsFalse(string.IsNullOrWhiteSpace(DiagnosticsPresenter.KindLabel(kind)));
        foreach (DiagnosticOutcomeKind outcome in Enum.GetValues<DiagnosticOutcomeKind>()) _ = DiagnosticsPresenter.OutcomeLabel(outcome);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void NoReadableSourceIsNeverAcleanResult()
    {
        DiagnosticSnapshot snapshot = Snapshot([], [Source(DiagnosticSourceKind.PcStewardHistory, DiagnosticSourceOutcome.Failed, DiagnosticSourceReason.ReaderFault), Source(DiagnosticSourceKind.SystemEventLog, DiagnosticSourceOutcome.Denied, DiagnosticSourceReason.AccessDenied)]);
        DiagnosticsTimelinePresentation timeline = DiagnosticsPresenter.Timeline(snapshot, Collected, Culture, Zone);
        Assert.AreEqual(DiagnosticsPresenter.NoCoverageWarning, timeline.CoverageWarning);
        Assert.AreEqual(0, timeline.RecordCount);
        StringAssert.Contains(timeline.Summary, "from 0 of 2 sources");
        DiagnosticsSnapshotRow row = DiagnosticsPresenter.SnapshotRow(new(snapshot.Id, Collected, snapshot.ExpiresAtUtc, 0, 0, snapshot.Sources), Collected, Culture, Zone);
        StringAssert.Contains(row.Summary, "no source could be read");
        Assert.IsFalse(row.HasCoverage);
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    public void TheTimelineIsNewestFirstByDayAndLabelsNearbyChangesWithoutCause()
    {
        DateTimeOffset crash = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        DiagnosticRecord update = new(DiagnosticSourceKind.PcStewardHistory, "7", false, Known(crash.AddMinutes(-4)), DiagnosticEventKind.PcStewardUpdate, DiagnosticOutcomeKind.Succeeded, null, 0, "Contoso Widget", "1.0 to 1.1", null, false);
        DiagnosticRecord shutdown = new(DiagnosticSourceKind.SystemEventLog, "System:41", false, Known(crash), DiagnosticEventKind.UnexpectedShutdown, DiagnosticOutcomeKind.NotApplicable, 41, 0x9F, null, null, null, false);
        DiagnosticRecord older = new(DiagnosticSourceKind.ApplicationEventLog, "Application:5", false, Known(crash.AddDays(-2)), DiagnosticEventKind.ApplicationCrash, DiagnosticOutcomeKind.NotApplicable, 1000, null, null, null, null, false);
        DiagnosticRecord untimed = new(DiagnosticSourceKind.Reliability, "Reliability:9", false, DiagnosticOccurrence.Unknown, DiagnosticEventKind.ApplicationHang, DiagnosticOutcomeKind.NotApplicable, 1002, null, "Fabrikam Viewer", null, null, false);
        DiagnosticSnapshot snapshot = Snapshot([older, update, shutdown, untimed], [Source(DiagnosticSourceKind.PcStewardHistory, DiagnosticSourceOutcome.Available, kept: 1)]);

        DiagnosticsTimelinePresentation timeline = DiagnosticsPresenter.Timeline(snapshot, Collected, Culture, Zone);

        CollectionAssert.AreEqual(ExpectedDays, timeline.Days.Select(day => day.Heading).ToArray());
        DiagnosticsEntry first = timeline.Days[0].Entries[0];
        Assert.AreEqual("PC didn't shut down cleanly", first.Heading);
        Assert.AreEqual("09:00", first.TimeText);
        Assert.IsTrue(first.Facts.Any(fact => fact.Label == "Code" && fact.Value == "0x0000009F"));
        CollectionAssert.Contains(first.Notes.ToArray(), DiagnosticTimelineBuilder.UnexpectedShutdownNote);
        CollectionAssert.Contains(first.Notes.ToArray(), DiagnosticTimelineBuilder.ProximityNote);
        Assert.HasCount(1, first.Nearby);
        Assert.AreEqual("PC Steward updated Contoso Widget 1.0 to 1.1 (succeeded), 4 minutes before", first.Nearby[0].Text);

        DiagnosticsEntry change = timeline.Days[0].Entries[1];
        Assert.AreEqual("PC Steward update: Contoso Widget", change.Heading);
        Assert.IsTrue(change.Facts.Any(fact => fact.Label == "Exit code" && fact.Value == "0"));
        Assert.HasCount(0, change.Nearby, "a PC Steward change is not labelled near itself");

        Assert.HasCount(1, timeline.Untimed);
        Assert.AreEqual("App stopped responding: Fabrikam Viewer", timeline.Untimed[0].Heading);
        Assert.AreEqual("Time unknown", timeline.Untimed[0].TimeText);
        Assert.AreEqual(4, timeline.RecordCount);
        foreach (DiagnosticsEntry entry in timeline.Days.SelectMany(day => day.Entries).Concat(timeline.Untimed))
            foreach (string text in new[] { entry.Heading, entry.Summary }.Concat(entry.Nearby.Select(line => line.Text)))
                Assert.IsFalse(text.Contains("caused", StringComparison.OrdinalIgnoreCase) || text.Contains("because", StringComparison.OrdinalIgnoreCase), text);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public void SavingDisclosesTimelinesRemovedEarlyAndFailures()
    {
        Assert.IsNull(DiagnosticsPresenter.SaveNote(new(true, DiagnosticSnapshotSaveFailure.None, 3, 0)), "expired removals are expected and not reported");
        Assert.AreEqual("1 older timeline was removed to make room, before its 30 days were up.", DiagnosticsPresenter.SaveNote(new(true, DiagnosticSnapshotSaveFailure.None, 0, 1)));
        Assert.AreEqual("2 older timelines were removed to make room, before their 30 days were up.", DiagnosticsPresenter.SaveNote(new(true, DiagnosticSnapshotSaveFailure.None, 0, 2)));
        Assert.AreEqual(DiagnosticsPresenter.CapacityHeldText, DiagnosticsPresenter.SaveNote(new(false, DiagnosticSnapshotSaveFailure.CapacityHeldByProtectedSnapshots, 0, 0)));
        Assert.AreEqual(DiagnosticsPresenter.SaveFailedText, DiagnosticsPresenter.SaveNote(new(false, DiagnosticSnapshotSaveFailure.StorageFailed, 0, 0)));
    }

    [TestMethod]
    [TestCategory("FR-DIA-05")]
    public void TheExportIsTheSameProjectionThePageShowsWithEverySource()
    {
        DiagnosticRecord crash = new(DiagnosticSourceKind.ApplicationEventLog, "Application:5", false, Known(Collected.AddHours(-3)), DiagnosticEventKind.ApplicationCrash, DiagnosticOutcomeKind.NotApplicable, 1000, null, null, null, null, false);
        DiagnosticSnapshot snapshot = Snapshot([crash],
        [
            Source(DiagnosticSourceKind.ApplicationEventLog, DiagnosticSourceOutcome.Available, kept: 1),
            Source(DiagnosticSourceKind.PcStewardHistory, DiagnosticSourceOutcome.Empty)
        ]);

        byte[] file = DiagnosticSnapshotExport.Serialize(snapshot);
        using JsonDocument document = JsonDocument.Parse(file);
        JsonElement root = document.RootElement;
        Assert.AreEqual(DiagnosticSnapshotExport.Format, root.GetProperty("format").GetString());
        Assert.AreEqual(1, root.GetProperty("formatVersion").GetInt32());
        Assert.AreEqual("2026-11-13T12:00:00.0000000Z", root.GetProperty("collectedAtUtc").GetString());
        CollectionAssert.AreEqual(ExpectedSourceOrder, root.GetProperty("sources").EnumerateArray().Select(source => source.GetProperty("source").GetString()).ToArray(), "sources in collection order");
        JsonElement record = root.GetProperty("records").EnumerateArray().Single();
        Assert.AreEqual(DiagnosticRecordJson.Serialize(crash), record.GetRawText(), "each record is the stored canonical projection");
        Assert.IsTrue(DiagnosticRecordJson.TryParse(record.GetRawText(), out DiagnosticRecord? parsed));
        Assert.AreEqual(crash, parsed);
        string[] properties = [.. root.EnumerateObject().Select(property => property.Name)];
        CollectionAssert.AreEqual(ExpectedExportProperties, properties, "no other field, path or name is written");

        DiagnosticsExportPreview preview = DiagnosticsPresenter.ExportPreview(snapshot, Culture, Zone);
        Assert.IsTrue(preview.CanExport);
        Assert.AreEqual("pc-steward-diagnostics-2026-11-13.json", preview.SuggestedFileName);
        StringAssert.StartsWith(preview.Lines[0], "1 record, exactly as shown on this page");
        Assert.IsTrue(preview.Lines.Any(line => line.StartsWith("PC Steward update history: Nothing recorded", StringComparison.Ordinal)));
        StringAssert.Contains(preview.Warning, "check the file before you share it");
    }

    [TestMethod]
    [TestCategory("FR-UI-01")]
    public void DiagnosticsIsANavigationItemAfterActivity()
    {
        ShellPage[] pages = [.. ShellNavigation.Items.Select(item => item.Page)];
        Assert.AreEqual(Array.IndexOf(pages, ShellPage.Activity) + 1, Array.IndexOf(pages, ShellPage.Diagnostics));
        Assert.AreEqual("Diagnostics", ShellNavigation.TitleOf(ShellPage.Diagnostics));
        Assert.AreEqual(ShellPage.Diagnostics, ShellNavigation.ParentOf(ShellPage.Diagnostics));
        Assert.IsFalse(ShellNavigation.IsTransient(ShellPage.Diagnostics));
    }

    [TestMethod]
    [TestCategory("FR-DIA-02")]
    [DataRow(0x9FL, "0x0000009F")]
    [DataRow(-2145124329L, "0x80240017")]
    [DataRow(0xC0000005L, "0xC0000005")]
    public void ResultCodesReadAsWindowsShowsThem(long code, string expected) => Assert.AreEqual(expected, DiagnosticsPresenter.ResultCodeText(code));

    private static DiagnosticOccurrence Known(DateTimeOffset at) => new(at.ToUniversalTime(), TimeSpan.Zero, DiagnosticTimeQuality.Known);

    private static DiagnosticSourceResult Source(DiagnosticSourceKind source, DiagnosticSourceOutcome outcome, DiagnosticSourceReason reason = DiagnosticSourceReason.None, int kept = 0, int allowed = 500, int rejected = 0) =>
        new(source, outcome, reason, Collected.AddDays(-7), Collected, allowed, kept + rejected, kept, rejected);

    private static DiagnosticSnapshot Snapshot(DiagnosticRecord[] records, DiagnosticSourceResult[] sources) =>
        new(Guid.Parse("4b8f4a2e-1d2c-4c55-9a51-6f0d3c7a9e10"), DiagnosticCollectionPolicy.Version, Collected, Collected.AddDays(30), Collected.AddDays(-7), sources, records, records.Sum(record => (long)DiagnosticRecordJson.ByteCount(record)));
}
