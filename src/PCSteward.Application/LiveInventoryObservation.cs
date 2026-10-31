using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Direct in-memory provider observation. Presence does not establish completeness, capability or approval.</summary>
public sealed class LiveInventoryObservation
{
    internal LiveInventoryObservation(Guid correlationId, DateTimeOffset observedAt, IEnumerable<CanonicalProduct> products,
        IEnumerable<InstallationInstance> instances, IEnumerable<string> warnings)
    {
        if (correlationId == Guid.Empty || observedAt == default) throw new ArgumentException("A live scan identity and observation time are required.");
        ArgumentNullException.ThrowIfNull(products);
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(warnings);
        CorrelationId = correlationId;
        ObservedAt = observedAt.ToUniversalTime();
        Products = Array.AsReadOnly(products.ToArray());
        Instances = Array.AsReadOnly(instances.ToArray());
        Warnings = Array.AsReadOnly(warnings.ToArray());
    }
    public Guid CorrelationId { get; }
    public DateTimeOffset ObservedAt { get; }
    public IReadOnlyList<CanonicalProduct> Products { get; }
    public IReadOnlyList<InstallationInstance> Instances { get; }
    public IReadOnlyList<string> Warnings { get; }
}
