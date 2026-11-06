using PCSteward.Application;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Chocolatey package Confirm addendum §5: the public façade over one N6 producer composition
/// (<c>ChocolateyReviewedRouteComposition</c>) for Chocolatey Community package updates, so the production composition in
/// <c>PCSteward.Execution</c> never names the Native namespace. Each instance holds the F′ evidence source and the package verifier
/// over ONE shared baseline store, and the production user-cache intent resolver over the existing connector query
/// (<see cref="ChocolateyUpdateSourceConnector.ResolveAsync"/>). It is the only <c>src</c> constructor site of that resolver.
/// It observes nothing until asked, starts nothing and admits nothing; the only <c>src</c> constructor caller is
/// <c>ChocolateyPackageUpdateComposition.CreateSession</c> (architecture-pinned).
/// </summary>
public sealed class ChocolateyPackageUpdateProducers
{
    private readonly ChocolateyReviewedRouteComposition composition = new();
    private readonly ChocolateyReviewIntentResolver resolver = new(new ChocolateyUpdateSourceConnector(), TimeProvider.System);

    /// <summary>F′: the fresh Desktop-role re-observation of the frozen installed version before the claim.</summary>
    public IReviewedUpdateDispatchEvidenceSource EvidenceSource => composition.EvidenceSource;

    /// <summary>The post-update verifier of the package route.</summary>
    public IReviewedUpdateVerifier Verifier => composition.Verifier;

    /// <summary>
    /// Checks exactly <paramref name="packageId"/> now: the user-cache candidate is resolved inside the call and observed at once
    /// (N5 obligation (a), admission obligation 6). Null on any refusal, fault or cancellation.
    /// </summary>
    public async Task<ChocolateyReviewIntentFacts?> CheckReviewIntentAsync(string packageId, CancellationToken cancellationToken)
    {
        try { return await composition.ResolveReviewIntentAsync(resolver, packageId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }
}
