using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// Stage C4 (Task 54 R1; FR-UPD-15, FR-UPD-01/03, AC-UPD-03; owner decision 2026-11-12 "Silent for all per-user apps"): the reviewed
/// route of a desktop WinGet update of an app installed for the current user. The installer mode is bound when the review is made
/// (<see cref="BindMode"/>), checked again against the fresh re-preparation immediately before approval, and frozen with the approved
/// job into a <see cref="ReviewedUpdateOperation"/> on <see cref="UpdateRouteContract.WinGetUserSilentV1"/> (<see cref="Freeze"/>).
/// The non-elevated runner accepts only such an operation (<see cref="Admits"/>) and builds the provider arguments from it alone.
/// Nothing here is native qualification, consent or a dispatch permit.
/// </summary>
public static class WingetUserReviewedRoute
{
    /// <summary>The fixed installation context of a per-user WinGet operation (data only).</summary>
    public const string InstallationContextId = "user:winget";

    /// <summary>Shown when a per-user selection has no reviewed installer mode. Nothing was approved or started.</summary>
    public const string NoReviewedModeMessage =
        "PC Steward has no reviewed way to run this update silently for your account, so it can't be updated here. Nothing was started.";

    /// <summary>Shown when the reviewed mode no longer matches the fresh evidence before launch. Nothing was approved or started.</summary>
    public const string ModeChangedMessage =
        "The installer mode you reviewed no longer applies to this update. Review it again; no update was started.";

    /// <summary>Shown when the current user's identity could not be read before launch. Nothing was started.</summary>
    public const string RequesterUnavailableMessage =
        "PC Steward could not confirm which Windows account is running it, so the update wasn't started.";

    /// <summary>
    /// The reviewed installer mode for <paramref name="selection"/>: <c>Selected(Silent)</c> for a WinGet user-scope update from the
    /// <c>winget</c> or <c>msstore</c> source, from the compiled capability table; null for anything else (no reviewed mode).
    /// </summary>
    public static ReviewedInstallerMode? BindMode(UpdateJobSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.Provider != UpdateProvider.WinGet || selection.Scope != UpdateScope.User ||
            selection.Source is not (UpdateSource.WinGet or UpdateSource.MicrosoftStore)) return null;
        ReviewedInstallerMode? mode = UpdateInstallerModeCapabilities.Resolve(selection.Provider, selection.Source, selection.Scope);
        return mode == ReviewedInstallerMode.Selected(InstallerMode.Silent) ? mode : null;
    }

    /// <summary>
    /// Freezes the approved <paramref name="job"/> with the mode bound at review into the per-user reviewed operation, or null when the
    /// review bound no mode, the mode no longer resolves for the job's selection, or any identity is outside its bounded format.
    /// <paramref name="evidenceId"/> and <paramref name="evidenceObservedAt"/> name the fresh inventory observation the job was
    /// re-prepared from (data only).
    /// </summary>
    public static ReviewedUpdateOperation? Freeze(ImmutableUpdateJob job, ReviewedInstallerMode? reviewedMode, string? requestingUserSid,
        string evidenceId, DateTimeOffset evidenceObservedAt)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (reviewedMode is null || BindMode(job.Selection) != reviewedMode || string.IsNullOrWhiteSpace(requestingUserSid)) return null;
        try
        {
            ReviewedUpdateOperation operation = new(job, requestingUserSid, job.Selection.StableProductKey, InstallationContextId, evidenceId,
                evidenceObservedAt, UpdateRouteContract.WinGetUserSilentV1, reviewedMode);
            return Admits(operation) && operation.BindsJob(job) ? operation : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The runner's check: the operation is on <see cref="UpdateRouteContract.WinGetUserSilentV1"/>, its mode is the one the compiled
    /// table gives its selection, and the review gate admits it. Shape only; the runner still checks it binds its exact approved job.
    /// </summary>
    public static bool Admits(ReviewedUpdateOperation? operation) =>
        operation is not null && operation.HasCurrentSchema && operation.RouteContract == UpdateRouteContract.WinGetUserSilentV1 &&
        operation.Selection.Provider == UpdateProvider.WinGet && operation.Selection.Scope == UpdateScope.User &&
        operation.Selection.Source is UpdateSource.WinGet or UpdateSource.MicrosoftStore &&
        operation.Selection.Architecture.Binding == ReviewedArchitectureBinding.Observed &&
        operation.Mode == ReviewedInstallerMode.Selected(InstallerMode.Silent) &&
        ReviewedUpdateReview.HasSupportedMode(operation);
}
