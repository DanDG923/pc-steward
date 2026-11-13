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

/// <summary>
/// A version-jump badge: icon plus text. UI review 2026-11-13 (item 9): it is a fact, not a status, so it carries no colour; apps
/// that change their first number on every release would otherwise always look like a warning. Major is still marked.
/// </summary>
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
        VersionJump.Minor => new(jump, "Minor", "", StatusTone.Neutral, false),
        VersionJump.Major => new(jump, "Major", "", StatusTone.Neutral, true),
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
        [nameof(MappingReviewReason.UnsupportedManualMapping)] = "The match you confirmed can't be used for updates.",
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
        [nameof(MappingReviewReason.SourceConflict)] = "The update sources give conflicting information.",
        [nameof(MappingReviewReason.MissingSourceEvidence)] = MissingEvidenceReason,
        [nameof(MappingReviewReason.MultipleInstallationInstanceAmbiguity)] = "More than one installation of this app was found.",
        [nameof(MappingReviewReason.SourceDisagreement)] = "Update sources disagree about this app.",
        [nameof(MappingReviewReason.MissingScopeEvidence)] = "The install scope isn't known.",
        [nameof(MappingReviewReason.MissingArchitectureEvidence)] = "The app's architecture isn't known.",
        [nameof(MappingReviewReason.MissingChannelEvidence)] = "The release channel isn't known.",
        [nameof(MappingReviewReason.ArchitectureCheckedAtReview)] = "Windows lists this app as 32-bit, as its installer registers it. PC Steward checks it is the 64-bit build when you review the update."
    };

    /// <summary>The words for a reason this build has no sentence for.</summary>
    public const string NeedsCloserLook = "PC Steward needs a closer look at this app.";

    /// <summary>The words for evidence that is missing or too old to act on.</summary>
    public const string MissingEvidenceReason = "PC Steward needs to check this app again.";

    /// <summary>Words for one recorded review reason. <c>None</c> has no words; an unrecognised reason gets a generic sentence.</summary>
    public static string? Describe(string reason) =>
        string.Equals(reason, nameof(MappingReviewReason.None), StringComparison.Ordinal) ? null
            : Reasons.TryGetValue(reason, out string? text) ? text : NeedsCloserLook;

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

/// <summary>
/// One row on the Updates page. UI polish phase 3 (approved target T-Updates-Ready): <see cref="SameApp"/> holds other inventory
/// records of the same app (for example 7-Zip found by both Windows and WinGet) that the presenter shows as this one row. The row's
/// own <see cref="StableProductKey"/> is the only one that can be selected or reviewed, so a duplicate is never reviewed or updated
/// twice; a decision taken on the row (ignore, pin, skip, reverse) is recorded for every record it stands for.
/// </summary>
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
    Guid? DecisionId = null,
    string Provider = "",
    string MatchText = "",
    string SourceName = "",
    string Scope = "",
    IReadOnlyList<UpdateRowPresentation>? SameApp = null,
    string PackageId = "",
    bool AsksAboutMatch = false,
    string CantUpdateReason = "",
    bool CanCheckAgain = false,
    bool OffersNewer = true,
    int Choices = 0,
    bool AsksToChoose = false)
{
    /// <summary>
    /// UI review 2026-11-13 (item 10): true when an update is on offer for this row, so it belongs on the Updates page. A row
    /// with nothing on offer stays on Installed apps. A verified or unreachable row is always shown, so nothing is hidden that
    /// PC Steward tried and failed to check.
    /// </summary>
    public bool IsOnOffer => Group != UpdateGroup.NeedsReview || Status != nameof(UpdateIntelligenceStatus.ReviewRequired) || ((NewVersion is not null || Choices > 0) && OffersNewer);

    /// <summary>
    /// True when the row puts a question to the owner: one possible match to confirm (<see cref="AsksAboutMatch"/>) or several tied
    /// matches to choose from (<see cref="AsksToChoose"/>, owner decision 2026-11-13: ask only when two packages tie).
    /// </summary>
    public bool AsksOwner => AsksAboutMatch || AsksToChoose;

    /// <summary>
    /// The one question the owner can answer for a "Check the match" row, for example "WinGet has 3.4.0, listed as
    /// Elgato.WaveLink. Is that this app?" Empty for every other row.
    /// </summary>
    public string Question => AsksToChoose ? $"PC Steward found {Choices.ToString(CultureInfo.CurrentCulture)} possible matches, and a newer version is on offer. Which one is this app?"
        : !AsksAboutMatch ? string.Empty
        : SourceName.Length > 0 ? $"{SourceName} has {NewVersion}, listed as {PackageId}. Is that this app?"
        : $"Version {NewVersion} is on offer, listed as {PackageId}. Is that this app?";

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

    /// <summary>The other records of this app that this row stands for (empty for most apps).</summary>
    public IReadOnlyList<UpdateRowPresentation> AlsoFound => SameApp ?? [];

    /// <summary>
    /// "Found twice (Windows and WinGet)" when this row stands for more than one inventory record, otherwise null. It explains why
    /// one app can appear twice on Installed apps but once here.
    /// </summary>
    public string? FoundText
    {
        get
        {
            if (AlsoFound.Count == 0) return null;
            int count = AlsoFound.Count + 1;
            string times = count == 2 ? "Found twice" : $"Found {count.ToString(CultureInfo.CurrentCulture)} times";
            string[] places = [.. new[] { Provider }.Concat(AlsoFound.Select(row => row.Provider)).Where(place => place.Length > 0).Distinct(StringComparer.Ordinal)];
            return places.Length < 2 ? times : $"{times} ({string.Join(", ", places[..^1])} and {places[^1]})";
        }
    }

    /// <summary>
    /// The row's one-line description. Ready: the publisher and a note that a major version can bring bigger changes (UI review
    /// 2026-11-13, item 9: "High confidence match" is what ready means, so it isn't repeated on every row). Check the match: the
    /// question. Can't update yet: the installed and offered versions, then the reason in words. Ignored and skipped: the decision.
    /// </summary>
    public string Summary
    {
        get
        {
            List<string> parts = [];
            switch (Group)
            {
                case UpdateGroup.Ready:
                    if (!string.Equals(Publisher, "Unknown", StringComparison.OrdinalIgnoreCase)) parts.Add(Publisher);
                    if (Badge is { Jump: VersionJump.Major }) parts.Add("bigger changes are possible");
                    parts.AddRange(Reasons);
                    break;
                case UpdateGroup.NeedsReview when AsksOwner:
                    parts.Add(Question);
                    break;
                case UpdateGroup.NeedsReview:
                    parts.Add($"Installed {InstalledVersion}");
                    if (NewVersion is not null) parts.Add(SourceName.Length > 0 ? $"{SourceName} offers {NewVersion}" : $"{NewVersion} offered");
                    parts.Add(ReasonText);
                    break;
                default:
                    parts.Add(ReasonText);
                    break;
            }
            if (FoundText is { } found) parts.Add(found);
            return string.Join(" · ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    /// <summary>Every record this row stands for, its own first: the keys a decision on the row is recorded for.</summary>
    public IReadOnlyList<string> AllKeys => [StableProductKey, .. AlsoFound.Select(row => row.StableProductKey)];
}

/// <summary>
/// The grouped Updates page. Nothing is preselected: <see cref="SelectedKeys"/> is empty unless the user chose Ready rows, and it can
/// only ever hold Ready keys (Task 73 Phase 6, SPEC §9.13 G4: multi-select replaces the single choice).
/// </summary>
public sealed record UpdatesPagePresentation(
    IReadOnlyList<UpdateRowPresentation> Ready,
    IReadOnlyList<UpdateRowPresentation> NeedsReview,
    IReadOnlyList<UpdateRowPresentation> IgnoredAndSkipped,
    IReadOnlyList<string> SelectedKeys)
{
    /// <summary>The chosen Ready rows, in Ready order.</summary>
    public IReadOnlyList<UpdateRowPresentation> Selected => [.. Ready.Where(row => SelectedKeys.Contains(row.StableProductKey, StringComparer.Ordinal))];

    /// <summary>Every Ready key, in order: what "Select all ready" and "Update all N…" choose — exactly the Ready group.</summary>
    public IReadOnlyList<string> ReadyKeys => [.. Ready.Select(row => row.StableProductKey)];

    public bool IsSelected(string stableProductKey) => SelectedKeys.Contains(stableProductKey, StringComparer.Ordinal);

    /// <summary>The review button is enabled only once the user has chosen at least one Ready row.</summary>
    public bool CanReview => SelectedKeys.Count > 0;

    /// <summary>"Update all N…" is offered whenever something is ready. It opens the review; it never starts an update.</summary>
    public bool CanReviewAll => Ready.Count > 0;

    public bool AllReadySelected => Ready.Count > 0 && SelectedKeys.Count == Ready.Count;

    /// <summary>
    /// A specific label: it names the chosen app, or counts the chosen apps. The trailing ellipsis says the button opens a page
    /// first: it opens the one review, and nothing is updated until the owner confirms there (Update all, owner request 2026-11-13).
    /// </summary>
    public string ReviewButtonLabel => Selected switch
    {
        [] => "Update selected…",
        [var row] => $"Update {row.Name}…",
        var rows => $"Update {rows.Count.ToString(CultureInfo.CurrentCulture)} selected…"
    };

    /// <summary>
    /// "Update all N…" (or "Update X…" when one is ready): reviews the whole Ready group — the verified, high-confidence matches only.
    /// It opens the one review; it never starts an update.
    /// </summary>
    public string ReviewAllLabel => Ready.Count == 1 ? $"Update {Ready[0].Name}…" : $"Update all {Ready.Count.ToString(CultureInfo.CurrentCulture)}…";

    /// <summary>The selection bar's sentence (approved target T-Updates-Ready), for example "2 of 4 ready updates selected".</summary>
    public string SelectionText => SelectedKeys.Count == 0
        ? Ready.Count == 1 ? "1 update is ready. You'll see a review before anything changes." : $"{Ready.Count.ToString(CultureInfo.CurrentCulture)} updates are ready. Update all of them or choose some. You'll see one review before anything changes."
        : $"{SelectedKeys.Count.ToString(CultureInfo.CurrentCulture)} of {DisplayFormat.Plural(Ready.Count, "ready update")} selected";

    /// <summary>
    /// UI review 2026-11-13 (item 10): the rows that ask the owner one question they can answer. Every row here has an update on
    /// offer and a match the owner can confirm; confirming records a reversible decision and never starts an update.
    /// </summary>
    public IReadOnlyList<UpdateRowPresentation> CheckTheMatch => [.. NeedsReview.Where(row => row.IsOnOffer && row.AsksOwner)];

    /// <summary>Rows with an update on offer, or a failed check, that nothing on this page can settle. Each has a reason in words.</summary>
    public IReadOnlyList<UpdateRowPresentation> CantUpdateYet => [.. NeedsReview.Where(row => row.IsOnOffer && !row.AsksOwner)];

    /// <summary><see cref="CantUpdateYet"/> grouped by its reason, in first-seen order: one line per reason, naming its apps.</summary>
    public IReadOnlyList<CantUpdateGroup> CantUpdateGroups =>
        [.. CantUpdateYet.GroupBy(row => row.CantUpdateReason, StringComparer.Ordinal).Select(group => new CantUpdateGroup(group.Key, [.. group], group.Any(row => row.CanCheckAgain)))];

    /// <summary>How many apps have a possible match but nothing on offer. They are listed on Installed apps, not here.</summary>
    public int NothingOnOfferCount => NeedsReview.Count(row => !row.IsOnOffer);

    /// <summary>The link that leads to those apps on Installed apps, or null when there are none.</summary>
    public string? NothingOnOfferText => NothingOnOfferCount switch
    {
        0 => null,
        1 => "1 more app has a possible match but no update on offer",
        var count => $"{count.ToString(CultureInfo.CurrentCulture)} more apps have a possible match but no update on offer"
    };

    /// <summary>True when the page has a row to show. A possible match with nothing on offer is not a row here.</summary>
    public bool HasAnyRow => Ready.Count + CheckTheMatch.Count + CantUpdateYet.Count + IgnoredAndSkipped.Count > 0;
}

/// <summary>One line of "Can't update yet": the reason in words, the apps it applies to, and whether checking again could help.</summary>
public sealed record CantUpdateGroup(string Reason, IReadOnlyList<UpdateRowPresentation> Rows, bool CanCheckAgain)
{
    /// <summary>The apps' names, in page order, for example "Windows Terminal, Copilot".</summary>
    public string Names => string.Join(", ", Rows.Select(row => row.Name));
}

/// <summary>
/// The Updates page header (UI polish phase 3, approved targets T-Updates and T-Updates-Ready). <see cref="Description"/> is the line
/// under the title; <see cref="Notice"/> is the info bar shown under it when update checks are off or the evidence isn't live.
/// </summary>
public sealed record UpdatesPageHeader(string Description, UpdatesPageNotice? Notice, string EmptyTitle, string EmptyMessage, string Footer, bool ShowsCheckForUpdates);

/// <summary>An Updates page info bar. <see cref="OffersTurnOnUpdateChecks"/> adds the "Turn on update checks…" button.</summary>
public sealed record UpdatesPageNotice(string Title, string Message, StatusTone Tone, bool OffersTurnOnUpdateChecks);

/// <summary>
/// FR-UPD-01, FR-POL-01–05, FR-UI-04 (Task 73 Phase 2, ADR-030 decision 2): groups update evidence into Ready, Needs review and
/// Ignored and skipped. It never decides actionability itself: Ready is exactly the domain's <c>VerifiedUpdate</c> status, and it
/// fails closed — a verified row without complete evidence, or while sources are off, goes to Needs review with a reason.
/// UI review 2026-11-13 (items 9 and 10): the page lists a needs-review row only when an update is on offer, as a question the
/// owner can answer ("Check the match") or with one reason in words ("Can't update yet"). That choice is presentation only.
/// </summary>
public static class UpdatesPagePresenter
{
    internal static readonly UpdateRowActionPresentation Ignore = new(UpdateRowAction.IgnoreApp, "Ignore updates for this app", "");
    internal static readonly UpdateRowActionPresentation Pin = new(UpdateRowAction.PinCurrentVersion, "Pin current version", "");
    private static readonly UpdateRowActionPresentation Details = new(UpdateRowAction.Details, "Details", "");

    public static UpdatesPagePresentation Build(
        IReadOnlyList<InventoryRow> rows,
        IReadOnlyDictionary<string, UpdateDetail> details,
        bool sourcesEnabled,
        IReadOnlyCollection<StableDecision> decisions,
        IReadOnlyCollection<string>? selectedKeys,
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
                        ready.Add(Row(row, detail, UpdateGroup.Ready, ReadyReasons(detail), WithSkip([Ignore, Pin, Details], detail)));
                    else
                        review.Add(Row(row, detail, UpdateGroup.NeedsReview,
                            [sourcesEnabled ? "The update evidence is incomplete, so it can't be reviewed." : "Update checks are off, so this update can't be reviewed."],
                            WithSkip([Ignore, Pin, Details], detail)) with
                        { CantUpdateReason = sourcesEnabled ? CantUpdateReasons.Incomplete : CantUpdateReasons.ChecksOff, CanCheckAgain = sourcesEnabled });
                    break;
                case nameof(UpdateIntelligenceStatus.ReviewRequired):
                    // Item 10: the owner is asked only what they can answer. The match can be confirmed (a domain rule, computed
                    // elsewhere) and an update is on offer; anything else is a reason, not a question.
                    bool newer = detail is not null && !detail.ReviewReasons.Any(reason => reason is nameof(MappingReviewReason.CandidateIsCurrent) or nameof(MappingReviewReason.CandidateIsOlder))
                        && !string.Equals(detail.CandidateVersion?.Trim(), row.Version.Trim(), StringComparison.OrdinalIgnoreCase);
                    bool asks = sourcesEnabled && newer && detail is { CanConfirmPackageIdentity: true, CandidateVersion: not null, PackageId: not null, SourceId: not null };
                    // Tied matches (owner decision 2026-11-13: ask only when two packages tie). The matcher records no single
                    // candidate, so the row has no new version of its own, but each choice carries its version. A choice that isn't
                    // known to be the installed version or older is on offer, so the app is never hidden as "nothing on offer".
                    int choices = detail is { SourceId: null, PackageId: null, CandidateVersion: null } ? detail.ReviewCandidates.Count : 0;
                    bool newerChoice = choices > 0 && detail!.ReviewCandidates.Any(choice => MayBeNewer(row.Version, choice.CandidateVersion));
                    bool chooses = sourcesEnabled && newerChoice;
                    review.Add(Row(row, detail, UpdateGroup.NeedsReview, NeedsReviewReasons(detail), WithSkip([Ignore, Pin, Details], detail)) with
                    {
                        AsksAboutMatch = asks,
                        AsksToChoose = chooses,
                        Choices = newerChoice ? choices : 0,
                        OffersNewer = choices > 0 ? newerChoice : newer,
                        CantUpdateReason = asks || chooses ? string.Empty : newerChoice ? CantUpdateReasons.MoreThanOne : CantUpdateReasons.For(detail),
                        CanCheckAgain = !asks && !chooses && sourcesEnabled && detail is { RequiresFreshSourceEvidence: true }
                    });
                    break;
                case nameof(UpdateIntelligenceStatus.FailedUnavailable):
                    review.Add(Row(row, detail, UpdateGroup.NeedsReview,
                        ["PC Steward couldn't reach the update source for this app. Check again to retry."], [Details]) with
                    { CantUpdateReason = CantUpdateReasons.CouldNotCheck, CanCheckAgain = sourcesEnabled });
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
        // UI polish phase 3 (approved target): records of the same app are shown as one row. Only the row's own key can be chosen.
        ready = SameAppRows(ready, details);
        review = SameAppRows(review, details);
        ignored = SameAppRows(ignored, details);
        // Only Ready keys survive: needs-review, ignored, skipped and pinned rows can never be selected, whatever was passed in.
        string[] selected = selectedKeys is null ? [] : [.. ready.Select(row => row.StableProductKey).Where(key => selectedKeys.Contains(key, StringComparer.Ordinal))];
        return new(ready, review, ignored, selected);
    }

    /// <summary>
    /// True unless <paramref name="candidate"/> is known to be the installed version or older. A version that can't be compared
    /// counts as possibly newer, so the page never says nothing is on offer when it can't tell.
    /// </summary>
    private static bool MayBeNewer(string installed, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase)
        && VersionComparison.Compare(new VersionComparisonRequest(installed, candidate)).Outcome is not (VersionComparisonOutcome.Equal or VersionComparisonOutcome.Older);

    /// <summary>The line under the "Check the match" heading.</summary>
    public const string CheckTheMatchDescription = "Answer once and PC Steward will offer the update.";

    /// <summary>The heading of the collapsed group of apps that have an update on offer PC Steward can't apply.</summary>
    public const string CantUpdateYetTitle = "Can't update yet";

    /// <summary>The line under that heading.</summary>
    public const string CantUpdateYetDescription = "Each line says why. Open an app to see more.";

    /// <summary>The Updates page's footer line when there is something to review (approved target T-Updates-Ready).</summary>
    public const string ReviewFooter = "You see a review of each update before it is installed.";

    /// <summary>The Updates page's footer line when nothing is shown yet (approved target T-Updates).</summary>
    public const string EmptyFooter = "PC Steward offers an update only when it is sure the update is for the app you have.";

    /// <summary>
    /// UI polish phase 3 (approved targets T-Updates and T-Updates-Ready; FR-UI-05): the header line, the info bar under it and the
    /// empty state. With update checks off the page says so calmly and offers "Turn on update checks…" (the existing source consent).
    /// With checks on, the line counts the rows and states freshness in words; anything less fresh than live also gets an info bar.
    /// Presentation only: it decides nothing about what can be updated.
    /// </summary>
    public static UpdatesPageHeader Header(UpdatesPagePresentation page, bool sourcesEnabled, IReadOnlyCollection<SourceSnapshot> sources, DateTimeOffset now,
        CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(sources);
        string footer = page.HasAnyRow ? ReviewFooter : EmptyFooter;
        if (!sourcesEnabled)
        {
            return new("Updates for your apps appear here.",
                new("Update checks are off.", "PC Steward hasn't looked for updates. Turn on update checks to see what is out of date.", StatusTone.Information, true),
                "No updates to show yet", "Updates appear here once PC Steward has looked for them.",
                footer, false);
        }
        FreshnessPresentation freshness = FreshnessPresenter.Describe(true, sources, now, culture, zone);
        LeastFreshSuccess least = FreshnessPresenter.LeastFresh(FreshnessPresenter.Checked(sources));
        string age = least.OldestSuccessAt is { } at ? DisplayFormat.Relative(at, now, culture, zone) : string.Empty;
        string evidence = freshness.Freshness switch
        {
            EvidenceFreshness.LocalOnly => "sources not checked yet",
            EvidenceFreshness.Live => $"checked {age}",
            _ when least.AnyNeverSucceeded || age.Length == 0 => $"{freshness.Word.ToLowerInvariant()} results",
            _ => $"{freshness.Word.ToLowerInvariant()} results, last checked {age}"
        };
        List<string> parts = [$"{page.Ready.Count.ToString(CultureInfo.CurrentCulture)} ready"];
        if (page.CheckTheMatch.Count > 0) parts.Add(DisplayFormat.Plural(page.CheckTheMatch.Count, "match", "matches") + " to check");
        if (page.CantUpdateYet.Count > 0) parts.Add($"{page.CantUpdateYet.Count.ToString(CultureInfo.CurrentCulture)} can't update yet");
        parts.Add(evidence);
        UpdatesPageNotice? notice = freshness.Freshness switch
        {
            EvidenceFreshness.Live => null,
            EvidenceFreshness.LocalOnly => new("Not checked yet", "PC Steward hasn't looked for updates yet. Check for updates to look now.", StatusTone.Information, false),
            _ => new($"{freshness.Word} results", freshness.Sentence, freshness.Tone, false)
        };
        bool checkedAtAll = freshness.Freshness != EvidenceFreshness.LocalOnly;
        return new(string.Join(" · ", parts), notice,
            checkedAtAll ? "No updates ready" : "No updates to show yet",
            checkedAtAll
                ? "PC Steward looked and found nothing newer for your apps."
                : "Check for updates to look for them.",
            footer, true);
    }

    /// <summary>
    /// UI polish phase 3 (approved targets; owner approved 2026-11-11): shows records of the same app — such as 7-Zip found by both
    /// Windows and WinGet — as one row. Records are only treated as the same app when everything shown and acted on matches: name,
    /// publisher, status, installed and new version, the reason in words, and (where known) scope, package source, package ID and
    /// architecture. The first record keeps its place and its key; the others go in <see cref="UpdateRowPresentation.SameApp"/>.
    /// </summary>
    private static List<UpdateRowPresentation> SameAppRows(List<UpdateRowPresentation> rows, IReadOnlyDictionary<string, UpdateDetail> details)
    {
        List<UpdateRowPresentation> shown = [];
        List<List<UpdateRowPresentation>> same = [];
        foreach (UpdateRowPresentation row in rows)
        {
            // The record must match every record already in the row, not only the first: an unknown scope or architecture must never
            // bridge two installations whose known values differ (independent review).
            int index = Enumerable.Range(0, shown.Count).FirstOrDefault(
                candidate => IsSameApp(shown[candidate], row, details) && same[candidate].All(member => IsSameApp(member, row, details)), -1);
            if (index < 0) { shown.Add(row); same.Add([]); }
            else same[index].Add(row);
        }
        return [.. shown.Select((row, index) => same[index].Count == 0 ? row : row with { SameApp = same[index] })];
    }

    private static bool IsSameApp(UpdateRowPresentation left, UpdateRowPresentation right, IReadOnlyDictionary<string, UpdateDetail> details)
    {
        static bool Same(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        static bool SameWhenKnown(string? a, string? b) =>
            string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || string.Equals(a, "Unknown", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b, "Unknown", StringComparison.OrdinalIgnoreCase) || Same(a, b);
        if (left.Group != right.Group || !Same(left.Name, right.Name) || !Same(left.Publisher, right.Publisher) || !string.Equals(left.Status, right.Status, StringComparison.Ordinal) ||
            !Same(left.InstalledVersion, right.InstalledVersion) || !Same(left.NewVersion, right.NewVersion) || !string.Equals(left.ReasonText, right.ReasonText, StringComparison.Ordinal))
            return false;
        UpdateDetail? a = details.GetValueOrDefault(left.StableProductKey);
        UpdateDetail? b = details.GetValueOrDefault(right.StableProductKey);
        if (left.Group == UpdateGroup.Ready && (a is null || b is null || !Same(a.SourceId, b.SourceId) || !Same(a.PackageId, b.PackageId))) return false;
        if (left.Group == UpdateGroup.IgnoredAndSkipped && (left.DecisionId is null) != (right.DecisionId is null)) return false;
        return (a is null || b is null || (SameWhenKnown(a.SourceId, b.SourceId) && SameWhenKnown(a.PackageId, b.PackageId) && SameWhenKnown(a.Architecture, b.Architecture)))
            && SameWhenKnown(left.Scope, right.Scope);
    }

    /// <summary>Reasons in words for a review-required row; never empty.</summary>
    /// <summary>A ready update has no reasons, except the one note that its architecture is checked when the owner reviews it.</summary>
    public static IReadOnlyList<string> ReadyReasons(UpdateDetail? detail) =>
        detail?.ReviewReasons.Contains(nameof(MappingReviewReason.ArchitectureCheckedAtReview), StringComparer.Ordinal) == true
            ? [ArchitectureCheckedAtReviewNote]
            : [];

    public const string ArchitectureCheckedAtReviewNote = "architecture is checked when you review";

    public static IReadOnlyList<string> NeedsReviewReasons(UpdateDetail? detail)
    {
        if (detail is null) return [ReviewReasonText.NeedsCloserLook];
        List<string> reasons = [.. ReviewReasonText.DescribeAll(detail.ReviewReasons)];
        if (detail.Confidence is nameof(MappingConfidence.Low) or nameof(MappingConfidence.None))
            reasons.Insert(0, detail.Confidence == nameof(MappingConfidence.Low) ? "The match is low confidence." : "No package matches this app with confidence.");
        if (detail.Freshness.Contains("cached", StringComparison.OrdinalIgnoreCase) && !reasons.Contains(ReviewReasonText.MissingEvidenceReason, StringComparer.Ordinal))
            reasons.Add("The last check is cached and may be out of date.");
        if (reasons.Count == 0) reasons.Add(string.IsNullOrWhiteSpace(detail.Explanation) ? ReviewReasonText.NeedsCloserLook : detail.Explanation);
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
        // How sure the match is, in words, from the domain's computed confidence (never decided here).
        string match = group == UpdateGroup.Ready && detail is not null ? $"{ReviewReasonText.Confidence(detail.Confidence)} match" : string.Empty;
        return new(row.StableProductKey, row.Name, row.Publisher, row.Status, group, row.Version, newVersion, badge, reasons,
            detail?.Freshness ?? string.Empty, actions, decisionId, PlaceFound(row.Provider), match, SourceName(detail?.SourceId), row.Scope, PackageId: detail?.PackageId ?? string.Empty);
    }

    /// <summary>Where Windows or a package manager reported an inventory record, in words ("Windows", "WinGet").</summary>
    private static string PlaceFound(string provider) => provider.Trim().ToLowerInvariant() switch
    {
        "winget" => "WinGet",
        "msstore" => "Microsoft Store",
        "chocolatey" => "Chocolatey",
        "registry" or "msix" or "appx" => "Windows",
        _ => string.Empty
    };

    /// <summary>A package source's name in words, or empty when it isn't known.</summary>
    internal static string SourceName(string? sourceId) => sourceId?.Trim().ToLowerInvariant() switch
    {
        "winget" => "WinGet",
        "msstore" => "Microsoft Store",
        null or "" => string.Empty,
        _ when sourceId.Contains("chocolatey", StringComparison.OrdinalIgnoreCase) => "Chocolatey",
        _ => sourceId.Trim()
    };

    private static StableDecision? Latest(IReadOnlyCollection<StableDecision> decisions, string key, params string[] kinds) => decisions
        .Where(decision => string.Equals(decision.StableProductKey, key, StringComparison.Ordinal) && kinds.Contains(decision.Kind, StringComparer.Ordinal))
        .OrderByDescending(decision => decision.CreatedAt)
        .FirstOrDefault();
}

/// <summary>
/// UI review 2026-11-13 (item 10): the one-line reasons under "Can't update yet". The recorded review reasons describe gaps inside
/// PC Steward ("the install scope isn't known"); these say the same thing the way an owner would ask it, and group apps that share a
/// cause. The full recorded reasons stay on the app's details. Presentation only: nothing here changes what can be updated.
/// </summary>
public static class CantUpdateReasons
{
    public const string CouldNotCheck = "PC Steward couldn't check for an update.";
    public const string ChecksOff = "Update checks are off.";
    public const string Incomplete = "PC Steward doesn't have everything it needs to update these yet.";
    public const string NeedsFreshCheck = "The last check is out of date.";
    public const string NotSureItMatches = "PC Steward isn't sure the update on offer is for the app you have.";
    public const string DifferentBuild = "The update on offer is for a different build than the one installed.";
    public const string VersionsNotComparable = "The installed and offered versions can't be compared.";
    public const string MoreThanOne = "More than one match or installation was found.";
    public const string HowInstalledUnknown = "PC Steward can't tell how this app was installed.";

    private static readonly (string Reason, string[] Recorded)[] Order =
    [
        (DifferentBuild, [nameof(MappingReviewReason.ArchitectureConflict), nameof(MappingReviewReason.ScopeConflict), nameof(MappingReviewReason.ChannelConflict)]),
        (VersionsNotComparable, [nameof(MappingReviewReason.IncomparableVersion), nameof(MappingReviewReason.ConnectorSpecificVersionSemantics)]),
        (MoreThanOne, [nameof(MappingReviewReason.AmbiguousCandidate), nameof(MappingReviewReason.MultipleInstallationInstanceAmbiguity), nameof(MappingReviewReason.SourceConflict), nameof(MappingReviewReason.SourceDisagreement)]),
        (HowInstalledUnknown, [nameof(MappingReviewReason.MissingScopeEvidence), nameof(MappingReviewReason.MissingArchitectureEvidence), nameof(MappingReviewReason.MissingChannelEvidence)])
    ];

    /// <summary>The one reason shown for a review-required row that can't be settled by confirming a match.</summary>
    public static string For(UpdateDetail? detail)
    {
        if (detail is null) return NotSureItMatches;
        if (detail.RequiresFreshSourceEvidence || detail.ReviewReasons.Contains(nameof(MappingReviewReason.MissingSourceEvidence), StringComparer.Ordinal)) return NeedsFreshCheck;
        foreach ((string reason, string[] recorded) in Order)
            if (detail.ReviewReasons.Any(item => recorded.Contains(item, StringComparer.Ordinal))) return reason;
        return NotSureItMatches;
    }
}
