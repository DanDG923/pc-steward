using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Catalog;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 3: CRD-01/04/11, FR-CAT-04/06, FR-AUT-01, FR-RSC-01-05. Disposable SQLite and injected evidence only.</summary>
[TestClass]
public sealed class CuratedMembershipTests
{
    private const string EntryId = "pcs:widget";
    private const string ProductKey = "winget:widget-installed";
    private static readonly DateTimeOffset AddedAt = DateTimeOffset.Parse("2026-10-09T12:00:00Z", CultureInfo.InvariantCulture);
    private const string CatalogJson = """
    {"schemaVersion":1,"catalogVersion":"2026.10.09.1","entries":[
      {"id":"pcs:widget","name":"Widget","publisher":"Contoso","category":"Utilities",
       "providers":[{"provider":"winget","source":"winget","packageId":"Contoso.Widget","scopes":["user","machine"],"default":true}],
       "detection":{"arpDisplayName":"^Widget$","arpPublisher":"^Contoso$"}},
      {"id":"pcs:other","name":"Other","publisher":"Other Publisher","category":"Utilities",
       "providers":[{"provider":"winget","source":"winget","packageId":"Other.Tool","scopes":["user"],"default":true}],"detection":{}},
      {"id":"pcs:chocolatey-setup","name":"Chocolatey","publisher":"Chocolatey Software","category":"Utilities",
       "providers":[{"provider":"winget","source":"winget","packageId":"Chocolatey.Chocolatey","scopes":["machine"],"default":true}],"detection":{},"flags":{"automationAllowed":false}}
    ]}
    """;

    [TestMethod]
    public async Task MigrationIsAdditiveAndIdempotent()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await SeedLegacyStateAsync(database);
        await database.ExecuteAsync("DROP TABLE IF EXISTS managed_apps; DELETE FROM schema_migrations WHERE version > 10;");
        Assert.AreEqual(10L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        string before = await database.SnapshotRetainedAsync();

        await database.Store.MigrateAsync(CancellationToken.None);
        await database.Store.MigrateAsync(CancellationToken.None);

        Assert.AreEqual(before, await database.SnapshotRetainedAsync(), "Two migrations must preserve exact logical values of all prior tables, including raw opt-ins.");
        Assert.AreEqual(11L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"), "Membership needs additive schema version 11.");
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM managed_apps;"), "Never infer membership from retained data.");
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM pragma_foreign_key_list('managed_apps');"), "Membership must not reference disposable scan rows.");
    }

    [TestMethod]
    public async Task MigrationFailureRollsBackNewTableAndVersion()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await SeedLegacyStateAsync(database);
        await database.ExecuteAsync("DROP TABLE IF EXISTS managed_apps; DELETE FROM schema_migrations WHERE version > 10; CREATE TRIGGER reject_v11 BEFORE INSERT ON schema_migrations WHEN NEW.version=11 BEGIN SELECT RAISE(ABORT,'fixture migration rejected'); END;");
        string before = await database.SnapshotRetainedAsync();

        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.MigrateAsync(CancellationToken.None));

        Assert.AreEqual(before, await database.SnapshotRetainedAsync());
        Assert.AreEqual(10L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name='managed_apps';"));
        await database.ExecuteAsync("DROP TRIGGER reject_v11;");
        await database.Store.MigrateAsync(CancellationToken.None);
        Assert.AreEqual(11L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
    }

    [TestMethod]
    public async Task NoInventoryOrUserCatalogAutoMembership()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await SeedLegacyStateAsync(database);
        Evidence evidence = new(database.Store);
        await database.Store.MigrateAsync(CancellationToken.None);

        Assert.HasCount(0, await database.Store.GetManagedAppsAsync(CancellationToken.None));
        Assert.HasCount(0, await Service(database.Store, evidence).GetManagedAppsAsync(CancellationToken.None));
        Assert.HasCount(1, await database.Store.GetUserCatalogEntriesAsync(CancellationToken.None));
        Assert.HasCount(1, await database.Store.GetLastKnownGoodProductsAsync(CancellationToken.None));
        Assert.AreEqual(0, evidence.CallCount);
    }

    [TestMethod]
    public async Task NewMembershipDefaultsAutomaticOff()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        StableDecision legacy = Decision(EntryId, "AutomaticUpdates", "On");
        await database.Store.SaveAsync(legacy, CancellationToken.None);
        Evidence evidence = new(database.Store);

        await Service(database.Store, evidence).AddAsync(EntryId, CancellationToken.None);

        Assert.AreEqual(new ManagedAppMembership(EntryId, AddedAt), (await database.Store.GetManagedAppsAsync(CancellationToken.None)).SingleOrDefault(), "Membership must persist even when the app is missing.");
        IReadOnlyCollection<StableDecision> decisions = await database.Store.GetAllAsync(CancellationToken.None);
        Assert.IsTrue(decisions.Contains(legacy), "Raw legacy opt-ins are preserved, not copied or enabled.");
        Assert.IsTrue(decisions.Any(IsOffRestriction));
        Assert.IsFalse((await new UserDecisionService(database.Store).GetPoliciesAsync(CancellationToken.None))[EntryId].AutomaticUpdates);
    }

    [TestMethod]
    public async Task AddIsIdempotentAndKeepsOriginalMembershipTimestamp()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.Store.AddManagedAppAsync(EntryId, AddedAt, CancellationToken.None);
        await database.Store.AddManagedAppAsync(EntryId, AddedAt.AddDays(1), CancellationToken.None);
        IReadOnlyList<ManagedAppMembership> rows = await database.Store.GetManagedAppsAsync(CancellationToken.None);
        Assert.HasCount(1, rows);
        Assert.AreEqual(AddedAt, rows.Single().AddedAt);
        Assert.AreEqual(1, (await database.Store.GetAllAsync(CancellationToken.None)).Count(IsOffRestriction));
    }

    [TestMethod]
    public async Task RemoveDoesNotUninstallOrDeleteHistory()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await SeedLegacyStateAsync(database);
        await database.SeedMembershipAsync(EntryId);
        Evidence evidence = new(database.Store);
        CanonicalProduct[] beforeProducts = (await database.Store.GetLastKnownGoodProductsAsync(CancellationToken.None)).ToArray();
        InstallationInstance[] beforeInstances = (await database.Store.GetLastKnownGoodInstancesAsync(CancellationToken.None)).ToArray();
        UpdateHistoryEntry[] beforeHistory = (await database.Store.GetHistoryForProductAsync(ProductKey, CancellationToken.None)).ToArray();
        string beforeJobs = await database.SnapshotTablesAsync(["approved_update_jobs", "operation_batches", "operation_batch_items", "agreement_acceptances", "downloaded_artifacts"]);
        StableDecision[] restrictive = (await database.Store.GetAllAsync(CancellationToken.None)).Where(row => row.Kind is "PinCurrentVersion" or "SkipVersion" or "Ignore").ToArray();
        ManualMapping[] mappings = (await database.Store.GetManualMappingsAsync(CancellationToken.None)).ToArray();

        await Service(database.Store, evidence).RemoveAsync(EntryId, CancellationToken.None);

        CollectionAssert.AreEquivalent(beforeHistory.Select(row => row.EventId).ToArray(), (await database.Store.GetHistoryForProductAsync(ProductKey, CancellationToken.None)).Select(row => row.EventId).ToArray());
        CollectionAssert.AreEqual(beforeProducts, (await database.Store.GetLastKnownGoodProductsAsync(CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(beforeInstances, (await database.Store.GetLastKnownGoodInstancesAsync(CancellationToken.None)).ToArray());
        CollectionAssert.AreEquivalent(restrictive, (await database.Store.GetAllAsync(CancellationToken.None)).Where(row => row.Kind is "PinCurrentVersion" or "SkipVersion" or "Ignore").ToArray());
        CollectionAssert.AreEqual(mappings, (await database.Store.GetManualMappingsAsync(CancellationToken.None)).ToArray());
        Assert.AreEqual(beforeJobs, await database.SnapshotTablesAsync(["approved_update_jobs", "operation_batches", "operation_batch_items", "agreement_acceptances", "downloaded_artifacts"]));
        Assert.AreEqual(0, evidence.InventoryMutationCalls, "Membership must never invoke scan promotion.");
        Assert.AreEqual(0, evidence.CallCount, "The injected evidence fixture's executor sentinel must never run.");
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM managed_apps WHERE catalog_entry_id='pcs:widget';"));
        Assert.IsFalse((await database.Store.GetAllAsync(CancellationToken.None)).Any(row => row.StableProductKey == ProductKey && row.Kind == "AutomaticUpdates" && row.Value == "On"));
        Assert.IsTrue((await database.Store.GetAllAsync(CancellationToken.None)).Any(IsOffRestriction));
    }

    [TestMethod]
    public async Task RemovedEntryDoesNotReappearAfterScan()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.SeedMembershipAsync(EntryId);
        CanonicalProduct product = Product(ProductKey);
        InstallationInstance instance = Instance(product);
        await database.Store.PromoteAsync([product], [instance], CancellationToken.None);
        await Service(database.Store, new(database.Store)).RemoveAsync(EntryId, CancellationToken.None);
        await database.Store.PromoteAsync([product], [instance], CancellationToken.None);
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM managed_apps;"));
        Assert.IsTrue((await database.Store.GetAllAsync(CancellationToken.None)).Any(IsOffRestriction));
    }

    [TestMethod]
    public async Task ClearScanPreservesMembership()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.SeedMembershipAsync(EntryId);
        await database.Store.SaveAsync(Decision(EntryId, "CuratedAutomaticUpdatesOff", "Off"), CancellationToken.None);
        CanonicalProduct product = Product(ProductKey);
        await database.Store.PromoteAsync([product], [Instance(product)], CancellationToken.None);
        string before = await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]);
        Assert.IsTrue((await database.Store.ClearScanDataAsync(CancellationToken.None)).Succeeded);
        Assert.IsTrue((await database.Store.ClearScanDataAsync(CancellationToken.None)).Succeeded);
        Assert.AreEqual(before, await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]));
        Assert.HasCount(0, await database.Store.GetLastKnownGoodProductsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ActiveBatchBlocksRemoval()
    {
        RecordingMembershipStore store = new([new(EntryId, AddedAt)]);
        Evidence evidence = new();
        CuratedMembershipService service = Service(store, evidence, _ => true);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.RemoveAsync(EntryId, CancellationToken.None));
        Assert.AreEqual(0, store.RemovalCalls);
        Assert.HasCount(1, store.Rows);
    }

    [TestMethod]
    public async Task ActiveWorkBeginningDuringAwaitedValidationBlocksAddAndRemove()
    {
        RecordingMembershipStore store = new([new(EntryId, AddedAt)]);
        bool active = false;
        Evidence evidence = new() { AfterCatalogRead = () => active = true };
        CuratedMembershipService service = Service(store, evidence, _ => active);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.AddAsync(EntryId, CancellationToken.None));
        active = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.RemoveAsync(EntryId, CancellationToken.None));
        Assert.AreEqual(0, store.AdditionCalls + store.RemovalCalls);
    }

    [TestMethod]
    public async Task RemovalDerivesOnlyExactCurrentProductKeys()
    {
        RecordingMembershipStore store = new([new(EntryId, AddedAt)]);
        CanonicalProduct exact = Product(ProductKey);
        CanonicalProduct near = Product("winget:widget-installed-copy");
        CanonicalProduct weak = Product("registry:name-only");
        CanonicalProduct conflict = Product("registry:conflict");
        Evidence evidence = new()
        {
            Products = [exact, near, weak, conflict],
            Instances = [Instance(exact), Instance(near) with { ProviderIdentity = "Contoso.Widget.Copy", Publisher = null },
                Instance(weak) with { Provider = "registry", ProviderIdentity = "local-widget", Publisher = null, SourceId = null },
                Instance(conflict), Instance(conflict) with { Id = Guid.NewGuid(), ProviderIdentity = "Other.Tool" }]
        };
        await Service(store, evidence).RemoveAsync(EntryId, CancellationToken.None);
        Assert.AreEqual(1, store.RemovalCalls);
        CollectionAssert.AreEquivalent(new[] { ProductKey }, store.RemovedPolicyKeys.ToArray(), "No arbitrary supplied key, near-name, missing publisher, conflicting or stale association.");
    }

    [TestMethod]
    public async Task RemovalKeepsSameNameAndPublisherDifferentPackageOptIns()
    {
        // Task 3 review I1 (CRD-04, FR-AUT-01): name/publisher evidence is not identity evidence for policy mutation,
        // and a product whose instances name a contradictory package identity is not uniquely associated.
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.SeedMembershipAsync(EntryId);
        const string CopyKey = "winget:widget-copy";
        const string MixedKey = "winget:widget-mixed";
        CanonicalProduct exact = Product(ProductKey);
        CanonicalProduct copy = Product(CopyKey);
        CanonicalProduct mixed = Product(MixedKey);
        await database.Store.PromoteAsync([exact, copy, mixed], [
            Instance(exact) with { Publisher = "Contoso" },
            Instance(copy) with { ProviderIdentity = "Contoso.Widget.Copy", Publisher = "Contoso" },
            Instance(mixed) with { Publisher = "Contoso" },
            Instance(mixed) with { Id = Guid.NewGuid(), ProviderIdentity = "Contoso.Widget.Copy", Publisher = "Contoso" }], CancellationToken.None);
        foreach (string key in new[] { ProductKey, CopyKey, MixedKey })
            await database.Store.SaveAsync(Decision(key, "AutomaticUpdates", "On"), CancellationToken.None);

        await Service(database.Store, new(database.Store)).RemoveAsync(EntryId, CancellationToken.None);

        IReadOnlyCollection<StableDecision> rows = await database.Store.GetAllAsync(CancellationToken.None);
        Assert.IsFalse(rows.Any(row => row.StableProductKey == ProductKey && row.Kind == "AutomaticUpdates" && row.Value == "On"), "The identity-associated product's opt-in is reversed.");
        Assert.IsTrue(rows.Any(row => row.StableProductKey == CopyKey && row.Kind == "AutomaticUpdates" && row.Value == "On"), "A different package with the same name and publisher keeps its opt-in.");
        Assert.IsTrue(rows.Any(row => row.StableProductKey == MixedKey && row.Kind == "AutomaticUpdates" && row.Value == "On"), "A product with a contradictory package identity keeps its opt-in.");
    }

    [TestMethod]
    public async Task RemovalReversesOnlyExactAutomaticOnRows()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.SeedMembershipAsync(EntryId);
        foreach (string key in new[] { EntryId, ProductKey, ProductKey + "-copy", "pcs:other" })
        {
            await database.Store.SaveAsync(Decision(key, "AutomaticUpdates", "On"), CancellationToken.None);
            await database.Store.SaveAsync(Decision(key, "SkipVersion", "1.1"), CancellationToken.None);
        }
        await database.Store.RemoveManagedAppAndDisableAutomaticAsync(EntryId, [ProductKey], CancellationToken.None);
        IReadOnlyCollection<StableDecision> rows = await database.Store.GetAllAsync(CancellationToken.None);
        Assert.IsFalse(rows.Any(row => (row.StableProductKey == EntryId || row.StableProductKey == ProductKey) && row.Kind == "AutomaticUpdates" && row.Value == "On"));
        Assert.AreEqual(2, rows.Count(row => row.Kind == "AutomaticUpdates" && row.Value == "On"));
        Assert.AreEqual(4, rows.Count(row => row.Kind == "SkipVersion"));
        Assert.IsTrue(rows.Any(IsOffRestriction));
    }

    [TestMethod]
    public async Task InvalidCatalogAndReservedOrUnknownIdsRejectMutations()
    {
        RecordingMembershipStore store = new();
        Evidence evidence = new();
        CuratedMembershipService service = Service(store, evidence);
        foreach (string id in new[] { "pcs:unknown", ChocolateySetupPreparation.CatalogEntryId, "PCS:widget", "pcs:widget;DELETE" })
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.AddAsync(id, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.RemoveAsync(id, CancellationToken.None));
        }
        evidence.Json = "{invalid}";
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.AddAsync(EntryId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.RemoveAsync(EntryId, CancellationToken.None));
        Assert.AreEqual(0, store.AdditionCalls + store.RemovalCalls);
    }

    [TestMethod]
    public async Task ReadsRevalidateCatalogAndKeepOrphansAndRejectedUserRowsWithheld()
    {
        RecordingMembershipStore store = new([new(EntryId, AddedAt), new("pcs:orphan", AddedAt), new("pcs:user-tampered", AddedAt), new(ChocolateySetupPreparation.CatalogEntryId, AddedAt)]);
        Evidence evidence = new()
        {
            UserRows = [new("pcs:user-tampered", "Tampered", "Unknown", "Utilities", "winget", "msstore", "Unknown.Tool", ["user"])]
        };
        CuratedMembershipService service = Service(store, evidence);
        IReadOnlyList<CuratedMembershipEntry> first = await service.GetManagedAppsAsync(CancellationToken.None);
        Assert.HasCount(4, first);
        Assert.IsTrue(first.Single(row => row.Membership.CatalogEntryId == EntryId).IsValidated);
        Assert.IsTrue(first.Where(row => row.Membership.CatalogEntryId != EntryId).All(row => !row.IsValidated && row.Entry is null && row.WithheldReason is not null));
        evidence.Json = "{invalid}";
        IReadOnlyList<CuratedMembershipEntry> second = await service.GetManagedAppsAsync(CancellationToken.None);
        Assert.HasCount(4, second);
        Assert.IsTrue(second.All(row => !row.IsValidated && row.Entry is null && row.WithheldReason is not null));
        Assert.AreEqual(2, evidence.CatalogReads, "A prior valid catalog cannot authorize a later read.");
    }

    [TestMethod]
    public async Task AddRevalidatesCurrentUserCatalogComposition()
    {
        RecordingMembershipStore store = new();
        Evidence evidence = new()
        {
            UserRows = [new("pcs:user-new", "New Tool", "Publisher", "Utilities", "winget", "winget", "Publisher.New", ["user"])]
        };
        CuratedMembershipService service = Service(store, evidence);
        await service.AddAsync("pcs:user-new", CancellationToken.None);
        Assert.AreEqual(1, store.AdditionCalls);
        evidence.UserRows = [evidence.UserRows.Single() with { Source = "msstore" }];
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => service.AddAsync("pcs:user-new", CancellationToken.None));
        Assert.AreEqual(1, store.AdditionCalls);
        Assert.AreEqual(2, evidence.CatalogReads);
    }

    [TestMethod]
    public async Task CancellationAfterAwaitedCatalogLeavesMembershipUntouched()
    {
        RecordingMembershipStore store = new([new(EntryId, AddedAt)]);
        using CancellationTokenSource cancellation = new();
        Evidence evidence = new() { AfterCatalogRead = cancellation.Cancel };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Service(store, evidence).RemoveAsync(EntryId, cancellation.Token));
        Assert.AreEqual(0, store.RemovalCalls);
        Assert.HasCount(1, store.Rows);
    }

    [TestMethod]
    public async Task RemovalRollsBackMembershipAndDecisionsIfRestrictionWriteFails()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.SeedMembershipAsync(EntryId);
        await database.Store.SaveAsync(Decision(ProductKey, "AutomaticUpdates", "On"), CancellationToken.None);
        await database.ExecuteAsync("CREATE TRIGGER reject_curated_off BEFORE INSERT ON stable_decisions WHEN NEW.kind='CuratedAutomaticUpdatesOff' BEGIN SELECT RAISE(ABORT,'fixture restriction rejected'); END;");
        string before = await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.RemoveManagedAppAndDisableAutomaticAsync(EntryId, [ProductKey], CancellationToken.None));
        Assert.AreEqual(before, await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]));
    }

    [TestMethod]
    public async Task PrecancelledStoreMutationLeavesBothCategoriesUntouched()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.SeedMembershipAsync(EntryId);
        string before = await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => database.Store.AddManagedAppAsync("pcs:other", AddedAt, cancellation.Token));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => database.Store.RemoveManagedAppAndDisableAutomaticAsync(EntryId, [ProductKey], cancellation.Token));
        Assert.AreEqual(before, await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]));
    }

    [TestMethod]
    public async Task MembershipOperationsHaveNoExecutorOrInventoryMutation()
    {
        RecordingMembershipStore store = new();
        Evidence fakeExecutor = new();
        CuratedMembershipService service = Service(store, fakeExecutor);
        await service.AddAsync(EntryId, CancellationToken.None);
        await service.RemoveAsync(EntryId, CancellationToken.None);
        Assert.AreEqual(0, fakeExecutor.CallCount);
        Assert.AreEqual(0, fakeExecutor.InventoryMutationCalls);
    }

    [TestMethod]
    public async Task FailedAddRollsBackMembershipAndDoesNotDestroyLegacyOn()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.ExecuteAsync("CREATE TABLE IF NOT EXISTS managed_apps(catalog_entry_id TEXT PRIMARY KEY, added_at TEXT NOT NULL); CREATE TRIGGER reject_curated_add_off BEFORE INSERT ON stable_decisions WHEN NEW.kind='CuratedAutomaticUpdatesOff' BEGIN SELECT RAISE(ABORT,'fixture restriction rejected'); END;");
        await database.Store.SaveAsync(Decision(EntryId, "AutomaticUpdates", "On"), CancellationToken.None);
        string before = await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.AddManagedAppAsync(EntryId, AddedAt, CancellationToken.None));
        Assert.AreEqual(before, await database.SnapshotTablesAsync(["managed_apps", "stable_decisions"]));
    }

    [TestMethod]
    public async Task OffOverrideSuppressesOnlyItsExactLegacyAutomaticKey()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.Store.SaveAsync(Decision(EntryId, "AutomaticUpdates", "On"), CancellationToken.None);
        await database.Store.SaveAsync(Decision(EntryId, "CuratedAutomaticUpdatesOff", "Off"), CancellationToken.None);
        await database.Store.SaveAsync(Decision("pcs:other", "AutomaticUpdates", "On"), CancellationToken.None);
        IReadOnlyDictionary<string, ProductUpdatePolicy> policies = await new UserDecisionService(database.Store).GetPoliciesAsync(CancellationToken.None);
        Assert.IsTrue(policies["pcs:other"].AutomaticUpdates, "Preserve unrelated raw legacy policy interpretation until Task 4 reports execution Unavailable.");
        Assert.IsFalse(policies[EntryId].AutomaticUpdates, "A restrictive curated Off dominates a legacy On.");
    }

    [TestMethod]
    public async Task MalformedStoredMembershipStaysVisibleButWithheld()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.ExecuteAsync("CREATE TABLE IF NOT EXISTS managed_apps(catalog_entry_id TEXT PRIMARY KEY, added_at TEXT NOT NULL); INSERT INTO managed_apps VALUES ('pcs:widget','invalid timestamp'), ('pcs:unknown','2026-10-09T12:00:00.0000000+00:00'), ('PCS:widget','2026-10-09T12:00:00.0000000+00:00');");
        IReadOnlyList<CuratedMembershipEntry> rows = await Service(database.Store, new(database.Store)).GetManagedAppsAsync(CancellationToken.None);
        Assert.HasCount(3, rows, "Malformed and orphaned rows must not disappear from the owner view.");
        Assert.IsTrue(rows.All(row => !row.IsValidated && row.Entry is null && row.WithheldReason is not null));
    }

    [TestMethod]
    public async Task OffRestrictionCannotBeOverriddenByLaterLegacyOnOrExpiry()
    {
        await using MembershipDatabase database = await MembershipDatabase.CreateAsync();
        await database.Store.SaveAsync(new StableDecision(Guid.NewGuid(), EntryId, "CuratedAutomaticUpdatesOff", "Off", AddedAt, null, AddedAt.AddDays(-1)), CancellationToken.None);
        await database.Store.SaveAsync(new StableDecision(Guid.NewGuid(), EntryId, "AutomaticUpdates", "On", AddedAt.AddDays(1)), CancellationToken.None);
        Assert.IsFalse((await new UserDecisionService(database.Store).GetPoliciesAsync(CancellationToken.None))[EntryId].AutomaticUpdates);
    }

    [TestMethod]
    public async Task FinalActiveGuardAndCancellationRunAfterInventoryAwait()
    {
        RecordingMembershipStore store = new([new(EntryId, AddedAt)]);
        bool active = false;
        Evidence evidence = new() { AfterInstancesRead = () => active = true };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Service(store, evidence, _ => active).RemoveAsync(EntryId, CancellationToken.None));
        using CancellationTokenSource cancellation = new();
        Evidence cancelledEvidence = new() { AfterInstancesRead = cancellation.Cancel };
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => Service(store, cancelledEvidence).RemoveAsync(EntryId, cancellation.Token));
        Assert.AreEqual(0, store.RemovalCalls);
    }

    private static CuratedMembershipService Service(ICuratedMembershipStore store, Evidence evidence, Func<string, bool>? active = null) =>
        new(store, evidence.LoadCatalogAsync, evidence, active ?? (_ => false), () => AddedAt);
    private static CanonicalProduct Product(string key) => new(Guid.NewGuid(), key, "Widget");
    private static InstallationInstance Instance(CanonicalProduct product) => new(Guid.NewGuid(), product.Id, "winget", "Contoso.Widget", "1.0", "User", "x64", SourceId: "winget");
    private static StableDecision Decision(string key, string kind, string value) => new(Guid.NewGuid(), key, kind, value, AddedAt, "retained fixture");
    private static bool IsOffRestriction(StableDecision row) => row.StableProductKey == EntryId && row.Kind == "CuratedAutomaticUpdatesOff" && row.Value == "Off";

    private static async Task SeedLegacyStateAsync(MembershipDatabase database)
    {
        CanonicalProduct product = Product(ProductKey);
        await database.Store.PromoteAsync([product], [Instance(product)], CancellationToken.None);
        await database.Store.SaveConnectorStateAsync("WinGet installed inventory", new(true, true), CancellationToken.None);
        await database.Store.SaveScheduledCheckSettingsAsync(new(ScheduleCadence.Weekly, true, true, DayOfWeek.Friday, new TimeOnly(18, 30)), CancellationToken.None);
        foreach ((string kind, string value) in new[] { ("PinCurrentVersion", "1.0"), ("SkipVersion", "1.1"), ("Ignore", "Indefinitely"), ("AutomaticUpdates", "On") })
            await database.Store.SaveAsync(Decision(ProductKey, kind, value), CancellationToken.None);
        await database.Store.SaveAsync(Decision(EntryId, "AutomaticUpdates", "On"), CancellationToken.None);
        await database.Store.SaveManualMappingAsync(new(Guid.NewGuid(), ProductKey, "winget", "Contoso.Widget", AddedAt, "exact fixture"), CancellationToken.None);
        await database.Store.SaveUserCatalogEntryAsync(new("pcs:user-extra", "Extra", "Publisher", "Utilities", "winget", "winget", "Publisher.Extra", ["user"]), AddedAt, CancellationToken.None);
        UpdateJobSelection selection = new(ProductKey, UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");
        ImmutableUpdateJob job = await new UpdateJobApprovalService(database.Store).ConfirmAsync(new(Guid.NewGuid(), Guid.NewGuid(), selection), AddedAt, CancellationToken.None);
        ApprovedOperationBatch batch = new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [new UpdateJobSelection("winget:other", UpdateProvider.WinGet, UpdateSource.WinGet, "Other.Tool", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")]).Confirm(AddedAt);
        await database.Store.SaveApprovedBatchAsync(batch, CancellationToken.None);
        await database.Store.RecordAsync(new(Guid.NewGuid(), job.Id, job.CorrelationId, ProductKey, UpdateProvider.WinGet, "Contoso.Widget", "1.0", "1.1", AddedAt, UpdateHistoryStatus.Success, 0, "verified fixture"), CancellationToken.None);
        await database.Store.RecordAgreementAcceptanceAsync(new(Guid.NewGuid(), UpdateSource.WinGet, "Contoso.Widget", "1.1", new string('A', 64), AddedAt), CancellationToken.None);
        await database.Store.SaveDownloadedArtifactAsync(new(Guid.NewGuid(), "update", "fixture.bin", 512, AddedAt), CancellationToken.None);
    }

    private sealed class RecordingMembershipStore(IReadOnlyList<ManagedAppMembership>? rows = null) : ICuratedMembershipStore
    {
        public List<ManagedAppMembership> Rows { get; } = rows?.ToList() ?? [];
        public int AdditionCalls { get; private set; }
        public int RemovalCalls { get; private set; }
        public IReadOnlyList<string> RemovedPolicyKeys { get; private set; } = [];
        public Task<IReadOnlyList<ManagedAppMembership>> GetManagedAppsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ManagedAppMembership>>(Rows.ToArray());
        public Task AddManagedAppAsync(string catalogEntryId, DateTimeOffset addedAt, CancellationToken cancellationToken)
        {
            AdditionCalls++;
            if (!Rows.Any(row => row.CatalogEntryId == catalogEntryId)) Rows.Add(new(catalogEntryId, addedAt));
            return Task.CompletedTask;
        }
        public Task RemoveManagedAppAndDisableAutomaticAsync(string catalogEntryId, IReadOnlyList<string> uniquelyAssociatedPolicyKeys, CancellationToken cancellationToken)
        {
            RemovalCalls++;
            RemovedPolicyKeys = uniquelyAssociatedPolicyKeys.ToArray();
            Rows.RemoveAll(row => row.CatalogEntryId == catalogEntryId);
            return Task.CompletedTask;
        }
    }

    private sealed class Evidence(SqliteDatabase? database = null) : IInventorySnapshotStore, IOperationExecutor
    {
        public string Json { get; set; } = CatalogJson;
        public IReadOnlyList<UserCatalogEntryRecord> UserRows { get; set; } = [];
        public IReadOnlyCollection<CanonicalProduct> Products { get; init; } = [];
        public IReadOnlyCollection<InstallationInstance> Instances { get; init; } = [];
        public int CatalogReads { get; private set; }
        public int InventoryMutationCalls { get; private set; }
        public int CallCount { get; private set; }
        public Action? AfterCatalogRead { get; init; }
        public Action? AfterInstancesRead { get; init; }
        public async Task<PersonalCatalog?> LoadCatalogAsync(CancellationToken cancellationToken)
        {
            CatalogReads++;
            // Revalidate bytes and current retained user rows every time. Never return a cached trusted object.
            CatalogLoadResult loaded = CatalogValidator.Validate(Encoding.UTF8.GetBytes(Json));
            IReadOnlyCollection<UserCatalogEntryRecord> users = database is null ? UserRows : await database.GetUserCatalogEntriesAsync(cancellationToken);
            await Task.Yield();
            AfterCatalogRead?.Invoke();
            return loaded.Catalog is null ? null : CatalogValidator.Compose(loaded.Catalog, users).Catalog;
        }
        public Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken) => database?.GetLastKnownGoodProductsAsync(cancellationToken) ?? Task.FromResult(Products);
        public async Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken)
        {
            IReadOnlyCollection<InstallationInstance> result = database is null ? Instances : await database.GetLastKnownGoodInstancesAsync(cancellationToken);
            await Task.Yield();
            AfterInstancesRead?.Invoke();
            return result;
        }
        public Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken)
        {
            InventoryMutationCalls++;
            return Task.CompletedTask;
        }
        public Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "fixture sentinel only"));
        }
    }

    internal sealed class MembershipDatabase : IAsyncDisposable
    {
        private readonly string path;
        private MembershipDatabase(string path) { this.path = path; Store = new(path); }
        public SqliteDatabase Store { get; }
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        public static async Task<MembershipDatabase> CreateAsync()
        {
            MembershipDatabase database = new(Path.Combine(Path.GetTempPath(), $"pcsteward-membership-{Guid.NewGuid():N}.db"));
            await database.Store.MigrateAsync(CancellationToken.None);
            return database;
        }
        public async Task ExecuteAsync(string sql)
        {
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        public async Task<long> ScalarAsync(string sql)
        {
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }
        public Task SeedMembershipAsync(string id) => ExecuteAsync("CREATE TABLE IF NOT EXISTS managed_apps(catalog_entry_id TEXT PRIMARY KEY, added_at TEXT NOT NULL); INSERT INTO managed_apps VALUES ('" + id + "','2026-10-09T12:00:00.0000000+00:00');");
        public Task<string> SnapshotRetainedAsync() => SnapshotTablesAsync(["canonical_products", "installation_instances", "stable_decisions", "manual_mappings", "app_metadata", "update_jobs", "approved_update_jobs", "update_job_history", "update_job_execution_state", "operation_batches", "operation_batch_items", "agreement_acceptances", "user_catalog_entries", "downloaded_artifacts"]);
        public async Task<string> SnapshotTablesAsync(IReadOnlyList<string> tables)
        {
            List<string> logicalRows = [];
            await using SqliteConnection connection = new(ConnectionString);
            await connection.OpenAsync();
            foreach (string table in tables)
            {
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "SELECT * FROM " + table + ";";
                await using SqliteDataReader reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    object?[] values = new object?[reader.FieldCount];
                    for (int index = 0; index < values.Length; index++) values[index] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                    logicalRows.Add(table + ":" + JsonSerializer.Serialize(values));
                }
            }
            logicalRows.Sort(StringComparer.Ordinal);
            return string.Join('\n', logicalRows);
        }
        public ValueTask DisposeAsync()
        {
            // Only three exact files under the disposable temp fixture prefix; no recursive removal.
            File.Delete(path);
            File.Delete(path + "-wal");
            File.Delete(path + "-shm");
            return ValueTask.CompletedTask;
        }
    }
}
