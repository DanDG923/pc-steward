using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 48: migration 10, FR-BAT-04 batch persistence, FR-INS-03 agreement acceptance, AC-V3-05.</summary>
[TestClass]
public sealed class OperationBatchPersistenceTests
{
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private static readonly DateTimeOffset ApprovedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task VersionNineJobsMigrateAsUpgradesWithTheirOriginalDigest()
    {
        string path = TempPath();
        try
        {
            ImmutableUpdateJob legacy = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Upgrade("winget:a", "Vendor.A", UpdateScope.Machine)).Confirm(ApprovedAt);
            await using (SqliteConnection connection = Open(path))
            {
                await ExecuteAsync(connection, "CREATE TABLE schema_migrations (version INTEGER PRIMARY KEY); INSERT INTO schema_migrations(version) VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9); CREATE TABLE approved_update_jobs (id TEXT PRIMARY KEY, correlation_id TEXT NOT NULL, stable_product_key TEXT NOT NULL, provider TEXT NOT NULL, source_id TEXT NOT NULL, package_id TEXT NOT NULL, installed_version TEXT NOT NULL, candidate_version TEXT NOT NULL, scope TEXT NOT NULL, architecture TEXT NOT NULL, channel TEXT NOT NULL, mapping_evidence_version TEXT NOT NULL, approved_at TEXT NOT NULL, digest TEXT NOT NULL);");
                UpdateJobSelection s = legacy.Selection;
                await ExecuteAsync(connection, "INSERT INTO approved_update_jobs VALUES ($id,$correlation,$stable,'WinGet','WinGet',$package,'1.0.0','1.1.0','Machine','X64','Stable','mapping-v1',$approved,$digest);",
                    ("$id", legacy.Id.ToString("D")), ("$correlation", legacy.CorrelationId.ToString("D")), ("$stable", s.StableProductKey), ("$package", s.PackageId),
                    ("$approved", ApprovedAt.ToString("O", CultureInfo.InvariantCulture)), ("$digest", legacy.Digest));
            }

            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            ImmutableUpdateJob restored = (await store.GetApprovedJobAsync(legacy.Id, CancellationToken.None))!;

            Assert.AreEqual(legacy.Digest, restored.Digest);
            Assert.AreEqual(PackageOperationKind.Upgrade, restored.Selection.Operation);
            Assert.IsTrue(restored.Selection.RunsElevated, "Pre-v10 machine-scope jobs keep their scope-derived route.");
            Assert.IsTrue(restored.Selection.IsPlainUpgrade);
            await store.MigrateAsync(CancellationToken.None);
            Assert.AreEqual(legacy.Digest, (await store.GetApprovedJobAsync(legacy.Id, CancellationToken.None))!.Digest, "Migration is idempotent.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task InstallJobRoundTripsWithoutAnInstalledVersion()
    {
        string path = TempPath();
        try
        {
            SqliteDatabase store = await StoreAsync(path);
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Install("pcs:notepadpp", "Notepad++.Notepad++", Hash, installerElevates: true)).Confirm(ApprovedAt);
            await store.SaveApprovedJobAsync(job, CancellationToken.None);

            ImmutableUpdateJob restored = (await store.GetApprovedJobAsync(job.Id, CancellationToken.None))!;
            Assert.AreEqual(job.Digest, restored.Digest);
            Assert.IsNull(restored.Selection.InstalledVersion);
            Assert.AreEqual(Hash, restored.Selection.AgreementHash);
            Assert.IsTrue(restored.Selection.RunsElevated);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("UPDATE approved_update_jobs SET agreement_hash = NULL WHERE operation = 'Install';")]
    [DataRow("UPDATE approved_update_jobs SET runs_elevated = 1 WHERE operation = 'Install';")]
    [DataRow("UPDATE approved_update_jobs SET runs_elevated = 7 WHERE operation = 'Install';")]
    [DataRow("UPDATE approved_update_jobs SET operation = 'Upgrade', installed_version = '1.0' WHERE operation = 'Install';")]
    [DataRow("UPDATE approved_update_jobs SET operation = '1' WHERE operation = 'Install';")]
    [DataRow("UPDATE approved_update_jobs SET package_id = 'Vendor.Other' WHERE operation = 'Install';")]
    [DataRow("UPDATE operation_batch_items SET sequence = sequence + 10; UPDATE operation_batch_items SET sequence = 1 WHERE sequence = 10; UPDATE operation_batch_items SET sequence = 0 WHERE sequence = 11; UPDATE operation_batch_items SET sequence = 2 WHERE sequence = 12;")]
    [DataRow("DELETE FROM operation_batch_items WHERE sequence = 2;")]
    [DataRow("DELETE FROM operation_batch_items WHERE sequence = 2; UPDATE operation_batches SET item_count = 2;")]
    [DataRow("UPDATE operation_batches SET approved_at = '2026-10-09T12:00:01.0000000+00:00';")]
    [DataRow("UPDATE operation_batches SET digest = '00' || substr(digest, 3);")]
    [DataRow("UPDATE operation_batch_items SET job_id = 'not-a-guid' WHERE sequence = 0;")]
    public async Task TamperedBatchRowsFailClosed(string tamper)
    {
        string path = TempPath();
        try
        {
            SqliteDatabase store = await StoreAsync(path);
            ApprovedOperationBatch batch = new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(),
                [Upgrade("winget:a", "Vendor.A", UpdateScope.User), Install("pcs:b", "Vendor.B", Hash, installerElevates: false), Upgrade("winget:c", "Vendor.C", UpdateScope.Machine)]).Confirm(ApprovedAt);
            await store.SaveApprovedBatchAsync(batch, CancellationToken.None);
            ApprovedOperationBatch restored = (await store.GetApprovedBatchAsync(batch.Id, CancellationToken.None))!;
            Assert.AreEqual(batch.Digest, restored.Digest);
            CollectionAssert.AreEqual(batch.Items.Select(item => item.Id).ToArray(), restored.Items.Select(item => item.Id).ToArray());

            await using (SqliteConnection connection = Open(path)) await ExecuteAsync(connection, tamper);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.GetApprovedBatchAsync(batch.Id, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task UnknownBatchIsNullAndABatchCannotBeSavedTwice()
    {
        string path = TempPath();
        try
        {
            SqliteDatabase store = await StoreAsync(path);
            Assert.IsNull(await store.GetApprovedBatchAsync(Guid.NewGuid(), CancellationToken.None));
            ApprovedOperationBatch batch = new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [Upgrade("winget:a", "Vendor.A", UpdateScope.User)]).Confirm(ApprovedAt);
            await store.SaveApprovedBatchAsync(batch, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<SqliteException>(() => store.SaveApprovedBatchAsync(batch, CancellationToken.None));
            Assert.AreEqual(batch.Digest, (await store.GetApprovedBatchAsync(batch.Id, CancellationToken.None))!.Digest, "A failed second save leaves the first intact.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task AgreementAcceptanceGatesBatchApprovalAndIsClearedWithPolicies()
    {
        string path = TempPath();
        try
        {
            SqliteDatabase store = await StoreAsync(path);
            OperationBatchApprovalService approvals = new(store, store);
            OperationBatchReview review = new(Guid.NewGuid(), Guid.NewGuid(), [Install("pcs:notepadpp", "Notepad++.Notepad++", Hash)]);

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => approvals.ConfirmAsync(review, ApprovedAt, CancellationToken.None));
            Assert.IsNull(await store.GetApprovedBatchAsync(review.BatchId, CancellationToken.None), "Nothing is stored without acceptance.");

            await approvals.RecordAcceptanceAsync(UpdateSource.WinGet, "Notepad++.Notepad++", "2.0.0", "F" + Hash[1..], ApprovedAt, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => approvals.ConfirmAsync(review, ApprovedAt, CancellationToken.None), "A different agreement set is not acceptance.");
            await approvals.RecordAcceptanceAsync(UpdateSource.WinGet, "Notepad++.Notepad++", "1.9.0", Hash, ApprovedAt, CancellationToken.None);
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => approvals.ConfirmAsync(review, ApprovedAt, CancellationToken.None), "Acceptance is per version.");

            await approvals.RecordAcceptanceAsync(UpdateSource.WinGet, "notepad++.notepad++", "2.0.0", Hash, ApprovedAt, CancellationToken.None);
            ApprovedOperationBatch batch = await approvals.ConfirmAsync(review, ApprovedAt, CancellationToken.None);
            Assert.AreEqual(batch.Digest, (await store.GetApprovedBatchAsync(batch.Id, CancellationToken.None))!.Digest);

            AdvancedDataClearResult cleared = await store.ClearMappingsAndPoliciesAsync(CancellationToken.None);
            Assert.IsTrue(cleared.Succeeded);
            Assert.IsFalse(await store.HasAgreementAcceptanceAsync(UpdateSource.WinGet, "Notepad++.Notepad++", "2.0.0", Hash, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static UpdateJobSelection Upgrade(string key, string packageId, UpdateScope scope) =>
        new(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "1.0.0", "1.1.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");

    private static UpdateJobSelection Install(string key, string packageId, string? agreementHash, bool installerElevates = false) =>
        UpdateJobSelection.ForInstall(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "2.0.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", agreementHash, installerElevates);

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"pcsteward-batch-{Guid.NewGuid():N}.db");

    private static async Task<SqliteDatabase> StoreAsync(string path)
    {
        SqliteDatabase store = new(path);
        await store.MigrateAsync(CancellationToken.None);
        return store;
    }

    private static SqliteConnection Open(string path)
    {
        SqliteConnection connection = new($"Data Source={path};Pooling=False");
        connection.Open();
        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, string value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}
