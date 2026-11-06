using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>The result view of one Confirm press (design §4.3): a heading and the lines shown in place of the progress line.</summary>
public sealed record ChocolateyClientUpdateRunView(string Heading, IReadOnlyList<string> Lines, bool Succeeded);

/// <summary>
/// FR-UPD-15, FR-UI-04, FR-HIS-01/03; Chocolatey Confirm design §4.3: the progress and result texts of a confirmed Chocolatey
/// client update. A finalized or uncertain result is shown with the same status and verification text that history shows for
/// the recorded job (<see cref="ReviewedUpdateHistoryPresentation"/>), so live text equals history. Display only.
/// </summary>
public static class ChocolateyClientUpdateRunPresentation
{
    public const string CheckingAgainText = "Checking Chocolatey again before it starts…";
    public const string WaitingForPermissionText =
        "Windows is asking for administrator permission. After you allow it, Chocolatey updates while PC Steward watches its folder and settings.";
    public const string VerifyingText = "Checking the updated Chocolatey against the reviewed release…";
    public const string RunningCloseGuardText = "Chocolatey is updating. Closing PC Steward now leaves the result unverified.";
    public const string NothingStartedPrefix = "Nothing was started.";
    public const string ElevationDeniedText = "You declined the administrator prompt. Nothing was changed.";
    public const string SucceededHeading = "Chocolatey updated";
    public const string NotVerifiedHeading = "Chocolatey update not verified";
    public const string NotStartedHeading = "Update not started";
    public const string UncertainHeading = "Chocolatey update result unknown";

    public static string PhaseText(ChocolateyClientUpdatePhase phase) => phase switch
    {
        ChocolateyClientUpdatePhase.WaitingForPermission => WaitingForPermissionText,
        ChocolateyClientUpdatePhase.Verifying => VerifyingText,
        _ => CheckingAgainText,
    };

    /// <summary>The text of a refusal before anything started (design §2, re-checked on press), after the fixed prefix.</summary>
    public static string RefusalText(ChocolateyClientUpdateRefusal refusal) => NothingStartedPrefix + " " + refusal switch
    {
        ChocolateyClientUpdateRefusal.NotAdmitted => ChocolateyClientUpdateViewModel.NotAdmittedText,
        ChocolateyClientUpdateRefusal.SplitTokenNotLimited => ChocolateyReviewDisclosurePresentation.SplitTokenRequiredText,
        ChocolateyClientUpdateRefusal.EarlierUpdateNeedsAttention => ChocolateyClientUpdateViewModel.EarlierUpdateNeedsAttentionText,
        _ => ChocolateyClientUpdateViewModel.ReviewExpiredText,
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

    /// <summary>The history record of the job this session ran, from a history read; null when it is not there.</summary>
    public static ReviewedUpdateHistoryView? RecordedView(IEnumerable<ReviewedUpdateHistoryRecord>? records, Guid jobId)
    {
        if (records is null) return null;
        ReviewedUpdateHistoryRecord? record = records.FirstOrDefault(candidate => candidate is not null && candidate.JobId == jobId);
        return record is null ? null : ReviewedUpdateHistoryPresentation.Present(record);
    }
}
