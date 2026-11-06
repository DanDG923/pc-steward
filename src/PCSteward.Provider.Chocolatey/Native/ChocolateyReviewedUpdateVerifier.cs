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
/// <para>
/// ADR-031 design §8.3 (W1): <see cref="VerifyClientAsync"/> verifies <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/>
/// with the interpreter-only <see cref="ReviewedClientRunFault"/> of the accepted helper reply: a Desktop-role client-mode
/// observation at the target version, judged by <see cref="ChocolateyClientUpdatePostcheck"/>'s fact overload against the F′
/// baseline's source release. The fact is applied first, so it can only lower the result.
/// </para>
/// </summary>
internal sealed class ChocolateyReviewedUpdateVerifier : IReviewedUpdateVerifier, IReviewedClientUpdateVerifier
{
    private readonly ChocolateyDesktopObserve observe;
    private readonly ChocolateyDesktopObserve observeClient;
    private readonly ChocolateyReleaseList releases;
    private readonly ChocolateyDispatchBaselines baselines;
    private readonly TimeProvider time;
    private readonly TimeSpan budget;

    internal ChocolateyReviewedUpdateVerifier(ChocolateyDispatchBaselines baselines)
        : this(ChocolateyDesktopObservation.ProductionObserve, ChocolateyDesktopObservation.ProductionObserveClient, ChocolateyReleaseList.Listed,
            baselines, TimeProvider.System, ChocolateyDesktopObservation.Budget)
    {
    }

    private ChocolateyReviewedUpdateVerifier(ChocolateyDesktopObserve observe, ChocolateyDesktopObserve observeClient, ChocolateyReleaseList releases,
        ChocolateyDispatchBaselines baselines, TimeProvider time, TimeSpan budget)
    {
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        this.observeClient = observeClient ?? throw new ArgumentNullException(nameof(observeClient));
        this.releases = releases ?? throw new ArgumentNullException(nameof(releases));
        this.baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.budget = budget;
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the verifier over a supplied observation and budget.</summary>
    internal static ChocolateyReviewedUpdateVerifier CreateForTests(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines,
        TimeProvider time, TimeSpan budget) =>
        new(observe, ChocolateyDesktopObservation.ProductionObserveClient, ChocolateyReleaseList.Listed, baselines, time, budget);

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the client verifier over a supplied client-mode observation
    /// and a fixture release list.
    /// </summary>
    internal static ChocolateyReviewedUpdateVerifier CreateClientForTests(ChocolateyDesktopObserve observeClient, ChocolateyReleaseList releases,
        ChocolateyDispatchBaselines baselines, TimeProvider time, TimeSpan budget) =>
        new((_, _, _, _) => null, observeClient, releases, baselines, time, budget);

    /// <summary>
    /// ADR-031 design §8.3: the client self-update's verification. The operation must be the exact client shape whose source
    /// and target come from the reviewed list, with the F′ baseline (one-use) naming the same source release and requester;
    /// otherwise Unverifiable. The run fault is applied first by the postcheck's fact overload; then the Desktop-role
    /// client-mode observation at the target version must be the exact target release. Never restart-pending. A caller
    /// cancellation throws.
    /// </summary>
    public async Task<PostUpdateInventoryResult> VerifyClientAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        ReviewedClientRunFault runFault, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation is null || ownership is null || runFault is null || !ownership.CanDispatchFor(operation) ||
            !ChocolateyDesktopObservation.TryGetClientRouteTarget(operation, releases, out ChocolateyReviewedRelease? source,
                out ChocolateyReviewedRelease? target) ||
            !baselines.TryTake(operation, out ChocolateyDispatchBaseline? baseline) || !ReferenceEquals(baseline.SourceRelease, source) ||
            !string.Equals(baseline.TargetId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal) ||
            !string.Equals(baseline.RequestingUserSid, operation.RequestingUserSid, StringComparison.Ordinal))
            return Unverified(PostUpdateVerificationDetail.ClientUnverifiable);
        // A reported run fault decides without reading anything (the postcheck applies it before the observation).
        if (!runFault.IsNone)
            return ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdatePostcheck.Evaluate(null, runFault, source, target, operation.RequestingUserSid));
        ChocolateyProfileResult? result = await ObserveClientAsync(target.NativeVersion, cancellationToken).ConfigureAwait(false);
        try
        {
            return ChocolateyClientUpdatePostcheck.ToResult(
                ChocolateyClientUpdatePostcheck.Evaluate(result, runFault, source, target, operation.RequestingUserSid));
        }
        finally
        {
            if (result is ChocolateyProfileResult.Observed observed) observed.Observation.Dispose();
        }
    }

    /// <summary>
    /// One bounded client-mode observation (budget plus the real-time cap, as <see cref="ChocolateyDesktopObservation"/>); a cap,
    /// token refusal or exception gives null (not finished), a caller cancellation throws, and a late result is disposed.
    /// </summary>
    private async Task<ChocolateyProfileResult?> ObserveClientAsync(ChocolateyNativeVersion expected, CancellationToken cancellationToken)
    {
        if (budget <= TimeSpan.Zero || budget > ChocolateyDesktopObservation.Budget) return null;
        // The linked source is disposed only once the observation has returned (it may outlive the cap).
        CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ChocolateyProfileResult?> work;
        try
        {
            limit.CancelAfter(budget + ChocolateyDesktopObservation.CapMargin);
            CancellationToken bounded = limit.Token;
            work = Task.Run(() => observeClient(ReviewedUpdateOperation.ChocolateyClientPackageId, expected, budget, bounded), CancellationToken.None);
        }
        catch
        {
            limit.Dispose();
            throw;
        }
        _ = work.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), limit, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            ChocolateyProfileResult? result = await work.WaitAsync(budget + ChocolateyDesktopObservation.CapMargin, time, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception) when (exception is TimeoutException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            DisposeWhenDone(work);
            return null;
        }
        catch (OperationCanceledException)
        {
            DisposeWhenDone(work);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static void DisposeWhenDone(Task<ChocolateyProfileResult?> work) =>
        _ = work.ContinueWith(static completed =>
        {
            if (completed.IsCompletedSuccessfully && completed.Result is ChocolateyProfileResult.Observed late) late.Observation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

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
