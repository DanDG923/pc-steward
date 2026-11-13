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
    private static readonly string[] FilterLabels = ["All", "Update ready", "Check the match", "No update source", "Pinned, ignored & skipped"];
    private static readonly string[] ActivityFilterLabels = ["All", "Updates", "Scans", "Problems"];
    private static readonly string[] FreshnessWords = ["Local-only", "Live", "Cached", "Offline", "Unavailable"];
    private static readonly string[] OutcomeLabels = ["Verified", "Restart required", "Installed, not verified", "Result not verified", "Failed", "Not started"];
    private static readonly string[] AttentionKeys = ["failed", "restart"];
    private static readonly string[] LiveFacts = ["Live results from your update sources", "Scanned 5 hours ago", "12 apps on this PC"];
    private static readonly HomeRowKind[] LinesWithSomethingToSay = [HomeRowKind.CantUpdateYet, HomeRowKind.Decisions];
    private static readonly string[] ReadyOnHome = ["ready-1", "ready-2"];
    private static readonly string[] LocalOnlyFacts = ["Scanned 30 minutes ago", "Local-only: update checks are off."];
    private static readonly int[] FilterCounts = [9, 1, 1, 1, 3];
    private static readonly string[] UpdatesLabels = ["Update ready"];
    private static readonly string[] NeedsReviewLabels = ["Check the match"];
    private static readonly string[] PinnedIgnoredLabels = ["Pinned", "Ignored", "Skipped"];
    private static readonly string[] InstanceFactLabels = ["Found by", "Identity", "Version", "Scope", "Architecture"];
    private static readonly int[] ActivityCounts = [5, 2, 2, 2];
    private static readonly string[] ExplainerTitles = ["Pin", "Skip this version", "Ignore updates for this app", "Confirm package"];
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
        StringAssert.Contains(mixed.Sentence, "these are the results of the last check", "nothing failed and it is under a week old, so it reads calmly");
        StringAssert.Contains(mixed.Sentence, "last successful check was 2 days ago", "the age is the least fresh source's (independent review M1)");

        Assert.AreEqual(EvidenceFreshness.Offline, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now, "source:offline")], Now, Culture, Utc).Freshness);
        Assert.AreEqual(EvidenceFreshness.Unavailable, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Unavailable, null)], Now, Culture, Utc).Freshness);
    }

    [TestMethod]
    [Description("FR-UI-01/05/06; ADR-030 concept A; UI polish phase 2 (T-Home-Ready): one status sentence, every FR-UI-01 count once, rows that open filtered pages, and info bars for problems and restarts.")]
    public void HomeHasOneStatusSentenceCountsAndRowsThatOpenFilteredPages()
    {
        InventoryRow[] rows =
        [
            Row("ready-1", "VerifiedUpdate", "1.0"), Row("ready-2", "VerifiedUpdate", "1.0"),
            Row("review", "ReviewRequired", "1.0"), Row("unavailable", "FailedUnavailable", "1.0"),
            Row("current", "Current", "2.0"), Row("unmanaged", "Unmanaged", "1.0"),
            Row("pinned", "Pinned", "1.0"), Row("ignored", "Ignored", "1.0"), Row("skipped", "Skipped", "1.0"),
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
        Assert.AreEqual("2 updates ready · checked 10 minutes ago", home.Hero);
        Assert.AreEqual(StatusTone.Information, home.HeroTone);
        Assert.AreEqual("Live", home.Freshness.Word);
        CollectionAssert.AreEqual(LiveFacts, home.Facts.Select(fact => fact.Text).ToArray(),
            "freshness in words and the scan time are always shown (FR-UI-05, independent review L4)");
        Assert.IsTrue(home.Facts.All(fact => fact.Glyph.Length == 1), "every fact has an icon as well as text");

        Assert.AreEqual(HomeActionKind.UpdateAll, home.Primary.Kind, "updates are ready: the primary action opens one review of them all (UI review item 15)");
        Assert.AreEqual("Update all 2…", home.Primary.Label, "the same label as the Updates page's button");
        Assert.AreEqual(HomePresenter.UpdateAllHelp, home.Primary.HelpText);
        StringAssert.Contains(home.Primary.HelpText, "Nothing is installed until you confirm there.");
        CollectionAssert.AreEqual(ReadyOnHome, home.ReadyUpdates.Select(update => update.StableProductKey).ToArray(), "Home lists the ready updates themselves");
        CollectionAssert.AreEqual(ReadyOnHome, home.ReadyKeys.ToArray());
        Assert.AreEqual("1.0 → 1.1", home.ReadyUpdates[0].VersionText);
        Assert.AreEqual("1.1", home.ReadyUpdates[0].NewVersion);
        Assert.IsNull(home.MoreReadyText, "every ready update fits on Home");
        StringAssert.StartsWith(home.Primary.AccessibleName, home.Primary.Label, "label-in-name");
        Assert.AreEqual(HomeActionKind.CheckForUpdates, home.Secondary!.Kind);
        Assert.AreEqual("Check for updates", home.Secondary.Label);
        StringAssert.StartsWith(home.Secondary.AccessibleName, home.Secondary.Label);
        Assert.AreEqual("Scans this PC, then looks for updates. Nothing is installed.", home.Secondary.HelpText, "independent review L1: it says it scans as well as checks");

        Assert.AreEqual(new HomeCounts(rows.Length, 2, 1, 1, 4, 1, 1, 1, 1, 1, 1), home.Counts, "FR-UI-01 counts include pinned, ignored and failed; a later scan resolves an old attempt");

        // UI review 2026-11-13 (item 15): a line appears only when it has something to say. The failed update and the restart are in
        // the info bars, so they are not repeated as a line; Installed apps, Activity and Decisions are in the navigation pane.
        CollectionAssert.AreEqual(LinesWithSomethingToSay, home.Rows.Select(row => row.Kind).ToArray());
        Dictionary<HomeRowKind, HomeRow> glance = home.Rows.ToDictionary(row => row.Kind);
        Assert.AreEqual(new HomeRowTarget(ShellPage.Updates), glance[HomeRowKind.CantUpdateYet].Target, "the line opens the page that says why");
        Assert.AreEqual("Can't update yet", glance[HomeRowKind.CantUpdateYet].Title);
        Assert.AreEqual("App unavailable can't be updated yet. Updates says why.", glance[HomeRowKind.CantUpdateYet].Description);
        Assert.AreEqual(new HomeRowTarget(ShellPage.Decisions), glance[HomeRowKind.Decisions].Target);
        Assert.AreEqual("1 pinned, 1 ignored and 1 skipped. You can undo any of them.", glance[HomeRowKind.Decisions].Description);
        Assert.IsTrue(home.Rows.All(row => row.Tone == StatusTone.Neutral), "colour is used only for status");
        Assert.IsFalse(glance.ContainsKey(HomeRowKind.NeedsReview), "a possible match with nothing newer on offer is not a question on Home");
        Assert.AreEqual(3, home.Recent.Count, "the last few things PC Steward did");
        Assert.IsTrue(home.Recent.Count <= HomePresenter.MaximumRecentLines);
        StringAssert.EndsWith(home.Recent[0].Line, " · 1 hour ago");
        StringAssert.Contains(home.Recent[0].Text, "Failed");
        foreach (HomeRow row in home.Rows)
        {
            StringAssert.StartsWith(row.AccessibleName, row.Title, "each row's accessible name starts with its visible title");
            StringAssert.Contains(row.AccessibleName, row.Description);
            Assert.AreEqual(1, row.Glyph.Length, "every row has an icon as well as text");
            Assert.AreNotEqual(StatusTone.Critical, row.Tone, "no row is red");
            foreach (string forbidden in new[] { "unsafe", "Unmanaged", "danger" })
                Assert.IsFalse(row.Description.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"{row.Kind}: {forbidden}");
        }

        CollectionAssert.AreEqual(AttentionKeys, home.Attention.Select(item => item.StableProductKey).ToArray(), "compact attention view: failed then restart, resolved attempts dropped");
        Assert.AreEqual("Failed", home.Attention[0].OutcomeLabel);
        Assert.AreEqual("Restart required", home.Attention[1].OutcomeLabel);
        Assert.IsTrue(home.Attention.Count <= HomePresenter.MaximumAttentionItems);

        Assert.HasCount(2, home.Notices);
        HomeNotice problem = home.Notices[0];
        Assert.AreEqual(HomeNoticeKind.NeedsAttention, problem.Kind);
        Assert.AreEqual(StatusTone.Attention, problem.Tone);
        StringAssert.StartsWith(problem.Message, "App failed: Failed.");
        Assert.AreEqual(ActivityFilter.Problems, problem.ActivityFilter);
        Assert.IsTrue(ActivityPresenter.Matches(problem.ActivityFilter, activity.Single(entry => entry.OutcomeLabel == "Failed")));
        StringAssert.StartsWith(problem.ActionAccessibleName, problem.ActionLabel, "label-in-name");
        HomeNotice restart = home.Notices[1];
        Assert.AreEqual(HomeNoticeKind.RestartRequired, restart.Kind);
        Assert.AreEqual(StatusTone.Information, restart.Tone, "Restart required is never alarming (final review L3)");
        Assert.AreEqual("Windows needs to restart to finish updating App restart. PC Steward won't restart your PC.", restart.Message);
        Assert.IsTrue(ActivityPresenter.Matches(restart.ActivityFilter, activity.Single(entry => entry.OutcomeLabel == "Restart required")), "independent review L3");
        StringAssert.StartsWith(restart.ActionAccessibleName, restart.ActionLabel, "label-in-name");
    }

    [TestMethod]
    [Description("UI polish phase 2 (T-Home): local-only is calm. The primary action turns on update checks, the other is a scan only, and nothing claims a check that didn't happen.")]
    public void HomeWhenLocalOnlyOffersToTurnOnUpdateChecksCalmly()
    {
        InventoryRow[] rows = [Row("a", "Unmanaged", "1.0"), Row("b", "Unmanaged", "2.0"), Row("ready", "VerifiedUpdate", "1.0")];
        HomePresentation home = HomePresenter.Build(new HomeInputs(rows,
            new Dictionary<string, UpdateDetail>(StringComparer.Ordinal) { ["ready"] = Detail("ready", "1.0", "2.0") }, false, [], [], [], [], Now.AddMinutes(-30), Now), Culture, Utc);

        Assert.AreEqual("3 apps found · update checks are off", home.Hero);
        Assert.AreEqual(0, home.Counts.Verified, "the ready count is the Updates page's Ready group, which fails closed with sources off");
        CollectionAssert.AreEqual(LocalOnlyFacts, home.Facts.Select(fact => fact.Text).ToArray());
        Assert.IsTrue(home.Facts.All(fact => fact.Tone == StatusTone.Neutral), "an off-by-choice state is never a warning");
        Assert.AreNotEqual(StatusTone.Attention, home.HeroTone);

        Assert.AreEqual(HomeActionKind.TurnOnUpdateChecks, home.Primary.Kind);
        Assert.AreEqual("Turn on update checks…", home.Primary.Label);
        StringAssert.StartsWith(home.Primary.AccessibleName, "Turn on update checks", "label-in-name, without the ellipsis");
        StringAssert.Contains(home.Primary.HelpText, "asks you first");
        Assert.AreEqual(HomeActionKind.ScanAgain, home.Secondary!.Kind, "with sources off the other action is a scan only (independent review L1)");
        Assert.AreEqual("Scan again", home.Secondary.Label);
        StringAssert.StartsWith(home.Secondary.AccessibleName, home.Secondary.Label);
        Assert.AreEqual(HomePresenter.ScanAgainHelp, home.Secondary.HelpText);

        Assert.IsEmpty(home.Rows, "a calm local-only Home has no lines: nothing has anything to say");
        Assert.IsEmpty(home.ReadyUpdates, "with update checks off nothing is offered");
        Assert.IsEmpty(home.Recent);
        Assert.IsEmpty(home.Notices);

        HomePresentation allUnmanaged = HomePresenter.Build(new HomeInputs([Row("a", "Unmanaged", "1.0"), Row("b", StatusVocabulary.InstalledKey, "1.0")],
            new Dictionary<string, UpdateDetail>(), false, [], [], [], [], Now.AddMinutes(-30), Now), Culture, Utc);
        Assert.IsEmpty(allUnmanaged.Rows);
        Assert.AreEqual(HomeActionKind.TurnOnUpdateChecks, allUnmanaged.Primary.Kind);
    }

    [TestMethod]
    [Description("FR-UI-01/06, FR-HIS-01 (UI review 2026-11-13, items 15 and 17): the bar about PC Steward's own update goes once it is closed or a day old, but a problem always shows; Activity says what it is in one line.")]
    public void TheOwnUpdateBarGoesAwayAndActivitySaysWhatItIs()
    {
        DateTimeOffset now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        Assert.IsTrue(HomePresenter.ShowsOwnUpdateBar(succeeded: true, closed: false, now.AddHours(-2), now));
        Assert.IsFalse(HomePresenter.ShowsOwnUpdateBar(succeeded: true, closed: true, now.AddHours(-2), now), "closed by the owner");
        Assert.IsFalse(HomePresenter.ShowsOwnUpdateBar(succeeded: true, closed: false, now.AddDays(-1), now), "a day old");
        Assert.IsTrue(HomePresenter.ShowsOwnUpdateBar(succeeded: true, closed: false, null, now), "shown when its time is not known");
        Assert.IsTrue(HomePresenter.ShowsOwnUpdateBar(succeeded: false, closed: true, now.AddDays(-9), now), "a problem is always shown");

        Assert.AreEqual("What PC Steward did on this PC: its scans, the updates it ran and the decisions you saved.", ActivityPresenter.PageDescription);
        Assert.AreEqual("Clear history or other data", ActivityPresenter.DataLinkLabel);
        StringAssert.StartsWith(ActivityPresenter.ExpandHint, "Open a record");
    }

    [TestMethod]
    [Description("FR-UI-01/05: Home before the first scan, and with sources on but nothing ready, never claims a check that didn't happen.")]
    public void HomeWithoutInventoryOrReadyUpdatesIsHonest()
    {
        HomePresentation empty = HomePresenter.Build(new HomeInputs([], new Dictionary<string, UpdateDetail>(), false, [], [], [], [], null, Now), Culture, Utc);
        Assert.IsFalse(empty.HasInventory);
        Assert.AreEqual("PC Steward hasn't looked at this PC yet", empty.Hero);
        Assert.AreEqual(EvidenceFreshness.LocalOnly, empty.Freshness.Freshness);
        Assert.IsEmpty(empty.Rows);
        Assert.IsEmpty(empty.Notices);

        InventoryRow[] rows = [Row("current", "Current", "1.0")];
        HomePresentation notChecked = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true, [], [], [], [], Now.AddMinutes(-30), Now), Culture, Utc);
        Assert.AreEqual("No updates ready · sources not checked yet", notChecked.Hero);
        Assert.AreEqual(HomeActionKind.CheckForUpdates, notChecked.Primary.Kind, "nothing is ready: the primary action checks, it never offers an empty review");
        Assert.IsNull(notChecked.Secondary);
        Assert.AreEqual("Local-only: PC Steward hasn't looked for updates yet.", notChecked.Facts[0].Text);
        Assert.AreNotEqual(StatusTone.Success, notChecked.HeroTone, "an unchecked state never reads as all clear");

        HomePresentation live = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-3))], [], [], [], Now.AddMinutes(-30), Now), Culture, Utc);
        Assert.AreEqual("No updates ready · checked 3 minutes ago", live.Hero);
        Assert.AreEqual(StatusTone.Success, live.HeroTone);
        Assert.IsEmpty(live.Rows, "nothing needs saying when every app is up to date");
        Assert.AreEqual("1 app on this PC", live.Facts[2].Text);
    }

    [TestMethod]
    [Description("FR-UI-05 (independent review M1, L4, L7): the freshness age and the hero's source age come from the least fresh source; Live lasts only the live window.")]
    public void FreshnessAgeComesFromTheLeastFreshSourceAndLiveExpires()
    {
        FreshnessPresentation unavailable = FreshnessPresenter.Describe(true,
            [Source("winget", SourceSnapshotState.Live, Now), Source("msstore", SourceSnapshotState.Unavailable, Now.AddDays(-9))], Now, Culture, Utc);
        Assert.AreEqual("Unavailable", unavailable.Word);
        Assert.AreEqual("Unavailable: an update source couldn't complete its check. The least fresh source's last successful check was on 23 Oct 2026.", unavailable.Sentence,
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
        Assert.AreEqual("No updates ready · checked 5 days ago", home.Hero);
        Assert.AreEqual("Scanned 10 minutes ago", home.Facts[1].Text);
        Assert.AreEqual("Unavailable", home.Freshness.Word);
        HomePresentation neverChecked = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Live, Now.AddMinutes(-2)), Source("msstore", SourceSnapshotState.Unavailable, null)],
            [], [], [], Now.AddMinutes(-10), Now), Culture, Utc);
        Assert.AreEqual("No updates ready · a source hasn't been checked successfully", neverChecked.Hero);
    }

    [TestMethod]
    [Description("FR-UI-05 (owner report 2026-11-13, release 0.1.15; owner decision the same day): a check under 15 minutes old reads as up to date however its results were loaded; older results read calmly; the warning is kept for a failed check and for results over a week old.")]
    public void RecentResultsReadAsLiveAndOnlyAFailedOrWeekOldCheckWarns()
    {
        // The owner's case: an update finished, then a scan reloaded the 11-minute-old results from this PC's store, where every
        // result is marked cached. Home said "No updates ready" beside a quiet "Cached" line and Updates showed a yellow warning.
        FreshnessPresentation reloaded = FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now.AddMinutes(-11))], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.Live, reloaded.Freshness);
        Assert.AreEqual("Live: update sources were checked 11 minutes ago.", reloaded.Sentence);
        Assert.AreEqual(StatusTone.Information, reloaded.Tone);
        Assert.AreEqual(EvidenceFreshness.Live, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now.Add(-FreshnessPresenter.LiveWindow))], Now, Culture, Utc).Freshness);

        // Past the live window it is Cached, said calmly: nothing went wrong.
        FreshnessPresentation hours = FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now.AddHours(-3))], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.Cached, hours.Freshness);
        Assert.AreEqual("Cached: these are the results of the last check. The last successful check was 3 hours ago.", hours.Sentence);
        Assert.AreEqual(StatusTone.Information, hours.Tone);
        Assert.AreEqual(StatusTone.Information, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Live, Now.AddDays(-6))], Now, Culture, Utc).Tone, "a live result that aged is the same");

        // A warning: older than a week, a check that failed and fell back to older results, or a source that never succeeded.
        Assert.AreEqual(TimeSpan.FromDays(7), FreshnessPresenter.StaleAfter);
        FreshnessPresentation week = FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now.AddDays(-8))], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.Cached, week.Freshness);
        StringAssert.StartsWith(week.Sentence, "Cached: results may be out of date.");
        Assert.AreEqual(StatusTone.Attention, week.Tone);
        Assert.AreEqual(StatusTone.Information, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now.AddDays(-7))], Now, Culture, Utc).Tone, "exactly a week is not yet a warning");
        FreshnessPresentation failed = FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now.AddMinutes(-11), "source:timeout", "source:stale")], Now, Culture, Utc);
        Assert.AreEqual(EvidenceFreshness.Cached, failed.Freshness, "a check that just failed is never Live, however recent the results it fell back to");
        StringAssert.StartsWith(failed.Sentence, "Cached: results may be out of date.");
        Assert.AreEqual(StatusTone.Attention, failed.Tone);
        Assert.AreEqual(StatusTone.Attention, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Live, null)], Now, Culture, Utc).Tone, "never succeeded");
        Assert.AreEqual(StatusTone.Attention, FreshnessPresenter.Describe(true, [Source("winget", SourceSnapshotState.Cached, Now, "source:offline")], Now, Culture, Utc).Tone, "offline is still a warning");
        // One recent source and one that failed: the worse one decides.
        Assert.AreEqual(StatusTone.Attention, FreshnessPresenter.Describe(true,
            [Source("winget", SourceSnapshotState.Cached, Now.AddMinutes(-2)), Source("chocolatey", SourceSnapshotState.Cached, Now.AddHours(-2), "source:unavailable", "source:stale")], Now, Culture, Utc).Tone);

        // Home says the same thing as Updates: a recent reload is the green "up to date" state, with no cached line.
        InventoryRow[] rows = [Row("current", "Current", "1.0")];
        HomePresentation home = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Cached, Now.AddMinutes(-11))], [], [], [], Now, Now), Culture, Utc);
        Assert.AreEqual("No updates ready · checked 11 minutes ago", home.Hero);
        Assert.AreEqual(StatusTone.Success, home.HeroTone);
        Assert.AreEqual("Live results from your update sources", home.Facts[0].Text);
        HomePresentation older = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Cached, Now.AddHours(-3))], [], [], [], Now, Now), Culture, Utc);
        Assert.AreEqual(hours.Sentence, older.Facts[0].Text);
        Assert.AreEqual(StatusTone.Neutral, older.Facts[0].Tone, "a healthy cache is a plain fact on Home");
        HomePresentation stale = HomePresenter.Build(new HomeInputs(rows, new Dictionary<string, UpdateDetail>(), true,
            [Source("winget", SourceSnapshotState.Cached, Now.AddDays(-8))], [], [], [], Now, Now), Culture, Utc);
        Assert.AreEqual(StatusTone.Attention, stale.Facts[0].Tone, "a week-old cache is called out on Home as it is on Updates");
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
        // Task 51: installers often register the target version before the restart that finishes them, so seeing it isn't enough.
        Assert.IsTrue(UpdateJobHistory.RestartPending(restart, "2.0.0", Now), "the target is installed but Windows hasn't restarted");
        Assert.IsTrue(UpdateJobHistory.RestartPending(restart, "2.0.0", Now, restart.EndedAt.AddMinutes(-5)), "the last restart was before the attempt");
        Assert.IsFalse(UpdateJobHistory.RestartPending(restart, "2.0.0", Now, restart.EndedAt.AddMinutes(5)), "Windows restarted after the attempt");
        Assert.IsFalse(UpdateJobHistory.RestartPending(restart, "1.0", Now, restart.EndedAt.AddSeconds(1)), "a restart closes it whatever the version");
        Assert.IsFalse(UpdateJobHistory.RestartPending(restart, "2.1", Now), "moved on past the target");
        Assert.IsFalse(UpdateJobHistory.RestartPending(restart, null, Now));
        Assert.IsFalse(UpdateJobHistory.RestartPending(failed, "1.0", Now), "only a restart-required outcome is a pending restart");
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
        Assert.AreEqual(ActivityFilter.Problems, home.Notices.Single().ActivityFilter, "no restart listed: the problems filter is enough");
        Assert.AreEqual(HomePresenter.AttentionProblemsLinkName, home.Notices.Single().ActionAccessibleName);
        Assert.IsFalse(activity.Any(entry => entry.OutcomeLabel == "Verified"), "resolution never relabels an attempt as Verified");
        Assert.AreEqual(4, activity.Count(entry => entry.IsProblem), "Activity keeps every recorded outcome");

        // Task 51: a restart-required update with its target already registered stays on Home until Windows has restarted.
        InventoryRow[] waiting = [Row("waiting", "Current", "2.0")];
        UpdateJobRecord[] restartJobs = [.. UpdateJobHistory.Group([History("waiting", UpdateHistoryStatus.RestartRequired, Now.AddHours(-1))])];
        IReadOnlyList<ActivityEntry> restartActivity = ActivityPresenter.Entries(restartJobs, [], [], [], waiting, Culture, Utc);
        HomePresentation notRestarted = HomePresenter.Build(new HomeInputs(waiting, new Dictionary<string, UpdateDetail>(), false, [], [], restartJobs,
            restartActivity, Now.AddMinutes(-30), Now, Now.AddDays(-1)), Culture, Utc);
        Assert.AreEqual(1, notRestarted.Counts.RestartRequired);
        Assert.AreEqual(HomeNoticeKind.RestartRequired, notRestarted.Notices.Single().Kind);
        HomePresentation restarted = HomePresenter.Build(new HomeInputs(waiting, new Dictionary<string, UpdateDetail>(), false, [], [], restartJobs,
            restartActivity, Now.AddMinutes(-30), Now, Now.AddMinutes(-10)), Culture, Utc);
        Assert.AreEqual(0, restarted.Counts.RestartRequired);
        Assert.IsEmpty(restarted.Notices);
    }

    [TestMethod]
    [Description("FR-UI-01; AC-UX-03; audit P-L7: saved filters use the status vocabulary and 'All' counts every app.")]
    public void InstalledAppsSavedFiltersCountEveryAppAndUseTheVocabulary()
    {
        InventoryRow[] rows =
        [
            Row("a", "VerifiedUpdate", "1"), Row("b", "ReviewRequired", "1"), Row("c", "FailedUnavailable", "1"),
            Row("d", "Unmanaged", "1"), Row("e", "Pinned", "1"), Row("f", "Ignored", "1"), Row("g", "Current", "1"),
            Row("h", StatusVocabulary.InstalledKey, "1"), Row("i", "Skipped", "1")
        ];
        IReadOnlyList<InstalledAppsFilterOption> options = InstalledAppsFilters.Options(rows);
        CollectionAssert.AreEqual(FilterLabels, options.Select(option => option.Label).ToArray());
        CollectionAssert.AreEqual(FilterCounts, options.Select(option => option.Count).ToArray());
        Assert.AreEqual("All (9)", options[0].DisplayLabel, "All includes current, pinned, ignored and source-not-checked rows");
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
        Assert.AreEqual("Found in the latest scan", timeline[1].Title);
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

    [TestMethod]
    [Description("UI polish phase 4 (approved targets T-Installed, T-Activity, T-Decisions): calm page copy, the list count, headings with the outcome in words and the decision explainers.")]
    public void PhaseFourPagesStateFactsCalmlyAndInWords()
    {
        Assert.AreEqual("3 apps", InstalledAppsFilters.CountText(3, 3));
        Assert.AreEqual("1 of 3 apps", InstalledAppsFilters.CountText(1, 3));
        StringAssert.StartsWith(InstalledAppsFilters.NoUpdateSourceNote, "No update source isn't a safety warning.");

        InventoryRow[] rows = [Row("widget", "Current", "1.0")];
        ScanRun[] scans = [new(Guid.NewGuid(), Now.AddHours(-3), Now.AddHours(-3).AddSeconds(42), "Completed", Guid.NewGuid())];
        StableDecision[] decisions = [new(Guid.NewGuid(), "widget", "PinCurrentVersion", "1.0", Now.AddDays(-4), null)];
        IReadOnlyList<ActivityEntry> entries = ActivityPresenter.Entries([], scans, decisions, [], rows, Culture, Utc);
        ActivityEntry scan = entries.Single(entry => entry.Kind == ActivityKind.Scan);
        Assert.AreEqual($"{scan.Title}: Completed", scan.Heading, "the outcome is in the heading, not only in the icon's colour");
        ActivityEntry decision = entries.Single(entry => entry.Kind == ActivityKind.Decision);
        Assert.AreEqual(decision.Title, decision.Heading, "a decision's title already says what it is");

        CollectionAssert.AreEqual(ExplainerTitles,
            DecisionsPresenter.Explainers.Select(explainer => explainer.Title).ToArray());
        DecisionRow[] glyphs = [.. DecisionsPresenter.Build(
            [new(Guid.NewGuid(), "widget", "PinCurrentVersion", "1.0", Now, null), new(Guid.NewGuid(), "widget", UserDecisionService.SkipVersionKind, "2.0", Now, null),
             new(Guid.NewGuid(), "widget", "Ignore", "Indefinitely", Now, null)],
            [new(Guid.NewGuid(), "widget", "winget", "Contoso.Widget", Now, null)], rows, Culture, Utc)];
        CollectionAssert.AreEquivalent(glyphs.Select(row => row.Glyph).ToArray(), DecisionsPresenter.Explainers.Select(explainer => explainer.Glyph).ToArray(),
            "each explainer uses the same icon as the decision rows of its kind");
        StringAssert.Contains(DecisionsPresenter.PageDescription, "reversible");
        StringAssert.Contains(DecisionsPresenter.RemoveNote, "Nothing is updated");
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
