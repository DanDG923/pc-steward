using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Inventory.Windows;
using PCSteward.Provider.WinGet;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace PCSteward.Desktop;

internal sealed class WindowsCurrentUserPackageCatalog : ICurrentUserPackageCatalog
{
    public IEnumerable<MsixPackageInfo> FindPackagesForCurrentUser()
    {
        foreach (Package package in new PackageManager().FindPackagesForUser(string.Empty))
        {
            MsixPackageInfo? record;
            try
            {
                bool isFramework = package.IsFramework, isResource = package.IsResourcePackage;
                // Shown names are read only for apps: frameworks and resource packs are most of the catalog and never offered.
                bool isApp = !isFramework && !isResource;
                record = new MsixPackageInfo(package.Id.FamilyName ?? string.Empty, package.Id.FullName ?? string.Empty, package.Id.Name ?? string.Empty, $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}", package.Id.Architecture.ToString() ?? string.Empty, package.Id.PublisherId ?? string.Empty, null, isFramework, isResource, IsSystemSigned(package),
                    isApp ? ReadOrNull(() => package.DisplayName) : null, isApp ? ReadOrNull(() => package.PublisherDisplayName) : null);
            }
            catch (Exception) { continue; }
            yield return record;
        }
    }

    // UI review 2026-11-13 (fault 3): a package Windows itself signed is an inbox part of Windows, not an app the owner installed.
    // WinGet doesn't update these, so they keep their full package name and are never offered. Store-signed apps (Windows
    // Terminal) are not System. A package whose signature can't be read is treated as an ordinary app.
    internal static bool IsSystemSigned(Package package)
    {
        try { return package.SignatureKind == PackageSignatureKind.System; }
        catch (Exception) { return false; }
    }

    // The shown names come from the package's resources; a package whose resources can't be read keeps its package name.
    internal static string? ReadOrNull(Func<string?> read)
    {
        try { return read(); }
        catch (Exception) { return null; }
    }
}

internal sealed class MsixLocalInventoryProvider(Func<bool> enabled) : ILocalInventoryProvider
{
    private readonly MsixInventoryProvider provider = new(new WindowsCurrentUserPackageCatalog());
    public string Name => "Current-user AppX/MSIX catalog";
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!enabled()) return new ProviderScanResult([], [], [], ["disabled"]);
        MsixInventoryResult result;
        try { result = provider.ReadCurrentUserPackages(DateTimeOffset.UtcNow); }
        catch (Exception) { return new ProviderScanResult([], [], [], ["msix:catalog-unavailable"]); }
        List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> raw = [];
        foreach (MsixInventoryRecord record in result.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The raw record is keyed by the same identity as the installation, so the details page still finds it (MSIX design note §3.1).
            string identity = MsixInventoryProvider.ProviderIdentity(record);
            Guid productId = GuidFor("product|" + identity); products.Add(new CanonicalProduct(productId, identity, record.DisplayName)); instances.Add(new InstallationInstance(GuidFor("instance|" + record.PackageFullName), productId, record.Provider, identity, record.Version, "User", record.Architecture, record.Publisher ?? record.PublisherId, null, record.InstallLocation, null, record.Classification)); raw.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, record.Provider, identity, $"full={record.PackageFullName};family={record.PackageFamilyName};publisher={record.PublisherId};classification={record.Classification};path={record.InstallLocation}"));
        }
        return new ProviderScanResult(products, instances, raw, result.Warnings);
    }, cancellationToken);
    private static Guid GuidFor(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}

internal sealed class WingetPostUpdatePackageInventory : IPostUpdatePackageInventory
{
    private readonly WingetInstalledInventoryProvider provider = new();
    private readonly WingetPostUpdateRegistryLink link = new();
    public Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => provider.HasExactExpectedVersionAsync(job, cancellationToken);
    public Task<string?> GetLinkedProductKeyAfterUpdateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => link.ResolveAsync(job, cancellationToken);
}
