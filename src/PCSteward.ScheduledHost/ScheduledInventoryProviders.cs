using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Inventory.Windows;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace PCSteward.ScheduledHost;

internal sealed class ScheduledRegistryInventoryProvider : ILocalInventoryProvider
{
    private readonly RegistryInventoryProvider provider = new(new WindowsUninstallRegistry());
    public string Name => "Windows uninstall registry";
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        RegistryInventoryResult result = provider.ReadAll(cancellationToken); List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> raw = [];
        foreach (RegistryInventoryRecord record in result.Records.Where(record => !string.IsNullOrWhiteSpace(record.DisplayName)))
        {
            cancellationToken.ThrowIfCancellationRequested(); Guid productId = StableGuid($"{record.Provider}|{record.KeyName}|{record.Scope}|{record.Architecture}");
            products.Add(new CanonicalProduct(productId, productId.ToString("N"), record.DisplayName!));
            instances.Add(new InstallationInstance(StableGuid($"instance|{record.Provider}|{record.KeyName}|{record.Scope}|{record.Architecture}"), productId, record.Provider, record.KeyName, record.DisplayVersion, record.Scope, record.Architecture, record.Publisher, record.InstallDate, null, null, "Application"));
            raw.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, record.Provider, record.KeyName, $"name={record.DisplayName};version={record.DisplayVersion};publisher={record.Publisher};scope={record.Scope};architecture={record.Architecture}"));
        }
        return new ProviderScanResult(products, instances, raw, result.Warnings);
    }, cancellationToken);
    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}

internal sealed class ScheduledMsixInventoryProvider(Func<bool> isEnabled) : ILocalInventoryProvider
{
    public string Name => "Current-user AppX/MSIX catalog";
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        if (!isEnabled()) return Task.FromResult(new ProviderScanResult([], [], [], ["msix:disabled"]));
        return Task.Run(() =>
    {
        List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> raw = [];
        foreach (Package package in new PackageManager().FindPackagesForUser(string.Empty))
        {
            cancellationToken.ThrowIfCancellationRequested(); if (package.IsFramework || package.IsResourcePackage) continue;
            string family = package.Id.FamilyName ?? string.Empty; string full = package.Id.FullName ?? string.Empty;
            if (!PackageFamilyName.IsValid(family) || string.IsNullOrWhiteSpace(full)) continue;
            Guid productId = StableGuid("product|" + family);
            // The same identity and names as the desktop's MSIX inventory: the package family names the app (stable across versions),
            // and the names Windows shows are used when it has them.
            products.Add(new CanonicalProduct(productId, family, MsixInventoryProvider.ShownName(ReadOrNull(() => package.DisplayName)) ?? package.Id.Name ?? family));
            instances.Add(new InstallationInstance(StableGuid("instance|" + full), productId, "msix", family, $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}", "User", package.Id.Architecture.ToString(), MsixInventoryProvider.ShownName(ReadOrNull(() => package.PublisherDisplayName)) ?? package.Id.PublisherId, null, package.InstalledLocation?.Path, null, "Application"));
            raw.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, "msix", full, $"family={family};publisher={package.Id.PublisherId};classification=Application"));
        }
        return new ProviderScanResult(products, instances, raw, []);
        }, cancellationToken);
    }
    private static string? ReadOrNull(Func<string?> read)
    {
        try { return read(); }
        catch (Exception) { return null; }
    }
    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}
