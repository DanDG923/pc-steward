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
/// FR-UI-01/05, AC-UX-03 (Task 73 Phase 1, ADR-030 foundation 2): the single status vocabulary. It maps the SPEC §7.2 user-facing
/// statuses to one label set used by every list, chip and filter, so filters show the same words as rows and never raw enum
/// names. Presentation only: it never decides whether anything is actionable.
/// </summary>
public static class StatusVocabulary
{
    /// <summary>Key for an inventoried row whose sources have not been checked.</summary>
    public const string InstalledKey = "Installed";

    /// <summary>SPEC §7.2 statuses that come from outcomes or product facts rather than <see cref="UpdateIntelligenceStatus"/>.</summary>
    public const string SelfManagedKey = "SelfManaged";
    public const string UnknownVersionKey = "UnknownVersion";
    public const string UpdateFailedKey = "UpdateFailed";
    public const string RestartRequiredKey = "RestartRequired";

    /// <summary>The filter entry that shows every status.</summary>
    public const string AllStatusesLabel = "All statuses";

    private static readonly StatusChipPresentation Unavailable = new("Unknown", "Status unavailable", "", StatusTone.Neutral, "PC Steward has no status for this item.");

    private static readonly Dictionary<string, StatusChipPresentation> Chips = new StatusChipPresentation[]
    {
        new(nameof(UpdateIntelligenceStatus.VerifiedUpdate), "Update available — verified", "", StatusTone.Information,
            "A newer compatible package has exact, fresh source evidence. It still needs your review and confirmation."),
        new(nameof(UpdateIntelligenceStatus.ReviewRequired), "Update available — review", "", StatusTone.Attention,
            "A likely update exists, but the match or channel needs your review. It cannot be updated from here until that is resolved."),
        new(nameof(UpdateIntelligenceStatus.FailedUnavailable), "Source unavailable", "", StatusTone.Attention,
            "The package source could not complete its check. Refresh source evidence to try again; nothing can be updated."),
        new(nameof(UpdateIntelligenceStatus.Current), "Current", "", StatusTone.Success,
            "The installed version is the same as, or newer than, the latest compatible trusted package."),
        new(nameof(UpdateIntelligenceStatus.Unmanaged), "Unmanaged", "", StatusTone.Neutral,
            "Inventoried only: no approved update source is mapped. This is not a safety warning."),
        new(nameof(UpdateIntelligenceStatus.Pinned), "Pinned", "", StatusTone.Neutral,
            "You chose to stay on this version. No update is offered until you remove the pin."),
        new(nameof(UpdateIntelligenceStatus.Ignored), "Ignored", "", StatusTone.Neutral,
            "You chose not to be told about updates for this app. It stays in the inventory."),
        new(InstalledKey, "Source not checked", "", StatusTone.Neutral,
            "Found on this PC. No package source has been checked for it yet."),
        new(SelfManagedKey, "Self-managed", "", StatusTone.Neutral,
            "This app is expected to update itself through its own updater."),
        new(UnknownVersionKey, "Unknown version", "", StatusTone.Neutral,
            "The installed version could not be read reliably, so no update can be offered."),
        new(UpdateFailedKey, "Update failed", "", StatusTone.Critical,
            "An update attempt failed or could not be verified. View the details and try again."),
        new(RestartRequiredKey, "Restart required", "", StatusTone.Attention,
            "Restart Windows to finish the update and verification.")
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
        Chips[InstalledKey]
    ];

    /// <summary>Every chip in the vocabulary.</summary>
    public static IReadOnlyCollection<StatusChipPresentation> All => Chips.Values;

    /// <summary>Status filter entries: "All statuses" followed by the same labels the rows show.</summary>
    public static IReadOnlyList<string> FilterLabels { get; } = [AllStatusesLabel, .. InventoryStatuses.Select(chip => chip.Label)];

    /// <summary>The chip for a row status key (an <see cref="UpdateIntelligenceStatus"/> name or <see cref="InstalledKey"/>).</summary>
    public static StatusChipPresentation For(string? key) => key is not null && Chips.TryGetValue(key, out StatusChipPresentation? chip) ? chip : Unavailable;

    public static StatusChipPresentation For(UpdateIntelligenceStatus status) => For(status.ToString());

    /// <summary>Maps a filter label back to the status key the inventory filter uses; unknown labels mean every status.</summary>
    public static string FilterKeyForLabel(string? label) =>
        InventoryStatuses.FirstOrDefault(chip => string.Equals(chip.Label, label, StringComparison.Ordinal))?.Key ?? AllStatusesLabel;

    /// <summary>Maps a stored filter key to its visible label.</summary>
    public static string FilterLabelForKey(string? key) =>
        InventoryStatuses.FirstOrDefault(chip => string.Equals(chip.Key, key, StringComparison.Ordinal))?.Label ?? AllStatusesLabel;
}
