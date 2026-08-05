using System.Diagnostics;
using System.Text.Json;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Performance;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main()
    {
        string? outputPath = GetOutputPath();
        List<double> samples = [];
        for (int run = 0; run < 30; run++)
        {
            Stopwatch timer = Stopwatch.StartNew(); await using BenchmarkStore store = new(); using InventoryScanOrchestrator orchestrator = new(store, [new FixtureProvider(300)]);
            ScanResult result = await orchestrator.ScanAsync(null, CancellationToken.None); if (!result.Succeeded || store.Products.Count != 300) return 1;
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }
        samples.Sort(); Console.WriteLine($"Phase 1 deterministic product workload: runs=30 records=300 medianMs={samples[14]:F2} p95Ms={samples[28]:F2}. This is not final-app UI, idle, or process-tree acceptance evidence; a VM is optional.");
        List<double> online = [];
        for (int run = 0; run < 20; run++)
        {
            Stopwatch timer = Stopwatch.StartNew(); UpdateSourceRefreshService service = new(new SourceCache()); IReadOnlyCollection<SourceSnapshot> snapshots = await service.RefreshAsync([new FixtureSource("winget"), new FixtureSource("msstore")], true, CancellationToken.None);
            if (snapshots.Count != 2 || snapshots.Any(snapshot => snapshot.State != SourceSnapshotState.Live)) return 1;
            online.Add(timer.Elapsed.TotalMilliseconds);
        }
        online.Sort(); Console.WriteLine($"Task 18 fixture online-check workload: runs=20 sources=2 medianMs={online[9]:F2} p95Ms={online[18]:F2}. Fixture connectors only; no live WinGet or Store source was contacted.");
        List<double> inventoryViews = [];
        for (int run = 0; run < 20; run++)
        {
            (CanonicalProduct[] products, InstallationInstance[] instances) = BuildInventoryFixture(2_500);
            InventoryExperienceViewModel viewModel = new();
            Stopwatch timer = Stopwatch.StartNew();
            viewModel.LoadInventory(products, instances, DateTimeOffset.UtcNow);
            viewModel.SetFilters("Fixture 19", "All statuses", "All publishers", "All sources", "All scopes", "Publisher");
            timer.Stop();
            if (viewModel.VisibleRows.Count == 0) return 1;
            inventoryViews.Add(timer.Elapsed.TotalMilliseconds);
        }
        inventoryViews.Sort();
        Console.WriteLine($"Task 35 large-inventory presentation workload: runs=20 records=2500 medianMs={inventoryViews[9]:F2} p95Ms={inventoryViews[18]:F2}. Application-layer load, search, filter, and sort only; the production WinUI list is structurally virtualized but UI frame timing requires an interactive desktop trace.");
        if (outputPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            PerformanceResult result = new(
                DateTimeOffset.UtcNow,
                Environment.MachineName,
                Environment.OSVersion.VersionString,
                Environment.ProcessorCount,
                Environment.Version.ToString(),
                samples,
                online,
                inventoryViews,
                "Fixture-only harness. Large-inventory samples cover application-layer load, search, filter, and sort; they do not represent WinUI frame timing, real update sources, five-minute idle activity, or process-tree resource use.");
            await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, JsonOptions));
            Console.WriteLine($"Raw machine-readable performance result: {outputPath}");
        }
        return 0;
    }

    private static string? GetOutputPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        int outputIndex = Array.IndexOf(arguments, "--output");
        if (outputIndex < 0) return null;
        if (outputIndex == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[outputIndex + 1])) throw new ArgumentException("--output requires a file path.");
        return Path.GetFullPath(arguments[outputIndex + 1]);
    }

    private sealed record PerformanceResult(
        DateTimeOffset CapturedAtUtc,
        string MachineName,
        string OperatingSystem,
        int LogicalProcessorCount,
        string Runtime,
        IReadOnlyList<double> LocalInventoryMilliseconds,
        IReadOnlyList<double> OnlineCheckMilliseconds,
        IReadOnlyList<double> InventoryViewMilliseconds,
        string ScopeNote);

    private static (CanonicalProduct[] Products, InstallationInstance[] Instances) BuildInventoryFixture(int count)
    {
        CanonicalProduct[] products = new CanonicalProduct[count];
        InstallationInstance[] instances = new InstallationInstance[count];
        for (int index = 0; index < count; index++)
        {
            Guid productId = Guid.NewGuid();
            products[index] = new CanonicalProduct(productId, $"fixture:{index}", $"Fixture {index}");
            instances[index] = new InstallationInstance(Guid.NewGuid(), productId, index % 3 == 0 ? "winget" : "registry", $"Fixture.Package.{index}", $"{index % 12 + 1}.0", index % 2 == 0 ? "Machine" : "User", "x64", index % 5 == 0 ? "Contoso" : "Fabrikam");
        }
        return (products, instances);
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
    private sealed class SourceCache : ISourceSnapshotStore
    {
        private readonly Dictionary<string, SourceSnapshot> entries = [];
        public Task SaveSourceSnapshotAsync(SourceSnapshot snapshot, CancellationToken cancellationToken) { entries[snapshot.SourceId] = snapshot; return Task.CompletedTask; }
        public Task<SourceSnapshot?> GetSourceSnapshotAsync(string sourceId, CancellationToken cancellationToken) => Task.FromResult(entries.GetValueOrDefault(sourceId));
    }
    private sealed class FixtureSource(string sourceId) : IUpdateSourceConnector
    {
        public string SourceId { get; } = sourceId; public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public async Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken) { await Task.Delay(5, cancellationToken); return new(SourceId, "fixture-1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [new(SourceId, "Fixture." + SourceId, "Fixture " + SourceId, null, "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []); }
    }
}
