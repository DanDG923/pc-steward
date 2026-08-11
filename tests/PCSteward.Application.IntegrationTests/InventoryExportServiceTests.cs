using System.Text.Json;
using System.Globalization;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class InventoryExportServiceTests
{
    [TestMethod]
    public async Task CsvAndJsonMatchCurrentInventoryAndIdentifyTheExportTime()
    {
        Guid productId = Guid.NewGuid();
        SnapshotStore store = new([new CanonicalProduct(productId, "contoso.widget", "Contoso, Widget")], [new InstallationInstance(Guid.NewGuid(), productId, "registry", "contoso-key", "2.0", "Machine", "x64")]);
        InventoryExportService service = new(store, new FixedTimeProvider(DateTimeOffset.Parse("2026-07-28T12:34:56Z", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)));

        InventoryExport csv = await service.CreateCsvAsync(CancellationToken.None);
        InventoryExport json = await service.CreateJsonAsync(includeUpdateHistory: true, CancellationToken.None);

        StringAssert.Contains(csv.Content, "2026-07-28T12:34:56.0000000+00:00");
        StringAssert.Contains(csv.Content, "Contoso, Widget");
        StringAssert.Contains(csv.Content, "reveals installed software");
        using JsonDocument document = JsonDocument.Parse(json.Content);
        Assert.AreEqual("2026-07-28T12:34:56.0000000+00:00", document.RootElement.GetProperty("exportedAt").GetString());
        Assert.AreEqual("Contoso, Widget", document.RootElement.GetProperty("inventory")[0].GetProperty("displayName").GetString());
        Assert.AreEqual(0, document.RootElement.GetProperty("updateHistory").GetArrayLength());
        StringAssert.Contains(document.RootElement.GetProperty("updateHistoryStatus").GetString(), "Phase 3");
    }

    [TestMethod]
    public async Task JsonOmitsOptionalUpdateHistoryWhenNotRequested()
    {
        InventoryExport export = await new InventoryExportService(new SnapshotStore([], []), new FixedTimeProvider(DateTimeOffset.UnixEpoch)).CreateJsonAsync(false, CancellationToken.None);
        using JsonDocument document = JsonDocument.Parse(export.Content);
        Assert.IsFalse(document.RootElement.TryGetProperty("updateHistory", out _));
    }

    [TestMethod]
    public async Task CsvNeutralizesSpreadsheetFormulaCells()
    {
        Guid productId = Guid.NewGuid();
        SnapshotStore store = new([new CanonicalProduct(productId, "@danger", "=WEBSERVICE(\"https://example.invalid\")")], [new InstallationInstance(Guid.NewGuid(), productId, "registry", "+cmd", "-1", "Machine", "x64")]);

        InventoryExport export = await new InventoryExportService(store, new FixedTimeProvider(DateTimeOffset.UnixEpoch)).CreateCsvAsync(CancellationToken.None);

        StringAssert.Contains(export.Content, "\"'=WEBSERVICE(\"\"https://example.invalid\"\")\"");
        StringAssert.Contains(export.Content, "\"'@danger\"");
        StringAssert.Contains(export.Content, "\"'+cmd\"");
        StringAssert.Contains(export.Content, "\"'-1\"");
    }

    private sealed class SnapshotStore(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances) : IInventorySnapshotStore
    {
        public Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken) => Task.FromResult(products);
        public Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken) => Task.FromResult(instances);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
