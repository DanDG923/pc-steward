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
