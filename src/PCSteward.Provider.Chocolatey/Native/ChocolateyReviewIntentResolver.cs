using PCSteward.Application;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 N5 obligation (a), admission obligation 6: the production resolver of the
/// user-cache candidate intent that <see cref="ChocolateyReviewedRouteComposition.ResolveReviewIntentAsync(ChocolateyIntentResolver, CancellationToken)"/>
/// asks for immediately before the desktop observation.
/// <list type="bullet">
/// <item>It resolves only through the existing <see cref="ChocolateyUpdateSourceConnector.ResolveAsync"/> (its machine-trusted
/// client, fixed <c>outdated</c> arguments, bounded output and timeout). It constructs no process and passes no argument of
/// its own beyond the Community source ID and the selected package ID, which is first checked against the supported ID grammar.</item>
/// <item>It stamps its own resolution time from the injected clock when resolution starts, the conservative end for every
/// downstream freshness window (N5 review L4).</item>
/// <item>It fails closed: an invalid ID, a fault, a timeout, the real-time cap, cancellation, no candidate, an ambiguous or
/// non-newer candidate (the connector refuses those), a non-Community source, or a candidate whose ID is not ordinally the
/// selected ID all give no intent.</item>
/// </list>
/// The intent stays provisional (UserCacheIntent): the observation validates it again, the review compares it with the frozen
/// selection, and the helper confirms the exact candidate. This type supplies no dispatcher or admission.
/// </summary>
internal sealed class ChocolateyReviewIntentResolver
{
    /// <summary>Real-time margin over the connector's own timeout, so the call cannot outlive its bound.</summary>
    internal static readonly TimeSpan CapMargin = TimeSpan.FromSeconds(5);

    private readonly ChocolateyUpdateSourceConnector connector;
    private readonly TimeProvider time;
    private readonly TimeSpan cap;

    internal ChocolateyReviewIntentResolver(ChocolateyUpdateSourceConnector connector, TimeProvider time)
        : this(connector, time, (connector ?? throw new ArgumentNullException(nameof(connector))).Timeout + CapMargin)
    {
    }

    private ChocolateyReviewIntentResolver(ChocolateyUpdateSourceConnector connector, TimeProvider time, TimeSpan cap)
    {
        this.connector = connector ?? throw new ArgumentNullException(nameof(connector));
        this.time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cap, TimeSpan.Zero);
        this.cap = cap;
    }

    /// <summary>Test-only: the resolver with a supplied real-time cap.</summary>
    internal static ChocolateyReviewIntentResolver CreateForTests(ChocolateyUpdateSourceConnector connector, TimeProvider time, TimeSpan cap) =>
        new(connector, time, cap);

    /// <summary>The resolver for exactly <paramref name="packageId"/>, as the composition's intent supply delegate.</summary>
    internal ChocolateyIntentResolver For(string packageId) => cancellationToken => ResolveAsync(packageId, cancellationToken);

    /// <summary>Resolves the selected package's user-cache candidate now; null on every refusal, fault or cancellation.</summary>
    internal async Task<ChocolateyResolvedIntent?> ResolveAsync(string packageId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(packageId) || !ChocolateyLocalInventoryProvider.ValidPackageId(packageId) || cancellationToken.IsCancellationRequested)
            return null;
        DateTimeOffset resolvedAt = time.GetUtcNow();
        StructuredUpdateCandidateEvidence? candidate;
        try
        {
            using CancellationTokenSource bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(cap);
            candidate = await connector.ResolveAsync(ChocolateyUpdateSourceConnector.CommunitySourceId, packageId, bounded.Token).ConfigureAwait(false);
            if (bounded.IsCancellationRequested) return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
        if (cancellationToken.IsCancellationRequested || candidate is null ||
            !string.Equals(candidate.SourceId, ChocolateyUpdateSourceConnector.CommunitySourceId, StringComparison.Ordinal) ||
            !string.Equals(candidate.PackageId, packageId, StringComparison.Ordinal))
            return null;
        return new(candidate, resolvedAt);
    }
}
