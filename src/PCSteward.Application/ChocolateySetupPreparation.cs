using PCSteward.Catalog;

namespace PCSteward.Application;

/// <summary>CRD-08 / FR-PRV-03: fixed reserved setup identity; retain it in the verification catalog.</summary>
public sealed class ChocolateySetupPreparation
{
    private readonly (InventoryWorkflow Workflow, IProviderHealthProbe Winget, IProviderHealthProbe Chocolatey, UpdateExecutionCapabilities Capabilities) dependencies;
    public const string CatalogEntryId = "pcs:chocolatey-setup";
    public const string PackageId = "Chocolatey.Chocolatey";

    public static bool IsReservedCatalogEntry(string catalogEntryId) => string.Equals(catalogEntryId, CatalogEntryId, StringComparison.Ordinal);

    /// <summary>Read-only preparation; the caller must still present agreements and obtain ordinary batch approval.</summary>
    public ChocolateySetupPreparation(InventoryWorkflow workflow, IProviderHealthProbe wingetHealth, IProviderHealthProbe chocolateyHealth, UpdateExecutionCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(wingetHealth);
        ArgumentNullException.ThrowIfNull(chocolateyHealth);
        ArgumentNullException.ThrowIfNull(capabilities);
        dependencies = (workflow, wingetHealth, chocolateyHealth, capabilities);
    }

    public async Task<InstallSelectionPreparation> PrepareAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!dependencies.Capabilities.MachineScopeEnabled) return new(null, "Machine-scope provider setup is unavailable.");
        ProviderHealthReport? winget = await ProbeFreshAsync(dependencies.Winget, cancellationToken).ConfigureAwait(false);
        if (winget is not { Provider: CatalogProviderKind.WinGet, Presence: ProviderPresence.Present, Trust: ProviderTrust.Trusted, State: ProviderProbeState.Current }) return new(null, "Trusted WinGet is required for reviewed Chocolatey setup.");
        if (!await ConfirmChocolateyAbsentAsync(cancellationToken).ConfigureAwait(false)) return new(null, "Chocolatey must be positively absent. Existing or uncertain clients cannot be reinstalled.");
        InstallSelectionPreparation prepared = await dependencies.Workflow.PrepareChocolateySetupSelectionAsync(ConfirmChocolateyAbsentAsync, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return prepared;
    }

    /// <summary>Composition port for ordinary install verification followed by a fresh strict health report.</summary>
    public async Task<ChocolateySetupVerification> VerifyAsync(PostUpdateInventoryResult ordinaryVerification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ordinaryVerification);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ordinaryVerification.ExpectedVersionDetected) return new(false, "Needs attention");
        ProviderHealthReport? chocolatey = await ProbeFreshAsync(dependencies.Chocolatey, cancellationToken).ConfigureAwait(false);
        return chocolatey is { Provider: CatalogProviderKind.Chocolatey, Presence: ProviderPresence.Present, Trust: ProviderTrust.Trusted, State: ProviderProbeState.Current }
            ? new(true, "Ready") : new(false, "Needs attention");
    }

    private async Task<bool> ConfirmChocolateyAbsentAsync(CancellationToken cancellationToken)
    {
        ProviderHealthReport? report = await ProbeFreshAsync(dependencies.Chocolatey, cancellationToken).ConfigureAwait(false);
        return report is { Provider: CatalogProviderKind.Chocolatey, Presence: ProviderPresence.Absent, Trust: ProviderTrust.Unknown, State: ProviderProbeState.Current };
    }

    private static async Task<ProviderHealthReport?> ProbeFreshAsync(IProviderHealthProbe probe, CancellationToken cancellationToken)
    {
        ProviderHealthReport report;
        try { report = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception) when (!cancellationToken.IsCancellationRequested) { return null; }
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return report.CheckedAt <= now && now - report.CheckedAt <= TimeSpan.FromMinutes(2) ? report : null;
    }

    internal static bool IsFixedSetupEntry(CatalogEntry entry) => IsReservedCatalogEntry(entry.Id) &&
        entry.Channel == CatalogChannel.Stable && !entry.AutomationAllowed && entry.Providers.Count == 1 &&
        entry.DefaultProvider is { Provider: CatalogProviderKind.WinGet, Source: CatalogSource.WinGet, PackageId: PackageId } provider &&
        provider.Scopes.Count == 1 && provider.Scopes[0] == CatalogScope.Machine;
}

public sealed record ChocolateySetupVerification(bool IsReady, string Message);
