using PCSteward.Application;
using PCSteward.Catalog;

namespace PCSteward.Provider.WinGet;

internal interface IWingetHealthClient
{
    WingetHealthDiscovery Discover();
    Task<WingetProcessResult> ReadVersionAsync(string executable, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(string executable, CancellationToken cancellationToken);
}

/// <summary>Read-only provider evidence, independent of saved source consent.</summary>
public sealed class WingetProviderHealthProbe : IProviderHealthProbe
{
    private readonly IWingetHealthClient client;
    private readonly Func<DateTimeOffset> utcNow;

    public WingetProviderHealthProbe() : this(new WindowsWingetHealthClient(), () => DateTimeOffset.UtcNow) { }
    internal WingetProviderHealthProbe(IWingetHealthClient client, Func<DateTimeOffset> utcNow)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    public Task<ProviderHealthReport> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Test-first scaffold: discovery is observable, but no client may run yet.
        try { _ = client.Discover(); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { }
        return Task.FromResult(new ProviderHealthReport(CatalogProviderKind.WinGet, ProviderPresence.Unknown,
            ProviderTrust.Unknown, ProviderProbeState.Checking, null, [], utcNow(), "Probe implementation pending."));
    }
}

internal sealed class WindowsWingetHealthClient : IWingetHealthClient
{
    public WingetHealthDiscovery Discover() => new(ProviderPresence.Unknown, ProviderTrust.Unknown, null);
    public Task<WingetProcessResult> ReadVersionAsync(string executable, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> ReadConfiguredSourcesAsync(string executable, CancellationToken cancellationToken) => throw new NotSupportedException();
}
