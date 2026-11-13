using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// What the one main button on app details does. Each value names an operation that already exists and keeps its own
/// confirmation; choosing one here decides nothing about what can be updated.
/// </summary>
public enum AppDetailAction
{
    None,
    Update,
    ConfirmMatch,
    ChooseMatch,
    CheckAgain,
    TurnOnUpdateChecks,
    UndoDecision
}

/// <summary>One labelled fact on app details, such as "Version" and "3.3.0".</summary>
public sealed record AppDetailFact(string Label, string Value);

/// <summary>
/// UI review 2026-11-13 (item 11; FR-UI-01/04/05): the top of app details. One headline, one sentence that says where the app
/// stands, at most one main action, the other decisions for a "More" menu, and two short lists of facts. Reasons recorded by the
/// matcher, package evidence and source plumbing stay under Technical details.
/// </summary>
public sealed record AppDetailSummary(
    string Headline,
    string Glyph,
    StatusTone Tone,
    string Sentence,
    IReadOnlyList<string> Notes,
    AppDetailAction Action,
    string ActionLabel,
    Guid? DecisionId,
    IReadOnlyList<UpdateRowActionPresentation> MoreActions,
    bool OffersMatchItYourself,
    string Subtitle,
    IReadOnlyList<AppDetailFact> OnThisPc,
    string SourceTitle,
    IReadOnlyList<AppDetailFact> FromSource)
{
    /// <summary>True when the page shows a main button.</summary>
    public bool HasAction => Action != AppDetailAction.None;
}

/// <summary>
/// Builds <see cref="AppDetailSummary"/> from the same evidence the Updates page uses, through <see cref="UpdatesPagePresenter"/>,
/// so an app says the same thing on both pages. Presentation only: "Update…" is offered exactly when the Updates page would list
/// the app as ready, and every action still goes through its own confirmation or the review step (FR-UPD-01, FR-POL-03/05).
/// </summary>
public static class AppDetailSummaryPresenter
{
    public const string UpdateLabel = "Update…";
    public const string SameAppLabel = "Yes, same app";
    public const string UseMatchLabel = "Use this match";
    public const string CheckAgainLabel = "Check again";
    public const string MatchItYourselfLabel = "Match it yourself";
    public const string OnThisPcTitle = "On this PC";
    public const string NotKnown = "Not known";
    public const string CantUpdateYetGlyph = "";

    /// <summary>Said under a ready update that is installed for everyone on the PC, where Windows asks before it starts.</summary>
    public const string AdministratorNote = "Windows will ask for administrator permission when this update starts.";

    public static AppDetailSummary Build(
        InventoryRow row,
        UpdateDetail? detail,
        bool sourcesEnabled,
        IReadOnlyCollection<StableDecision> decisions,
        IReadOnlyCollection<SourceSnapshot> sources,
        DateTimeOffset now,
        CultureInfo? culture = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(sources);
        Dictionary<string, UpdateDetail> details = new(StringComparer.Ordinal);
        if (detail is not null) details[row.StableProductKey] = detail;
        UpdatesPagePresentation page = UpdatesPagePresenter.Build([row], details, sourcesEnabled, decisions, null, now);
        UpdateRowPresentation? updates = page.Ready.Concat(page.NeedsReview).Concat(page.IgnoredAndSkipped).FirstOrDefault();
        StatusChipPresentation chip = StatusVocabulary.For(row.Status);
        string source = UpdatesPagePresenter.SourceName(detail?.SourceId);
        string package = detail?.PackageId ?? string.Empty;
        string from = source.Length > 0 ? source : "An update source";

        string headline = chip.Label, glyph = chip.Glyph, sentence;
        StatusTone tone = chip.Tone;
        List<string> notes = [];
        AppDetailAction action = AppDetailAction.None;
        string actionLabel = string.Empty;
        Guid? decisionId = null;
        IReadOnlyList<UpdateRowActionPresentation> more = [UpdatesPagePresenter.Ignore, UpdatesPagePresenter.Pin];
        bool matchItYourself = false;

        void CantUpdateYet()
        {
            headline = UpdatesPagePresenter.CantUpdateYetTitle; glyph = CantUpdateYetGlyph; tone = StatusTone.Neutral;
        }
        void CheckOrTurnOn(bool canCheck)
        {
            if (!sourcesEnabled) { action = AppDetailAction.TurnOnUpdateChecks; actionLabel = HomePresenter.TurnOnUpdateChecksLabel; }
            else if (canCheck) { action = AppDetailAction.CheckAgain; actionLabel = CheckAgainLabel; }
        }

        switch (updates)
        {
            case { Group: UpdateGroup.Ready }:
                sentence = $"Version {updates.NewVersion} is ready. You see a review before anything is installed.";
                action = AppDetailAction.Update; actionLabel = UpdateLabel;
                if (updates.Badge is { Jump: VersionJump.Major }) notes.Add("This is a major version change, so bigger changes are possible.");
                notes.AddRange(updates.Reasons);
                if (string.Equals(row.Scope, "Machine", StringComparison.OrdinalIgnoreCase)) notes.Add(AdministratorNote);
                more = MoreFrom(updates);
                break;
            case { Group: UpdateGroup.IgnoredAndSkipped }:
                sentence = updates.ReasonText;
                if (updates.DecisionId is { } id && updates.Actions.FirstOrDefault(item => item.Action == UpdateRowAction.Reverse) is { } reverse)
                {
                    action = AppDetailAction.UndoDecision; actionLabel = reverse.Label; decisionId = id;
                }
                more = [];
                break;
            case { Status: nameof(UpdateIntelligenceStatus.VerifiedUpdate) }:
                // A verified update the Updates page won't offer: update checks are off, or the evidence is incomplete.
                CantUpdateYet();
                sentence = sourcesEnabled
                    ? "An update was found, but PC Steward is missing something it needs before it can update this app."
                    : "An update was found at the last check. Update checks are off, so it can't be installed from here.";
                CheckOrTurnOn(canCheck: true);
                more = MoreFrom(updates);
                break;
            case { Status: nameof(UpdateIntelligenceStatus.FailedUnavailable) }:
                sentence = "PC Steward couldn't check for an update for this app.";
                CheckOrTurnOn(canCheck: true);
                more = [];
                break;
            case { AsksAboutMatch: true }:
                sentence = $"{from} has version {updates.NewVersion}, listed as {package}. If that is this app, confirm it and PC Steward will offer the update.";
                action = AppDetailAction.ConfirmMatch; actionLabel = SameAppLabel;
                more = MoreFrom(updates);
                break;
            case not null when sourcesEnabled && detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0:
                string found = detail.ReviewCandidates.Count == 1 ? "one possible match" : $"{detail.ReviewCandidates.Count.ToString(CultureInfo.CurrentCulture)} possible matches";
                sentence = $"PC Steward found {found} for this app{(updates.AsksToChoose ? ", and a newer version is on offer" : string.Empty)}. Use one only if you recognise it as this app.";
                action = AppDetailAction.ChooseMatch; actionLabel = UseMatchLabel;
                more = MoreFrom(updates);
                break;
            case { IsOnOffer: true }:
                CantUpdateYet();
                sentence = $"Version {updates.NewVersion} is on offer, but PC Steward can't update this app yet. {updates.CantUpdateReason}";
                CheckOrTurnOn(updates.CanCheckAgain);
                more = MoreFrom(updates);
                break;
            case not null:
                // A possible match with nothing newer on offer. Confirming it, where the domain allows that, lets later updates be offered.
                bool canConfirm = sourcesEnabled && detail is { CanConfirmPackageIdentity: true, SourceId: not null, PackageId: not null };
                string listed = package.Length > 0 ? $"{from} lists a possible match, {package}, with nothing newer than the version you have." : "PC Steward found a possible match with nothing newer than the version you have.";
                if (canConfirm)
                {
                    sentence = $"{listed} If that is this app, confirm it and PC Steward will offer its updates when they appear.";
                    action = AppDetailAction.ConfirmMatch; actionLabel = SameAppLabel;
                }
                else
                {
                    headline = "No update on offer"; glyph = CantUpdateYetGlyph; tone = StatusTone.Neutral;
                    sentence = listed;
                    CheckOrTurnOn(updates.CanCheckAgain);
                }
                more = MoreFrom(updates);
                break;
            default:
                switch (row.Status)
                {
                    case nameof(UpdateIntelligenceStatus.Current):
                        bool cached = detail?.Freshness.Contains("cached", StringComparison.OrdinalIgnoreCase) == true;
                        sentence = cached ? "This app was up to date the last time PC Steward reached its update source."
                            : source.Length > 0 ? $"This app is up to date. {source} has nothing newer." : "This app is up to date.";
                        break;
                    case nameof(UpdateIntelligenceStatus.Unmanaged):
                        sentence = "PC Steward found no update source for this app, so it can't check it for updates.";
                        matchItYourself = true;
                        break;
                    case StatusVocabulary.InstalledKey:
                        sentence = sourcesEnabled ? "PC Steward hasn't looked for an update for this app yet."
                            : "Update checks are off, so PC Steward hasn't looked for an update for this app.";
                        CheckOrTurnOn(canCheck: true);
                        if (action == AppDetailAction.CheckAgain) actionLabel = "Check for updates";
                        break;
                    default:
                        sentence = chip.Meaning;
                        break;
                }
                break;
        }

        string publisher = Known(row.Publisher);
        List<AppDetailFact> onThisPc =
        [
            new("Version", Known(row.Version)),
            new("Publisher", publisher),
            new("Installed for", InstalledFor(row.Scope))
        ];
        List<AppDetailFact> fromSource = [];
        if (source.Length > 0 && package.Length > 0)
        {
            fromSource.Add(new("Package", package));
            fromSource.Add(new("Latest", detail?.CandidateVersion ?? NotKnown));
            fromSource.Add(new("How sure", ReviewReasonText.Confidence(detail?.Confidence)));
            fromSource.Add(new("Checked", CheckedText(detail?.SourceId, sourcesEnabled, sources, now, culture, zone)));
        }
        string subtitle = publisher == NotKnown ? $"Version {Known(row.Version)}" : $"Version {Known(row.Version)} · {publisher}";
        return new(headline, glyph, tone, sentence, notes, action, actionLabel, decisionId, more, matchItYourself, subtitle,
            onThisPc, fromSource.Count == 0 ? string.Empty : $"From {source}", fromSource);
    }

    /// <summary>Who the app is installed for, in the words the owner would use.</summary>
    public static string InstalledFor(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        "user" => "Just you",
        "machine" => "Everyone on this PC",
        _ => NotKnown
    };

    private static IReadOnlyList<UpdateRowActionPresentation> MoreFrom(UpdateRowPresentation updates) =>
        [.. updates.Actions.Where(item => item.Action is UpdateRowAction.IgnoreApp or UpdateRowAction.PinCurrentVersion or UpdateRowAction.SkipVersion)];

    private static string Known(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase) ? NotKnown : value.Trim();

    private static string CheckedText(string? sourceId, bool sourcesEnabled, IReadOnlyCollection<SourceSnapshot> sources, DateTimeOffset now, CultureInfo? culture, TimeZoneInfo? zone)
    {
        if (!sourcesEnabled) return "Update checks are off";
        SourceSnapshot? snapshot = sources.FirstOrDefault(item => string.Equals(item.SourceId, sourceId, StringComparison.OrdinalIgnoreCase));
        if (snapshot?.LastSuccessAt is not { } at) return "Not yet";
        string when = DisplayFormat.Relative(at, now, culture, zone);
        return snapshot.State == SourceSnapshotState.Live ? when : $"{when} (couldn't be reached since)";
    }

}
