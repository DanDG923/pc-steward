using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>What a recorded decision is.</summary>
public enum DecisionKind
{
    Pin,
    Ignore,
    Mapping,
    /// <summary>Owner decision 2026-11-02: Skip this version.</summary>
    Skip,
    /// <summary>A stored decision this version doesn't recognise (for example from a newer PC Steward). It is shown, never guessed.</summary>
    Other
}

/// <summary>
/// One reversible decision on the Decisions page. <see cref="RemoveLabel"/> is the visible button text and
/// <see cref="RemoveAccessibleName"/> starts with it and names the decision kind and the product, so each Remove button for an
/// app is unique and focus can be restored to it (Phase 1 focus rule; label-in-name; independent review L5).
/// </summary>
public sealed record DecisionRow(
    Guid Id,
    DecisionKind Kind,
    string StableProductKey,
    string ProductName,
    string Title,
    string Detail,
    string? Reason,
    DateTimeOffset CreatedAt,
    string Glyph,
    string RemoveLabel,
    string RemoveAccessibleName);

/// <summary>One "What each decision does" entry on the Decisions page: a glyph, the decision's name and what it does.</summary>
public sealed record DecisionExplainer(string Glyph, string Title, string Description);

/// <summary>
/// FR-POL-01–05 (Task 73 Phase 3, ADR-030 foundation 7): pins, ignores (indefinitely or until a date) and confirmed package identities
/// in one view, each with its reason and its existing reversal. Presentation only: building this list changes nothing.
/// </summary>
public static class DecisionsPresenter
{
    /// <summary>The longest reason the decision store keeps (FR-POL-03); longer text is never shown because it is never saved.</summary>
    public const int MaximumReasonLength = 512;

    public static IReadOnlyList<DecisionRow> Build(
        IEnumerable<StableDecision> decisions,
        IEnumerable<ManualMapping> mappings,
        IReadOnlyCollection<InventoryRow> rows,
        CultureInfo? culture = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(rows);
        List<DecisionRow> result = [];
        foreach (StableDecision decision in decisions)
        {
            string product = PageFocusRestore.ProductName(rows, decision.StableProductKey);
            string created = $"Saved {DisplayFormat.Date(decision.CreatedAt, culture, zone)}";
            DecisionRow row = decision.Kind switch
            {
                "Ignore" => new(decision.Id, DecisionKind.Ignore, decision.StableProductKey, product,
                    decision.ExpiresAt is { } until ? $"Ignored until {DisplayFormat.Date(until, culture, zone)}" : "Ignored indefinitely",
                    $"Updates for this app aren't shown. {created}.", Reason(decision.Reason), decision.CreatedAt, "\uE74F",
                    "Remove ignore", PageFocusRestore.RemoveDecisionName(product, decision.ExpiresAt is { } ignoreUntil ? $"ignore until {DisplayFormat.Date(ignoreUntil, culture, zone)}" : "ignore indefinitely")),
                "PinSourcePackage" => new(decision.Id, DecisionKind.Pin, decision.StableProductKey, product,
                    $"Pinned to package {decision.Value}", $"No other package is offered for this app. {created}.", Reason(decision.Reason), decision.CreatedAt, "\uE718",
                    "Remove pin", PageFocusRestore.RemoveDecisionName(product, "pin source package")),
                "PinCurrentVersion" => new(decision.Id, DecisionKind.Pin, decision.StableProductKey, product,
                    $"Pinned at version {decision.Value}", $"No other version is offered for this app. {created}.", Reason(decision.Reason), decision.CreatedAt, "\uE718",
                    "Remove pin", PageFocusRestore.RemoveDecisionName(product, "pin current version")),
                UserDecisionService.SkipVersionKind => new(decision.Id, DecisionKind.Skip, decision.StableProductKey, product,
                    $"Skipping version {decision.Value}", $"Version {decision.Value} isn't offered for this app. A newer version will be offered again. {created}.",
                    Reason(decision.Reason), decision.CreatedAt, "\uE893",
                    $"Stop skipping {decision.Value}", $"Stop skipping {decision.Value} for {product}"),
                // An unrecognised kind (for example from a newer PC Steward) is shown as what it is, never as a pin it may not be.
                _ => new(decision.Id, DecisionKind.Other, decision.StableProductKey, product,
                    "Saved decision (not recognised)", $"This version of PC Steward doesn't recognise this decision, so it has no effect here. {created}.",
                    Reason(decision.Reason), decision.CreatedAt, "\uE946",
                    "Remove decision", $"Remove decision (not recognised) saved {DisplayFormat.DateTime(decision.CreatedAt, culture, zone)} for {product}"),
            };
            result.Add(row);
        }
        foreach (ManualMapping mapping in mappings)
        {
            string product = PageFocusRestore.ProductName(rows, mapping.StableProductKey);
            result.Add(new(mapping.Id, DecisionKind.Mapping, mapping.StableProductKey, product,
                $"Package identity confirmed: {mapping.SourceId}:{mapping.PackageId}",
                $"You confirmed this package is the installed app. Saved {DisplayFormat.Date(mapping.CreatedAt, culture, zone)}.",
                Reason(mapping.Reason), mapping.CreatedAt, "\uE71B",
                "Remove manual mapping", PageFocusRestore.RemoveManualMappingName(product, mapping.SourceId, mapping.PackageId)));
        }
        return result
            .OrderBy(row => row.ProductName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.Kind)
            .ThenByDescending(row => row.CreatedAt)
            .ToArray();
    }

    /// <summary>A heading for each kind with its count, for example "Pins (2)".</summary>
    public static string GroupHeading(DecisionKind kind, int count) => kind switch
    {
        DecisionKind.Pin => $"Pins ({count})",
        DecisionKind.Ignore => $"Ignored apps ({count})",
        DecisionKind.Skip => $"Skipped versions ({count})",
        DecisionKind.Other => $"Other saved decisions ({count})",
        _ => $"Confirmed package identities ({count})"
    };

    /// <summary>
    /// UI polish phase 4 (approved target T-Decisions): the page description. Every decision is reversible and none starts an update.
    /// </summary>
    public const string PageDescription = "Pins, ignores, skipped versions and confirmed packages. Each one is reversible, and none starts an update.";

    /// <summary>The empty state's heading and sentence (approved target T-Decisions).</summary>
    public const string EmptyTitle = "No decisions yet";

    public const string EmptyMessage = "When you pin an app, ignore its updates or skip a version, it's listed here so you can undo it at any time.";

    /// <summary>The footnote under the list when decisions exist.</summary>
    public const string RemoveNote = "Removing a decision applies the normal review rules again at the next source check. Nothing is updated when you do.";

    /// <summary>
    /// "What each decision does" (approved target T-Decisions): one explainer per kind people can choose, with the same glyphs the
    /// decision rows use. Presentation only.
    /// </summary>
    public static IReadOnlyList<DecisionExplainer> Explainers { get; } =
    [
        new("\uE718", "Pin", "Keep an app on its current version. No updates are offered until you unpin it."),
        new("\uE893", "Skip this version", "Hide one specific version. The next newer version is offered as normal."),
        new("\uE74F", "Ignore updates for this app", "Stop offering updates for an app, for good or until a date you choose."),
        new("\uE71B", "Confirm package", "Tell PC Steward which package an app is when its match needs your review."),
    ];

    private static string? Reason(string? reason) => string.IsNullOrWhiteSpace(reason)
        ? null
        : reason.Trim()[..Math.Min(reason.Trim().Length, MaximumReasonLength)];
}
