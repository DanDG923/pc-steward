using PCSteward.Application;
using PCSteward.Catalog;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Inert Task 2 seam for strict trusted Machine/Neutral choices (CRD-08 / FR-INS-04).</summary>
public sealed class ChocolateyInstallChoiceAuthority : IInstallChoiceAuthority
{
    public ChocolateyInstallChoiceAuthority() { }
    internal ChocolateyInstallChoiceAuthority(IInstallPreflightAuthority preflight, IProviderHealthProbe health)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(health);
    }

    public Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstallPreflightResult>>([]);
}
