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

/// <summary>
/// The owner-facing trusted-sources control represents these two persisted records as one consent.
/// Legacy or interrupted asymmetric records therefore have one fail-closed effective state.
/// </summary>
public static class TrustedSourceConnectorConsent
{
    public const string InstalledInventoryConnector = "WinGet installed inventory";
    public const string PackageSourcesConnector = "WinGet package sources";

    public static ConnectorState EffectiveState(ConnectorState installedInventory, ConnectorState packageSources) =>
        new(
            installedInventory.Enabled && packageSources.Enabled,
            installedInventory.SourceDisclosureApproved && packageSources.SourceDisclosureApproved);

    public static bool IsEnabled(ConnectorState installedInventory, ConnectorState packageSources)
    {
        ConnectorState effective = EffectiveState(installedInventory, packageSources);
        return effective.Enabled && effective.SourceDisclosureApproved;
    }

    public static void Normalize(IDictionary<string, ConnectorState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        if (!states.TryGetValue(InstalledInventoryConnector, out ConnectorState? installedInventory) ||
            !states.TryGetValue(PackageSourcesConnector, out ConnectorState? packageSources)) return;

        ConnectorState effective = EffectiveState(installedInventory, packageSources);
        states[InstalledInventoryConnector] = effective;
        states[PackageSourcesConnector] = effective;
    }
}

public interface IConnectorSettingsStore
{
    Task<ConnectorState> GetConnectorStateAsync(string connectorName, bool defaultEnabled, CancellationToken cancellationToken);
    Task SaveConnectorStateAsync(string connectorName, ConnectorState state, CancellationToken cancellationToken);
    Task SaveConnectorStatesAsync(IReadOnlyDictionary<string, ConnectorState> states, CancellationToken cancellationToken);
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
public sealed record AdvancedDataClearResult(string Category, bool Succeeded, int DeletedItems, string Message);
public interface IAdvancedDataControlStore
{
    Task<AdvancedDataClearResult> ClearSourceMetadataAsync(CancellationToken cancellationToken);
    Task<AdvancedDataClearResult> ClearUpdateHistoryAsync(CancellationToken cancellationToken);
    Task<AdvancedDataClearResult> ClearMappingsAndPoliciesAsync(CancellationToken cancellationToken);
}

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
