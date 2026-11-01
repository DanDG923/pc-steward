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
                sourcesEnabled ? "Local-only: no package source has been checked yet." : "Local-only: package sources are off, so updates aren't checked.",
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

/// <summary>Where a Home tile leads: a page, plus the saved filter it opens with.</summary>
public sealed record HomeTileTarget(ShellPage Page, InstalledAppsFilter? AppsFilter = null, ActivityFilter? ActivityFilter = null);

/// <summary>The Home tiles (concept A).</summary>
public enum HomeTileKind
{
    RestartRequired,
    NeedsAttention,
    NeedsReview,
    PinnedAndIgnored,
    Unmanaged,
    InstalledApps,
    RecentActivity
}

/// <summary>One Home tile. It is a real button: <see cref="AccessibleName"/> starts with the title and states the count and where it leads.</summary>
public sealed record HomeTile(HomeTileKind Kind, string Title, string Value, string Description, string Glyph, StatusTone Tone, HomeTileTarget Target)
{
    public string AccessibleName => $"{Title}: {Value}. {Description} Opens {ShellNavigation.TitleOf(Target.Page)}.";
}

/// <summary>One line in the compact attention view (FR-UI-06).</summary>
public sealed record HomeAttentionItem(string StableProductKey, string AppName, string OutcomeLabel, string Glyph, StatusTone Tone, string Summary, string When)
{
    public string AccessibleName => $"{AppName}: {OutcomeLabel}. {Summary} {When}";
}

/// <summary>The FR-UI-01 counts. Each one appears exactly once on Home (hero or tile), so nothing is restated (audit P-M4).</summary>
public sealed record HomeCounts(int Installed, int Verified, int ReviewRequired, int SourceUnavailable, int Current, int Unmanaged, int Pinned, int Ignored, int Failed, int RestartRequired);

/// <summary>Everything Home shows.</summary>
public sealed record HomePresentation(
    bool HasInventory,
    string Hero,
    FreshnessPresentation Freshness,
    string PrimaryLabel,
    string PrimaryAccessibleName,
    string SecondaryLabel,
    string SecondaryAccessibleName,
    string SecondaryHelpText,
    HomeCounts Counts,
    IReadOnlyList<HomeTile> Tiles,
    IReadOnlyList<HomeAttentionItem> Attention,
    string SecondaryCaption = "",
    ActivityFilter AttentionActivityFilter = ActivityFilter.Problems,
    string AttentionLinkAccessibleName = HomePresenter.AttentionProblemsLinkName);

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
/// FR-UI-01/05/06 (Task 73 Phase 3, ADR-030 concept A; audit P-H4, P-M4): Home is one status sentence, freshness in words, a primary
/// "Review updates" and a secondary "Check for updates", and tiles that open filtered pages. The ready count is the Updates page's
/// Ready group, so Home never promises more than Updates can review. Presentation only: nothing here starts, approves or elevates.
/// </summary>
public static class HomePresenter
{
    /// <summary>The most items in the compact attention view.</summary>
    public const int MaximumAttentionItems = 5;

    public const string CheckForUpdatesHelp =
        "Reads the apps installed on this PC again and, if package sources are on, refreshes their evidence. It never asks for administrator permission and never installs anything.";

    /// <summary>The secondary action's help text when package sources are off: a scan only (independent review L1).</summary>
    public const string ScanAgainHelp =
        "Reads the apps installed on this PC again. Package sources are off, so no update source is checked. It never asks for administrator permission and never installs anything.";

    /// <summary>The visible caption under the hero buttons when package sources are on (independent review L1).</summary>
    public const string CheckForUpdatesCaption = "Scans this PC, then checks your enabled update sources.";

    public const string AttentionProblemsLinkName = "View in Activity: update problems";
    public const string AttentionUpdatesLinkName = "View in Activity: updates, including restarts and problems";

    public static HomePresentation Build(HomeInputs inputs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        IReadOnlyList<InventoryRow> rows = inputs.Rows;
        int ready = UpdatesPagePresenter.Build(rows, inputs.Details, inputs.SourcesEnabled, inputs.Decisions, null, inputs.Now).Ready.Count;
        Dictionary<string, string> installed = rows.GroupBy(row => row.StableProductKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Version, StringComparer.Ordinal);
        UpdateJobRecord[] latest = [.. UpdateJobHistory.LatestPerProduct(inputs.Jobs).Values.OrderByDescending(job => job.EndedAt)];
        UpdateJobRecord[] attention = latest.Where(job => UpdateJobHistory.NeedsAttention(job, installed.GetValueOrDefault(job.StableProductKey), inputs.LastScanAt)).ToArray();
        UpdateJobRecord[] restart = latest.Where(job => UpdateJobHistory.RestartPending(job, installed.GetValueOrDefault(job.StableProductKey), inputs.LastScanAt)).ToArray();
        int Count(UpdateIntelligenceStatus status) => rows.Count(row => row.Status == status.ToString());
        HomeCounts counts = new(rows.Count, ready, Count(UpdateIntelligenceStatus.ReviewRequired), Count(UpdateIntelligenceStatus.FailedUnavailable),
            Count(UpdateIntelligenceStatus.Current), Count(UpdateIntelligenceStatus.Unmanaged), Count(UpdateIntelligenceStatus.Pinned),
            Count(UpdateIntelligenceStatus.Ignored), attention.Length, restart.Length);
        FreshnessPresentation freshness = FreshnessPresenter.Describe(inputs.SourcesEnabled, inputs.Sources, inputs.Now, culture, zone);
        bool hasInventory = inputs.LastScanAt is not null || rows.Count != 0;

        // FR-UI-05 (independent review L4, M1): the hero always states when this PC was last scanned and, with sources on, the
        // least fresh source's last successful check — the same age the freshness sentence uses.
        string scannedText = inputs.LastScanAt is { } scanAt ? $"scanned {DisplayFormat.Relative(scanAt, inputs.Now, culture, zone)}" : "scan time unknown";
        string readyText = ready == 0 ? "No updates ready" : $"{DisplayFormat.Plural(ready, "update")} ready";
        string hero = !hasInventory ? "PC Steward hasn't looked at this PC yet"
            : inputs.SourcesEnabled ? $"{readyText} · {scannedText} · {SourcesCheckedText(inputs.Sources, inputs.Now, culture, zone)}"
            : $"{readyText} · {scannedText}";

        List<HomeTile> tiles =
        [
            new(HomeTileKind.RestartRequired, "Restart required", restart.Length.ToString("N0", CultureInfo.CurrentCulture),
                restart.Length == 0 ? "No update is waiting for a restart." : $"{DisplayFormat.Plural(restart.Length, "update")} will finish after Windows restarts. PC Steward never restarts your PC.",
                "\uE777", restart.Length == 0 ? StatusTone.Neutral : StatusTone.Information, new(ShellPage.Activity, ActivityFilter: ActivityFilter.Updates)),
            new(HomeTileKind.NeedsAttention, "Needs attention", attention.Length.ToString("N0", CultureInfo.CurrentCulture),
                attention.Length == 0 ? "No update failed or is waiting to be verified." : "Updates that failed or couldn't be verified. Scan again or review them.",
                "\uE7BA", attention.Length == 0 ? StatusTone.Neutral : StatusTone.Attention, new(ShellPage.Activity, ActivityFilter: ActivityFilter.Problems)),
            new(HomeTileKind.NeedsReview, "Needs review", (counts.ReviewRequired + counts.SourceUnavailable).ToString("N0", CultureInfo.CurrentCulture),
                counts.SourceUnavailable == 0
                    ? "Possible updates whose match needs your review. They can't be updated until it's resolved."
                    : $"Possible updates whose match needs your review, including {DisplayFormat.Plural(counts.SourceUnavailable, "app")} whose source couldn't be checked.",
                "\uE9D5", counts.ReviewRequired + counts.SourceUnavailable == 0 ? StatusTone.Neutral : StatusTone.Attention,
                new(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.NeedsReview)),
            new(HomeTileKind.PinnedAndIgnored, "Pinned & ignored", $"{counts.Pinned.ToString("N0", CultureInfo.CurrentCulture)} pinned · {counts.Ignored.ToString("N0", CultureInfo.CurrentCulture)} ignored",
                "Your reversible decisions. Review or undo them on Decisions.", "\uE718", StatusTone.Neutral, new(ShellPage.Decisions)),
            new(HomeTileKind.Unmanaged, "Unmanaged", counts.Unmanaged.ToString("N0", CultureInfo.CurrentCulture),
                "Inventoried only: no approved update source is mapped. This is not a safety warning.", "\uE946", StatusTone.Neutral,
                new(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.Unmanaged)),
            new(HomeTileKind.InstalledApps, "Installed apps", counts.Installed.ToString("N0", CultureInfo.CurrentCulture),
                $"{counts.Current.ToString("N0", CultureInfo.CurrentCulture)} current. See every app found on this PC.", "\uE71D", StatusTone.Neutral,
                new(ShellPage.InstalledApps, AppsFilter: InstalledAppsFilter.All)),
            new(HomeTileKind.RecentActivity, "Recent activity",
                inputs.Activity.Count == 0 ? "Nothing yet" : $"{inputs.Activity[0].Title}: {inputs.Activity[0].OutcomeLabel}",
                inputs.Activity.Count == 0 ? "Scans, updates and decisions are recorded on this PC." : DisplayFormat.Relative(inputs.Activity[0].OccurredAt, inputs.Now, culture, zone) + ".",
                "\uE81C", StatusTone.Neutral, new(ShellPage.Activity, ActivityFilter: ActivityFilter.All)),
        ];

        HomeAttentionItem[] items = attention.Concat(restart).OrderByDescending(job => job.EndedAt).Take(MaximumAttentionItems)
            .Select(job => new HomeAttentionItem(job.StableProductKey, ActivityPresenter.ProductName(rows, job.StableProductKey, job.PackageId),
                job.Outcome.Label, job.Outcome.Glyph, job.Outcome.Tone, job.Outcome.NextStep, DisplayFormat.Relative(job.EndedAt, inputs.Now, culture, zone)))
            .ToArray();

        // Independent review L1: with sources off the secondary action is a scan only, and says so.
        string secondary = inputs.SourcesEnabled ? "Check for updates" : "Scan again";
        // Independent review L3: when the list shows a pending restart, open an Activity filter that contains it.
        bool listsRestart = items.Any(item => restart.Any(job => string.Equals(job.StableProductKey, item.StableProductKey, StringComparison.Ordinal)));
        return new(hasInventory, hero, freshness,
            "Review updates", ready == 0 ? "Review updates: none are ready" : $"Review updates: {DisplayFormat.Plural(ready, "update")} ready",
            secondary, $"{secondary} without administrator permission", inputs.SourcesEnabled ? CheckForUpdatesHelp : ScanAgainHelp,
            counts, tiles, items,
            inputs.SourcesEnabled ? CheckForUpdatesCaption : string.Empty,
            listsRestart ? ActivityFilter.Updates : ActivityFilter.Problems,
            listsRestart ? AttentionUpdatesLinkName : AttentionProblemsLinkName);
    }

    private static string SourcesCheckedText(IReadOnlyCollection<SourceSnapshot> sources, DateTimeOffset now, CultureInfo? culture, TimeZoneInfo? zone)
    {
        LeastFreshSuccess least = FreshnessPresenter.LeastFresh(FreshnessPresenter.Checked(sources));
        if (least.SourceCount == 0 || least.OldestSuccessAt is not { } oldest) return "sources not checked yet";
        if (least.AnyNeverSucceeded) return "a source hasn't been checked successfully";
        return $"sources checked {DisplayFormat.Relative(oldest, now, culture, zone)}";
    }
}
