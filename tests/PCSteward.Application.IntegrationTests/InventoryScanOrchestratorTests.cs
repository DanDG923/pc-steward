using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class InventoryScanOrchestratorTests
{
    [TestMethod]
    public async Task LiveObservationPrecedesPersistenceAndCannotBeReplacedByDisplayRows()
    {
        PoisoningStore store = new();
        using InventoryScanOrchestrator orchestrator = new(store, [new FakeProvider("good", false)]);
        DateTimeOffset before = DateTimeOffset.UtcNow;
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        LiveInventoryObservation? observation = result.Observation;
        Assert.IsNotNull(observation);
        Assert.AreEqual(result.CorrelationId, observation.CorrelationId);
        Assert.IsTrue(observation.ObservedAt >= before && observation.ObservedAt <= DateTimeOffset.UtcNow);
        Assert.HasCount(1, observation.Products);
        Assert.HasCount(1, observation.Instances);
        Assert.IsEmpty(store.Products);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<CanonicalProduct>)observation.Products).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<InstallationInstance>)observation.Instances).Clear());
        Assert.IsEmpty(typeof(LiveInventoryObservation).GetConstructors());
    }

    private sealed class PoisoningStore : IInventorySnapshotStore
    {
        internal List<CanonicalProduct> Products { get; } = [];
        public Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken ct)
        {
            ((IList<CanonicalProduct>)products).Clear();
            ((IList<InstallationInstance>)instances).Clear();
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<CanonicalProduct>>(Products);
        public Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyCollection<InstallationInstance>>([]);
    }
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
        Assert.IsTrue(result.Cancelled); Assert.AreEqual(0, store.Products.Count); Assert.IsNull(result.Observation);
    }
    [TestMethod]
    public async Task TimedOutProviderDoesNotBlockAnotherProviderOrPublishPartialProviderData()
    {
        DelayedProvider slow = new(); FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [slow, new FakeProvider("good", false)]);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded); Assert.AreEqual(1, store.Products.Count); CollectionAssert.Contains(result.Warnings.ToArray(), "slow:timeout"); Assert.IsTrue(slow.Cancelled);
    }
    [TestMethod]
    public async Task DuplicateProviderProductsAreNormalizedWithoutAbortingTheScan()
    {
        FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [new DuplicateProvider()]);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded); Assert.AreEqual(1, store.Products.Count);
    }
    [TestMethod]
    public async Task ProviderFaultAfterTimeoutIsContainedAndOtherResultsPublish()
    {
        FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [new FaultAfterCancellationProvider(), new FakeProvider("good", false)]);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded); Assert.AreEqual(1, store.Products.Count); CollectionAssert.Contains(result.Warnings.ToArray(), "fault:timeout-failed");
    }
    [TestMethod]
    public async Task TimedOutProviderQuiescesBeforeTheScanPublishes()
    {
        UncooperativeButFiniteProvider slow = new(); FakeStore store = new(); InventoryScanOrchestrator orchestrator = new(store, [slow, new FakeProvider("good", false)]);
        ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(result.Succeeded); Assert.IsTrue(slow.Completed); Assert.AreEqual(1, store.Products.Count);
    }
    [TestMethod]
    public async Task UnresponsiveProviderBlocksReplacementScansUntilItStops()
    {
        UnresponsiveThenCompleteProvider slow = new(); FakeStore store = new(); using InventoryScanOrchestrator orchestrator = new(store, [slow], quiescenceTimeout: TimeSpan.FromMilliseconds(20));
        ScanResult timedOut = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsFalse(timedOut.Succeeded); CollectionAssert.Contains(timedOut.Warnings.ToArray(), "unresponsive:timeout-unresponsive"); Assert.IsNull(timedOut.Observation);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => orchestrator.ScanAsync(null, CancellationToken.None));
        await Task.Delay(180);
        ScanResult replacement = await orchestrator.ScanAsync(null, CancellationToken.None);
        Assert.IsTrue(replacement.Succeeded);
    }
    [TestMethod]
    public async Task ConcurrentScanIsRejectedAndMeasuredProviderConcurrencyIsOne()
    {
        ConcurrencyProbeProvider provider = new(); FakeStore store = new(); using InventoryScanOrchestrator orchestrator = new(store, [provider]);
        Task<ScanResult> first = orchestrator.ScanAsync(null, CancellationToken.None);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => orchestrator.ScanAsync(null, CancellationToken.None));
        provider.Release();
        Assert.IsTrue((await first).Succeeded); Assert.AreEqual(1, provider.MaximumActive);
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
        public bool Cancelled { get; private set; }
        public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { try { await Task.Delay(100, cancellationToken); } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { Cancelled = true; throw; } return new ProviderScanResult([], [], [], []); }
    }
    private sealed class DuplicateProvider : ILocalInventoryProvider
    {
        public string Name => "duplicate"; public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            Guid productId = Guid.NewGuid(); CanonicalProduct product = new(productId, "duplicate", "Duplicate");
            InstallationInstance first = new(Guid.NewGuid(), productId, "registry", "duplicate-key", "1", "Machine", "x64"); InstallationInstance second = new(Guid.NewGuid(), productId, "registry", "duplicate-key", "1", "Machine", "x64");
            return Task.FromResult(new ProviderScanResult([product, product], [first, second], [], []));
        }
    }
    private sealed class FaultAfterCancellationProvider : ILocalInventoryProvider
    {
        public string Name => "fault"; public TimeSpan Timeout => TimeSpan.FromMilliseconds(10);
        public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { try { await Task.Delay(100, cancellationToken); } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw new InvalidOperationException("fault after cancellation"); } return new ProviderScanResult([], [], [], []); }
    }
    private sealed class UncooperativeButFiniteProvider : ILocalInventoryProvider
    {
        public string Name => "finite"; public TimeSpan Timeout => TimeSpan.FromMilliseconds(10); public bool Completed { get; private set; }
        public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { await Task.Delay(80, CancellationToken.None); Completed = true; return new ProviderScanResult([], [], [], []); }
    }
    private sealed class UnresponsiveThenCompleteProvider : ILocalInventoryProvider
    {
        public string Name => "unresponsive"; public TimeSpan Timeout => TimeSpan.FromMilliseconds(10);
        private int calls;
        public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) { if (Interlocked.Increment(ref calls) == 1) await Task.Delay(100, CancellationToken.None); return new ProviderScanResult([], [], [], []); }
    }
    private sealed class ConcurrencyProbeProvider : ILocalInventoryProvider
    {
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int active;
        public string Name => "probe"; public TimeSpan Timeout => TimeSpan.FromSeconds(2); public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public int MaximumActive { get; private set; }
        public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            int current = Interlocked.Increment(ref active); MaximumActive = Math.Max(MaximumActive, current); Started.TrySetResult();
            try { await release.Task.WaitAsync(cancellationToken); return new ProviderScanResult([], [], [], []); }
            finally { Interlocked.Decrement(ref active); }
        }
        public void Release() => release.TrySetResult();
    }
}
