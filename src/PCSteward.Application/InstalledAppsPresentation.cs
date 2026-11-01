using System.Globalization;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>The saved Installed apps filters (Task 73 Phase 3, ADR-030 decision 5: Unmanaged is a saved filter, not a page).</summary>
public enum InstalledAppsFilter
{
    All,
    Updates,
    NeedsReview,
    Unmanaged,
    PinnedAndIgnored
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
        [InstalledAppsFilter.NeedsReview] = [nameof(UpdateIntelligenceStatus.ReviewRequired), nameof(UpdateIntelligenceStatus.FailedUnavailable)],
        [InstalledAppsFilter.Unmanaged] = [nameof(UpdateIntelligenceStatus.Unmanaged)],
        [InstalledAppsFilter.PinnedAndIgnored] = [nameof(UpdateIntelligenceStatus.Pinned), nameof(UpdateIntelligenceStatus.Ignored)],
    };

    /// <summary>The filter's short label.</summary>
    public static string Label(InstalledAppsFilter filter) => filter switch
    {
        InstalledAppsFilter.Updates => "Updates",
        InstalledAppsFilter.NeedsReview => "Needs review",
        InstalledAppsFilter.Unmanaged => "Unmanaged",
        InstalledAppsFilter.PinnedAndIgnored => "Pinned & ignored",
        _ => "All"
    };

    /// <summary>True when a row with <paramref name="status"/> belongs in <paramref name="filter"/>. "All" matches every status.</summary>
    public static bool Matches(InstalledAppsFilter filter, string? status) =>
        !Statuses.TryGetValue(filter, out string[]? keys) || keys.Contains(status, StringComparer.Ordinal);

    /// <summary>The five saved filters in order, each with its count over every saved row.</summary>
    public static IReadOnlyList<InstalledAppsFilterOption> Options(IReadOnlyCollection<InventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Enum.GetValues<InstalledAppsFilter>().Select(filter =>
        {
            string[] labels = Statuses.TryGetValue(filter, out string[]? keys) ? keys.Select(key => StatusVocabulary.For(key).Label).ToArray() : [];
            string description = filter switch
            {
                InstalledAppsFilter.All => "every app in the saved inventory",
                InstalledAppsFilter.Unmanaged => $"{labels[0]}. {StatusVocabulary.For(nameof(UpdateIntelligenceStatus.Unmanaged)).Meaning}",
                _ => string.Join(", ", labels)
            };
            return new InstalledAppsFilterOption(filter, Label(filter), rows.Count(row => Matches(filter, row.Status)), labels, description);
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
                return new(entry.OccurredAt, when, $"Pinned at version {value}", Reason(reason), "", StatusTone.Neutral);
            case "PinSourcePackage":
                return new(entry.OccurredAt, when, $"Pinned to package {value}", Reason(reason), "", StatusTone.Neutral);
            case "Ignore":
                string until = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expires)
                    ? $"until {DisplayFormat.Date(expires, culture, zone)}" : "indefinitely";
                return new(entry.OccurredAt, when, $"Ignored {until}", Reason(reason), "", StatusTone.Neutral);
            case "ManualMapping":
                return new(entry.OccurredAt, when, $"Package identity confirmed: {entry.Summary.Replace("Manual mapping: ", string.Empty, StringComparison.Ordinal)}", Reason(reason), "", StatusTone.Neutral);
            case "InventoryScan":
                return new(entry.OccurredAt, when, "Found in the latest inventory scan", null, "", StatusTone.Neutral);
            case "Update":
                Match match = UpdateSummary().Match(entry.Summary);
                if (match.Success && Enum.TryParse(match.Groups["status"].Value, false, out UpdateHistoryStatus status) && Enum.IsDefined(status))
                {
                    string target = match.Groups["target"].Value;
                    string title = status switch
                    {
                        UpdateHistoryStatus.Queued => $"Update to {target} queued",
                        UpdateHistoryStatus.Running => $"Update to {target} started",
                        _ => $"Update to {target}: {UpdateOutcomePresenter.ForStatus(status, reason, null, target).Label}"
                    };
                    UpdateOutcomePresentation outcome = UpdateOutcomePresenter.ForStatus(status, reason, null, target);
                    bool intermediate = status is UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running;
                    return new(entry.OccurredAt, when, title, reason is null ? null : $"Recorded detail: {SanitizedDiagnosticBundle.Sanitize(reason)}",
                        intermediate ? "" : outcome.Glyph, intermediate ? StatusTone.Neutral : outcome.Tone);
                }
                return new(entry.OccurredAt, when, "Update recorded", reason is null ? null : SanitizedDiagnosticBundle.Sanitize(reason), "", StatusTone.Neutral);
            default:
                return new(entry.OccurredAt, when, entry.Summary, Reason(reason), "", StatusTone.Neutral);
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
