using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Inventory.Windows;

namespace PCSteward.Desktop;

internal sealed class RegistryLocalInventoryProvider : ILocalInventoryProvider
{
    private readonly RegistryInventoryProvider provider = new(new WindowsUninstallRegistry());
    public string Name => "Windows uninstall registry";

    public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RegistryInventoryResult result = provider.ReadAll();
        List<CanonicalProduct> products = []; List<InstallationInstance> instances = [];
        foreach (RegistryInventoryRecord record in result.Records.Where(record => !string.IsNullOrWhiteSpace(record.DisplayName)))
        {
            Guid productId = StableGuid($"{record.Provider}|{record.KeyName}|{record.Scope}|{record.Architecture}");
            products.Add(new CanonicalProduct(productId, productId.ToString("N"), record.DisplayName!));
            instances.Add(new InstallationInstance(StableGuid($"instance|{record.Provider}|{record.KeyName}|{record.Scope}|{record.Architecture}"), productId, record.Provider, record.KeyName, record.DisplayVersion, record.Scope, record.Architecture));
        }
        return Task.FromResult(new ProviderScanResult(products, instances, result.Warnings));
    }

    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}
