using PCSteward.Application;
using PCSteward.Domain;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace PCSteward.Persistence.Sqlite;

public sealed partial class SqliteDatabase : IReviewedUpdateOperationStore
{
    private const int MaximumSchemaVersion = 14;

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
            """, cancellationToken, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES(9);", cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task ValidateReviewedOperationsSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT job_id,schema_version,canonical_payload,canonical_digest,draft_generation FROM reviewed_update_operations LIMIT 0;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CreateReviewedDraftGenerationSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            ALTER TABLE reviewed_update_operations ADD COLUMN draft_generation TEXT
                CHECK(draft_generation IS NULL OR (typeof(draft_generation)='text' AND length(draft_generation)=36));
            """, cancellationToken, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES(10);", cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task CreateReviewedArchitectureSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE reviewed_update_operations_v3 (
                job_id TEXT NOT NULL PRIMARY KEY CHECK(length(job_id)=36),
                schema_version INTEGER NOT NULL CHECK(schema_version IN (2,3)),
                canonical_payload BLOB NOT NULL CHECK(typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384),
                canonical_digest TEXT NOT NULL CHECK(typeof(canonical_digest)='text' AND length(canonical_digest)=64 AND canonical_digest NOT GLOB '*[^0-9A-F]*'),
                draft_generation TEXT CHECK(draft_generation IS NULL OR (typeof(draft_generation)='text' AND length(draft_generation)=36))
            );
            """, cancellationToken, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, """
            INSERT INTO reviewed_update_operations_v3 SELECT job_id,schema_version,canonical_payload,canonical_digest,draft_generation FROM reviewed_update_operations;
            """, cancellationToken, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "DROP TABLE reviewed_update_operations;", cancellationToken, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "ALTER TABLE reviewed_update_operations_v3 RENAME TO reviewed_update_operations;", cancellationToken, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES(12);", cancellationToken, transaction).ConfigureAwait(false);
    }

    public async Task<ReviewedUpdateDraft> SaveReviewedOperationAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        byte[] payload = operation.GetCanonicalBytes();
        if (!operation.HasCurrentSchema || !ReviewedUpdateOperation.TryRestore(payload, operation.Digest, out ReviewedUpdateOperation? verified) || !operation.HasSameIdentity(verified))
            throw new ArgumentException("Reviewed operation is outside the canonical storage contract.", nameof(operation));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await RefuseFutureSchemaAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        Guid createdGeneration = Guid.NewGuid();
        command.CommandText = "INSERT INTO reviewed_update_operations(job_id,schema_version,canonical_payload,canonical_digest,draft_generation) VALUES($job,$schema,$payload,$digest,$generation) ON CONFLICT(job_id) DO NOTHING;";
        command.Parameters.AddWithValue("$job", operation.JobId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$schema", ReviewedUpdateOperation.SchemaVersion);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = payload;
        command.Parameters.AddWithValue("$digest", operation.Digest);
        command.Parameters.AddWithValue("$generation", createdGeneration.ToString("D", CultureInfo.InvariantCulture));
        int inserted = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        StoredReviewedOperation? existing = await ReadReviewedOperationAsync(connection, transaction, operation.JobId, cancellationToken).ConfigureAwait(false);
        if (existing?.Generation is not { } generation || !operation.HasSameIdentity(existing.Operation) ||
            (inserted == 1 && generation != createdGeneration))
            throw new InvalidOperationException("The stored job identity differs from the reviewed operation.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new(existing.Operation, generation);
    }

    public async Task<ReviewedUpdateDraft?> GetReviewedOperationDraftAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job identity is required.", nameof(jobId));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        StoredReviewedOperation? row = await ReadReviewedOperationAsync(connection, null, jobId, cancellationToken).ConfigureAwait(false);
        return row is { Operation.HasCurrentSchema: true, Generation: { } generation } ? new(row.Operation, generation) : null;
    }

    public async Task<ReviewedUpdateOperation?> GetReviewedOperationAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job identity is required.", nameof(jobId));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadReviewedOperationAsync(connection, null, jobId, cancellationToken).ConfigureAwait(false))?.Operation;
    }

    private static async Task<StoredReviewedOperation?> ReadReviewedOperationAsync(SqliteConnection connection, SqliteTransaction? transaction,
        Guid jobId, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // Bound the projected blob before the provider materializes its bytes. SQLite itself remains untrusted storage.
        command.CommandText = """
            SELECT CASE WHEN typeof(schema_version)='integer' AND schema_version IN (2,3) THEN schema_version END,
                CASE WHEN typeof(canonical_digest)='text' AND length(canonical_digest)=64 THEN canonical_digest END,
                CASE WHEN typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384 THEN canonical_payload END,
                CASE WHEN typeof(draft_generation)='text' AND length(draft_generation)=36 THEN draft_generation END,
                draft_generation IS NULL
            FROM reviewed_update_operations WHERE job_id=$job LIMIT 2;
            """;
        command.Parameters.AddWithValue("$job", jobId.ToString("D", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        if (reader.GetValue(0) is not long schema || schema is not (2 or 3) ||
            reader.GetValue(1) is not string digest || reader.GetValue(2) is not byte[] bytes ||
            !ReviewedUpdateOperation.TryRestore(bytes, digest, out ReviewedUpdateOperation? operation) || operation is null ||
            operation.EncodingVersion != schema || operation.JobId != jobId)
            throw new InvalidOperationException("The persisted reviewed operation is corrupt, noncanonical or belongs to another job.");
        Guid? generation = null;
        if (reader.GetValue(3) is string generationText && Guid.TryParseExact(generationText, "D", out Guid parsed) &&
            parsed != Guid.Empty && parsed.ToString("D", CultureInfo.InvariantCulture) == generationText)
            generation = parsed;
        else if (reader.GetValue(4) is not long isNull || isNull != 1)
            throw new InvalidOperationException("The persisted draft generation is invalid.");
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("More than one reviewed operation exists for the job identity.");
        return new(operation, generation);
    }

    private sealed record StoredReviewedOperation(ReviewedUpdateOperation Operation, Guid? Generation);
}
