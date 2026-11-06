using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-UPD-15, FR-UI-04, FR-HIS-01/03; Chocolatey package Confirm addendum §4: the progress and result texts of a confirmed
/// Chocolatey package update. A finalized or uncertain result is shown with the same status and verification text that history
/// shows for the recorded job (<see cref="ReviewedUpdateHistoryPresentation"/>), so live text equals history. Display only.
/// </summary>
public static class ChocolateyPackageUpdateRunPresentation
{
    public const string CheckingAgainText = "Checking the package and Chocolatey again before it starts…";
    public const string WaitingForPermissionText =
        "Windows is asking for administrator permission. After you allow it, Chocolatey confirms the exact version and updates the package.";
    public const string VerifyingText = "Checking the updated package on this PC…";
    public const string RunningCloseGuardText = "The package is updating. Closing PC Steward now leaves the result unverified.";
    public const string NothingStartedPrefix = ChocolateyClientUpdateRunPresentation.NothingStartedPrefix;
    public const string ElevationDeniedText = ChocolateyClientUpdateRunPresentation.ElevationDeniedText;
    public const string SucceededHeading = "Package updated";
    public const string NotVerifiedHeading = "Package update not verified";
    public const string NotStartedHeading = ChocolateyClientUpdateRunPresentation.NotStartedHeading;
    public const string UncertainHeading = "Package update result unknown";
    public const string LostTrackText =
        "PC Steward lost track of this update. Check the package with Chocolatey before relying on it, and look at the update history.";

    public static string PhaseText(ChocolateyClientUpdatePhase phase) => phase switch
    {
        ChocolateyClientUpdatePhase.WaitingForPermission => WaitingForPermissionText,
        ChocolateyClientUpdatePhase.Verifying => VerifyingText,
        _ => CheckingAgainText,
    };

    /// <summary>The text of a refusal before anything started, after the fixed prefix.</summary>
    public static string RefusalText(ChocolateyClientUpdateRefusal refusal) => NothingStartedPrefix + " " + refusal switch
    {
        ChocolateyClientUpdateRefusal.NotAdmitted => ChocolateyPackageUpdateViewModel.NotAdmittedText,
        ChocolateyClientUpdateRefusal.CheckTooOld => ChocolateyPackageUpdateViewModel.CheckTooOldText,
        ChocolateyClientUpdateRefusal.SplitTokenNotLimited => ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText,
        ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention => ChocolateyPackageUpdateViewModel.EarlierUpdateNeedsAttentionText,
        _ => ChocolateyPackageUpdateViewModel.ReviewExpiredText,
    };

    /// <summary>
    /// The result of one press. A refusal or a not-started result says that nothing was started; a declined administrator prompt
    /// says nothing was changed; every recorded result shows the history view of its record (<paramref name="recorded"/>), or the
    /// workflow's recorded message when the record cannot be read back.
    /// </summary>
    public static ChocolateyClientUpdateRunView Present(ChocolateyClientUpdateRunResult run, ReviewedUpdateHistoryView? recorded)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Result is not { } result) return new(NotStartedHeading, [RefusalText(run.Refusal)], false);
        if (result.Outcome is null || result.Disposition is ReviewedUpdateDispatchDisposition.NotStarted or ReviewedUpdateDispatchDisposition.NotStartedClaimRetained)
            return new(NotStartedHeading, [NothingStartedPrefix + " " + result.Message], false);
        if (result.Outcome == UpdateDispatchOutcome.ElevationDenied) return new(NotStartedHeading, [ElevationDeniedText], false);
        bool succeeded = result.Outcome == UpdateDispatchOutcome.Success;
        string heading = succeeded ? SucceededHeading : result.Outcome == UpdateDispatchOutcome.Uncertain ? UncertainHeading : NotVerifiedHeading;
        if (recorded is null) return new(heading, [result.Message], succeeded);
        List<string> lines = [recorded.Status];
        if (!string.IsNullOrWhiteSpace(recorded.VerificationDetail)) lines.Add(recorded.VerificationDetail);
        if (!string.IsNullOrWhiteSpace(recorded.LingeringProcessesNote)) lines.Add(recorded.LingeringProcessesNote);
        return new(heading, lines.AsReadOnly(), succeeded);
    }
}
