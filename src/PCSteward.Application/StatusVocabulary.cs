using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>How a status chip is coloured. Colour is never the only signal: every chip also has a glyph and a text label.</summary>
public enum StatusTone
{
    Neutral,
    Information,
    Success,
    Attention,
    Critical
}

/// <summary>One status chip: the label, a Segoe Fluent glyph, a tone and a plain-language meaning.</summary>
public sealed record StatusChipPresentation(string Key, string Label, string Glyph, StatusTone Tone, string Meaning)
{
    /// <summary>The chip's accessible name. It always contains the visible label.</summary>
    public string AccessibleName(string? subject = null) => string.IsNullOrWhiteSpace(subject) ? $"Status: {Label}" : $"{subject}: {Label}";
}

/// <summary>
/// UI review 2026-11-13 (item 5): one short name per state, used on every chip, filter, heading and count — "Update ready",
/// "Check the match", "Couldn't check", "Up to date", "No update source", "Not checked yet", "Pinned", "Ignored", "Skipped".
/// FR-UI-01/05, AC-UX-03 (Task 73 Phase 1, ADR-030 foundation 2): the single status vocabulary. It maps the SPEC §7.2 user-facing
/// inventory statuses to one label set used by every list, chip and filter, so filters show the same words as rows and never raw enum
/// names. Presentation only: it never decides whether anything is actionable.
/// </summary>
public static class StatusVocabulary
{
    /// <summary>Key for an inventoried row whose sources have not been checked.</summary>
    public const string InstalledKey = "Installed";

    // Task 73 final review L3: the unreferenced outcome/product-fact chips (Self-managed, Unknown version, Update failed, Restart
    // required) were removed. Update outcomes, including "Restart required" (Information tone), are presented only by
    // UpdateOutcomePresenter, so there is one tone per outcome and no flattened "Update failed" chip.

    /// <summary>
    /// UI polish phase 2 (owner-approved targets, 2026-11-11): the user-facing name of the <c>Unmanaged</c> status. The stored status
    /// key stays <c>Unmanaged</c>; only the words on screen change.
    /// </summary>
    public const string NoUpdateSourceLabel = "No update source";

    /// <summary>The filter entry that shows every status.</summary>
    public const string AllStatusesLabel = "All statuses";

    private static readonly StatusChipPresentation Unavailable = new("Unknown", "Status unavailable", "", StatusTone.Neutral, "PC Steward has no status for this item.");

    private static readonly Dictionary<string, StatusChipPresentation> Chips = new StatusChipPresentation[]
    {
        new(nameof(UpdateIntelligenceStatus.VerifiedUpdate), "Update ready", "", StatusTone.Information,
            "A newer version is available and the match is exact. Nothing is installed until you review and confirm it."),
        new(nameof(UpdateIntelligenceStatus.ReviewRequired), "Check the match", "", StatusTone.Attention,
            "PC Steward found a likely update but isn't sure it belongs to this app. It can't be updated until you confirm the match."),
        new(nameof(UpdateIntelligenceStatus.FailedUnavailable), "Couldn't check", "", StatusTone.Attention,
            "PC Steward couldn't reach the update source for this app. Check again to retry; it can't be updated until then."),
        new(nameof(UpdateIntelligenceStatus.Current), "Up to date", "", StatusTone.Success,
            "The installed version is the newest one the update source offers."),
        new(nameof(UpdateIntelligenceStatus.Unmanaged), NoUpdateSourceLabel, "", StatusTone.Neutral,
            "PC Steward doesn't know where to get updates for this app, so it only lists it. This is not a safety warning."),
        new(nameof(UpdateIntelligenceStatus.Pinned), "Pinned", "", StatusTone.Neutral,
            "You chose to stay on this version. No update is offered until you remove the pin."),
        new(nameof(UpdateIntelligenceStatus.Ignored), "Ignored", "", StatusTone.Neutral,
            "You chose not to be told about updates for this app. It stays on the Installed apps list."),
        new(nameof(UpdateIntelligenceStatus.Skipped), "Skipped", "\uE893", StatusTone.Neutral,
            "You chose to skip this version. The next newer version will be offered; you can stop skipping at any time."),
        new(InstalledKey, "Not checked yet", "", StatusTone.Neutral,
            "Found on this PC. PC Steward hasn't looked for updates for it yet.")
    }.ToDictionary(chip => chip.Key, StringComparer.Ordinal);

    /// <summary>Inventory statuses in filter order: what can be acted on first, then everything else.</summary>
    public static IReadOnlyList<StatusChipPresentation> InventoryStatuses { get; } =
    [
        Chips[nameof(UpdateIntelligenceStatus.VerifiedUpdate)],
        Chips[nameof(UpdateIntelligenceStatus.ReviewRequired)],
        Chips[nameof(UpdateIntelligenceStatus.FailedUnavailable)],
        Chips[nameof(UpdateIntelligenceStatus.Current)],
        Chips[nameof(UpdateIntelligenceStatus.Unmanaged)],
        Chips[nameof(UpdateIntelligenceStatus.Pinned)],
        Chips[nameof(UpdateIntelligenceStatus.Ignored)],
        Chips[nameof(UpdateIntelligenceStatus.Skipped)],
        Chips[InstalledKey]
    ];

    /// <summary>Every chip in the vocabulary.</summary>
    public static IReadOnlyCollection<StatusChipPresentation> All => Chips.Values;

    /// <summary>Status filter entries: "All statuses" followed by the same labels the rows show.</summary>
    public static IReadOnlyList<string> FilterLabels { get; } = [AllStatusesLabel, .. InventoryStatuses.Select(chip => chip.Label)];

    /// <summary>The chip for a row status key (an <see cref="UpdateIntelligenceStatus"/> name or <see cref="InstalledKey"/>).</summary>
    public static StatusChipPresentation For(string? key) => key is not null && Chips.TryGetValue(key, out StatusChipPresentation? chip) ? chip : Unavailable;

    public static StatusChipPresentation For(UpdateIntelligenceStatus status) => For(status.ToString());

    /// <summary>
    /// Where a status sorts (UI review 2026-11-13, item 18): the order the filter lists them in, which puts what can be acted on
    /// first (Update ready, then Check the match). An unknown status sorts last.
    /// </summary>
    public static int SortRank(string? key)
    {
        for (int index = 0; index < InventoryStatuses.Count; index++)
            if (string.Equals(InventoryStatuses[index].Key, key, StringComparison.Ordinal)) return index;
        return InventoryStatuses.Count;
    }

    /// <summary>Maps a filter label back to the status key the inventory filter uses; unknown labels mean every status.</summary>
    public static string FilterKeyForLabel(string? label) =>
        InventoryStatuses.FirstOrDefault(chip => string.Equals(chip.Label, label, StringComparison.Ordinal))?.Key ?? AllStatusesLabel;

    /// <summary>Maps a stored filter key to its visible label.</summary>
    public static string FilterLabelForKey(string? key) =>
        InventoryStatuses.FirstOrDefault(chip => string.Equals(chip.Key, key, StringComparison.Ordinal))?.Label ?? AllStatusesLabel;
}
