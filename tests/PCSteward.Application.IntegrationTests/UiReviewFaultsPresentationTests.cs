using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, the four faults seen on the owner's 4K display at 150% scaling (AC-UX-03, FR-UI-01, FR-HIS-01): action
/// rows that stacked and clipped, a list stuck at five rows, package IDs shown as names, and scans burying updates on Activity.
/// Portable and deterministic; nothing here grants authority.
/// </summary>
[TestClass]
public sealed class UiReviewFaultsPresentationTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
    private static readonly int[] OneLine = [0, 0];
    private static readonly int[] TwoLines = [0, 0, 1];
    private static readonly int[] OneThenTwo = [0, 1, 1];
    private static readonly int[] RecordCounts = [32, 4, 28, 1];

    [TestMethod]
    [Description("AC-UX-03 (fault 2): a row that fits on one line stays on one line when sizes are fractions, as they are at 150% scaling.")]
    public void ARowThatFitsIsNeverSplitByRounding()
    {
        // The layout hands sizes over as single-precision numbers on a fractional pixel grid (two thirds of a unit at 150%).
        // Find a pair whose exact sum is a hair more than the single-precision width the row is handed back: the old panel
        // compared again while arranging and moved the second button to a line nobody had measured.
        int hazards = 0;
        for (int first = 90; first < 400; first++)
        {
            for (int second = 60; second < 260; second += 7)
            {
                double a = (float)(first * 2 / 3.0), b = (float)(second * 2 / 3.0);
                WrapLayout.Item[] items = [new(a, 32), new(b, 32)];
                WrapLayout.Measured measured = WrapLayout.Measure(items, double.PositiveInfinity, 8, 8);
                double handedBack = (float)measured.Width;
                if (a + 8 + b > handedBack) hazards++;

                CollectionAssert.AreEqual(OneLine, measured.Lines.ToArray());
                Assert.AreEqual(32, measured.Height);
                IReadOnlyList<WrapLayout.Slot> slots = WrapLayout.Arrange(items, measured.Lines, handedBack, 8, 8);
                Assert.AreEqual(0, slots[1].Y, $"{a} + 8 + {b} arranged in {handedBack} stays on the first line");
                Assert.AreEqual(a + 8, slots[1].X, 0.0001);

                // A second measure at the width the row asked for (a grid does this for an auto-sized column) fits too.
                CollectionAssert.AreEqual(OneLine, WrapLayout.Measure(items, handedBack, 8, 8).Lines.ToArray());
            }
        }
        Assert.IsTrue(hazards > 0, "the search covers the rounding case that stacked the buttons");
    }

    [TestMethod]
    [Description("AC-UX-01 (owner report 2026-11-13, release 0.1.14): on Home, \"Check for updates\" has an icon and is a little taller than \"Update all N…\" beside it, so it sat lower. In a row of controls every item on a line is given the line's height, so buttons on one line are the same height with level edges; a row of text and chips keeps each item's own height.")]
    public void ItemsOnOneLineAreLevel()
    {
        // "Update all 4…" (text only) and "Check for updates" (icon and text) on one line.
        WrapLayout.Item[] items = [new(120, 32), new(160, 34)];
        WrapLayout.Measured measured = WrapLayout.Measure(items, 600, 8, 8);
        Assert.AreEqual(34, measured.Height);
        IReadOnlyList<WrapLayout.Slot> slots = WrapLayout.Arrange(items, measured.Lines, 600, 8, 8, levelLines: true);
        Assert.AreEqual(new WrapLayout.Slot(0, 0, 120, 34), slots[0]);
        Assert.AreEqual(new WrapLayout.Slot(128, 0, 160, 34), slots[1]);

        // The taller item first, and a row of three: still one height, whichever is tallest.
        IReadOnlyList<WrapLayout.Slot> three = WrapLayout.Arrange([new(50, 34), new(50, 32), new(50, 30)], [0, 0, 0], 600, 8, 8, levelLines: true);
        Assert.IsTrue(three.All(slot => slot.Y == 0 && slot.Height == 34));

        // When the row wraps, each line has its own height and never borrows the other's.
        WrapLayout.Item[] narrow = [new(120, 32), new(160, 34), new(90, 20)];
        WrapLayout.Measured two = WrapLayout.Measure(narrow, 280, 8, 8);
        CollectionAssert.AreEqual(OneThenTwo, two.Lines.ToArray());
        IReadOnlyList<WrapLayout.Slot> wrapped = WrapLayout.Arrange(narrow, two.Lines, 280, 8, 8, levelLines: true);
        Assert.AreEqual(new WrapLayout.Slot(0, 0, 120, 32), wrapped[0]);
        Assert.AreEqual(new WrapLayout.Slot(0, 40, 160, 34), wrapped[1]);
        Assert.AreEqual(new WrapLayout.Slot(168, 40, 90, 34), wrapped[2]);

        // The row's own height is unchanged: it was always the sum of its lines' tallest items.
        Assert.AreEqual(32 + 8 + 34, two.Height);

        // A row of text and chips does not ask for it: each item keeps its own height at the top of its line.
        IReadOnlyList<WrapLayout.Slot> plain = WrapLayout.Arrange(items, measured.Lines, 600, 8, 8);
        Assert.AreEqual(new WrapLayout.Slot(0, 0, 120, 32), plain[0]);
        Assert.AreEqual(new WrapLayout.Slot(128, 0, 160, 34), plain[1]);
    }

    [TestMethod]
    [Description("AC-UX-04 (audit P-M9): a row that is too wide still wraps, and each line is placed under the one before.")]
    public void ARowThatIsTooWideStillWraps()
    {
        WrapLayout.Item[] items = [new(100, 32), new(100, 40), new(100, 32)];
        WrapLayout.Measured measured = WrapLayout.Measure(items, 220, 8, 6);
        CollectionAssert.AreEqual(TwoLines, measured.Lines.ToArray());
        Assert.AreEqual(208, measured.Width);
        Assert.AreEqual(40 + 6 + 32, measured.Height);
        IReadOnlyList<WrapLayout.Slot> slots = WrapLayout.Arrange(items, measured.Lines, 220, 8, 6);
        Assert.AreEqual(new WrapLayout.Slot(0, 0, 100, 32), slots[0]);
        Assert.AreEqual(new WrapLayout.Slot(108, 0, 100, 40), slots[1]);
        Assert.AreEqual(new WrapLayout.Slot(0, 46, 100, 32), slots[2]);

        // One item wider than the row is capped to the row, on its own line.
        WrapLayout.Measured wide = WrapLayout.Measure([new(50, 20), new(500, 20)], 200, 8, 8);
        Assert.AreEqual(200, wide.Width);
        Assert.AreEqual(200, WrapLayout.Arrange([new(50, 20), new(500, 20)], wide.Lines, 200, 8, 8)[1].Width);
        Assert.AreEqual(0, WrapLayout.Measure([], 200, 8, 8).Height);
        Assert.ThrowsExactly<ArgumentException>(() => WrapLayout.Arrange(items, [0], 220, 8, 6));
    }

    [TestMethod]
    [Description("AC-UX-03 (fault 1): the list grows to the bottom of the window from the content around it, never below its minimum.")]
    public void TheListFillsTheWindowBelowItsContent()
    {
        // A maximised 4K window at 150%: about 1,390 units of page, 250 above the list, a footnote of 40 below, 8 of card chrome.
        Assert.AreEqual(1092, ViewportFill.ListHeight(1390, 250, 40, 8, 320));
        Assert.AreEqual(320, ViewportFill.ListHeight(500, 250, 40, 8, 320), "a short window keeps the minimum and the page scrolls");
        Assert.AreEqual(1132, ViewportFill.ListHeight(1390.6, 250, -3, 8, 320), "a negative measurement is treated as nothing below");
        Assert.AreEqual(1100, ViewportFill.CardHeight(1390, 250, 40));
        Assert.AreEqual(0, ViewportFill.CardHeight(200, 250, 40));
    }

    [TestMethod]
    [Description("FR-HIS-01 (fault 4): a day's quiet scans are one row, so they no longer bury the updates; problems keep their own rows.")]
    public void ADaysScansAreOneRow()
    {
        InventoryRow[] rows = [Row("widget", "App widget")];
        List<ScanRun> scans = [];
        for (int index = 0; index < 26; index++)
        {
            DateTimeOffset started = Now.AddMinutes(-10 - (index * 7));
            scans.Add(new(Guid.NewGuid(), started, started.AddSeconds(12), "Completed", Guid.NewGuid()));
        }
        scans.Add(new(Guid.NewGuid(), Now.AddHours(-5), Now.AddHours(-5).AddSeconds(3), "Cancelled", Guid.NewGuid()));
        scans.Add(new(Guid.NewGuid(), Now.AddHours(-6), null, "Failed", Guid.NewGuid()));
        UpdateJobRecord[] jobs = [.. UpdateJobHistory.Group(Enumerable.Range(0, 4).Select(index =>
            History("widget", UpdateHistoryStatus.Success, Now.AddHours(-1).AddMinutes(-index))))];

        IReadOnlyList<ActivityEntry> entries = ActivityPresenter.Entries(jobs, scans, [], [], rows, Culture, Utc);
        ActivityPagePresentation all = ActivityPresenter.Build(entries, ActivityFilter.All, Now, Culture, Utc);

        CollectionAssert.AreEqual(RecordCounts, all.Filters.Select(filter => filter.Count).ToArray(), "the tabs still count every record");
        ActivityDay today = all.Days.Single();
        Assert.AreEqual(6, today.Entries.Count, "four updates, one row for the day's scans and the scan that didn't complete");
        ActivityEntry rollup = today.Entries.Single(entry => entry.ScanCount > 1);
        Assert.AreEqual("27 scans", rollup.Title);
        Assert.AreEqual("26 completed, 1 cancelled", rollup.OutcomeLabel);
        Assert.AreEqual("27 scans: 26 completed, 1 cancelled", rollup.Heading);
        Assert.AreEqual("Latest 14:50", rollup.RowTimeText);
        Assert.AreEqual("Latest 13 Nov 2026 · 14:50", rollup.TimeText);
        Assert.IsFalse(rollup.IsProblem);
        Assert.AreEqual(27, rollup.Facts.Count, "each scan's time and result is inside the row");
        Assert.AreEqual(new DetailFact("14:50", "Completed in 12 seconds"), rollup.Facts[0]);
        Assert.AreEqual("Cancelled in 3 seconds", rollup.Facts.Single(fact => fact.Value.StartsWith("Cancelled", StringComparison.Ordinal)).Value);
        Assert.AreEqual(0, today.Entries.ToList().IndexOf(rollup), "the row sits where the day's latest scan was");
        ActivityEntry failed = today.Entries.Single(entry => entry.Kind == ActivityKind.Scan && entry.IsProblem);
        Assert.AreEqual("Did not complete", failed.OutcomeLabel);

        ActivityPagePresentation scansOnly = ActivityPresenter.Build(entries, ActivityFilter.Scans, Now, Culture, Utc);
        Assert.AreEqual(2, scansOnly.Days.Single().Entries.Count);
        Assert.AreEqual(28, scansOnly.MatchingCount);

        // One quiet scan in a day keeps its own row, and a day of cancelled scans says so without a success tick.
        ScanRun single = new(Guid.NewGuid(), Now.AddMinutes(-3), Now.AddMinutes(-3).AddSeconds(50), "Completed", Guid.NewGuid());
        ActivityEntry alone = ActivityPresenter.Build(ActivityPresenter.Entries([], [single], [], [], rows, Culture, Utc), ActivityFilter.All, Now, Culture, Utc).Days.Single().Entries.Single();
        Assert.AreEqual("Scan", alone.Title);
        Assert.AreEqual(1, alone.ScanCount);
        ScanRun[] cancelled = [new(Guid.NewGuid(), Now.AddHours(-1), Now.AddHours(-1), "Cancelled", Guid.NewGuid()), new(Guid.NewGuid(), Now.AddHours(-2), Now.AddHours(-2), "Cancelled", Guid.NewGuid())];
        ActivityEntry allCancelled = ActivityPresenter.Build(ActivityPresenter.Entries([], cancelled, [], [], rows, Culture, Utc), ActivityFilter.All, Now, Culture, Utc).Days.Single().Entries.Single();
        Assert.AreEqual("2 scans: All cancelled", allCancelled.Heading);
        Assert.AreEqual(StatusTone.Neutral, allCancelled.Tone);
    }

    [TestMethod]
    [Description("FR-HIS-01 (fault 4): a row under a day heading shows the time of day, once; the date stays in its accessible name.")]
    public void ARowUnderADayHeadingShowsTheTimeOfDay()
    {
        DateTimeOffset at = new(2026, 11, 13, 14, 50, 10, TimeSpan.Zero);
        Assert.AreEqual("14:50", DisplayFormat.TimeOfDay(at, at.AddSeconds(20), Culture, Utc), "not 14:50–14:50");
        Assert.AreEqual("14:50", DisplayFormat.TimeOfDay(at, null, Culture, Utc));
        Assert.AreEqual("14:50–14:53", DisplayFormat.TimeOfDay(at, at.AddMinutes(3), Culture, Utc));
        Assert.AreEqual("13 Nov 2026 · 23:59 – 14 Nov 2026 · 00:02", DisplayFormat.TimeOfDay(at.AddHours(9).AddMinutes(9), at.AddHours(9).AddMinutes(12), Culture, Utc));

        UpdateJobRecord[] jobs = [.. UpdateJobHistory.Group([History("widget", UpdateHistoryStatus.Running, at.AddMinutes(-3), job: JobId), History("widget", UpdateHistoryStatus.Success, at, job: JobId)])];
        ActivityEntry update = ActivityPresenter.Entries(jobs, [], [new StableDecision(Guid.NewGuid(), "widget", "PinCurrentVersion", "1.0", at)], [], [Row("widget", "App widget")], Culture, Utc)
            .Single(entry => entry.Kind == ActivityKind.Update);
        Assert.AreEqual("14:47–14:50", update.RowTimeText);
        Assert.AreEqual("13 Nov 2026 · 14:47–14:50", update.TimeText);
        StringAssert.Contains(update.AccessibleName, "13 Nov 2026");
        ActivityEntry decision = ActivityPresenter.Entries([], [], [new StableDecision(Guid.NewGuid(), "widget", "PinCurrentVersion", "1.0", at)], [], [Row("widget", "App widget")], Culture, Utc).Single();
        Assert.AreEqual("14:50", decision.RowTimeText);
    }

    private static readonly Guid JobId = Guid.NewGuid();

    [TestMethod]
    [Description("FR-HIS-01 (fault 3): a recorded update is named by its app, even after the update gave the app a new record.")]
    public void ARecordedUpdateIsNamedByItsApp()
    {
        // The update's own record ("runtime-8.0.33") is gone: the app now has a new record matched to the same package.
        InventoryRow[] rows = [Row("runtime-8.0.34", "Microsoft Windows Desktop Runtime - 8.0.34 (x64)")];
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase) { ["Contoso.App"] = "Microsoft Windows Desktop Runtime - 8.0.34 (x64)" };
        UpdateJobRecord[] jobs = [.. UpdateJobHistory.Group([History("runtime-8.0.33", UpdateHistoryStatus.Success, Now)])];

        Assert.AreEqual("Microsoft Windows Desktop Runtime - 8.0.34 (x64) update", ActivityPresenter.Entries(jobs, [], [], [], rows, Culture, Utc, names).Single().Title);
        Assert.AreEqual("Contoso.App update", ActivityPresenter.Entries(jobs, [], [], [], rows, Culture, Utc).Single().Title, "with no match the package ID is still better than a key");
        Assert.AreEqual("Contoso.App update", ActivityPresenter.Entries(jobs, [], [], [], rows, Culture, Utc, new Dictionary<string, string> { ["Contoso.App"] = " " }).Single().Title);
        InventoryRow[] same = [Row("runtime-8.0.33", "Desktop Runtime 8")];
        Assert.AreEqual("Desktop Runtime 8 update", ActivityPresenter.Entries(jobs, [], [], [], same, Culture, Utc, names).Single().Title, "the app's own record wins");

        // The view model offers the names: package ID to the matched app's name, never a row that is named by the ID itself.
        InventoryExperienceViewModel viewModel = new();
        Guid app = Guid.NewGuid(), wingetRecord = Guid.NewGuid(), unmatched = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(wingetRecord, "winget:github.cli", "GitHub.cli"), new CanonicalProduct(app, "registry:github-cli", "GitHub CLI"), new CanonicalProduct(unmatched, "registry:other", "Other")],
            [new InstallationInstance(Guid.NewGuid(), wingetRecord, "winget", "GitHub.cli", "2.1", "Machine", "x64"), new InstallationInstance(Guid.NewGuid(), app, "registry", "github-cli", "2.1", "Machine", "x64", "GitHub, Inc."), new InstallationInstance(Guid.NewGuid(), unmatched, "registry", "other", "1.0", "Machine", "x64")]);
        PackageMapping mapping = new("winget", "GitHub.cli", "2.1", MappingConfidence.High, MappingOrigin.Automatic, "Exact version match.", false, [], new VersionComparisonResult(VersionComparisonOutcome.Equal, VersionComparisonExplanation.NumericDotted));
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary(
            [new UpdateIntelligenceItem("winget:github.cli", UpdateIntelligenceStatus.Current, mapping, "Exact.", "winget", Now), new UpdateIntelligenceItem("registry:github-cli", UpdateIntelligenceStatus.Current, mapping, "Exact.", "winget", Now), new UpdateIntelligenceItem("registry:other", UpdateIntelligenceStatus.Unmanaged, null, "None.")],
            new Dictionary<UpdateIntelligenceStatus, int>(), []));
        Assert.AreEqual(1, viewModel.PackageNames.Count);
        Assert.AreEqual("GitHub CLI", viewModel.PackageNames["github.CLI"], "package IDs match without regard to case");
    }

    [TestMethod]
    [Description("FR-UI-01 (fault 3): Installed apps opens on apps; system components are listed on request, and a row never starts with a stray dot.")]
    public void InstalledAppsOpensOnAppsTheOwnerRecognises()
    {
        Assert.AreEqual("Igor Pavlov · Windows registry", InstalledAppsFilters.PublisherAndSource("Igor Pavlov", "Windows registry"));
        Assert.AreEqual("WinGet", InstalledAppsFilters.PublisherAndSource("", "WinGet"), "no publisher, no stray separator");
        Assert.AreEqual("WinGet", InstalledAppsFilters.PublisherAndSource(null, "WinGet"));
        Assert.AreEqual("Windows registry", InstalledAppsFilters.PublisherAndSource("Unknown", "Windows registry"));
        Assert.AreEqual("Show system components (85)", InstalledAppsFilters.SystemComponentsLabel(85));

        InventoryExperienceViewModel viewModel = new();
        Guid appId = Guid.NewGuid(), frameworkId = Guid.NewGuid(), resourceId = Guid.NewGuid(), systemId = Guid.NewGuid(), unknownId = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(appId, "msix:terminal", "Windows Terminal"), new CanonicalProduct(frameworkId, "msix:vclibs", "Microsoft.VCLibs.140.00"), new CanonicalProduct(resourceId, "msix:lang", "Language pack"),
             new CanonicalProduct(systemId, "msix:shell", "1527c705-839a-4832-9118-54d4Bd6a0c89"), new CanonicalProduct(unknownId, "registry:7zip", "7-Zip")],
            [Instance(appId, "Application"), Instance(frameworkId, "Framework"), Instance(resourceId, "Resource"), Instance(systemId, "System"), Instance(unknownId, "Unknown")]);

        Assert.IsFalse(viewModel.ShowSystemComponents, "hidden until asked for");
        Assert.AreEqual(3, viewModel.SystemComponentCount);
        Assert.AreEqual(5, viewModel.AllRows.Count, "the saved inventory and every other page keep every record");
        CollectionAssert.AreEqual(AppNames, viewModel.VisibleRows.Select(row => row.Name).ToArray());
        Assert.AreEqual(2, InstalledAppsFilters.Options(viewModel.ListedRows)[0].Count, "the All tab counts what the list can show");
        Assert.AreEqual("2 apps", InstalledAppsFilters.CountText(viewModel.VisibleRows.Count, viewModel.ListedRows.Count));

        viewModel.SetShowSystemComponents(true);
        Assert.AreEqual(5, viewModel.VisibleRows.Count);
        Assert.AreEqual(5, InstalledAppsFilters.Options(viewModel.ListedRows)[0].Count);
        viewModel.SetFilters("zip", null, null, null, null);
        viewModel.SetShowSystemComponents(false);
        Assert.AreEqual("7-Zip", viewModel.VisibleRows.Single().Name, "the switch combines with search");
    }

    private static readonly string[] AppNames = ["7-Zip", "Windows Terminal"];

    private static InstallationInstance Instance(Guid product, string classification) =>
        new(Guid.NewGuid(), product, classification == "Unknown" ? "registry" : "msix", product.ToString("N"), "1.0", "User", "x64", "Contoso", null, null, null, classification);

    private static InventoryRow Row(string key, string name) =>
        new(Guid.NewGuid(), name, "Contoso", "winget", "User", "1.0", "Current", "Application", [], key);

    private static UpdateHistoryEntry History(string key, UpdateHistoryStatus status, DateTimeOffset at, Guid? job = null) =>
        new(Guid.NewGuid(), job ?? Guid.NewGuid(), Guid.NewGuid(), key, UpdateProvider.WinGet, "Contoso.App", "1.0", "2.0", at, status, 0, "recorded");
}
