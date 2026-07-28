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
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);

    public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        RegistryInventoryResult result = provider.ReadAll();
        List<CanonicalProduct> products = []; List<InstallationInstance> instances = []; List<RawProviderRecord> rawRecords = [];
        foreach (RegistryInventoryRecord record in result.Records.Where(record => !string.IsNullOrWhiteSpace(record.DisplayName)))
        {
            Guid productId = StableGuid($"{record.Provider}|{record.KeyName}|{record.Scope}|{record.Architecture}");
            products.Add(new CanonicalProduct(productId, productId.ToString("N"), record.DisplayName!));
            instances.Add(new InstallationInstance(StableGuid($"instance|{record.Provider}|{record.KeyName}|{record.Scope}|{record.Architecture}"), productId, record.Provider, record.KeyName, record.DisplayVersion, record.Scope, record.Architecture, record.Publisher, record.InstallDate, null, null, "Application"));
            rawRecords.Add(new RawProviderRecord(Guid.NewGuid(), Guid.Empty, record.Provider, record.KeyName, $"name={record.DisplayName};version={record.DisplayVersion};publisher={record.Publisher};installDate={record.InstallDate};scope={record.Scope};architecture={record.Architecture}"));
        }
        return new ProviderScanResult(products, instances, rawRecords, result.Warnings);
    }, cancellationToken);

    private static Guid StableGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}
