using PCSteward.Application;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>Closed verdict of the client self-update postcheck (ADR-031 design §3.6, §3a).</summary>
internal enum ChocolateyClientUpdateVerdict
{
    /// <summary>Every client file matches the target release, which is the registered client; no leftover.</summary>
    Verified,
    /// <summary>As <see cref="Verified"/>, plus the one reviewed leftover whose bytes are the job's source release (disclosed).</summary>
    VerifiedWithLeftover,
    /// <summary>The files or registration are not exactly the target release (including a target never installed).</summary>
    NotTargetRelease,
    /// <summary>The only failure is the reviewed leftover name with bytes of no listed release (named; never deleted).</summary>
    UnlistedLeftover,
    /// <summary>The check did not finish (timed out or cancelled).</summary>
    NotFinished,
    /// <summary>
    /// As <see cref="Verified"/> otherwise, but the reviewed leftover's bytes are a listed release other than the job's source
    /// (named in the message; never deleted).
    /// </summary>
    LeftoverNotSource,
    /// <summary>
    /// Design §2 R3 (LOW-2): the resumed upgrade's release of the binding's holds failed part-way, so the run did not happen
    /// under the reviewed conditions; never verified, whatever the re-observation shows.
    /// </summary>
    ReleaseIncomplete,
    /// <summary>Design §2.1 R5: the runtime watchdog terminated the job because a setup override name appeared while it ran.</summary>
    SetupOverrideDuringRun,
    /// <summary>Design §2.1 R5: the runtime watchdog terminated the job because the requester could modify the root while it ran.</summary>
    RootWritableDuringRun,
    /// <summary>Design §2.1 R5: the runtime watchdog terminated the job because it could not keep watching (fail closed).</summary>
    WatchFailedDuringRun,
}

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/13, AC-UPD-07; ADR-031 design mapping §3.6 and §3a: the pure postcheck rule for a client
/// self-update, over a Desktop-role client observation made with the target version as the expected installed version.
/// Verified only when the observation matched exactly the target release of the same list (complete manifest and engine trust),
/// the unique <c>chocolatey</c> registration is the target version, its archive equals the reviewed asset digest, and the
/// requester is the job's; a leftover is accepted only when its observed bytes are the job's <b>source</b> release's
/// <c>Chocolatey.PowerShell.dll</c> (compared directly, so two listed releases sharing that file cannot cause a false
/// mismatch). <c>watchdogFault</c> (the binding's <c>WatchdogFault</c>, design §2.1 R5) forces the matching watchdog verdict
/// first, whatever the re-observation shows (the run was terminated and Chocolatey may be partly updated);
/// <c>holdReleaseIncomplete</c> (the binding's <c>ReleaseIncomplete</c>) then forces
/// <see cref="ChocolateyClientUpdateVerdict.ReleaseIncomplete"/>. Everything else is not verified. A restart-pending
/// result never exists here: <see cref="ToResult"/> always reports it false.
/// <para>
/// Independent review L3: <c>src</c> callers must use <see cref="Evaluate"/> over the client self-update binding, which
/// reads the source release (<c>InstalledRelease</c>), <c>WatchdogFault</c> and <c>ReleaseIncomplete</c> from the binding
/// itself, so no caller can pass a watchdog fault or a release state that is not the job's. The free-parameter
/// <see cref="EvaluateForTests"/> has no <c>src</c> caller (architecture-banned).
/// </para>
/// </summary>
internal static class ChocolateyClientUpdatePostcheck
{
    /// <summary>
    /// The postcheck for the client self-update job of <paramref name="client"/>: its matched installed release is the
    /// source, and its recorded watchdog fault and incomplete hold release are applied first. Throws for a package binding.
    /// </summary>
    internal static ChocolateyClientUpdateVerdict Evaluate(ChocolateyProfileResult result, ChocolateyHeldClientBinding client,
        ChocolateyReviewedRelease target, string requestingUserSid)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (client.Purpose != ChocolateyBindingPurpose.ClientSelfUpdate || client.InstalledRelease is not { } source)
            throw new ArgumentException("Only a client self-update binding has a client postcheck.", nameof(client));
        return EvaluateCore(result, source, target, requestingUserSid, client.WatchdogFault, client.ReleaseIncomplete);
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the rule over free parameters.</summary>
    internal static ChocolateyClientUpdateVerdict EvaluateForTests(ChocolateyProfileResult result, ChocolateyReviewedRelease source,
        ChocolateyReviewedRelease target, string requestingUserSid, ChocolateyClientWatchdogFault watchdogFault, bool holdReleaseIncomplete) =>
        EvaluateCore(result, source, target, requestingUserSid, watchdogFault, holdReleaseIncomplete);

    private static ChocolateyClientUpdateVerdict EvaluateCore(ChocolateyProfileResult result, ChocolateyReviewedRelease source,
        ChocolateyReviewedRelease target, string requestingUserSid, ChocolateyClientWatchdogFault watchdogFault, bool holdReleaseIncomplete)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(requestingUserSid);
        switch (watchdogFault)
        {
            case ChocolateyClientWatchdogFault.None: break;
            case ChocolateyClientWatchdogFault.SetupOverrideDuringRun: return ChocolateyClientUpdateVerdict.SetupOverrideDuringRun;
            case ChocolateyClientWatchdogFault.RootWritableDuringRun: return ChocolateyClientUpdateVerdict.RootWritableDuringRun;
            default: return ChocolateyClientUpdateVerdict.WatchFailedDuringRun;
        }
        if (holdReleaseIncomplete) return ChocolateyClientUpdateVerdict.ReleaseIncomplete;
        switch (result)
        {
            case ChocolateyProfileResult.TimedOut or ChocolateyProfileResult.Cancelled:
                return ChocolateyClientUpdateVerdict.NotFinished;
            case ChocolateyProfileResult.Refused refused:
                return refused.Detail == ChocolateyProfileRefusalDetail.UnlistedReleaseLeftover
                    ? ChocolateyClientUpdateVerdict.UnlistedLeftover : ChocolateyClientUpdateVerdict.NotTargetRelease;
            case ChocolateyProfileResult.Observed { Observation: var observation }:
                bool sameList = ReferenceEquals(source.List, target.List) && target.List.Contains(target) && target.List.Contains(source) &&
                    ChocolateyReleaseList.IsNewer(target, source);
                bool exact = sameList && ReferenceEquals(observation.Release, target) &&
                    string.Equals(observation.InstalledVersion.Normalized, target.Version, StringComparison.Ordinal) &&
                    string.Equals(observation.Common.TargetId, PCSteward.Domain.ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal) &&
                    string.Equals(observation.Common.ArchiveSha256, target.AssetSha256, StringComparison.Ordinal) &&
                    string.Equals(observation.Common.ReleaseAssetSha256, target.AssetSha256, StringComparison.Ordinal) &&
                    string.Equals(observation.ClientState.EngineSha256, target.EngineSha256, StringComparison.Ordinal) &&
                    observation.ClientState.LicensedExtensionRegistrationDigest is null &&
                    string.Equals(observation.Common.RequestingUserSid, requestingUserSid, StringComparison.Ordinal);
                if (!exact) return ChocolateyClientUpdateVerdict.NotTargetRelease;
                return observation.ReleaseResidue switch
                {
                    null => ChocolateyClientUpdateVerdict.Verified,
                    { } residue when residue.IsModuleOf(source) => ChocolateyClientUpdateVerdict.VerifiedWithLeftover,
                    { } residue when target.List.Contains(residue.MatchedRelease) && residue.IsModuleOf(residue.MatchedRelease) =>
                        ChocolateyClientUpdateVerdict.LeftoverNotSource,
                    _ => ChocolateyClientUpdateVerdict.NotTargetRelease,
                };
            default:
                return ChocolateyClientUpdateVerdict.NotTargetRelease;
        }
    }

    /// <summary>
    /// The workflow result: Success only for the two verified verdicts, and the reviewed leftover is carried as a distinct
    /// note so Verify and history disclose it; never restart-pending.
    /// </summary>
    internal static PostUpdateInventoryResult ToResult(ChocolateyClientUpdateVerdict verdict) => verdict switch
    {
        ChocolateyClientUpdateVerdict.Verified => new PostUpdateInventoryResult(true, false),
        ChocolateyClientUpdateVerdict.VerifiedWithLeftover =>
            new PostUpdateInventoryResult(true, false, PostUpdateVerificationDetail.None, PostUpdateVerificationNote.ClientReviewedLeftover),
        ChocolateyClientUpdateVerdict.UnlistedLeftover => new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientLeftoverNotListed),
        ChocolateyClientUpdateVerdict.LeftoverNotSource => new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientLeftoverNotSource),
        ChocolateyClientUpdateVerdict.ReleaseIncomplete => new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientReleaseIncomplete),
        ChocolateyClientUpdateVerdict.SetupOverrideDuringRun =>
            new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientSetupOverrideDuringRun),
        ChocolateyClientUpdateVerdict.RootWritableDuringRun =>
            new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientRootWritableDuringRun),
        ChocolateyClientUpdateVerdict.WatchFailedDuringRun =>
            new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientWatchFailedDuringRun),
        ChocolateyClientUpdateVerdict.NotTargetRelease => new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientChanged),
        _ => new PostUpdateInventoryResult(false, false, PostUpdateVerificationDetail.ClientUnverifiable),
    };
}
