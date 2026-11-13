using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

/// <summary>
/// FR-DIA-01/03/04 (Task 64 stage 2, ADR-015/027): whole immutable diagnostic snapshots in three snapshot-owned tables. No table
/// references update history or scan data, so Clear update history never alters a frozen snapshot and only Clear scan data,
/// expiry, capacity pruning or factory reset remove one. Rows are untrusted on read: every record is parsed strictly and a
/// snapshot with any bad row is reported unreadable rather than replayed with fewer records.
/// </summary>
public sealed partial class SqliteDatabase : IDiagnosticSnapshotStore, IDiagnosticAppHistorySource
{
    internal static readonly string[] DiagnosticTables = ["diagnostic_records", "diagnostic_source_results", "diagnostic_snapshots"];

    private static async Task CreateDiagnosticSnapshotSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE diagnostic_snapshots (
                id TEXT NOT NULL PRIMARY KEY CHECK(length(id)=36),
                policy_version INTEGER NOT NULL CHECK(policy_version >= 1),
                collected_at TEXT NOT NULL CHECK(length(collected_at)=28),
                expires_at TEXT NOT NULL CHECK(length(expires_at)=28),
                lookback_from TEXT NOT NULL CHECK(length(lookback_from)=28),
                record_count INTEGER NOT NULL CHECK(record_count BETWEEN 0 AND 2000),
                payload_bytes INTEGER NOT NULL CHECK(payload_bytes BETWEEN 0 AND 8388608)
            );
            CREATE INDEX ix_diagnostic_snapshots_collected ON diagnostic_snapshots(collected_at, id);
            CREATE TABLE diagnostic_source_results (
                snapshot_id TEXT NOT NULL CHECK(length(snapshot_id)=36),
                source TEXT NOT NULL CHECK(length(source) BETWEEN 1 AND 64),
                outcome TEXT NOT NULL CHECK(length(outcome) BETWEEN 1 AND 64),
                reason TEXT NOT NULL CHECK(length(reason) BETWEEN 1 AND 64),
                queried_from TEXT NOT NULL CHECK(length(queried_from)=28),
                queried_to TEXT NOT NULL CHECK(length(queried_to)=28),
                allowed_records INTEGER NOT NULL CHECK(allowed_records BETWEEN 0 AND 500),
                returned_records INTEGER NOT NULL CHECK(returned_records BETWEEN 0 AND 500),
                kept_records INTEGER NOT NULL CHECK(kept_records BETWEEN 0 AND 500),
                rejected_records INTEGER NOT NULL CHECK(rejected_records BETWEEN 0 AND 500),
                PRIMARY KEY(snapshot_id, source)
            );
            CREATE TABLE diagnostic_records (
                snapshot_id TEXT NOT NULL CHECK(length(snapshot_id)=36),
                ordinal INTEGER NOT NULL CHECK(ordinal BETWEEN 0 AND 1999),
                payload TEXT NOT NULL CHECK(typeof(payload)='text' AND length(CAST(payload AS BLOB)) BETWEEN 2 AND 4096),
                PRIMARY KEY(snapshot_id, ordinal)
            );
            INSERT INTO schema_migrations(version) VALUES (13);
            """, cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task ValidateDiagnosticSnapshotSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, "SELECT id FROM diagnostic_snapshots LIMIT 0; SELECT snapshot_id FROM diagnostic_source_results LIMIT 0; SELECT payload FROM diagnostic_records LIMIT 0;", cancellationToken, transaction).ConfigureAwait(false);

    public async Task<DiagnosticSnapshotSaveResult> SaveDiagnosticSnapshotAsync(DiagnosticSnapshot snapshot, DateTimeOffset nowUtc, IReadOnlyCollection<Guid> protectedSnapshotIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(protectedSnapshotIds);
        if (!TryEncode(snapshot, out string[]? payloads, out long payloadBytes)) return new(false, DiagnosticSnapshotSaveFailure.InvalidSnapshot, 0, 0);
        if (snapshot.IsExpired(nowUtc)) return new(false, DiagnosticSnapshotSaveFailure.AlreadyExpired, 0, 0);
        try
        {
            await using SqliteConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
            await using SqliteTransaction transaction = connection.BeginTransaction();
            int expired = await DeleteExpiredAsync(connection, transaction, nowUtc, cancellationToken).ConfigureAwait(false);
            string id = snapshot.Id.ToString("D", CultureInfo.InvariantCulture);
            await ExecuteAsync(connection, "INSERT INTO diagnostic_snapshots(id,policy_version,collected_at,expires_at,lookback_from,record_count,payload_bytes) VALUES ($id,$policy,$collected,$expires,$from,$count,$bytes);",
                cancellationToken, transaction, ("$id", id), ("$policy", snapshot.PolicyVersion), ("$collected", Utc(snapshot.CollectedAtUtc)), ("$expires", Utc(snapshot.ExpiresAtUtc)),
                ("$from", Utc(snapshot.LookbackFromUtc)), ("$count", payloads!.Length), ("$bytes", payloadBytes)).ConfigureAwait(false);
            foreach (DiagnosticSourceResult source in snapshot.Sources)
            {
                await ExecuteAsync(connection, "INSERT INTO diagnostic_source_results VALUES ($id,$source,$outcome,$reason,$from,$to,$allowed,$returned,$kept,$rejected);", cancellationToken, transaction,
                    ("$id", id), ("$source", source.Source.ToString()), ("$outcome", source.Outcome.ToString()), ("$reason", source.Reason.ToString()), ("$from", Utc(source.QueriedFromUtc)),
                    ("$to", Utc(source.QueriedToUtc)), ("$allowed", source.AllowedRecords), ("$returned", source.MatchingRecordsReturned), ("$kept", source.RecordsKept), ("$rejected", source.RecordsRejected)).ConfigureAwait(false);
            }
            for (int ordinal = 0; ordinal < payloads.Length; ordinal++)
                await ExecuteAsync(connection, "INSERT INTO diagnostic_records VALUES ($id,$ordinal,$payload);", cancellationToken, transaction, ("$id", id), ("$ordinal", ordinal), ("$payload", payloads[ordinal])).ConfigureAwait(false);

            HashSet<string> keep = [id, .. protectedSnapshotIds.Select(value => value.ToString("D", CultureInfo.InvariantCulture))];
            int pruned = 0;
            while (await TotalDiagnosticBytesAsync(connection, transaction, cancellationToken).ConfigureAwait(false) > DiagnosticCollectionPolicy.MaximumRetainedBytes)
            {
                string? oldest = await OldestPrunableAsync(connection, transaction, keep, cancellationToken).ConfigureAwait(false);
                if (oldest is null)
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return new(false, DiagnosticSnapshotSaveFailure.CapacityHeldByProtectedSnapshots, 0, 0);
                }
                await DeleteSnapshotAsync(connection, transaction, oldest, cancellationToken).ConfigureAwait(false);
                pruned++;
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new(true, DiagnosticSnapshotSaveFailure.None, expired, pruned);
        }
        catch (SqliteException)
        {
            return new(false, DiagnosticSnapshotSaveFailure.StorageFailed, 0, 0);
        }
    }

    public async Task<IReadOnlyList<DiagnosticSnapshotSummary>> ListDiagnosticSnapshotsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        List<(Guid Id, DateTimeOffset Collected, DateTimeOffset Expires, int Count, long Bytes)> rows = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,collected_at,expires_at,record_count,payload_bytes FROM diagnostic_snapshots WHERE expires_at > $now ORDER BY collected_at DESC, id DESC LIMIT 1000;";
            command.Parameters.AddWithValue("$now", Utc(nowUtc));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetValue(0) is string idText && Guid.TryParseExact(idText, "D", out Guid id) &&
                    reader.GetValue(1) is string collectedText && TryParseUtc(collectedText, out DateTimeOffset collected) &&
                    reader.GetValue(2) is string expiresText && TryParseUtc(expiresText, out DateTimeOffset expires) && expires > nowUtc.ToUniversalTime() &&
                    reader.GetValue(3) is long count && reader.GetValue(4) is long bytes)
                    rows.Add((id, collected, expires, (int)count, bytes));
            }
        }
        List<DiagnosticSnapshotSummary> summaries = [];
        foreach ((Guid id, DateTimeOffset collected, DateTimeOffset expires, int count, long bytes) in rows)
        {
            IReadOnlyList<DiagnosticSourceResult>? sources = await ReadSourcesAsync(connection, id, cancellationToken).ConfigureAwait(false);
            if (sources is not null) summaries.Add(new(id, collected, expires, count, bytes, sources));
        }
        return summaries.AsReadOnly();
    }

    public async Task<DiagnosticSnapshotLoad> LoadDiagnosticSnapshotAsync(Guid snapshotId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        string id = snapshotId.ToString("D", CultureInfo.InvariantCulture);
        long policy, count, bytes; string collectedText, expiresText, fromText;
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT policy_version,collected_at,expires_at,lookback_from,record_count,payload_bytes FROM diagnostic_snapshots WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return new(DiagnosticSnapshotLoadStatus.Unavailable, null);
            if (reader.GetValue(0) is not long p || reader.GetValue(1) is not string c || reader.GetValue(2) is not string e || reader.GetValue(3) is not string f ||
                reader.GetValue(4) is not long n || reader.GetValue(5) is not long b)
                return new(DiagnosticSnapshotLoadStatus.Unreadable, null);
            (policy, collectedText, expiresText, fromText, count, bytes) = (p, c, e, f, n, b);
        }
        if (!TryParseUtc(collectedText, out DateTimeOffset collected) || !TryParseUtc(expiresText, out DateTimeOffset expires) || !TryParseUtc(fromText, out DateTimeOffset from))
            return new(DiagnosticSnapshotLoadStatus.Unreadable, null);
        if (expires <= nowUtc.ToUniversalTime()) return new(DiagnosticSnapshotLoadStatus.Unavailable, null);

        IReadOnlyList<DiagnosticSourceResult>? sources = await ReadSourcesAsync(connection, snapshotId, cancellationToken).ConfigureAwait(false);
        if (sources is null) return new(DiagnosticSnapshotLoadStatus.Unreadable, null);
        List<DiagnosticRecord> records = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ordinal,payload FROM diagnostic_records WHERE snapshot_id = $id ORDER BY ordinal LIMIT 2001;";
            command.Parameters.AddWithValue("$id", id);
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetValue(0) is not long ordinal || ordinal != records.Count || reader.GetValue(1) is not string payload ||
                    !DiagnosticRecordJson.TryParse(payload, out DiagnosticRecord? record))
                    return new(DiagnosticSnapshotLoadStatus.Unreadable, null);
                records.Add(record!);
            }
        }
        DiagnosticSnapshot snapshot = new(snapshotId, (int)policy, collected, expires, from, sources, records.AsReadOnly(), bytes);
        if (records.Count != count || !TryEncode(snapshot, out _, out long recomputed) || recomputed != bytes) return new(DiagnosticSnapshotLoadStatus.Unreadable, null);
        return new(DiagnosticSnapshotLoadStatus.Loaded, snapshot);
    }

    public async Task<int> DeleteExpiredDiagnosticSnapshotsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        int deleted = await DeleteExpiredAsync(connection, transaction, nowUtc, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public async Task<(IReadOnlyList<DiagnosticAppHistoryEntry?> Entries, bool MoreMatched)> GetUpdateHistoryBetweenAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken)
    {
        if (maximumEntries is < 1 or > DiagnosticCollectionPolicy.MaximumRecordsPerSource) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        List<DiagnosticAppHistoryEntry?> entries = [];
        bool more = false;
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        // The interval filter runs in SQLite on the parsed instant, so stored offsets cannot reorder it, and at most one row past
        // the limit is read to learn whether more matched.
        command.CommandText = """
            SELECT h.event_id,h.job_id,h.correlation_id,h.stable_product_key,h.provider,h.package_id,h.installed_version,h.target_version,h.occurred_at,h.status,h.exit_code,h.diagnostic,
                   (SELECT p.display_name FROM canonical_products p WHERE p.stable_key = h.stable_product_key LIMIT 1)
            FROM update_job_history h
            WHERE julianday(h.occurred_at) BETWEEN julianday($from) AND julianday($to)
            ORDER BY julianday(h.occurred_at) DESC, h.event_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$from", Utc(fromUtc));
        command.Parameters.AddWithValue("$to", Utc(toUtc));
        command.Parameters.AddWithValue("$limit", maximumEntries + 1);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Every matching row counts, readable or not, so an unreadable row is disclosed and cannot hide truncation.
            if (entries.Count == maximumEntries) { more = true; break; }
            entries.Add(TryReadHistoryEntry(reader, out UpdateHistoryEntry? entry) ? new(entry!, reader.GetValue(12) as string) : null);
        }
        return (entries.AsReadOnly(), more);
    }

    private static bool TryEncode(DiagnosticSnapshot snapshot, out string[]? payloads, out long payloadBytes)
    {
        payloads = null;
        payloadBytes = 0;
        if (snapshot.Id == Guid.Empty || snapshot.PolicyVersion != DiagnosticCollectionPolicy.Version ||
            snapshot.CollectedAtUtc.Offset != TimeSpan.Zero || snapshot.ExpiresAtUtc != DiagnosticCollectionPolicy.ExpiresAt(snapshot.CollectedAtUtc) ||
            snapshot.LookbackFromUtc < DiagnosticCollectionPolicy.LookbackStart(snapshot.CollectedAtUtc) || snapshot.LookbackFromUtc >= snapshot.CollectedAtUtc ||
            snapshot.Records.Count > DiagnosticCollectionPolicy.MaximumRecordsOverall ||
            snapshot.Sources.Count != Enum.GetValues<DiagnosticSourceKind>().Length ||
            !snapshot.Sources.Select(source => source.Source).Order().SequenceEqual(Enum.GetValues<DiagnosticSourceKind>()) ||
            snapshot.Sources.Any(source => !Enum.IsDefined(source.Outcome) || !Enum.IsDefined(source.Reason) ||
                source.AllowedRecords is < 0 or > DiagnosticCollectionPolicy.MaximumRecordsPerSource ||
                source.MatchingRecordsReturned is < 0 or > DiagnosticCollectionPolicy.MaximumRecordsPerSource ||
                source.RecordsKept is < 0 or > DiagnosticCollectionPolicy.MaximumRecordsPerSource ||
                source.RecordsRejected is < 0 or > DiagnosticCollectionPolicy.MaximumRecordsPerSource))
            return false;
        string[] encoded = new string[snapshot.Records.Count];
        long total = 0;
        for (int index = 0; index < encoded.Length; index++)
        {
            DiagnosticRecord record = snapshot.Records[index];
            if (record is null || !DiagnosticRecordAdmission.IsValidStored(record)) return false;
            encoded[index] = DiagnosticRecordJson.Serialize(record);
            total += System.Text.Encoding.UTF8.GetByteCount(encoded[index]) + 1L;
        }
        if (total > DiagnosticCollectionPolicy.MaximumCollectionBytes - DiagnosticCollectionPolicy.ReservedEnvelopeBytes) return false;
        payloads = encoded;
        payloadBytes = total;
        return true;
    }

    private static async Task<IReadOnlyList<DiagnosticSourceResult>?> ReadSourcesAsync(SqliteConnection connection, Guid snapshotId, CancellationToken cancellationToken)
    {
        List<DiagnosticSourceResult> sources = [];
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT source,outcome,reason,queried_from,queried_to,allowed_records,returned_records,kept_records,rejected_records FROM diagnostic_source_results WHERE snapshot_id = $id LIMIT 6;";
        command.Parameters.AddWithValue("$id", snapshotId.ToString("D", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!TryName(reader.GetValue(0), out DiagnosticSourceKind source) || !TryName(reader.GetValue(1), out DiagnosticSourceOutcome outcome) ||
                !TryName(reader.GetValue(2), out DiagnosticSourceReason reason) ||
                reader.GetValue(3) is not string fromText || !TryParseUtc(fromText, out DateTimeOffset from) ||
                reader.GetValue(4) is not string toText || !TryParseUtc(toText, out DateTimeOffset to) ||
                reader.GetValue(5) is not long allowed || reader.GetValue(6) is not long returned || reader.GetValue(7) is not long kept || reader.GetValue(8) is not long rejected)
                return null;
            sources.Add(new(source, outcome, reason, from, to, (int)allowed, (int)returned, (int)kept, (int)rejected));
        }
        sources.Sort((left, right) => left.Source.CompareTo(right.Source));
        return sources.Select(result => result.Source).SequenceEqual(Enum.GetValues<DiagnosticSourceKind>()) ? sources.AsReadOnly() : null;
    }

    private static async Task<int> DeleteExpiredAsync(SqliteConnection connection, SqliteTransaction transaction, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        List<string> expired = [];
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT id FROM diagnostic_snapshots WHERE expires_at <= $now;";
            command.Parameters.AddWithValue("$now", Utc(nowUtc));
            await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) expired.Add(reader.GetString(0));
        }
        foreach (string id in expired) await DeleteSnapshotAsync(connection, transaction, id, cancellationToken).ConfigureAwait(false);
        return expired.Count;
    }

    private static async Task DeleteSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, string id, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, "DELETE FROM diagnostic_records WHERE snapshot_id = $id; DELETE FROM diagnostic_source_results WHERE snapshot_id = $id; DELETE FROM diagnostic_snapshots WHERE id = $id;",
            cancellationToken, transaction, ("$id", id)).ConfigureAwait(false);

    private static async Task<long> TotalDiagnosticBytesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // Each snapshot is charged its record bytes plus the fixed envelope charge (snapshot row and source results).
        command.CommandText = "SELECT COALESCE(SUM(payload_bytes), 0) + (COUNT(*) * $envelope) FROM diagnostic_snapshots;";
        command.Parameters.AddWithValue("$envelope", DiagnosticCollectionPolicy.ReservedEnvelopeBytes);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> OldestPrunableAsync(SqliteConnection connection, SqliteTransaction transaction, HashSet<string> keep, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM diagnostic_snapshots ORDER BY collected_at ASC, id ASC;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string id = reader.GetString(0);
            if (!keep.Contains(id)) return id;
        }
        return null;
    }

    private static bool TryName<TEnum>(object value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        return value is string text && Enum.GetNames<TEnum>().Contains(text, StringComparer.Ordinal) && Enum.TryParse(text, false, out result);
    }

    /// <summary>Fixed-width UTC text (28 characters), so text order is time order inside these tables.</summary>
    private static string Utc(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static bool TryParseUtc(string text, out DateTimeOffset value)
    {
        value = default;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)) return false;
        value = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
        return true;
    }
}
