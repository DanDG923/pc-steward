using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class SqliteDatabaseTests
{
    [TestMethod]
    public async Task MigrationAndPromotionPreserveLastKnownGoodSnapshotWhenReplacementFails()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct product = new(Guid.NewGuid(), "winget:contoso.widget", "Contoso Widget");
        InstallationInstance instance = new(Guid.NewGuid(), product.Id, "registry", "contoso-widget", "1.0", "User", "x64");
        await database.Store.PromoteAsync([product], [instance], CancellationToken.None);

        try { await database.Store.PromoteAsync([product], [new InstallationInstance(Guid.NewGuid(), Guid.NewGuid(), "registry", "broken", null, "User", "x64")], CancellationToken.None); Assert.Fail("Expected foreign-key failure."); } catch (SqliteException) { }

        IReadOnlyCollection<CanonicalProduct> products = await database.Store.GetLastKnownGoodProductsAsync(CancellationToken.None);
        Assert.AreEqual(1, products.Count);
        Assert.AreEqual(product.StableKey, products.Single().StableKey);
    }

    [TestMethod]
    public async Task StableDecisionsSurviveInventoryPromotion()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        StableDecision decision = new(Guid.NewGuid(), "winget:contoso.widget", "Pin", "1.0", DateTimeOffset.UtcNow);
        await database.Store.SaveAsync(decision, CancellationToken.None);
        await database.Store.PromoteAsync([], [], CancellationToken.None);

        IReadOnlyCollection<StableDecision> decisions = await database.Store.GetForProductAsync(decision.StableProductKey, CancellationToken.None);
        Assert.AreEqual(1, decisions.Count);
        Assert.AreEqual(decision.Value, decisions.Single().Value);
    }

    [TestMethod]
    public async Task ClearScanDataIsTransactionalIdempotentAndPreservesDecisionsHistoryMappingsAndArtifacts()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct product = new(Guid.NewGuid(), "winget:contoso.widget", "Contoso Widget");
        await database.Store.PromoteAsync([product], [new InstallationInstance(Guid.NewGuid(), product.Id, "registry", "contoso-widget", "1.0", "User", "x64")], CancellationToken.None);
        await database.Store.SaveAsync(new StableDecision(Guid.NewGuid(), product.StableKey, "Pin", "1.0", DateTimeOffset.UtcNow), CancellationToken.None);
        await database.Store.SaveManualMappingAsync(new ManualMapping(Guid.NewGuid(), product.StableKey, "winget", "Contoso.Widget", DateTimeOffset.UtcNow), CancellationToken.None);
        await database.Store.SaveUpdateJobAsync(new UpdateJobRecord(Guid.NewGuid(), "winget", "winget", "Contoso.Widget", DateTimeOffset.UtcNow), CancellationToken.None);
        await database.Store.SaveDownloadedArtifactAsync(new DownloadedArtifact(Guid.NewGuid(), "update", "payload.bin", 512, DateTimeOffset.UtcNow), CancellationToken.None);

        ClearScanDataResult first = await database.Store.ClearScanDataAsync(CancellationToken.None);
        ClearScanDataResult second = await database.Store.ClearScanDataAsync(CancellationToken.None);
        ScanDataSummary summary = await database.Store.GetScanDataSummaryAsync(CancellationToken.None);
        (int jobs, int artifacts, int decisions, int mappings) = await database.Store.GetPreservedDataCountsAsync(CancellationToken.None);

        Assert.IsTrue(first.Succeeded);
        Assert.IsTrue(second.Succeeded);
        Assert.AreEqual(0, (await database.Store.GetLastKnownGoodProductsAsync(CancellationToken.None)).Count);
        Assert.AreEqual(0, summary.ItemCount);
        Assert.IsNotNull(summary.LastClearedAt);
        Assert.AreEqual((1, 1, 1, 1), (jobs, artifacts, decisions, mappings));

        await database.Store.PromoteAsync([product], [], CancellationToken.None);
        Assert.HasCount(1, await database.Store.GetForProductAsync(product.StableKey, CancellationToken.None));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string path;
        private TestDatabase(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public SqliteDatabase Store { get; }
        public static async Task<TestDatabase> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            return new TestDatabase(path, store);
        }
        public ValueTask DisposeAsync() { File.Delete(path); return ValueTask.CompletedTask; }
    }
}
