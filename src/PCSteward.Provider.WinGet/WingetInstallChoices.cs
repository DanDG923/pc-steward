using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Provider.WinGet;

/// <summary>FR-INS-02/04: reuse strict live COM filtering for supported choices on this x64 PC.</summary>
public sealed class WingetInstallChoiceAuthority : IInstallChoiceAuthority
{
    // Process-lifetime gate also bounds overlapping discovery calls. A timed-out COM call retains its slot until it exits.
    private static readonly SemaphoreSlim DiscoverySlots = new(2, 2);
    private static readonly UpdateArchitecture[] SupportedArchitectures = [UpdateArchitecture.X64, UpdateArchitecture.X86, UpdateArchitecture.Neutral];
    private readonly IInstallPreflightAuthority preflight;

    public WingetInstallChoiceAuthority() => preflight = new WingetInstallPreflightAuthority(new BoundedCatalogQuery(new WinGetComInstallCatalogQuery()), TimeSpan.FromSeconds(30));
    internal WingetInstallChoiceAuthority(IInstallPreflightAuthority preflight) => this.preflight = new BoundedPreflight(preflight ?? throw new ArgumentNullException(nameof(preflight)));
    internal WingetInstallChoiceAuthority(IWinGetInstallCatalogQuery query, TimeSpan timeout) => preflight = new WingetInstallPreflightAuthority(new BoundedCatalogQuery(query), timeout);

    public async Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        if (!entry.Providers.Contains(provider) || provider is not { Provider: CatalogProviderKind.WinGet, Source: CatalogSource.WinGet } || entry.Channel != CatalogChannel.Stable || ChocolateySetupPreparation.IsReservedCatalogEntry(entry.Id)) return [];
        InstallPreflightRequest[] requests = [.. provider.Scopes.SelectMany(scope => SupportedArchitectures.Select(architecture =>
            new InstallPreflightRequest(entry.Id, UpdateProvider.WinGet, UpdateSource.WinGet, provider.PackageId, scope == CatalogScope.Machine ? UpdateScope.Machine : UpdateScope.User, architecture)))];
        InstallPreflightResult[] results = await Task.WhenAll(requests.Select(request => preflight.PreflightAsync(request, cancellationToken))).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        List<InstallPreflightResult> choices = [];
        for (int index = 0; index < results.Length; index++)
        {
            InstallPreflightResult result = results[index];
            InstallPreflightRequest request = requests[index];
            if (result is not { IsActionable: true, Selection: { } selection } || selection.Operation != PackageOperationKind.Install ||
                selection.StableProductKey != entry.Id || selection.Provider != UpdateProvider.WinGet || selection.Source != UpdateSource.WinGet ||
                !string.Equals(selection.PackageId, provider.PackageId, StringComparison.OrdinalIgnoreCase) || selection.Channel != UpdateChannel.Stable ||
                selection.Scope != request.Scope || (selection.Architecture != request.Architecture && selection.Architecture != UpdateArchitecture.Neutral)) continue;
            if (!choices.Any(choice => choice.Selection!.EqualsEvidence(selection))) choices.Add(result);
        }
        return choices.AsReadOnly();
    }

    private static async Task<T> BoundedAsync<T>(Func<Task<T>> query, CancellationToken cancellationToken)
    {
        await DiscoverySlots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { cancellationToken.ThrowIfCancellationRequested(); return await query().ConfigureAwait(false); }
        finally { DiscoverySlots.Release(); }
    }

    private sealed class BoundedPreflight(IInstallPreflightAuthority authority) : IInstallPreflightAuthority
    {
        public Task<InstallPreflightResult> PreflightAsync(InstallPreflightRequest request, CancellationToken cancellationToken) =>
            BoundedAsync(() => authority.PreflightAsync(request, cancellationToken), cancellationToken);
    }

    private sealed class BoundedCatalogQuery(IWinGetInstallCatalogQuery query) : IWinGetInstallCatalogQuery
    {
        public Task<WinGetInstallObservation?> QueryAsync(string packageId, string? expectedVersion, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken) =>
            BoundedAsync(() => query.QueryAsync(packageId, expectedVersion, scope, architecture, cancellationToken), cancellationToken);
    }
}
