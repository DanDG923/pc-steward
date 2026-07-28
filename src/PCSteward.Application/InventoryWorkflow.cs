using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record InventoryDashboard(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, DateTimeOffset? LastCompletedAt, ScanDataSummary ScanData, IReadOnlyDictionary<string, ConnectorState> Connectors);

/// <summary>Application use case for the inventory desktop. It owns no adapter implementation or UI type.</summary>
public sealed class InventoryWorkflow : IDisposable
{
    private const string WingetConnector = "WinGet installed inventory";
    private readonly IInventorySnapshotStore snapshots;
    private readonly IScanDataStore scanData;
    private readonly IConnectorSettingsStore connectorSettings;
    private readonly InventoryScanOrchestrator orchestrator;
    private readonly InventoryExportService exports;

    public InventoryWorkflow(IInventorySnapshotStore snapshots, IScanDataStore scanData, IConnectorSettingsStore connectorSettings, IReadOnlyCollection<ILocalInventoryProvider> providers, IScanEventSink? events = null)
    {
        this.snapshots = snapshots; this.scanData = scanData; this.connectorSettings = connectorSettings; orchestrator = new InventoryScanOrchestrator(snapshots, providers, events); exports = new InventoryExportService(snapshots);
    }

    public async Task<InventoryDashboard> LoadAsync(IReadOnlyCollection<ConnectorOption> connectors, CancellationToken cancellationToken)
    {
        Dictionary<string, ConnectorState> states = [];
        foreach (ConnectorOption connector in connectors)
        {
            ConnectorState state = await connectorSettings.GetConnectorStateAsync(connector.Name, connector.IsEnabled, cancellationToken).ConfigureAwait(false);
            if (connector.IsEssential) state = state with { Enabled = true };
            if (connector.Name == WingetConnector && !state.SourceDisclosureApproved) state = state with { Enabled = false };
            states.Add(connector.Name, state);
        }
        return new InventoryDashboard(
            await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false),
            await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false),
            await scanData.GetLastCompletedScanAtAsync(cancellationToken).ConfigureAwait(false),
            await scanData.GetScanDataSummaryAsync(cancellationToken).ConfigureAwait(false),
            states);
    }

    public async Task SaveConnectorStateAsync(string connectorName, bool enabled, bool sourceDisclosureApproved, CancellationToken cancellationToken)
    {
        if (connectorName == WingetConnector && enabled && !sourceDisclosureApproved) throw new InvalidOperationException("WinGet requires explicit source-disclosure approval.");
        await connectorSettings.SaveConnectorStateAsync(connectorName, new ConnectorState(enabled, sourceDisclosureApproved), cancellationToken);
    }

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) => orchestrator.ScanAsync(progress, cancellationToken);
    public Task<ClearScanDataResult> ClearScanDataAsync(CancellationToken cancellationToken) => scanData.ClearScanDataAsync(cancellationToken);
    public Task<InventoryExport> CreateExportAsync(bool json, CancellationToken cancellationToken) => json ? exports.CreateJsonAsync(true, cancellationToken) : exports.CreateCsvAsync(cancellationToken);
    public async Task<IReadOnlyCollection<RawProviderRecord>> GetRawDiagnosticsAsync(Guid productId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken);
        HashSet<(string Provider, string Identity)> keys = instances.Where(instance => instance.ProductId == productId).Select(instance => (instance.Provider, instance.ProviderIdentity)).ToHashSet();
        return (await snapshots.GetRawProviderRecordsAsync(cancellationToken)).Where(record => keys.Contains((record.Provider, record.RecordKey))).ToArray();
    }
    public void Dispose() => orchestrator.Dispose();
}
