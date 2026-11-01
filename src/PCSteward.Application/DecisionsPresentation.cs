using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>What a recorded decision is.</summary>
public enum DecisionKind
{
    Pin,
    Ignore,
    Mapping
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
                _ => new(decision.Id, DecisionKind.Pin, decision.StableProductKey, product,
                    $"Pinned at version {decision.Value}", $"No other version is offered for this app. {created}.", Reason(decision.Reason), decision.CreatedAt, "\uE718",
                    "Remove pin", PageFocusRestore.RemoveDecisionName(product, "pin current version")),
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
        _ => $"Confirmed package identities ({count})"
    };

    private static string? Reason(string? reason) => string.IsNullOrWhiteSpace(reason)
        ? null
        : reason.Trim()[..Math.Min(reason.Trim().Length, MaximumReasonLength)];
}
