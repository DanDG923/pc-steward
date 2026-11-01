using System.Runtime.CompilerServices;
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
/// <c>ClientChanged</c> and <c>Unverifiable</c> carry their detail and are Unverified, never Success. A file-only capture the
/// observer refused as <c>InstalledMismatch</c> (the static reads completed but the registration is not the expected version,
/// for example an exit-0 update that changed nothing) is Unverified with detail <c>None</c>, so the workflow says the target
/// version was not detected (N5 review L1); every other capture refusal stays <c>ClientUnverifiable</c>. <c>RestartPending</c>
/// is always false: this route never infers a restart (L5). A caller cancellation during either observation throws
/// <see cref="OperationCanceledException"/> (N5 review L3); the workflow records "verification did not finish", Unverified.
/// </summary>
internal sealed class ChocolateyReviewedUpdateVerifier : IReviewedUpdateVerifier
{
    private readonly ChocolateyDesktopObserve observe;
    private readonly ChocolateyDispatchBaselines baselines;
    private readonly TimeProvider time;
    private readonly TimeSpan budget;

    internal ChocolateyReviewedUpdateVerifier(ChocolateyDispatchBaselines baselines)
        : this(ChocolateyDesktopObservation.ProductionObserve, baselines, TimeProvider.System, ChocolateyDesktopObservation.Budget)
    {
    }

    private ChocolateyReviewedUpdateVerifier(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines, TimeProvider time,
        TimeSpan budget)
    {
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        this.baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.budget = budget;
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the verifier over a supplied observation and budget.</summary>
    internal static ChocolateyReviewedUpdateVerifier CreateForTests(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines,
        TimeProvider time, TimeSpan budget) => new(observe, baselines, time, budget);

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

        StrongBox<bool> installedMismatch = new(false);
        ChocolateyPostClientResult<bool> result = await ChocolateyPostUpdateClientCheck.RunAsync(baseline.ClientState,
            token => CaptureFilesAsync(targetId, expected, baseline, installedMismatch, token),
            token => ReobserveTargetAsync(targetId, expected, baseline, token),
            cancellationToken).ConfigureAwait(false);
        return result.Comparison switch
        {
            ChocolateyClientComparison.Unchanged => result.Value ? new PostUpdateInventoryResult(true, false) : Unverified(PostUpdateVerificationDetail.None),
            ChocolateyClientComparison.ClientChanged => Unverified(PostUpdateVerificationDetail.ClientChanged),
            _ when installedMismatch.Value => Unverified(PostUpdateVerificationDetail.None),
            _ => Unverified(PostUpdateVerificationDetail.ClientUnverifiable),
        };
    }

    /// <summary>Not Success and never restart-pending (L5).</summary>
    private static PostUpdateInventoryResult Unverified(PostUpdateVerificationDetail detail) => new(false, false, detail);

    /// <summary>
    /// The file-only capture: the observation's client state, or null (Unverifiable) when it cannot be read.
    /// <paramref name="installedMismatch"/> records an <c>InstalledMismatch</c> refusal (the result is then null).
    /// </summary>
    private async Task<ChocolateyClientStateData?> CaptureFilesAsync(string targetId, ChocolateyNativeVersion expected,
        ChocolateyDispatchBaseline baseline, StrongBox<bool> installedMismatch, CancellationToken cancellationToken)
    {
        ChocolateyDesktopOutcome outcome = await ChocolateyDesktopObservation
            .ObserveOutcomeAsync(observe, targetId, expected, budget, time, cancellationToken).ConfigureAwait(false);
        installedMismatch.Value = outcome.InstalledMismatch;
        using ChocolateyProfileObservation? capture = outcome.Observation;
        if (capture is null || !string.Equals(capture.Common.RequestingUserSid, baseline.RequestingUserSid, StringComparison.Ordinal)) return null;
        return capture.ClientState;
    }

    /// <summary>
    /// Reached only after an Unchanged comparison: the unique exact target at the expected version, client unchanged. A caller
    /// cancellation throws rather than reading as "not detected" (L3).
    /// </summary>
    private async Task<bool> ReobserveTargetAsync(string targetId, ChocolateyNativeVersion expected, ChocolateyDispatchBaseline baseline,
        CancellationToken cancellationToken)
    {
        using ChocolateyProfileObservation? reobserved = await ChocolateyDesktopObservation
            .TryObserveAsync(observe, targetId, expected, budget, time, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return reobserved is not null &&
            string.Equals(reobserved.Common.RequestingUserSid, baseline.RequestingUserSid, StringComparison.Ordinal) &&
            ChocolateyPostUpdateClientCheck.Compare(baseline.ClientState, reobserved.ClientState) == ChocolateyClientComparison.Unchanged;
    }
}
