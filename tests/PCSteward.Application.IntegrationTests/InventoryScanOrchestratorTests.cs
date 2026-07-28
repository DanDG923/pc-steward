using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class InventoryScanOrchestratorTests
{
    [TestMethod]
    public async Task PublishesSuccessfulProvidersWhenOneProviderFails()
    {
        FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [new FakeProvider("good", false), new FakeProvider("bad", true)]);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded); Assert.AreEqual(1, store.Products.Count); CollectionAssert.Contains(result.Warnings.ToArray(), "bad:failed");
    }
    [TestMethod]
    public async Task CancellationDoesNotReplaceLastKnownGoodInventory()
    {
        FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [new FakeProvider("slow", false)]); using CancellationTokenSource cancellation = new(); cancellation.Cancel();
        ScanResult result = await orchestrator.ScanAsync(null, cancellation.Token);
        Assert.IsTrue(result.Cancelled); Assert.AreEqual(0, store.Products.Count);
    }
    [TestMethod]
    public async Task TimedOutProviderDoesNotBlockAnotherProviderOrPublishPartialProviderData()
    {
        FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [new DelayedProvider(), new FakeProvider("good", false)]);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded); Assert.AreEqual(1, store.Products.Count); CollectionAssert.Contains(result.Warnings.ToArray(), "slow:timeout");
    }
    private sealed class FakeStore : IInventorySnapshotStore
    {
        public List<CanonicalProduct> Products { get; } = [];
        public List<InstallationInstance> Instances { get; } = [];
        public Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken) { Products.Clear(); Products.AddRange(products); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<CanonicalProduct>>(Products);
        public Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<InstallationInstance>>(Instances);
    }
    private sealed class FakeProvider(string name, bool fail) : ILocalInventoryProvider
    {
        public string Name => name;
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); if (fail) throw new InvalidOperationException(); CanonicalProduct product = new(Guid.NewGuid(), name, name); InstallationInstance instance = new(Guid.NewGuid(), product.Id, name, name, "1", "Machine", "x64"); return Task.FromResult(new ProviderScanResult([product], [instance], [], [])); }
    }
    private sealed class DelayedProvider : ILocalInventoryProvider
    {
        public string Name => "slow";
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(20);
        public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { await Task.Delay(100, CancellationToken.None); return new ProviderScanResult([], [], [], []); }
    }
}
