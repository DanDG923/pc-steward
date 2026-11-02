using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Task 73 Phase 3 (ADR-030 concept A; FR-UI-01/05/06, FR-HIS-01–03, FR-POL-01–05): Home, Installed apps filters, app details,
/// Activity and Decisions presentation. Portable and deterministic (fixed culture, UTC zone and clock); nothing here grants authority.
/// </summary>
[TestClass]
public sealed class HomeActivityDecisionsPresentationTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] FilterLabels = ["All", "Updates", "Needs review", "Unmanaged", "Pinned & ignored"];
    private static readonly string[] ActivityFilterLabels = ["All", "Updates", "Scans", "Problems"];
    private static readonly string[] FreshnessWords = ["Local-only", "Live", "Cached", "Offline", "Unavailable"];
    private static readonly string[] OutcomeLabels = ["Verified", "Restart required", "Installed, not verified", "Result not verified", "Failed", "Not started"];
    private static readonly string[] AttentionKeys = ["failed", "restart"];
    private static readonly int[] FilterCounts = [8, 1, 2, 1, 2];
    private static readonly string[] UpdatesLabels = ["Update available — verified"];
    private static readonly string[] NeedsReviewLabels = ["Update available — review", "Source unavailable"];
    private static readonly string[] PinnedIgnoredLabels = ["Pinned", "Ignored"];
    private static readonly string[] InstanceFactLabels = ["Found by", "Identity", "Version", "Scope", "Architecture"];
    private static readonly int[] ActivityCounts = [5, 2, 2, 2];
    private static readonly string[] DayHeadings = ["Today", "Yesterday", "30 Oct 2026", "28 Oct 2026"];

    [TestMethod]
    [Description("AC-UX-03; audit P-M6: one date format app-wide, relative ages in words and sizes in bytes, KB, MB and GB.")]
    public void DisplayFormatUsesOneDateFormatAndHumanReadableSizes()
    {
        DateTimeOffset at = new(2026, 11, 3, 14, 5, 0, TimeSpan.Zero);
        Assert.AreEqual("3 Nov 2026", DisplayFormat.Date(at, Culture, Utc));
        Assert.AreEqual("14:05", DisplayFormat.Time(at, Culture, Utc));
        Assert.AreEqual("3 Nov 2026 · 14:05", DisplayFormat.DateTime(at, Culture, Utc));
        Assert.AreEqual("3 Nov 2026 · 14:05–14:09", DisplayFormat.Range(at, at.AddMinutes(4), Culture, Utc));
        Assert.AreEqual("Started 3 Nov 2026 · 14:05", DisplayFormat.Range(at, null, Culture, Utc));

        Assert.AreEqual("just now", DisplayFormat.Relative(Now.AddSeconds(-20), Now, Culture, Utc));
        Assert.AreEqual("just now", DisplayFormat.Relative(Now.AddMinutes(5), Now, Culture, Utc), "clock skew never reads as a negative age");
        Assert.AreEqual("1 minute ago", DisplayFormat.Relative(Now.AddMinutes(-1), Now, Culture, Utc));
        Assert.AreEqual("10 minutes ago", DisplayFormat.Relative(Now.AddMinutes(-10), Now, Culture, Utc));
        Assert.AreEqual("3 hours ago", DisplayFormat.Relative(Now.AddHours(-3), Now, Culture, Utc));
        Assert.AreEqual("yesterday", DisplayFormat.Relative(Now.AddHours(-20), Now, Culture, Utc));
        Assert.AreEqual("4 days ago", DisplayFormat.Relative(Now.AddDays(-4), Now, Culture, Utc));
        Assert.AreEqual("on 20 Oct 2026", DisplayFormat.Relative(Now.AddDays(-12), Now, Culture, Utc));

        Assert.AreEqual("Today", DisplayFormat.DayHeading(new DateOnly(2026, 11, 1), Now, Culture, Utc));
        Assert.AreEqual("Yesterday", DisplayFormat.DayHeading(new DateOnly(2026, 10, 31), Now, Culture, Utc));
        Assert.AreEqual("29 Oct 2026", DisplayFormat.DayHeading(new DateOnly(2026, 10, 29), Now, Culture, Utc));

        Assert.AreEqual("0 bytes", DisplayFormat.Size(-1, Culture));
        Assert.AreEqual("1 byte", DisplayFormat.Size(1, Culture));
        Assert.AreEqual("1023 bytes", DisplayFormat.Size(1023, Culture));
        Assert.AreEqual("1.5 KB", DisplayFormat.Size(1536, Culture));
        Assert.AreEqual("5 MB", DisplayFormat.Size(5L * 1024 * 1024, Culture));
        Assert.AreEqual("3 GB", DisplayFormat.Size(3L * 1024 * 1024 * 1024, Culture));
        Assert.AreEqual(DisplayFormat.Size(4096), UpdateJobCleanupText.Size(4096), "the completion view uses the same formatter");
        Assert.AreEqual("1 app", DisplayFormat.Plural(1, "app"));
        Assert.AreEqual("3 apps", DisplayFormat.Plural(3, "app"));
    }

    [TestMethod]
    [Description("FR-UI-05: freshness is always one of five words, and the least fresh source wins.")]
    public void FreshnessIsStatedInWordsAndNeverOverstated()
    {
        Assert.AreEqual(FreshnessWords.Length, Enum.GetValues<EvidenceFreshness>().Length);
        CollectionAssert.AreEqual(FreshnessWords, Enum.GetValues<EvidenceFreshness>().Select(FreshnessPresenter.Word).ToArray());

        FreshnessPresentation off = FreshnessPresenter.Describe(false, [Source("winget", SourceSnapshotState.Live, Now)], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.LocalOnly, off.Freshness);
        StringAssert.StartsWith(off.Sentence, "Local-only");
        Assert.AreEqual(EvidenceFreshness.LocalOnly, FreshnessPresenter.Describe(true, [], Now, Culture, Utc).Freshness);

        FreshnessPresentation live = FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-10))], Now, Culture, Utc);
        Assert.AreEqual("Live", live.Word);
        StringAssert.Contains(live.Sentence, "10 minutes ago");

        FreshnessPresentation mixed = FreshnessPresenter.Describe(true,
            [Source("winget", SourceSnapshotState.Live, Now), Source("msstore", SourceSnapshotState.Cached, Now.AddDays(-2))], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.Cached, mixed.Freshness, "a cached source is never summarised as live");
        StringAssert.Contains(mixed.Sentence, "may be out of date");
        StringAssert.Contains(mixed.Sentence, "last successful check was 2 days ago", "the age is the least fresh source's (independent review M1)");

        Assert.AreEqual(EvidenceFreshness.Offline, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now, "source:offline")], Now, Culture, Utc).Freshness);
        Assert.AreEqual(EvidenceFreshness.Unavailable, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Unavailable, null)], Now, Culture, Utc).Freshness);
    }

    [TestMethod]
    [Description("FR-UI-01/05/06; ADR-030 concept A; audit P-H4/P-M4: one hero sentence, FR-UI-01 counts including pinned, ignored and failed, tiles that open filtered pages, and the compact attention view.")]
    public void HomeHasOneHeroSentenceCountsAndTilesThatOpenFilteredPages()
    {
        InventoryRow[] rows =
        [
            Row("ready-1", "VerifiedUpdate", "1.0"), Row("ready-2", "VerifiedUpdate", "1.0"),
            Row("review", "ReviewRequired", "1.0"), Row("unavailable", "FailedUnavailable", "1.0"),
            Row("current", "Current", "2.0"), Row("unmanaged", "Unmanaged", "1.0"),
            Row("pinned", "Pinned", "1.0"), Row("ignored", "Ignored", "1.0"),
            Row("failed", "Current", "1.0"), Row("restart", "Current", "1.0"), Row("fixed", "Current", "2.0")
        ];
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal)
        {
            ["ready-1"] = Detail("ready-1", "1.0", "1.1"),
            ["ready-2"] = Detail("ready-2", "1.0", "2.0")
        };
        UpdateJobRecord[] jobs = [.. UpdateJobHistory.Group(
        [
            History("failed", UpdateHistoryStatus.InstallerFailed, Now.AddHours(-1), exitCode: 1603),
            History("restart", UpdateHistoryStatus.RestartRequired, Now.AddHours(-2)),
            History("fixed", UpdateHistoryStatus.Inconclusive, Now.AddHours(-3), target: "2.0"),
        ])];
        IReadOnlyList<ActivityEntry> activity = ActivityPresenter.Entries(jobs, [], [], [], rows, Culture, Utc);
        HomePresentation home = HomePresenter.Build(new HomeInputs(rows, details, true, [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-10))],
            [], jobs, activity, Now.AddHours(-5), Now), Culture, Utc);

        Assert.IsTrue(home.HasInventory);
        Assert.AreEqual("2 updates ready · scanned 5 hours ago · sources checked 10 minutes ago", home.Hero, "the scan time is shown even with sources on (independent review L4)");
        Assert.AreEqual("Live", home.Freshness.Word);
        Assert.AreEqual("Review updates", home.PrimaryLabel);
        StringAssert.StartsWith(home.PrimaryAccessibleName, home.PrimaryLabel, "label-in-name");
        Assert.AreEqual("Check for updates", home.SecondaryLabel);
        StringAssert.StartsWith(home.SecondaryAccessibleName, home.SecondaryLabel);
        StringAssert.Contains(home.SecondaryHelpText, "never asks for administrator permission");
        Assert.AreEqual("Scans this PC, then checks your enabled update sources.", home.SecondaryCaption, "independent review L1");

        Assert.AreEqual(new HomeCounts(rows.Length, 2, 1, 1, 4, 1, 1, 1, 1, 1), home.Counts, "FR-UI-01 counts include pinned, ignored and failed; a later scan resolves an old attempt");

        Dictionary<HomeTileKind, HomeTile> tiles = home.Tiles.ToDictionary(tile => tile.Kind);
        Assert.HasCount(Enum.GetValues<HomeTileKind>().Length, tiles);
        Assert.AreEqual(new HomeTileTarget(ShellPage.Activity, ActivityFilter: ActivityFilter.Updates), tiles[HomeTileKind.RestartRequired].Target);
        Assert.AreEqual(new HomeTileTarget(ShellPage.Activity, ActivityFilter: ActivityFilter.Problems), tiles[HomeTileKind.NeedsAttention].Target);
        Assert.AreEqual(new HomeTileTarget(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.NeedsReview), tiles[HomeTileKind.NeedsReview].Target);
        Assert.AreEqual(new HomeTileTarget(ShellPage.Decisions), tiles[HomeTileKind.PinnedAndIgnored].Target);
        Assert.AreEqual(new HomeTileTarget(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.Unmanaged), tiles[HomeTileKind.Unmanaged].Target);
        Assert.AreEqual(new HomeTileTarget(ShellPage.Activity, ActivityFilter: ActivityFilter.All), tiles[HomeTileKind.RecentActivity].Target);
        Assert.AreEqual("2", tiles[HomeTileKind.NeedsReview].Value);
        Assert.AreEqual("1 pinned · 1 ignored", tiles[HomeTileKind.PinnedAndIgnored].Value);
        StringAssert.Contains(tiles[HomeTileKind.Unmanaged].Description, "not a safety warning", "unmanaged never reads as unsafe");
        Assert.IsFalse(home.Tiles.Any(tile => tile.Description.Contains("unsafe", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(home.Tiles.All(tile => tile.Glyph.Length == 1), "every tile has an icon as well as text");
        foreach (HomeTile tile in home.Tiles)
        {
            StringAssert.StartsWith(tile.AccessibleName, tile.Title, "each tile's accessible name starts with its visible title");
            StringAssert.Contains(tile.AccessibleName, tile.Value);
        }

        CollectionAssert.AreEqual(AttentionKeys, home.Attention.Select(item => item.StableProductKey).ToArray(), "compact attention view: failed then restart, resolved attempts dropped");
        Assert.AreEqual("Failed", home.Attention[0].OutcomeLabel);
        Assert.AreEqual("Restart required", home.Attention[1].OutcomeLabel);
        Assert.IsTrue(home.Attention.Count <= HomePresenter.MaximumAttentionItems);
        Assert.AreEqual(ActivityFilter.Updates, home.AttentionActivityFilter, "a listed restart opens a filter that includes it (independent review L3)");
        Assert.IsTrue(ActivityPresenter.Matches(home.AttentionActivityFilter, activity.Single(entry => entry.OutcomeLabel == "Restart required")));
        Assert.IsTrue(ActivityPresenter.Matches(home.AttentionActivityFilter, activity.Single(entry => entry.OutcomeLabel == "Failed")));
        StringAssert.StartsWith(home.AttentionLinkAccessibleName, "View in Activity", "label-in-name");
    }

    [TestMethod]
    [Description("FR-UI-01/05: Home before the first scan, and with sources off, never claims a check that didn't happen.")]
    public void HomeWithoutInventoryOrSourcesIsHonest()
    {
        HomePresentation empty = HomePresenter.Build(new HomeInputs([], new Dictionary<string, UpdateDetail>(), false, [], [], [], [], null, Now), Culture, Utc);
        Assert.IsFalse(empty.HasInventory);
        Assert.AreEqual("PC Steward hasn't looked at this PC yet", empty.Hero);
        Assert.AreEqual(EvidenceFreshness.LocalOnly, empty.Freshness.Freshness);
        Assert.AreEqual("Nothing yet", empty.Tiles.Single(tile => tile.Kind == HomeTileKind.RecentActivity).Value);

        InventoryRow[] rows = [Row("ready", "VerifiedUpdate", "1.0")];
        HomePresentation sourcesOff = HomePresenter.Build(new HomeInputs(rows,
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal) { ["ready"] = Detail("ready", "1.0", "2.0") }, false, [], [], [], [], Now.AddMinutes(-30), Now), Culture, Utc);
        Assert.AreEqual("No updates ready · scanned 30 minutes ago", sourcesOff.Hero, "the ready count is the Updates page's Ready group, which fails closed with sources off");
        Assert.AreEqual(0, sourcesOff.Counts.Verified);
        Assert.AreEqual("Scan again", sourcesOff.SecondaryLabel, "with sources off the secondary action is a scan only (independent review L1)");
        StringAssert.StartsWith(sourcesOff.SecondaryAccessibleName, sourcesOff.SecondaryLabel);
        Assert.AreEqual(HomePresenter.ScanAgainHelp, sourcesOff.SecondaryHelpText);
        Assert.AreEqual(string.Empty, sourcesOff.SecondaryCaption);
    }

    [TestMethod]
    [Description("FR-UI-05 (independent review M1, L4, L7): the freshness age and the hero's source age come from the least fresh source; Live lasts only the live window.")]
    public void FreshnessAgeComesFromTheLeastFreshSourceAndLiveExpires()
    {
        FreshnessPresentation unavailable = FreshnessPresenter.Describe(true,
            [Source("winget", SourceSnapshotState.Live, Now), Source("msstore", SourceSnapshotState.Unavailable, Now.AddDays(-9))], Now, Culture, Utc);
        Assert.AreEqual("Unavailable", unavailable.Word);
        Assert.AreEqual("Unavailable: a package source couldn't complete its check. The least fresh source's last successful check was on 23 Oct 2026.", unavailable.Sentence,
            "9 days ago is shown as a date by the one app-wide format; never the live source's 'just now'");
        FreshnessPresentation fiveDays = FreshnessPresenter.Describe(true,
            [Source("winget", SourceSnapshotState.Live, Now), Source("msstore", SourceSnapshotState.Unavailable, Now.AddDays(-5))], Now, Culture, Utc);
        StringAssert.Contains(fiveDays.Sentence, "last successful check was 5 days ago");
        Assert.IsFalse(fiveDays.Sentence.Contains("just now", StringComparison.Ordinal));
        FreshnessPresentation never = FreshnessPresenter.Describe(true,
            [Source("winget", SourceSnapshotState.Live, Now), Source("msstore", SourceSnapshotState.Unavailable, null)], Now, Culture, Utc);
        StringAssert.Contains(never.Sentence, "One source has never completed a successful check.");

        Assert.AreEqual(TimeSpan.FromMinutes(15), FreshnessPresenter.LiveWindow);
        Assert.AreEqual(EvidenceFreshness.Live, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-14))], Now, Culture, Utc).Freshness);
        FreshnessPresentation expired = FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-16))], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.Cached, expired.Freshness, "a live result older than the live window reads as Cached at render time");
        StringAssert.Contains(expired.Sentence, "The last successful check was 16 minutes ago.");
        Assert.AreEqual(EvidenceFreshness.Cached, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Live, null)], Now, Culture, Utc).Freshness);

        InventoryRow[] rows = [Row("current", "Current", "1.0")];
        HomePresentation home = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-2)), Source("msstore", SourceSnapshotState.Unavailable, Now.AddDays(-5))],
            [], [], [], Now.AddMinutes(-10), Now), Culture, Utc);
        Assert.AreEqual("No updates ready · scanned 10 minutes ago · sources checked 5 days ago", home.Hero);
        Assert.AreEqual("Unavailable", home.Freshness.Word);
        HomePresentation neverChecked = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-2)), Source("msstore", SourceSnapshotState.Unavailable, null)],
            [], [], [], Now.AddMinutes(-10), Now), Culture, Utc);
        Assert.AreEqual("No updates ready · scanned 10 minutes ago · a source hasn't been checked successfully", neverChecked.Hero);
    }

    [TestMethod]
    [Description("FR-UI-06, FR-HIS-01 (independent review M2): an attempt drops off Home's attention list once a completed inventory shows it moved on; it is never relabelled Verified.")]
    public void AttentionAttemptsResolveByVersionComparisonOrAbsenceButKeepTheirOutcome()
    {
        UpdateJobRecord Job(UpdateHistoryStatus status, string installed = "1.0", string target = "2.0") =>
            UpdateJobHistory.Group([History("app", status, Now.AddHours(-1), target: target, installed: installed)]).Single();
        UpdateJobRecord failed = Job(UpdateHistoryStatus.InstallerFailed);

        Assert.IsTrue(UpdateJobHistory.NeedsAttention(failed, "1.0", Now), "unchanged installed version: still needs attention");
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(failed, "2.0", Now), "installed equals target");
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(Job(UpdateHistoryStatus.InstallerFailed, target: "2.0"), "2.0.0.0", Now), "equal by the domain comparer, not by string");
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(Job(UpdateHistoryStatus.Inconclusive, target: "2.0.0.0"), "2.0", Now));
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(failed, "2.1", Now), "installed is newer than the target");
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(failed, "1.5", Now), "installed version changed since the attempt");
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(failed, "0.9", Now), "a downgrade is also a change since the attempt");
        Assert.IsTrue(UpdateJobHistory.NeedsAttention(Job(UpdateHistoryStatus.InstallerFailed, installed: "1.0"), "1.0.0", Now), "the same version written differently is not a change");
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(failed, null, Now), "absent from a completed inventory taken after the attempt");
        Assert.IsTrue(UpdateJobHistory.NeedsAttention(failed, null, Now.AddHours(-2)), "absence in an inventory taken before the attempt proves nothing");
        Assert.IsTrue(UpdateJobHistory.NeedsAttention(failed, null, null), "no completed inventory: still needs attention");

        UpdateJobRecord restart = Job(UpdateHistoryStatus.RestartRequired);
        Assert.IsTrue(UpdateJobHistory.RestartPending(restart, "1.0", Now));
        Assert.IsFalse(UpdateJobHistory.RestartPending(restart, "2.0.0", Now), "a restart finished: the target is installed");
        Assert.IsFalse(UpdateJobHistory.RestartPending(restart, null, Now));
        Assert.IsFalse(UpdateJobHistory.NeedsAttention(Job(UpdateHistoryStatus.Success), "1.0", Now), "a verified attempt never needs attention");

        InventoryRow[] rows = [Row("same", "Current", "1.0"), Row("equal", "Current", "2.0.0.0"), Row("newer", "Current", "3.0"), Row("changed", "Current", "1.5")];
        UpdateJobRecord[] jobs = [.. UpdateJobHistory.Group(
        [
            History("same", UpdateHistoryStatus.InstallerFailed, Now.AddHours(-1)),
            History("equal", UpdateHistoryStatus.Inconclusive, Now.AddHours(-2)),
            History("newer", UpdateHistoryStatus.RestartRequired, Now.AddHours(-3)),
            History("changed", UpdateHistoryStatus.VerificationFailed, Now.AddHours(-4)),
            History("removed", UpdateHistoryStatus.InstallerFailed, Now.AddHours(-5)),
        ])];
        IReadOnlyList<ActivityEntry> activity = ActivityPresenter.Entries(jobs, [], [], [], rows, Culture, Utc);
        HomePresentation home = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), false, [], [], jobs, activity, Now.AddMinutes(-30), Now), Culture, Utc);
        Assert.AreEqual("same", home.Attention.Single().StableProductKey, "resolved attempts drop off Home's attention list");
        Assert.AreEqual(1, home.Counts.Failed);
        Assert.AreEqual(0, home.Counts.RestartRequired);
        Assert.AreEqual(ActivityFilter.Problems, home.AttentionActivityFilter, "no restart listed: the problems filter is enough");
        Assert.AreEqual(HomePresenter.AttentionProblemsLinkName, home.AttentionLinkAccessibleName);
        Assert.IsFalse(activity.Any(entry => entry.OutcomeLabel == "Verified"), "resolution never relabels an attempt as Verified");
        Assert.AreEqual(4, activity.Count(entry => entry.IsProblem), "Activity keeps every recorded outcome");
    }

    [TestMethod]
    [Description("FR-UI-01; AC-UX-03; audit P-L7: saved filters use the status vocabulary and 'All' counts every app.")]
    public void InstalledAppsSavedFiltersCountEveryAppAndUseTheVocabulary()
    {
        InventoryRow[] rows =
        [
            Row("a", "VerifiedUpdate", "1"), Row("b", "ReviewRequired", "1"), Row("c", "FailedUnavailable", "1"),
            Row("d", "Unmanaged", "1"), Row("e", "Pinned", "1"), Row("f", "Ignored", "1"), Row("g", "Current", "1"),
            Row("h", StatusVocabulary.InstalledKey, "1")
        ];
        IReadOnlyList<InstalledAppsFilterOption> options = InstalledAppsFilters.Options(rows);
        CollectionAssert.AreEqual(FilterLabels, options.Select(option => option.Label).ToArray());
        CollectionAssert.AreEqual(FilterCounts, options.Select(option => option.Count).ToArray());
        Assert.AreEqual("All (8)", options[0].DisplayLabel, "All includes current, pinned, ignored and source-not-checked rows");
        foreach (InstalledAppsFilterOption option in options) StringAssert.StartsWith(option.AccessibleName, option.DisplayLabel);
        CollectionAssert.AreEqual(UpdatesLabels, options[1].StatusLabels.ToArray());
        CollectionAssert.AreEqual(NeedsReviewLabels, options[2].StatusLabels.ToArray());
        StringAssert.Contains(options[3].AccessibleName, "This is not a safety warning.");
        CollectionAssert.AreEqual(PinnedIgnoredLabels, options[4].StatusLabels.ToArray());

        InventoryExperienceViewModel viewModel = new();
        viewModel.LoadInventory(
            [new CanonicalProduct(Guid.NewGuid(), "k1", "Alpha"), new CanonicalProduct(Guid.NewGuid(), "k2", "Beta")],
            [], Now);
        viewModel.SetSavedFilter(InstalledAppsFilter.Unmanaged);
        Assert.IsEmpty(viewModel.VisibleRows);
        viewModel.SetSavedFilter(InstalledAppsFilter.All);
        Assert.HasCount(2, viewModel.VisibleRows);
        viewModel.SetFilters("alp", null, null, null, null);
        Assert.HasCount(1, viewModel.VisibleRows, "search combines with the saved filter");
        viewModel.SetSavedFilter((InstalledAppsFilter)99);
        Assert.AreEqual(InstalledAppsFilter.All, viewModel.SavedFilter);
    }

    [TestMethod]
    [Description("FR-HIS-02; audit P-L6: technical details are structured sections and the per-product timeline is a list in words.")]
    public void AppDetailsAreStructuredAndTheTimelineIsInWords()
    {
        InstallationInstance instance = new(Guid.NewGuid(), Guid.NewGuid(), "registry", "contoso-widget", "1.0", "User", "x64", "Contoso");
        DetailItem item = AppDetailPresenter.Instances([instance]).Single();
        Assert.AreEqual("registry: 1.0", item.Title);
        CollectionAssert.IsSubsetOf(InstanceFactLabels, item.Facts.Select(fact => fact.Label).ToArray());

        ReviewCandidateChoice candidate = new("winget", "Contoso.Widget", "2.0", "Contoso", "User", "X64", null, "evidence", ["ArchitectureConflict"]);
        DetailItem candidateItem = AppDetailPresenter.Candidates([candidate]).Single();
        Assert.AreEqual("winget · Contoso.Widget", candidateItem.Title);
        StringAssert.Contains(candidateItem.Facts.Single(fact => fact.Label == "Why it needs review").Value, "different architecture");

        IReadOnlyList<ProductTimelineItem> timeline = AppDetailPresenter.Timeline(
        [
            new(Now.AddDays(-3), "PinCurrentVersion", "PinCurrentVersion: 1.0", "Work laptop"),
            new(Now.AddDays(-2), "Ignore", $"Ignore: {Now.AddDays(10):O}", null),
            new(Now.AddDays(-1), "Update", "Update InstallerFailed: 2.0", "installer exit 1603"),
            new(Now.AddHours(-2), "Update", "Update Running: 2.0", "running"),
            new(Now.AddHours(-1), "InventoryScan", "Present in the most recent successful inventory scan.", null),
            new(Now.AddMinutes(-5), "Update", "Update NotAStatus: 2.0", null),
        ], Culture, Utc);
        Assert.AreEqual("Update recorded", timeline[0].Title, "unrecognised update text falls back safely");
        Assert.AreEqual("Found in the latest inventory scan", timeline[1].Title);
        Assert.AreEqual("Update to 2.0 started", timeline[2].Title);
        Assert.AreEqual("Update to 2.0: Failed", timeline[3].Title);
        StringAssert.Contains(timeline[3].Detail!, "installer exit 1603");
        Assert.AreEqual("Ignored until 11 Nov 2026", timeline[4].Title);
        Assert.AreEqual("Pinned at version 1.0", timeline[5].Title);
        Assert.AreEqual("Reason: Work laptop", timeline[5].Detail);
        Assert.AreEqual("29 Oct 2026 · 12:00", timeline[5].When);
        Assert.IsFalse(timeline.Any(entry => entry.Title.Contains("PinCurrentVersion", StringComparison.Ordinal)), "no raw kinds in user copy");
        Assert.IsTrue(timeline.All(entry => entry.Glyph.Length == 1));

        // Final review Info-2: the app timeline passes the recorded exit code and diagnostic (provider-completed evidence) to the
        // outcome presenter like Activity does, so the same job shows the same display state in both places.
        UpdateHistoryEntry[] cases =
        [
            History("app", UpdateHistoryStatus.VerificationRequired, Now, exitCode: 0),
            History("app", UpdateHistoryStatus.VerificationRequired, Now, diagnostic: UpdateOutcomePresenter.ProviderCompletedCheckInterruptedDiagnostic),
            History("app", UpdateHistoryStatus.VerificationRequired, Now, exitCode: null),
            History("app", UpdateHistoryStatus.InstallerFailed, Now, exitCode: 1603),
            History("app", UpdateHistoryStatus.TimedOut, Now),
        ];
        foreach (UpdateHistoryEntry recorded in cases)
        {
            UpdateOutcomePresentation activity = UpdateJobHistory.Group([recorded]).Single().Outcome;
            ProductTimelineItem shown = AppDetailPresenter.Timeline(
                [new(recorded.OccurredAt, "Update", $"Update {recorded.Status}: {recorded.TargetVersion}", recorded.Diagnostic, recorded.ExitCode)], Culture, Utc).Single();
            Assert.AreEqual($"Update to {recorded.TargetVersion}: {activity.Label}", shown.Title, $"{recorded.Status}/{recorded.ExitCode}/{recorded.Diagnostic}");
            Assert.AreEqual(activity.Tone, shown.Tone, recorded.Status.ToString());
        }
        Assert.AreEqual("Update to 2.0: Installed, not verified", AppDetailPresenter.Timeline([new(Now, "Update", "Update VerificationRequired: 2.0", "recorded", 0)], Culture, Utc).Single().Title,
            "the provider reported finishing (exit code zero), so the timeline matches Activity");
    }

    [TestMethod]
    [Description("FR-HIS-01/03; UJ-04: one record per job; outcomes use the Phase 2 display states; exit code alone is never Verified.")]
    public void UpdateJobsAreGroupedAndUseTheHonestOutcomes()
    {
        Guid job = Guid.NewGuid();
        UpdateJobRecord record = UpdateJobHistory.Group(
        [
            History("app", UpdateHistoryStatus.Running, Now.AddMinutes(-5), job: job),
            History("app", UpdateHistoryStatus.VerificationRequired, Now.AddMinutes(-2), job: job, exitCode: 0),
            History("app", UpdateHistoryStatus.Inconclusive, Now.AddMinutes(-2), job: job, exitCode: 0),
        ]).Single();
        Assert.AreEqual(Now.AddMinutes(-5), record.StartedAt);
        Assert.AreEqual(Now.AddMinutes(-2), record.EndedAt);
        Assert.AreEqual(UpdateHistoryStatus.Inconclusive, record.Final.Status, "a terminal status outranks verification-required at the same time");
        Assert.AreEqual(UpdateOutcomeKind.InstalledNotVerified, record.Outcome.Kind, "exit code zero is not Verified");

        Dictionary<UpdateHistoryStatus, string> expected = new()
        {
            [UpdateHistoryStatus.Success] = "Verified",
            [UpdateHistoryStatus.RestartRequired] = "Restart required",
            [UpdateHistoryStatus.Inconclusive] = "Installed, not verified",
            [UpdateHistoryStatus.Running] = "Result not verified",
            [UpdateHistoryStatus.TimedOut] = "Result not verified",
            [UpdateHistoryStatus.InstallerFailed] = "Failed",
            [UpdateHistoryStatus.ElevationDenied] = "Not started",
        };
        foreach ((UpdateHistoryStatus status, string label) in expected)
        {
            UpdateJobRecord single = UpdateJobHistory.Group([History("app", status, Now)]).Single();
            Assert.AreEqual(label, single.Outcome.Label, status.ToString());
            CollectionAssert.Contains(OutcomeLabels, single.Outcome.Label);
        }

        // FR-HIS-03: every failure class stays distinct, in words.
        string[] classes = [.. new[] { UpdateHistoryStatus.SourceFailed, UpdateHistoryStatus.DownloadFailed, UpdateHistoryStatus.InstallerFailed, UpdateHistoryStatus.ElevationDenied, UpdateHistoryStatus.TimedOut, UpdateHistoryStatus.VerificationFailed }
            .Select(UpdateJobHistory.FailureClass)];
        Assert.HasCount(classes.Length, classes.Distinct(StringComparer.Ordinal).ToArray());
        Assert.IsTrue(Enum.GetValues<UpdateHistoryStatus>().All(status => UpdateJobHistory.FailureClass(status) != "Unknown"));
    }

    [TestMethod]
    [Description("FR-HIS-01–03: Activity is filterable (All, Updates, Scans, Problems), grouped by day, with outcome, times, provider, package ID, target version and expandable sanitized details.")]
    public void ActivityIsFilterableGroupedByDayAndShowsStructuredDetails()
    {
        InventoryRow[] rows = [Row("widget", "Current", "1.0")];
        Guid widgetJob = Guid.NewGuid();
        UpdateJobRecord[] jobs = [.. UpdateJobHistory.Group(
        [
            History("widget", UpdateHistoryStatus.Running, Now.AddHours(-1).AddMinutes(-3), job: widgetJob),
            History("widget", UpdateHistoryStatus.InstallerFailed, Now.AddHours(-1), job: widgetJob, exitCode: 1603, diagnostic: "installer failed token=abc123"),
            History("gone", UpdateHistoryStatus.Success, Now.AddDays(-1)),
        ])];
        ScanRun[] scans =
        [
            new(Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-3).AddSeconds(42), "Completed", Guid.NewGuid()),
            new(Guid.NewGuid(), Now.AddDays(-2), null, "Failed", Guid.NewGuid()),
        ];
        StableDecision[] decisions = [new(Guid.NewGuid(), "widget", "PinCurrentVersion", "1.0", Now.AddDays(-4), "Hold for now")];

        IReadOnlyList<ActivityEntry> entries = ActivityPresenter.Entries(jobs, scans, decisions, [], rows, Culture, Utc);
        ActivityPagePresentation all = ActivityPresenter.Build(entries, ActivityFilter.All, Now, Culture, Utc);

        CollectionAssert.AreEqual(ActivityFilterLabels, all.Filters.Select(filter => filter.Label).ToArray());
        CollectionAssert.AreEqual(ActivityCounts, all.Filters.Select(filter => filter.Count).ToArray());
        CollectionAssert.AreEqual(DayHeadings, all.Days.Select(day => day.Heading).ToArray());
        foreach (ActivityFilterOption option in all.Filters) StringAssert.Contains(option.AccessibleName, option.DisplayLabel);

        ActivityEntry failed = all.Days[0].Entries[0];
        Assert.AreEqual("App widget update", failed.Title);
        Assert.AreEqual("Failed", failed.OutcomeLabel);
        Assert.IsTrue(failed.IsProblem);
        Assert.AreEqual("1 Nov 2026 · 10:57–11:00", failed.TimeText);
        Dictionary<string, string> facts = failed.Facts.ToDictionary(fact => fact.Label, fact => fact.Value);
        Assert.AreEqual("WinGet", facts["Provider"]);
        Assert.AreEqual("Contoso.App", facts["Package ID"]);
        Assert.AreEqual("1.0 → 2.0", facts["Version"]);
        Assert.AreEqual("1 Nov 2026 · 10:57", facts["Started"]);
        Assert.AreEqual("1 Nov 2026 · 11:00", facts["Finished"]);
        Dictionary<string, string> details = failed.Details.ToDictionary(fact => fact.Label, fact => fact.Value);
        Assert.AreEqual("1603", details["Exit code"]);
        Assert.AreEqual("Installer failure", details["Failure class"]);
        Assert.IsFalse(details["Recorded output"].Contains("abc123", StringComparison.Ordinal), "output is sanitized");
        StringAssert.Contains(failed.AccessibleName, "Failed");

        Assert.AreEqual("Contoso.App update", Single(entries, ActivityKind.Update, "Verified").Title, "an app no longer installed falls back to its package ID");
        ActivityEntry scan = Single(entries, ActivityKind.Scan, "Completed");
        Assert.AreEqual("42 seconds", scan.Facts.Single(fact => fact.Label == "Duration").Value);
        Assert.IsTrue(entries.All(entry => entry.Glyph.Length == 1), "every entry has an icon as well as its outcome label");
        Assert.IsTrue(Single(entries, ActivityKind.Scan, "Did not complete").IsProblem);
        ActivityEntry decision = entries.Single(entry => entry.Kind == ActivityKind.Decision);
        Assert.AreEqual("App widget: Pinned at version 1.0", decision.Title);
        Assert.AreEqual("Hold for now", decision.Facts.Single().Value);

        ActivityPagePresentation problems = ActivityPresenter.Build(entries, ActivityFilter.Problems, Now, Culture, Utc);
        Assert.AreEqual(2, problems.MatchingCount);
        Assert.IsTrue(problems.Days.SelectMany(day => day.Entries).All(entry => entry.IsProblem));
        Assert.AreEqual(ActivityFilter.All, ActivityPresenter.Build(entries, (ActivityFilter)42, Now, Culture, Utc).Filter);
        Assert.AreEqual("No activity matches this filter.", ActivityPresenter.Build([], ActivityFilter.All, Now, Culture, Utc).Summary);
    }

    [TestMethod]
    [Description("FR-POL-01–05: pins, ignores (indefinite or until a date) and mappings in one reversible view, with reasons and product names in accessible names.")]
    public void DecisionsAreOneReversibleViewWithReasonsAndProductNames()
    {
        InventoryRow[] rows = [Row("alpha", "Pinned", "1.0"), Row("beta", "Ignored", "1.0"), Row("gamma", "Current", "1.0")];
        string longReason = new('r', 600);
        StableDecision[] decisions =
        [
            new(Guid.NewGuid(), "alpha", "PinCurrentVersion", "1.0", Now.AddDays(-1), "Work laptop"),
            new(Guid.NewGuid(), "beta", "Ignore", "Indefinitely", Now.AddDays(-2)),
            new(Guid.NewGuid(), "beta", "Ignore", Now.AddDays(5).ToString("O"), Now.AddDays(-1), longReason, Now.AddDays(5)),
        ];
        ManualMapping[] mappings = [new(Guid.NewGuid(), "gamma", "winget", "Contoso.Gamma", Now.AddDays(-3), "Same app")];

        IReadOnlyList<DecisionRow> view = DecisionsPresenter.Build(decisions, mappings, rows, Culture, Utc);

        Assert.HasCount(4, view);
        DecisionRow pin = view.Single(row => row.Kind == DecisionKind.Pin);
        Assert.AreEqual("Pinned at version 1.0", pin.Title);
        Assert.AreEqual("Work laptop", pin.Reason);
        Assert.AreEqual("Remove pin", pin.RemoveLabel);
        Assert.AreEqual(PageFocusRestore.RemoveDecisionName("App alpha", "pin current version"), pin.RemoveAccessibleName);
        Assert.AreEqual("Ignored indefinitely", view.Single(row => row.Kind == DecisionKind.Ignore && row.Reason is null).Title);
        DecisionRow until = view.Single(row => row.Kind == DecisionKind.Ignore && row.Reason is not null);
        Assert.AreEqual("Ignored until 6 Nov 2026", until.Title);
        Assert.AreEqual(DecisionsPresenter.MaximumReasonLength, until.Reason!.Length, "reasons are bounded to 512 characters");
        DecisionRow mapping = view.Single(row => row.Kind == DecisionKind.Mapping);
        Assert.AreEqual(PageFocusRestore.RemoveManualMappingName("App gamma", "winget", "Contoso.Gamma"), mapping.RemoveAccessibleName);
        foreach (DecisionRow row in view)
        {
            StringAssert.StartsWith(row.RemoveAccessibleName, row.RemoveLabel, "label-in-name");
            StringAssert.Contains(row.RemoveAccessibleName, row.ProductName, "the product is named (Phase 1 focus rule)");
        }
        foreach (DecisionRow row in view)
            Assert.AreSame(row, PageFocusRestore.UniqueMatch(view.Where(other => other.RemoveAccessibleName == row.RemoveAccessibleName)),
                $"every Remove name is unique, including two ignores of one app (independent review L5): {row.RemoveAccessibleName}");
        Assert.AreEqual("Remove ignore indefinitely decision for App beta", view.Single(row => row.Kind == DecisionKind.Ignore && row.Reason is null).RemoveAccessibleName);
        Assert.AreEqual("Remove ignore until 6 Nov 2026 decision for App beta", until.RemoveAccessibleName);
        DecisionRow sourcePin = DecisionsPresenter.Build([new(Guid.NewGuid(), "alpha", "PinSourcePackage", "Contoso.Alpha", Now, null)], [], rows, Culture, Utc).Single();
        Assert.AreEqual("Remove pin source package decision for App alpha", sourcePin.RemoveAccessibleName);
        Assert.AreNotEqual(pin.RemoveAccessibleName, sourcePin.RemoveAccessibleName, "the two pin kinds of one app are told apart");
        Assert.AreEqual("Pins (2)", DecisionsPresenter.GroupHeading(DecisionKind.Pin, 2));
        Assert.IsTrue(view.All(row => row.Glyph.Length == 1));
    }

    private static ActivityEntry Single(IEnumerable<ActivityEntry> entries, ActivityKind kind, string outcome) =>
        entries.Single(entry => entry.Kind == kind && entry.OutcomeLabel == outcome);

    private static InventoryRow Row(string key, string status, string version) =>
        new(Guid.NewGuid(), $"App {key}", "Contoso", "winget", "User", version, status, "Application", [], key);

    private static UpdateDetail Detail(string key, string installed, string? candidate) =>
        new(key, installed, candidate, "winget", "Contoso.App", "High", "Exact match.", "winget: live result from today.", "X64", null,
            "Review the update.", [], [], []);

    private static SourceSnapshot Source(string id, SourceSnapshotState state, DateTimeOffset? lastSuccess, params string[] warnings) =>
        new(id, "1.0", lastSuccess, state, [], warnings);

    private static UpdateHistoryEntry History(string key, UpdateHistoryStatus status, DateTimeOffset at, Guid? job = null, int? exitCode = null,
        string diagnostic = "recorded", string target = "2.0", string installed = "1.0") =>
        new(Guid.NewGuid(), job ?? Guid.NewGuid(), Guid.NewGuid(), key, UpdateProvider.WinGet, "Contoso.App", installed, target, at, status, exitCode, diagnostic);
}
