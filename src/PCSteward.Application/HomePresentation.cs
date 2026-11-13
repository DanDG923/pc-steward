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
/// Owner report 2026-11-13 (release 0.1.15; owner decision the same day): Home and Updates showed one state differently, and a
/// check 11 minutes old was called "Cached" with a warning, because results read back from this PC's store are always marked
/// cached. Now a result that nothing went wrong with reads as Live for <see cref="LiveWindow"/> after its last success however
/// it was loaded; an older one is Cached with a calm note; and the warning tone is kept for a check that failed and fell back to
/// older results, a source that never succeeded, and results older than <see cref="StaleAfter"/>.
/// </summary>
public static class FreshnessPresenter
{
    /// <summary>
    /// How long a live source result reads as "Live" on screen. No domain or application freshness constant exists for display, so
    /// this presentation-only window is 15 minutes; it changes only the word shown, never what may be updated.
    /// </summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How old cached results are before they are called out as a warning (owner decision 2026-11-13). The longest scheduled
    /// check is weekly, so results older than a week mean checks are not running. Presentation only, like <see cref="LiveWindow"/>.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

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
                sourcesEnabled ? "Local-only: PC Steward hasn't looked for updates yet." : "Local-only: update checks are off.",
                StatusTone.Neutral);
        }
        EvidenceFreshness worst = checkedSources.Select(source => Of(source, now)).Max();
        LeastFreshSuccess least = LeastFresh(checkedSources);
        string age = least.OldestSuccessAt is { } at ? DisplayFormat.Relative(at, now, culture, zone) : "at an unknown time";
        string lastCheck = least.AnyNeverSucceeded
            ? (checkedSources.Length == 1 ? "It has never completed a successful check." : "One source has never completed a successful check.")
            : checkedSources.Length == 1 ? $"The last successful check was {age}." : $"The least fresh source's last successful check was {age}.";
        bool calm = worst == EvidenceFreshness.Live || (worst == EvidenceFreshness.Cached && !NeedsWarning(checkedSources, least, now));
        string sentence = worst switch
        {
            EvidenceFreshness.Live => $"Live: update sources were checked {age}.",
            EvidenceFreshness.Cached when calm => $"Cached: these are the results of the last check. {lastCheck}",
            EvidenceFreshness.Cached => $"Cached: results may be out of date. {lastCheck}",
            EvidenceFreshness.Offline => $"Offline: an update source couldn't be reached. {lastCheck}",
            _ => $"Unavailable: an update source couldn't complete its check. {lastCheck}"
        };
        return new(worst, Word(worst), sentence, calm ? StatusTone.Information : StatusTone.Attention);
    }

    /// <summary>
    /// Cached results are a warning when a check failed and fell back to older results (the refresh adds its reason to the
    /// source's warnings), when a source has never succeeded, or when the least fresh success is older than <see cref="StaleAfter"/>.
    /// </summary>
    private static bool NeedsWarning(SourceSnapshot[] checkedSources, LeastFreshSuccess least, DateTimeOffset now) =>
        least.AnyNeverSucceeded || least.OldestSuccessAt is not { } oldest || now - oldest > StaleAfter || checkedSources.Any(FellBack);

    /// <summary>A cached source whose latest check failed: the refresh kept the older results and recorded why.</summary>
    private static bool FellBack(SourceSnapshot source) => source.State == SourceSnapshotState.Cached && source.Warnings.Count != 0;

    private static EvidenceFreshness Of(SourceSnapshot source, DateTimeOffset now) => source.State switch
    {
        SourceSnapshotState.Live when source.LastSuccessAt is { } at && now - at <= LiveWindow => EvidenceFreshness.Live,
        SourceSnapshotState.Live => EvidenceFreshness.Cached,
        SourceSnapshotState.Cached when source.Warnings.Contains("source:offline", StringComparer.Ordinal) => EvidenceFreshness.Offline,
        SourceSnapshotState.Cached when !FellBack(source) && source.LastSuccessAt is { } at && now - at <= LiveWindow => EvidenceFreshness.Live,
        SourceSnapshotState.Cached => EvidenceFreshness.Cached,
        SourceSnapshotState.Unavailable when source.Warnings.Contains("source:offline", StringComparer.Ordinal) => EvidenceFreshness.Offline,
        SourceSnapshotState.Unavailable => EvidenceFreshness.Unavailable,
        _ => EvidenceFreshness.LocalOnly
    };
}

/// <summary>Where a Home row leads: a page, plus the saved filter it opens with.</summary>
public sealed record HomeRowTarget(ShellPage Page, InstalledAppsFilter? AppsFilter = null, ActivityFilter? ActivityFilter = null);

/// <summary>
/// The lines Home shows under the ready updates (UI review 2026-11-13, item 15). A line is shown only when it has something to
/// say, so a calm PC has none: the navigation pane already opens Installed apps, Activity and Decisions.
/// </summary>
public enum HomeRowKind
{
    /// <summary>Apps with a possible match the owner can confirm, and something newer on offer.</summary>
    NeedsReview,
    /// <summary>Apps with something newer on offer that PC Steward can't update yet.</summary>
    CantUpdateYet,
    /// <summary>Apps the owner pinned, ignored or skipped.</summary>
    Decisions
}

/// <summary>
/// One Home line: an icon, a title and one plain sentence, and the page it opens. It is a real button: its
/// <see cref="AccessibleName"/> starts with the title, then says what the line says and where it leads.
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
    /// <summary>Opens one review of every ready update; nothing is installed until the owner confirms there.</summary>
    UpdateAll,
    /// <summary>A read-only scan, then a refresh of the enabled sources.</summary>
    CheckForUpdates,
    /// <summary>A read-only scan only (sources are off).</summary>
    ScanAgain
}

/// <summary>A Home action: its label, an accessible name that starts with the label, and help text for its tooltip.</summary>
public sealed record HomeAction(HomeActionKind Kind, string Label, string AccessibleName, string HelpText);

/// <summary>One fact under the status sentence: an icon and a short phrase. The text carries the meaning, never the colour.</summary>
public sealed record HomeFact(string Glyph, string Text, StatusTone Tone);

/// <summary>
/// A ready update listed on Home (UI review 2026-11-13, item 15): the same app, versions and "Update…" as its row on Updates.
/// </summary>
public sealed record HomeReadyUpdate(string StableProductKey, string Name, string VersionText, string NewVersion);

/// <summary>One line under "Recent" on Home: what happened and when, with the outcome's icon.</summary>
public sealed record HomeRecentLine(string Text, string When, string Glyph, StatusTone Tone)
{
    public string Line => $"{Text} · {When}";
}

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
    IReadOnlyList<HomeAttentionItem> Attention,
    IReadOnlyList<HomeReadyUpdate> ReadyUpdates,
    IReadOnlyList<string> ReadyKeys,
    IReadOnlyList<HomeRecentLine> Recent)
{
    /// <summary>How many ready updates are on Updates but not listed on Home.</summary>
    public int MoreReady => ReadyKeys.Count - ReadyUpdates.Count;

    /// <summary>The link under the ready updates when Home doesn't list them all, or null.</summary>
    public string? MoreReadyText => MoreReady <= 0 ? null : $"and {MoreReady.ToString("N0", CultureInfo.CurrentCulture)} more on Updates";
}

/// <summary>
/// Inputs for <see cref="HomePresenter.Build"/>; all are existing presentation state. <see cref="LastBootAt"/> (Task 51) is when Windows
/// last started, if known: a restart-required update stays on Home until then.
/// </summary>
public sealed record HomeInputs(
    IReadOnlyList<InventoryRow> Rows,
    IReadOnlyDictionary<string, UpdateDetail> Details,
    bool SourcesEnabled,
    IReadOnlyCollection<SourceSnapshot> Sources,
    IReadOnlyCollection<StableDecision> Decisions,
    IReadOnlyCollection<UpdateJobRecord> Jobs,
    IReadOnlyList<ActivityEntry> Activity,
    DateTimeOffset? LastScanAt,
    DateTimeOffset Now,
    DateTimeOffset? LastBootAt = null);

/// <summary>
/// FR-UI-01/05/06 (ADR-030 concept A; UI review 2026-11-13, item 15): Home answers one question, "is anything out of date?". It
/// is one status card (an icon, one sentence, facts with icons, and the action that fits the state), info bars for updates that
/// need the owner, the ready updates themselves, lines that appear only when they have something to say, and the last few things
/// PC Steward did. The primary action follows the state: "Turn on update checks…" when local-only, "Update all N…" when updates
/// are ready, otherwise "Check for updates". The ready updates are the Updates page's Ready group and "Update all N…" opens the
/// same review, so Home never offers more than Updates can review and every update still passes the review step (FR-UPD-01).
/// Presentation only: nothing here starts, approves or elevates.
/// </summary>
public static class HomePresenter
{
    /// <summary>The most items in the compact attention view.</summary>
    public const int MaximumAttentionItems = 5;

    public const string TurnOnUpdateChecksLabel = "Turn on update checks…";

    public const string TurnOnUpdateChecksHelp =
        "Shows what WinGet and Chocolatey would receive and asks you first.";

    public const string CheckForUpdatesHelp =
        "Scans this PC, then looks for updates. Nothing is installed.";

    /// <summary>The scan-only action's help text when package sources are off (independent review L1).</summary>
    public const string ScanAgainHelp =
        "Reads the apps installed on this PC again. Update checks are off, so it doesn't look for updates.";

    public const string UpdateAllHelp = "Opens one review of every ready update. Nothing is installed until you confirm there.";

    /// <summary>The most ready updates listed on Home; the rest are one click away on Updates.</summary>
    public const int MaximumReadyUpdates = 5;

    /// <summary>The most lines under "Recent".</summary>
    public const int MaximumRecentLines = 3;

    /// <summary>The Activity filter that "See all activity" under Recent opens with.</summary>
    public const ActivityFilter RecentActivityFilter = ActivityFilter.All;

    /// <summary>
    /// UI review 2026-11-13 (item 15): whether Home shows the bar about PC Steward's own update. A problem, or an update still in
    /// progress, is always shown. A finished update is shown until the owner closes it, and for a day after it was recorded.
    /// </summary>
    public static bool ShowsOwnUpdateBar(bool succeeded, bool closed, DateTimeOffset? recordedAt, DateTimeOffset now) =>
        !succeeded || (!closed && (recordedAt is not { } at || now - at < TimeSpan.FromDays(1)));

    public const string AttentionProblemsLinkName = "View in Activity: update problems";
    public const string AttentionUpdatesLinkName = "View in Activity: updates, including restarts and problems";
    public const string RestartDetailsLinkName = "Details: updates waiting for a restart, in Activity";

    public static HomePresentation Build(HomeInputs inputs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        IReadOnlyList<InventoryRow> rows = inputs.Rows;
        UpdatesPagePresentation updates = UpdatesPagePresenter.Build(rows, inputs.Details, inputs.SourcesEnabled, inputs.Decisions, null, inputs.Now);
        int ready = updates.Ready.Count;
        Dictionary<string, string> installed = rows.GroupBy(row => row.StableProductKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Version, StringComparer.Ordinal);
        UpdateJobRecord[] latest = [.. UpdateJobHistory.LatestPerProduct(inputs.Jobs).Values.OrderByDescending(job => job.EndedAt)];
        UpdateJobRecord[] attention = latest.Where(job => UpdateJobHistory.NeedsAttention(job, installed.GetValueOrDefault(job.StableProductKey), inputs.LastScanAt)).ToArray();
        UpdateJobRecord[] restart = latest.Where(job => UpdateJobHistory.RestartPending(job, installed.GetValueOrDefault(job.StableProductKey), inputs.LastScanAt, inputs.LastBootAt)).ToArray();
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
        HomeAction checkForUpdates = new(HomeActionKind.CheckForUpdates, "Check for updates", "Check for updates (scans this PC, then looks for updates)", CheckForUpdatesHelp);
        if (!hasInventory)
        {
            hero = "PC Steward hasn't looked at this PC yet";
            (heroGlyph, heroTone) = ("", StatusTone.Information);
            facts = [];
            primary = new(HomeActionKind.ScanAgain, "Scan this PC", "Scan this PC (reads the installed apps; nothing is changed)", ScanAgainHelp);
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
            secondary = new(HomeActionKind.ScanAgain, "Scan again", "Scan again (reads the installed apps; nothing is changed)", ScanAgainHelp);
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
                // Owner decision 2026-11-13: cached results read calmly unless a check failed or they are over a week old.
                EvidenceFreshness.Cached => new("", freshness.Sentence, freshness.Tone == StatusTone.Attention ? StatusTone.Attention : StatusTone.Neutral),
                _ => new("", freshness.Sentence, StatusTone.Attention)
            };
            facts = [evidence, scanned, new("\uE71D", $"{DisplayFormat.Plural(rows.Count, "app")} on this PC", StatusTone.Neutral)];
            if (ready == 0)
            {
                primary = checkForUpdates;
                secondary = null;
            }
            else
            {
                // Owner report 2026-11-13 (release 0.1.15): a long app name is not on the button, so its accessible name says it.
                string reviewAllName = updates.ReviewAllUnnamedApp is { } unnamed
                    ? $"{updates.ReviewAllLabel} ({unnamed}; opens one review before anything is installed)"
                    : $"{updates.ReviewAllLabel} (opens one review before anything is installed)";
                primary = new(HomeActionKind.UpdateAll, updates.ReviewAllLabel, reviewAllName, UpdateAllHelp);
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

        // UI review 2026-11-13 (item 15): a line appears only when it has something to say. What needs the owner is in the info bars
        // above; Installed apps, Activity and Decisions are in the navigation pane.
        List<HomeRow> glance = [];
        if (hasInventory)
        {
            // With update checks off the status sentence already says why nothing can update, so these two lines stay away.
            if (inputs.SourcesEnabled && MatchesRow(updates.CheckTheMatch) is { } matches) glance.Add(matches);
            if (inputs.SourcesEnabled && CantUpdateYetRow(updates.CantUpdateYet) is { } blocked) glance.Add(blocked);
            if (DecisionsRow(counts) is { } decisions) glance.Add(decisions);
        }
        HomeReadyUpdate[] readyUpdates = [.. updates.Ready.Take(MaximumReadyUpdates).Select(row => new HomeReadyUpdate(row.StableProductKey, row.Name, row.VersionText, row.NewVersion ?? string.Empty))];
        HomeRecentLine[] recent = hasInventory
            ? [.. inputs.Activity.Take(MaximumRecentLines).Select(entry => new HomeRecentLine($"{entry.Title}: {entry.OutcomeLabel}", DisplayFormat.Relative(entry.OccurredAt, inputs.Now, culture, zone), entry.Glyph, entry.Tone == StatusTone.Success ? StatusTone.Success : StatusTone.Neutral))]
            : [];

        return new(hasInventory, hero, heroGlyph, heroTone, facts, freshness, primary, secondary, counts, glance, notices, items, readyUpdates, updates.ReadyKeys, recent);
    }

    /// <summary>
    /// "Check the match": the apps the Updates page asks about, by name. It counts exactly the rows of that group on Updates, which
    /// is where the line leads.
    /// </summary>
    private static HomeRow? MatchesRow(IReadOnlyList<UpdateRowPresentation> matches)
    {
        if (matches.Count == 0) return null;
        StatusChipPresentation chip = StatusVocabulary.For(nameof(UpdateIntelligenceStatus.ReviewRequired));
        string description = matches.Count == 1
            ? $"{matches[0].Name} has a possible match. Confirm it is the same app and its update can be offered."
            : $"{DisplayFormat.Plural(matches.Count, "app")} have a possible match to confirm: {Names(matches.Take(MaximumNamedApps).Select(match => match.Name), matches.Count)}.";
        return new(HomeRowKind.NeedsReview, chip.Label, description, chip.Glyph, chip.Tone, new(ShellPage.Updates));
    }

    /// <summary>"Can't update yet": something newer is on offer but PC Steward can't update the app yet. Updates says why.</summary>
    private static HomeRow? CantUpdateYetRow(IReadOnlyList<UpdateRowPresentation> blocked)
    {
        if (blocked.Count == 0) return null;
        string description = blocked.Count == 1
            ? $"{blocked[0].Name} can't be updated yet. Updates says why."
            : $"{DisplayFormat.Plural(blocked.Count, "app")} can't be updated yet. Updates says why.";
        return new(HomeRowKind.CantUpdateYet, UpdatesPagePresenter.CantUpdateYetTitle, description, AppDetailSummaryPresenter.CantUpdateYetGlyph, StatusTone.Neutral, new(ShellPage.Updates));
    }

    private const string DecisionsGlyph = "\uE718";

    /// <summary>The most app names a Home line lists before "and N more".</summary>
    private const int MaximumNamedApps = 3;

    private static HomeRow? DecisionsRow(HomeCounts counts)
    {
        List<string> parts = [];
        if (counts.Pinned != 0) parts.Add($"{counts.Pinned.ToString("N0", CultureInfo.CurrentCulture)} pinned");
        if (counts.Ignored != 0) parts.Add($"{counts.Ignored.ToString("N0", CultureInfo.CurrentCulture)} ignored");
        if (counts.Skipped != 0) parts.Add($"{counts.Skipped.ToString("N0", CultureInfo.CurrentCulture)} skipped");
        if (parts.Count == 0) return null;
        return new(HomeRowKind.Decisions, "Decisions", $"{JoinWithAnd(parts)}. You can undo any of them.", DecisionsGlyph, StatusTone.Neutral, new(ShellPage.Decisions));
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
