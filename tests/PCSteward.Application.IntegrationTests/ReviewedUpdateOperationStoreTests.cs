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
        Assert.AreEqual(9L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
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
        await database.ExecuteAsync("INSERT INTO schema_migrations(version) VALUES(10);");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.MigrateAsync(CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => database.Store.SaveReviewedOperationAsync(Operation(), CancellationToken.None));
        Assert.AreEqual(10L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        Assert.IsNull(await database.Store.GetReviewedOperationAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [TestMethod]
    public async Task MissingTableWithCompletedMarkerIsNotReportedAsMigrated()
    {
        await using Fixture database = await Fixture.CreateAsync();
        await database.ExecuteAsync("DROP TABLE reviewed_update_operations;");
        await Assert.ThrowsExactlyAsync<SqliteException>(() => database.Store.MigrateAsync(CancellationToken.None));
        Assert.AreEqual(9L, await database.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
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
        internal Task RemoveNineAsync() => ExecuteAsync("DROP TABLE reviewed_update_operations; DELETE FROM schema_migrations WHERE version=9;");
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
