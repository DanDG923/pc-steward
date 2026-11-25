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
    string? ProviderSuppliedStableProductKey = null,
    SourceInstallerFit InstallerFit = SourceInstallerFit.NoObjection);

/// <summary>
/// What the source said, when it checked for updates, about running this update's installer on PC Steward's reviewed route for the
/// app's install scope (owner's PC, 2026-11-19: an update shown as Ready was then refused at review for its installer type). Anything
/// but <see cref="NoObjection"/> holds the update back as a review item with the reason; <see cref="NoObjection"/> proves nothing, the
/// review still checks the installer. Stored as its number, so values are only appended.
/// </summary>
public enum SourceInstallerFit
{
    /// <summary>Nothing known against the installer (or the source doesn't say). The review decides.</summary>
    NoObjection = 0,

    /// <summary>The installer is of a kind the reviewed route for this scope can't run (for example a zip around an MSI).</summary>
    InstallerNotSupported = 1,

    /// <summary>The source has no installer for the scope the app is installed in (for example only every-user, installed for one account).</summary>
    NoInstallerForInstalledScope = 2,

    /// <summary>
    /// Owner decision 2026-11-25 (Discord): the source marks the package as one that updates itself (WinGet lists it only under "require
    /// explicit targeting", from the manifest's <c>RequireExplicitUpgrade</c>). The app's own updater decides its version and can switch
    /// back a version PC Steward installed, so the update is never offered; the app stays listed and says it updates itself.
    /// </summary>
    UpdatesItself = 3,
}

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
    /// <summary>
    /// Owner decision 2026-11-02 (FR-POL-01–05): the user chose to skip exactly this candidate version. Appended so existing
    /// ordinals are unchanged. Never actionable; a newer candidate than the skipped version is assessed normally again.
    /// </summary>
    Skipped
}

public sealed record ProductUpdatePolicy(bool IsPinned = false, bool IsIgnored = false, string? PinDescription = null, DateTimeOffset? IgnoreUntil = null,
    string? SkippedVersion = null)
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
