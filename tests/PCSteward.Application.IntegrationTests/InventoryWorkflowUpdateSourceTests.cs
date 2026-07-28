using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class InventoryWorkflowUpdateSourceTests
{
    [TestMethod]
    public async Task ComposedWorkflowUsesApprovedSourceOnlyWhenCheckedAndSupportsExactManualMappings()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct product = new(Guid.NewGuid(), "winget:contoso.widget", "Contoso Widget");
        InstallationInstance instance = new(Guid.NewGuid(), product.Id, "winget", "Contoso.Widget", "1.0.0", "Machine", "x64", "Contoso", Channel: "Stable", SourceId: "winget");
        await database.Store.PersistScanAsync(new ScanRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", Guid.NewGuid()), [product], [instance], [], [], CancellationToken.None);
        SourcePackageRecord package = new("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable");
        using InventoryWorkflow workflow = new(database.Store, database.Store, database.Store, Array.Empty<ILocalInventoryProvider>(), decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FakeConnector(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], []))]);

        UpdateIntelligenceSummary localOnly = await workflow.LoadUpdateIntelligenceAsync(false, CancellationToken.None);
        UpdateIntelligenceSummary checkedSources = await workflow.RefreshUpdateIntelligenceAsync(true, CancellationToken.None);
        ManualMappingValidation mapping = await workflow.CreateManualMappingAsync(product.StableKey, "winget", "Contoso.Widget", true, "reviewed", CancellationToken.None);

        Assert.AreEqual(UpdateIntelligenceStatus.Unmanaged, localOnly.Items.Single().Status);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, checkedSources.Items.Single().Status);
        Assert.AreEqual(SourceSnapshotState.Live, checkedSources.Sources.Single().State);
        Assert.IsTrue(mapping.IsValid);
        Assert.HasCount(1, await workflow.GetManualMappingsAsync(CancellationToken.None));
        Assert.IsTrue((await workflow.GetTimelineAsync(product.StableKey, CancellationToken.None)).Any(entry => entry.Kind == "InventoryScan"));
    }

    [TestMethod]
    public async Task SamePackageIdFromAnotherSourceAndMultipleInstallTargetsRemainReviewOnly()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct wrongSourceProduct = new(Guid.NewGuid(), "winget:msstore:contoso.widget", "Contoso Widget");
        CanonicalProduct multiTargetProduct = new(Guid.NewGuid(), "winget:winget:contoso.multi", "Contoso Multi");
        InstallationInstance wrongSource = new(Guid.NewGuid(), wrongSourceProduct.Id, "winget", "Contoso.Widget", "1.0.0", "Machine", "x64", "Contoso", Channel: "Stable", SourceId: "msstore");
        InstallationInstance x64 = new(Guid.NewGuid(), multiTargetProduct.Id, "winget", "Contoso.Multi", "1.0.0", "Machine", "x64", "Contoso", Channel: "Stable", SourceId: "winget");
        InstallationInstance x86 = new(Guid.NewGuid(), multiTargetProduct.Id, "winget", "Contoso.Multi", "1.0.0", "Machine", "x86", "Contoso", Channel: "Stable", SourceId: "winget");
        await database.Store.PersistScanAsync(new ScanRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", Guid.NewGuid()), [wrongSourceProduct, multiTargetProduct], [wrongSource, x64, x86], [], [], CancellationToken.None);
        SourceSnapshot snapshot = new("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable"), new SourcePackageRecord("winget", "Contoso.Multi", "Contoso Multi", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);
        using InventoryWorkflow workflow = new(database.Store, database.Store, database.Store, Array.Empty<ILocalInventoryProvider>(), decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FakeConnector(snapshot)]);

        UpdateIntelligenceSummary result = await workflow.RefreshUpdateIntelligenceAsync(true, CancellationToken.None);

        UpdateIntelligenceItem wrongSourceItem = result.Items.Single(item => item.StableProductKey == wrongSourceProduct.StableKey);
        UpdateIntelligenceItem multiTargetItem = result.Items.Single(item => item.StableProductKey == multiTargetProduct.StableKey);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, wrongSourceItem.Status);
        CollectionAssert.Contains(wrongSourceItem.Mapping!.ReviewReasons.ToArray(), MappingReviewReason.SourceConflict);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, multiTargetItem.Status);
        CollectionAssert.Contains(multiTargetItem.Mapping!.ReviewReasons.ToArray(), MappingReviewReason.MultipleInstallationInstanceAmbiguity);
        Assert.IsFalse(wrongSourceItem.Mapping.IsActionable);
        Assert.IsFalse(multiTargetItem.Mapping.IsActionable);
    }

    private sealed class FakeConnector(SourceSnapshot result) : IUpdateSourceConnector
    {
        public string SourceId => result.SourceId;
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string path;
        private TestDatabase(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public SqliteDatabase Store { get; }
        public static async Task<TestDatabase> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-workflow-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path); await store.MigrateAsync(CancellationToken.None); return new TestDatabase(path, store);
        }
        public ValueTask DisposeAsync() { File.Delete(path); return ValueTask.CompletedTask; }
    }
}
