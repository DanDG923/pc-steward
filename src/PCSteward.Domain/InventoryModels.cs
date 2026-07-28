namespace PCSteward.Domain;

public sealed record CanonicalProduct(Guid Id, string StableKey, string DisplayName);
public sealed record InstallationInstance(
    Guid Id,
    Guid ProductId,
    string Provider,
    string ProviderIdentity,
    string? Version,
    string Scope,
    string Architecture,
    string? Publisher = null,
    string? InstallDate = null,
    string? InstallPath = null,
    string? Channel = null,
    string Classification = "Unknown",
    string? SourceId = null);
public sealed record RawProviderRecord(Guid Id, Guid ScanRunId, string Provider, string RecordKey, string Payload);
public sealed record ScanRun(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt, string Status, Guid CorrelationId);
public sealed record ScanProviderTiming(Guid Id, Guid ScanRunId, string Provider, TimeSpan Duration, string? Warning);
public sealed record StableDecision(Guid Id, string StableProductKey, string Kind, string Value, DateTimeOffset CreatedAt, string? Reason = null, DateTimeOffset? ExpiresAt = null);
public sealed record ManualMapping(Guid Id, string StableProductKey, string SourceId, string PackageId, DateTimeOffset CreatedAt, string? Reason = null);
public sealed record ProductTimelineEntry(DateTimeOffset OccurredAt, string Kind, string Summary, string? Reason = null);
public sealed record DownloadedArtifact(Guid Id, string Category, string RelativePath, long SizeBytes, DateTimeOffset CreatedAt);
