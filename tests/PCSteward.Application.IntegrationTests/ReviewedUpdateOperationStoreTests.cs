using Microsoft.Data.Sqlite;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ReviewedUpdateOperationStoreTests
{
    [TestMethod]
    public async Task CanonicalDataRoundTripsWithoutCreatingALegacyApproval()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        ReviewedUpdateOperation? restored = await database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None);
        Assert.IsNotNull(restored);
        Assert.IsTrue(operation.HasSameIdentity(restored));
        Assert.IsNull(await database.Store.GetApprovedJobAsync(operation.JobId, CancellationToken.None));
        Assert.IsFalse(await database.Store.IsFreshResetStoreAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RecreatedEqualDataHasANewGenerationAndCannotMatchTheOldDraft()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        ReviewedUpdateDraft first = await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        ReviewedUpdateDraft same = await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        Assert.AreNotEqual(Guid.Empty, first.Generation);
        Assert.IsTrue(first.HasSameDraftIdentity(same));
        ReviewedUpdateDraft? read = await database.Store.GetReviewedOperationDraftAsync(operation.JobId, CancellationToken.None);
        Assert.IsTrue(first.HasSameDraftIdentity(read));
        await database.ExecuteAsync("DELETE FROM reviewed_update_operations WHERE job_id=$job;", ("$job", operation.JobId.ToString("D")));
        Assert.IsNull(await database.Store.GetReviewedOperationDraftAsync(operation.JobId, CancellationToken.None));
        ReviewedUpdateDraft replacement = await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        Assert.IsTrue(first.Operation.HasSameIdentity(replacement.Operation));
        Assert.AreNotEqual(first.Generation, replacement.Generation);
        Assert.IsFalse(first.HasSameDraftIdentity(replacement));
    }

    [TestMethod]
    public async Task SchemaNineDataMigratesWithoutInventingADraftGeneration()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        await database.RemoveTenAsync();
        await database.Store.MigrateAsync(CancellationToken.None);
        Assert.IsTrue(operation.HasSameIdentity(await database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None)));
        Assert.IsNull(await database.Store.GetReviewedOperationDraftAsync(operation.JobId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None));
        Assert.AreEqual(operation.Digest, await database.ScalarAsync("SELECT canonical_digest FROM reviewed_update_operations;"));
    }

    [TestMethod]
    public async Task GenerationMigrationFailurePreservesSchemaNineDataAndRollsBackTheColumn()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        await database.RemoveTenAsync();
        await database.ExecuteAsync("CREATE TRIGGER reject_ten BEFORE INSERT ON schema_migrations WHEN NEW.version=10 BEGIN SELECT RAISE(ABORT,'fixture generation migration fault'); END;");
        Assert.AreEqual(9L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.ExecuteAsync("INSERT INTO schema_migrations(version) VALUES(10);"));
        Exception? failure = null;
        try { await database.Store.MigrateAsync(CancellationToken.None); }
        catch (Exception exception) { failure = exception; }
        Assert.IsInstanceOfType<SqliteException>(failure, $"marker={await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;")}; column={await database.ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('reviewed_update_operations') WHERE name='draft_generation';")}; trigger={await database.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name='reject_ten';")}");
        Assert.AreEqual(9L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM pragma_table_info('reviewed_update_operations') WHERE name='draft_generation';"));
        Assert.AreEqual(operation.Digest, await database.ScalarAsync("SELECT canonical_digest FROM reviewed_update_operations;"));
    }

    [TestMethod]
    public async Task MalformedGenerationIsCorruptionRatherThanHistoricalAbsence()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        await database.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE reviewed_update_operations SET draft_generation='invalid';");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.GetReviewedOperationDraftAsync(operation.JobId, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task TriggerCannotChooseTheGenerationOfANewlyCreatedDraft()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.ExecuteAsync("CREATE TRIGGER alter_generation AFTER INSERT ON reviewed_update_operations BEGIN UPDATE reviewed_update_operations SET draft_generation='11111111-1111-1111-1111-111111111111' WHERE job_id=NEW.job_id; END;");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None));
        Assert.IsNull(await database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task SameIdentityIsIdempotentButConflictingIdentityNeverOverwrites()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.SaveReviewedOperationAsync(
            Operation(operation.JobId, InstallerMode.Interactive), CancellationToken.None));
        Assert.IsTrue(operation.HasSameIdentity(await database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None)));
    }

    [TestMethod]
    public async Task MigrationFromEightPreservesLegacyHistoryWithoutInferringMode()
    {
        await using Fixture database = await Fixture.CreateAsync();
        await database.RemoveNineAsync();
        ImmutableUpdateJob legacy = Job();
        await database.Store.SaveApprovedJobAsync(legacy, CancellationToken.None);
        UpdateHistoryEntry history = new(Guid.NewGuid(), legacy.Id, legacy.CorrelationId, legacy.Selection.StableProductKey,
            legacy.Selection.Provider, legacy.Selection.PackageId, "1.0", "1.1", legacy.ApprovedAt,
            UpdateHistoryStatus.Success, 0, "fixture historical success");
        await database.Store.RecordAsync(history, CancellationToken.None);
        await database.Store.MigrateAsync(CancellationToken.None);
        Assert.AreEqual(legacy.Digest, (await database.Store.GetApprovedJobAsync(legacy.Id, CancellationToken.None))!.Digest);
        Assert.HasCount(1, await database.Store.GetHistoryForProductAsync(legacy.Selection.StableProductKey, CancellationToken.None));
        Assert.IsNull(await database.Store.GetReviewedOperationAsync(legacy.Id, CancellationToken.None));
        Assert.AreEqual(11L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
    }

    [TestMethod]
    public async Task CorruptOrSubstitutedCanonicalDataIsRefused()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation original = Operation();
        await database.Store.SaveReviewedOperationAsync(original, CancellationToken.None);
        await database.ExecuteAsync("UPDATE reviewed_update_operations SET canonical_digest = $digest;", ("$digest", new string('0', 64)));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.GetReviewedOperationAsync(original.JobId, CancellationToken.None));
        ReviewedUpdateOperation other = Operation(Guid.NewGuid());
        await database.ExecuteAsync("UPDATE reviewed_update_operations SET canonical_payload=$payload,canonical_digest=$digest;",
            ("$payload", other.GetCanonicalBytes()), ("$digest", other.Digest));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.GetReviewedOperationAsync(original.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task SqlBoundsAndRuntimeChecksRefuseOversizeOrUnknownSchema()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None);
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.ExecuteAsync(
            "UPDATE reviewed_update_operations SET canonical_payload=$payload;", ("$payload", new byte[16 * 1024 + 1])));
        await database.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE reviewed_update_operations SET canonical_payload=zeroblob(16385);");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None));
        await database.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE reviewed_update_operations SET schema_version=3,canonical_payload=$payload;",
            ("$payload", operation.GetCanonicalBytes()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None));
    }

    [TestMethod]
    public async Task MigrationCommitFaultRollsBackTheNewTableAndPreservesLegacyData()
    {
        await using Fixture database = await Fixture.CreateAsync();
        await database.RemoveNineAsync();
        ImmutableUpdateJob legacy = Job();
        await database.Store.SaveApprovedJobAsync(legacy, CancellationToken.None);
        await database.ExecuteAsync("CREATE TRIGGER reject_nine BEFORE INSERT ON schema_migrations WHEN NEW.version=9 BEGIN SELECT RAISE(ABORT,'fixture migration fault'); END;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.MigrateAsync(CancellationToken.None));
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM sqlite_master WHERE name='reviewed_update_operations';"));
        Assert.AreEqual(8L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(legacy.Digest, (await database.Store.GetApprovedJobAsync(legacy.Id, CancellationToken.None))!.Digest);
    }

    [TestMethod]
    public async Task PartialMigrationCannotBeSilentlyAdopted()
    {
        await using Fixture database = await Fixture.CreateAsync();
        await database.ExecuteAsync("DELETE FROM schema_migrations WHERE version=9;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.MigrateAsync(CancellationToken.None));
        Assert.AreEqual(0L, await database.ScalarAsync("SELECT COUNT(*) FROM schema_migrations WHERE version=9;"));
    }

    [TestMethod]
    public async Task FutureSchemaIsRefusedBeforeMigrationMutation()
    {
        await using Fixture database = await Fixture.CreateAsync();
        await database.ExecuteAsync("INSERT INTO schema_migrations(version) VALUES(12);");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.MigrateAsync(CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.SaveReviewedOperationAsync(Operation(), CancellationToken.None));
        Assert.AreEqual(12L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        Assert.IsNull(await database.Store.GetReviewedOperationAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingTableWithCompletedMarkerIsNotReportedAsMigrated()
    {
        await using Fixture database = await Fixture.CreateAsync();
        await database.ExecuteAsync("DROP TABLE reviewed_update_operations;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.MigrateAsync(CancellationToken.None));
        Assert.AreEqual(11L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
    }

    [TestMethod]
    public async Task TriggerModifiedInsertCannotBeReportedAsTheSavedReview()
    {
        await using Fixture database = await Fixture.CreateAsync();
        ReviewedUpdateOperation operation = Operation();
        await database.ExecuteAsync("CREATE TRIGGER alter_review AFTER INSERT ON reviewed_update_operations BEGIN UPDATE reviewed_update_operations SET canonical_digest='0000000000000000000000000000000000000000000000000000000000000000' WHERE job_id=NEW.job_id; END;");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.SaveReviewedOperationAsync(operation, CancellationToken.None));
        Assert.IsNull(await database.Store.GetReviewedOperationAsync(operation.JobId, CancellationToken.None));
    }

    private static ImmutableUpdateJob Job(Guid? id = null) => new UpdateJobReview(id ?? Guid.NewGuid(),
        Guid.Parse("22222222-2222-2222-2222-222222222222"), new UpdateJobSelection("winget:contoso.widget", UpdateProvider.WinGet,
            UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64,
            UpdateChannel.Stable, "mapping-v1")).Confirm(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));

    private static ReviewedUpdateOperation Operation(Guid? id = null, InstallerMode mode = InstallerMode.Silent)
    {
        ImmutableUpdateJob job = Job(id);
        return new(job, "S-1-5-21-11-22-33-1001", "native:123", "context:123", "evidence:123", job.ApprovedAt.AddSeconds(-1),
            UpdateRouteContract.WinGetUserExactV2, ReviewedInstallerMode.Selected(mode));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"pcsteward-task50-{Guid.NewGuid():N}.db");
        private Fixture() => Store = new(path);
        internal SqliteDatabase Store { get; }
        internal static async Task<Fixture> CreateAsync()
        {
            Fixture fixture = new();
            await fixture.Store.MigrateAsync(CancellationToken.None);
            return fixture;
        }
        internal Task RemoveNineAsync() => ExecuteAsync("DROP TABLE update_dispatch_claims; DROP TABLE reviewed_update_operations; DELETE FROM schema_migrations WHERE version IN (9,10,11);");
        internal Task RemoveTenAsync() => ExecuteAsync("DROP TABLE update_dispatch_claims; ALTER TABLE reviewed_update_operations DROP COLUMN draft_generation; DELETE FROM schema_migrations WHERE version IN (10,11);");
        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = Connection();
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }
        internal async Task<object?> ScalarAsync(string sql)
        {
            await using SqliteConnection connection = Connection();
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }
        private SqliteConnection Connection() => new(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        public ValueTask DisposeAsync()
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" }) File.Delete(file);
            return ValueTask.CompletedTask;
        }
    }
}
