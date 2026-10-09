using PCSteward.Catalog;

namespace PCSteward.Application;

public enum ProviderPresence { Unknown, Absent, Present }
public enum ProviderTrust { Unknown, Untrusted, Trusted }
public enum ProviderProbeState { Checking, Current, Partial, Unavailable }

/// <summary>Timestamped display evidence only; this report grants no operation or source consent.</summary>
public sealed record ProviderHealthReport(
    CatalogProviderKind Provider,
    ProviderPresence Presence,
    ProviderTrust Trust,
    ProviderProbeState State,
    string? Version,
    IReadOnlyList<string> ConfiguredSources,
    DateTimeOffset CheckedAt,
    string Reason);

public interface IProviderHealthProbe
{
    Task<ProviderHealthReport> ProbeAsync(CancellationToken cancellationToken);
}
