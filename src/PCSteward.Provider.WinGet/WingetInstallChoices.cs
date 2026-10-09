using PCSteward.Application;
using PCSteward.Catalog;

namespace PCSteward.Provider.WinGet;

/// <summary>Inert Task 2 seam for bounded live applicable installer discovery (FR-INS-02/04).</summary>
public sealed class WingetInstallChoiceAuthority : IInstallChoiceAuthority
{
    public WingetInstallChoiceAuthority() { }
    internal WingetInstallChoiceAuthority(IInstallPreflightAuthority preflight) => ArgumentNullException.ThrowIfNull(preflight);

    public Task<IReadOnlyList<InstallPreflightResult>> PreflightChoicesAsync(CatalogEntry entry, CatalogProviderIdentity provider, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InstallPreflightResult>>([]);
}
