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
                record = new MsixPackageInfo(package.Id.FamilyName ?? string.Empty, package.Id.FullName ?? string.Empty, package.Id.Name ?? string.Empty, $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}", package.Id.Architecture.ToString() ?? string.Empty, package.Id.PublisherId ?? string.Empty, null, package.IsFramework, package.IsResourcePackage, false);
            }
            catch (Exception) { continue; }
            yield return record;
        }
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
            cancellationToken.ThrowIfCancellationRequested(); Guid productId = GuidFor("product|" + record.PackageFamilyName); products.Add(new CanonicalProduct(productId, record.PackageFamilyName, record.DisplayName)); instances.Add(new InstallationInstance(GuidFor("instance|" + record.PackageFullName), productId, record.Provider, record.PackageFullName, record.Version, "User", record.Architecture, record.PublisherId, null, record.InstallLocation, null, record.Classification)); raw.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, record.Provider, record.PackageFullName, $"family={record.PackageFamilyName};publisher={record.PublisherId};classification={record.Classification};path={record.InstallLocation}"));
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
