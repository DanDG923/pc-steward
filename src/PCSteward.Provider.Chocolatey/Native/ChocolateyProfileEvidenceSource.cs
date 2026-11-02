using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// The desktop review intent (design rev 6 §7; Profile V2 "Native producer"): the fresh Desktop-role observation of the
/// target plus a <b>supplied</b> structured candidate (the non-elevated client's <c>outdated</c> record, resolved by the
/// caller), which this producer validates but never captures or re-runs. It is provisional
/// <see cref="ReviewedCandidateProvenance.UserCacheIntent"/> with the fixed Profile V2 disclosure. Review data only: it is
/// not helper qualification, not trusted candidate evidence and not a dispatch permit; the helper confirms the exact
/// candidate itself at stage C before updating.
/// <see cref="IntentResolvedAt"/> is when the supplied candidate was resolved; <see cref="ObservedAt"/> is when the
/// observation started. They are kept separate.
/// </summary>
internal sealed record ChocolateyReviewIntent(string PackageId, string InstalledVersion, string CandidateVersion, string RequestingUserSid,
    string ProfileEvidenceId, ReviewedCandidateProvenance CandidateProvenance, bool TargetLibBadResidue, string Disclosure, DateTimeOffset ObservedAt,
    DateTimeOffset IntentResolvedAt);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §7 (F′, N5) and adapter gates §A.6.1 (A2-L6): the dispatch
/// evidence source for <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>.
/// <list type="bullet">
/// <item><see cref="ObserveAsync"/> requires the fixed route shape and an ownership that may dispatch the operation, then
/// runs the Desktop-role observation (<see cref="ChocolateyDesktopObservation"/>) for the operation's package ID and frozen
/// installed version with no expected fingerprint. The evidence identity is <c>choco-profile-v2:</c> plus the
/// <b>fresh</b> observation's fingerprint (F′), never a value copied from the frozen operation, and the requesting SID is
/// the fresh token's. Any refusal, timeout, cancellation or token refusal returns null, so nothing is dispatched. The
/// observation's client-state baseline is recorded for the verifier of the same operation.</item>
/// <item><see cref="ObserveReviewIntentAsync"/> is the same observation for a supplied candidate intent that the
/// non-elevated client already reported; it validates that intent and its freshness and never runs <c>outdated</c> or any
/// other process: the Desktop role has no held client binding, and the sealed N3 commands run only through the helper item
/// (design rev 6 §3, §4).</item>
/// <item>Each producer timestamps its observation when the observation starts (N5 review L4), so freshness windows
/// downstream (<see cref="FreshUpdateDispatchEvidence.Matches"/>: two minutes) are measured from the oldest fact read.</item>
/// </list>
/// Data alone grants nothing: admission still compares the evidence with the frozen review, and the helper still compares
/// its own fingerprint with the frozen one.
/// </summary>
internal sealed class ChocolateyProfileEvidenceSource : IReviewedUpdateDispatchEvidenceSource
{
    /// <summary>Profile V2 "UI must disclose before review" (fixed text).</summary>
    internal const string UserCacheDisclosure = ChocolateyReviewDisclosurePresentation.UserCacheDisclosure;

    private readonly ChocolateyDesktopObserve observe;
    private readonly ChocolateyDesktopObserve observeClient;
    private readonly ChocolateyReleaseList releases;
    private readonly ChocolateyDispatchBaselines baselines;
    private readonly TimeProvider time;
    private readonly TimeSpan budget;

    internal ChocolateyProfileEvidenceSource(ChocolateyDispatchBaselines baselines)
        : this(ChocolateyDesktopObservation.ProductionObserve, ChocolateyDesktopObservation.ProductionObserveClient, ChocolateyReleaseList.Listed,
            baselines, TimeProvider.System, ChocolateyDesktopObservation.Budget)
    {
    }

    private ChocolateyProfileEvidenceSource(ChocolateyDesktopObserve observe, ChocolateyDesktopObserve observeClient, ChocolateyReleaseList releases,
        ChocolateyDispatchBaselines baselines, TimeProvider time, TimeSpan budget)
    {
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        this.observeClient = observeClient ?? throw new ArgumentNullException(nameof(observeClient));
        this.releases = releases ?? throw new ArgumentNullException(nameof(releases));
        this.baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.budget = budget;
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the source over a supplied observation and budget.</summary>
    internal static ChocolateyProfileEvidenceSource CreateForTests(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines,
        TimeProvider time, TimeSpan budget) =>
        new(observe, ChocolateyDesktopObservation.ProductionObserveClient, ChocolateyReleaseList.Listed, baselines, time, budget);

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): ADR-031 F′ over a supplied client-mode observation and a
    /// fixture release list.
    /// </summary>
    internal static ChocolateyProfileEvidenceSource CreateClientForTests(ChocolateyDesktopObserve observeClient, ChocolateyReleaseList releases,
        ChocolateyDispatchBaselines baselines, TimeProvider time, TimeSpan budget) =>
        new((_, _, _, _) => null, observeClient, releases, baselines, time, budget);

    public async Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        CancellationToken cancellationToken)
    {
        if (operation is null || ownership is null || !ownership.CanDispatchFor(operation)) return null;
        if (ChocolateyDesktopObservation.TryGetClientRouteTarget(operation, releases, out ChocolateyReviewedRelease? source, out _))
            return await ObserveClientAsync(operation, source, cancellationToken).ConfigureAwait(false);
        if (!ChocolateyDesktopObservation.TryGetRouteTarget(operation, out string? targetId, out ChocolateyNativeVersion? installed, out _))
            return null;
        // L4: the evidence time is when the observation started, never after it (the conservative end for freshness).
        DateTimeOffset observedAt = time.GetUtcNow();
        using ChocolateyProfileObservation? observation =
            await ChocolateyDesktopObservation.TryObserveAsync(observe, targetId, installed, budget, time, cancellationToken).ConfigureAwait(false);
        if (observation is null || cancellationToken.IsCancellationRequested) return null;
        string requester = observation.Common.RequestingUserSid;
        if (!string.Equals(requester, operation.RequestingUserSid, StringComparison.Ordinal)) return null;

        // F′ comes only from the fresh observation (A2-L6).
        string freshEvidenceId = ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint;
        if (CreateEvidence(operation, requester, freshEvidenceId, observedAt) is not { } evidence) return null;
        baselines.Record(operation, new ChocolateyDispatchBaseline(targetId, requester, observation.ClientState));
        return evidence;
    }

    /// <summary>
    /// ADR-031 design §7.1 F′ for the client self-update: a fresh Desktop-role client-mode observation that must select exactly
    /// the frozen source release of the same reviewed list (whose offer is the frozen target), for the same requester. The
    /// evidence identity is <c>choco-profile-v2:</c> plus the fresh fingerprint; the baseline records the source release for
    /// the verifier's postcheck. Anything else returns null (nothing is dispatched).
    /// </summary>
    private async Task<FreshUpdateDispatchEvidence?> ObserveClientAsync(ReviewedUpdateOperation operation, ChocolateyReviewedRelease source,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAt = time.GetUtcNow();
        using ChocolateyProfileObservation? observation = await ChocolateyDesktopObservation.TryObserveAsync(observeClient,
            ReviewedUpdateOperation.ChocolateyClientPackageId, source.NativeVersion, budget, time, cancellationToken).ConfigureAwait(false);
        if (observation is null || cancellationToken.IsCancellationRequested || !ReferenceEquals(observation.Release, source)) return null;
        string requester = observation.Common.RequestingUserSid;
        if (!string.Equals(requester, operation.RequestingUserSid, StringComparison.Ordinal)) return null;
        string freshEvidenceId = ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint;
        if (CreateEvidence(operation, requester, freshEvidenceId, observedAt) is not { } evidence) return null;
        baselines.Record(operation, new ChocolateyDispatchBaseline(ReviewedUpdateOperation.ChocolateyClientPackageId, requester,
            observation.ClientState, source));
        return evidence;
    }

    /// <summary>The single evidence construction: the fresh identity, the fresh requester and the observation start time.</summary>
    private static FreshUpdateDispatchEvidence? CreateEvidence(ReviewedUpdateOperation operation, string requester, string freshEvidenceId,
        DateTimeOffset observedAt)
    {
        try
        {
            return new FreshUpdateDispatchEvidence(operation.Selection, requester, operation.NativeInstanceId, operation.InstallationContextId,
                freshEvidenceId, observedAt, operation.RouteContract, operation.Mode, actionable: true, sourceIsFresh: true);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The longest a supplied candidate may have been resolved before the review observation starts. It equals the two-minute
    /// dispatch-evidence freshness window (<see cref="FreshUpdateDispatchEvidence.Matches"/>), the shortest freshness bound
    /// on this path, so an intent is never older than evidence dispatch itself would accept.
    /// </summary>
    internal static readonly TimeSpan IntentMaxAge = TimeSpan.FromMinutes(2);

    /// <summary>
    /// The review intent for a <b>supplied</b> <paramref name="intent"/>: the non-elevated client's <c>outdated</c> record
    /// for one package (the existing structured candidate), resolved by the caller at <paramref name="intentResolvedAt"/>.
    /// This method validates that record; it does not capture or re-run <c>outdated</c>. The intent must name the Community
    /// source, a supported non-client ID and normalized versions with a stable, strictly newer candidate, and must have been
    /// resolved no later than the observation start and at most <see cref="IntentMaxAge"/> before it (checked before
    /// anything is observed); the fresh observation must then find exactly that installed version. Anything else returns
    /// null. The result is provisional <see cref="ReviewedCandidateProvenance.UserCacheIntent"/> with the fixed disclosure;
    /// the helper confirms the exact candidate at stage C.
    /// </summary>
    internal async Task<ChocolateyReviewIntent?> ObserveReviewIntentAsync(StructuredUpdateCandidateEvidence intent, DateTimeOffset intentResolvedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset observedAt = time.GetUtcNow();
        if (intentResolvedAt == default || intentResolvedAt > observedAt || observedAt - intentResolvedAt > IntentMaxAge) return null;
        if (intent is null || !string.Equals(intent.SourceId, ChocolateyUpdateSourceConnector.CommunitySourceId, StringComparison.Ordinal) ||
            !ChocolateyNativePackageId.IsSupported(intent.PackageId) || ChocolateyClientReleasePolicy.IsClientTarget(intent.PackageId) ||
            !ChocolateyDesktopObservation.TryNormalized(intent.InstalledVersion, out ChocolateyNativeVersion? installed) ||
            !ChocolateyDesktopObservation.TryNormalized(intent.CandidateVersion, out ChocolateyNativeVersion? candidate) || candidate.IsPrerelease ||
            VersionComparison.Compare(new(installed.Normalized, candidate.Normalized)).Outcome != VersionComparisonOutcome.Newer)
            return null;
        using ChocolateyProfileObservation? observation =
            await ChocolateyDesktopObservation.TryObserveAsync(observe, intent.PackageId, installed, budget, time, cancellationToken).ConfigureAwait(false);
        if (observation is null || cancellationToken.IsCancellationRequested) return null;
        return new ChocolateyReviewIntent(intent.PackageId, installed.Normalized, candidate.Normalized, observation.Common.RequestingUserSid,
            ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint, ReviewedCandidateProvenance.UserCacheIntent,
            observation.TargetLibBadResidue, UserCacheDisclosure, observedAt, intentResolvedAt.ToUniversalTime());
    }
}
