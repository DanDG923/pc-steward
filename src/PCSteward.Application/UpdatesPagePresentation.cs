using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>The three Updates page groups (ADR-030 decision 2, concept B). Only <see cref="Ready"/> can be selected.</summary>
public enum UpdateGroup
{
    Ready,
    NeedsReview,
    IgnoredAndSkipped
}

/// <summary>How big the version change is. <see cref="Unknown"/> shows no badge.</summary>
public enum VersionJump
{
    Unknown,
    Patch,
    Minor,
    Major
}

/// <summary>A version-jump badge: icon plus text, never colour alone. Major is highlighted.</summary>
public sealed record VersionJumpBadge(VersionJump Jump, string Label, string Glyph, StatusTone Tone, bool IsHighlighted)
{
    /// <summary>The badge's accessible name; it contains the visible label.</summary>
    public string AccessibleName => $"Version change: {Label}";
}

/// <summary>
/// FR-UPD-01, FR-UI-04 (Task 73 Phase 2): classifies the jump between the installed and the new version for a badge. It is
/// presentation only and decides nothing: a badge never makes an update actionable. Only plain numeric dotted versions
/// (for example 1.2.3) that the domain comparison reports as newer get a badge; anything else is <see cref="VersionJump.Unknown"/>.
/// </summary>
public static class VersionJumpPresenter
{
    private const int MaximumComponents = 8;

    public static VersionJump Classify(string? installedVersion, string? newVersion)
    {
        if (!TryParse(installedVersion, out uint[] installed) || !TryParse(newVersion, out uint[] candidate)) return VersionJump.Unknown;
        VersionComparisonResult comparison = VersionComparison.Compare(new VersionComparisonRequest(installedVersion, newVersion));
        if (comparison.Outcome != VersionComparisonOutcome.Newer) return VersionJump.Unknown;
        int length = Math.Max(installed.Length, candidate.Length);
        for (int index = 0; index < length; index++)
        {
            uint left = index < installed.Length ? installed[index] : 0;
            uint right = index < candidate.Length ? candidate[index] : 0;
            if (left == right) continue;
            return index switch { 0 => VersionJump.Major, 1 => VersionJump.Minor, _ => VersionJump.Patch };
        }
        return VersionJump.Unknown;
    }

    /// <summary>The badge to show, or null for <see cref="VersionJump.Unknown"/> (no badge).</summary>
    public static VersionJumpBadge? Badge(VersionJump jump) => jump switch
    {
        VersionJump.Patch => new(jump, "Patch", "", StatusTone.Neutral, false),
        VersionJump.Minor => new(jump, "Minor", "", StatusTone.Information, false),
        VersionJump.Major => new(jump, "Major", "", StatusTone.Attention, true),
        _ => null
    };

    private static bool TryParse(string? version, out uint[] components)
    {
        components = [];
        if (string.IsNullOrWhiteSpace(version)) return false;
        string[] parts = version.Trim().Split('.');
        if (parts.Length > MaximumComponents) return false;
        uint[] parsed = new uint[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length == 0 || !parts[index].All(char.IsAsciiDigit) ||
                !uint.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[index])) return false;
        }
        components = parsed;
        return true;
    }
}

/// <summary>Plain-language reasons for review-only evidence (ADR-030 foundation 9: no raw enum names in user copy).</summary>
public static class ReviewReasonText
{
    private static readonly Dictionary<string, string> Reasons = new(StringComparer.Ordinal)
    {
        [nameof(MappingReviewReason.Unmatched)] = "No package matched this app.",
        [nameof(MappingReviewReason.MissingPackageId)] = "The source gave no package ID.",
        [nameof(MappingReviewReason.UntrustedConnector)] = "The match came from a source you haven't approved.",
        [nameof(MappingReviewReason.UnsupportedManualMapping)] = "The package identity you confirmed can't be used for updates.",
        [nameof(MappingReviewReason.AmbiguousPublisher)] = "The publisher name is ambiguous.",
        [nameof(MappingReviewReason.PublisherConflict)] = "The publisher doesn't match the installed app.",
        [nameof(MappingReviewReason.ArchitectureConflict)] = "The package is for a different architecture than the installed app.",
        [nameof(MappingReviewReason.ScopeConflict)] = "The package installs for a different scope (current user or all users) than the installed app.",
        [nameof(MappingReviewReason.ChannelConflict)] = "The package is from a different release channel (for example preview instead of stable).",
        [nameof(MappingReviewReason.IncomparableVersion)] = "The installed and new versions can't be compared reliably.",
        [nameof(MappingReviewReason.ConnectorSpecificVersionSemantics)] = "This source numbers versions its own way, so they can't be compared here.",
        [nameof(MappingReviewReason.CandidateIsCurrent)] = "The source's version is the same as the installed one.",
        [nameof(MappingReviewReason.CandidateIsOlder)] = "The source's version is older than the installed one.",
        [nameof(MappingReviewReason.AmbiguousCandidate)] = "More than one package could match this app.",
        [nameof(MappingReviewReason.SourceConflict)] = "The package sources give conflicting information.",
        [nameof(MappingReviewReason.MissingSourceEvidence)] = "The source evidence is missing or out of date.",
        [nameof(MappingReviewReason.MultipleInstallationInstanceAmbiguity)] = "More than one installation of this app was found.",
        [nameof(MappingReviewReason.SourceDisagreement)] = "Package sources disagree about this app.",
        [nameof(MappingReviewReason.MissingScopeEvidence)] = "The install scope isn't known.",
        [nameof(MappingReviewReason.MissingArchitectureEvidence)] = "The app's architecture isn't known.",
        [nameof(MappingReviewReason.MissingChannelEvidence)] = "The release channel isn't known."
    };

    /// <summary>Words for one recorded review reason. <c>None</c> has no words; an unrecognised reason gets a generic sentence.</summary>
    public static string? Describe(string reason) =>
        string.Equals(reason, nameof(MappingReviewReason.None), StringComparison.Ordinal) ? null
            : Reasons.TryGetValue(reason, out string? text) ? text : "Another evidence check needs your review.";

    /// <summary>Words for every recorded reason, without duplicates, in the order they were recorded.</summary>
    public static IReadOnlyList<string> DescribeAll(IEnumerable<string> reasons) =>
        reasons.Select(Describe).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>The confidence level in words. The level itself comes from the domain; this never changes it.</summary>
    public static string Confidence(string? confidence) => confidence switch
    {
        nameof(MappingConfidence.High) => "High confidence",
        nameof(MappingConfidence.Medium) => "Medium confidence",
        nameof(MappingConfidence.Low) => "Low confidence",
        nameof(MappingConfidence.None) => "No confident match",
        _ => "Confidence not available"
    };
}

/// <summary>An action in a row's overflow menu. <see cref="SkipVersion"/> is the owner-accepted Skip this version decision (2026-11-02).</summary>
public enum UpdateRowAction
{
    IgnoreApp,
    PinCurrentVersion,
    Details,
    Reverse,
    SkipVersion
}

/// <summary>
/// One overflow-menu entry. The wording keeps "ignore this app", "pin this version" and "skip this version" clearly different.
/// <see cref="Version"/> is the exact version a Skip this version entry records (null for every other action).
/// </summary>
public sealed record UpdateRowActionPresentation(UpdateRowAction Action, string Label, string Glyph, string? Version = null);

/// <summary>One row on the Updates page.</summary>
public sealed record UpdateRowPresentation(
    string StableProductKey,
    string Name,
    string Publisher,
    string Status,
    UpdateGroup Group,
    string InstalledVersion,
    string? NewVersion,
    VersionJumpBadge? Badge,
    IReadOnlyList<string> Reasons,
    string Freshness,
    IReadOnlyList<UpdateRowActionPresentation> Actions,
    Guid? DecisionId = null)
{
    /// <summary>Only Ready rows can be selected for review. Selection is not approval.</summary>
    public bool CanSelect => Group == UpdateGroup.Ready;

    /// <summary>
    /// SPEC §7.3 / audit §6.4: needs-review and ignored rows have no update control at all — not a disabled one. The Desktop builds
    /// a selection control only when this is true.
    /// </summary>
    public bool HasUpdateControl => Group == UpdateGroup.Ready;

    /// <summary>"installed → new" in words, or the installed version alone when no new version is established.</summary>
    public string VersionText => NewVersion is null ? InstalledVersion : $"{InstalledVersion} → {NewVersion}";

    /// <summary>The reason in words, as one sentence block (empty for Ready rows).</summary>
    public string ReasonText => string.Join(" ", Reasons);
}

/// <summary>The grouped Updates page. Nothing is preselected: <see cref="SelectedKey"/> is null unless the user chose a Ready row.</summary>
public sealed record UpdatesPagePresentation(
    IReadOnlyList<UpdateRowPresentation> Ready,
    IReadOnlyList<UpdateRowPresentation> NeedsReview,
    IReadOnlyList<UpdateRowPresentation> IgnoredAndSkipped,
    string? SelectedKey)
{
    public UpdateRowPresentation? Selected => SelectedKey is null ? null : Ready.SingleOrDefault(row => row.StableProductKey == SelectedKey);

    /// <summary>The review button is enabled only once the user has chosen one Ready row.</summary>
    public bool CanReview => Selected is not null;

    /// <summary>A specific label: it names the chosen app. Opening the review never starts an update.</summary>
    public string ReviewButtonLabel => Selected is { } row ? $"Review update for {row.Name}" : "Review update";

    public bool HasAnyRow => Ready.Count + NeedsReview.Count + IgnoredAndSkipped.Count > 0;
}

/// <summary>
/// FR-UPD-01, FR-POL-01–05, FR-UI-04 (Task 73 Phase 2, ADR-030 decision 2): groups update evidence into Ready, Needs review and
/// Ignored and skipped. It never decides actionability itself: Ready is exactly the domain's <c>VerifiedUpdate</c> status, and it
/// fails closed — a verified row without complete evidence, or while sources are off, goes to Needs review with a reason.
/// </summary>
public static class UpdatesPagePresenter
{
    private static readonly UpdateRowActionPresentation Ignore = new(UpdateRowAction.IgnoreApp, "Ignore updates for this app", "");
    private static readonly UpdateRowActionPresentation Pin = new(UpdateRowAction.PinCurrentVersion, "Pin current version", "");
    private static readonly UpdateRowActionPresentation Details = new(UpdateRowAction.Details, "Details", "");

    public static UpdatesPagePresentation Build(
        IReadOnlyList<InventoryRow> rows,
        IReadOnlyDictionary<string, UpdateDetail> details,
        bool sourcesEnabled,
        IReadOnlyCollection<StableDecision> decisions,
        string? selectedKey,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(decisions);
        List<UpdateRowPresentation> ready = [], review = [], ignored = [];
        foreach (InventoryRow row in rows)
        {
            UpdateDetail? detail = details.GetValueOrDefault(row.StableProductKey);
            switch (row.Status)
            {
                case nameof(UpdateIntelligenceStatus.VerifiedUpdate):
                    if (sourcesEnabled && detail is { CandidateVersion: not null, PackageId: not null, SourceId: not null })
                        ready.Add(Row(row, detail, UpdateGroup.Ready, [], WithSkip([Ignore, Pin, Details], detail)));
                    else
                        review.Add(Row(row, detail, UpdateGroup.NeedsReview,
                            [sourcesEnabled ? "The update evidence is incomplete, so it can't be reviewed." : "Package sources are off, so this update can't be reviewed."],
                            WithSkip([Ignore, Pin, Details], detail)));
                    break;
                case nameof(UpdateIntelligenceStatus.ReviewRequired):
                    review.Add(Row(row, detail, UpdateGroup.NeedsReview, NeedsReviewReasons(detail), WithSkip([Ignore, Pin, Details], detail)));
                    break;
                case nameof(UpdateIntelligenceStatus.FailedUnavailable):
                    review.Add(Row(row, detail, UpdateGroup.NeedsReview,
                        ["The package source couldn't confirm whether an update is available. Refresh source evidence to try again."], [Details]));
                    break;
                case nameof(UpdateIntelligenceStatus.Ignored):
                    StableDecision? ignore = Latest(decisions, row.StableProductKey, "Ignore");
                    string until = ignore?.ExpiresAt is { } expires && expires > now
                        ? $"You chose not to be told about updates for this app until {DisplayFormat.Date(expires)}."
                        : "You chose not to be told about updates for this app.";
                    ignored.Add(Row(row, detail, UpdateGroup.IgnoredAndSkipped, [until],
                        ignore is null ? [Details] : [new(UpdateRowAction.Reverse, "Stop ignoring this app", ""), Details], ignore?.Id));
                    break;
                case nameof(UpdateIntelligenceStatus.Pinned):
                    StableDecision? pin = Latest(decisions, row.StableProductKey, "PinCurrentVersion", "PinSourcePackage");
                    ignored.Add(Row(row, detail, UpdateGroup.IgnoredAndSkipped,
                        [pin is { Kind: "PinCurrentVersion" } ? $"You pinned this app at version {pin.Value}. No other version is offered." : "You pinned this app. No other version is offered."],
                        pin is null ? [Details] : [new(UpdateRowAction.Reverse, "Remove pin", ""), Details], pin?.Id));
                    break;
                case nameof(UpdateIntelligenceStatus.Skipped):
                    // Owner decision 2026-11-02: the skipped version is named; a newer version returns the app to Ready by itself.
                    StableDecision? skip = Latest(decisions, row.StableProductKey, UserDecisionService.SkipVersionKind);
                    string skipped = skip?.Value ?? detail?.CandidateVersion ?? "this version";
                    ignored.Add(Row(row, detail, UpdateGroup.IgnoredAndSkipped,
                        [$"Skipped version {skipped}. You chose not to install it; a newer version will be offered again."],
                        skip is null ? [Details] : [new(UpdateRowAction.Reverse, $"Stop skipping {skipped}", "\uE7A7"), Details], skip?.Id));
                    break;
            }
        }
        string? selected = selectedKey is not null && ready.Any(row => row.StableProductKey == selectedKey) ? selectedKey : null;
        return new(ready, review, ignored, selected);
    }

    /// <summary>Reasons in words for a review-required row; never empty.</summary>
    public static IReadOnlyList<string> NeedsReviewReasons(UpdateDetail? detail)
    {
        if (detail is null) return ["The evidence for this app needs your review."];
        List<string> reasons = [.. ReviewReasonText.DescribeAll(detail.ReviewReasons)];
        if (detail.Confidence is nameof(MappingConfidence.Low) or nameof(MappingConfidence.None))
            reasons.Insert(0, detail.Confidence == nameof(MappingConfidence.Low) ? "The match is low confidence." : "No package matches this app with confidence.");
        if (detail.Freshness.Contains("cached", StringComparison.OrdinalIgnoreCase) && !reasons.Contains("The source evidence is missing or out of date.", StringComparer.Ordinal))
            reasons.Add("The source evidence is cached and may be out of date.");
        if (reasons.Count == 0) reasons.Add(string.IsNullOrWhiteSpace(detail.Explanation) ? "The evidence for this app needs your review." : detail.Explanation);
        return reasons;
    }

    /// <summary>Adds "Skip this version (V)" before Details when an exact new version is known; otherwise nothing is invented.</summary>
    private static IReadOnlyList<UpdateRowActionPresentation> WithSkip(IReadOnlyList<UpdateRowActionPresentation> actions, UpdateDetail? detail) =>
        detail?.CandidateVersion is { } version && !string.IsNullOrWhiteSpace(version)
            ? [.. actions.Where(action => action.Action != UpdateRowAction.Details),
                new(UpdateRowAction.SkipVersion, $"Skip this version ({version.Trim()})", "\uE893", version.Trim()), Details]
            : actions;

    private static UpdateRowPresentation Row(InventoryRow row, UpdateDetail? detail, UpdateGroup group, IReadOnlyList<string> reasons,
        IReadOnlyList<UpdateRowActionPresentation> actions, Guid? decisionId = null)
    {
        string? newVersion = detail?.CandidateVersion;
        VersionJumpBadge? badge = group == UpdateGroup.Ready ? VersionJumpPresenter.Badge(VersionJumpPresenter.Classify(row.Version, newVersion)) : null;
        return new(row.StableProductKey, row.Name, row.Publisher, row.Status, group, row.Version, newVersion, badge, reasons,
            detail?.Freshness ?? string.Empty, actions, decisionId);
    }

    private static StableDecision? Latest(IReadOnlyCollection<StableDecision> decisions, string key, params string[] kinds) => decisions
        .Where(decision => string.Equals(decision.StableProductKey, key, StringComparison.Ordinal) && kinds.Contains(decision.Kind, StringComparer.Ordinal))
        .OrderByDescending(decision => decision.CreatedAt)
        .FirstOrDefault();
}
