using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Provider.Chocolatey;

/// <summary>CRD-08 / FR-INS-04: live fixed-feed choices through strict current trusted-client health.</summary>
public sealed class ChocolateyInstallChoiceAuthority : IInstallChoiceAuthority
{
    private readonly IInstallPreflightAuthority preflight;
    private readonly IProviderHealthProbe health;
    public ChocolateyInstallChoiceAuthority() : this(ChocolateyInstallPreflightAuthority.CreateStrict(), new ChocolateyProviderHealthProbe()) { }
    internal ChocolateyInstallChoiceAuthority(IStrictChocolateyQueryClient client, IProviderHealthProbe health) : this(ChocolateyInstallPreflightAuthority.CreateStrict(client), health) { }
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

/// <summary>Provider-owned strict read-only launch boundary; no identity comes from display/report/catalog data.</summary>
internal interface IStrictChocolateyQueryClient
{
    ChocolateyClientIdentity Resolve(bool requireCallerCannotModifyLayout);
    void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout);
    Task<ChocolateyProcessResult> RunAsync(ChocolateyClientIdentity client, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class StrictChocolateyQueryProcess(IStrictChocolateyQueryClient client) : IChocolateyProcess
{
    private ChocolateyClientIdentity? capturedClient;
    public Task<ChocolateyProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capturedClient ??= client.Resolve(requireCallerCannotModifyLayout: true);
        client.ValidateUnchanged(capturedClient, requireCallerCannotModifyLayout: true);
        cancellationToken.ThrowIfCancellationRequested();
        return client.RunAsync(capturedClient, arguments, timeout, cancellationToken);
    }
}

internal sealed class WindowsStrictChocolateyQueryClient : IStrictChocolateyQueryClient
{
    public ChocolateyClientIdentity Resolve(bool requireCallerCannotModifyLayout) => MachineTrustedChocolatey.ResolveExecutable(requireCallerCannotModifyLayout);
    public void ValidateUnchanged(ChocolateyClientIdentity client, bool requireCallerCannotModifyLayout) => MachineTrustedChocolatey.ValidateUnchanged(client, requireCallerCannotModifyLayout);
    // The existing captured-client process branch repeats strict identity/layout validation and cancellation immediately before Process.Start.
    public Task<ChocolateyProcessResult> RunAsync(ChocolateyClientIdentity client, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken) =>
        new WindowsChocolateyProcess(client).RunAsync(arguments, timeout, cancellationToken);
}
