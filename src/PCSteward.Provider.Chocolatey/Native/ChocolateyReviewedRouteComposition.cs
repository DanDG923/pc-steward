using PCSteward.Application;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>A supplied user-cache candidate (the non-elevated client's <c>outdated</c> record) and when its resolver resolved it.</summary>
internal sealed record ChocolateyResolvedIntent(StructuredUpdateCandidateEvidence Candidate, DateTimeOffset ResolvedAt);

/// <summary>
/// Resolves the user-cache candidate intent when asked; it must resolve it during the call (N5 obligation (a)). The production
/// resolver is <see cref="ChocolateyReviewIntentResolver.For"/>.
/// </summary>
internal delegate Task<ChocolateyResolvedIntent?> ChocolateyIntentResolver(CancellationToken cancellationToken);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 N6 composition preparation for the N5 obligations:
/// <list type="bullet">
/// <item>(b) the single construction site of the desktop producers: ONE <see cref="ChocolateyDispatchBaselines"/> shared by
/// the F′ evidence source and the verifier, so the verifier takes the baseline that F′ recorded for the same operation
/// (separate instances would make every verification unverifiable).</item>
/// <item>(a) the fresh-intent supply path: <see cref="ResolveReviewIntentAsync(ChocolateyIntentResolver, CancellationToken)"/> asks the resolver for the intent and
/// accepts it only when its own resolution time falls inside that call, then observes immediately; the review shows that
/// time (<see cref="ChocolateyReviewIntentFacts.IntentResolvedAt"/>).</item>
/// <item>the split-token probe for the review's §(L6) unavailability reason.</item>
/// </list>
/// It supplies no route dispatcher, leaves the review gate's admitted-mode check unchanged and has no <c>src</c> caller
/// (architecture test). Since the Task 50 activation the review gate admits the route's exact shape, but nothing in
/// production composes a review or a dispatch for it yet.
/// </summary>
internal sealed class ChocolateyReviewedRouteComposition
{
    private readonly ChocolateyProfileEvidenceSource evidenceSource;
    private readonly ChocolateyReviewedUpdateVerifier verifier;
    private readonly TimeProvider time;
    private readonly ChocolateyDesktopObserve observeClient;
    private readonly ChocolateyReleaseList releases;

    internal ChocolateyReviewedRouteComposition()
        : this(static baselines => new ChocolateyProfileEvidenceSource(baselines), static baselines => new ChocolateyReviewedUpdateVerifier(baselines),
            TimeProvider.System, ChocolateyDesktopObservation.ProductionObserveClient, ChocolateyReleaseList.Listed)
    {
    }

    private ChocolateyReviewedRouteComposition(Func<ChocolateyDispatchBaselines, ChocolateyProfileEvidenceSource> createSource,
        Func<ChocolateyDispatchBaselines, ChocolateyReviewedUpdateVerifier> createVerifier, TimeProvider time, ChocolateyDesktopObserve observeClient,
        ChocolateyReleaseList releases)
    {
        ArgumentNullException.ThrowIfNull(createSource);
        ArgumentNullException.ThrowIfNull(createVerifier);
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        this.observeClient = observeClient ?? throw new ArgumentNullException(nameof(observeClient));
        this.releases = releases ?? throw new ArgumentNullException(nameof(releases));
        // Obligation (b): exactly one baseline store for both producers.
        ChocolateyDispatchBaselines baselines = new();
        evidenceSource = createSource(baselines) ?? throw new InvalidOperationException("No evidence source was created.");
        verifier = createVerifier(baselines) ?? throw new InvalidOperationException("No verifier was created.");
    }

    /// <summary>Test-only (an architecture test bans every <c>src</c> caller): the composition over supplied producer factories.</summary>
    internal static ChocolateyReviewedRouteComposition CreateForTests(Func<ChocolateyDispatchBaselines, ChocolateyProfileEvidenceSource> createSource,
        Func<ChocolateyDispatchBaselines, ChocolateyReviewedUpdateVerifier> createVerifier, TimeProvider time) =>
        new(createSource, createVerifier, time, ChocolateyDesktopObservation.ProductionObserveClient, ChocolateyReleaseList.Listed);

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the composition over supplied producer factories and a
    /// supplied client-mode review observation and fixture release list.
    /// </summary>
    internal static ChocolateyReviewedRouteComposition CreateWithClientReviewForTests(Func<ChocolateyDispatchBaselines, ChocolateyProfileEvidenceSource> createSource,
        Func<ChocolateyDispatchBaselines, ChocolateyReviewedUpdateVerifier> createVerifier, TimeProvider time, ChocolateyDesktopObserve observeClient,
        ChocolateyReleaseList releases) => new(createSource, createVerifier, time, observeClient, releases);

    /// <summary>
    /// Chocolatey Confirm design §3: the review-time observation of the installed client at exactly the verified release, over
    /// the same client-mode observation and release list as this composition's F′ producer
    /// (<see cref="ChocolateyDesktopObservation.ObserveClientReviewAsync"/>). Null on any refusal, fault or cancellation.
    /// </summary>
    internal Task<ChocolateyClientReviewEvidence?> ObserveClientReviewAsync(string verifiedInstalledRelease, CancellationToken cancellationToken) =>
        ChocolateyDesktopObservation.ObserveClientReviewAsync(observeClient, releases, verifiedInstalledRelease, ChocolateyDesktopObservation.Budget, time,
            cancellationToken);

    internal IReviewedUpdateDispatchEvidenceSource EvidenceSource => evidenceSource;

    internal IReviewedUpdateVerifier Verifier => verifier;

    /// <summary>
    /// Obligation (a): resolves the intent now and observes immediately. The resolver's own resolution time must be no earlier
    /// than this call's start and no later than its return, so a cached or replayed intent is refused; the producer then
    /// applies its own freshness and validation rules. Any refusal, fault or cancellation returns null.
    /// </summary>
    internal async Task<ChocolateyReviewIntentFacts?> ResolveReviewIntentAsync(ChocolateyIntentResolver resolve, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        DateTimeOffset requested = time.GetUtcNow();
        ChocolateyResolvedIntent? resolved;
        try
        {
            resolved = await resolve(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        if (resolved?.Candidate is null || cancellationToken.IsCancellationRequested) return null;
        DateTimeOffset returned = time.GetUtcNow();
        if (resolved.ResolvedAt < requested || resolved.ResolvedAt > returned) return null;
        ChocolateyReviewIntent? intent;
        try
        {
            intent = await evidenceSource.ObserveReviewIntentAsync(resolved.Candidate, resolved.ResolvedAt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        return intent is null || cancellationToken.IsCancellationRequested ? null : ToFacts(intent);
    }

    /// <summary>
    /// Admission obligation 6: the production supply path. The production <paramref name="resolver"/> resolves the exact
    /// <paramref name="packageId"/> inside this call, immediately before the observation, under the same in-call time rule.
    /// </summary>
    internal Task<ChocolateyReviewIntentFacts?> ResolveReviewIntentAsync(ChocolateyReviewIntentResolver resolver, string packageId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return ResolveReviewIntentAsync(resolver.For(packageId), cancellationToken);
    }

    /// <summary>The review-relevant facts of an intent, keeping its own resolved time separate from the observation time.</summary>
    internal static ChocolateyReviewIntentFacts ToFacts(ChocolateyReviewIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        return new(intent.PackageId, intent.InstalledVersion, intent.CandidateVersion, intent.RequestingUserSid, intent.ProfileEvidenceId,
            intent.CandidateProvenance, intent.TargetLibBadResidue, intent.IntentResolvedAt, intent.ObservedAt);
    }

    /// <summary>The desktop's own token under the §2.1 rules, mapped to the review's split-token state (§(L6)).</summary>
    internal static ReviewedRouteSplitTokenState ProbeSplitTokenState()
    {
        using RequesterToken? token = RequesterToken.TryFromCurrentProcess(out TokenVerdict verdict);
        return MapSplitToken(token is null ? verdict : TokenVerdict.Accepted);
    }

    /// <summary>
    /// Pure mapping: only <see cref="TokenVerdict.Accepted"/> is a usable split token. A token that is not the Limited half of
    /// a split pair (UAC off, standard user, or an elevated desktop) is <see cref="ReviewedRouteSplitTokenState.NoSplitToken"/>;
    /// a refused shape is unsupported; anything else is unknown. Every value except Accepted is unavailable.
    /// </summary>
    internal static ReviewedRouteSplitTokenState MapSplitToken(TokenVerdict verdict) => verdict switch
    {
        TokenVerdict.Accepted => ReviewedRouteSplitTokenState.SplitTokenLimited,
        TokenVerdict.PeerNotLimited or TokenVerdict.EvaluationNotLimited or TokenVerdict.NoLinkedToken => ReviewedRouteSplitTokenState.NoSplitToken,
        TokenVerdict.EvaluationNotMediumIntegrity or TokenVerdict.EvaluationRestricted or TokenVerdict.EvaluationAppContainer or
            TokenVerdict.EvaluationAdministratorsEnabled or TokenVerdict.EvaluationPrivilegeNotAllowed => ReviewedRouteSplitTokenState.DesktopTokenUnsupported,
        _ => ReviewedRouteSplitTokenState.Unknown,
    };
}
