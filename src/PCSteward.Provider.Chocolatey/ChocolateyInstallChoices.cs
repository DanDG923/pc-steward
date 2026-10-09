using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey;

/// <summary>CRD-08 / FR-INS-04: live fixed-feed choices through strict current trusted-client health.</summary>
public sealed class ChocolateyInstallChoiceAuthority : IInstallChoiceAuthority
{
    private readonly IInstallPreflightAuthority preflight;
    private readonly IProviderHealthProbe health;
    public ChocolateyInstallChoiceAuthority() : this(new ChocolateyInstallPreflightAuthority(), new ChocolateyProviderHealthProbe()) { }
    internal ChocolateyInstallChoiceAuthority(IInstallPreflightAuthority preflight, IProviderHealthProbe health)
    {
        this.preflight = preflight ?? throw new ArgumentNullException(nameof(preflight));
        this.health = health ?? throw new ArgumentNullException(nameof(health));
    }

    public async Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();
        if (!entry.Providers.Contains(provider) || provider is not { Provider: CatalogProviderKind.Chocolatey, Source: CatalogSource.ChocolateyCommunity } ||
            !provider.Scopes.Contains(CatalogScope.Machine) || entry.Channel != CatalogChannel.Stable || ChocolateySetupPreparation.IsReservedCatalogEntry(entry.Id)) return [];
        ProviderHealthReport report;
        try { report = await health.ProbeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return []; }
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (report is not { Provider: CatalogProviderKind.Chocolatey, Presence: ProviderPresence.Present, Trust: ProviderTrust.Trusted, State: ProviderProbeState.Current } ||
            report.CheckedAt > now || now - report.CheckedAt > TimeSpan.FromMinutes(2)) return [];
        InstallPreflightResult result = await preflight.PreflightAsync(new(entry.Id, UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, provider.PackageId, UpdateScope.Machine, UpdateArchitecture.Neutral), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result is { IsActionable: true, Selection: { Operation: PackageOperationKind.Install, Provider: UpdateProvider.Chocolatey, Source: UpdateSource.ChocolateyCommunity, Scope: UpdateScope.Machine, Architecture: UpdateArchitecture.Neutral, Channel: UpdateChannel.Stable } selection } &&
            selection.StableProductKey == entry.Id && string.Equals(selection.PackageId, provider.PackageId, StringComparison.OrdinalIgnoreCase) ? [result] : [];
    }
}
