using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Why a Chocolatey package update cannot be confirmed on the admitted screen (addendum §3), in display order.</summary>
public enum ChocolateyPackageUpdateUnavailableReason
{
    /// <summary>C1 is off (only reachable in tests: the Desktop opens the read-only disclosure instead).</summary>
    NotAdmitted,
    /// <summary>C9: the trusted-sources setting is off, so the package is not checked.</summary>
    TrustedSourcesOff,
    /// <summary>The check and the review are being prepared.</summary>
    Checking,
    /// <summary>The check found no reviewable update, or could not finish.</summary>
    NoCheckedUpdate,
    /// <summary>One of the Profile V2 disclosure's own reasons (split token, review data).</summary>
    Disclosure,
    /// <summary>C2: the check is 10 minutes old or older.</summary>
    CheckTooOld,
    /// <summary>C8: the review's lifetime has passed, or it was used.</summary>
    ReviewExpired,
    /// <summary>C6: an earlier Chocolatey update needs attention.</summary>
    EarlierUpdateNeedsAttention,
}

/// <summary>
/// The review session's state as the package screen shows it (display only; every value is re-checked on the press).
/// </summary>
public sealed record ChocolateyPackageUpdateReviewState(ChocolateyClientUpdateReviewStage Stage, bool ReviewLive, bool CheckFresh,
    bool EarlierUpdateNeedsAttention, ReviewedUpdateOperation? Operation, ChocolateyReviewIntentFacts? Intent, DateTimeOffset? ExpiresAt)
{
    /// <summary>Before a session exists, or while it is being prepared.</summary>
    public static ChocolateyPackageUpdateReviewState Checking { get; } =
        new(ChocolateyClientUpdateReviewStage.Preparing, false, false, false, null, null, null);
}

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-UI-03/04, TM-03/04/07/13; Chocolatey package Confirm addendum §3–§4: the portable view model of the
/// admitted Chocolatey package review. It shows every Profile V2 disclosure line
/// (<see cref="ChocolateyReviewDisclosurePresentation.Present"/>) for the frozen operation and its checked intent, the change and
/// the expiry while the review is live, and every reason Confirm is off.
/// <list type="bullet">
/// <item>The split-token state is probed through the injected function on every <see cref="Open"/> (each check), never cached.</item>
/// <item><see cref="CanConfirm"/> holds only when C1, C9, a live review, a fresh check, no earlier update needing attention and
/// the disclosure's own conditions all hold. Pressing it goes only through the session, which re-checks everything.</item>
/// </list>
/// It holds no session, review, draft, permit or confirm path.
/// </summary>
public sealed class ChocolateyPackageUpdateViewModel
{
    public const string EntryText = "Review Chocolatey update…";
    public const string EntryAutomationDetail = "Opens the review. PC Steward checks this package again before anything can be confirmed.";
    public const string DetailCopy =
        "PC Steward can update this Chocolatey package after you review it. It checks the package again first, and Windows asks for " +
        "administrator permission once.";
    public const string CheckAgainText = "Check again";
    public const string CloseText = "Close";
    public const string ChangeLabel = "Change";
    public const string ExpiresLabel = "Review expires";
    public const string UnavailableHeading = "Why this update cannot be confirmed";
    public const string NotAdmittedText = "Not available yet: waiting for review and owner approval.";
    public const string TrustedSourcesOffText =
        "Turn on \"Look for updates online\" in Settings first. Checking this package asks Chocolatey Community about it.";
    public const string CheckingText = "Checking this package in your user cache and on this PC…";
    public const string NoCheckedUpdateText =
        "No update can be reviewed: your user cache shows no newer stable version of this package, or PC Steward could not check it. " +
        "Nothing was started.";
    public const string CheckTooOldText = "This check is more than 10 minutes old. Check again before you confirm.";
    public const string ReviewExpiredText = "This review has expired. Check again to see the current state.";
    public const string EarlierUpdateNeedsAttentionText =
        "An earlier Chocolatey update did not finish cleanly. Check again to look at this PC afresh before another update.";

    private ChocolateyPackageUpdateViewModel(bool admitted, string packageId, bool trustedSourcesEnabled, ReviewedRouteSplitTokenState splitToken,
        ChocolateyPackageUpdateReviewState review)
    {
        Admitted = admitted;
        PackageId = packageId;
        TrustedSourcesEnabled = trustedSourcesEnabled;
        SplitTokenState = splitToken;
        Review = review;
        Disclosure = review.Stage == ChocolateyClientUpdateReviewStage.Ready && review.Operation is { } operation
            ? ChocolateyReviewDisclosurePresentation.Present(operation, review.Intent, splitToken)
            : null;

        List<ChocolateyPackageUpdateUnavailableReason> reasons = [];
        List<string> texts = [];
        void Add(ChocolateyPackageUpdateUnavailableReason reason, string text) { reasons.Add(reason); texts.Add(text); }
        if (!admitted) Add(ChocolateyPackageUpdateUnavailableReason.NotAdmitted, NotAdmittedText);
        if (!trustedSourcesEnabled) Add(ChocolateyPackageUpdateUnavailableReason.TrustedSourcesOff, TrustedSourcesOffText);
        if (admitted && trustedSourcesEnabled)
        {
            switch (review.Stage)
            {
                // Nothing is checked or prepared without a limited split token (C4): the reason is shown instead.
                case ChocolateyClientUpdateReviewStage.NotPrepared or ChocolateyClientUpdateReviewStage.Preparing when SplitTokenReason(splitToken) is { } token:
                    Add(ChocolateyPackageUpdateUnavailableReason.Disclosure, ChocolateyReviewDisclosurePresentation.Text(token));
                    break;
                case ChocolateyClientUpdateReviewStage.NotPrepared or ChocolateyClientUpdateReviewStage.Preparing:
                    Add(ChocolateyPackageUpdateUnavailableReason.Checking, CheckingText);
                    break;
                case ChocolateyClientUpdateReviewStage.PrepareFailed:
                    Add(ChocolateyPackageUpdateUnavailableReason.NoCheckedUpdate, NoCheckedUpdateText);
                    break;
                case ChocolateyClientUpdateReviewStage.Ready when Disclosure is { } disclosure:
                    foreach (string text in disclosure.UnavailableTexts) Add(ChocolateyPackageUpdateUnavailableReason.Disclosure, text);
                    if (!review.CheckFresh) Add(ChocolateyPackageUpdateUnavailableReason.CheckTooOld, CheckTooOldText);
                    if (!review.ReviewLive) Add(ChocolateyPackageUpdateUnavailableReason.ReviewExpired, ReviewExpiredText);
                    break;
                default:
                    Add(ChocolateyPackageUpdateUnavailableReason.ReviewExpired, ReviewExpiredText);
                    break;
            }
            if (review.EarlierUpdateNeedsAttention)
                Add(ChocolateyPackageUpdateUnavailableReason.EarlierUpdateNeedsAttention, EarlierUpdateNeedsAttentionText);
        }
        UnavailableReasons = reasons.AsReadOnly();
        UnavailableTexts = texts.AsReadOnly();
        UnavailableFields = texts.Select(text => new ReviewedDisplayField("Unavailable", text)).ToArray();

        List<ReviewedDisplayField> fields = [];
        if (Disclosure is { } shown)
        {
            fields.Add(new("Candidate", shown.CandidateIntent));
            fields.Add(new("Candidate checked", shown.IntentTime));
            fields.Add(new("Your user cache", shown.UserCacheDisclosure));
            fields.Add(new("Installer mode", shown.InstallerMode));
            if (shown.RuntimeLimits is { } limits) fields.Add(new("Package scripts and downloads", limits));
            fields.Add(new("Running installers", shown.LingeringProcesses));
            if (shown.LibBadResidue is { } residue) fields.Add(new("Earlier failed install", residue));
            // The change and expiry lines only while the review is live and the check is fresh.
            if (review.ReviewLive && review.CheckFresh && review.Operation is { } frozen)
            {
                fields.Add(new(ChangeLabel, ChangeText(frozen)));
                if (review.ExpiresAt is { } expires)
                    fields.Add(new(ExpiresLabel, expires.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"));
            }
        }
        Fields = fields.AsReadOnly();
    }

    /// <summary>
    /// Opens the admitted review as "Checking": probes the split-token state now. A probe fault is Unknown (unavailable).
    /// </summary>
    public static ChocolateyPackageUpdateViewModel Open(string packageId, bool trustedSourcesEnabled, Func<ReviewedRouteSplitTokenState> probeSplitToken) =>
        OpenView(ChocolateyPackageUpdateAdmission.IsOwnerApproved, packageId, trustedSourcesEnabled, probeSplitToken);

    /// <summary>
    /// Tests only: the view with the admission given, so both values stay under test whichever is compiled in. Production code
    /// never calls it (architecture-pinned).
    /// </summary>
    internal static ChocolateyPackageUpdateViewModel OpenWithAdmissionForTests(bool admitted, string packageId, bool trustedSourcesEnabled,
        Func<ReviewedRouteSplitTokenState> probeSplitToken) => OpenView(admitted, packageId, trustedSourcesEnabled, probeSplitToken);

    private static ChocolateyPackageUpdateViewModel OpenView(bool admitted, string packageId, bool trustedSourcesEnabled,
        Func<ReviewedRouteSplitTokenState> probeSplitToken)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(probeSplitToken);
        ReviewedRouteSplitTokenState splitToken;
        try { splitToken = probeSplitToken(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { splitToken = ReviewedRouteSplitTokenState.Unknown; }
        if (!Enum.IsDefined(splitToken)) splitToken = ReviewedRouteSplitTokenState.Unknown;
        return new(admitted, packageId, trustedSourcesEnabled, splitToken, ChocolateyPackageUpdateReviewState.Checking);
    }

    /// <summary>The same view (same probe result) over a new review state.</summary>
    public ChocolateyPackageUpdateViewModel WithReview(ChocolateyPackageUpdateReviewState review)
    {
        ArgumentNullException.ThrowIfNull(review);
        return new(Admitted, PackageId, TrustedSourcesEnabled, SplitTokenState, review);
    }

    /// <summary>The disclosure's split-token reason for a probe result; null for a limited split token.</summary>
    private static ReviewedRouteUnavailableReason? SplitTokenReason(ReviewedRouteSplitTokenState splitToken) => splitToken switch
    {
        ReviewedRouteSplitTokenState.SplitTokenLimited => null,
        ReviewedRouteSplitTokenState.NoSplitToken => ReviewedRouteUnavailableReason.SplitTokenRequired,
        ReviewedRouteSplitTokenState.DesktopTokenUnsupported => ReviewedRouteUnavailableReason.DesktopTokenUnsupported,
        _ => ReviewedRouteUnavailableReason.DesktopTokenUnknown,
    };

    /// <summary>
    /// The Desktop checks the package and prepares a review only when C1, C9 and C4 hold. Otherwise the dialog shows why, and
    /// nothing is checked, saved or started.
    /// </summary>
    public bool ShouldPrepare => Admitted && TrustedSourcesEnabled && SplitTokenState == ReviewedRouteSplitTokenState.SplitTokenLimited;

    /// <summary>"ripgrep 14.1.0 → 15.2.0" from the frozen operation.</summary>
    public static string ChangeText(ReviewedUpdateOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return $"{operation.Selection.PackageId} {operation.Selection.InstalledVersion} → {operation.Selection.CandidateVersion}";
    }

    public bool Admitted { get; }
    public string PackageId { get; }
    public bool TrustedSourcesEnabled { get; }
    public ReviewedRouteSplitTokenState SplitTokenState { get; }
    public ChocolateyPackageUpdateReviewState Review { get; }

    /// <summary>The Profile V2 disclosure of the frozen operation; null until a review is ready.</summary>
    public ChocolateyReviewDisclosureView? Disclosure { get; }

    public string Title => Disclosure?.Title ?? $"Update {PackageId} (Chocolatey Community)";

    /// <summary>The one-line state at the top of the dialog (a polite live region in the Desktop).</summary>
    public string Summary => CanConfirm
        ? $"Ready to review: {(Review.Operation is { } frozen ? ChangeText(frozen) : PackageId)}. Read what it does below, then confirm."
        : UnavailableReasons.Contains(ChocolateyPackageUpdateUnavailableReason.Checking) ? CheckingText : NotConfirmableSummary;

    public const string NotConfirmableSummary = "This update cannot be confirmed right now. The reasons are listed below.";
    public IReadOnlyList<ReviewedDisplayField> Fields { get; }
    public IReadOnlyList<ChocolateyPackageUpdateUnavailableReason> UnavailableReasons { get; }
    public IReadOnlyList<string> UnavailableTexts { get; }
    public IReadOnlyList<ReviewedDisplayField> UnavailableFields { get; }
    public bool IsUnavailable => UnavailableReasons.Count != 0;

    /// <summary>Every condition the screen can see holds; the session re-checks them on the press.</summary>
    public bool CanConfirm => Admitted && TrustedSourcesEnabled && Review.Stage == ChocolateyClientUpdateReviewStage.Ready && Review.ReviewLive &&
        Review.CheckFresh && !Review.EarlierUpdateNeedsAttention && Disclosure is { CanConfirm: true } && UnavailableReasons.Count == 0;

    /// <summary>A new check would help: the check found nothing, or the review or check has aged, or an earlier update needs a new look.</summary>
    public bool OffersCheckAgain => Admitted && TrustedSourcesEnabled && !CanConfirm &&
        (Review.Stage == ChocolateyClientUpdateReviewStage.PrepareFailed || Review.EarlierUpdateNeedsAttention ||
            Review.Stage == ChocolateyClientUpdateReviewStage.Ready && (!Review.ReviewLive || !Review.CheckFresh));

    /// <summary>The primary button's text; empty (hidden) when it has nothing to do.</summary>
    public string PrimaryText => CanConfirm ? ConfirmText : OffersCheckAgain ? CheckAgainText : string.Empty;

    /// <summary>Bound to the primary button's enabled state.</summary>
    public bool IsPrimaryActionEnabled => CanConfirm || OffersCheckAgain;

    public string ConfirmText => "Update " + PackageId;

    public string ConfirmAutomationName => CanConfirm
        ? $"Update {PackageId} with Chocolatey. Windows asks for administrator permission once."
        : "Confirm is unavailable for this Chocolatey update";
}
