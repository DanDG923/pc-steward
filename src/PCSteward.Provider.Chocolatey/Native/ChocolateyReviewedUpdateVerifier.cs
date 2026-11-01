using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13, AC-UPD-07; Task 50 design rev 6 §7 (N5), Task 90 M2 and fact extension rev 3 §4.3
/// (L5): the post-update verifier for <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>.
/// <list type="number">
/// <item>It takes the F′ observation's client-state baseline for the same operation (one-use); none means unverifiable.</item>
/// <item><see cref="ChocolateyPostUpdateClientCheck.RunAsync"/> runs first with a file-only capture: a Desktop-role
/// observation (protected reads only, the client is never run) for the target at the expected version, whose client state
/// is compared with the baseline.</item>
/// <item>Only <see cref="ChocolateyClientComparison.Unchanged"/> proceeds to the residue-aware registration re-observation:
/// a second, independent Desktop-role observation that must find the unique exact target at the expected version (the
/// observer refuses duplicate targets, pending markers and <c>lib-bkp</c> residue), for the same requester, with the client
/// state still equal to the baseline.</item>
/// </list>
/// Success (<c>ExpectedVersionDetected</c>) only when that re-observation proves the expected version.
/// <c>ClientChanged</c> and <c>Unverifiable</c> carry their detail and are Unverified, never Success. <c>RestartPending</c>
/// is always false: this route never infers a restart (L5). Cancellation propagates; the workflow records Unverified.
/// </summary>
internal sealed class ChocolateyReviewedUpdateVerifier : IReviewedUpdateVerifier
{
    private readonly ChocolateyDesktopObserve observe;
    private readonly ChocolateyDispatchBaselines baselines;
    private readonly TimeProvider time;

    internal ChocolateyReviewedUpdateVerifier(ChocolateyDispatchBaselines baselines)
        : this(ChocolateyDesktopObservation.ProductionObserve, baselines, TimeProvider.System)
    {
    }

    private ChocolateyReviewedUpdateVerifier(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines, TimeProvider time)
    {
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        this.baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the verifier over a supplied observation.</summary>
    internal static ChocolateyReviewedUpdateVerifier CreateForTests(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines,
        TimeProvider time) => new(observe, baselines, time);

    public async Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation is null || ownership is null || !ownership.CanDispatchFor(operation) ||
            !ChocolateyDesktopObservation.TryGetRouteTarget(operation, out string? targetId, out _, out ChocolateyNativeVersion? expected) ||
            !baselines.TryTake(operation, out ChocolateyDispatchBaseline? baseline) ||
            !string.Equals(baseline.TargetId, targetId, StringComparison.Ordinal) ||
            !string.Equals(baseline.RequestingUserSid, operation.RequestingUserSid, StringComparison.Ordinal))
            return Unverified(PostUpdateVerificationDetail.ClientUnverifiable);

        ChocolateyPostClientResult<bool> result = await ChocolateyPostUpdateClientCheck.RunAsync(baseline.ClientState,
            token => CaptureFilesAsync(targetId, expected, baseline, token),
            token => ReobserveTargetAsync(targetId, expected, baseline, token),
            cancellationToken).ConfigureAwait(false);
        return result.Comparison switch
        {
            ChocolateyClientComparison.Unchanged => result.Value ? new PostUpdateInventoryResult(true, false) : Unverified(PostUpdateVerificationDetail.None),
            ChocolateyClientComparison.ClientChanged => Unverified(PostUpdateVerificationDetail.ClientChanged),
            _ => Unverified(PostUpdateVerificationDetail.ClientUnverifiable),
        };
    }

    /// <summary>Not Success and never restart-pending (L5).</summary>
    private static PostUpdateInventoryResult Unverified(PostUpdateVerificationDetail detail) => new(false, false, detail);

    /// <summary>The file-only capture: the observation's client state, or null (Unverifiable) when it cannot be read.</summary>
    private async Task<ChocolateyClientStateData?> CaptureFilesAsync(string targetId, ChocolateyNativeVersion expected,
        ChocolateyDispatchBaseline baseline, CancellationToken cancellationToken)
    {
        using ChocolateyProfileObservation? capture =
            await ChocolateyDesktopObservation.TryObserveAsync(observe, targetId, expected, time, cancellationToken).ConfigureAwait(false);
        if (capture is null || !string.Equals(capture.Common.RequestingUserSid, baseline.RequestingUserSid, StringComparison.Ordinal)) return null;
        return capture.ClientState;
    }

    /// <summary>Reached only after an Unchanged comparison: the unique exact target at the expected version, client unchanged.</summary>
    private async Task<bool> ReobserveTargetAsync(string targetId, ChocolateyNativeVersion expected, ChocolateyDispatchBaseline baseline,
        CancellationToken cancellationToken)
    {
        using ChocolateyProfileObservation? reobserved =
            await ChocolateyDesktopObservation.TryObserveAsync(observe, targetId, expected, time, cancellationToken).ConfigureAwait(false);
        return reobserved is not null && !cancellationToken.IsCancellationRequested &&
            string.Equals(reobserved.Common.RequestingUserSid, baseline.RequestingUserSid, StringComparison.Ordinal) &&
            ChocolateyPostUpdateClientCheck.Compare(baseline.ClientState, reobserved.ClientState) == ChocolateyClientComparison.Unchanged;
    }
}
