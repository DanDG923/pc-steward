namespace PCSteward.Domain;

public enum SourceSnapshotState
{
    Live,
    Cached,
    LocalOnly,
    Unavailable
}

public sealed record SourcePackageRecord(
    string SourceId,
    string PackageId,
    string DisplayName,
    string? Publisher,
    string? InstalledVersion,
    string? AvailableVersion,
    string Scope,
    string Architecture,
    string? Channel,
    bool RequiresConnectorSpecificVersionSemantics = false,
    string? ProviderSuppliedStableProductKey = null);

public sealed record SourceSnapshot(
    string SourceId,
    string? ProviderVersion,
    DateTimeOffset? LastSuccessAt,
    SourceSnapshotState State,
    IReadOnlyCollection<SourcePackageRecord> Packages,
    IReadOnlyCollection<string> Warnings);

public enum UpdateIntelligenceStatus
{
    Current,
    VerifiedUpdate,
    ReviewRequired,
    Unmanaged,
    Pinned,
    Ignored,
    FailedUnavailable,

    /// <summary>The user skipped this available version (FR-POL-06). A newer version is offered again.</summary>
    Skipped
}

/// <summary>
/// Durable, reversible per-app choices. They can only restrict or annotate behaviour; none grants execution
/// authority. <see cref="PreferredProvider"/>, <see cref="AutomaticUpdates"/> and <see cref="CloseAppConsent"/>
/// are recorded now (FR-PRV-04, FR-AUT-01, FR-BAT-08) and consumed by later tasks.
/// </summary>
public sealed record ProductUpdatePolicy(
    bool IsPinned = false,
    bool IsIgnored = false,
    string? PinDescription = null,
    DateTimeOffset? IgnoreUntil = null,
    string? SkippedVersion = null,
    string? PreferredProvider = null,
    bool AutomaticUpdates = false,
    bool CloseAppConsent = false)
{
    public bool IsIgnoreActive(DateTimeOffset now) => IsIgnored && (IgnoreUntil is null || IgnoreUntil > now);
}

public sealed record UpdateIntelligenceItem(
    string StableProductKey,
    UpdateIntelligenceStatus Status,
    PackageMapping? Mapping,
    string Explanation,
    string? SourceId = null,
    DateTimeOffset? LastSuccessAt = null,
    IReadOnlyCollection<SourceCandidateObservation>? SourceCandidates = null);

public sealed record SourceCandidateObservation(
    string SourceId,
    string PackageId,
    string? CandidateVersion,
    string? Publisher,
    string Scope,
    string Architecture,
    string? Channel,
    SourceSnapshotState State,
    DateTimeOffset? LastSuccessAt,
    MappingConfidence Confidence,
    IReadOnlyCollection<MappingReviewReason> ReviewReasons);

public sealed record UpdateIntelligenceSummary(
    IReadOnlyCollection<UpdateIntelligenceItem> Items,
    IReadOnlyDictionary<UpdateIntelligenceStatus, int> Counts,
    IReadOnlyCollection<SourceSnapshot> Sources)
{
    public IReadOnlyCollection<UpdateIntelligenceItem> AttentionNeeded => Items.Where(item => item.Status is UpdateIntelligenceStatus.ReviewRequired or UpdateIntelligenceStatus.FailedUnavailable).ToArray();
}
