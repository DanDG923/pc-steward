namespace PCSteward.Application;

/// <summary>
/// AC-UX-02, FR-UI-02 (Task 73 Phase 1; independent review M3): portable rules for restoring keyboard focus after a page is
/// refreshed and for the accessible names of repeated per-row actions. Focus goes back to a control only when exactly one
/// control on the rebuilt page carries the remembered name; otherwise the Desktop focuses the start of the page instead of
/// guessing. Per-row action names include the product name so that rows can be told apart. Presentation only; it decides nothing.
/// </summary>
public static class PageFocusRestore
{
    /// <summary>The single candidate, or null when there are none or more than one.</summary>
    public static T? UniqueMatch<T>(IEnumerable<T> candidates) where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        T? found = null;
        foreach (T candidate in candidates)
        {
            if (found is not null) return null;
            found = candidate;
        }
        return found;
    }

    /// <summary>The display name for a stable product key, falling back to the key itself when the product is not loaded.</summary>
    public static string ProductName(IEnumerable<InventoryRow> rows, string stableProductKey)
    {
        ArgumentNullException.ThrowIfNull(rows);
        string? name = rows.FirstOrDefault(row => string.Equals(row.StableProductKey, stableProductKey, StringComparison.Ordinal))?.Name;
        return !string.IsNullOrWhiteSpace(name) ? name
            : !string.IsNullOrWhiteSpace(stableProductKey) ? stableProductKey
            : "an unknown app";
    }

    /// <summary>Accessible name of the per-row Remove button for a pin or ignore decision.</summary>
    public static string RemoveDecisionName(string productName, string kind) => $"Remove {kind} decision for {productName}";

    /// <summary>Accessible name of the per-row Remove button for a manual mapping.</summary>
    public static string RemoveManualMappingName(string productName, string sourceId, string packageId) =>
        $"Remove manual mapping {sourceId}:{packageId} for {productName}";
}
