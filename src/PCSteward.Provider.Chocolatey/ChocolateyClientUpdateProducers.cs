using PCSteward.Application;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Provider.Chocolatey;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Chocolatey Confirm design §3 item 1: the public façade over one N6 producer composition
/// (<c>ChocolateyReviewedRouteComposition</c>) for the Chocolatey client self-update, so the production composition in
/// <c>PCSteward.Execution</c> never names the Native namespace. Each instance holds the F′ evidence source and the client verifier
/// over ONE shared baseline store, and the review-time observation over the same client-mode observation and compiled list.
/// It observes nothing until asked, starts nothing and admits nothing; the only <c>src</c> constructor caller is
/// <c>ChocolateyClientUpdateComposition.CreateSession</c> (architecture-pinned).
/// </summary>
public sealed class ChocolateyClientUpdateProducers
{
    private readonly ChocolateyReviewedRouteComposition composition = new();

    /// <summary>F′: the fresh Desktop-role re-observation before the claim (the production client-mode producer).</summary>
    public IReviewedUpdateDispatchEvidenceSource EvidenceSource => composition.EvidenceSource;

    /// <summary>The post-update verifier; it also implements <see cref="IReviewedClientUpdateVerifier"/>.</summary>
    public IReviewedUpdateVerifier Verifier => composition.Verifier;

    /// <summary>The review-time observation of the installed client at exactly the verified release; null on any refusal or fault.</summary>
    public async Task<ChocolateyClientReviewEvidence?> ObserveClientReviewAsync(string verifiedInstalledRelease, CancellationToken cancellationToken)
    {
        try { return await composition.ObserveClientReviewAsync(verifiedInstalledRelease, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }
}
