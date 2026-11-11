using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>How fresh the displayed update evidence is, in the five words of FR-UI-05 / ADR-030 foundation 8.</summary>
public enum EvidenceFreshness
{
    LocalOnly,
    Live,
    Cached,
    Offline,
    Unavailable
}

/// <summary>Freshness in words plus a one-sentence explanation.</summary>
public sealed record FreshnessPresentation(EvidenceFreshness Freshness, string Word, string Sentence, StatusTone Tone);

/// <summary>The oldest last-successful check among the checked sources, and whether any checked source has never succeeded.</summary>
public readonly record struct LeastFreshSuccess(DateTimeOffset? OldestSuccessAt, bool AnyNeverSucceeded, int SourceCount);

/// <summary>
/// FR-UI-05 (Task 73 Phase 3): states freshness in words — live, cached, local-only, offline or unavailable — never by colour alone.
/// When sources disagree the least fresh state is shown, and the age quoted is the least fresh source's (independent review M1), so
/// the summary never overstates freshness. A source reads as Live only while its last success is within <see cref="LiveWindow"/> of
/// the render-time clock (independent review L7); after that it reads as Cached. Presentation only.
/// </summary>
public static class FreshnessPresenter
{
    /// <summary>
    /// How long a live source result reads as "Live" on screen. No domain or application freshness constant exists for display, so
    /// this presentation-only window is 15 minutes; it changes only the word shown, never what may be updated.
    /// </summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(15);

    public static string Word(EvidenceFreshness freshness) => freshness switch
    {
        EvidenceFreshness.Live => "Live",
        EvidenceFreshness.Cached => "Cached",
        EvidenceFreshness.Offline => "Offline",
        EvidenceFreshness.Unavailable => "Unavailable",
        _ => "Local-only"
    };

    /// <summary>The sources that were checked at all (anything but local-only).</summary>
    public static SourceSnapshot[] Checked(IEnumerable<SourceSnapshot> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return [.. sources.Where(source => source.State != SourceSnapshotState.LocalOnly)];
    }

    /// <summary>The least fresh last success among <paramref name="checkedSources"/>; a source that never succeeded is reported as such.</summary>
    public static LeastFreshSuccess LeastFresh(IReadOnlyCollection<SourceSnapshot> checkedSources)
    {
        ArgumentNullException.ThrowIfNull(checkedSources);
        DateTimeOffset? oldest = checkedSources.Select(source => source.LastSuccessAt).Where(at => at is not null).Min();
        return new(oldest, checkedSources.Any(source => source.LastSuccessAt is null), checkedSources.Count);
    }

    public static FreshnessPresentation Describe(bool sourcesEnabled, IReadOnlyCollection<SourceSnapshot> sources, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        SourceSnapshot[] checkedSources = Checked(sources);
        if (!sourcesEnabled || checkedSources.Length == 0)
        {
            return new(EvidenceFreshness.LocalOnly, Word(EvidenceFreshness.LocalOnly),
                sourcesEnabled ? "Local-only: no package source has been checked yet." : "Local-only: no package source is on.",
                StatusTone.Neutral);
        }
        EvidenceFreshness worst = checkedSources.Select(source => Of(source, now)).Max();
        LeastFreshSuccess least = LeastFresh(checkedSources);
        string age = least.OldestSuccessAt is { } at ? DisplayFormat.Relative(at, now, culture, zone) : "at an unknown time";
        string lastCheck = least.AnyNeverSucceeded
            ? (checkedSources.Length == 1 ? "It has never completed a successful check." : "One source has never completed a successful check.")
            : checkedSources.Length == 1 ? $"The last successful check was {age}." : $"The least fresh source's last successful check was {age}.";
        string sentence = worst switch
        {
            EvidenceFreshness.Live => $"Live: package sources were checked {age}.",
            EvidenceFreshness.Cached => $"Cached: results may be out of date. {lastCheck}",
            EvidenceFreshness.Offline => $"Offline: a package source couldn't be reached. {lastCheck}",
            _ => $"Unavailable: a package source couldn't complete its check. {lastCheck}"
        };
        return new(worst, Word(worst), sentence, worst == EvidenceFreshness.Live ? StatusTone.Information : StatusTone.Attention);
    }

    private static EvidenceFreshness Of(SourceSnapshot source, DateTimeOffset now) => source.State switch
    {
        SourceSnapshotState.Live when source.LastSuccessAt is { } at && now - at <= LiveWindow => EvidenceFreshness.Live,
        SourceSnapshotState.Live => EvidenceFreshness.Cached,
        SourceSnapshotState.Cached when source.Warnings.Contains("source:offline", StringComparer.Ordinal) => EvidenceFreshness.Offline,
        SourceSnapshotState.Cached => EvidenceFreshness.Cached,
        SourceSnapshotState.Unavailable when source.Warnings.Contains("source:offline", StringComparer.Ordinal) => EvidenceFreshness.Offline,
        SourceSnapshotState.Unavailable => EvidenceFreshness.Unavailable,
        _ => EvidenceFreshness.LocalOnly
    };
}

/// <summary>Where a Home row leads: a page, plus the saved filter it opens with.</summary>
public sealed record HomeRowTarget(ShellPage Page, InstalledAppsFilter? AppsFilter = null, ActivityFilter? ActivityFilter = null);

/// <summary>The "At a glance" rows on Home (UI polish phase 2, approved targets T-Home and T-Home-Ready).</summary>
public enum HomeRowKind
{
    Updates,
    NeedsReview,
    InstalledApps,
    RestartsAndChecks,
    Decisions,
    RecentActivity
}

/// <summary>
/// One "At a glance" row: an icon, a title and one plain sentence, and the page it opens. It is a real button: its
/// <see cref="AccessibleName"/> starts with the title, then says what the row says and where it leads.
/// </summary>
public sealed record HomeRow(HomeRowKind Kind, string Title, string Description, string Glyph, StatusTone Tone, HomeRowTarget Target)
{
    public string AccessibleName => $"{Title}: {Description} Opens {ShellNavigation.TitleOf(Target.Page)}.";
}

/// <summary>What a Home action button does. None of them selects, approves, elevates or installs anything.</summary>
public enum HomeActionKind
{
    /// <summary>Opens the existing package-source consent; turning checks on starts nothing by itself.</summary>
    TurnOnUpdateChecks,
    /// <summary>Opens the Updates page.</summary>
    ReviewUpdates,
    /// <summary>A read-only scan, then a refresh of the enabled sources.</summary>
    CheckForUpdates,
    /// <summary>A read-only scan only (sources are off).</summary>
    ScanAgain
}

/// <summary>A Home action: its label, an accessible name that starts with the label, and help text for its tooltip.</summary>
public sealed record HomeAction(HomeActionKind Kind, string Label, string AccessibleName, string HelpText);

/// <summary>One fact under the status sentence: an icon and a short phrase. The text carries the meaning, never the colour.</summary>
public sealed record HomeFact(string Glyph, string Text, StatusTone Tone);

/// <summary>The kinds of message Home shows above the page when something needs the owner.</summary>
public enum HomeNoticeKind
{
    NeedsAttention,
    RestartRequired
}

/// <summary>
/// FR-UI-06: an info bar at the top of Home for updates that failed or couldn't be verified, or that wait for a restart. It names
/// the apps and opens Activity with a filter that contains every app it names.
/// </summary>
public sealed record HomeNotice(HomeNoticeKind Kind, string Title, string Message, StatusTone Tone, string ActionLabel, string ActionAccessibleName, ActivityFilter ActivityFilter);

/// <summary>One update attempt in the compact attention view (FR-UI-06).</summary>
public sealed record HomeAttentionItem(string StableProductKey, string AppName, string OutcomeLabel, string Glyph, StatusTone Tone, string Summary, string When)
{
    public string AccessibleName => $"{AppName}: {OutcomeLabel}. {Summary} {When}";
}

/// <summary>The FR-UI-01 counts. Each one appears once on Home, in the status sentence or a row, so nothing is restated (audit P-M4).</summary>
public sealed record HomeCounts(int Installed, int Verified, int ReviewRequired, int SourceUnavailable, int Current, int Unmanaged, int Pinned, int Ignored, int Failed, int RestartRequired, int Skipped = 0, int NotChecked = 0);

/// <summary>Everything Home shows.</summary>
public sealed record HomePresentation(
    bool HasInventory,
    string Hero,
    string HeroGlyph,
    StatusTone HeroTone,
    IReadOnlyList<HomeFact> Facts,
    FreshnessPresentation Freshness,
    HomeAction Primary,
    HomeAction? Secondary,
    HomeCounts Counts,
    IReadOnlyList<HomeRow> Rows,
    IReadOnlyList<HomeNotice> Notices,
    IReadOnlyList<HomeAttentionItem> Attention);

/// <summary>Inputs for <see cref="HomePresenter.Build"/>; all are existing presentation state.</summary>
public sealed record HomeInputs(
    IReadOnlyList<InventoryRow> Rows,
    IReadOnlyDictionary<string, UpdateDetail> Details,
    bool SourcesEnabled,
    IReadOnlyCollection<SourceSnapshot> Sources,
    IReadOnlyCollection<StableDecision> Decisions,
    IReadOnlyCollection<UpdateJobRecord> Jobs,
    IReadOnlyList<ActivityEntry> Activity,
    DateTimeOffset? LastScanAt,
    DateTimeOffset Now);

/// <summary>
/// FR-UI-01/05/06 (ADR-030 concept A; UI polish phase 2, approved targets T-Home and T-Home-Ready): Home is one status card (an icon,
/// one sentence, facts with icons, and the action that fits the state), info bars for updates that need the owner, and an "At a
/// glance" list of plain-English rows that open filtered pages. The primary action follows the state: "Turn on update checks…" when
/// local-only, "Review updates" when updates are ready, otherwise "Check for updates". The ready count is the Updates page's Ready
/// group, so Home never promises more than Updates can review. Presentation only: nothing here starts, approves or elevates.
/// </summary>
public static class HomePresenter
{
    /// <summary>The most items in the compact attention view.</summary>
    public const int MaximumAttentionItems = 5;

    public const string TurnOnUpdateChecksLabel = "Turn on update checks…";

    public const string TurnOnUpdateChecksHelp =
        "Shows what WinGet and Chocolatey would receive and asks you first. Turning checks on doesn't scan, check or install anything by itself.";

    public const string CheckForUpdatesHelp =
        "Scans this PC, then checks your enabled update sources. It never asks for administrator permission and never installs anything.";

    /// <summary>The scan-only action's help text when package sources are off (independent review L1).</summary>
    public const string ScanAgainHelp =
        "Reads the apps installed on this PC again. Package sources are off, so no update source is checked. It never asks for administrator permission and never installs anything.";

    public const string ReviewUpdatesHelp = "Opens Updates, where you choose what to review. Nothing is installed until you confirm it.";

    /// <summary>The footer line on Home (approved targets).</summary>
    public const string Reassurance = "Scanning and checking never ask for administrator permission. Windows asks only when you start an update that needs it.";

    public const string AttentionProblemsLinkName = "View in Activity: update problems";
    public const string AttentionUpdatesLinkName = "View in Activity: updates, including restarts and problems";
    public const string RestartDetailsLinkName = "Details: updates waiting for a restart, in Activity";

    public static HomePresentation Build(HomeInputs inputs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        IReadOnlyList<InventoryRow> rows = inputs.Rows;
        int ready = UpdatesPagePresenter.Build(rows, inputs.Details, inputs.SourcesEnabled, inputs.Decisions, null, inputs.Now).Ready.Count;
        Dictionary<string, string> installed = rows.GroupBy(row => row.StableProductKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Version, StringComparer.Ordinal);
        UpdateJobRecord[] latest = [.. UpdateJobHistory.LatestPerProduct(inputs.Jobs).Values.OrderByDescending(job => job.EndedAt)];
        UpdateJobRecord[] attention = latest.Where(job => UpdateJobHistory.NeedsAttention(job, installed.GetValueOrDefault(job.StableProductKey), inputs.LastScanAt)).ToArray();
        UpdateJobRecord[] restart = latest.Where(job => UpdateJobHistory.RestartPending(job, installed.GetValueOrDefault(job.StableProductKey), inputs.LastScanAt)).ToArray();
        int Count(string status) => rows.Count(row => row.Status == status);
        HomeCounts counts = new(rows.Count, ready, Count(nameof(UpdateIntelligenceStatus.ReviewRequired)), Count(nameof(UpdateIntelligenceStatus.FailedUnavailable)),
            Count(nameof(UpdateIntelligenceStatus.Current)), Count(nameof(UpdateIntelligenceStatus.Unmanaged)), Count(nameof(UpdateIntelligenceStatus.Pinned)),
            Count(nameof(UpdateIntelligenceStatus.Ignored)), attention.Length, restart.Length, Count(nameof(UpdateIntelligenceStatus.Skipped)), Count(StatusVocabulary.InstalledKey));
        FreshnessPresentation freshness = FreshnessPresenter.Describe(inputs.SourcesEnabled, inputs.Sources, inputs.Now, culture, zone);
        bool hasInventory = inputs.LastScanAt is not null || rows.Count != 0;

        // FR-UI-05 (independent review L4, M1): the card always states when this PC was last scanned and, with sources on, the least
        // fresh source's last successful check (the same age the freshness sentence uses), and freshness in words.
        HomeFact scanned = new("", inputs.LastScanAt is { } scanAt ? $"Scanned {DisplayFormat.Relative(scanAt, inputs.Now, culture, zone)}" : "Scan time unknown", StatusTone.Neutral);
        string hero;
        string heroGlyph;
        StatusTone heroTone;
        HomeFact[] facts;
        HomeAction primary;
        HomeAction? secondary;
        HomeAction checkForUpdates = new(HomeActionKind.CheckForUpdates, "Check for updates", "Check for updates without administrator permission", CheckForUpdatesHelp);
        if (!hasInventory)
        {
            hero = "PC Steward hasn't looked at this PC yet";
            (heroGlyph, heroTone) = ("", StatusTone.Information);
            facts = [];
            primary = new(HomeActionKind.ScanAgain, "Start local inventory", "Start local inventory (read-only scan of this PC)", ScanAgainHelp);
            secondary = null;
        }
        else if (!inputs.SourcesEnabled)
        {
            // Local-only is the owner's choice, so it reads calmly: a neutral fact with a lock, never a warning.
            hero = $"{DisplayFormat.Plural(rows.Count, "app")} found · update checks are off";
            (heroGlyph, heroTone) = ("", StatusTone.Information);
            facts = [scanned, new("", freshness.Sentence, StatusTone.Neutral)];
            primary = new(HomeActionKind.TurnOnUpdateChecks, TurnOnUpdateChecksLabel, "Turn on update checks: shows what is shared and asks you first", TurnOnUpdateChecksHelp);
            // Independent review L1: with sources off the other action is a scan only, and says so.
            secondary = new(HomeActionKind.ScanAgain, "Scan again", "Scan again without administrator permission", ScanAgainHelp);
        }
        else
        {
            string checkedText = SourcesCheckedText(inputs.Sources, inputs.Now, culture, zone);
            hero = ready == 0 ? $"No updates ready · {checkedText}" : $"{DisplayFormat.Plural(ready, "update")} ready · {checkedText}";
            (heroGlyph, heroTone) = ready != 0 ? ("", StatusTone.Information)
                : freshness.Freshness == EvidenceFreshness.Live ? ("", StatusTone.Success)
                : ("", StatusTone.Information);
            HomeFact evidence = freshness.Freshness switch
            {
                EvidenceFreshness.Live => new("", "Live results from your update sources", StatusTone.Success),
                EvidenceFreshness.LocalOnly => new("", freshness.Sentence, StatusTone.Neutral),
                EvidenceFreshness.Cached => new("", freshness.Sentence, StatusTone.Neutral),
                _ => new("", freshness.Sentence, StatusTone.Attention)
            };
            facts = [evidence, scanned];
            if (ready == 0)
            {
                primary = checkForUpdates;
                secondary = null;
            }
            else
            {
                primary = new(HomeActionKind.ReviewUpdates, "Review updates", $"Review updates: {DisplayFormat.Plural(ready, "update")} ready", ReviewUpdatesHelp);
                secondary = checkForUpdates;
            }
        }

        Func<UpdateJobRecord, HomeAttentionItem> item = job => new HomeAttentionItem(job.StableProductKey, ActivityPresenter.ProductName(rows, job.StableProductKey, job.PackageId),
            job.Outcome.Label, job.Outcome.Glyph, job.Outcome.Tone, job.Outcome.NextStep, DisplayFormat.Relative(job.EndedAt, inputs.Now, culture, zone));
        HomeAttentionItem[] items = attention.Concat(restart).OrderByDescending(job => job.EndedAt).Take(MaximumAttentionItems).Select(item).ToArray();
        List<HomeNotice> notices = [];
        if (attention.Length != 0)
        {
            HomeAttentionItem[] problems = attention.Take(MaximumAttentionItems).Select(item).ToArray();
            string message = problems.Length == 1
                ? $"{problems[0].AppName}: {problems[0].OutcomeLabel}. {problems[0].Summary}"
                : $"{DisplayFormat.Plural(attention.Length, "update")} failed or couldn't be verified: {Names(problems.Select(problem => problem.AppName), attention.Length)}.";
            notices.Add(new(HomeNoticeKind.NeedsAttention, "Needs attention", message, StatusTone.Attention, "View in Activity", AttentionProblemsLinkName, ActivityFilter.Problems));
        }
        if (restart.Length != 0)
        {
            string names = Names(restart.Take(MaximumAttentionItems).Select(item).Select(waiting => waiting.AppName), restart.Length);
            // Final review L3: Restart required has one tone everywhere, Information, and PC Steward never restarts the PC.
            notices.Add(new(HomeNoticeKind.RestartRequired, "Restart required", $"Windows needs to restart to finish updating {names}. PC Steward won't restart your PC.",
                StatusTone.Information, "Details", RestartDetailsLinkName, ActivityFilter.Updates));
        }

        List<HomeRow> glance = [];
        if (hasInventory)
        {
            HomeRow review = NeedsReviewRow(counts);
            HomeRow apps = InstalledAppsRow(counts);
            HomeRow restarts = RestartsAndChecksRow(attention.Length, restart.Length);
            HomeRow decisions = DecisionsRow(counts);
            HomeRow activity = RecentActivityRow(inputs.Activity, inputs.Now, culture, zone);
            if (!inputs.SourcesEnabled)
            {
                HomeRow updates = new(HomeRowKind.Updates, "Updates", "Not checked. Turn on a package source to look for updates.", "", StatusTone.Neutral, new(ShellPage.Updates));
                glance.AddRange([apps, updates, review, restarts, decisions, activity]);
            }
            else
            {
                // With sources on, the status sentence carries the ready count, so there is no separate Updates row.
                glance.AddRange([review, apps, decisions, restarts, activity]);
            }
            // Something that needs the owner comes first.
            if (attention.Length != 0) { glance.Remove(restarts); glance.Insert(0, restarts); }
        }

        return new(hasInventory, hero, heroGlyph, heroTone, facts, freshness, primary, secondary, counts, glance, notices, items);
    }

    private static HomeRow NeedsReviewRow(HomeCounts counts)
    {
        int review = counts.ReviewRequired + counts.SourceUnavailable;
        string description = review == 0
            ? "Nothing needs your review."
            : $"{DisplayFormat.Plural(review, "possible match", "possible matches")} {(review == 1 ? "needs" : "need")} a look before {(review == 1 ? "it" : "they")} can update."
              + (counts.SourceUnavailable == 0 ? string.Empty : $" This includes {DisplayFormat.Plural(counts.SourceUnavailable, "app")} whose source couldn't be checked.");
        return new(HomeRowKind.NeedsReview, "Needs review", description, "", StatusTone.Neutral, new(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.NeedsReview));
    }

    private static HomeRow InstalledAppsRow(HomeCounts counts)
    {
        // UI polish phase 2: the old unmanaged tile is merged into this row, and the status reads "No update source" on screen.
        // "No update source" counts the same rows as the Installed apps filter of that name; rows whose source hasn't been checked
        // yet only count towards "none has an update source yet".
        string apps = DisplayFormat.Plural(counts.Installed, "app");
        string current = counts.Current.ToString("N0", CultureInfo.CurrentCulture);
        string description = counts.Installed == 0 ? "No apps found in the last scan."
            : counts.Unmanaged + counts.NotChecked == counts.Installed ? (counts.Installed == 1 ? $"{apps}. It has no update source yet." : $"{apps}. None has an update source yet.")
            : counts.Unmanaged == 0 ? $"{apps}: {current} current."
            : $"{apps}: {current} current, {counts.Unmanaged.ToString("N0", CultureInfo.CurrentCulture)} with no update source.";
        return new(HomeRowKind.InstalledApps, "Installed apps", description, "", StatusTone.Neutral, new(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.All));
    }

    private static HomeRow RestartsAndChecksRow(int attention, int restart)
    {
        if (attention == 0 && restart == 0)
            return new(HomeRowKind.RestartsAndChecks, "Restarts and checks", "Nothing is waiting for a restart or a check.", "", StatusTone.Neutral,
                new(ShellPage.Activity, ActivityFilter: ActivityFilter.Problems));
        List<string> parts = [];
        if (attention != 0) parts.Add($"{DisplayFormat.Plural(attention, "update")} {(attention == 1 ? "needs" : "need")} attention.");
        if (restart != 0) parts.Add($"{DisplayFormat.Plural(restart, "update")} {(restart == 1 ? "is" : "are")} waiting for a restart.");
        // Independent review L3: when a restart is counted, open an Activity filter that contains it (Updates includes problems).
        return new(HomeRowKind.RestartsAndChecks, attention != 0 ? "Needs attention" : "Restart required", string.Join(" ", parts),
            attention != 0 ? "" : "", attention != 0 ? StatusTone.Attention : StatusTone.Information,
            new(ShellPage.Activity, ActivityFilter: restart != 0 ? ActivityFilter.Updates : ActivityFilter.Problems));
    }

    private static HomeRow DecisionsRow(HomeCounts counts)
    {
        List<string> parts = [];
        if (counts.Pinned != 0) parts.Add($"{counts.Pinned.ToString("N0", CultureInfo.CurrentCulture)} pinned");
        if (counts.Ignored != 0) parts.Add($"{counts.Ignored.ToString("N0", CultureInfo.CurrentCulture)} ignored");
        if (counts.Skipped != 0) parts.Add($"{counts.Skipped.ToString("N0", CultureInfo.CurrentCulture)} skipped");
        string description = parts.Count == 0 ? "No apps pinned, ignored or skipped." : $"{JoinWithAnd(parts)}. You can undo any of them.";
        return new(HomeRowKind.Decisions, "Decisions", description, "", StatusTone.Neutral, new(ShellPage.Decisions));
    }

    private static HomeRow RecentActivityRow(IReadOnlyList<ActivityEntry> activity, DateTimeOffset now, CultureInfo? culture, TimeZoneInfo? zone)
    {
        if (activity.Count == 0)
            return new(HomeRowKind.RecentActivity, "Recent activity", "Nothing yet. Scans, updates and decisions are recorded on this PC.", "", StatusTone.Neutral,
                new(ShellPage.Activity, ActivityFilter: ActivityFilter.All));
        ActivityEntry latest = activity[0];
        bool success = latest.Tone == StatusTone.Success;
        return new(HomeRowKind.RecentActivity, "Recent activity", $"{latest.Title}: {latest.OutcomeLabel}, {DisplayFormat.Relative(latest.OccurredAt, now, culture, zone)}.",
            success ? "" : "", success ? StatusTone.Success : StatusTone.Neutral, new(ShellPage.Activity, ActivityFilter: ActivityFilter.All));
    }

    /// <summary>"A", "A and B", "A, B and C", or "A, B and 3 more" when <paramref name="total"/> is larger than the names given.</summary>
    private static string Names(IEnumerable<string> names, int total)
    {
        List<string> shown = [.. names];
        if (total > shown.Count) shown.Add($"{(total - shown.Count).ToString("N0", CultureInfo.CurrentCulture)} more");
        return JoinWithAnd(shown);
    }

    private static string JoinWithAnd(List<string> parts) =>
        parts.Count <= 1 ? string.Concat(parts) : $"{string.Join(", ", parts.Take(parts.Count - 1))} and {parts[^1]}";

    private static string SourcesCheckedText(IReadOnlyCollection<SourceSnapshot> sources, DateTimeOffset now, CultureInfo? culture, TimeZoneInfo? zone)
    {
        LeastFreshSuccess least = FreshnessPresenter.LeastFresh(FreshnessPresenter.Checked(sources));
        if (least.SourceCount == 0 || least.OldestSuccessAt is not { } oldest) return "sources not checked yet";
        if (least.AnyNeverSucceeded) return "a source hasn't been checked successfully";
        return $"checked {DisplayFormat.Relative(oldest, now, culture, zone)}";
    }
}
