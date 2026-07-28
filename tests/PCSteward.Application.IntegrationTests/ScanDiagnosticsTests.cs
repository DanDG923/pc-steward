using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ScanDiagnosticsTests
{
    [TestMethod]
    public async Task SuccessfulScanEmitsBoundedCorrelatedStructuredEvents()
    {
        BoundedScanEventSink sink = new(2); InventoryScanOrchestrator orchestrator = new(new Store(), [new Provider()], sink);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        ScanEvent[] events = sink.Snapshot().ToArray();
        Assert.IsTrue(result.Succeeded); Assert.HasCount(2, events); Assert.AreEqual("scan.provider.started", events[0].EventId); Assert.AreEqual("scan.completed", events[1].EventId); Assert.IsTrue(events.All(scanEvent => scanEvent.CorrelationId == result.CorrelationId)); Assert.IsTrue(events.All(scanEvent => scanEvent.Severity == ScanEventSeverity.Information));
    }
    private sealed class Store : IInventorySnapshotStore
    {
        public Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<CanonicalProduct>>([]);
        public Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<InstallationInstance>>([]);
    }
    private sealed class Provider : ILocalInventoryProvider
    {
        public string Name => "fixture"; public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { CanonicalProduct product = new(Guid.NewGuid(), "fixture", "Fixture"); return Task.FromResult(new ProviderScanResult([product], [new InstallationInstance(Guid.NewGuid(), product.Id, "fixture", "fixture", "1", "Machine", "x64")], [], [])); }
    }
}
