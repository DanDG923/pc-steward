namespace PCSteward.Domain;

public enum MappingConfidence
{
    None,
    Low,
    Medium,
    High
}

public enum MappingOrigin
{
    Automatic,
    ProviderSupplied,
    UserConfirmed,
    Imported
}

public enum MappingReviewReason
{
    None,
    Unmatched,
    MissingPackageId,
    UntrustedConnector,
    UnsupportedManualMapping,
    AmbiguousPublisher,
    PublisherConflict,
    ArchitectureConflict,
    ScopeConflict,
    ChannelConflict,
    IncomparableVersion,
    ConnectorSpecificVersionSemantics,
    CandidateIsCurrent,
    CandidateIsOlder,
    AmbiguousCandidate,
    SourceConflict,
    MissingSourceEvidence,
    MultipleInstallationInstanceAmbiguity,
    SourceDisagreement,
    MissingScopeEvidence,
    MissingArchitectureEvidence,
    MissingChannelEvidence
}

public sealed record ProductMatchProfile(
    string StableProductKey,
    string DisplayName,
    string? ProviderPackageId,
    string? PackageFamilyName,
    string? MsiProductCode,
    string? Publisher,
    string? InstalledVersion,
    string Scope,
    string Architecture,
    string? Channel,
    IReadOnlyCollection<string>? ProductAliases = null,
    IReadOnlyCollection<string>? PublisherAliases = null,
    string? InstalledSourceId = null,
    bool HasConflictingTargetInstances = false,
    // False only for a product known solely from WinGet's own installed list (no uninstall entry or MSIX package of its own).
    bool HasConcreteLocalEvidence = true);

public sealed record PackageMatchCandidate(
    string SourceId,
    string PackageId,
    string DisplayName,
    string? PackageFamilyName,
    string? MsiProductCode,
    string? Publisher,
    string? CandidateVersion,
    string Scope,
    string Architecture,
    string? Channel,
    bool IsTrustedConnector,
    string? ProviderSuppliedStableProductKey = null,
    bool RequiresConnectorSpecificVersionSemantics = false,
    IReadOnlyCollection<string>? ProductAliases = null,
    IReadOnlyCollection<string>? PublisherAliases = null);

public sealed record DeclaredPackageMapping(string StableProductKey, string SourceId, string PackageId, MappingOrigin Origin);

public sealed record ManualMappingValidation(bool IsValid, MappingReviewReason Reason, string Explanation);

public sealed record PackageMapping(
    string SourceId,
    string PackageId,
    string? CandidateVersion,
    MappingConfidence Confidence,
    MappingOrigin Origin,
    string EvidenceExplanation,
    bool IsActionable,
    IReadOnlyCollection<MappingReviewReason> ReviewReasons,
    VersionComparisonResult VersionComparison);

public sealed record ProductPackageMatchResult(PackageMapping? Mapping, IReadOnlyCollection<MappingReviewReason> ReviewReasons, string Explanation);

public static class ProductPackageMatching
{
    public static ProductPackageMatchResult Resolve(
        ProductMatchProfile product,
        IEnumerable<PackageMatchCandidate> candidates,
        IEnumerable<DeclaredPackageMapping>? declaredMappings = null,
        IEnumerable<string>? supportedTrustedSources = null)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(candidates);

        HashSet<string> supportedSources = (supportedTrustedSources ?? []).Select(Canonical).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
        IReadOnlyCollection<DeclaredPackageMapping> mappings = (declaredMappings ?? []).ToArray();
        List<PackageMapping> assessments = candidates
            .Where(candidate => candidate is not null)
            .OrderBy(candidate => Canonical(candidate.SourceId), StringComparer.Ordinal)
            .ThenBy(candidate => Canonical(candidate.PackageId), StringComparer.Ordinal)
            .Select(candidate => Assess(product, candidate, mappings, supportedSources))
            .Where(assessment => assessment.Confidence != MappingConfidence.None)
            .ToList();

        if (assessments.Count == 0)
        {
            return new(null, [MappingReviewReason.Unmatched], "No explicit identity or publisher-qualified product alias matched a trusted package.");
        }

        MappingConfidence strongest = assessments.Max(assessment => assessment.Confidence);
        List<PackageMapping> strongestMappings = assessments.Where(assessment => assessment.Confidence == strongest).ToList();
        if (strongestMappings.Count != 1)
        {
            return new(null, [MappingReviewReason.AmbiguousCandidate], "More than one package has the same strongest explicit evidence; review is required.");
        }

        PackageMapping selected = strongestMappings[0];
        return new(selected, selected.ReviewReasons, selected.EvidenceExplanation);
    }

    public static ManualMappingValidation ValidateManualMapping(DeclaredPackageMapping mapping, PackageMatchCandidate candidate, IEnumerable<string> supportedTrustedSources)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(supportedTrustedSources);

        HashSet<string> supportedSources = supportedTrustedSources.Select(Canonical).Where(value => value.Length > 0).ToHashSet(StringComparer.Ordinal);
        bool supported = supportedSources.Contains(Canonical(mapping.SourceId));
        bool targetMatches = Equal(mapping.SourceId, candidate.SourceId) && Equal(mapping.PackageId, candidate.PackageId);
        bool permittedOrigin = mapping.Origin is MappingOrigin.UserConfirmed or MappingOrigin.Imported;
        if (supported && candidate.IsTrustedConnector && targetMatches && permittedOrigin)
        {
            return new(true, MappingReviewReason.None, "The mapping targets an exact package ID from a supported trusted connector.");
        }

        return new(false, MappingReviewReason.UnsupportedManualMapping, "Manual mappings must target an exact package ID from a supported trusted connector.");
    }

    private static PackageMapping Assess(ProductMatchProfile product, PackageMatchCandidate candidate, IReadOnlyCollection<DeclaredPackageMapping> declaredMappings, HashSet<string> supportedSources)
    {
        if (Canonical(candidate.PackageId).Length == 0)
        {
            return Create(candidate, MappingConfidence.None, MappingOrigin.Automatic, "The candidate has no bounded package ID.", [MappingReviewReason.MissingPackageId], product);
        }

        (MappingConfidence confidence, MappingOrigin origin, string evidence, List<MappingReviewReason> reasons) = Identify(product, candidate, declaredMappings, supportedSources);
        if (confidence == MappingConfidence.None)
        {
            return Create(candidate, confidence, origin, evidence, reasons, product);
        }

        if (!candidate.IsTrustedConnector)
        {
            reasons.Add(MappingReviewReason.UntrustedConnector);
        }

        if (product.HasConflictingTargetInstances) reasons.Add(MappingReviewReason.MultipleInstallationInstanceAmbiguity);
        AddCompatibilityReasons(product, candidate, reasons);
        VersionComparisonResult comparison = VersionComparison.Compare(new(product.InstalledVersion, candidate.CandidateVersion, candidate.RequiresConnectorSpecificVersionSemantics));
        // A name-only candidate with incomplete publisher evidence has not established that its
        // version uses the same scheme as the installed product. Keep it review-only without
        // presenting an incidental installer version as a potential downgrade.
        bool versionEvidenceIsUsable = confidence >= MappingConfidence.Medium;
        if (versionEvidenceIsUsable) AddVersionReason(comparison, reasons);

        // Owner decision 2026-11-13: a unique match on the same normalized product name AND publisher (Medium) is as actionable as an
        // exact identity (High), when nothing else needs review. Near names, missing or conflicting publishers (Low) and two equally
        // strong candidates (AmbiguousCandidate in Resolve) still need the owner's confirmation.
        bool actionable = confidence >= MappingConfidence.Medium && candidate.IsTrustedConnector && reasons.Count == 0 && comparison.Outcome == VersionComparisonOutcome.Newer;
        return new(candidate.SourceId, candidate.PackageId, candidate.CandidateVersion, confidence, origin, Describe(evidence, comparison, reasons, versionEvidenceIsUsable), actionable, reasons.ToArray(), comparison);
    }

    private static (MappingConfidence Confidence, MappingOrigin Origin, string Evidence, List<MappingReviewReason> Reasons) Identify(ProductMatchProfile product, PackageMatchCandidate candidate, IReadOnlyCollection<DeclaredPackageMapping> declaredMappings, HashSet<string> supportedSources)
    {
        List<MappingReviewReason> reasons = [];
        DeclaredPackageMapping? mapping = declaredMappings.FirstOrDefault(item => Equal(item.StableProductKey, product.StableProductKey) && Equal(item.SourceId, candidate.SourceId) && Equal(item.PackageId, candidate.PackageId));
        if (mapping is not null)
        {
            ManualMappingValidation validation = ValidateManualMapping(mapping, candidate, supportedSources);
            if (validation.IsValid)
            {
                return (MappingConfidence.High, mapping.Origin, validation.Explanation, reasons);
            }

            reasons.Add(validation.Reason);
            return (MappingConfidence.Low, mapping.Origin, validation.Explanation, reasons);
        }

        if (candidate.IsTrustedConnector && Equal(candidate.ProviderSuppliedStableProductKey, product.StableProductKey))
        {
            return (MappingConfidence.High, MappingOrigin.ProviderSupplied, "The trusted connector supplied this exact canonical product key.", reasons);
        }

        if (Equal(product.ProviderPackageId, candidate.PackageId))
        {
            if (!Known(product.InstalledSourceId))
            {
                reasons.Add(MappingReviewReason.MissingSourceEvidence);
                return (MappingConfidence.Low, MappingOrigin.Automatic, "The installed package ID has no source provenance, so automatic matching is blocked.", reasons);
            }

            if (!Equal(product.InstalledSourceId, candidate.SourceId))
            {
                reasons.Add(MappingReviewReason.SourceConflict);
                return (MappingConfidence.Low, MappingOrigin.Automatic, "The package ID matches, but the candidate comes from a different trusted source.", reasons);
            }

            return (MappingConfidence.High, MappingOrigin.Automatic, "The package ID exactly matches the installed provider package ID.", reasons);
        }

        if (Equal(product.PackageFamilyName, candidate.PackageFamilyName))
        {
            return (MappingConfidence.High, MappingOrigin.Automatic, "The package family identity exactly matches.", reasons);
        }

        if (Equal(product.MsiProductCode, candidate.MsiProductCode))
        {
            return (MappingConfidence.High, MappingOrigin.Automatic, "The MSI product identity exactly matches.", reasons);
        }

        if (!AliasesOverlap(ProductAliases(product), ProductAliases(candidate)))
        {
            reasons.Add(MappingReviewReason.Unmatched);
            return (MappingConfidence.None, MappingOrigin.Automatic, "No explicit identity or normalized product alias matched.", reasons);
        }

        string[] productPublishers = PublisherAliases(product);
        string[] candidatePublishers = PublisherAliases(candidate);
        if (productPublishers.Length == 0 || candidatePublishers.Length == 0)
        {
            reasons.Add(MappingReviewReason.AmbiguousPublisher);
            return (MappingConfidence.Low, MappingOrigin.Automatic, "The product alias matched, but publisher evidence is incomplete.", reasons);
        }

        if (!AliasesOverlap(productPublishers, candidatePublishers))
        {
            reasons.Add(MappingReviewReason.PublisherConflict);
            return (MappingConfidence.Low, MappingOrigin.Automatic, "The product alias matched, but the publisher identities conflict.", reasons);
        }

        return (MappingConfidence.Medium, MappingOrigin.Automatic, "The normalized product and publisher aliases match without a stable package identity.", reasons);
    }

    private static PackageMapping Create(PackageMatchCandidate candidate, MappingConfidence confidence, MappingOrigin origin, string evidence, IReadOnlyCollection<MappingReviewReason> reasons, ProductMatchProfile product)
    {
        VersionComparisonResult comparison = VersionComparison.Compare(new(product.InstalledVersion, candidate.CandidateVersion, candidate.RequiresConnectorSpecificVersionSemantics));
        return new(candidate.SourceId, candidate.PackageId, candidate.CandidateVersion, confidence, origin, Describe(evidence, comparison, reasons, false), false, reasons.ToArray(), comparison);
    }

    private static void AddCompatibilityReasons(ProductMatchProfile product, PackageMatchCandidate candidate, List<MappingReviewReason> reasons)
    {
        // The installed side counts too: an update can only be bound to an installed scope and architecture that are known.
        if (!Known(candidate.Architecture) || !Known(product.Architecture)) reasons.Add(MappingReviewReason.MissingArchitectureEvidence);
        if (!Known(candidate.Scope) || !Known(product.Scope)) reasons.Add(MappingReviewReason.MissingScopeEvidence);
        if (!Known(candidate.Channel)) reasons.Add(MappingReviewReason.MissingChannelEvidence);
        if (Conflicts(product.Architecture, candidate.Architecture)) reasons.Add(MappingReviewReason.ArchitectureConflict);
        if (Conflicts(product.Scope, candidate.Scope)) reasons.Add(MappingReviewReason.ScopeConflict);
        if (Conflicts(product.Channel, candidate.Channel)) reasons.Add(MappingReviewReason.ChannelConflict);
    }

    private static void AddVersionReason(VersionComparisonResult comparison, List<MappingReviewReason> reasons)
    {
        switch (comparison.Outcome)
        {
            case VersionComparisonOutcome.Incomparable:
                reasons.Add(MappingReviewReason.IncomparableVersion);
                break;
            case VersionComparisonOutcome.ConnectorDeferred:
                reasons.Add(MappingReviewReason.ConnectorSpecificVersionSemantics);
                break;
            case VersionComparisonOutcome.Equal:
                reasons.Add(MappingReviewReason.CandidateIsCurrent);
                break;
            case VersionComparisonOutcome.Older:
                reasons.Add(MappingReviewReason.CandidateIsOlder);
                break;
        }
    }

    private static string Describe(string evidence, VersionComparisonResult comparison, IReadOnlyCollection<MappingReviewReason> reasons, bool versionEvidenceIsUsable)
    {
        string version = !versionEvidenceIsUsable
            ? "The candidate version is not used until package identity is verified."
            : comparison.Outcome switch
        {
            VersionComparisonOutcome.Newer => "Candidate version is newer.",
            VersionComparisonOutcome.Equal => "Candidate version is already installed.",
            VersionComparisonOutcome.Older => "Candidate version is older and is blocked.",
            VersionComparisonOutcome.ConnectorDeferred => "Version ordering requires connector-specific semantics.",
            _ => "Versions cannot be safely ordered."
        };
        string review = reasons.Count == 0 ? string.Empty : " Review required: " + string.Join(", ", reasons) + ".";
        return evidence + " " + version + review;
    }

    private static string[] ProductAliases(ProductMatchProfile profile) => Aliases(profile.DisplayName, profile.ProductAliases);
    private static string[] ProductAliases(PackageMatchCandidate candidate) => Aliases(candidate.DisplayName, candidate.ProductAliases);
    private static string[] PublisherAliases(ProductMatchProfile profile) => Aliases(profile.Publisher, profile.PublisherAliases);
    private static string[] PublisherAliases(PackageMatchCandidate candidate) => Aliases(candidate.Publisher, candidate.PublisherAliases);
    private static string[] Aliases(string? primary, IReadOnlyCollection<string>? aliases) => new[] { primary }.Concat(aliases ?? []).Select(NormalizeAlias).Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    private static bool AliasesOverlap(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right) => left.Any(value => right.Contains(value, StringComparer.Ordinal));
    private static bool Conflicts(string? left, string? right) => Known(left) && Known(right) && !Equal(left, right);
    private static bool Known(string? value) => Canonical(value) is not "" and not "UNKNOWN";
    private static bool Equal(string? left, string? right) => Canonical(left).Length > 0 && string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);
    private static string Canonical(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 256 ? string.Empty : value.Trim().ToUpperInvariant();
    private static string NormalizeAlias(string? value) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > 256 ? string.Empty : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
