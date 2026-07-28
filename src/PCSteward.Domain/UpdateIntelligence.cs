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
            (SourceSnapshot Snapshot, PackageMatchCandidate Candidate)[] candidateEntries = snapshots
                .Where(source => source.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
                .SelectMany(source => source.Packages.Select(package => (source, new PackageMatchCandidate(package.SourceId, package.PackageId, package.DisplayName, null, null, package.Publisher, package.AvailableVersion, package.Scope, package.Architecture, package.Channel, true, package.ProviderSuppliedStableProductKey, package.RequiresConnectorSpecificVersionSemantics))))
                .ToArray();
            PackageMatchCandidate[] candidates = candidateEntries.Select(entry => entry.Candidate).ToArray();
            if (candidates.Length == 0 && snapshots.Any(source => source.State == SourceSnapshotState.Unavailable))
            {
                SourceSnapshot unavailable = snapshots.First(source => source.State == SourceSnapshotState.Unavailable);
                items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.FailedUnavailable, null, "No source result is available. " + string.Join(" ", unavailable.Warnings), unavailable.SourceId, unavailable.LastSuccessAt));
                continue;
            }
            SourceCandidateObservation[] observations = candidateEntries
                .Select(entry => (entry.Snapshot, Entry: ProductPackageMatching.Resolve(product, [entry.Candidate], declaredMappings, trustedSources).Mapping))
                .Where(entry => entry.Entry is not null)
                .Select(entry => new SourceCandidateObservation(entry.Snapshot.SourceId, entry.Entry!.PackageId, entry.Entry.CandidateVersion, entry.Snapshot.Packages.First(package => string.Equals(package.PackageId, entry.Entry.PackageId, StringComparison.OrdinalIgnoreCase)).Publisher, entry.Snapshot.Packages.First(package => string.Equals(package.PackageId, entry.Entry.PackageId, StringComparison.OrdinalIgnoreCase)).Scope, entry.Snapshot.Packages.First(package => string.Equals(package.PackageId, entry.Entry.PackageId, StringComparison.OrdinalIgnoreCase)).Architecture, entry.Snapshot.Packages.First(package => string.Equals(package.PackageId, entry.Entry.PackageId, StringComparison.OrdinalIgnoreCase)).Channel, entry.Snapshot.State, entry.Snapshot.LastSuccessAt, entry.Entry.Confidence, entry.Entry.ReviewReasons))
                .OrderBy(observation => observation.SourceId, StringComparer.Ordinal)
                .ThenBy(observation => observation.PackageId, StringComparer.Ordinal)
                .ToArray();
            ProductPackageMatchResult result = ProductPackageMatching.Resolve(product, candidates, declaredMappings, trustedSources);
            if (result.Mapping is null)
            {
                UpdateIntelligenceStatus unmatchedStatus = observations.Length == 0 ? UpdateIntelligenceStatus.Unmanaged : UpdateIntelligenceStatus.ReviewRequired;
                string explanation = observations.Length == 0 ? result.Explanation : result.Explanation + " Matching source candidates are shown for review.";
                items.Add(new(product.StableProductKey, unmatchedStatus, null, explanation, SourceCandidates: observations));
                continue;
            }
            PackageMapping mapping = result.Mapping;
            if ((mapping.Origin is MappingOrigin.Automatic or MappingOrigin.ProviderSupplied) && HasSourceDisagreement(observations))
            {
                MappingReviewReason[] reasons = mapping.ReviewReasons.Concat([MappingReviewReason.SourceDisagreement]).Distinct().ToArray();
                mapping = mapping with { IsActionable = false, ReviewReasons = reasons, EvidenceExplanation = mapping.EvidenceExplanation + " Review required: source candidates disagree." };
            }
            SourceSnapshot? source = sourceById.GetValueOrDefault(mapping.SourceId);
            UpdateIntelligenceStatus status = mapping.IsActionable ? UpdateIntelligenceStatus.VerifiedUpdate
                : mapping.VersionComparison.Outcome == VersionComparisonOutcome.Equal ? UpdateIntelligenceStatus.Current
                : UpdateIntelligenceStatus.ReviewRequired;
            items.Add(new(product.StableProductKey, status, mapping, mapping.EvidenceExplanation, mapping.SourceId, source?.LastSuccessAt, observations));
        }
        Dictionary<UpdateIntelligenceStatus, int> counts = Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, status => items.Count(item => item.Status == status));
        return new(items, counts, snapshots);
    }

    private static bool HasSourceDisagreement(IReadOnlyCollection<SourceCandidateObservation> observations)
    {
        if (observations.Select(observation => observation.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) return false;
        return HasDifferentKnownValues(observations.Select(observation => observation.CandidateVersion))
            || HasDifferentKnownValues(observations.Select(observation => observation.Publisher))
            || HasDifferentKnownValues(observations.Select(observation => observation.Architecture))
            || HasDifferentKnownValues(observations.Select(observation => observation.Channel));
    }

    private static bool HasDifferentKnownValues(IEnumerable<string?> values) => values.Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() > 1;
}
