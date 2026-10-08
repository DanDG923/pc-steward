using System.Text;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class PersonalCatalogWorkflowTests
{
    private const string CatalogJson = """
    {
      "schemaVersion": 1,
      "catalogVersion": "2026.10.08.1",
      "entries": [
        {
          "id": "pcs:7zip", "name": "7-Zip", "publisher": "Igor Pavlov", "category": "Compression",
          "providers": [
            { "provider": "winget", "source": "winget", "packageId": "7zip.7zip", "scopes": ["machine"], "default": true },
            { "provider": "chocolatey", "source": "chocolatey-community", "packageId": "7zip", "scopes": ["machine"], "default": false }
          ],
          "detection": { "arpDisplayName": "^7-Zip [0-9.]{1,32}( \\((x64|arm64)\\))?$", "arpPublisher": "^Igor Pavlov$" }
        },
        {
          "id": "pcs:firefox", "name": "Mozilla Firefox", "publisher": "Mozilla", "category": "Browsers",
          "providers": [ { "provider": "winget", "source": "winget", "packageId": "Mozilla.Firefox", "scopes": ["user", "machine"], "default": true } ],
          "detection": { "arpDisplayName": "^Mozilla Firefox( \\((x64|x86) [a-z]{2,3}(-[A-Z]{2})?\\))?$", "arpPublisher": "^Mozilla$" }
        },
        {
          "id": "pcs:git", "name": "Git", "publisher": "The Git Development Community", "category": "Developer",
          "providers": [ { "provider": "chocolatey", "source": "chocolatey-community", "packageId": "git", "scopes": ["machine"], "default": true } ],
          "detection": { "arpDisplayName": "^Git$", "arpPublisher": "^The Git Development Community$" }
        }
      ]
    }
    """;

    private static PersonalCatalog Catalog() => CatalogValidator.Validate(Encoding.UTF8.GetBytes(CatalogJson)).Catalog ?? throw new InvalidOperationException("fixture catalog invalid");

    [TestMethod]
    public void MatchingCreatesCatalogMappingsOnlyFromExactDetection()
    {
        (CanonicalProduct sevenZip, InstallationInstance sevenZipInstance) = Product("registry:7zip", "7-Zip 24.08 (x64)", "registry", "7-Zip", "Igor Pavlov");
        (CanonicalProduct firefox, InstallationInstance firefoxInstance) = Product("winget:winget:Mozilla.Firefox", "Mozilla Firefox (x64 en-GB)", "winget", "Mozilla.Firefox", null, "winget");
        (CanonicalProduct nameOnly, InstallationInstance nameOnlyInstance) = Product("registry:firefox-copy", "Mozilla Firefox", "registry", "FirefoxKey", null);
        (CanonicalProduct git, InstallationInstance gitInstance) = Product("chocolatey:unknown:git", "git", "chocolatey", "git", null);
        (CanonicalProduct other, InstallationInstance otherInstance) = Product("registry:other", "Unrelated", "registry", "Other", "Someone");

        IReadOnlyList<CatalogProductMatch> matches = PersonalCatalogMatching.Match(Catalog(), [sevenZip, firefox, nameOnly, git, other], [sevenZipInstance, firefoxInstance, nameOnlyInstance, gitInstance, otherInstance]);

        Assert.AreEqual(new DeclaredPackageMapping("registry:7zip", "winget", "7zip.7zip", MappingOrigin.Catalog), matches.Single(match => match.StableProductKey == "registry:7zip").Mapping);
        Assert.AreEqual(new DeclaredPackageMapping(firefox.StableKey, "winget", "Mozilla.Firefox", MappingOrigin.Catalog), matches.Single(match => match.StableProductKey == firefox.StableKey).Mapping);
        Assert.AreEqual(new DeclaredPackageMapping(git.StableKey, "chocolatey-community", "git", MappingOrigin.Catalog), matches.Single(match => match.StableProductKey == git.StableKey).Mapping);
        CatalogProductMatch weak = matches.Single(match => match.StableProductKey == nameOnly.StableKey);
        Assert.AreEqual(CatalogDetectionKind.NameOnly, weak.Detection.Kind);
        Assert.IsNull(weak.Mapping);
        Assert.IsFalse(matches.Any(match => match.StableProductKey == other.StableKey));
    }

    [TestMethod]
    public void PreferredProviderSelectsTheProviderForNameAndPublisherMatchesOnly()
    {
        (CanonicalProduct product, InstallationInstance instance) = Product("registry:7zip", "7-Zip 24.08 (x64)", "registry", "7-Zip", "Igor Pavlov");
        Dictionary<string, ProductUpdatePolicy> preferChocolatey = new() { [product.StableKey] = new(PreferredProvider: "chocolatey") };
        Dictionary<string, ProductUpdatePolicy> tampered = new() { [product.StableKey] = new(PreferredProvider: "scoop") };

        Assert.AreEqual("chocolatey-community", PersonalCatalogMatching.Match(Catalog(), [product], [instance], preferChocolatey).Single().Mapping!.SourceId);
        Assert.AreEqual("winget", PersonalCatalogMatching.Match(Catalog(), [product], [instance], tampered).Single().Mapping!.SourceId);

        (CanonicalProduct managed, InstallationInstance wingetManaged) = Product("winget:winget:7zip.7zip", "7-Zip 24.08 (x64)", "winget", "7zip.7zip", null, "winget");
        Assert.AreEqual("winget", PersonalCatalogMatching.Match(Catalog(), [managed], [wingetManaged], preferChocolatey).Single().Mapping!.SourceId, "ADR-024: an installed app keeps its managing provider.");
    }

    [TestMethod]
    public void ConflictingInstanceEvidenceIsAmbiguousAndCreatesNoMapping()
    {
        CanonicalProduct product = new(Guid.NewGuid(), "merged:product", "7-Zip 24.08 (x64)");
        InstallationInstance winget = new(Guid.NewGuid(), product.Id, "winget", "Mozilla.Firefox", "1.0", "Machine", "x64", SourceId: "winget");
        InstallationInstance registry = new(Guid.NewGuid(), product.Id, "registry", "7-Zip", "24.08", "Machine", "x64", "Igor Pavlov");
        CatalogProductMatch match = PersonalCatalogMatching.Match(Catalog(), [product], [winget, registry]).Single();
        Assert.AreEqual(CatalogDetectionKind.Ambiguous, match.Detection.Kind);
        Assert.IsNull(match.Mapping);
    }

    [TestMethod]
    public async Task WorkflowUsesCatalogMappingsReportsStatesAndHonoursSkipAndManualMappings()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        (CanonicalProduct product, InstallationInstance instance) = Product("registry:7zip", "7-Zip 24.08 (x64)", "registry", "7-Zip", "Igor Pavlov", version: "24.08");
        await database.Store.PersistScanAsync(new ScanRun(Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed", Guid.NewGuid()), [product], [instance], [], [], CancellationToken.None);
        SourceSnapshot winget = new("winget", "1.0", DateTimeOffset.UtcNow, SourceSnapshotState.Live,
            [new("winget", "7zip.7zip", "7-Zip", "Igor Pavlov", "24.08", "24.09", "Machine", "x64", "Stable"), new("winget", "Other.Tool", "Other", "Other", "1.0", "2.0", "Machine", "x64", "Stable")], []);
        using InventoryWorkflow withoutCatalog = Workflow(database, winget, null);
        using InventoryWorkflow workflow = Workflow(database, winget, Catalog());

        Assert.AreNotEqual(UpdateIntelligenceStatus.VerifiedUpdate, (await withoutCatalog.RefreshUpdateIntelligenceAsync(true, CancellationToken.None)).Items.Single().Status);
        Assert.IsNull(await withoutCatalog.GetCatalogOverviewAsync(true, CancellationToken.None));

        UpdateIntelligenceItem item = (await workflow.RefreshUpdateIntelligenceAsync(true, CancellationToken.None)).Items.Single();
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, item.Status);
        Assert.AreEqual(MappingOrigin.Catalog, item.Mapping!.Origin);

        CatalogOverview overview = (await workflow.GetCatalogOverviewAsync(true, CancellationToken.None))!;
        Assert.AreEqual("2026.10.08.1", overview.CatalogVersion);
        Assert.AreEqual(CatalogAppState.UpdateAvailable, State(overview, "pcs:7zip"));
        Assert.AreEqual(CatalogAppState.NotInstalled, State(overview, "pcs:firefox"));
        Assert.AreEqual(CatalogAppState.NotInstalled, State(overview, "pcs:git"));

        await workflow.SkipVersionAsync(product.StableKey, "24.09", "not now", CancellationToken.None);
        Assert.AreEqual(UpdateIntelligenceStatus.Skipped, (await workflow.LoadUpdateIntelligenceAsync(true, CancellationToken.None)).Items.Single().Status);
        Assert.AreEqual(CatalogAppState.Skipped, State((await workflow.GetCatalogOverviewAsync(true, CancellationToken.None))!, "pcs:7zip"));

        ManualMappingValidation manual = await workflow.CreateManualMappingAsync(product.StableKey, "winget", "Other.Tool", true, "user choice wins", CancellationToken.None);
        Assert.IsTrue(manual.IsValid);
        UpdateIntelligenceItem afterManual = (await workflow.LoadUpdateIntelligenceAsync(true, CancellationToken.None)).Items.Single();
        Assert.AreEqual("Other.Tool", afterManual.Mapping!.PackageId);
        Assert.AreEqual(MappingOrigin.UserConfirmed, afterManual.Mapping.Origin);
    }

    [TestMethod]
    public async Task UserCatalogEntriesAreValidatedSurviveClearScanDataAndClearWithPolicies()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        using InventoryWorkflow workflow = Workflow(database, null, Catalog());
        UserCatalogEntryRecord notepad = new("pcs:user-notepadpp", "Notepad++", "Notepad++ Team", "Developer", "winget", "winget", "Notepad++.Notepad++", ["machine"]);

        Assert.IsTrue((await workflow.AddUserCatalogEntryAsync(notepad, CancellationToken.None)).Added);
        Assert.IsFalse((await workflow.AddUserCatalogEntryAsync(notepad, CancellationToken.None)).Added, "Duplicate ID");
        Assert.IsFalse((await workflow.AddUserCatalogEntryAsync(notepad with { Id = "pcs:user-other" }, CancellationToken.None)).Added, "Duplicate package");
        Assert.IsFalse((await workflow.AddUserCatalogEntryAsync(notepad with { Id = "pcs:user-firefox", PackageId = "Mozilla.Firefox" }, CancellationToken.None)).Added, "Bundled package");
        Assert.IsFalse((await workflow.AddUserCatalogEntryAsync(notepad with { Id = "pcs:user-store", Source = "msstore", PackageId = "Vendor.Store" }, CancellationToken.None)).Added, "msstore");
        Assert.AreEqual(CatalogAppState.NotInstalled, State((await workflow.GetCatalogOverviewAsync(false, CancellationToken.None))!, "pcs:user-notepadpp"));

        await workflow.SkipVersionAsync("registry:any", "1.2.3", null, CancellationToken.None);
        await workflow.ClearScanDataAsync(CancellationToken.None);
        Assert.HasCount(1, await database.Store.GetUserCatalogEntriesAsync(CancellationToken.None));
        Assert.AreEqual("1.2.3", (await new UserDecisionService(database.Store).GetPoliciesAsync(CancellationToken.None))["registry:any"].SkippedVersion);

        AdvancedDataClearResult cleared = await workflow.ClearMappingsAndPoliciesAsync(CancellationToken.None);
        Assert.IsTrue(cleared.Succeeded);
        Assert.HasCount(0, await database.Store.GetUserCatalogEntriesAsync(CancellationToken.None));
        Assert.IsTrue(await workflow.RemoveUserCatalogEntryAsync("pcs:user-notepadpp", CancellationToken.None) is false);
    }

    [TestMethod]
    public async Task TamperedRowsCannotWidenAuthority()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO stable_decisions(id,stable_product_key,kind,value,created_at) VALUES
                  ('11111111-1111-1111-1111-111111111111','registry:x','PreferredProvider','scoop','2026-10-08T00:00:00Z'),
                  ('22222222-2222-2222-2222-222222222222','registry:x','SkipVersion','1.0; rm -rf','2026-10-08T00:00:00Z'),
                  ('33333333-3333-3333-3333-333333333333','registry:x','AutomaticUpdates','Yes','2026-10-08T00:00:00Z'),
                  ('44444444-4444-4444-4444-444444444444','registry:x','CloseAppConsent','Force','2026-10-08T00:00:00Z');
                INSERT INTO user_catalog_entries(id,name,publisher,category,provider,source,package_id,scopes,created_at) VALUES
                  ('pcs:user-store','Store','V','Utilities','winget','msstore','Vendor.Store','user','2026-10-08T00:00:00Z'),
                  ('pcs:user-cmd','Cmd','V','Utilities','winget','winget','Vendor.App & calc','user','2026-10-08T00:00:00Z'),
                  ('pcs:custom','Custom','V','Utilities','winget','winget','Vendor.Custom','user','2026-10-08T00:00:00Z'),
                  ('pcs:user-choco','Choco','V','Utilities','chocolatey','chocolatey-community','tool','user','2026-10-08T00:00:00Z');
                """;
            await command.ExecuteNonQueryAsync();
        }

        ProductUpdatePolicy policy = (await new UserDecisionService(database.Store).GetPoliciesAsync(CancellationToken.None))["registry:x"];
        Assert.IsNull(policy.PreferredProvider);
        Assert.IsNull(policy.SkippedVersion);
        Assert.IsFalse(policy.AutomaticUpdates);
        Assert.IsFalse(policy.CloseAppConsent);

        using InventoryWorkflow workflow = Workflow(database, null, Catalog());
        CatalogOverview overview = (await workflow.GetCatalogOverviewAsync(false, CancellationToken.None))!;
        Assert.AreEqual(3, overview.Entries.Count, "Only bundled entries remain.");
        Assert.IsTrue(overview.RejectedUserEntries.Count >= 4, string.Join(", ", overview.RejectedUserEntries));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new UserDecisionService(database.Store).SetPreferredProviderAsync("registry:x", "scoop", null, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => new UserDecisionService(database.Store).SkipVersionAsync("registry:x", "1.0; rm", null, CancellationToken.None));
    }

    [TestMethod]
    public async Task MigrationCreatesTheUserCatalogTable()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 9; SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='user_catalog_entries';";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        Assert.IsTrue(await reader.ReadAsync());
        Assert.AreEqual(1L, reader.GetInt64(0));
        Assert.IsTrue(await reader.NextResultAsync() && await reader.ReadAsync());
        Assert.AreEqual(1L, reader.GetInt64(0));
    }

    private static CatalogAppState State(CatalogOverview overview, string id) => overview.Entries.Single(entry => entry.Entry.Id == id).State;

    private static (CanonicalProduct, InstallationInstance) Product(string key, string name, string provider, string identity, string? publisher, string? source = null, string version = "1.0")
    {
        CanonicalProduct product = new(Guid.NewGuid(), key, name);
        return (product, new(Guid.NewGuid(), product.Id, provider, identity, version, "Machine", "x64", publisher, Channel: "Stable", SourceId: source));
    }

    private static InventoryWorkflow Workflow(TestDatabase database, SourceSnapshot? source, PersonalCatalog? catalog) => source is null
        ? new(database.Store, database.Store, database.Store, Array.Empty<ILocalInventoryProvider>(), decisions: database.Store, advancedData: database.Store, catalog: catalog, userCatalog: database.Store)
        : new(database.Store, database.Store, database.Store, Array.Empty<ILocalInventoryProvider>(), decisions: database.Store, sourceCache: database.Store, updateSourceConnectors: [new FixedConnector(source)], advancedData: database.Store, catalog: catalog, userCatalog: database.Store);

    private sealed class FixedConnector(SourceSnapshot result) : IUpdateSourceConnector
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
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        public static async Task<TestDatabase> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-catalog-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path); await store.MigrateAsync(CancellationToken.None); return new TestDatabase(path, store);
        }
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }
}
