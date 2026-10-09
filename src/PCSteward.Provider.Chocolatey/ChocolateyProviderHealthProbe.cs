using PCSteward.Application;
using PCSteward.Catalog;

namespace PCSteward.Provider.Chocolatey;

internal interface IChocolateyHealthClient
{
    ChocolateyHealthDiscovery Discover(bool requireCallerCannotModifyLayout);
    void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout);
    Task<ChocolateyProcessResult> ReadVersionAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken);
}

/// <summary>Strict trusted-client health; evidence grants neither source consent nor execution approval.</summary>
public sealed class ChocolateyProviderHealthProbe : IProviderHealthProbe
{
    private readonly IChocolateyHealthClient client;
    private readonly Func<DateTimeOffset> utcNow;

    public ChocolateyProviderHealthProbe() : this(new WindowsChocolateyHealthClient(), () => DateTimeOffset.UtcNow) { }
    internal ChocolateyProviderHealthProbe(IChocolateyHealthClient client, Func<DateTimeOffset> utcNow)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public Task<ProviderHealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Test-first scaffold: discovery is observable, but no client may run yet.
        try { _ = client.Discover(requireCallerCannotModifyLayout: true); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        return Task.FromResult(new ProviderHealthReport(CatalogProviderKind.Chocolatey, ProviderPresence.Unknown,
            ProviderTrust.Unknown, ProviderProbeState.Checking, null, [], utcNow(), "Probe implementation pending."));
    }
}

internal sealed class WindowsChocolateyHealthClient : IChocolateyHealthClient
{
    public ChocolateyHealthDiscovery Discover(bool requireCallerCannotModifyLayout) => new(ProviderPresence.Unknown, ProviderTrust.Unknown, null);
    public void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout) => throw new NotSupportedException();
    public Task<ChocolateyProcessResult> ReadVersionAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(ChocolateyClientIdentity client, CancellationToken cancellationToken) => throw new NotSupportedException();
}
