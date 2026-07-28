using System.Collections.Concurrent;
using System.Diagnostics;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record ProviderScanResult(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, IReadOnlyCollection<string> Warnings);
public interface ILocalInventoryProvider { string Name { get; } Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken); }
public sealed record ScanProgress(Guid CorrelationId, string Phase, TimeSpan Elapsed, string? Provider);
public sealed record ScanResult(Guid CorrelationId, bool Succeeded, bool Cancelled, IReadOnlyDictionary<string, TimeSpan> Timings, IReadOnlyCollection<string> Warnings);

public sealed class InventoryScanOrchestrator : IDisposable
{
    private readonly IInventorySnapshotStore store; private readonly IReadOnlyCollection<ILocalInventoryProvider> providers; private readonly SemaphoreSlim lease = new(1, 1);
    public InventoryScanOrchestrator(IInventorySnapshotStore store, IReadOnlyCollection<ILocalInventoryProvider> providers) { this.store = store; this.providers = providers; }
    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!await lease.WaitAsync(0, CancellationToken.None)) throw new InvalidOperationException("A full scan is already active.");
        Guid correlationId = Guid.NewGuid(); Stopwatch elapsed = Stopwatch.StartNew(); ConcurrentDictionary<string, TimeSpan> timings = []; ConcurrentBag<string> warnings = []; List<CanonicalProduct> products = []; List<InstallationInstance> instances = [];
        try
        {
            foreach (ILocalInventoryProvider provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested(); progress?.Report(new ScanProgress(correlationId, "Collecting", elapsed.Elapsed, provider.Name)); Stopwatch timing = Stopwatch.StartNew();
                try { ProviderScanResult result = await provider.ScanAsync(cancellationToken); products.AddRange(result.Products); instances.AddRange(result.Instances); foreach (string warning in result.Warnings) warnings.Add($"{provider.Name}:{warning}"); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { warnings.Add($"{provider.Name}:failed"); }
                finally { timings[provider.Name] = timing.Elapsed; }
            }
            progress?.Report(new ScanProgress(correlationId, "Publishing", elapsed.Elapsed, null)); await store.PromoteAsync(products, instances, cancellationToken); return new ScanResult(correlationId, true, false, timings, warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return new ScanResult(correlationId, false, true, timings, warnings); }
        finally { elapsed.Stop(); lease.Release(); }
    }
    public void Dispose() => lease.Dispose();
}
