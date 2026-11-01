using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// The desktop review intent (design rev 6 §7; Profile V2 "Native producer"): the fresh Desktop-role observation of the
/// target plus the non-elevated client's <c>outdated</c> candidate, kept as <see cref="ReviewedCandidateProvenance.UserCacheIntent"/>
/// with the fixed Profile V2 disclosure. Review data only: it is not helper qualification, not trusted candidate evidence
/// and not a dispatch permit; the helper must independently confirm the exact candidate before updating.
/// </summary>
internal sealed record ChocolateyReviewIntent(string PackageId, string InstalledVersion, string CandidateVersion, string RequestingUserSid,
    string ProfileEvidenceId, ReviewedCandidateProvenance CandidateProvenance, bool TargetLibBadResidue, string Disclosure, DateTimeOffset ObservedAt);

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
/// <item><see cref="ObserveReviewIntentAsync"/> is the same observation for a candidate intent the non-elevated client
/// already reported. It runs no process: the Desktop role has no held client binding, and the sealed N3 commands run only
/// through the helper item (design rev 6 §3, §4).</item>
/// </list>
/// Data alone grants nothing: admission still compares the evidence with the frozen review, and the helper still compares
/// its own fingerprint with the frozen one.
/// </summary>
internal sealed class ChocolateyProfileEvidenceSource : IReviewedUpdateDispatchEvidenceSource
{
    /// <summary>Profile V2 "UI must disclose before review" (fixed text).</summary>
    internal const string UserCacheDisclosure =
        "Candidate shown from your user cache; PC Steward confirms the exact version with protected machine data before updating.";

    private readonly ChocolateyDesktopObserve observe;
    private readonly ChocolateyDispatchBaselines baselines;
    private readonly TimeProvider time;

    internal ChocolateyProfileEvidenceSource(ChocolateyDispatchBaselines baselines)
        : this(ChocolateyDesktopObservation.ProductionObserve, baselines, TimeProvider.System)
    {
    }

    private ChocolateyProfileEvidenceSource(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines, TimeProvider time)
    {
        this.observe = observe ?? throw new ArgumentNullException(nameof(observe));
        this.baselines = baselines ?? throw new ArgumentNullException(nameof(baselines));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the source over a supplied observation.</summary>
    internal static ChocolateyProfileEvidenceSource CreateForTests(ChocolateyDesktopObserve observe, ChocolateyDispatchBaselines baselines,
        TimeProvider time) => new(observe, baselines, time);

    public async Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
        CancellationToken cancellationToken)
    {
        if (operation is null || ownership is null || !ownership.CanDispatchFor(operation) ||
            !ChocolateyDesktopObservation.TryGetRouteTarget(operation, out string? targetId, out ChocolateyNativeVersion? installed, out _))
            return null;
        using ChocolateyProfileObservation? observation =
            await ChocolateyDesktopObservation.TryObserveAsync(observe, targetId, installed, time, cancellationToken).ConfigureAwait(false);
        if (observation is null || cancellationToken.IsCancellationRequested) return null;
        string requester = observation.Common.RequestingUserSid;
        if (!string.Equals(requester, operation.RequestingUserSid, StringComparison.Ordinal)) return null;

        // F′ comes only from the fresh observation (A2-L6).
        string freshEvidenceId = ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint;
        FreshUpdateDispatchEvidence evidence;
        try
        {
            evidence = new FreshUpdateDispatchEvidence(operation.Selection, requester, operation.NativeInstanceId, operation.InstallationContextId,
                freshEvidenceId, time.GetUtcNow(), operation.RouteContract, operation.Mode, actionable: true, sourceIsFresh: true);
        }
        catch (ArgumentException)
        {
            return null;
        }
        baselines.Record(operation, new ChocolateyDispatchBaseline(targetId, requester, observation.ClientState));
        return evidence;
    }

    /// <summary>
    /// The review intent for <paramref name="intent"/>: the non-elevated client's <c>outdated</c> record for one package
    /// (the existing structured candidate). The intent must name the Community source, a supported non-client ID and
    /// normalized versions with a stable, strictly newer candidate; the fresh observation must then find exactly that
    /// installed version. Anything else returns null. The result is provisional user-cache intent with the fixed disclosure.
    /// </summary>
    internal async Task<ChocolateyReviewIntent?> ObserveReviewIntentAsync(StructuredUpdateCandidateEvidence intent, CancellationToken cancellationToken)
    {
        if (intent is null || !string.Equals(intent.SourceId, ChocolateyUpdateSourceConnector.CommunitySourceId, StringComparison.Ordinal) ||
            !ChocolateyNativePackageId.IsSupported(intent.PackageId) || ChocolateyClientReleasePolicy.IsClientTarget(intent.PackageId) ||
            !ChocolateyDesktopObservation.TryNormalized(intent.InstalledVersion, out ChocolateyNativeVersion? installed) ||
            !ChocolateyDesktopObservation.TryNormalized(intent.CandidateVersion, out ChocolateyNativeVersion? candidate) || candidate.IsPrerelease ||
            VersionComparison.Compare(new(installed.Normalized, candidate.Normalized)).Outcome != VersionComparisonOutcome.Newer)
            return null;
        using ChocolateyProfileObservation? observation =
            await ChocolateyDesktopObservation.TryObserveAsync(observe, intent.PackageId, installed, time, cancellationToken).ConfigureAwait(false);
        if (observation is null || cancellationToken.IsCancellationRequested) return null;
        return new ChocolateyReviewIntent(intent.PackageId, installed.Normalized, candidate.Normalized, observation.Common.RequestingUserSid,
            ReviewedUpdateOperation.ProfileEvidencePrefix + observation.Fingerprint, ReviewedCandidateProvenance.UserCacheIntent,
            observation.TargetLibBadResidue, UserCacheDisclosure, time.GetUtcNow());
    }
}
