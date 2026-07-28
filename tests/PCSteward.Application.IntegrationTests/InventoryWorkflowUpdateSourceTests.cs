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

    [TestMethod]
    public async Task ApprovedUpdateSelectionRequiresPersistedConsentFreshEvidenceAndExactInstalledFields()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct product = new(Guid.NewGuid(), "winget:contoso.widget", "Contoso Widget");
        InstallationInstance instance = new(Guid.NewGuid(), product.Id, "winget", "Contoso.Widget", "1.0.0", "Machine", "x64", "Contoso", Channel: "Stable", SourceId: "winget");
        await database.Store.PersistScanAsync(new ScanRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", Guid.NewGuid()), [product], [instance], [], [], CancellationToken.None);
        SourcePackageRecord package = new("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable");
        using InventoryWorkflow workflow = new(database.Store, database.Store, database.Store, Array.Empty<ILocalInventoryProvider>(), decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FakeConnector(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], []))], targetedInstalledInventory: new TargetedInventory(true, [new TargetedInstalledCompatibility(UpdateScope.Machine, UpdateArchitecture.X64)]));

        UpdateSelectionPreparation withoutConsent = await workflow.PrepareApprovedUpdateSelectionAsync(product.StableKey, CancellationToken.None);
        await database.Store.SaveConnectorStateAsync("WinGet package sources", new ConnectorState(true, true), CancellationToken.None);
        UpdateSelectionPreparation prepared = await workflow.PrepareApprovedUpdateSelectionAsync(product.StableKey, CancellationToken.None);

        Assert.IsNull(withoutConsent.Selection);
        Assert.IsNotNull(prepared.Selection, prepared.Message);
        Assert.AreEqual("Contoso.Widget", prepared.Selection.PackageId);
        Assert.AreEqual("1.0.0", prepared.Selection.InstalledVersion);
        Assert.AreEqual("1.1.0", prepared.Selection.CandidateVersion);
        Assert.AreEqual(UpdateScope.Machine, prepared.Selection.Scope);
        Assert.AreEqual(UpdateArchitecture.X64, prepared.Selection.Architecture);
    }

    [TestMethod]
    public async Task UnknownWingetScopeCannotBeInferredFromAnUnrelatedSameNameRegistryProduct()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct wingetProduct = new(Guid.NewGuid(), "winget:mozilla.firefox", "Mozilla Firefox (x64 en-US)");
        CanonicalProduct registryProduct = new(Guid.NewGuid(), "registry:mozilla.firefox", "Mozilla Firefox (x64 en-US)");
        InstallationInstance winget = new(Guid.NewGuid(), wingetProduct.Id, "winget", "Mozilla.Firefox", "152.0.6", "Unknown", "Unknown", "Mozilla", SourceId: "winget");
        InstallationInstance registry = new(Guid.NewGuid(), registryProduct.Id, "registry", "Mozilla Firefox", "152.0.6", "Machine", "x64", "Mozilla");
        await database.Store.PersistScanAsync(new ScanRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", Guid.NewGuid()), [wingetProduct, registryProduct], [winget, registry], [], [], CancellationToken.None);
        SourcePackageRecord package = new("winget", "Mozilla.Firefox", "Mozilla Firefox", null, "152.0.6", "153.0.1", "Unknown", "Unknown", null);
        using InventoryWorkflow workflow = new(database.Store, database.Store, database.Store, Array.Empty<ILocalInventoryProvider>(), decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FakeConnector(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], []))], targetedInstalledInventory: new TargetedInventory(true, [new TargetedInstalledCompatibility(UpdateScope.Machine, UpdateArchitecture.X64)]));
        await database.Store.SaveConnectorStateAsync("WinGet package sources", new ConnectorState(true, true), CancellationToken.None);

        UpdateSelectionPreparation prepared = await workflow.PrepareApprovedUpdateSelectionAsync(wingetProduct.StableKey, CancellationToken.None);

        Assert.IsNotNull(prepared.Selection, prepared.Message);
        Assert.AreEqual(UpdateScope.Machine, prepared.Selection.Scope);
        Assert.AreEqual(UpdateArchitecture.X64, prepared.Selection.Architecture);
    }

    [TestMethod]
    public async Task ReInventoryUsesTheProductionWorkflowAndRejectsAChangedExactPackage()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        Guid providerProductId = Guid.NewGuid();
        CanonicalProduct providerProduct = new(providerProductId, "ignored", "Contoso Widget");
        InstallationInstance installed = new(Guid.NewGuid(), providerProductId, "winget", "Contoso.Widget", "1.1.0", "Unknown", "Unknown", "Contoso", SourceId: "winget");
        string stableKey = InventoryNormalization.ProductFingerprint(new RawInstallEvidence("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.1.0", "Unknown", "Unknown", null, "Application", "fixture", "winget"));
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(stableKey, UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "1.1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);
        using InventoryWorkflow matching = new(database.Store, database.Store, database.Store, [new FixedProvider(new ProviderScanResult([providerProduct], [installed], [], []))], targetedInstalledInventory: new TargetedInventory(true));
        using InventoryWorkflow changed = new(database.Store, database.Store, database.Store, [new FixedProvider(new ProviderScanResult([providerProduct], [installed with { ProviderIdentity = "Contoso.Other" }], [], []))], targetedInstalledInventory: new TargetedInventory(true));

        Assert.IsTrue((await matching.ReInventoryAsync(job, CancellationToken.None)).ExpectedVersionDetected);
        Assert.IsFalse((await changed.ReInventoryAsync(job, CancellationToken.None)).ExpectedVersionDetected);
    }

    [TestMethod]
    public async Task CurrentLocalInventoryChangeStopsPreparationBeforeAJobCanBeCreated()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct stale = new(Guid.NewGuid(), "winget:contoso.widget", "Contoso Widget");
        InstallationInstance staleInstance = new(Guid.NewGuid(), stale.Id, "winget", "Contoso.Widget", "1.0.0", "Machine", "x64", "Contoso", SourceId: "winget");
        await database.Store.PersistScanAsync(new ScanRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", Guid.NewGuid()), [stale], [staleInstance], [], [], CancellationToken.None);
        await database.Store.SaveConnectorStateAsync("WinGet package sources", new ConnectorState(true, true), CancellationToken.None);
        SourcePackageRecord package = new("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable");
        using InventoryWorkflow workflow = new(database.Store, database.Store, database.Store, [new FixedProvider(new ProviderScanResult([], [], [], []))], decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FakeConnector(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], []))]);

        UpdateSelectionPreparation prepared = await workflow.PrepareApprovedUpdateSelectionAsync(stale.StableKey, CancellationToken.None);

        Assert.IsNull(prepared.Selection);
        StringAssert.Contains(prepared.Message, "no longer present");
    }

    [TestMethod]
    public async Task CurrentInventoryChangeBetweenReviewAndConfirmationNeverStartsTheProvider()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        Guid productId = Guid.NewGuid();
        CanonicalProduct product = new(productId, "ignored", "Contoso Widget");
        InstallationInstance installed = new(Guid.NewGuid(), productId, "winget", "Contoso.Widget", "1.0.0", "Unknown", "Unknown", "Contoso", SourceId: "winget");
        string stableKey = InventoryNormalization.ProductFingerprint(new RawInstallEvidence("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "Unknown", "Unknown", null, "Application", "fixture", "winget"));
        await database.Store.SaveConnectorStateAsync("WinGet package sources", new ConnectorState(true, true), CancellationToken.None);
        SourcePackageRecord package = new("winget", "Contoso.Widget", "Contoso Widget", "Contoso", "1.0.0", "1.1.0", "Unknown", "Unknown", "Stable");
        using InventoryWorkflow inventory = new(database.Store, database.Store, database.Store, [new SequenceProvider(new ProviderScanResult([product], [installed], [], []), new ProviderScanResult([], [], [], []))], decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FakeConnector(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], []))], targetedInstalledInventory: new TargetedInventory(true, [new TargetedInstalledCompatibility(UpdateScope.Machine, UpdateArchitecture.X64)]));
        RecordingExecutor executor = new();
        ReviewedUpdateExecutionWorkflow workflow = new(inventory.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database.Store), new SequentialUpdateBatchCoordinator(executor, new NeverReachedInventory(), new InMemoryHistory(), new NoRunningApplicationDetector()));

        UpdateReviewPreparation review = await workflow.BeginReviewAsync(stableKey, CancellationToken.None);
        ReviewedUpdateExecutionResult result = await workflow.ConfirmAndRunAsync(review.Review!, CancellationToken.None);

        Assert.IsFalse(result.Started);
        Assert.HasCount(0, executor.Jobs);
        Assert.IsNull(await database.Store.GetApprovedJobAsync(review.Review!.JobId, CancellationToken.None));
    }

    private sealed class FakeConnector(SourceSnapshot result) : IUpdateSourceConnector
    {
        public string SourceId => result.SourceId;
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class FixedProvider(ProviderScanResult result) : ILocalInventoryProvider
    {
        public string Name => "fixture";
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class SequenceProvider(params ProviderScanResult[] results) : ILocalInventoryProvider
    {
        private readonly Queue<ProviderScanResult> results = new(results);
        public string Name => "fixture";
        public TimeSpan Timeout => TimeSpan.FromSeconds(1);
        public Task<ProviderScanResult> ScanAsync(CancellationToken cancellationToken) => Task.FromResult(results.Dequeue());
    }

    private sealed class TargetedInventory(bool expected, IReadOnlyCollection<TargetedInstalledCompatibility>? compatibility = null) : ITargetedInstalledInventory
    {
        public Task<IReadOnlyCollection<TargetedInstalledCompatibility>> ResolveExactCompatibilityAsync(UpdateSource source, string packageId, string installedVersion, CancellationToken cancellationToken) => Task.FromResult(compatibility ?? Array.Empty<TargetedInstalledCompatibility>() as IReadOnlyCollection<TargetedInstalledCompatibility>);
        public Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(expected);
    }

    private sealed class RecordingExecutor : IApprovedUpdateExecutor
    {
        public List<Guid> Jobs { get; } = [];
        public Task<UpdateProviderAttempt> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence currentEvidence, CancellationToken cancellationToken) { Jobs.Add(job.Id); return Task.FromResult(new UpdateProviderAttempt(UpdateProviderAttemptStatus.AwaitingVerification, 0, "completed")); }
    }

    private sealed class NeverReachedInventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => throw new InvalidOperationException("Execution should not reach verification.");
    }

    private sealed class InMemoryHistory : IUpdateHistoryStore
    {
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>([]);
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
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
