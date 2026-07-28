using System.Diagnostics;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Performance;

internal static class Program
{
    private static async Task<int> Main()
    {
        List<double> samples = [];
        for (int run = 0; run < 30; run++)
        {
            Stopwatch timer = Stopwatch.StartNew(); await using BenchmarkStore store = new(); using InventoryScanOrchestrator orchestrator = new(store, [new FixtureProvider(300)]);
            ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None); if (!result.Succeeded || store.Products.Count != 300) return 1;
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }
        samples.Sort(); Console.WriteLine($"Phase 1 deterministic product workload: runs=30 records=300 medianMs={samples[14]:F2} p95Ms={samples[28]:F2}. This is not clean-VM, UI, idle, or process-tree acceptance evidence.");
        return 0;
    }
    private sealed class FixtureProvider(int count) : ILocalInventoryProvider
    {
        public string Name => "fixture"; public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
        {
            List<CanonicalProduct> products = []; List<InstallationInstance> instances = [];
            for (int i = 0; i < count; i++) { Guid id = Guid.NewGuid(); products.Add(new CanonicalProduct(id, $"fixture-{i}", $"Fixture {i}")); instances.Add(new InstallationInstance(Guid.NewGuid(), id, "fixture", $"fixture-{i}", "1.0", "Machine", "x64")); }
            return Task.FromResult(new ProviderScanResult(products, instances, [], []));
        }
    }
    private sealed class BenchmarkStore : IInventorySnapshotStore, IAsyncDisposable
    {
        public List<CanonicalProduct> Products { get; } = [];
        public Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken) { Products.Clear(); Products.AddRange(products); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<CanonicalProduct>>(Products);
        public Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<InstallationInstance>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
