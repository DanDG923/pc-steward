using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Applies durable user choices without weakening package matching or creating update execution authority.</summary>
public sealed class UserDecisionService
{
    private const int MaximumReasonLength = 512;
    private readonly IUserDecisionStore store;

    public UserDecisionService(IUserDecisionStore store) => this.store = store;

    public async Task<ManualMappingValidation> CreateManualMappingAsync(string stableProductKey, SourcePackageRecord package, IReadOnlyCollection<SourceSnapshot> snapshots, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableProductKey); ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(snapshots);
        bool supplied = snapshots.Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached).SelectMany(snapshot => snapshot.Packages).Any(candidate => Same(candidate.SourceId, package.SourceId) && Same(candidate.PackageId, package.PackageId));
        DeclaredPackageMapping declared = new(stableProductKey, package.SourceId, package.PackageId, MappingOrigin.UserConfirmed);
        PackageMatchCandidate candidate = new(package.SourceId, package.PackageId, package.DisplayName, null, null, package.Publisher, package.AvailableVersion, package.Scope, package.Architecture, package.Channel, true, package.ProviderSuppliedStableProductKey, package.RequiresConnectorSpecificVersionSemantics);
        ManualMappingValidation validation = supplied ? ProductPackageMatching.ValidateManualMapping(declared, candidate, snapshots.Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached).Select(snapshot => snapshot.SourceId)) : new(false, MappingReviewReason.UnsupportedManualMapping, "Manual mappings must select an exact package ID currently supplied by an enabled trusted connector.");
        if (!validation.IsValid) return validation;
        bool alreadyMapped = (await store.GetManualMappingsAsync(cancellationToken))
            .Any(mapping => Same(mapping.StableProductKey, stableProductKey) && Same(mapping.SourceId, package.SourceId) && Same(mapping.PackageId, package.PackageId));
        if (alreadyMapped) return validation;
        await store.SaveManualMappingAsync(new ManualMapping(Guid.NewGuid(), stableProductKey, package.SourceId, package.PackageId, DateTimeOffset.UtcNow, BoundedReason(reason)), cancellationToken);
        return validation;
    }

    public Task PinCurrentVersionAsync(string stableProductKey, string currentVersion, string? reason, CancellationToken cancellationToken) => SaveDecisionAsync(stableProductKey, "PinCurrentVersion", currentVersion, reason, null, cancellationToken);
    public Task PinSourcePackageAsync(string stableProductKey, string sourceId, string packageId, string? reason, CancellationToken cancellationToken) => SaveDecisionAsync(stableProductKey, "PinSourcePackage", $"{sourceId}:{packageId}", reason, null, cancellationToken);

    public Task IgnoreAsync(string stableProductKey, DateTimeOffset? until, string? reason, CancellationToken cancellationToken)
    {
        if (until is not null && until <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(until), "An ignore-until date must be in the future.");
        return SaveDecisionAsync(stableProductKey, "Ignore", until is null ? "Indefinitely" : until.Value.ToString("O"), reason, until, cancellationToken);
    }

    public Task ReverseDecisionAsync(Guid decisionId, CancellationToken cancellationToken) => store.DeleteAsync(decisionId, cancellationToken);
    public Task DeleteManualMappingAsync(Guid mappingId, CancellationToken cancellationToken) => store.DeleteManualMappingAsync(mappingId, cancellationToken);
    public async Task<IReadOnlyCollection<StableDecision>> GetActiveDecisionsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return (await store.GetAllAsync(cancellationToken)).Where(decision => decision.Kind != "Ignore" || decision.ExpiresAt is null || decision.ExpiresAt > now).ToArray();
    }
    public Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken) => store.GetManualMappingsAsync(cancellationToken);
    public async Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ProductTimelineEntry> decisions = await store.GetTimelineAsync(stableProductKey, cancellationToken);
        IReadOnlyCollection<ProductTimelineEntry> mappings = (await store.GetManualMappingsAsync(cancellationToken))
            .Where(mapping => string.Equals(mapping.StableProductKey, stableProductKey, StringComparison.Ordinal))
            .Select(mapping => new ProductTimelineEntry(mapping.CreatedAt, "ManualMapping", $"Manual mapping: {mapping.SourceId}:{mapping.PackageId}", mapping.Reason))
            .ToArray();
        return decisions.Concat(mappings).OrderByDescending(entry => entry.OccurredAt).ToArray();
    }

    public async Task<IReadOnlyDictionary<string, ProductUpdatePolicy>> GetPoliciesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow; Dictionary<string, ProductUpdatePolicy> policies = [];
        foreach (IGrouping<string, StableDecision> group in (await store.GetAllAsync(cancellationToken)).GroupBy(decision => decision.StableProductKey, StringComparer.Ordinal))
        {
            StableDecision? pin = group.Where(decision => decision.Kind is "PinCurrentVersion" or "PinSourcePackage").OrderByDescending(decision => decision.CreatedAt).FirstOrDefault();
            StableDecision? ignore = group.Where(decision => decision.Kind == "Ignore" && (decision.ExpiresAt is null || decision.ExpiresAt > now)).OrderByDescending(decision => decision.CreatedAt).FirstOrDefault();
            policies[group.Key] = new(pin is not null, ignore is not null, pin is null ? null : $"The product is pinned to {pin.Value} by a recorded user decision.", ignore?.ExpiresAt);
        }
        return policies;
    }

    private Task SaveDecisionAsync(string stableProductKey, string kind, string value, string? reason, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableProductKey); ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return store.SaveAsync(new StableDecision(Guid.NewGuid(), stableProductKey, kind, value, DateTimeOffset.UtcNow, BoundedReason(reason), expiresAt), cancellationToken);
    }

    private static string? BoundedReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, MaximumReasonLength)];
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
