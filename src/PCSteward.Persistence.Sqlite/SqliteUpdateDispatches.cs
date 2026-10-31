using PCSteward.Application;
using PCSteward.Domain;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Security.Cryptography;

namespace PCSteward.Persistence.Sqlite;

public sealed partial class SqliteDatabase : IUpdateDispatchStore
{
    private static async Task CreateUpdateDispatchSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await ExecuteAsync(connection, """
            CREATE TABLE update_dispatch_claims (
                job_id TEXT NOT NULL PRIMARY KEY CHECK(length(job_id)=36),
                claim_id TEXT NOT NULL UNIQUE CHECK(length(claim_id)=36),
                terminal_event_id TEXT NOT NULL UNIQUE CHECK(length(terminal_event_id)=36),
                draft_generation TEXT NOT NULL CHECK(length(draft_generation)=36),
                canonical_payload BLOB NOT NULL CHECK(typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384),
                canonical_digest TEXT NOT NULL CHECK(length(canonical_digest)=64),
                job_digest TEXT NOT NULL CHECK(length(job_digest)=64),
                fresh_evidence BLOB NOT NULL CHECK(typeof(fresh_evidence)='blob' AND length(fresh_evidence) BETWEEN 1 AND 8192),
                claimed_at TEXT NOT NULL CHECK(length(claimed_at)=33),
                state TEXT NOT NULL CHECK(state IN ('DispatchClaimed','Final','Uncertain')),
                terminal_payload BLOB NOT NULL CHECK(typeof(terminal_payload)='blob' AND length(terminal_payload)=16384),
                terminal_digest TEXT,
                CHECK((state='DispatchClaimed' AND terminal_digest IS NULL) OR (state IN ('Final','Uncertain') AND terminal_digest IS NOT NULL AND typeof(terminal_digest)='text' AND length(terminal_digest)=64))
            );
            """, ct, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "CREATE UNIQUE INDEX ix_update_dispatch_unresolved ON update_dispatch_claims((1)) WHERE state IN ('DispatchClaimed','Uncertain');", ct, transaction).ConfigureAwait(false);
        await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES(11);", ct, transaction).ConfigureAwait(false);
    }

    private static async Task ValidateUpdateDispatchSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT job_id,claim_id,terminal_event_id,draft_generation,canonical_payload,canonical_digest,job_digest,fresh_evidence,claimed_at,state,terminal_payload,terminal_digest FROM update_dispatch_claims LIMIT 0;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenDispatchConnectionAsync(CancellationToken ct)
    {
        SqliteConnection connection = new(connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await RefuseFutureSchemaAsync(connection, ct).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA synchronous=FULL;", ct).ConfigureAwait(false);
            await ExecuteAsync(connection, "PRAGMA busy_timeout=5000;", ct).ConfigureAwait(false);
            await using SqliteCommand check = connection.CreateCommand();
            check.CommandText = "PRAGMA synchronous;";
            if (Convert.ToInt64(await check.ExecuteScalarAsync(ct).ConfigureAwait(false), CultureInfo.InvariantCulture) != 2)
                throw new InvalidOperationException("Full claim durability could not be established.");
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence,
        OperationOwnership ownership, DateTimeOffset claimedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(ownership);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ownership.CanDispatchFor(draft.Operation) || !evidence.Matches(draft.Operation, claimedAt)) return null;
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), draft, evidence, claimedAt.ToUniversalTime());
        await using SqliteConnection connection = await OpenDispatchConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await RefuseFutureSchemaAsync(connection, cancellationToken, transaction).ConfigureAwait(false);
        await using SqliteCommand command = ClaimCommand(connection, transaction, claim);
        command.CommandText = """
            INSERT INTO update_dispatch_claims(job_id,claim_id,terminal_event_id,draft_generation,canonical_payload,canonical_digest,job_digest,fresh_evidence,claimed_at,state,terminal_payload)
            SELECT job_id,$claim,$event,draft_generation,canonical_payload,canonical_digest,$jobDigest,$evidence,$at,'DispatchClaimed',zeroblob(16384)
            FROM reviewed_update_operations
            WHERE job_id=$job AND draft_generation=$generation AND schema_version=3 AND canonical_digest=$digest AND canonical_payload=$payload
                AND NOT EXISTS(SELECT 1 FROM update_dispatch_claims WHERE state IN ('DispatchClaimed','Uncertain'))
            ON CONFLICT(job_id) DO NOTHING;
            """;
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1) return null;
        command.CommandText = $"SELECT COUNT(*) FROM update_dispatch_claims WHERE {ExactClaimPredicate} AND state='DispatchClaimed' AND terminal_digest IS NULL AND terminal_payload=zeroblob(16384);";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("Stored claim differs from the reviewed dispatch identity.");
        StoredReviewedOperation? storedDraft = await ReadReviewedOperationAsync(connection, transaction, draft.Operation.JobId, cancellationToken).ConfigureAwait(false);
        if (storedDraft?.Generation != draft.Generation || !draft.Operation.HasSameIdentity(storedDraft.Operation))
            throw new InvalidOperationException("The original saved draft changed during claim creation.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!ownership.CanDispatchFor(draft.Operation)) throw new InvalidOperationException("Operation ownership ended before claim commit.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return claim;
    }

    public async Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal, UpdateDispatchSettlement settlement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(settlement);
        if (!settlement.TryConsumeFor(claim, ownership) ||
            (terminal.Outcome != UpdateDispatchOutcome.Uncertain && !settlement.ProvesSettlement) ||
            (terminal.Outcome is UpdateDispatchOutcome.Success or UpdateDispatchOutcome.RestartRequired && !settlement.PermitsVerifiedOutcome))
            throw new InvalidOperationException("Final state requires matching live ownership and positive native settlement; unresolved work remains conflicting.");
        if (!ownership.CanDispatchFor(claim.Draft.Operation) || terminal.OccurredAt < claim.ClaimedAt)
            throw new InvalidOperationException("Final recording requires current ownership and a valid result time.");
        byte[] payload = terminal.GetReservedPayload(settlement);
        await using SqliteConnection connection = await OpenDispatchConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using SqliteCommand command = ClaimCommand(connection, transaction, claim);
        command.Parameters.Add("$terminal", SqliteType.Blob).Value = payload;
        command.Parameters.AddWithValue("$terminalDigest", Convert.ToHexString(SHA256.HashData(payload)));
        string state = terminal.Outcome == UpdateDispatchOutcome.Uncertain ? "Uncertain" : "Final";
        command.Parameters.AddWithValue("$state", state);
        command.CommandText = $"UPDATE update_dispatch_claims SET state=$state,terminal_payload=$terminal,terminal_digest=$terminalDigest WHERE {ExactClaimPredicate} AND state='DispatchClaimed';";
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Claim is missing, changed or already finalized; outcome remains uncertain.");
        command.CommandText = $"SELECT COUNT(*) FROM update_dispatch_claims WHERE {ExactClaimPredicate} AND state=$state AND terminal_payload=$terminal AND terminal_digest=$terminalDigest;";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) != 1 || !ownership.CanDispatchFor(claim.Draft.Operation))
            throw new InvalidOperationException("Final recording changed or ownership ended before commit.");
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!ownership.CanReconcile || now == default) throw new InvalidOperationException("Reconciliation requires production operation ownership.");
        await using SqliteConnection connection = await OpenDispatchConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT CASE WHEN typeof(job_id)='text' AND length(job_id)=36 THEN job_id END FROM update_dispatch_claims WHERE state='DispatchClaimed' LIMIT 2;";
        List<Guid> jobs = [];
        await using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.GetValue(0) is not string value || !Guid.TryParseExact(value, "D", out Guid job) || job == Guid.Empty || job.ToString("D") != value)
                    throw new InvalidOperationException("Unfinished dispatch identity is corrupt.");
                jobs.Add(job);
            }
        }
        if (jobs.Count > 1) throw new InvalidOperationException("Conflicting unfinished dispatch records require explicit reconciliation.");
        foreach (Guid job in jobs)
        {
            ReviewedUpdateOperation operation = (await ReadDispatchStateAsync(connection, transaction, job, cancellationToken).ConfigureAwait(false))!.Value.Operation;
            if (operation.RequestingUserSid != ownership.RequestingUserSid) throw new InvalidOperationException("Recovery requester differs from the operation owner.");
        }
        byte[] payload = new UpdateDispatchTerminal(UpdateDispatchOutcome.Uncertain, now, null, "Interrupted dispatch; invocation and result are uncertain. Never replay automatically.").GetReservedPayload();
        command.CommandText = "UPDATE update_dispatch_claims SET state='Uncertain',terminal_payload=$terminal,terminal_digest=$digest WHERE state='DispatchClaimed';";
        command.Parameters.Add("$terminal", SqliteType.Blob).Value = payload;
        command.Parameters.AddWithValue("$digest", Convert.ToHexString(SHA256.HashData(payload)));
        int recovered = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (recovered != jobs.Count || !ownership.CanReconcile) throw new InvalidOperationException("Recovery identity or ownership changed.");
        foreach (Guid job in jobs)
        {
            (UpdateDispatchState State, ReviewedUpdateOperation Operation)? recoveredState = await ReadDispatchStateAsync(connection, transaction, job, cancellationToken).ConfigureAwait(false);
            if (recoveredState?.State != UpdateDispatchState.Uncertain || recoveredState.Value.Operation.RequestingUserSid != ownership.RequestingUserSid)
                throw new InvalidOperationException("Recovered record differs from the operation owner.");
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return recovered;
    }

    public async Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A dispatch job identity is required.", nameof(jobId));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadDispatchStateAsync(connection, null, jobId, cancellationToken).ConfigureAwait(false))?.State;
    }

    private static async Task<(UpdateDispatchState State, ReviewedUpdateOperation Operation)?> ReadDispatchStateAsync(SqliteConnection connection,
        SqliteTransaction? transaction, Guid jobId, CancellationToken ct)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT CASE WHEN typeof(state)='text' AND length(state)<=15 THEN state END,
                CASE WHEN typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384 THEN canonical_payload END,
                CASE WHEN typeof(canonical_digest)='text' AND length(canonical_digest)=64 THEN canonical_digest END,
                CASE WHEN typeof(terminal_payload)='blob' AND length(terminal_payload)=16384 THEN terminal_payload END,
                CASE WHEN typeof(terminal_digest)='text' AND length(terminal_digest)=64 THEN terminal_digest END,
                CASE WHEN typeof(draft_generation)='text' AND length(draft_generation)=36 THEN draft_generation END
            FROM update_dispatch_claims WHERE job_id=$job LIMIT 2;
            """;
        command.Parameters.AddWithValue("$job", jobId.ToString("D", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        if (reader.GetValue(0) is not string stateText || !Enum.TryParse(stateText, false, out UpdateDispatchState state) || !Enum.IsDefined(state) || state.ToString() != stateText ||
            reader.GetValue(1) is not byte[] canonical || reader.GetValue(2) is not string digest ||
            !ReviewedUpdateOperation.TryRestore(canonical, digest, out ReviewedUpdateOperation? operation) || operation is null || operation.JobId != jobId ||
            reader.GetValue(3) is not byte[] terminal || reader.GetValue(5) is not string generation ||
            !Guid.TryParseExact(generation, "D", out Guid parsedGeneration) || parsedGeneration == Guid.Empty || parsedGeneration.ToString("D") != generation ||
            (state == UpdateDispatchState.DispatchClaimed ? !reader.IsDBNull(4) || terminal.Any(b => b != 0) : reader.GetValue(4) is not string terminalDigest || terminalDigest != Convert.ToHexString(SHA256.HashData(terminal))))
            throw new InvalidOperationException("Persisted dispatch data is corrupt; execution remains unavailable.");
        if (await reader.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidOperationException("Duplicate dispatch identity.");
        return (state, operation);
    }

    private const string ExactClaimPredicate = "job_id=$job AND claim_id=$claim AND terminal_event_id=$event AND draft_generation=$generation AND canonical_digest=$digest AND canonical_payload=$payload AND job_digest=$jobDigest AND fresh_evidence=$evidence AND claimed_at=$at";

    public async Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        if (!ownership.CanReconcile) throw new InvalidOperationException("Barrier checks require current production-gate ownership.");
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM update_dispatch_claims WHERE state IS NULL OR state<>'Final' LIMIT 1;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        bool unresolved = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        // Presence is a barrier even if identity/state is unreadable. No persisted row grants a permit.
        if (!ownership.CanReconcile) throw new InvalidOperationException("Operation ownership ended during barrier checks.");
        return unresolved;
    }

    private static SqliteCommand ClaimCommand(SqliteConnection connection, SqliteTransaction transaction, UpdateDispatchClaim claim)
    {
        SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        ReviewedUpdateOperation operation = claim.Draft.Operation;
        command.Parameters.AddWithValue("$job", operation.JobId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$claim", claim.ClaimId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$event", claim.TerminalEventId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$generation", claim.Draft.Generation.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$digest", operation.Digest);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = operation.GetCanonicalBytes();
        command.Parameters.Add("$evidence", SqliteType.Blob).Value = claim.Evidence.GetCanonicalBytes();
        command.Parameters.AddWithValue("$jobDigest", operation.LegacyJobDigest);
        command.Parameters.AddWithValue("$at", claim.ClaimedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return command;
    }
}
