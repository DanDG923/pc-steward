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
        HashSet<string> concreteKeys = profiles.Where(profile => profile.HasConcreteLocalEvidence).Select(profile => profile.StableProductKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<(string SourceId, string PackageId)> linkedToConcreteProduct = LinkedToConcreteProduct(concreteKeys, snapshots);
        Dictionary<string, (string SourceId, string PackageId)> wingetListsAs = WinGetListsAs(profiles);
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
            mapping = UseSourceReportsNoNewerVersion(mapping, snapshots);
            SourceSnapshot? source = sourceById.GetValueOrDefault(mapping.SourceId);
            bool availabilityUnavailable = IsAvailabilityUnavailable(mapping, source);
            UpdateIntelligenceStatus status = mapping.ReviewReasons.Contains(MappingReviewReason.SourceDisagreement) ? UpdateIntelligenceStatus.ReviewRequired
                : availabilityUnavailable ? UpdateIntelligenceStatus.FailedUnavailable
                : mapping.IsActionable ? UpdateIntelligenceStatus.VerifiedUpdate
                : IsVerifiedCurrent(mapping) ? UpdateIntelligenceStatus.Current
                : IsListedByWinGetAtTheSameVersion(product, mapping, wingetListsAs) ? UpdateIntelligenceStatus.Current
                : IsUnconfirmedSameVersion(mapping) ? UpdateIntelligenceStatus.Unmanaged
                : UpdateIntelligenceStatus.ReviewRequired;
            string itemExplanation = availabilityUnavailable
                ? "The repository identified the exact package, but it did not establish update availability. This is incomplete source evidence, not a decision for you; refresh the source to retry."
                : status == UpdateIntelligenceStatus.Current && !IsVerifiedCurrent(mapping)
                ? $"WinGet lists this app as package {mapping.PackageId} at the same version, so there is nothing to update."
                : status == UpdateIntelligenceStatus.Unmanaged
                ? "A package that might be this app has no newer version, so there is nothing to update. It hasn't been confirmed as the same app."
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
        // Nor does it when an uninstall entry or MSIX package already matches that exact package by name and publisher, or better, or is
        // the entry WinGet's own list names (WinGetListsAs).
        HashSet<(string SourceId, string PackageId)> matchedByConcreteProduct = items
            .Where(item => concreteKeys.Contains(item.StableProductKey) && item.Mapping is { } mapping &&
                (mapping.Confidence >= MappingConfidence.Medium || wingetListsAs.GetValueOrDefault(item.StableProductKey) == (mapping.SourceId.ToUpperInvariant(), mapping.PackageId.ToUpperInvariant())))
            .Select(item => (item.Mapping!.SourceId.ToUpperInvariant(), item.Mapping.PackageId.ToUpperInvariant()))
            .ToHashSet();
        items.RemoveAll(item => profiles.First(profile => profile.StableProductKey == item.StableProductKey) is { HasConcreteLocalEvidence: false, ProviderPackageId: { } package, InstalledSourceId: { } source } &&
            matchedByConcreteProduct.Contains((source.ToUpperInvariant(), package.ToUpperInvariant())));
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

    // WinGet's installed list names each package by the uninstall entry it correlated it with, so a WinGet-only record whose name and
    // version equal exactly one concrete product's says which package that product is. Only used to call a same-version app current
    // and to avoid a second row; an update still needs the matching evidence above.
    private static Dictionary<string, (string SourceId, string PackageId)> WinGetListsAs(IReadOnlyCollection<ProductMatchProfile> profiles)
    {
        ProductMatchProfile[] concrete = profiles.Where(profile => profile.HasConcreteLocalEvidence && profile.InstalledVersion is not null).ToArray();
        Dictionary<string, (string SourceId, string PackageId)> result = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> claimedTwice = new(StringComparer.OrdinalIgnoreCase);
        foreach (ProductMatchProfile record in profiles.Where(profile => !profile.HasConcreteLocalEvidence && profile.ProviderPackageId is not null && profile.InstalledSourceId is not null && profile.InstalledVersion is not null))
        {
            ProductMatchProfile[] same = concrete.Where(profile =>
                string.Equals(profile.DisplayName.Trim(), record.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(profile.InstalledVersion, record.InstalledVersion, StringComparison.Ordinal)).Take(2).ToArray();
            if (same.Length != 1) continue;
            if (!result.TryAdd(same[0].StableProductKey, (record.InstalledSourceId!.ToUpperInvariant(), record.ProviderPackageId!.ToUpperInvariant()))) claimedTwice.Add(same[0].StableProductKey);
        }
        foreach (string key in claimedTwice) result.Remove(key);
        return result;
    }

    private static bool IsListedByWinGetAtTheSameVersion(ProductMatchProfile product, PackageMapping mapping, Dictionary<string, (string SourceId, string PackageId)> wingetListsAs) =>
        mapping.Origin == MappingOrigin.Automatic &&
        mapping.VersionComparison.Outcome == VersionComparisonOutcome.Equal &&
        wingetListsAs.GetValueOrDefault(product.StableProductKey) == (mapping.SourceId.ToUpperInvariant(), mapping.PackageId.ToUpperInvariant()) &&
        mapping.ReviewReasons.All(reason => reason is MappingReviewReason.AmbiguousPublisher or MappingReviewReason.CandidateIsCurrent
            or MappingReviewReason.MissingScopeEvidence or MappingReviewReason.MissingArchitectureEvidence or MappingReviewReason.MissingChannelEvidence);

    private static HashSet<(string SourceId, string PackageId)> LinkedToConcreteProduct(HashSet<string> concreteKeys, IReadOnlyCollection<SourceSnapshot> snapshots) =>
        snapshots
            .Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
            .SelectMany(snapshot => snapshot.Packages)
            .Where(package => package.ProviderSuppliedStableProductKey is { } key && concreteKeys.Contains(key))
            .Select(package => (package.SourceId.ToUpperInvariant(), package.PackageId.ToUpperInvariant()))
            .ToHashSet();

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

    /// <summary>
    /// The source lists the matched package as installed with no newer version (its installed and available versions are the same),
    /// but the app's own version text differs, because the uninstall entry and the package number the same release differently
    /// (MEGAsync's entry says 6.7.0 where WinGet lists package 6.7.0.3). Nothing newer exists, so nothing would run: the comparison is
    /// treated as equal and the same-version rules above decide (current, not confirmed, or a conflict that still needs review). It
    /// can only take an update away, never offer one.
    /// </summary>
    private static PackageMapping UseSourceReportsNoNewerVersion(PackageMapping mapping, IReadOnlyCollection<SourceSnapshot> snapshots)
    {
        if (mapping.Origin is not (MappingOrigin.Automatic or MappingOrigin.ProviderSupplied) ||
            mapping.VersionComparison.Outcome == VersionComparisonOutcome.Equal ||
            mapping.ReviewReasons.Contains(MappingReviewReason.SourceDisagreement))
        {
            return mapping;
        }

        SourcePackageRecord[] packages = snapshots
            .Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
            .Where(snapshot => string.Equals(snapshot.SourceId, mapping.SourceId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(snapshot => snapshot.Packages)
            .Where(candidate => string.Equals(candidate.PackageId, mapping.PackageId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (packages is not [{ InstalledVersion: { } installed, AvailableVersion: { } available }] ||
            !string.Equals(installed, available, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(mapping.CandidateVersion, available, StringComparison.OrdinalIgnoreCase))
        {
            return mapping;
        }

        return mapping with
        {
            IsActionable = false,
            ReviewReasons = [.. mapping.ReviewReasons.Where(reason => reason is not (MappingReviewReason.CandidateIsOlder or MappingReviewReason.CandidateIsCurrent or MappingReviewReason.IncomparableVersion))],
            VersionComparison = new VersionComparisonResult(VersionComparisonOutcome.Equal, VersionComparisonExplanation.NumericDotted),
            EvidenceExplanation = $"The source lists package {mapping.PackageId} as installed at {installed} with no newer version, so there is nothing to update. This app numbers the same release differently."
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
