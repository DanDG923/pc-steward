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
    /// <summary><see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/> is not admitted (ADR-031 owner decisions 3–4).</summary>
    NotAdmittedPendingReview,
    /// <summary>The installed release is listed, but no strictly newer listed release exists (ADR-031 design §3.3).</summary>
    NoNewerReviewedRelease,
    /// <summary>The installed release is unknown or not on the reviewed list, so nothing is offered (ADR-031 owner decision 2).</summary>
    InstalledReleaseNotReviewed,
    /// <summary>design rev 6 §(L6): UAC off, standard-user desktop, or an elevated desktop.</summary>
    SplitTokenRequired,
    DesktopTokenUnsupported,
    DesktopTokenUnknown,
}

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-UI-03/04, TM-03/04/07/13; ADR-031 (Option 2, owner decisions 1–5), ADR-030; design mapping
/// §3.3, §3.7 and §8: the portable view model behind the Desktop's Chocolatey client update surface. It shows the installed
/// Chocolatey release as the last scan reported it (or why it is unknown), the offer (the newest strictly newer release on the compiled
/// reviewed list, only from a listed installed release), the review-sheet facts and disclosures of
/// <see cref="ChocolateyClientUpdatePresentation"/>, and every reason the update is unavailable.
/// <list type="bullet">
/// <item>No reviewed client operation can be prepared while <c>ReviewedUpdateReview.HasSupportedMode</c> refuses
/// <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/>, so the not-admitted reason is always present and
/// <see cref="CanConfirm"/> is always false. This type holds no review, draft, permit or confirmation path; it starts nothing.</item>
/// <item>The split-token state is probed through the injected function on every <see cref="Open"/>, never cached.</item>
/// <item>The installed version comes from the scan's Chocolatey package list and is display data only: the offer is
/// recomputed by the native observer from the held client's files before any job could exist. Until admission the offer is
/// therefore worded as "may be available", never as an update that will happen (independent review L3).</item>
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
    public const string ReportedByLastScanText = "(reported by the last scan)";
    public const string MayBeAvailableTitlePrefix = "A newer reviewed Chocolatey release may be available: ";
    public const string NoNewerReleaseText = "No newer reviewed Chocolatey release is available.";
    public const string InstalledNotListedOfferText = "No update offered: the installed release is not on the reviewed list.";
    public const string InstalledUnknownOfferText = "No update offered: the installed release is unknown.";
    public const string CatalogUnavailableOfferText = "No update offered: PC Steward's list of reviewed releases is unavailable.";
    public const string ReviewedReleasesUnavailableText = "Unavailable";
    public const string NoNewerReleaseShort = "No newer reviewed release";
    public const string InstalledNotListedShort = "Not on the reviewed list";
    public const string InstalledUnknownShort = "No update offered";
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
        "The installed Chocolatey release is unknown or not on PC Steward's reviewed list, so no update is offered. Bringing Chocolatey " +
        "to a reviewed release is done outside PC Steward.";

    private ChocolateyClientUpdateViewModel(string? installed, bool installedReported, IReadOnlyList<string> listed, bool catalogAvailable,
        string signer, string? offer, ReviewedRouteSplitTokenState splitToken)
    {
        InstalledVersion = installed;
        InstalledReported = installedReported;
        CatalogAvailable = catalogAvailable;
        ListedVersions = listed;
        OfferVersion = offer;
        SplitTokenState = splitToken;
        Signer = signer;

        // No reviewed client operation can be prepared while the review gate refuses the route: always unavailable.
        List<ChocolateyClientUpdateUnavailableReason> reasons = [ChocolateyClientUpdateUnavailableReason.NotAdmittedPendingReview];
        if (offer is null)
            reasons.Add(installed is not null && listed.Contains(installed, StringComparer.Ordinal)
                ? ChocolateyClientUpdateUnavailableReason.NoNewerReviewedRelease
                : ChocolateyClientUpdateUnavailableReason.InstalledReleaseNotReviewed);
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
    /// the reported installed version (null: no scan reported one; anything else malformed: reported but unreadable) and applies
    /// the ADR-031 offer rule.
    /// </summary>
    public static ChocolateyClientUpdateViewModel Open(ChocolateyClientReleaseCatalogFacts catalog, string? reportedInstalledVersion,
        Func<ReviewedRouteSplitTokenState> probeSplitToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(probeSplitToken);
        ReviewedRouteSplitTokenState splitToken;
        try { splitToken = probeSplitToken(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { splitToken = ReviewedRouteSplitTokenState.Unknown; }
        if (!Enum.IsDefined(splitToken)) splitToken = ReviewedRouteSplitTokenState.Unknown;

        string[] listed;
        try { listed = ValidCatalog(catalog.ListedVersions) ? catalog.ListedVersions.ToArray() : []; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { listed = []; }
        string signer = string.IsNullOrWhiteSpace(catalog.Signer) || catalog.Signer.Length > 128 ? "Unknown" : catalog.Signer;
        string? installed = ChocolateyClientUpdatePresentation.IsReleaseVersion(reportedInstalledVersion) ? reportedInstalledVersion : null;
        return new(installed, reportedInstalledVersion is not null, listed, listed.Length != 0, signer, OfferTarget(installed, listed), splitToken);
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

    public string? InstalledVersion { get; }

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
    /// Independent review L3: until admission the offer comes from unverified scan data, so it is never asserted as an update:
    /// "A newer reviewed Chocolatey release may be available: X" when the rule offers X; otherwise "Update Chocolatey".
    /// </summary>
    public string Title => OfferVersion is null ? GenericTitle : MayBeAvailableTitlePrefix + OfferVersion;

    public string InstalledText => InstalledVersion is not null
        ? $"Chocolatey {InstalledVersion}, as Chocolatey's package list reported at the last scan. PC Steward checks every Chocolatey file " +
          "against the reviewed release before any update."
        : InstalledReported ? InstalledUnreadableText : InstalledUnknownText;

    /// <summary>Independent review M1/L3: the possible offer, or why nothing is offered (unknown, list unavailable, unlisted, none newer).</summary>
    public string OfferText => OfferVersion is not null
        ? $"A newer reviewed Chocolatey release may be available: {OfferVersion}. The last scan reported Chocolatey {InstalledVersion}; " +
          $"{OfferVersion} is the newest release on PC Steward's reviewed list, and PC Steward checks the installed Chocolatey's files before any update."
        : NoOfferState switch
        {
            NoOffer.InstalledUnknown => InstalledUnknownOfferText,
            NoOffer.CatalogUnavailable => CatalogUnavailableOfferText,
            NoOffer.InstalledNotListed => InstalledNotListedOfferText,
            _ => NoNewerReleaseText,
        };

    public string ReviewedReleasesText => CatalogAvailable ? string.Join(", ", ListedVersions) : ReviewedReleasesUnavailableText;

    public string SignerText =>
        $"Each reviewed release is signed by {Signer}. Before anything runs, PC Steward checks the installed Chocolatey's signature and " +
        "that every file exactly matches a reviewed release.";

    /// <summary>
    /// The Settings card and Installed apps detail "Status" summary, for example
    /// "Chocolatey 2.7.5 (reported by the last scan) · No newer reviewed release" (independent review L2: the version is the scan's).
    /// </summary>
    public string Summary =>
        $"Chocolatey {(InstalledVersion is null ? "(release unknown)" : InstalledVersion + " " + ReportedByLastScanText)} · " + (OfferVersion is not null
            ? $"{OfferVersion} may be available; updating is {NotAvailableYetShort}"
            : NoOfferState switch
            {
                NoOffer.InstalledUnknown => InstalledUnknownShort,
                NoOffer.CatalogUnavailable => CatalogUnavailableShort,
                NoOffer.InstalledNotListed => InstalledNotListedShort,
                _ => NoNewerReleaseShort,
            });

    private enum NoOffer { InstalledUnknown, CatalogUnavailable, InstalledNotListed, NoNewerListed }

    // Why nothing is offered (meaningful only when OfferVersion is null), in precedence order.
    private NoOffer NoOfferState => InstalledVersion is null ? NoOffer.InstalledUnknown
        : !CatalogAvailable ? NoOffer.CatalogUnavailable
        : !ListedVersions.Contains(InstalledVersion, StringComparer.Ordinal) ? NoOffer.InstalledNotListed
        : NoOffer.NoNewerListed;

    /// <summary>The review facts in display order (installed, offer, list, signer, what happens, verification, permission, watchdog, caution).</summary>
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
