using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record ProviderScanResult(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, IReadOnlyCollection<RawProviderRecord> RawRecords, IReadOnlyCollection<string> Warnings);
public interface ILocalInventoryProvider { string Name { get; } TimeSpan Timeout { get; } Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken); }
public sealed record ScanProgress(Guid CorrelationId, string Phase, TimeSpan Elapsed, string? Provider);
public sealed record ScanResult(Guid CorrelationId, bool Succeeded, bool Cancelled, IReadOnlyDictionary<string, TimeSpan> Timings, IReadOnlyCollection<string> Warnings);

public sealed class InventoryScanOrchestrator : IDisposable
{
    private readonly IInventorySnapshotStore store; private readonly IReadOnlyCollection<ILocalInventoryProvider> providers; private readonly SemaphoreSlim lease = new(1, 1);
    public InventoryScanOrchestrator(IInventorySnapshotStore store, IReadOnlyCollection<ILocalInventoryProvider> providers) { this.store = store; this.providers = providers; }
    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!await lease.WaitAsync(0, CancellationToken.None)) throw new InvalidOperationException("A full scan is already active.");
        Guid correlationId = Guid.NewGuid(); Guid scanId = Guid.NewGuid(); DateTimeOffset startedAt = DateTimeOffset.UtcNow; Stopwatch elapsed = Stopwatch.StartNew(); Dictionary<string, TimeSpan> timings = []; List<string> warnings = []; List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> rawRecords = [];
        try
        {
            foreach (ILocalInventoryProvider provider in providers)
            {
                cancellationToken.ThrowIfCancellationRequested(); progress?.Report(new ScanProgress(correlationId, "Collecting", elapsed.Elapsed, provider.Name)); Stopwatch timing = Stopwatch.StartNew();
                using CancellationTokenSource providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<ProviderScanResult>? providerTask = null;
                try { providerTask = provider.ScanAsync(providerCancellation.Token); ProviderScanResult result = await providerTask.WaitAsync(provider.Timeout, cancellationToken); products.AddRange(result.Products); instances.AddRange(result.Instances); rawRecords.AddRange(result.RawRecords); foreach (string warning in result.Warnings) warnings.Add($"{provider.Name}:{warning}"); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (TimeoutException) { providerCancellation.Cancel(); if (providerTask is not null) { try { await providerTask.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None); } catch (OperationCanceledException) { } catch (TimeoutException) { warnings.Add($"{provider.Name}:timeout-unresponsive"); } } warnings.Add($"{provider.Name}:timeout"); }
                catch (Exception) { warnings.Add($"{provider.Name}:failed"); }
                finally { timings[provider.Name] = timing.Elapsed; }
            }
            (products, instances) = Normalize(products, instances);
            ScanRun run = new(scanId, startedAt, DateTimeOffset.UtcNow, "Completed", correlationId);
            ScanProviderTiming[] timingRows = timings.Select(pair => new ScanProviderTiming(Guid.NewGuid(), scanId, pair.Key, pair.Value, warnings.FirstOrDefault(warning => warning.StartsWith(pair.Key + ":", StringComparison.Ordinal)))).ToArray();
            RawProviderRecord[] rawRows = rawRecords.Select(record => record with { ScanRunId = scanId }).ToArray();
            progress?.Report(new ScanProgress(correlationId, "Publishing", elapsed.Elapsed, null)); await store.PersistScanAsync(run, products, instances, rawRows, timingRows, cancellationToken); return new ScanResult(correlationId, true, false, timings, warnings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { await store.RecordScanAsync(new ScanRun(scanId, startedAt, DateTimeOffset.UtcNow, "Cancelled", correlationId), timings.Select(pair => new ScanProviderTiming(Guid.NewGuid(), scanId, pair.Key, pair.Value, null)).ToArray(), CancellationToken.None); return new ScanResult(correlationId, false, true, timings, warnings); }
        finally { elapsed.Stop(); lease.Release(); }
    }
    private static (List<CanonicalProduct> Products, List<InstallationInstance> Instances) Normalize(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances)
    {
        Dictionary<Guid, CanonicalProduct> names = products.GroupBy(product => product.Id).ToDictionary(group => group.Key, group => group.OrderBy(product => product.DisplayName, StringComparer.Ordinal).ThenBy(product => product.StableKey, StringComparer.Ordinal).First());
        DeduplicationResult normalized = InventoryNormalization.NormalizeAndDeduplicate(instances.Select(instance => new RawInstallEvidence(instance.Provider, instance.ProviderIdentity, names[instance.ProductId].DisplayName, null, instance.Version, instance.Scope, instance.Architecture, null, "Unknown", instance.Id.ToString("N"))));
        List<CanonicalProduct> normalizedProducts = []; List<InstallationInstance> normalizedInstances = [];
        foreach (NormalizedInstall install in normalized.Instances)
        {
            Guid productId = StableGuid("product|" + install.ProductFingerprint); Guid instanceId = StableGuid("instance|" + install.InstanceFingerprint);
            normalizedProducts.Add(new CanonicalProduct(productId, install.ProductFingerprint, install.Evidence.DisplayName));
            normalizedInstances.Add(new InstallationInstance(instanceId, productId, install.Evidence.Provider, install.Evidence.ProviderIdentity, install.Evidence.Version, install.Evidence.Scope, install.Evidence.Architecture));
        }
        return (normalizedProducts.DistinctBy(product => product.Id).ToList(), normalizedInstances);
    }
    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    public void Dispose() => lease.Dispose();
}
