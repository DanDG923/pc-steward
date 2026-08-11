using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record InventoryExport(string Content, string FileExtension, DateTimeOffset ExportedAt);

/// <summary>Formats the current local snapshot only; it does not read network sources or write files.</summary>
public sealed class InventoryExportService(IInventorySnapshotStore store, TimeProvider? clock = null)
{
    private const string PrivacyWarning = "This export reveals installed software and may contain identifying machine metadata. Keep it private.";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<InventoryExport> CreateCsvAsync(CancellationToken cancellationToken)
    {
        (CanonicalProduct[] products, InstallationInstance[] instances, DateTimeOffset exportedAt) = await ReadSnapshotAsync(cancellationToken);
        StringBuilder csv = new();
        csv.AppendLine("ExportedAt,PrivacyWarning,ProductName,StableKey,Provider,ProviderIdentity,Version,Scope,Architecture");
        foreach (CanonicalProduct product in products)
        {
            InstallationInstance[] productInstances = instances.Where(instance => instance.ProductId == product.Id).ToArray();
            if (productInstances.Length == 0) AppendCsvRow(csv, exportedAt, product, null);
            else foreach (InstallationInstance instance in productInstances) AppendCsvRow(csv, exportedAt, product, instance);
        }
        return new InventoryExport(csv.ToString(), ".csv", exportedAt);
    }

    public async Task<InventoryExport> CreateJsonAsync(bool includeUpdateHistory, CancellationToken cancellationToken)
    {
        (CanonicalProduct[] products, InstallationInstance[] instances, DateTimeOffset exportedAt) = await ReadSnapshotAsync(cancellationToken);
        object document = new
        {
            exportedAt = exportedAt.ToString("O", CultureInfo.InvariantCulture),
            privacyWarning = PrivacyWarning,
            inventory = products.Select(product => new
            {
                product.DisplayName,
                product.StableKey,
                instances = instances.Where(instance => instance.ProductId == product.Id).Select(instance => new { instance.Provider, instance.ProviderIdentity, instance.Version, instance.Scope, instance.Architecture })
            }),
            updateHistory = includeUpdateHistory ? Array.Empty<object>() : null,
            updateHistoryStatus = includeUpdateHistory ? "Not collected until Phase 3." : null
        };
        return new InventoryExport(JsonSerializer.Serialize(document, JsonOptions), ".json", exportedAt);
    }

    private async Task<(CanonicalProduct[] Products, InstallationInstance[] Instances, DateTimeOffset ExportedAt)> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        CanonicalProduct[] products = (await store.GetLastKnownGoodProductsAsync(cancellationToken)).OrderBy(product => product.StableKey, StringComparer.Ordinal).ToArray();
        InstallationInstance[] instances = (await store.GetLastKnownGoodInstancesAsync(cancellationToken)).OrderBy(instance => instance.Provider, StringComparer.Ordinal).ThenBy(instance => instance.ProviderIdentity, StringComparer.Ordinal).ToArray();
        return (products, instances, clock.GetUtcNow());
    }

    private static void AppendCsvRow(StringBuilder csv, DateTimeOffset exportedAt, CanonicalProduct product, InstallationInstance? instance) =>
        csv.AppendLine(string.Join(',', new[] { exportedAt.ToString("O", CultureInfo.InvariantCulture), PrivacyWarning, product.DisplayName, product.StableKey, instance?.Provider ?? string.Empty, instance?.ProviderIdentity ?? string.Empty, instance?.Version ?? string.Empty, instance?.Scope ?? string.Empty, instance?.Architecture ?? string.Empty }.Select(Escape)));
    private static string Escape(string value)
    {
        string safe = IsSpreadsheetFormula(value) ? "'" + value : value;
        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }

    private static bool IsSpreadsheetFormula(string value)
    {
        int index = 0;
        while (index < value.Length && value[index] is ' ' or '\t' or '\r' or '\n') index++;
        return index < value.Length && value[index] is '=' or '+' or '-' or '@';
    }
}
