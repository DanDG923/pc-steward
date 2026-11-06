using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Read-only display facts of PC Steward's compiled list of reviewed Chocolatey client releases (ADR-031 Option 2): the
/// normalized versions, strictly ascending, and the expected signer. Supplied once by the composition from the compiled list;
/// never read from remote metadata, the database or a scan. Display only: it decides no trust and admits nothing.
/// </summary>
public sealed record ChocolateyClientReleaseCatalogFacts(IReadOnlyList<string> ListedVersions, string Signer)
{
    /// <summary>
    /// Independent review L5: the facts shown when the compiled list cannot be read. An empty list fails validation, so the view
    /// shows "Reviewed releases: Unavailable" and offers nothing.
    /// </summary>
    public static ChocolateyClientReleaseCatalogFacts Unavailable { get; } = new(Array.Empty<string>(), string.Empty);

    /// <summary>
    /// Independent review L5: the composition's single read of the compiled list. Any fault while building it (including a type
    /// initializer failure) or a null result falls back to <see cref="Unavailable"/>, so the catalog can never fail startup.
    /// </summary>
    public static ChocolateyClientReleaseCatalogFacts ReadOrUnavailable(Func<ChocolateyClientReleaseCatalogFacts> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        try { return read() ?? Unavailable; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return Unavailable; }
    }
}

/// <summary>Closed reasons the Chocolatey client update cannot be confirmed, in display order.</summary>
public enum ChocolateyClientUpdateUnavailableReason
{
    /// <summary>
    /// No reviewed Desktop composition of <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/> exists and owner admission is
    /// outstanding (ADR-031 owner decisions 3–4; design §8.6 gate 3). Always present, whatever <c>HasSupportedMode</c> admits.
    /// </summary>
    NotAdmittedPendingReview,
    /// <summary>The installed release is listed, but no strictly newer listed release exists (ADR-031 design §3.3).</summary>
    NoNewerReviewedRelease,
    /// <summary>The verified installed release is not on the reviewed list shown, so nothing is offered (ADR-031 owner decision 2).</summary>
    InstalledReleaseNotReviewed,
    /// <summary>The installed release is verified, but the compiled reviewed list is unavailable, so nothing is offered (independent review L5).</summary>
    ReviewedReleasesUnavailable,
    /// <summary>ADR-031 design §8.6 gate 3: the installed client has not been checked yet (summaries), so nothing is offered.</summary>
    InstalledReleaseNotChecked,
    /// <summary>ADR-031 design §8.6 gate 3: the review's check of the installed client is running, so nothing is offered yet.</summary>
    InstalledReleaseChecking,
    /// <summary>ADR-031 design §8.6 gate 3: the check refused, could not run, timed out or was stopped, so nothing is offered.</summary>
    InstalledReleaseNotVerified,
    /// <summary>design rev 6 §(L6): UAC off, standard-user desktop, or an elevated desktop.</summary>
    SplitTokenRequired,
    DesktopTokenUnsupported,
    DesktopTokenUnknown,
}

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-UI-03/04, TM-03/04/07/13; ADR-031 (Option 2, owner decisions 1–5), ADR-030; design mapping
/// §3.3, §3.7 and §8 (gate 3 of §8.6): the portable view model behind the Desktop's Chocolatey client update surface. It shows the
/// installed Chocolatey release as PC Steward's read-only check verified it (<see cref="ChocolateyInstalledClientVerification"/>), or
/// why it is not verified; the release the last scan reported, as secondary display information only; the offer (the newest
/// strictly newer release on the compiled reviewed list, computed only from the verified installed release); the review-sheet facts
/// and disclosures of <see cref="ChocolateyClientUpdatePresentation"/>; and every reason the update is unavailable.
/// <list type="bullet">
/// <item>The not-admitted reason is always present, independent of <c>ReviewedUpdateReview.HasSupportedMode</c> (which since the
/// Task 50 activation admits the exact <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/> shape): the Desktop composes
/// no client review and owner admission approval is still outstanding, so <see cref="CanConfirm"/> is always false (design §8.6
/// gate 3, re-verified on the activation). This type holds no review, draft, permit or confirmation path; it starts nothing.</item>
/// <item>The split-token state is probed through the injected function on every <see cref="Open"/>, never cached.</item>
/// <item>ADR-031 ("offered only from a listed, currently trusted client"): the offer is computed only from a
/// <see cref="ChocolateyInstalledClientCheck.Verified"/> result of the native Desktop-role observation and the compiled list's
/// offer rule. The scan-reported version never decides anything; while the client is not checked, or could not be verified,
/// nothing is offered. A verified offer is still "not yet enabled": nothing can start from this surface.</item>
/// </list>
/// </summary>
public sealed class ChocolateyClientUpdateViewModel
{
    public const string SettingsCardTitle = "Chocolatey";
    public const string SettingsCardDescription =
        "Keeping the Chocolatey client itself up to date. PC Steward offers only releases it has reviewed, and every update needs your review.";
    public const string DetailHeading = "Chocolatey client update";
    public const string DetailCopy =
        "PC Steward can update Chocolatey itself only to a newer release it has reviewed. You can read what that update would do; it cannot start yet.";
    public const string EntryText = "View Chocolatey update…";
    public const string EntryAutomationDetail = "Opens the review. This update cannot be confirmed yet.";
    public const string GenericTitle = "Update Chocolatey";
    public const string ConfirmText = "Update Chocolatey";
    public const string CloseText = "Close";
    public const string FactsHeading = "What this update does";
    public const string DisclosuresHeading = "Chocolatey's own setup also";
    public const string UnavailableHeading = "Why this update cannot be confirmed";
    public const string NotStartedText = "No update was started. Updating Chocolatey itself is not available yet.";

    public const string InstalledUnknownText = "Unknown: no scan has reported a Chocolatey version.";
    public const string InstalledUnreadableText = "Unknown: the last scan reported a Chocolatey version that could not be read.";
    public const string ReportedByLastScanLabel = "Reported by the last scan";
    public const string ReportedByLastScanText = "(reported by the last scan)";
    public const string ReportedDisplayOnlyText = "Shown for information only; PC Steward offers an update only from the installed Chocolatey it verified.";
    public const string AvailableTitlePrefix = "A newer reviewed Chocolatey release is available: ";
    public const string NotYetEnabledSuffix = " (not yet enabled)";
    public const string NoNewerReleaseText = "No newer reviewed Chocolatey release is available.";
    public const string InstalledNotListedOfferText = "No update offered: the installed release is not on the reviewed list.";
    public const string CatalogUnavailableOfferText = "No update offered: PC Steward's list of reviewed releases is unavailable.";
    public const string CouldNotVerifyText = "PC Steward could not verify the installed Chocolatey";
    public const string CouldNotVerifyOfferText = "No update offered: " + CouldNotVerifyText + ".";
    public const string NotCheckedYetText = "Not checked yet";
    public const string NotCheckedInstalledText = NotCheckedYetText + ". PC Steward checks the installed Chocolatey when you open this review.";
    public const string NotCheckedOfferText = "No update offered until PC Steward verifies the installed Chocolatey.";
    public const string CheckingText = "Checking the installed Chocolatey…";
    public const string CheckingOfferText = "No update offered while PC Steward checks the installed Chocolatey.";
    public const string RefusedDetailText = "its files, folder or settings do not exactly match a reviewed release.";
    public const string UnavailableDetailText = "the check could not be completed.";
    public const string TimedOutDetailText = "the check did not finish in time.";
    public const string CancelledDetailText = "the check was stopped.";
    public const string ReviewedReleasesUnavailableText = "Unavailable";
    public const string NoNewerReleaseShort = "No newer reviewed release";
    public const string InstalledNotListedShort = "Not on the reviewed list";
    public const string CouldNotVerifyShort = "Could not verify the installed Chocolatey";
    public const string CheckingShort = "Checking…";
    public const string CatalogUnavailableShort = "Reviewed releases unavailable";
    public const string NotAvailableYetShort = "not available yet";
    public const string PermissionText =
        "Windows will ask for permission once, after you confirm. Checking for this update never asks for administrator permission.";
    public const string WatchdogText =
        "While Chocolatey updates, PC Steward keeps checking its folder and settings. If the folder becomes changeable by your account, " +
        "a setting that weakens its protection appears, or the checks cannot continue, PC Steward stops the update immediately and shows " +
        "it as not verified, with what to do next.";
    public const string UntrustedFolderCautionText =
        "If Chocolatey's folder may have been left changeable by your account, or PC Steward loses track of the update, the result says so: " +
        ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep;

    public const string NotAdmittedText = "Not available yet: waiting for review and owner approval.";
    public const string NoNewerReviewedReleaseText =
        "No newer reviewed release: Chocolatey can be updated only to a newer release PC Steward has reviewed.";
    public const string InstalledReleaseNotReviewedText =
        "The verified Chocolatey release is not on PC Steward's reviewed list, so no update is offered. Bringing Chocolatey " +
        "to a reviewed release is done outside PC Steward.";
    public const string ReviewedReleasesUnavailableReasonText =
        "PC Steward's list of reviewed releases is unavailable, so no update is offered.";
    public const string InstalledReleaseNotCheckedText =
        "Not checked yet: PC Steward offers an update only after it verifies the installed Chocolatey, which it does when you open this review.";
    public const string InstalledReleaseCheckingText =
        "PC Steward is checking the installed Chocolatey; nothing is offered until it is verified.";
    public const string InstalledReleaseNotVerifiedText =
        CouldNotVerifyText + ", so no update is offered. PC Steward offers an update only from a Chocolatey whose files exactly match a reviewed release.";

    private ChocolateyClientUpdateViewModel(ChocolateyInstalledClientVerification verification, string? reported, bool installedReported,
        IReadOnlyList<string> listed, bool catalogAvailable, string signer, ReviewedRouteSplitTokenState splitToken)
    {
        Verification = verification;
        string? installed = verification.Check == ChocolateyInstalledClientCheck.Verified ? verification.InstalledRelease : null;
        // ADR-031 / design §8.6 gate 3: the offer comes only from the verified installed release and the compiled list's rule.
        string? offer = OfferTarget(installed, listed);
        VerifiedInstalledVersion = installed;
        ReportedVersion = reported;
        InstalledReported = installedReported;
        CatalogAvailable = catalogAvailable;
        ListedVersions = listed;
        OfferVersion = offer;
        SplitTokenState = splitToken;
        Signer = signer;

        // No reviewed client operation can be prepared while the review gate refuses the route: always unavailable.
        List<ChocolateyClientUpdateUnavailableReason> reasons = [ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview];
        if (offer is null)
            reasons.Add(verification.Check switch
            {
                ChocolateyInstalledClientCheck.Verified => !catalogAvailable ? ChocolateyClientUpdateUnavailableReason.ReviewedReleasesUnavailable
                    : listed.Contains(installed!, StringComparer.Ordinal) ? ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease
                    : ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed,
                ChocolateyInstalledClientCheck.NotChecked => ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotChecked,
                ChocolateyInstalledClientCheck.Checking => ChocolateyClientUpdateUnavailableReason.InstalledReleaseChecking,
                _ => ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotVerified,
            });
        switch (splitToken)
        {
            case ReviewedRouteSplitTokenState.SplitTokenLimited: break;
            case ReviewedRouteSplitTokenState.NoSplitToken: reasons.Add(ChocolateyClientUpdateUnavailableReason.SplitTokenRequired); break;
            case ReviewedRouteSplitTokenState.DesktopTokenUnsupported: reasons.Add(ChocolateyClientUpdateUnavailableReason.DesktopTokenUnsupported); break;
            default: reasons.Add(ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown); break;
        }
        UnavailableReasons = reasons.AsReadOnly();
        UnavailableFields = UnavailableReasons.Select(reason => new ReviewedDisplayField("Unavailable", Text(reason))).ToArray();

        Fields = new ReviewedDisplayField[]
        {
            new("Installed", InstalledText),
            new(ReportedByLastScanLabel, ReportedText),
            new("Offer", OfferText),
            new("Reviewed releases", ReviewedReleasesText),
            new("Signer", SignerText),
            new("What happens", ChocolateyClientUpdatePresentation.WhatHappensText),
            new("Verification", ChocolateyClientUpdatePresentation.VerificationText),
            new("Administrator permission", PermissionText),
            new("Safety checks while it runs", WatchdogText),
            new("If the folder may have been changed", UntrustedFolderCautionText),
        };
        DisclosureFields = ChocolateyClientUpdatePresentation.Disclosures.Select(text => new ReviewedDisplayField("Chocolatey's setup", text)).ToArray();
    }

    /// <summary>
    /// Opens the view: probes the split-token state now (a probe fault or undefined value is Unknown), validates the compiled
    /// catalog (any malformed or non-ascending entry, or a fault reading it, fails closed to an unavailable, empty list), normalizes
    /// the reported installed version for display (null: no scan reported one; anything else malformed: reported but unreadable)
    /// and applies the ADR-031 offer rule to the <paramref name="verification"/>'s verified release only.
    /// </summary>
    public static ChocolateyClientUpdateViewModel Open(ChocolateyClientReleaseCatalogFacts catalog, ChocolateyInstalledClientVerification verification,
        string? reportedInstalledVersion, Func<ReviewedRouteSplitTokenState> probeSplitToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(probeSplitToken);
        ReviewedRouteSplitTokenState splitToken;
        try { splitToken = probeSplitToken(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { splitToken = ReviewedRouteSplitTokenState.Unknown; }
        if (!Enum.IsDefined(splitToken)) splitToken = ReviewedRouteSplitTokenState.Unknown;

        string[] listed;
        try { listed = ValidCatalog(catalog.ListedVersions) ? catalog.ListedVersions.ToArray() : []; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { listed = []; }
        string signer = string.IsNullOrWhiteSpace(catalog.Signer) || catalog.Signer.Length > 128 ? "Unknown" : catalog.Signer;
        string? reported = ChocolateyClientUpdatePresentation.IsReleaseVersion(reportedInstalledVersion) ? reportedInstalledVersion : null;
        return new(verification, reported, reportedInstalledVersion is not null, listed, listed.Length != 0, signer, splitToken);
    }

    /// <summary>
    /// The same view (catalog, reported release and the split-token state probed when it opened) with a new check result: the
    /// review dialog opens as <see cref="ChocolateyInstalledClientVerification.Checking"/> and shows the result when the check ends.
    /// </summary>
    public ChocolateyClientUpdateViewModel WithVerification(ChocolateyInstalledClientVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return new(verification, ReportedVersion, InstalledReported, ListedVersions, CatalogAvailable, Signer, SplitTokenState);
    }

    /// <summary>
    /// The ADR-031 offer rule (design §3.3) over display data: when <paramref name="installed"/> is exactly a listed release and the
    /// newest listed release is strictly newer, that newest release; otherwise null. Never an equal or older release, and nothing
    /// for an unknown or unlisted installed release.
    /// </summary>
    internal static string? OfferTarget(string? installed, IReadOnlyList<string> listed)
    {
        if (installed is null || listed.Count == 0 || !listed.Contains(installed, StringComparer.Ordinal)) return null;
        string newest = listed[^1];
        return IsNewer(installed, newest) ? newest : null;
    }

    /// <summary>
    /// The installed Chocolatey client version as the last scan's Chocolatey package list reported it: exactly one distinct,
    /// well-formed version of the <c>chocolatey</c> package from the Chocolatey inventory. Null when the scan reported no
    /// <c>chocolatey</c> package (not reported); <see cref="string.Empty"/> when it reported one whose version cannot be read
    /// (missing, malformed or conflicting), which <see cref="Open"/> shows as reported but unreadable (independent review L4).
    /// </summary>
    public static string? ReportedInstalledVersion(IEnumerable<InventoryRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        string[] versions = rows.SelectMany(row => row.Instances).Where(IsClientInstance).Select(instance => instance.Version ?? string.Empty)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (versions.Length == 0) return null;
        return versions.Length == 1 && ChocolateyClientUpdatePresentation.IsReleaseVersion(versions[0]) ? versions[0] : string.Empty;
    }

    /// <summary>True for the inventory row of the Chocolatey client itself (the <c>chocolatey</c> package from the Chocolatey inventory).</summary>
    public static bool IsClientRow(InventoryRow? row) => row is not null && row.Instances.Any(IsClientInstance);

    private static bool IsClientInstance(InstallationInstance instance) =>
        string.Equals(instance.Provider, "chocolatey", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(instance.ProviderIdentity, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The check result this view shows (never cached by the Desktop: each review checks afresh).</summary>
    public ChocolateyInstalledClientVerification Verification { get; }

    /// <summary>The installed release PC Steward verified; null unless <see cref="Verification"/> is Verified. The only input to the offer.</summary>
    public string? VerifiedInstalledVersion { get; }

    /// <summary>The release the last scan reported (well-formed only); display information, never an input to the offer.</summary>
    public string? ReportedVersion { get; }

    /// <summary>True when the last scan reported a Chocolatey client version, even one that could not be read.</summary>
    public bool InstalledReported { get; }

    public IReadOnlyList<string> ListedVersions { get; }

    /// <summary>False when the compiled list failed validation or could not be read: "Reviewed releases: Unavailable".</summary>
    public bool CatalogAvailable { get; }

    public string? OfferVersion { get; }
    public string Signer { get; }

    /// <summary>The split-token state probed when this view was opened.</summary>
    public ReviewedRouteSplitTokenState SplitTokenState { get; }

    /// <summary>
    /// ADR-031 design §8.6 gate 3: only a verified installed release yields an offer, still not enabled while the route is not
    /// admitted: "A newer reviewed Chocolatey release is available: X (not yet enabled)"; otherwise "Update Chocolatey".
    /// </summary>
    public string Title => OfferVersion is null ? GenericTitle : AvailableTitlePrefix + OfferVersion + NotYetEnabledSuffix;

    /// <summary>The installed Chocolatey as PC Steward's check found it: verified, not checked yet, checking, or why it is not verified.</summary>
    public string InstalledText => Verification.Check switch
    {
        ChocolateyInstalledClientCheck.Verified =>
            $"Chocolatey {VerifiedInstalledVersion}, verified by PC Steward: every Chocolatey file matches this reviewed release. PC Steward checks " +
            "the files again before any update.",
        ChocolateyInstalledClientCheck.NotChecked => NotCheckedInstalledText,
        ChocolateyInstalledClientCheck.Checking => CheckingText,
        ChocolateyInstalledClientCheck.Refused => CouldNotVerifyText + ": " + RefusedDetailText,
        ChocolateyInstalledClientCheck.TimedOut => CouldNotVerifyText + ": " + TimedOutDetailText,
        ChocolateyInstalledClientCheck.Cancelled => CouldNotVerifyText + ": " + CancelledDetailText,
        _ => CouldNotVerifyText + ": " + UnavailableDetailText,
    };

    /// <summary>The release the last scan reported, as secondary display information (independent review L2/L4).</summary>
    public string ReportedText => ReportedVersion is not null
        ? $"Chocolatey {ReportedVersion} {ReportedByLastScanText}. {ReportedDisplayOnlyText}"
        : InstalledReported ? InstalledUnreadableText : InstalledUnknownText;

    /// <summary>The verified offer, or why nothing is offered (not checked, checking, not verified, list unavailable, unlisted, none newer).</summary>
    public string OfferText => OfferVersion is not null
        ? $"{AvailableTitlePrefix}{OfferVersion}{NotYetEnabledSuffix}. PC Steward verified the installed Chocolatey {VerifiedInstalledVersion}; " +
          $"{OfferVersion} is the newest release on its reviewed list. Updating Chocolatey itself is {NotAvailableYetShort}."
        : NoOfferState switch
        {
            NoOffer.NotChecked => NotCheckedOfferText,
            NoOffer.Checking => CheckingOfferText,
            NoOffer.NotVerified => CouldNotVerifyOfferText,
            NoOffer.CatalogUnavailable => CatalogUnavailableOfferText,
            NoOffer.InstalledNotListed => InstalledNotListedOfferText,
            _ => NoNewerReleaseText,
        };

    public string ReviewedReleasesText => CatalogAvailable ? string.Join(", ", ListedVersions) : ReviewedReleasesUnavailableText;

    public string SignerText =>
        $"Each reviewed release is signed by {Signer}. Before anything runs, PC Steward checks the installed Chocolatey's signature and " +
        "that every file exactly matches a reviewed release.";

    /// <summary>
    /// The Settings card, Installed apps detail "Status" and review dialog summary. Before a check, the release is the scan's,
    /// qualified as such: "Chocolatey 2.7.5 (reported by the last scan) · Not checked yet" (independent review L2). After a
    /// verified check: "Chocolatey 2.7.5 (verified) · No newer reviewed release", or "Chocolatey 2.7.5 (verified) · 2.8.0 is a newer
    /// reviewed release; updating is not available yet" (never the "Update available — verified" status, which is reserved for an
    /// update that can be reviewed and started).
    /// </summary>
    public string Summary =>
        $"Chocolatey {(VerifiedInstalledVersion is not null ? VerifiedInstalledVersion + " (verified)" : ReportedVersion is null ? "(release unknown)" : ReportedVersion + " " + ReportedByLastScanText)} · " +
        (OfferVersion is not null
            ? $"{OfferVersion} is a newer reviewed release; updating is {NotAvailableYetShort}"
            : NoOfferState switch
            {
                NoOffer.NotChecked => NotCheckedYetText,
                NoOffer.Checking => CheckingShort,
                NoOffer.NotVerified => CouldNotVerifyShort,
                NoOffer.CatalogUnavailable => CatalogUnavailableShort,
                NoOffer.InstalledNotListed => InstalledNotListedShort,
                _ => NoNewerReleaseShort,
            });

    private enum NoOffer { NotChecked, Checking, NotVerified, CatalogUnavailable, InstalledNotListed, NoNewerListed }

    // Why nothing is offered (meaningful only when OfferVersion is null), in precedence order.
    private NoOffer NoOfferState => Verification.Check switch
    {
        ChocolateyInstalledClientCheck.NotChecked => NoOffer.NotChecked,
        ChocolateyInstalledClientCheck.Checking => NoOffer.Checking,
        ChocolateyInstalledClientCheck.Verified => !CatalogAvailable ? NoOffer.CatalogUnavailable
            : !ListedVersions.Contains(VerifiedInstalledVersion!, StringComparer.Ordinal) ? NoOffer.InstalledNotListed
            : NoOffer.NoNewerListed,
        _ => NoOffer.NotVerified,
    };

    /// <summary>
    /// The review facts in display order (installed, reported by the last scan, offer, list, signer, what happens, verification,
    /// permission, watchdog, caution).
    /// </summary>
    public IReadOnlyList<ReviewedDisplayField> Fields { get; }

    /// <summary>The native side effects of Chocolatey's setup (design §2 R4), disclosed before review.</summary>
    public IReadOnlyList<ReviewedDisplayField> DisclosureFields { get; }

    public IReadOnlyList<ChocolateyClientUpdateUnavailableReason> UnavailableReasons { get; }

    /// <summary>One line per unavailability reason, in display order.</summary>
    public IReadOnlyList<ReviewedDisplayField> UnavailableFields { get; }

    public bool IsUnavailable => UnavailableReasons.Count != 0;

    /// <summary>False while any reason applies; the not-admitted reason always applies, so this is always false.</summary>
    public bool CanConfirm => UnavailableReasons.Count == 0;

    /// <summary>Bound to the Confirm button's enabled state: exactly <see cref="CanConfirm"/>.</summary>
    public bool IsConfirmEnabled => CanConfirm;

    public string ConfirmAutomationName => CanConfirm ? "Update Chocolatey after review" : "Update Chocolatey is unavailable: " + NotAdmittedText;

    public string EntryAutomationName => AccessibilityText.LabelInName(EntryText, Summary + ". " + EntryAutomationDetail);

    public static string Text(ChocolateyClientUpdateUnavailableReason reason) => reason switch
    {
        ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview => NotAdmittedText,
        ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease => NoNewerReviewedReleaseText,
        ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed => InstalledReleaseNotReviewedText,
        ChocolateyClientUpdateUnavailableReason.ReviewedReleasesUnavailable => ReviewedReleasesUnavailableReasonText,
        ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotChecked => InstalledReleaseNotCheckedText,
        ChocolateyClientUpdateUnavailableReason.InstalledReleaseChecking => InstalledReleaseCheckingText,
        ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotVerified => InstalledReleaseNotVerifiedText,
        ChocolateyClientUpdateUnavailableReason.SplitTokenRequired => ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText,
        ChocolateyClientUpdateUnavailableReason.DesktopTokenUnsupported => ChocolateyReviewDisclosurePresentation.DesktopTokenUnsupportedText,
        ChocolateyClientUpdateUnavailableReason.DesktopTokenUnknown => ChocolateyReviewDisclosurePresentation.DesktopTokenUnknownText,
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static bool ValidCatalog(IReadOnlyList<string>? versions)
    {
        if (versions is null || versions.Count is 0 or > 64) return false;
        for (int index = 0; index < versions.Count; index++)
        {
            if (!ChocolateyClientUpdatePresentation.IsReleaseVersion(versions[index])) return false;
            if (index > 0 && !IsNewer(versions[index - 1], versions[index])) return false;
        }
        return true;
    }

    private static bool IsNewer(string than, string candidate) =>
        VersionComparison.Compare(new VersionComparisonRequest(than, candidate)).Outcome == VersionComparisonOutcome.Newer;
}
