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

public interface ISourceSnapshotStore
{
    Task SaveSourceSnapshotAsync(SourceSnapshot snapshot, CancellationToken cancellationToken);
    Task<SourceSnapshot?> GetSourceSnapshotAsync(string sourceId, CancellationToken cancellationToken);
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
