using System.Diagnostics.CodeAnalysis;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// One Desktop-role static profile observation over the current process token. Production is exactly
/// <see cref="ChocolateyDesktopObservation.ProductionObserve"/>; only the producers' test-only factories supply another.
/// Null means no token satisfied the §2.1 rules, so nothing was observed.
/// </summary>
internal delegate ChocolateyProfileResult? ChocolateyDesktopObserve(string targetId, ChocolateyNativeVersion frozenInstalled,
    TimeSpan remaining, CancellationToken cancellationToken);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §7 (N5) and §2.1: the shared, read-only Desktop-role observation used by
/// the desktop producers. It runs <see cref="ChocolateyProfileObserver.Observe"/> with
/// <see cref="ChocolateyObserverRole.Desktop"/> and no expected fingerprint over
/// <see cref="RequesterToken.TryFromCurrentProcess"/> (which refuses a token that fails the §2.1 rules). The Desktop role
/// reads protected files only: it never launches the client, never holds files and never elevates. The observation is
/// bounded by the observer's own <c>remaining</c> budget plus a real-time cap that also cancels it; any refusal, interrupt, cap, exception or
/// inconsistent observation yields no observation (fail closed).
/// </summary>
internal static class ChocolateyDesktopObservation
{
    /// <summary>The observer budget for one Desktop-role observation (well inside the two-minute dispatch-evidence freshness).</summary>
    internal static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);

    /// <summary>Slack after <see cref="Budget"/> before an observation that has not returned is abandoned.</summary>
    internal static readonly TimeSpan CapMargin = TimeSpan.FromSeconds(5);

    /// <summary>The only production observation (an architecture test pins both native calls to this method).</summary>
    internal static ChocolateyProfileResult? ProductionObserve(string targetId, ChocolateyNativeVersion frozenInstalled, TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        using RequesterToken? token = RequesterToken.TryFromCurrentProcess(out _);
        if (token is null) return null;
        return ChocolateyProfileObserver.Observe(token, ChocolateyObserverRole.Desktop, targetId, frozenInstalled, expectedFingerprint: null,
            remaining, cancellationToken);
    }

    /// <summary>
    /// ADR-031 design §3.6/§7.1 F′ and verification: the Desktop-role client-mode observation (target <c>chocolatey</c>, the
    /// release selected by engine digest from the compiled list), with the same token rules as <see cref="ProductionObserve"/>.
    /// Shaped as a <see cref="ChocolateyDesktopObserve"/>: any other target observes nothing. Reads only; never launches.
    /// </summary>
    internal static ChocolateyProfileResult? ProductionObserveClient(string targetId, ChocolateyNativeVersion expectedInstalled, TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(targetId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal)) return null;
        using RequesterToken? token = RequesterToken.TryFromCurrentProcess(out _);
        if (token is null) return null;
        return ChocolateyProfileObserver.ObserveClient(token, ChocolateyObserverRole.Desktop, expectedInstalled, expectedFingerprint: null,
            remaining, cancellationToken);
    }

    /// <summary>
    /// ADR-031 design §3.3/§3.5: the frozen client self-update's source and target releases from <paramref name="releases"/> —
    /// the listed release at the frozen installed version and that list's offer for it, which must be the frozen candidate,
    /// over the exact route shape (<see cref="ChocolateyClientUpdateArguments.IsUpgradeShape"/>). False for anything else,
    /// including every operation while the compiled list holds a single release.
    /// </summary>
    internal static bool TryGetClientRouteTarget(ReviewedUpdateOperation? operation, ChocolateyReleaseList releases,
        [NotNullWhen(true)] out ChocolateyReviewedRelease? source, [NotNullWhen(true)] out ChocolateyReviewedRelease? target)
    {
        ArgumentNullException.ThrowIfNull(releases);
        source = null;
        target = null;
        if (operation is null) return false;
        ChocolateyReviewedRelease? installed = releases.FindByVersion(operation.Selection.InstalledVersion);
        ChocolateyReviewedRelease? offer = releases.OfferTarget(installed);
        if (installed is null || offer is null) return false;
        if (!ChocolateyClientUpdateArguments.IsUpgradeShape(operation, installed, offer)) return false;
        source = installed;
        target = offer;
        return true;
    }

    /// <summary>
    /// The frozen operation's fixed route shape (the predicates of <see cref="ChocolateyReviewedUpdateArguments.Build"/>), its
    /// package ID, its frozen installed version and its reviewed candidate. False for anything else.
    /// </summary>
    internal static bool TryGetRouteTarget(ReviewedUpdateOperation? operation, [NotNullWhen(true)] out string? targetId,
        [NotNullWhen(true)] out ChocolateyNativeVersion? installed, [NotNullWhen(true)] out ChocolateyNativeVersion? candidate)
    {
        targetId = null;
        installed = null;
        candidate = null;
        if (operation is null) return false;
        try { _ = ChocolateyReviewedUpdateArguments.Build(operation); }
        catch (ArgumentException) { return false; }
        if (!TryNormalized(operation.Selection.InstalledVersion, out installed) || !TryNormalized(operation.Selection.CandidateVersion, out candidate))
            return false;
        targetId = operation.Selection.PackageId;
        return true;
    }

    /// <summary>A supported native version whose normalized form is exactly <paramref name="text"/>.</summary>
    internal static bool TryNormalized(string? text, [NotNullWhen(true)] out ChocolateyNativeVersion? version)
    {
        version = null;
        if (text is null || !ChocolateyNativeVersion.TryParse(text, out ChocolateyNativeVersion? parsed) || parsed is null ||
            !string.Equals(parsed.Normalized, text, StringComparison.Ordinal))
            return false;
        version = parsed;
        return true;
    }

    /// <summary>
    /// Runs one observation off the caller's thread and returns it only when it is <c>Observed</c> and consistent with the
    /// request (<see cref="IsConsistent"/>). Refused, TimedOut, Cancelled, a token refusal, an exception, a caller cancellation
    /// or the real-time cap give null; an observation that arrives after the cap is disposed unused.
    /// </summary>
    internal static async Task<ChocolateyProfileObservation?> TryObserveAsync(ChocolateyDesktopObserve observe, string targetId,
        ChocolateyNativeVersion frozenInstalled, TimeSpan budget, TimeProvider time, CancellationToken cancellationToken) =>
        (await ObserveOutcomeAsync(observe, targetId, frozenInstalled, budget, time, cancellationToken).ConfigureAwait(false)).Observation;

    /// <summary>
    /// <see cref="TryObserveAsync"/> that also reports whether the observer settled as <c>Refused/InstalledMismatch</c> (the
    /// static reads completed but the installed registration is not <paramref name="frozenInstalled"/>), so the verifier can
    /// say "target version was not detected" rather than "client could not be checked" (N5 review L1). The flag is set only
    /// for that settled refusal without a caller cancellation; it is never evidence of anything.
    /// <para>The observer runs under a token linked to the caller's that is also cancelled at the real-time cap
    /// (<paramref name="budget"/> + <see cref="CapMargin"/>), so an abandoned observation is told to stop and release its
    /// handles (N5 review L2). The linked source is disposed only once the observation has returned.</para>
    /// </summary>
    internal static async Task<ChocolateyDesktopOutcome> ObserveOutcomeAsync(ChocolateyDesktopObserve observe, string targetId,
        ChocolateyNativeVersion frozenInstalled, TimeSpan budget, TimeProvider time, CancellationToken cancellationToken)
    {
        ChocolateyProfileResult? result = (await RunCappedAsync(observe, targetId, frozenInstalled, budget, time, cancellationToken)
            .ConfigureAwait(false)).Result;
        if (result is ChocolateyProfileResult.Refused { Reason: ChocolateyProfileRefusal.InstalledMismatch })
            return new(null, InstalledMismatch: !cancellationToken.IsCancellationRequested);
        if (result is not ChocolateyProfileResult.Observed observed) return default;
        ChocolateyProfileObservation observation = observed.Observation;
        if (cancellationToken.IsCancellationRequested || !IsConsistent(observation, targetId, frozenInstalled))
        {
            observation.Dispose();
            return default;
        }
        return new(observation, InstalledMismatch: false);
    }

    /// <summary>
    /// The bounded run shared by <see cref="ObserveOutcomeAsync"/> and the installed-client display check: one observation off
    /// the caller's thread under a token linked to the caller's that is also cancelled at the real-time cap
    /// (<paramref name="budget"/> + <see cref="CapMargin"/>). The raw result is returned (an <c>Observed</c> one is owned by the
    /// caller); a caller cancellation, an invalid budget or an observer exception gives no result, and an observation abandoned
    /// at the cap gives no result with <see cref="ChocolateyCappedResult.CapReached"/> set (it is disposed when it returns).
    /// </summary>
    private static async Task<ChocolateyCappedResult> RunCappedAsync(ChocolateyDesktopObserve observe, string targetId,
        ChocolateyNativeVersion frozenInstalled, TimeSpan budget, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observe);
        ArgumentNullException.ThrowIfNull(time);
        if (cancellationToken.IsCancellationRequested || budget <= TimeSpan.Zero || budget > Budget) return default;
        CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<ChocolateyProfileResult?> work;
        try
        {
            limit.CancelAfter(budget + CapMargin);
            CancellationToken bounded = limit.Token;
            work = Task.Run(() => observe(targetId, frozenInstalled, budget, bounded), CancellationToken.None);
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
            return new(await work.WaitAsync(budget + CapMargin, time, cancellationToken).ConfigureAwait(false), CapReached: false);
        }
        catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
        {
            DisposeWhenDone(work);
            return new(null, CapReached: !cancellationToken.IsCancellationRequested);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return default;
        }
    }

    /// <summary>
    /// FR-UPD-15, FR-UI-03/04; ADR-031 ("offered only from a listed, currently trusted client"), design §8.6 gate 3: the
    /// production display check of the installed client: <see cref="ProductionObserveClient"/> (Desktop role, client mode,
    /// current process token, no expected fingerprint) over <see cref="ChocolateyReleaseList.Listed"/> within <see cref="Budget"/>.
    /// Read-only and non-elevated: it never launches the client, holds no file and starts nothing.
    /// </summary>
    internal static Task<ChocolateyInstalledClientVerification> ObserveInstalledClientAsync(CancellationToken cancellationToken) =>
        ObserveInstalledClientAsync(ProductionObserveClient, ChocolateyReleaseList.Listed, Budget, TimeProvider.System, cancellationToken);

    /// <summary>
    /// ADR-031 design §8.6 gate 3: which listed release the installed client verifiably is, for display. In client mode the
    /// observer selects the listed release by engine digest, requires every release file to match it and the <c>chocolatey</c>
    /// registration to be at that release's version, and then requires that version to equal the expected one (step 8a). So:
    /// <list type="number">
    /// <item>The newest listed release is observed first (no scan data is used). A consistent <c>Observed</c> result whose
    /// matched release is exactly that listed release is <see cref="ChocolateyInstalledClientVerification.Verified"/>.</item>
    /// <item>Only a settled <c>Refused/InstalledMismatch</c> naming another listed release leads to one second observation at that
    /// release; its value only chooses what to observe and is never shown. The second result is judged by rule 1 alone.</item>
    /// <item>Every other refusal (and a second mismatch) is <c>Refused</c>; an exhausted budget, an observer interrupt or the cap
    /// is <c>TimedOut</c>; a caller cancellation is <c>Cancelled</c>; a token refusal, an exception or an empty list is
    /// <c>Unavailable</c>.</item>
    /// </list>
    /// Both observations together stay within <paramref name="budget"/> (plus one <see cref="CapMargin"/>). Every observation is
    /// disposed here; only the release's normalized version leaves this method.
    /// </summary>
    internal static async Task<ChocolateyInstalledClientVerification> ObserveInstalledClientAsync(ChocolateyDesktopObserve observeClient,
        ChocolateyReleaseList releases, TimeSpan budget, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observeClient);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(time);
        if (cancellationToken.IsCancellationRequested) return ChocolateyInstalledClientVerification.Cancelled;
        if (budget <= TimeSpan.Zero || budget > Budget || releases.Releases.Count == 0) return ChocolateyInstalledClientVerification.Unavailable;
        long started = time.GetTimestamp();
        ChocolateyReviewedRelease expected = releases.Releases[^1];
        for (int attempt = 0; attempt < 2; attempt++)
        {
            TimeSpan remaining = budget - time.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return ChocolateyInstalledClientVerification.TimedOut;
            ChocolateyCappedResult capped = await RunCappedAsync(observeClient, ReviewedUpdateOperation.ChocolateyClientPackageId,
                expected.NativeVersion, remaining, time, cancellationToken).ConfigureAwait(false);
            if (capped.Result is ChocolateyProfileResult.Observed observed)
            {
                using ChocolateyProfileObservation observation = observed.Observation;
                if (cancellationToken.IsCancellationRequested) return ChocolateyInstalledClientVerification.Cancelled;
                return releases.Contains(expected) && ReferenceEquals(observation.Release, expected) &&
                    IsConsistent(observation, ReviewedUpdateOperation.ChocolateyClientPackageId, expected.NativeVersion)
                        ? ChocolateyInstalledClientVerification.Verified(expected.Version)
                        : ChocolateyInstalledClientVerification.Refused;
            }
            if (cancellationToken.IsCancellationRequested) return ChocolateyInstalledClientVerification.Cancelled;
            switch (capped.Result)
            {
                case ChocolateyProfileResult.Refused { Reason: ChocolateyProfileRefusal.InstalledMismatch, ObservedInstalled: { } other } when attempt == 0:
                    ChocolateyReviewedRelease? listed = releases.FindByVersion(other);
                    if (listed is null || ReferenceEquals(listed, expected)) return ChocolateyInstalledClientVerification.Refused;
                    expected = listed;
                    continue;
                case ChocolateyProfileResult.Refused:
                    return ChocolateyInstalledClientVerification.Refused;
                // The caller did not cancel, so an observer interrupt is the budget or the cap.
                case ChocolateyProfileResult.TimedOut or ChocolateyProfileResult.Cancelled:
                    return ChocolateyInstalledClientVerification.TimedOut;
                default:
                    return capped.CapReached ? ChocolateyInstalledClientVerification.TimedOut : ChocolateyInstalledClientVerification.Unavailable;
            }
        }
        return ChocolateyInstalledClientVerification.Refused;
    }

    /// <summary>
    /// Defence in depth over what the observer already guarantees: a Desktop observation holds nothing, names the requested
    /// target at the requested installed version, and its fingerprint is the 64-hex digest of its own common data.
    /// </summary>
    internal static bool IsConsistent(ChocolateyProfileObservation observation, string targetId, ChocolateyNativeVersion installed)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.HeldClient is not null ||
            !string.Equals(observation.Common.TargetId, targetId, StringComparison.Ordinal) ||
            !string.Equals(observation.InstalledVersion.Normalized, installed.Normalized, StringComparison.Ordinal) ||
            !string.Equals(observation.Common.InstalledVersion.Normalized, installed.Normalized, StringComparison.Ordinal) ||
            !ReviewedUpdateOperation.TryGetProfileEvidenceDigest(ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint, out _))
            return false;
        try { return string.Equals(ChocolateyCommonProfileFingerprint.Compute(observation.Common), observation.Fingerprint, StringComparison.Ordinal); }
        catch (ArgumentException) { return false; }
    }

    private static void DisposeWhenDone(Task<ChocolateyProfileResult?> work) =>
        _ = work.ContinueWith(static completed =>
        {
            if (completed.IsCompletedSuccessfully && completed.Result is ChocolateyProfileResult.Observed late) late.Observation.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}

/// <summary>
/// One Desktop-role observation outcome: the consistent observation (owned by the caller), or none; <see cref="InstalledMismatch"/>
/// is true only when the observer settled as <c>Refused/InstalledMismatch</c> (then <see cref="Observation"/> is null).
/// </summary>
internal readonly record struct ChocolateyDesktopOutcome(ChocolateyProfileObservation? Observation, bool InstalledMismatch);

/// <summary>One bounded observation's raw result (an <c>Observed</c> one owned by the caller), and whether it was abandoned at the cap.</summary>
internal readonly record struct ChocolateyCappedResult(ChocolateyProfileResult? Result, bool CapReached);

/// <summary>The F′ observation's file-first baseline (Task 90 M2), kept for the post-update check of the same operation.</summary>
/// <remarks>ADR-031 design §8.3: for the client self-update, <see cref="SourceRelease"/> is the F′ observation's listed release.</remarks>
internal sealed record ChocolateyDispatchBaseline(string TargetId, string RequestingUserSid, ChocolateyClientStateData ClientState,
    ChocolateyReviewedRelease? SourceRelease = null);

/// <summary>
/// FR-UPD-15; design rev 6 §7: hands the F′ observation's <see cref="ChocolateyClientStateData"/> from the evidence source to
/// the verifier of the same frozen operation (keyed by its canonical digest). In memory only, never persisted, bounded,
/// latest-wins and one-use: the verifier takes the entry, so a baseline can serve only one verification. A missing baseline
/// makes the verification unverifiable, never Success.
/// </summary>
internal sealed class ChocolateyDispatchBaselines
{
    internal const int Capacity = 8;
    private readonly Lock gate = new();
    private readonly List<(string Digest, ChocolateyDispatchBaseline Baseline)> entries = [];

    internal void Record(ReviewedUpdateOperation operation, ChocolateyDispatchBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(baseline);
        lock (gate)
        {
            entries.RemoveAll(entry => string.Equals(entry.Digest, operation.Digest, StringComparison.Ordinal));
            entries.Add((operation.Digest, baseline));
            while (entries.Count > Capacity) entries.RemoveAt(0);
        }
    }

    internal bool TryTake(ReviewedUpdateOperation operation, [NotNullWhen(true)] out ChocolateyDispatchBaseline? baseline)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            int index = entries.FindIndex(entry => string.Equals(entry.Digest, operation.Digest, StringComparison.Ordinal));
            if (index < 0)
            {
                baseline = null;
                return false;
            }
            baseline = entries[index].Baseline;
            entries.RemoveAt(index);
            return true;
        }
    }
}
