using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// UI review 2026-11-13 (item 12; FR-POL-03/05, FR-UI-01): what the page says after a pin, ignore, skip or confirmed match, in
/// place of a second dialog. <see cref="DecisionIds"/> and <see cref="MappingIds"/> are the records that were just saved, so
/// "Undo" reverses exactly those through the existing reversal operations.
/// </summary>
public sealed record DecisionNotice(string Message, IReadOnlyList<Guid> DecisionIds, IReadOnlyList<Guid> MappingIds)
{
    public const string UndoLabel = "Undo";

    /// <summary>False when the saved record couldn't be found again; the line is still shown, without Undo.</summary>
    public bool CanUndo => DecisionIds.Count + MappingIds.Count > 0;

    /// <summary>The Undo button's accessible name, which starts with its visible label.</summary>
    public string UndoAccessibleName => $"{UndoLabel}: {Message}";
}

/// <summary>
/// Builds the line shown after a decision is saved. The ids come from the decisions as reloaded after the save, newest first for
/// each app, so Undo can never reverse an older decision of another kind. Presentation only.
/// </summary>
public static class DecisionNoticePresenter
{
    /// <summary>What is announced after Undo.</summary>
    public const string Undone = "Undone.";

    public static DecisionNotice Pinned(string name, string version, IEnumerable<string> keys, IReadOnlyCollection<StableDecision> active) =>
        new($"{name} is pinned at version {version}.", Newest(active, keys, "PinCurrentVersion"), []);

    public static DecisionNotice Ignored(string name, DateTimeOffset? until, IEnumerable<string> keys, IReadOnlyCollection<StableDecision> active,
        CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        new(until is { } date ? $"Updates for {name} are ignored until {DisplayFormat.Date(date, culture, zone)}." : $"Updates for {name} are ignored.",
            Newest(active, keys, "Ignore"), []);

    public static DecisionNotice Skipped(string name, string version, IEnumerable<string> keys, IReadOnlyCollection<StableDecision> active) =>
        new($"Version {version} of {name} is skipped. A newer version will be offered.", Newest(active, keys, UserDecisionService.SkipVersionKind), []);

    /// <summary>
    /// <paramref name="savedBefore"/> holds the ids of the matches that were already saved before this confirmation. Confirming a
    /// match that was already saved adds nothing, so the line says so and offers no Undo: Undo must never remove an earlier decision.
    /// </summary>
    public static DecisionNotice MatchConfirmed(string name, string stableProductKey, string sourceId, string packageId,
        IReadOnlyCollection<ManualMapping> mappings, IReadOnlyCollection<Guid> savedBefore)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(savedBefore);
        Guid[] ids = [.. mappings
            .Where(mapping => string.Equals(mapping.StableProductKey, stableProductKey, StringComparison.Ordinal)
                && string.Equals(mapping.SourceId, sourceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(mapping.PackageId, packageId, StringComparison.OrdinalIgnoreCase)
                && !savedBefore.Contains(mapping.Id))
            .OrderByDescending(mapping => mapping.CreatedAt).Take(1).Select(mapping => mapping.Id)];
        return new(ids.Length > 0 ? $"{packageId} is now matched to {name}." : $"{packageId} was already matched to {name}.", [], ids);
    }

    private static Guid[] Newest(IReadOnlyCollection<StableDecision> active, IEnumerable<string> keys, string kind)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(keys);
        return [.. keys.Distinct(StringComparer.Ordinal).Select(key => active
                .Where(decision => string.Equals(decision.StableProductKey, key, StringComparison.Ordinal) && string.Equals(decision.Kind, kind, StringComparison.Ordinal))
                .OrderByDescending(decision => decision.CreatedAt).FirstOrDefault())
            .OfType<StableDecision>().Select(decision => decision.Id)];
    }
}
