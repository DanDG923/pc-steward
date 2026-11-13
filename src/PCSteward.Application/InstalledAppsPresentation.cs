using System.Globalization;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>The saved Installed apps filters (Task 73 Phase 3, ADR-030 decision 5: Unmanaged, shown as "No update source", is a saved filter, not a page).</summary>
public enum InstalledAppsFilter
{
    All,
    Updates,
    NeedsReview,
    Unmanaged,
    PinnedAndIgnored,
    /// <summary>UI review 2026-11-13 (item 14): apps PC Steward has matched to an update source. It replaces the Repository apps page.</summary>
    HasUpdateSource
}

/// <summary>One saved-filter button: its label, the count of matching apps and the vocabulary labels it covers.</summary>
public sealed record InstalledAppsFilterOption(InstalledAppsFilter Filter, string Label, int Count, IReadOnlyList<string> StatusLabels, string Description)
{
    /// <summary>The visible label with its count, for example "All (312)".</summary>
    public string DisplayLabel => $"{Label} ({Count.ToString("N0", CultureInfo.CurrentCulture)})";

    /// <summary>The accessible name; it starts with the visible label (label-in-name).</summary>
    public string AccessibleName => $"{DisplayLabel}: {Description}";
}

/// <summary>
/// FR-UI-01/05, AC-UX-03 (Task 73 Phase 3; audit P-L7): the saved Installed apps filters. They use the Phase 1 status vocabulary,
/// and every count is taken over the whole saved inventory, so "All" really counts every app (including current, pinned, ignored
/// and source-not-checked rows). Presentation only: a filter never changes data or makes anything actionable.
/// </summary>
public static class InstalledAppsFilters
{
    private static readonly Dictionary<InstalledAppsFilter, string[]> Statuses = new()
    {
        [InstalledAppsFilter.Updates] = [nameof(UpdateIntelligenceStatus.VerifiedUpdate)],
        // UI review 2026-11-13 (item 5): this tab is named for the one state it lists. An app whose source couldn't be reached
        // ("Couldn't check") is not a match to check; it is found under All, by its status, and on Updates.
        [InstalledAppsFilter.NeedsReview] = [nameof(UpdateIntelligenceStatus.ReviewRequired)],
        [InstalledAppsFilter.Unmanaged] = [nameof(UpdateIntelligenceStatus.Unmanaged)],
        [InstalledAppsFilter.PinnedAndIgnored] = [nameof(UpdateIntelligenceStatus.Pinned), nameof(UpdateIntelligenceStatus.Ignored), nameof(UpdateIntelligenceStatus.Skipped)],
    };

    /// <summary>The filter's short label.</summary>
    public static string Label(InstalledAppsFilter filter) => filter switch
    {
        InstalledAppsFilter.Updates => StatusVocabulary.For(nameof(UpdateIntelligenceStatus.VerifiedUpdate)).Label,
        InstalledAppsFilter.NeedsReview => StatusVocabulary.For(nameof(UpdateIntelligenceStatus.ReviewRequired)).Label,
        InstalledAppsFilter.Unmanaged => StatusVocabulary.NoUpdateSourceLabel,
        InstalledAppsFilter.PinnedAndIgnored => "Pinned, ignored & skipped",
        InstalledAppsFilter.HasUpdateSource => HasUpdateSourceLabel,
        _ => "All"
    };

    /// <summary>True when a row with <paramref name="status"/> belongs in <paramref name="filter"/>. "All" matches every status.</summary>
    public static bool Matches(InstalledAppsFilter filter, string? status) =>
        filter != InstalledAppsFilter.HasUpdateSource && (!Statuses.TryGetValue(filter, out string[]? keys) || keys.Contains(status, StringComparer.Ordinal));

    /// <summary>
    /// True when <paramref name="row"/> belongs in <paramref name="filter"/>. "Has an update source" is not a status: it lists the
    /// apps whose keys are in <paramref name="withUpdateSource"/>, which are the apps matched to a package in an update source.
    /// </summary>
    public static bool Matches(InstalledAppsFilter filter, InventoryRow row, IReadOnlySet<string>? withUpdateSource)
    {
        ArgumentNullException.ThrowIfNull(row);
        return filter == InstalledAppsFilter.HasUpdateSource ? withUpdateSource?.Contains(row.StableProductKey) == true : Matches(filter, row.Status);
    }

    /// <summary>The tab that lists the apps PC Steward can check for updates (UI review 2026-11-13, item 14).</summary>
    public const string HasUpdateSourceLabel = "Has an update source";

    /// <summary>
    /// Said under "Check the match" when some of those apps are already on the version their match lists, so confirming them
    /// only records the match (FR-POL-03/05). It counts the apps the list shows now, so a search or filter narrows it.
    /// </summary>
    public static string ConfirmCurrentMatchesText(int count) => count == 1
        ? "1 of these apps is already on the version its match lists. Confirm the match and PC Steward will offer its updates when they appear."
        : $"{count.ToString("N0", CultureInfo.CurrentCulture)} of these apps are already on the version their match lists. Confirm the matches and PC Steward will offer their updates when they appear.";

    /// <summary>The button beside that line. It opens a dialog that names the apps before anything is saved.</summary>
    public const string ConfirmCurrentMatchesLabel = "Confirm these matches…";

    /// <summary>The dialog's confirming button.</summary>
    public static string ConfirmCurrentMatchesButton(int count) => count == 1 ? "Confirm match" : $"Confirm {count.ToString("N0", CultureInfo.CurrentCulture)} matches";

    /// <summary>The most app names the dialog lists before "and N more".</summary>
    public const int MaximumConfirmNames = 12;

    /// <summary>The dialog's first line: the apps whose matches would be confirmed, by name.</summary>
    public static string ConfirmCurrentMatchesNames(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        List<string> shown = [.. names.Take(MaximumConfirmNames)];
        if (names.Count > shown.Count) shown.Add($"{(names.Count - shown.Count).ToString("N0", CultureInfo.CurrentCulture)} more");
        string list = shown.Count <= 1 ? string.Concat(shown) : $"{string.Join(", ", shown.Take(shown.Count - 1))} and {shown[^1]}";
        return names.Count == 1 ? $"This confirms the match for {list}." : $"This confirms the matches for {list}.";
    }

    public const string ConfirmCurrentMatchesTitle = "Confirm these matches?";
    public const string ConfirmCurrentMatchesExplanation = "Each of these apps is already on the version its update source lists. A match with a newer version, or one that can't be checked, is left for you to answer on its own. You can undo each one on the Decisions page. It does not approve or start an update.";

    /// <summary>What "Has an update source" says in place of an empty list while update checks are off.</summary>
    public const string ChecksOffTitle = "Update checks are off";
    public const string ChecksOffMessage = "PC Steward can't tell which apps have an update source until update checks are on. You can turn them on in Settings.";

    /// <summary>The saved filters in the order their tabs are shown: "Has an update source" sits beside "No update source".</summary>
    private static readonly InstalledAppsFilter[] TabOrder =
    [
        InstalledAppsFilter.All, InstalledAppsFilter.Updates, InstalledAppsFilter.NeedsReview, InstalledAppsFilter.HasUpdateSource,
        InstalledAppsFilter.Unmanaged, InstalledAppsFilter.PinnedAndIgnored
    ];

    /// <summary>
    /// UI polish phase 4 (approved target T-Installed): the page's footnote whenever an app has no update source. It says, calmly,
    /// that "No update source" is not a warning, so no row needs a warning badge.
    /// </summary>
    public const string NoUpdateSourceNote = "No update source isn't a safety warning. It means PC Steward doesn't know where to get updates for that app.";

    /// <summary>
    /// UI review 2026-11-13 (fault 3): true for a record that is part of Windows or of another app rather than an app the owner
    /// installed: an MSIX framework, resource pack or system package. Installed apps lists these only on request.
    /// </summary>
    public static bool IsSystemComponent(InventoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Classification is "Framework" or "Resource" or "System";
    }

    /// <summary>The label of the switch that lists system components, with how many there are.</summary>
    public static string SystemComponentsLabel(int count) => $"Show system components ({count.ToString("N0", CultureInfo.CurrentCulture)})";

    /// <summary>
    /// The second line of an Installed apps row: the publisher and where the record came from. A record with no publisher shows
    /// only where it came from, never a stray separator ("· WinGet"). UI review 2026-11-13 (item 8): where a record came from is
    /// plumbing, so it is shown only when <paramref name="showSource"/> says another listed row has the same name and the source
    /// is what tells them apart; otherwise the line is the publisher alone.
    /// </summary>
    public static string PublisherAndSource(string? publisher, string source, bool showSource = true)
    {
        bool known = !string.IsNullOrWhiteSpace(publisher) && !string.Equals(publisher.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase);
        if (!showSource) return known ? publisher!.Trim() : "Unknown publisher";
        return known ? $"{publisher!.Trim()} · {source}" : source;
    }

    /// <summary>The names that more than one of <paramref name="rows"/> share, ignoring case: those rows show their source.</summary>
    public static IReadOnlySet<string> SharedNames(IEnumerable<InventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.GroupBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Skip(1).Any()).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The page's one line of description (UI review 2026-11-13, item 7: no reassurance in page descriptions).</summary>
    public const string PageDescription = "Every app found on this PC.";

    /// <summary>
    /// When this PC was last scanned, in plain words ("Scanned today at 14:50"), instead of the scan mode's internal name
    /// (UI review 2026-11-13, item 8).
    /// </summary>
    public static string ScannedText(DateTimeOffset? lastScanAt, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        lastScanAt is { } at ? $"Scanned {DisplayFormat.DayAndTime(at, now, culture, zone)}" : "Not scanned yet";

    /// <summary>The count beside the search box: "437 apps", or "12 of 437 apps" when a search or filter narrows the list.</summary>
    public static string CountText(int shown, int total) => shown == total
        ? DisplayFormat.Plural(total, "app")
        : $"{shown.ToString("N0", CultureInfo.CurrentCulture)} of {DisplayFormat.Plural(total, "app")}";

    /// <summary>
    /// The saved filters in order, each with its count over <paramref name="rows"/>. "Has an update source" is offered when
    /// <paramref name="withUpdateSource"/> is given: the keys of the apps matched to a package in an update source.
    /// </summary>
    public static IReadOnlyList<InstalledAppsFilterOption> Options(IReadOnlyCollection<InventoryRow> rows, IReadOnlySet<string>? withUpdateSource = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return TabOrder.Where(filter => filter != InstalledAppsFilter.HasUpdateSource || withUpdateSource is not null).Select(filter =>
        {
            string[] labels = Statuses.TryGetValue(filter, out string[]? keys) ? keys.Select(key => StatusVocabulary.For(key).Label).ToArray() : [];
            string description = filter switch
            {
                InstalledAppsFilter.All => "every app in the saved inventory",
                InstalledAppsFilter.Unmanaged => $"{labels[0]}. {StatusVocabulary.For(nameof(UpdateIntelligenceStatus.Unmanaged)).Meaning}",
                InstalledAppsFilter.HasUpdateSource => "apps PC Steward can check for updates",
                _ => string.Join(", ", labels)
            };
            return new InstalledAppsFilterOption(filter, Label(filter), rows.Count(row => Matches(filter, row, withUpdateSource)), labels, description);
        }).ToArray();
    }
}

/// <summary>One labelled fact in a structured detail section.</summary>
public sealed record DetailFact(string Label, string Value);

/// <summary>One structured item (an installed instance or a source candidate) with its facts.</summary>
public sealed record DetailItem(string Title, IReadOnlyList<DetailFact> Facts);

/// <summary>One entry in an app's own timeline (FR-HIS-02).</summary>
public sealed record ProductTimelineItem(DateTimeOffset OccurredAt, string When, string Title, string? Detail, string Glyph, StatusTone Tone)
{
    /// <summary>The entry's accessible name: title, detail and time.</summary>
    public string AccessibleName => string.IsNullOrWhiteSpace(Detail) ? $"{Title}. {When}" : $"{Title}. {Detail}. {When}";
}

/// <summary>
/// FR-HIS-02, FR-UI-04 (Task 73 Phase 3; audit P-L6): turns an app's technical details into structured sections — installed
/// instances, source candidates and its own timeline — instead of newline-joined text. Presentation only.
/// </summary>
public static partial class AppDetailPresenter
{
    /// <summary>Each installed instance as a titled list of facts.</summary>
    public static IReadOnlyList<DetailItem> Instances(IEnumerable<InstallationInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        return instances.Select(instance => new DetailItem(
            $"{instance.Provider}: {(string.IsNullOrWhiteSpace(instance.Version) ? "unknown version" : instance.Version)}",
            [
                new("Found by", instance.Provider),
                new("Identity", instance.ProviderIdentity),
                new("Version", string.IsNullOrWhiteSpace(instance.Version) ? "Unknown" : instance.Version),
                new("Scope", instance.Scope),
                new("Architecture", instance.Architecture),
                new("Publisher", string.IsNullOrWhiteSpace(instance.Publisher) ? "Unknown" : instance.Publisher),
            ])).ToArray();
    }

    /// <summary>Each source candidate as a titled list of facts, with review reasons in words.</summary>
    public static IReadOnlyList<DetailItem> Candidates(IEnumerable<ReviewCandidateChoice> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.Select(candidate =>
        {
            List<DetailFact> facts =
            [
                new("Source", candidate.SourceId),
                new("Package ID", candidate.PackageId),
                new("Version", candidate.CandidateVersion ?? "Not established"),
                new("Publisher", candidate.Publisher ?? "Unknown"),
                new("Scope", candidate.Scope),
                new("Architecture", candidate.Architecture),
                new("Channel", candidate.Channel ?? "Unknown"),
                new("Evidence", candidate.Evidence),
            ];
            IReadOnlyList<string> reasons = ReviewReasonText.DescribeAll(candidate.ReviewReasons);
            if (reasons.Count != 0) facts.Add(new("Why it needs review", string.Join(" ", reasons)));
            return new DetailItem($"{candidate.SourceId} · {candidate.PackageId}", facts);
        }).ToArray();
    }

    /// <summary>The app's own timeline, newest first, with every kind in words.</summary>
    public static IReadOnlyList<ProductTimelineItem> Timeline(IEnumerable<ProductTimelineEntry> entries, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.OrderByDescending(entry => entry.OccurredAt).Select(entry => TimelineItem(entry, culture, zone)).ToArray();
    }

    private static ProductTimelineItem TimelineItem(ProductTimelineEntry entry, CultureInfo? culture, TimeZoneInfo? zone)
    {
        string when = DisplayFormat.DateTime(entry.OccurredAt, culture, zone);
        string? reason = string.IsNullOrWhiteSpace(entry.Reason) ? null : entry.Reason.Trim();
        string value = Value(entry.Summary);
        switch (entry.Kind)
        {
            case "PinCurrentVersion":
                return new(entry.OccurredAt, when, $"Pinned at version {value}", Reason(reason), "\uE718", StatusTone.Neutral);
            case "PinSourcePackage":
                return new(entry.OccurredAt, when, $"Pinned to package {value}", Reason(reason), "\uE718", StatusTone.Neutral);
            case "Ignore":
                string until = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expires)
                    ? $"until {DisplayFormat.Date(expires, culture, zone)}" : "indefinitely";
                return new(entry.OccurredAt, when, $"Ignored {until}", Reason(reason), "\uE74F", StatusTone.Neutral);
            case UserDecisionService.SkipVersionKind:
                return new(entry.OccurredAt, when, $"Skipped version {value}", Reason(reason), "\uE893", StatusTone.Neutral);
            case "ManualMapping":
                return new(entry.OccurredAt, when, $"Match confirmed: {entry.Summary.Replace("Manual mapping: ", string.Empty, StringComparison.Ordinal)}", Reason(reason), "\uE71B", StatusTone.Neutral);
            case "InventoryScan":
                return new(entry.OccurredAt, when, "Found in the latest scan", null, "\uE721", StatusTone.Neutral);
            case "Update":
                Match match = UpdateSummary().Match(entry.Summary);
                if (match.Success && Enum.TryParse(match.Groups["status"].Value, false, out UpdateHistoryStatus status) && Enum.IsDefined(status))
                {
                    string target = match.Groups["target"].Value;
                    // Final review Info-2: the same inputs as Activity (recorded diagnostic and exit code, which carry the
                    // provider-completed evidence), so one job shows the same display state in both places.
                    UpdateOutcomePresentation outcome = UpdateOutcomePresenter.ForStatus(status, entry.Reason, entry.ExitCode, target);
                    string title = status switch
                    {
                        UpdateHistoryStatus.Queued => $"Update to {target} queued",
                        UpdateHistoryStatus.Running => $"Update to {target} started",
                        _ => $"Update to {target}: {outcome.Label}"
                    };
                    bool intermediate = status is UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running;
                    return new(entry.OccurredAt, when, title, reason is null ? null : $"Recorded detail: {SanitizedDiagnosticBundle.Sanitize(reason)}",
                        intermediate ? "\uE895" : outcome.Glyph, intermediate ? StatusTone.Neutral : outcome.Tone);
                }
                return new(entry.OccurredAt, when, "Update recorded", reason is null ? null : SanitizedDiagnosticBundle.Sanitize(reason), "\uE81C", StatusTone.Neutral);
            default:
                return new(entry.OccurredAt, when, entry.Summary, Reason(reason), "\uE81C", StatusTone.Neutral);
        }
    }

    private static string? Reason(string? reason) => reason is null ? null : $"Reason: {reason}";

    // "Kind: value" summaries written by the decision store; the value is everything after the first ": ".
    private static string Value(string summary)
    {
        int separator = summary.IndexOf(": ", StringComparison.Ordinal);
        return separator < 0 ? summary : summary[(separator + 2)..];
    }

    [GeneratedRegex(@"^Update (?<status>[A-Za-z]+): (?<target>.*)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex UpdateSummary();
}
