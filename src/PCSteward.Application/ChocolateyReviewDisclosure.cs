using System.Globalization;
using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, FR-HIS-01, TM-07/13; Task 50 design rev 6 settle window (rev-4 L4): processes the update started that were
/// still running when it finished may be closed. The helper reports that in its diagnostic as the fixed
/// <see cref="Token"/>; it never changes an outcome. The review discloses it beforehand and history shows the fixed note
/// beside the outcome, whatever the outcome. Display only.
/// </summary>
public static partial class ReviewedUpdateLingeringProcesses
{
    /// <summary>The helper diagnostic's fixed token (design rev 6, settle window).</summary>
    public const string Token = "lingering-descendants-terminated";

    /// <summary>Review disclosure (design rev 6, rev-4 L4).</summary>
    public const string ReviewDisclosure =
        "Processes started by the update, including installers still running, may be closed when the update finishes.";

    /// <summary>History note shown beside the recorded outcome when the token was recorded.</summary>
    public const string HistoryNote =
        "Recorded at the time: processes started by the update, such as installers, were still running when it finished and were closed. " +
        "Check the application before relying on it.";

    /// <summary>
    /// True when the helper's observation <paramref name="diagnostic"/> carries <see cref="Token"/> as a whole token (the workflow's
    /// input). History recognises only the workflow's leading marker (<c>ReviewedUpdateHistoryPresentation.LingeringRecorded</c>).
    /// </summary>
    public static bool IsReported(string? diagnostic) => diagnostic is not null && TokenPattern().IsMatch(diagnostic);

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])lingering-descendants-terminated(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();
}

/// <summary>
/// The desktop requester-token shape relevant to the runtime-limit Chocolatey route (design rev 6 §(L6)): the route is
/// available only to an administrator account using a UAC split token, with PC Steward running non-elevated.
/// </summary>
public enum ReviewedRouteSplitTokenState
{
    /// <summary>Not determined (unreadable token, non-Windows, or not probed): unavailable.</summary>
    Unknown,
    /// <summary>The desktop token is the Medium, UAC-filtered (elevation type Limited) half of a split token.</summary>
    SplitTokenLimited,
    /// <summary>No split token: UAC is off, a standard-user desktop, or PC Steward itself runs elevated.</summary>
    NoSplitToken,
    /// <summary>A split token whose shape the route refuses (integrity, restriction, AppContainer or privileges).</summary>
    DesktopTokenUnsupported,
}

/// <summary>Closed reasons the runtime-limit Chocolatey route is unavailable at review, in display order.</summary>
public enum ReviewedRouteUnavailableReason
{
    /// <summary>The route is not admitted (<c>HasSupportedMode</c>) while it waits on owner host evidence.</summary>
    NotAdmittedPendingHostEvidence,
    /// <summary>design rev 6 §(L6): UAC off, standard-user desktop, or an elevated desktop.</summary>
    SplitTokenRequired,
    DesktopTokenUnsupported,
    DesktopTokenUnknown,
    /// <summary>The checked user-cache intent does not match the frozen review.</summary>
    ReviewDataMismatch,
}

/// <summary>
/// The review-relevant facts of a desktop review intent (N5 <c>ObserveReviewIntentAsync</c>): the supplied user-cache
/// candidate, the time it was resolved (<see cref="IntentResolvedAt"/>) and when the desktop observation started
/// (<see cref="ObservedAt"/>). Display data only; never trusted candidate evidence, helper qualification or a permit.
/// </summary>
public sealed record ChocolateyReviewIntentFacts(string PackageId, string InstalledVersion, string CandidateVersion, string RequestingUserSid,
    string ProfileEvidenceId, ReviewedCandidateProvenance CandidateProvenance, bool TargetLibBadResidue, DateTimeOffset IntentResolvedAt,
    DateTimeOffset ObservedAt);

/// <summary>Presentation-safe review disclosure for the runtime-limit Chocolatey route. Every string is fixed or derived from the frozen review.</summary>
public sealed record ChocolateyReviewDisclosureView(
    string Title,
    string CandidateIntent,
    DateTimeOffset? IntentResolvedAt,
    string IntentTime,
    string UserCacheDisclosure,
    string InstallerMode,
    string? RuntimeLimits,
    string LingeringProcesses,
    string? LibBadResidue,
    IReadOnlyList<ReviewedRouteUnavailableReason> UnavailableReasons,
    IReadOnlyList<string> UnavailableTexts,
    bool CanConfirm);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Profile V2 "UI must disclose before review", design rev 6 §(L6) and rev-4 L4, N5
/// obligation (a): the review disclosure for <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>. It shows
/// the provisional user-cache candidate with the intent's own resolved time, the fixed Profile V2 disclosure, the
/// script-runtime limits, the package-script mode, the lingering-process disclosure and any lib-bad residue note.
/// <see cref="ChocolateyReviewDisclosureView.CanConfirm"/> is true only when every conjunct holds: the real review gate
/// (<see cref="ReviewedUpdateReview.HasSupportedMode"/>, which today admits only WinGet), a UAC split token and an intent
/// matching the frozen review. Presentation never enables confirmation by itself; <see cref="ReviewedUpdateReview.Confirm"/>
/// rechecks the gate.
/// </summary>
public static class ChocolateyReviewDisclosurePresentation
{
    /// <summary>Profile V2 "UI must disclose before review" (verbatim).</summary>
    public const string UserCacheDisclosure =
        "Candidate shown from your user cache; PC Steward confirms the exact version with protected machine data before updating.";

    public const string NotAdmittedText =
        "Unavailable: PC Steward does not allow this Chocolatey update route yet. It stays off until its checks have been proven on a real PC " +
        "(Chocolatey version eligibility, an exact-package test, the administrator helper's installation and its administrator-approval prompt).";

    /// <summary>design rev 6 §(L6): the split-token availability constraint.</summary>
    public const string SplitTokenRequiredText =
        "Unavailable: this route needs an administrator account with User Account Control turned on (a split token), with PC Steward " +
        "running without administrator rights. It is unavailable when User Account Control is off, on standard-user desktops, and when " +
        "PC Steward itself runs as administrator.";

    public const string DesktopTokenUnsupportedText =
        "Unavailable: PC Steward's Windows sign-in token has a shape this route does not support (for example a restricted, " +
        "low-integrity or app-container token, or extra privileges).";

    public const string DesktopTokenUnknownText =
        "Unavailable: PC Steward could not confirm that your account uses User Account Control with a split administrator token.";

    public const string ReviewDataMismatchText =
        "Unavailable: the checked user-cache candidate does not match this review. Refresh and review again.";

    public const string LibBadResidueText =
        "Chocolatey kept files from an earlier failed install of this package (lib-bad). They are ignored and never count as the installed version.";

    public const string IntentTimeUnavailableText = "The time the candidate was checked is unavailable.";

    public static ChocolateyReviewDisclosureView Present(ReviewedUpdateOperation operation, ChocolateyReviewIntentFacts? intent,
        ReviewedRouteSplitTokenState splitToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ReviewedUpdateSelection selection = operation.Selection;
        bool matches = intent is not null && Matches(operation, intent);

        List<ReviewedRouteUnavailableReason> reasons = [];
        if (!ReviewedUpdateReview.HasSupportedMode(operation)) reasons.Add(ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence);
        AddSplitTokenReason(reasons, splitToken);
        if (!matches) reasons.Add(ReviewedRouteUnavailableReason.ReviewDataMismatch);

        DateTimeOffset? resolved = matches ? intent!.IntentResolvedAt.ToUniversalTime() : null;
        return new(
            $"{selection.PackageId} {selection.InstalledVersion} → {selection.CandidateVersion} ({selection.Provider}, {selection.Scope})",
            $"Available version {selection.CandidateVersion} for {selection.PackageId} (installed {selection.InstalledVersion}) is a provisional intent from your user cache.",
            resolved,
            resolved is { } at
                ? "Candidate checked in your user cache at " + at.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC."
                : IntentTimeUnavailableText,
            UserCacheDisclosure,
            ReviewedUpdateHistoryPresentation.Mode(operation.Mode),
            ReviewedUpdateProviderDisclosure.GetRiskText(operation),
            ReviewedUpdateLingeringProcesses.ReviewDisclosure,
            matches && intent!.TargetLibBadResidue ? LibBadResidueText : null,
            reasons,
            reasons.Select(Text).ToArray(),
            reasons.Count == 0);
    }

    public const string NoReviewTitle = "Chocolatey Community machine update (reviewed route)";

    public const string NoReviewCandidateText =
        "No reviewed Chocolatey candidate is available. PC Steward does not prepare reviews for this route while it is not allowed.";

    /// <summary>
    /// Admission obligation 5 (N6 review M1): the same disclosure when no reviewed operation exists, which is always the case
    /// today because nothing composes this route. It shows the fixed pre-review disclosures, the route's script-runtime limits and
    /// package-script mode, the not-admitted reason and the freshly probed split-token reason. Without a reviewed operation there
    /// is nothing to confirm, so <see cref="ChocolateyReviewDisclosureView.CanConfirm"/> is always false.
    /// </summary>
    public static ChocolateyReviewDisclosureView PresentWithoutReview(ReviewedRouteSplitTokenState splitToken)
    {
        List<ReviewedRouteUnavailableReason> reasons = [ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence];
        AddSplitTokenReason(reasons, splitToken);
        return new(
            NoReviewTitle,
            NoReviewCandidateText,
            null,
            IntentTimeUnavailableText,
            UserCacheDisclosure,
            ReviewedUpdateHistoryPresentation.Mode(ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled)),
            ReviewedUpdateProviderDisclosure.ChocolateyCommunityScriptRuntimeLimits,
            ReviewedUpdateLingeringProcesses.ReviewDisclosure,
            null,
            reasons,
            reasons.Select(Text).ToArray(),
            false);
    }

    private static void AddSplitTokenReason(List<ReviewedRouteUnavailableReason> reasons, ReviewedRouteSplitTokenState splitToken)
    {
        switch (splitToken)
        {
            case ReviewedRouteSplitTokenState.SplitTokenLimited: break;
            case ReviewedRouteSplitTokenState.NoSplitToken: reasons.Add(ReviewedRouteUnavailableReason.SplitTokenRequired); break;
            case ReviewedRouteSplitTokenState.DesktopTokenUnsupported: reasons.Add(ReviewedRouteUnavailableReason.DesktopTokenUnsupported); break;
            default: reasons.Add(ReviewedRouteUnavailableReason.DesktopTokenUnknown); break;
        }
    }

    public static string Text(ReviewedRouteUnavailableReason reason) => reason switch
    {
        ReviewedRouteUnavailableReason.NotAdmittedPendingHostEvidence => NotAdmittedText,
        ReviewedRouteUnavailableReason.SplitTokenRequired => SplitTokenRequiredText,
        ReviewedRouteUnavailableReason.DesktopTokenUnsupported => DesktopTokenUnsupportedText,
        ReviewedRouteUnavailableReason.DesktopTokenUnknown => DesktopTokenUnknownText,
        ReviewedRouteUnavailableReason.ReviewDataMismatch => ReviewDataMismatchText,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    /// <summary>
    /// The intent must be the same provisional candidate, requester and profile identity the review froze, with consistent times:
    /// resolved, then observed, then reviewed, and reviewed at most <see cref="IntentMaxAge"/> after it was resolved.
    /// </summary>
    private static bool Matches(ReviewedUpdateOperation operation, ChocolateyReviewIntentFacts intent) =>
        operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 &&
        operation.CandidateProvenance == ReviewedCandidateProvenance.UserCacheIntent &&
        intent.CandidateProvenance == ReviewedCandidateProvenance.UserCacheIntent &&
        string.Equals(intent.PackageId, operation.Selection.PackageId, StringComparison.Ordinal) &&
        string.Equals(intent.InstalledVersion, operation.Selection.InstalledVersion, StringComparison.Ordinal) &&
        string.Equals(intent.CandidateVersion, operation.Selection.CandidateVersion, StringComparison.Ordinal) &&
        string.Equals(intent.RequestingUserSid, operation.RequestingUserSid, StringComparison.Ordinal) &&
        string.Equals(intent.ProfileEvidenceId, operation.EvidenceId, StringComparison.Ordinal) &&
        intent.IntentResolvedAt != default && intent.ObservedAt != default && intent.IntentResolvedAt <= intent.ObservedAt &&
        intent.ObservedAt <= operation.ReviewedAt &&
        operation.ReviewedAt - intent.IntentResolvedAt >= TimeSpan.Zero && operation.ReviewedAt - intent.IntentResolvedAt <= IntentMaxAge;

    /// <summary>
    /// The longest a review may follow the intent's own resolved time. It equals the producer's intent freshness bound
    /// (<c>ChocolateyProfileEvidenceSource.IntentMaxAge</c>, which Application cannot reference; a test pins equality).
    /// </summary>
    internal static readonly TimeSpan IntentMaxAge = TimeSpan.FromMinutes(2);
}
