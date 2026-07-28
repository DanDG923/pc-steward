namespace PCSteward.Domain;

/// <summary>Combines bounded, non-executable source records with deterministic matching rules.</summary>
public static class UpdateIntelligence
{
    public static UpdateIntelligenceSummary Assess(
        IEnumerable<ProductMatchProfile> products,
        IEnumerable<SourceSnapshot> sources,
        IEnumerable<DeclaredPackageMapping>? declaredMappings = null,
        IReadOnlyDictionary<string, ProductUpdatePolicy>? policies = null)
    {
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(sources);
        SourceSnapshot[] snapshots = sources.OrderBy(source => source.SourceId, StringComparer.Ordinal).ToArray();
        string[] trustedSources = snapshots.Where(source => source.State is SourceSnapshotState.Live or SourceSnapshotState.Cached).Select(source => source.SourceId).ToArray();
        Dictionary<string, SourceSnapshot> sourceById = snapshots.ToDictionary(source => source.SourceId, StringComparer.OrdinalIgnoreCase);
        List<UpdateIntelligenceItem> items = [];
        foreach (ProductMatchProfile product in products.OrderBy(product => product.StableProductKey, StringComparer.Ordinal))
        {
            ProductUpdatePolicy policy = policies?.GetValueOrDefault(product.StableProductKey) ?? new();
            if (policy.IsIgnoreActive(DateTimeOffset.UtcNow)) { items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.Ignored, null, policy.IgnoreUntil is null ? "The product is ignored indefinitely by a recorded user decision." : $"The product is ignored until {policy.IgnoreUntil.Value:O} by a recorded user decision.")); continue; }
            if (policy.IsPinned) { items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.Pinned, null, policy.PinDescription ?? "The product is pinned by a recorded user decision.")); continue; }
            PackageMatchCandidate[] candidates = snapshots.Where(source => source.State is SourceSnapshotState.Live or SourceSnapshotState.Cached).SelectMany(source => source.Packages).Select(package => new PackageMatchCandidate(package.SourceId, package.PackageId, package.DisplayName, null, null, package.Publisher, package.AvailableVersion, package.Scope, package.Architecture, package.Channel, true, package.ProviderSuppliedStableProductKey, package.RequiresConnectorSpecificVersionSemantics)).ToArray();
            if (candidates.Length == 0 && snapshots.Any(source => source.State == SourceSnapshotState.Unavailable))
            {
                SourceSnapshot unavailable = snapshots.First(source => source.State == SourceSnapshotState.Unavailable);
                items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.FailedUnavailable, null, "No source result is available. " + string.Join(" ", unavailable.Warnings), unavailable.SourceId, unavailable.LastSuccessAt));
                continue;
            }
            ProductPackageMatchResult result = ProductPackageMatching.Resolve(product, candidates, declaredMappings, trustedSources);
            if (result.Mapping is null) { items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.Unmanaged, null, result.Explanation)); continue; }
            SourceSnapshot? source = sourceById.GetValueOrDefault(result.Mapping.SourceId);
            UpdateIntelligenceStatus status = result.Mapping.IsActionable ? UpdateIntelligenceStatus.VerifiedUpdate
                : result.Mapping.VersionComparison.Outcome == VersionComparisonOutcome.Equal ? UpdateIntelligenceStatus.Current
                : UpdateIntelligenceStatus.ReviewRequired;
            items.Add(new(product.StableProductKey, status, result.Mapping, result.Explanation, result.Mapping.SourceId, source?.LastSuccessAt));
        }
        Dictionary<UpdateIntelligenceStatus, int> counts = Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, status => items.Count(item => item.Status == status));
        return new(items, counts, snapshots);
    }
}
