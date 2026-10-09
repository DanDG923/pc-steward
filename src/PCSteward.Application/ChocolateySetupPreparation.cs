namespace PCSteward.Application;

/// <summary>CRD-08 / FR-PRV-03: fixed reserved setup identity; retain it in the verification catalog.</summary>
public sealed class ChocolateySetupPreparation
{
    public const string CatalogEntryId = "pcs:chocolatey-setup";
    public const string PackageId = "Chocolatey.Chocolatey";

    public static bool IsReservedCatalogEntry(string catalogEntryId) => string.Equals(catalogEntryId, CatalogEntryId, StringComparison.Ordinal);

    /// <summary>Inert test-first seam; no health probe, provider query, bootstrap, installer or approval.</summary>
    public ChocolateySetupPreparation(InventoryWorkflow workflow, IProviderHealthProbe wingetHealth, IProviderHealthProbe chocolateyHealth, UpdateExecutionCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(wingetHealth);
        ArgumentNullException.ThrowIfNull(chocolateyHealth);
        ArgumentNullException.ThrowIfNull(capabilities);
    }

    public Task<InstallSelectionPreparation> PrepareAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new InstallSelectionPreparation(null, "Chocolatey setup preparation is unavailable."));

    /// <summary>Composition port for ordinary install verification followed by a fresh strict health report.</summary>
    public Task<ChocolateySetupVerification> VerifyAsync(PostUpdateInventoryResult ordinaryVerification, CancellationToken cancellationToken) =>
        Task.FromResult(new ChocolateySetupVerification(false, "Needs attention"));
}

public sealed record ChocolateySetupVerification(bool IsReady, string Message);
