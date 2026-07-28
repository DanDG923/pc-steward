using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Inventory.Windows;
using PCSteward.Provider.WinGet;
using Windows.Management.Deployment;

namespace PCSteward.Desktop;

internal sealed class WindowsCurrentUserPackageCatalog : ICurrentUserPackageCatalog
{
    public IEnumerable<MsixPackageInfo> FindPackagesForCurrentUser() => new PackageManager().FindPackagesForUser(string.Empty).Select(package => new MsixPackageInfo(package.Id.FamilyName ?? string.Empty, package.Id.FullName ?? string.Empty, package.Id.Name ?? string.Empty, package.Id.Version.ToString() ?? string.Empty, package.Id.Architecture.ToString() ?? string.Empty, package.Id.PublisherId ?? string.Empty, package.InstalledLocation?.Path, package.IsFramework, package.IsResourcePackage, false));
}

internal sealed class MsixLocalInventoryProvider(Func<bool> enabled) : ILocalInventoryProvider
{
    private readonly MsixInventoryProvider provider = new(new WindowsCurrentUserPackageCatalog());
    public string Name => "Current-user AppX/MSIX catalog";
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        if (!enabled()) return new ProviderScanResult([], [], [], ["disabled"]);
        MsixInventoryResult result = provider.ReadCurrentUserPackages(DateTimeOffset.UtcNow); List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> raw = [];
        foreach (MsixInventoryRecord record in result.Records)
        {
            cancellationToken.ThrowIfCancellationRequested(); Guid productId = GuidFor("product|" + record.PackageFamilyName); products.Add(new CanonicalProduct(productId, record.PackageFamilyName, record.DisplayName)); instances.Add(new InstallationInstance(GuidFor("instance|" + record.PackageFullName), productId, record.Provider, record.PackageFullName, record.Version, "User", record.Architecture, record.PublisherId, null, record.InstallLocation, null, record.Classification)); raw.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, record.Provider, record.PackageFullName, $"family={record.PackageFamilyName};publisher={record.PublisherId};classification={record.Classification};path={record.InstallLocation}"));
        }
        return new ProviderScanResult(products, instances, raw, result.Warnings);
    }, cancellationToken);
    private static Guid GuidFor(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}

internal sealed class WingetLocalInventoryProvider(Func<bool> enabled) : ILocalInventoryProvider
{
    private readonly WingetInstalledInventoryProvider provider = new(new WindowsWingetProcess());
    public string Name => "WinGet installed inventory";
    public TimeSpan Timeout => TimeSpan.FromSeconds(35);
    public async Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        if (!enabled()) return new ProviderScanResult([], [], [], ["disabled"]);
        WingetInventoryResult result = await provider.ReadInstalledAsync(cancellationToken); List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> raw = [];
        foreach (WingetInstalledRecord record in result.Records)
        {
            string? sourceId = record.Source?.Trim().ToLowerInvariant() switch { "winget" => "winget", "msstore" => "msstore", _ => null };
            string sourceKey = sourceId ?? "unknown";
            Guid productId = GuidFor("product|winget|" + sourceKey + "|" + record.Id); products.Add(new CanonicalProduct(productId, "winget:" + sourceKey + ":" + record.Id, record.Name)); instances.Add(new InstallationInstance(GuidFor("instance|winget|" + sourceKey + "|" + record.Id + "|" + record.Version), productId, "winget", record.Id, record.Version, "Unknown", "Unknown", null, null, null, null, "Application", sourceId)); raw.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, "winget", record.Id, $"name={record.Name};version={record.Version};source={sourceId ?? "unknown"}"));
        }
        return new ProviderScanResult(products, instances, raw, result.Warnings);
    }
    private static Guid GuidFor(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}
