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

    [TestMethod]
    public async Task PersistScanStoresSnapshotRawEvidenceRunAndTimingInOneClearableCategory()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        Guid productId = Guid.NewGuid(); Guid scanId = Guid.NewGuid();
        await database.Store.PersistScanAsync(new ScanRun(scanId, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "Completed", Guid.NewGuid()), [new CanonicalProduct(productId, "contoso", "Contoso")], [new InstallationInstance(Guid.NewGuid(), productId, "registry", "contoso", "1", "Machine", "x64")], [new RawProviderRecord(Guid.NewGuid(), scanId, "registry", "contoso", "bounded")], [new ScanProviderTiming(Guid.NewGuid(), scanId, "registry", TimeSpan.FromMilliseconds(12), null)], CancellationToken.None);

        ScanDataSummary summary = await database.Store.GetScanDataSummaryAsync(CancellationToken.None);
        Assert.AreEqual(5, summary.ItemCount);
        Assert.IsTrue(summary.SizeBytes > 0);
        Assert.AreEqual(0, (await database.Store.ClearScanDataAsync(CancellationToken.None)).RemainingItems);
        Assert.IsNull(await database.Store.GetLastCompletedScanAtAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ConnectorChoicePersistsOutsideScanData()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await database.Store.SaveConnectorEnabledAsync("Current-user AppX/MSIX catalog", false, CancellationToken.None);
        Assert.IsFalse(await database.Store.GetConnectorEnabledAsync("Current-user AppX/MSIX catalog", true, CancellationToken.None));
        await database.Store.ClearScanDataAsync(CancellationToken.None);
        Assert.IsFalse(await database.Store.GetConnectorEnabledAsync("Current-user AppX/MSIX catalog", true, CancellationToken.None));
    }

    [TestMethod]
    public async Task LegacyWingetEnabledSettingFailsClosedUntilSourceDisclosureIsApproved()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await database.Store.SaveConnectorEnabledAsync("WinGet installed inventory", true, CancellationToken.None);
        Assert.IsFalse(await database.Store.GetConnectorEnabledAsync("WinGet installed inventory", false, CancellationToken.None));
        await database.Store.SaveConnectorStateAsync("WinGet installed inventory", new PCSteward.Application.ConnectorState(true, true), CancellationToken.None);
        Assert.IsTrue(await database.Store.GetConnectorEnabledAsync("WinGet installed inventory", false, CancellationToken.None));
    }

    [TestMethod]
    public async Task TypedInstallationMetadataSurvivesPersistence()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync(); Guid productId = Guid.NewGuid();
        InstallationInstance instance = new(Guid.NewGuid(), productId, "registry", "contoso", "1", "Machine", "x64", "Contoso", "20260728", "C:\\Program Files\\Contoso", "Stable", "Application");
        await database.Store.PromoteAsync([new CanonicalProduct(productId, "contoso", "Contoso")], [instance], CancellationToken.None);
        InstallationInstance restored = (await database.Store.GetLastKnownGoodInstancesAsync(CancellationToken.None)).Single();
        Assert.AreEqual(instance.Publisher, restored.Publisher); Assert.AreEqual(instance.InstallDate, restored.InstallDate); Assert.AreEqual(instance.InstallPath, restored.InstallPath); Assert.AreEqual(instance.Channel, restored.Channel); Assert.AreEqual(instance.Classification, restored.Classification);
    }

    [TestMethod]
    public async Task SourceCacheReplacesOnlySuccessfulLiveSnapshotsAndRestoresTheirTimestamp()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync(); DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;
        SourceSnapshot snapshot = new("winget", "v1.9", timestamp, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);
        await database.Store.SaveSourceSnapshotAsync(snapshot, CancellationToken.None);
        SourceSnapshot restored = (await database.Store.GetSourceSnapshotAsync("winget", CancellationToken.None))!;
        Assert.AreEqual(SourceSnapshotState.Cached, restored.State); Assert.AreEqual(timestamp, restored.LastSuccessAt); Assert.AreEqual("Contoso.Widget", restored.Packages.Single().PackageId);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => database.Store.SaveSourceSnapshotAsync(snapshot with { State = SourceSnapshotState.Cached }, CancellationToken.None));
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
