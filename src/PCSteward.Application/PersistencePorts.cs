using PCSteward.Domain;

namespace PCSteward.Application;

public interface IInventorySnapshotStore
{
    Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken);
}

public interface IStableDecisionStore
{
    Task SaveAsync(StableDecision decision, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string stableProductKey, CancellationToken cancellationToken);
}

public sealed record ScanDataSummary(long SizeBytes, DateTimeOffset? LastClearedAt, int ItemCount);
public sealed record ClearScanDataResult(bool Succeeded, int DeletedItems, int RemainingItems, string Message);

/// <summary>Owns only scan-derived records. It intentionally excludes settings, decisions, history and downloaded artifacts.</summary>
public interface IScanDataStore : IInventorySnapshotStore
{
    Task<ScanDataSummary> GetScanDataSummaryAsync(CancellationToken cancellationToken);
    Task<ClearScanDataResult> ClearScanDataAsync(CancellationToken cancellationToken);
}
