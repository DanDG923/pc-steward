using PCSteward.Application;
using PCSteward.Domain;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PCSteward.Persistence.Sqlite;

public sealed partial class SqliteDatabase : IReviewedUpdateOperationStore
{
    private const int MaximumSchemaVersion = 9;

    private static async Task RefuseFutureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken, SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) == 0) return;
        command.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_migrations;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) > MaximumSchemaVersion)
            throw new InvalidOperationException("Database schema is newer than this build; migration and reviewed-operation writes are refused.");
    }

    private static async Task CreateReviewedOperationsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE reviewed_update_operations (
                job_id TEXT NOT NULL PRIMARY KEY CHECK(length(job_id)=36),
                schema_version INTEGER NOT NULL CHECK(schema_version=2),
                canonical_payload BLOB NOT NULL CHECK(typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384),
                canonical_digest TEXT NOT NULL CHECK(typeof(canonical_digest)='text' AND length(canonical_digest)=64 AND canonical_digest NOT GLOB '*[^0-9A-F]*')
            );
            INSERT INTO schema_migrations(version) VALUES(9);
            """, cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task ValidateReviewedOperationsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT job_id,schema_version,canonical_payload,canonical_digest FROM reviewed_update_operations LIMIT 0;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveReviewedOperationAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        byte[] payload = operation.GetCanonicalBytes();
        if (!ReviewedUpdateOperation.TryRestore(payload, operation.Digest, out ReviewedUpdateOperation? verified) || !operation.HasSameIdentity(verified))
            throw new ArgumentException("Reviewed operation is outside the canonical storage contract.", nameof(operation));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await RefuseFutureSchemaAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO reviewed_update_operations(job_id,schema_version,canonical_payload,canonical_digest) VALUES($job,$schema,$payload,$digest) ON CONFLICT(job_id) DO NOTHING;";
        command.Parameters.AddWithValue("$job", operation.JobId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$schema", ReviewedUpdateOperation.SchemaVersion);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
        command.Parameters.AddWithValue("$digest", operation.Digest);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        ReviewedUpdateOperation? existing = await ReadReviewedOperationAsync(connection, transaction, operation.JobId, cancellationToken).ConfigureAwait(false);
        if (!operation.HasSameIdentity(existing))
            throw new InvalidOperationException("The stored job identity differs from the reviewed operation.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReviewedUpdateOperation?> GetReviewedOperationAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job identity is required.", nameof(jobId));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadReviewedOperationAsync(connection, null, jobId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReviewedUpdateOperation?> ReadReviewedOperationAsync(SqliteConnection connection, SqliteTransaction? transaction,
        Guid jobId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // Bound the projected blob before the provider materializes its bytes. SQLite itself remains untrusted storage.
        command.CommandText = """
            SELECT CASE WHEN typeof(schema_version)='integer' AND schema_version=2 THEN schema_version END,
                CASE WHEN typeof(canonical_digest)='text' AND length(canonical_digest)=64 THEN canonical_digest END,
                CASE WHEN typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384 THEN canonical_payload END
            FROM reviewed_update_operations WHERE job_id=$job LIMIT 2;
            """;
        command.Parameters.AddWithValue("$job", jobId.ToString("D", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.GetValue(0) is not long schema || schema != ReviewedUpdateOperation.SchemaVersion ||
            reader.GetValue(1) is not string digest || reader.GetValue(2) is not byte[] bytes ||
            !ReviewedUpdateOperation.TryRestore(bytes, digest, out ReviewedUpdateOperation? operation) || operation is null || operation.JobId != jobId ||
            await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The persisted reviewed operation is corrupt, noncanonical or belongs to another job.");
        return operation;
    }
}
