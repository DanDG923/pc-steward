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
        ProductMatchProfile[] profiles = products.OrderBy(product => product.StableProductKey, StringComparer.Ordinal).ToArray();
        HashSet<(string SourceId, string PackageId)> linkedToConcreteProduct = LinkedToConcreteProduct(profiles, snapshots);
        foreach (ProductMatchProfile product in profiles)
        {
            // WinGet's own installed record of an app whose uninstall entry WinGet links to the same package by product code is the
            // same app, already assessed on that entry's row. It gets no row of its own (no duplicate in Updates).
            if (!product.HasConcreteLocalEvidence && product.ProviderPackageId is { } ownPackage && product.InstalledSourceId is { } ownSource &&
                linkedToConcreteProduct.Contains((ownSource.ToUpperInvariant(), ownPackage.ToUpperInvariant())))
            {
                continue;
            }

            ProductUpdatePolicy policy = policies?.GetValueOrDefault(product.StableProductKey) ?? new();
            if (policy.IsIgnoreActive(DateTimeOffset.UtcNow)) { items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.Ignored, null, policy.IgnoreUntil is null ? "The product is ignored indefinitely by a recorded user decision." : $"The product is ignored until {policy.IgnoreUntil.Value:O} by a recorded user decision.")); continue; }
            if (policy.IsPinned) { items.Add(new(product.StableProductKey, UpdateIntelligenceStatus.Pinned, null, policy.PinDescription ?? "The product is pinned by a recorded user decision.")); continue; }
            (SourceSnapshot Snapshot, PackageMatchCandidate Candidate)[] candidateEntries = snapshots
                .Where(source => source.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
                .SelectMany(source => source.Packages.Select(package => (source, new PackageMatchCandidate(package.SourceId, package.PackageId, package.DisplayName, null, null, package.Publisher, package.AvailableVersion, package.Scope, package.Architecture, package.Channel, true, package.ProviderSuppliedStableProductKey, package.RequiresConnectorSpecificVersionSemantics))))
                .ToArray();
            PackageMatchCandidate[] candidates = candidateEntries.Select(entry => entry.Candidate).ToArray();
            SourceSnapshot? unavailable = snapshots.FirstOrDefault(source =>
                source.State == SourceSnapshotState.Unavailable &&
                string.Equals(source.SourceId, product.InstalledSourceId, StringComparison.OrdinalIgnoreCase));
            bool noSourceIdentityAndAllSourcesUnavailable = product.InstalledSourceId is null && snapshots.Length > 0 && snapshots.All(source => source.State == SourceSnapshotState.Unavailable);
            if (candidates.Length == 0 && (unavailable is not null || noSourceIdentityAndAllSourcesUnavailable))
            {
                unavailable ??= snapshots.First(source => source.State == SourceSnapshotState.Unavailable);
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
            mapping = UseConfirmedSourceCurrentEvidence(mapping, snapshots);
            SourceSnapshot? source = sourceById.GetValueOrDefault(mapping.SourceId);
            bool availabilityUnavailable = IsAvailabilityUnavailable(mapping, source);
            UpdateIntelligenceStatus status = mapping.ReviewReasons.Contains(MappingReviewReason.SourceDisagreement) ? UpdateIntelligenceStatus.ReviewRequired
                : availabilityUnavailable ? UpdateIntelligenceStatus.FailedUnavailable
                : mapping.IsActionable ? UpdateIntelligenceStatus.VerifiedUpdate
                : IsVerifiedCurrent(mapping) ? UpdateIntelligenceStatus.Current
                : IsUnconfirmedSameVersion(mapping) ? UpdateIntelligenceStatus.Unmanaged
                : UpdateIntelligenceStatus.ReviewRequired;
            string itemExplanation = availabilityUnavailable
                ? "The repository identified the exact package, but it did not establish update availability. This is incomplete source evidence, not a decision for you; refresh the source to retry."
                : status == UpdateIntelligenceStatus.Unmanaged
                ? "A package that might be this app has the same version, so there is nothing to update. It hasn't been confirmed as the same app."
                : mapping.EvidenceExplanation;
            // Owner decision 2026-11-02: a recorded skip of exactly this candidate version hides it. It runs after matching and can
            // only remove actionability; a newer candidate than the skipped version is assessed normally again.
            if (status is UpdateIntelligenceStatus.VerifiedUpdate or UpdateIntelligenceStatus.ReviewRequired &&
                SkipVersionRule.Applies(policy.SkippedVersion, mapping.CandidateVersion))
            {
                status = UpdateIntelligenceStatus.Skipped;
                mapping = mapping with { IsActionable = false };
                itemExplanation = $"You chose to skip version {mapping.CandidateVersion}. A newer version will be offered again.";
            }
            items.Add(new(product.StableProductKey, status, mapping, itemExplanation, mapping.SourceId, source?.LastSuccessAt, observations));
        }
        Dictionary<UpdateIntelligenceStatus, int> counts = Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, status => items.Count(item => item.Status == status));
        return new(items, counts, snapshots);
    }

    private static bool HasSourceDisagreement(IReadOnlyCollection<SourceCandidateObservation> observations)
    {
        if (observations.Select(observation => observation.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2) return false;
        return HasDifferentKnownValues(observations.Select(observation => observation.CandidateVersion))
            || HasDifferentKnownValues(observations.Select(observation => observation.Publisher))
            || HasDifferentKnownValues(observations.Select(observation => observation.Scope))
            || HasDifferentKnownValues(observations.Select(observation => observation.Architecture))
            || HasDifferentKnownValues(observations.Select(observation => observation.Channel));
    }

    // Owner-facing rule (2026-11-13): when the package's version equals the installed one, nothing would ever run, so the scope,
    // architecture and channel evidence that binding an update needs is not required to call the app current. Conflicts still are.
    private static bool IsVerifiedCurrent(PackageMapping mapping) =>
        mapping.Confidence >= MappingConfidence.Medium &&
        mapping.VersionComparison.Outcome == VersionComparisonOutcome.Equal &&
        mapping.ReviewReasons.All(reason => reason is MappingReviewReason.CandidateIsCurrent
            or MappingReviewReason.MissingScopeEvidence or MappingReviewReason.MissingArchitectureEvidence or MappingReviewReason.MissingChannelEvidence);

    // A weak automatic match (near name, missing or different publisher) at the same version offers nothing to review: no update
    // exists, and confirming the identity can wait until the package has a newer version.
    private static bool IsUnconfirmedSameVersion(PackageMapping mapping) =>
        mapping.Confidence < MappingConfidence.Medium &&
        mapping.Origin == MappingOrigin.Automatic &&
        mapping.VersionComparison.Outcome == VersionComparisonOutcome.Equal;

    private static HashSet<(string SourceId, string PackageId)> LinkedToConcreteProduct(IReadOnlyCollection<ProductMatchProfile> profiles, IReadOnlyCollection<SourceSnapshot> snapshots)
    {
        HashSet<string> concreteKeys = profiles.Where(profile => profile.HasConcreteLocalEvidence).Select(profile => profile.StableProductKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshots
            .Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
            .SelectMany(snapshot => snapshot.Packages)
            .Where(package => package.ProviderSuppliedStableProductKey is { } key && concreteKeys.Contains(key))
            .Select(package => (package.SourceId.ToUpperInvariant(), package.PackageId.ToUpperInvariant()))
            .ToHashSet();
    }

    private static bool IsAvailabilityUnavailable(PackageMapping mapping, SourceSnapshot? source)
    {
        if (mapping.Confidence != MappingConfidence.High ||
            mapping.CandidateVersion is not null ||
            source?.State is not (SourceSnapshotState.Live or SourceSnapshotState.Cached) ||
            mapping.ReviewReasons.Count == 0 ||
            !mapping.ReviewReasons.All(reason => reason == MappingReviewReason.IncomparableVersion))
        {
            return false;
        }

        SourcePackageRecord[] exactPackages = source.Packages
            .Where(package => string.Equals(package.SourceId, mapping.SourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(package.PackageId, mapping.PackageId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return exactPackages.Length == 1 && exactPackages[0].AvailableVersion is null;
    }

    private static PackageMapping UseConfirmedSourceCurrentEvidence(PackageMapping mapping, IReadOnlyCollection<SourceSnapshot> snapshots)
    {
        if (mapping.Origin != MappingOrigin.UserConfirmed ||
            !mapping.ReviewReasons.All(reason => reason is MappingReviewReason.CandidateIsOlder or MappingReviewReason.CandidateIsCurrent or MappingReviewReason.IncomparableVersion))
        {
            return mapping;
        }

        SourcePackageRecord? package = snapshots
            .Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
            .Where(snapshot => string.Equals(snapshot.SourceId, mapping.SourceId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(snapshot => snapshot.Packages)
            .FirstOrDefault(candidate => string.Equals(candidate.PackageId, mapping.PackageId, StringComparison.OrdinalIgnoreCase));
        if (package?.InstalledVersion is null || package.AvailableVersion is null ||
            !string.Equals(package.InstalledVersion, package.AvailableVersion, StringComparison.OrdinalIgnoreCase))
        {
            return mapping;
        }

        return mapping with
        {
            CandidateVersion = package.AvailableVersion,
            IsActionable = false,
            ReviewReasons = [],
            VersionComparison = new VersionComparisonResult(VersionComparisonOutcome.Equal, VersionComparisonExplanation.NumericDotted),
            EvidenceExplanation = "The user confirmed this exact package identity. The source reports that its installed version matches the available version."
        };
    }

    private static bool HasDifferentKnownValues(IEnumerable<string?> values) => values.Where(value => !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase)).Select(value => value!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Count() > 1;
}

/// <summary>
/// Owner decision 2026-11-02 (FR-POL-01–05): the deterministic Skip this version rule. A skip of version V applies only while the
/// candidate equals V under the domain version comparer, or is the identical (trimmed, case-insensitive) string the user saw.
/// Newer, older, incomparable and connector-deferred candidates are not skipped and stay subject to the existing actionability rules.
/// </summary>
public static class SkipVersionRule
{
    public static bool Applies(string? skippedVersion, string? candidateVersion)
    {
        if (string.IsNullOrWhiteSpace(skippedVersion) || string.IsNullOrWhiteSpace(candidateVersion)) return false;
        string skipped = skippedVersion.Trim();
        string candidate = candidateVersion.Trim();
        if (string.Equals(skipped, candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return VersionComparison.Compare(new VersionComparisonRequest(skipped, candidate)).Outcome == VersionComparisonOutcome.Equal;
    }
}
