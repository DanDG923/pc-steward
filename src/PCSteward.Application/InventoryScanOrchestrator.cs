using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record ProviderScanResult(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, IReadOnlyCollection<RawProviderRecord> RawRecords, IReadOnlyCollection<string> Warnings);
public interface ILocalInventoryProvider { string Name { get; } TimeSpan Timeout { get; } Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken); }
/// <summary>
/// One progress report from a scan. <see cref="Step"/> of <see cref="Steps"/> counts from 1: one step for each place the scan
/// reads, then one for saving what it found. Both are 0 when the reporter does not count steps.
/// </summary>
public sealed record ScanProgress(Guid CorrelationId, string Phase, TimeSpan Elapsed, string? Provider, int Step = 0, int Steps = 0);
public sealed record ScanResult(Guid CorrelationId, bool Succeeded, bool Cancelled, IReadOnlyDictionary<string, TimeSpan> Timings, IReadOnlyCollection<string> Warnings, LiveInventoryObservation? Observation = null);

public sealed class InventoryScanOrchestrator : IDisposable
{
    private readonly IInventorySnapshotStore store; private readonly IReadOnlyCollection<ILocalInventoryProvider> providers; private readonly IScanEventSink events; private readonly TimeSpan quiescenceTimeout; private readonly SemaphoreSlim lease = new(1, 1);
    public InventoryScanOrchestrator(IInventorySnapshotStore store, IReadOnlyCollection<ILocalInventoryProvider> providers, IScanEventSink? events = null, TimeSpan? quiescenceTimeout = null) { this.store = store; this.providers = providers; this.events = events ?? NullScanEventSink.Instance; this.quiescenceTimeout = quiescenceTimeout ?? TimeSpan.FromSeconds(4); }
    public async Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        if (!await lease.WaitAsync(0, CancellationToken.None)) throw new InvalidOperationException("A full scan is already active.");
        bool releaseLease = true;
        Guid correlationId = Guid.NewGuid(); Guid scanId = Guid.NewGuid(); DateTimeOffset startedAt = DateTimeOffset.UtcNow; Stopwatch elapsed = Stopwatch.StartNew(); Dictionary<string, TimeSpan> timings = []; List<string> warnings = []; List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> rawRecords = [];
        try
        {
            int step = 0, steps = providers.Count + 1;
            foreach (ILocalInventoryProvider provider in providers)
            {
                step++;
                cancellationToken.ThrowIfCancellationRequested(); events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Information, "scan.provider.started", provider.Name, "Provider collection started.")); progress?.Report(new ScanProgress(correlationId, "Collecting", elapsed.Elapsed, provider.Name, step, steps)); Stopwatch timing = Stopwatch.StartNew();
                using CancellationTokenSource providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task<ProviderScanResult>? providerTask = null;
                try { providerTask = provider.ScanAsync(providerCancellation.Token); ProviderScanResult result = await providerTask.WaitAsync(provider.Timeout, cancellationToken); products.AddRange(result.Products); instances.AddRange(result.Instances); rawRecords.AddRange(result.RawRecords); foreach (string warning in result.Warnings) warnings.Add($"{provider.Name}:{warning}"); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (TimeoutException)
                {
                    providerCancellation.Cancel();
                    if (providerTask is not null)
                    {
                        try { await providerTask.WaitAsync(quiescenceTimeout, CancellationToken.None); }
                        catch (OperationCanceledException) { }
                        catch (TimeoutException)
                        {
                            warnings.Add($"{provider.Name}:timeout-unresponsive");
                            events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Error, "scan.provider.timeout_unresponsive", provider.Name, "Provider did not quiesce after cancellation; a replacement scan is blocked until it stops."));
                            releaseLease = false;
                            _ = ObserveUnresponsiveProviderAsync(providerTask, provider.Name, correlationId);
                        }
                        catch (Exception)
                        {
                            warnings.Add($"{provider.Name}:timeout-failed");
                            events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Warning, "scan.provider.timeout_failed", provider.Name, "Provider faulted after timeout."));
                        }
                    }
                    warnings.Add($"{provider.Name}:timeout");
                    events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Warning, "scan.provider.timeout", provider.Name, "Provider timed out and was cancelled."));
                }
                catch (Exception) { warnings.Add($"{provider.Name}:failed"); events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Error, "scan.provider.failed", provider.Name, "Provider failed.")); }
                finally { timings[provider.Name] = timing.Elapsed; }
            }
            if (warnings.Any(warning => warning.EndsWith(":timeout-unresponsive", StringComparison.Ordinal))) { await store.RecordScanAsync(new ScanRun(scanId, startedAt, DateTimeOffset.UtcNow, "Failed", correlationId), timings.Select(pair => new ScanProviderTiming(Guid.NewGuid(), scanId, pair.Key, pair.Value, warnings.FirstOrDefault(warning => warning.StartsWith(pair.Key + ":", StringComparison.Ordinal)))).ToArray(), CancellationToken.None); return new ScanResult(correlationId, false, false, timings, warnings); }
            (products, instances) = Normalize(products, instances);
            LiveInventoryObservation observation = new(correlationId, DateTimeOffset.UtcNow, products, instances, warnings);
            ScanRun run = new(scanId, startedAt, DateTimeOffset.UtcNow, "Completed", correlationId);
            ScanProviderTiming[] timingRows = timings.Select(pair => new ScanProviderTiming(Guid.NewGuid(), scanId, pair.Key, pair.Value, warnings.FirstOrDefault(warning => warning.StartsWith(pair.Key + ":", StringComparison.Ordinal)))).ToArray();
            RawProviderRecord[] rawRows = rawRecords.Select(record => record with { ScanRunId = scanId }).ToArray();
            progress?.Report(new ScanProgress(correlationId, "Publishing", elapsed.Elapsed, null, steps, steps)); await store.PersistScanAsync(run, products, instances, rawRows, timingRows, cancellationToken); events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Information, "scan.completed", null, "Scan completed.")); return new ScanResult(correlationId, true, false, timings, warnings, observation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { await store.RecordScanAsync(new ScanRun(scanId, startedAt, DateTimeOffset.UtcNow, "Cancelled", correlationId), timings.Select(pair => new ScanProviderTiming(Guid.NewGuid(), scanId, pair.Key, pair.Value, null)).ToArray(), CancellationToken.None); return new ScanResult(correlationId, false, true, timings, warnings); }
        finally { elapsed.Stop(); if (releaseLease) lease.Release(); }
    }
    private async Task ObserveUnresponsiveProviderAsync(Task providerTask, string providerName, Guid correlationId)
    {
        try { await providerTask.ConfigureAwait(false); events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Warning, "scan.provider.late_completion", providerName, "Provider completed after the scan was failed.")); }
        catch (OperationCanceledException) { events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Information, "scan.provider.late_cancellation", providerName, "Provider observed cancellation after the scan was failed.")); }
        catch (Exception) { events.Write(new ScanEvent(correlationId, DateTimeOffset.UtcNow, ScanEventSeverity.Error, "scan.provider.late_failure", providerName, "Provider faulted after the scan was failed.")); }
        finally { try { lease.Release(); } catch (ObjectDisposedException) { } }
    }
    private static (List<CanonicalProduct> Products, List<InstallationInstance> Instances) Normalize(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances)
    {
        Dictionary<Guid, CanonicalProduct> names = products.GroupBy(product => product.Id).ToDictionary(group => group.Key, group => group.OrderBy(product => product.DisplayName, StringComparer.Ordinal).ThenBy(product => product.StableKey, StringComparer.Ordinal).First());
        DeduplicationResult normalized = InventoryNormalization.NormalizeAndDeduplicate(instances.Select(instance => new RawInstallEvidence(instance.Provider, instance.ProviderIdentity, names[instance.ProductId].DisplayName, instance.Publisher, instance.Version, instance.Scope, instance.Architecture, instance.Channel, instance.Classification, instance.Id.ToString("N"), instance.SourceId)));
        List<CanonicalProduct> normalizedProducts = []; List<InstallationInstance> normalizedInstances = [];
        foreach (NormalizedInstall install in normalized.Instances)
        {
            Guid productId = StableGuid("product|" + install.ProductFingerprint); Guid instanceId = StableGuid("instance|" + install.InstanceFingerprint);
            normalizedProducts.Add(new CanonicalProduct(productId, install.ProductFingerprint, install.Evidence.DisplayName));
            InstallationInstance? original = instances.FirstOrDefault(candidate => candidate.Provider == install.Evidence.Provider && candidate.ProviderIdentity == install.Evidence.ProviderIdentity && candidate.Scope == install.Evidence.Scope && candidate.Architecture == install.Evidence.Architecture && candidate.Version == install.Evidence.Version && candidate.Channel == install.Evidence.Channel && candidate.SourceId == install.Evidence.SourceId);
            normalizedInstances.Add(new InstallationInstance(instanceId, productId, install.Evidence.Provider, install.Evidence.ProviderIdentity, install.Evidence.Version, install.Evidence.Scope, install.Evidence.Architecture, install.Evidence.Publisher, original?.InstallDate, original?.InstallPath, install.Evidence.Channel, install.Evidence.Classification, original?.SourceId));
        }
        return (normalizedProducts.DistinctBy(product => product.Id).ToList(), normalizedInstances);
    }
    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
    public void Dispose() => lease.Dispose();
}
