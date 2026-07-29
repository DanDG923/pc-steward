using PCSteward.Domain;

namespace PCSteward.Application;

public interface IInventorySnapshotStore
{
    Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<RawProviderRecord>> GetRawProviderRecordsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<RawProviderRecord>>([]);
    Task PersistScanAsync(ScanRun run, IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, IReadOnlyCollection<RawProviderRecord> rawRecords, IReadOnlyCollection<ScanProviderTiming> timings, CancellationToken cancellationToken) => PromoteAsync(products, instances, cancellationToken);
    Task RecordScanAsync(ScanRun run, IReadOnlyCollection<ScanProviderTiming> timings, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ConnectorState(bool Enabled, bool SourceDisclosureApproved);
public interface IConnectorSettingsStore
{
    Task<ConnectorState> GetConnectorStateAsync(string connectorName, bool defaultEnabled, CancellationToken cancellationToken);
    Task SaveConnectorStateAsync(string connectorName, ConnectorState state, CancellationToken cancellationToken);
}

public interface IStableDecisionStore
{
    Task SaveAsync(StableDecision decision, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string stableProductKey, CancellationToken cancellationToken);
}

/// <summary>Stable user choices are independent from disposable scan data and are never execution authority.</summary>
public interface IUserDecisionStore : IStableDecisionStore
{
    Task<IReadOnlyCollection<StableDecision>> GetAllAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Guid decisionId, CancellationToken cancellationToken);
    Task SaveManualMappingAsync(ManualMapping mapping, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken);
    Task DeleteManualMappingAsync(Guid mappingId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string stableProductKey, CancellationToken cancellationToken);
}

public interface ISourceSnapshotStore
{
    Task SaveSourceSnapshotAsync(SourceSnapshot snapshot, CancellationToken cancellationToken);
    Task<SourceSnapshot?> GetSourceSnapshotAsync(string sourceId, CancellationToken cancellationToken);
    Task PruneSourceSnapshotsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record ScanDataSummary(long SizeBytes, DateTimeOffset? LastClearedAt, int ItemCount);
public sealed record ClearScanDataResult(bool Succeeded, int DeletedItems, int RemainingItems, string Message);

/// <summary>Owns only scan-derived records. It intentionally excludes settings, decisions, history and downloaded artifacts.</summary>
public interface IScanDataStore : IInventorySnapshotStore
{
    Task<ScanDataSummary> GetScanDataSummaryAsync(CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetLastCompletedScanAtAsync(CancellationToken cancellationToken);
    Task<ClearScanDataResult> ClearScanDataAsync(CancellationToken cancellationToken);
}

/// <summary>Ledger entries are evidence only; the fixed cache root remains the deletion authority.</summary>
public interface IDownloadedArtifactStore
{
    Task SaveDownloadedArtifactAsync(DownloadedArtifact artifact, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DownloadedArtifact>> GetDownloadedArtifactsAsync(CancellationToken cancellationToken);
    Task DeleteDownloadedArtifactAsync(Guid artifactId, CancellationToken cancellationToken);
    Task<DateTimeOffset?> GetDownloadedArtifactsLastCleanedAtAsync(CancellationToken cancellationToken);
    Task SetDownloadedArtifactsLastCleanedAtAsync(DateTimeOffset cleanedAt, CancellationToken cancellationToken);
}
