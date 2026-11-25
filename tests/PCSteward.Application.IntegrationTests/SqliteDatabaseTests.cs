using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class SqliteDatabaseTests
{
    [TestMethod]
    public async Task UnifiedConnectorConsentRoundTripsAsOnePersistedStateChange()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        Dictionary<string, ConnectorState> states = new()
        {
            ["WinGet installed inventory"] = new(true, true),
            ["WinGet package sources"] = new(true, true)
        };

        await database.Store.SaveConnectorStatesAsync(states, CancellationToken.None);

        Assert.AreEqual(states["WinGet installed inventory"], await database.Store.GetConnectorStateAsync("WinGet installed inventory", false, CancellationToken.None));
        Assert.AreEqual(states["WinGet package sources"], await database.Store.GetConnectorStateAsync("WinGet package sources", false, CancellationToken.None));
    }

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
    public async Task DecisionReasonsExpiryMappingsAndReversalPersistOutsideScanData()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync(); DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(1);
        StableDecision decision = new(Guid.NewGuid(), "winget:contoso.widget", "Ignore", "until", DateTimeOffset.UtcNow, "Busy", expiry);
        await database.Store.SaveAsync(decision, CancellationToken.None); await database.Store.SaveManualMappingAsync(new ManualMapping(Guid.NewGuid(), decision.StableProductKey, "winget", "Contoso.Widget", DateTimeOffset.UtcNow, "exact package"), CancellationToken.None);
        StableDecision restored = (await database.Store.GetAllAsync(CancellationToken.None)).Single(); ManualMapping mapping = (await database.Store.GetManualMappingsAsync(CancellationToken.None)).Single();
        Assert.AreEqual("Busy", restored.Reason); Assert.AreEqual(expiry, restored.ExpiresAt); Assert.AreEqual("exact package", mapping.Reason); Assert.HasCount(1, await database.Store.GetTimelineAsync(decision.StableProductKey, CancellationToken.None));
        await database.Store.DeleteAsync(decision.Id, CancellationToken.None); await database.Store.DeleteManualMappingAsync(mapping.Id, CancellationToken.None);
        Assert.HasCount(0, await database.Store.GetAllAsync(CancellationToken.None)); Assert.HasCount(0, await database.Store.GetManualMappingsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ClearScanDataIsTransactionalIdempotentAndPreservesDecisionsHistoryMappingsAndArtifacts()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CanonicalProduct product = new(Guid.NewGuid(), "winget:contoso.widget", "Contoso Widget");
        await database.Store.PromoteAsync([product], [new InstallationInstance(Guid.NewGuid(), product.Id, "registry", "contoso-widget", "1.0", "User", "x64")], CancellationToken.None);
        await database.Store.SaveAsync(new StableDecision(Guid.NewGuid(), product.StableKey, "Pin", "1.0", DateTimeOffset.UtcNow), CancellationToken.None);
        await database.Store.SaveManualMappingAsync(new ManualMapping(Guid.NewGuid(), product.StableKey, "winget", "Contoso.Widget", DateTimeOffset.UtcNow), CancellationToken.None);
        UpdateJobSelection updateSelection = new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");
        await new UpdateJobApprovalService(database.Store).ConfirmAsync(new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), updateSelection), DateTimeOffset.UtcNow, CancellationToken.None);
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
    public async Task AdvancedDataControlsClearOnlyTheirIndependentCategories()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await database.Store.SaveSourceSnapshotAsync(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.Widget", "Widget", null, "1.0", "1.1", "User", "x64", "Stable")], []), CancellationToken.None);
        await database.Store.SaveAsync(new StableDecision(Guid.NewGuid(), "winget:contoso.widget", "Pin", "1.0", DateTimeOffset.UtcNow), CancellationToken.None);
        await database.Store.SaveManualMappingAsync(new ManualMapping(Guid.NewGuid(), "winget:contoso.widget", "winget", "Contoso.Widget", DateTimeOffset.UtcNow), CancellationToken.None);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(DateTimeOffset.UtcNow);
        await database.Store.RecordAsync(new UpdateHistoryEntry(Guid.NewGuid(), job.Id, job.CorrelationId, job.Selection.StableProductKey, job.Selection.Provider, job.Selection.PackageId, "1.0", "1.1", DateTimeOffset.UtcNow, UpdateHistoryStatus.Success, 0, "verified"), CancellationToken.None);

        Assert.IsTrue((await database.Store.ClearSourceMetadataAsync(CancellationToken.None)).Succeeded);
        Assert.IsNull(await database.Store.GetSourceSnapshotAsync("winget", CancellationToken.None));
        Assert.HasCount(1, await database.Store.GetAllAsync(CancellationToken.None));
        Assert.IsTrue((await database.Store.ClearUpdateHistoryAsync(CancellationToken.None)).Succeeded);
        Assert.HasCount(0, await database.Store.GetHistoryForProductAsync("winget:contoso.widget", CancellationToken.None));
        Assert.IsTrue((await database.Store.ClearMappingsAndPoliciesAsync(CancellationToken.None)).Succeeded);
        Assert.HasCount(0, await database.Store.GetAllAsync(CancellationToken.None));
        Assert.HasCount(0, await database.Store.GetManualMappingsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ScheduledCheckSettingsPersistIndependentlyAndManualModeCannotCreateASchedule()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ScheduledCheckSettings requested = new(ScheduleCadence.Weekly, true, true, DayOfWeek.Friday, new TimeOnly(18, 30));
        await database.Store.SaveScheduledCheckSettingsAsync(requested, CancellationToken.None);
        Assert.AreEqual(requested, await database.Store.GetScheduledCheckSettingsAsync(CancellationToken.None));

        await database.Store.SaveScheduledCheckSettingsAsync(ScheduledCheckSettings.Manual with { IsEnabled = true, NotificationsEnabled = true }, CancellationToken.None);
        ScheduledCheckSettings manual = await database.Store.GetScheduledCheckSettingsAsync(CancellationToken.None);
        Assert.AreEqual(ScheduleCadence.Manual, manual.Cadence);
        Assert.IsFalse(manual.IsEnabled);
        Assert.IsTrue(manual.NotificationsEnabled);

        await database.Store.ClearScanDataAsync(CancellationToken.None);
        Assert.AreEqual(manual, await database.Store.GetScheduledCheckSettingsAsync(CancellationToken.None));

        await database.Store.SaveScheduledCheckStatusAsync(new ScheduledCheckStatus(DateTimeOffset.Parse("2026-07-29T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture), "Completed", 0), CancellationToken.None);
        ScheduledCheckStatus status = await database.Store.GetScheduledCheckStatusAsync(CancellationToken.None);
        Assert.AreEqual("Completed", status.LastResult);
        Assert.AreEqual(0, status.ConsecutiveFailures);
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
    public async Task BothWinGetConnectorDisclosuresPersistAndFailClosedWhenRevoked()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        foreach (string connector in new[] { "WinGet installed inventory", "WinGet package sources" })
        {
            await database.Store.SaveConnectorStateAsync(connector, new PCSteward.Application.ConnectorState(true, true), CancellationToken.None);
            PCSteward.Application.ConnectorState approved = await database.Store.GetConnectorStateAsync(connector, false, CancellationToken.None);
            Assert.IsTrue(approved.Enabled, connector);
            Assert.IsTrue(approved.SourceDisclosureApproved, connector);

            await database.Store.SaveConnectorStateAsync(connector, new PCSteward.Application.ConnectorState(true, false), CancellationToken.None);
            PCSteward.Application.ConnectorState revoked = await database.Store.GetConnectorStateAsync(connector, false, CancellationToken.None);
            Assert.IsFalse(revoked.Enabled, connector);
            Assert.IsFalse(revoked.SourceDisclosureApproved, connector);
        }
    }

    [TestMethod]
    public async Task TypedInstallationMetadataSurvivesPersistence()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync(); Guid productId = Guid.NewGuid();
        InstallationInstance instance = new(Guid.NewGuid(), productId, "registry", "contoso", "1", "Machine", "x64", "Contoso", "20260728", "C:\\Program Files\\Contoso", "Stable", "Application", "winget");
        await database.Store.PromoteAsync([new CanonicalProduct(productId, "contoso", "Contoso")], [instance], CancellationToken.None);
        InstallationInstance restored = (await database.Store.GetLastKnownGoodInstancesAsync(CancellationToken.None)).Single();
        Assert.AreEqual(instance.Publisher, restored.Publisher); Assert.AreEqual(instance.InstallDate, restored.InstallDate); Assert.AreEqual(instance.InstallPath, restored.InstallPath); Assert.AreEqual(instance.Channel, restored.Channel); Assert.AreEqual(instance.Classification, restored.Classification); Assert.AreEqual(instance.SourceId, restored.SourceId);
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

    [TestMethod]
    public async Task SourceCacheKeepsWhatTheSourceSaidAboutEachInstaller()
    {
        // Migration 16 (owner's PC, 2026-11-19): an update held back for its installer stays held back when the results are cached.
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SourcePackageRecord[] packages =
        [
            new("winget", "Contoso.Fine", "Fine", "Contoso", "1.0", "1.1", "Machine", "X64", "Stable"),
            new("winget", "Contoso.Zip", "Zip", "Contoso", "1.0", "1.1", "Machine", "X64", "Stable", InstallerFit: SourceInstallerFit.InstallerNotSupported),
            new("winget", "Contoso.User", "User", "Contoso", "1.0", "1.1", "User", "Unknown", "Stable", InstallerFit: SourceInstallerFit.NoInstallerForInstalledScope),
            // Migration 18 (owner decision 2026-11-25): an app that updates itself stays held back from the cache too.
            new("winget", "Fabrikam.Chat", "Chat", "Fabrikam", "1.0", "1.1", "User", "X64", "Stable", InstallerFit: SourceInstallerFit.UpdatesItself)
        ];
        await database.Store.SaveSourceSnapshotAsync(new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, packages, []), CancellationToken.None);

        SourceSnapshot restored = (await database.Store.GetSourceSnapshotAsync("winget", CancellationToken.None))!;

        foreach (SourcePackageRecord package in packages)
            Assert.AreEqual(package.InstallerFit, restored.Packages.Single(item => item.PackageId == package.PackageId).InstallerFit, package.PackageId);
        Assert.HasCount(packages.Length, restored.Packages);
    }

    [TestMethod]
    public async Task SourceCachePersistsChocolateyCommunitySnapshots()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync(); DateTimeOffset timestamp = DateTimeOffset.UnixEpoch;
        SourceSnapshot snapshot = new("chocolatey-community", "v2.4", timestamp, SourceSnapshotState.Live, [new SourcePackageRecord("chocolatey-community", "contoso.widget", "Contoso Widget", null, "1.0.0", "1.1.0", "Machine", "Neutral", null)], []);

        await database.Store.SaveSourceSnapshotAsync(snapshot, CancellationToken.None);

        SourceSnapshot restored = (await database.Store.GetSourceSnapshotAsync("chocolatey-community", CancellationToken.None))!;
        Assert.AreEqual(SourceSnapshotState.Cached, restored.State); Assert.AreEqual(timestamp, restored.LastSuccessAt); Assert.AreEqual("contoso.widget", restored.Packages.Single().PackageId);
    }

    [TestMethod]
    public async Task SourceCacheRetentionPrunesOldSnapshotsAndTheirPackageRows()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await database.Store.SaveSourceSnapshotAsync(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow.AddDays(-31), SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.Widget", "Widget", null, "1.0", "1.1", "Machine", "x64", "Stable")], []), CancellationToken.None);

        await database.Store.PruneSourceSnapshotsAsync(DateTimeOffset.UtcNow.AddDays(-30), CancellationToken.None);

        Assert.IsNull(await database.Store.GetSourceSnapshotAsync("winget", CancellationToken.None));
    }

    [TestMethod]
    public async Task MigrationEighteenKeepsCachedRowsAndTakesAnAppThatUpdatesItself()
    {
        // Migration 18 (owner decision 2026-11-25): a database from before it has the cache column's CHECK of 0 to 2.
        string path = Path.Combine(Path.GetTempPath(), $"pcsteward-m18-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            await store.SaveSourceSnapshotAsync(new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live,
                [new SourcePackageRecord("winget", "Contoso.User", "User", "Contoso", "1.0", "1.1", "User", "Unknown", "Stable", InstallerFit: SourceInstallerFit.NoInstallerForInstalledScope)], []), CancellationToken.None);
            await using (SqliteConnection connection = new($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE source_package_cache DROP COLUMN installer_fit; ALTER TABLE source_package_cache ADD COLUMN installer_fit INTEGER NOT NULL DEFAULT 0 CHECK(installer_fit IN (0,1,2)); " +
                    "UPDATE source_package_cache SET installer_fit = 2; DELETE FROM schema_migrations WHERE version = 18;";
                await command.ExecuteNonQueryAsync();
            }

            await store.MigrateAsync(CancellationToken.None);

            Assert.AreEqual(SourceInstallerFit.NoInstallerForInstalledScope, (await store.GetSourceSnapshotAsync("winget", CancellationToken.None))!.Packages.Single().InstallerFit);
            await store.SaveSourceSnapshotAsync(new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live,
                [new SourcePackageRecord("winget", "Fabrikam.Chat", "Chat", "Fabrikam", "1.0", "1.1", "User", "X64", "Stable", InstallerFit: SourceInstallerFit.UpdatesItself)], []), CancellationToken.None);
            Assert.AreEqual(SourceInstallerFit.UpdatesItself, (await store.GetSourceSnapshotAsync("winget", CancellationToken.None))!.Packages.Single().InstallerFit);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task MalformedSnapshotIdentifiersAreSkippedInsteadOfCrashingStartupReads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pcsteward-malformed-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            await using (SqliteConnection connection = new($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "INSERT INTO canonical_products(id,stable_key,display_name) VALUES ('not-a-guid','bad','Bad'); INSERT INTO installation_instances(id,product_id,provider,provider_identity,version,scope,architecture,classification) VALUES ('also-bad','not-a-guid','registry','bad','1','User','x64','Unknown');";
                await command.ExecuteNonQueryAsync();
            }

            Assert.HasCount(0, await store.GetLastKnownGoodProductsAsync(CancellationToken.None));
            Assert.HasCount(0, await store.GetLastKnownGoodInstancesAsync(CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public async Task DownloadedArtifactLedgerPersistsOwnershipStateHashAndLastCleanedTime()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync(); Guid jobId = Guid.NewGuid();
        DownloadedArtifact artifact = new(Guid.NewGuid(), "update", "payload.bin", 512, DateTimeOffset.UtcNow, "aabbcc", jobId, DownloadedArtifactOutcome.VerifiedSuccess, true);
        await database.Store.SaveDownloadedArtifactAsync(artifact, CancellationToken.None); await database.Store.SetDownloadedArtifactsLastCleanedAtAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        DownloadedArtifact restored = (await database.Store.GetDownloadedArtifactsAsync(CancellationToken.None)).Single();

        Assert.AreEqual(artifact, restored); Assert.IsNotNull(await database.Store.GetDownloadedArtifactsLastCleanedAtAsync(CancellationToken.None));
        await database.Store.DeleteDownloadedArtifactAsync(artifact.Id, CancellationToken.None); Assert.HasCount(0, await database.Store.GetDownloadedArtifactsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ApprovedJobRoundTripsButTamperedDatabaseEvidenceFailsClosed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pcsteward-job-{Guid.NewGuid():N}.db");
        try
        {
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            UpdateJobSelection selection = new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "1.1.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");
            UpdateJobReview review = new(Guid.NewGuid(), Guid.NewGuid(), selection);
            ImmutableUpdateJob job = await new UpdateJobApprovalService(store).ConfirmAsync(review, DateTimeOffset.UtcNow, CancellationToken.None);

            ImmutableUpdateJob restored = (await store.GetApprovedJobAsync(job.Id, CancellationToken.None))!;
            Assert.AreEqual(job.Digest, restored.Digest);
            Assert.AreEqual(job.Selection.CandidateVersion, restored.Selection.CandidateVersion);
            await Assert.ThrowsExactlyAsync<SqliteException>(() => store.SaveApprovedJobAsync(job, CancellationToken.None));

            await using (SqliteConnection connection = new($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using SqliteCommand command = connection.CreateCommand();
                command.CommandText = "UPDATE approved_update_jobs SET candidate_version = '9.9.9' WHERE id = $id;";
                command.Parameters.AddWithValue("$id", job.Id.ToString("D"));
                await command.ExecuteNonQueryAsync();
            }

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.GetApprovedJobAsync(job.Id, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task UpdateHistoryIsAppendOnlyRecoverableRetainedAndIncludedInTheProductTimeline()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "1.1.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(DateTimeOffset.UtcNow);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UpdateHistoryEntry running = new(Guid.NewGuid(), job.Id, job.CorrelationId, job.Selection.StableProductKey, job.Selection.Provider, job.Selection.PackageId, job.Selection.InstalledVersion, job.Selection.CandidateVersion, now, UpdateHistoryStatus.Running, null, "running");
        await database.Store.RecordAsync(running, CancellationToken.None);
        await database.Store.RecoverInterruptedAsync(CancellationToken.None);
        await database.Store.RecordAsync(running with { EventId = Guid.NewGuid(), OccurredAt = now.AddMinutes(1), Status = UpdateHistoryStatus.Success, ExitCode = 0, Diagnostic = "verified" }, CancellationToken.None);

        IReadOnlyCollection<UpdateHistoryEntry> history = await database.Store.GetHistoryForProductAsync(job.Selection.StableProductKey, CancellationToken.None);
        IReadOnlyCollection<ProductTimelineEntry> timeline = await database.Store.GetTimelineAsync(job.Selection.StableProductKey, CancellationToken.None);
        await database.Store.PruneAsync(new UpdateHistoryRetention(2, now.AddDays(1)), CancellationToken.None);

        Assert.IsTrue(history.Any(entry => entry.Status == UpdateHistoryStatus.VerificationRequired && entry.Diagnostic.Contains("verification-required", StringComparison.Ordinal)));
        Assert.IsTrue(history.Any(entry => entry.Status == UpdateHistoryStatus.Success && entry.ExitCode == 0));
        Assert.IsTrue(timeline.Any(entry => entry.Summary.Contains("Update Success", StringComparison.Ordinal)));
        Assert.AreEqual(0, timeline.Single(entry => entry.Summary.Contains("Update Success", StringComparison.Ordinal)).ExitCode, "the timeline carries the recorded exit code");
        Assert.IsTrue((await database.Store.GetHistoryForProductAsync(job.Selection.StableProductKey, CancellationToken.None)).Count <= 2);
    }

    [TestMethod]
    [Description("FR-HIS-01 (Task 73 Phase 3): the Activity reader returns recorded update events and scan runs newest first, bounded, read-only, and skips rows that do not parse.")]
    public async Task ActivityHistoryReaderReturnsBoundedNewestFirstRecordsWithoutWriting()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        DateTimeOffset now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        Guid job = Guid.NewGuid();
        UpdateHistoryEntry started = new(Guid.NewGuid(), job, Guid.NewGuid(), "winget:contoso.widget", UpdateProvider.WinGet, "Contoso.Widget", "1.0.0", "1.1.0", now, UpdateHistoryStatus.Running, null, "running");
        await database.Store.RecordAsync(started, CancellationToken.None);
        await database.Store.RecordAsync(started with { EventId = Guid.NewGuid(), OccurredAt = now.AddMinutes(2), Status = UpdateHistoryStatus.InstallerFailed, ExitCode = 1603, Diagnostic = "installer exit 1603" }, CancellationToken.None);
        await database.Store.RecordScanAsync(new ScanRun(Guid.NewGuid(), now.AddHours(-2), now.AddHours(-2).AddSeconds(40), "Completed", Guid.NewGuid()), [], CancellationToken.None);
        await database.Store.RecordScanAsync(new ScanRun(Guid.NewGuid(), now.AddHours(-1), now.AddHours(-1).AddSeconds(5), "Cancelled", Guid.NewGuid()), [], CancellationToken.None);

        IReadOnlyList<UpdateHistoryEntry> history = await database.Store.GetRecentUpdateHistoryAsync(10, CancellationToken.None);
        IReadOnlyList<UpdateHistoryEntry> bounded = await database.Store.GetRecentUpdateHistoryAsync(1, CancellationToken.None);
        IReadOnlyList<ScanRun> scans = await database.Store.GetRecentScanRunsAsync(10, CancellationToken.None);

        Assert.HasCount(2, history);
        Assert.AreEqual(UpdateHistoryStatus.InstallerFailed, history[0].Status);
        Assert.AreEqual(1603, history[0].ExitCode);
        Assert.HasCount(1, bounded);
        Assert.AreEqual(UpdateHistoryStatus.InstallerFailed, bounded[0].Status);
        Assert.HasCount(2, scans);
        Assert.AreEqual("Cancelled", scans[0].Status);
        Assert.AreEqual("Completed", scans[1].Status);
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => database.Store.GetRecentUpdateHistoryAsync(0, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => database.Store.GetRecentScanRunsAsync(SqliteDatabase.MaximumActivityRecords + 1, CancellationToken.None));
        // Reading changes nothing.
        Assert.HasCount(2, await database.Store.GetHistoryForProductAsync("winget:contoso.widget", CancellationToken.None));
    }

    private static UpdateHistoryEntry HistoryEvent(UpdateHistoryStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "widget", UpdateProvider.WinGet, "Widget", "1", "2", DateTimeOffset.UtcNow, status, null, "test");

    [TestMethod]
    public async Task PruneKeepsExactlyTheTwoNewestTerminalEvents()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        UpdateHistoryEntry first = HistoryEvent(UpdateHistoryStatus.Success);
        UpdateHistoryEntry second = first with { EventId = Guid.NewGuid(), OccurredAt = first.OccurredAt.AddMinutes(1) };
        UpdateHistoryEntry third = second with { EventId = Guid.NewGuid(), OccurredAt = second.OccurredAt.AddMinutes(1) };
        foreach (UpdateHistoryEntry entry in new[] { first, second, third }) await database.Store.RecordAsync(entry, CancellationToken.None);
        await database.Store.PruneAsync(new UpdateHistoryRetention(2, third.OccurredAt.AddDays(1)), CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { second.EventId, third.EventId }, (await database.Store.GetHistoryForProductAsync("widget", CancellationToken.None)).Select(e => e.EventId).ToArray());
    }

    [TestMethod]
    public async Task InterruptedRecoveryPreservesEventsAndTimesOnSecondRun()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await database.Store.RecordAsync(HistoryEvent(UpdateHistoryStatus.Running), CancellationToken.None);
        await database.Store.RecoverInterruptedAsync(CancellationToken.None);
        UpdateHistoryEntry[] first = (await database.Store.GetHistoryForProductAsync("widget", CancellationToken.None)).ToArray();
        Assert.HasCount(2, first);
        Assert.HasCount(1, first.Where(e => e.Status == UpdateHistoryStatus.VerificationRequired).ToArray());
        await database.Store.RecoverInterruptedAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(first, (await database.Store.GetHistoryForProductAsync("widget", CancellationToken.None)).ToArray());
        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT occurred_at FROM update_job_execution_state WHERE status = 'VerificationRequired'";
        Assert.AreEqual(first.Single(e => e.Status == UpdateHistoryStatus.VerificationRequired).OccurredAt.ToString("O", CultureInfo.InvariantCulture), command.ExecuteScalar());
    }

    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task ConnectorSemanticsSurvivesCacheRead(bool semantics, bool legacy)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SourcePackageRecord package = new("winget", "Widget", "Widget", null, "1", "2", "User", "x64", null, semantics);
        await database.Store.SaveSourceSnapshotAsync(new("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [package], []), CancellationToken.None);
        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        if (legacy)
        {
            command.CommandText = "UPDATE source_package_cache SET connector_semantics=$flag";
            command.Parameters.AddWithValue("$flag", semantics.ToString());
            command.ExecuteNonQuery();
        }
        else
        {
            command.CommandText = "SELECT connector_semantics FROM source_package_cache";
            Assert.AreEqual<object>(semantics ? 1L : 0L, command.ExecuteScalar());
        }
        Assert.AreEqual(semantics, (await database.Store.GetSourceSnapshotAsync("winget", CancellationToken.None))!.Packages.Single().RequiresConnectorSpecificVersionSemantics);
    }

    [TestMethod]
    public async Task NegativeExitCodeSurvivesRegionalMinusSign()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        CultureInfo original = CultureInfo.CurrentCulture;
        CultureInfo culture = (CultureInfo)original.Clone();
        culture.NumberFormat.NegativeSign = "\u2212";
        try
        {
            CultureInfo.CurrentCulture = culture;
            UpdateHistoryEntry entry = HistoryEvent(UpdateHistoryStatus.InstallerFailed) with { ExitCode = -1978335212 };
            await database.Store.RecordAsync(entry, CancellationToken.None);
            UpdateHistoryEntry product = (await database.Store.GetHistoryForProductAsync("widget", CancellationToken.None)).Single();
            UpdateHistoryEntry activity = (await database.Store.GetRecentUpdateHistoryAsync(1, CancellationToken.None)).Single();
            CollectionAssert.AreEqual(new[] { entry.ExitCode, entry.ExitCode }, new[] { product.ExitCode, activity.ExitCode });
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly string path;
        private TestDatabase(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
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
