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

/// <summary>
/// FR-UI-05 (Task 73 Phase 3): states freshness in words — live, cached, local-only, offline or unavailable — never by colour alone.
/// When sources disagree the least fresh state is shown, so the summary never overstates freshness. Presentation only.
/// </summary>
public static class FreshnessPresenter
{
    public static string Word(EvidenceFreshness freshness) => freshness switch
    {
        EvidenceFreshness.Live => "Live",
        EvidenceFreshness.Cached => "Cached",
        EvidenceFreshness.Offline => "Offline",
        EvidenceFreshness.Unavailable => "Unavailable",
        _ => "Local-only"
    };

    public static FreshnessPresentation Describe(bool sourcesEnabled, IReadOnlyCollection<SourceSnapshot> sources, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        SourceSnapshot[] checkedSources = sources.Where(source => source.State != SourceSnapshotState.LocalOnly).ToArray();
        if (!sourcesEnabled || checkedSources.Length == 0)
        {
            return new(EvidenceFreshness.LocalOnly, Word(EvidenceFreshness.LocalOnly),
                sourcesEnabled ? "Local-only: no package source has been checked yet." : "Local-only: package sources are off, so updates aren't checked.",
                StatusTone.Neutral);
        }
        EvidenceFreshness worst = checkedSources.Select(Of).Max();
        DateTimeOffset? last = checkedSources.Select(source => source.LastSuccessAt).Where(at => at is not null).Max();
        string age = last is { } at ? DisplayFormat.Relative(at, now, culture, zone) : "at an unknown time";
        string sentence = worst switch
        {
            EvidenceFreshness.Live => $"Live: package sources were checked {age}.",
            EvidenceFreshness.Cached => $"Cached: the last successful source check was {age}; results may be out of date.",
            EvidenceFreshness.Offline => $"Offline: a package source couldn't be reached. The last successful check was {age}.",
            _ => $"Unavailable: a package source couldn't complete its check. The last successful check was {age}."
        };
        return new(worst, Word(worst), sentence, worst == EvidenceFreshness.Live ? StatusTone.Information : StatusTone.Attention);
    }

    private static EvidenceFreshness Of(SourceSnapshot source) => source.State switch
    {
        SourceSnapshotState.Live => EvidenceFreshness.Live,
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

    public static HomePresentation Build(HomeInputs inputs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        IReadOnlyList<InventoryRow> rows = inputs.Rows;
        int ready = UpdatesPagePresenter.Build(rows, inputs.Details, inputs.SourcesEnabled, inputs.Decisions, null, inputs.Now).Ready.Count;
        Dictionary<string, string> installed = rows.GroupBy(row => row.StableProductKey, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Version, StringComparer.Ordinal);
        UpdateJobRecord[] latest = [.. UpdateJobHistory.LatestPerProduct(inputs.Jobs).Values.OrderByDescending(job => job.EndedAt)];
        UpdateJobRecord[] attention = latest.Where(job => UpdateJobHistory.NeedsAttention(job, installed.GetValueOrDefault(job.StableProductKey))).ToArray();
        UpdateJobRecord[] restart = latest.Where(job => UpdateJobHistory.RestartPending(job, installed.GetValueOrDefault(job.StableProductKey))).ToArray();
        int Count(UpdateIntelligenceStatus status) => rows.Count(row => row.Status == status.ToString());
        HomeCounts counts = new(rows.Count, ready, Count(UpdateIntelligenceStatus.ReviewRequired), Count(UpdateIntelligenceStatus.FailedUnavailable),
            Count(UpdateIntelligenceStatus.Current), Count(UpdateIntelligenceStatus.Unmanaged), Count(UpdateIntelligenceStatus.Pinned),
            Count(UpdateIntelligenceStatus.Ignored), attention.Length, restart.Length);
        FreshnessPresentation freshness = FreshnessPresenter.Describe(inputs.SourcesEnabled, inputs.Sources, inputs.Now, culture, zone);
        bool hasInventory = inputs.LastScanAt is not null || rows.Count != 0;

        DateTimeOffset? lastChecked = inputs.SourcesEnabled ? inputs.Sources.Select(source => source.LastSuccessAt).Where(at => at is not null).Max() : null;
        string checkedText = lastChecked is { } sourceAt ? $"last checked {DisplayFormat.Relative(sourceAt, inputs.Now, culture, zone)}"
            : inputs.LastScanAt is { } scanAt ? $"last scanned {DisplayFormat.Relative(scanAt, inputs.Now, culture, zone)}"
            : "not checked yet";
        string readyText = ready == 0 ? "No updates ready" : $"{DisplayFormat.Plural(ready, "update")} ready";
        string hero = hasInventory ? $"{readyText} · {checkedText}" : "PC Steward hasn't looked at this PC yet";

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

        return new(hasInventory, hero, freshness,
            "Review updates", ready == 0 ? "Review updates: none are ready" : $"Review updates: {DisplayFormat.Plural(ready, "update")} ready",
            "Check for updates", "Check for updates without administrator permission", CheckForUpdatesHelp,
            counts, tiles, items);
    }
}
