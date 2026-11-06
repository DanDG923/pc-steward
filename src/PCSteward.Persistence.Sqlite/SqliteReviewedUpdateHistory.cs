using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

/// <summary>Read-only history projection of dispatch claims. Rows are untrusted data; nothing here restores a permit or settlement.</summary>
public sealed partial class SqliteDatabase : IReviewedUpdateHistoryReader
{
    public const int MaximumReviewedHistoryRecords = 500;

    public async Task<IReadOnlyList<ReviewedUpdateHistoryRecord>> GetReviewedUpdateHistoryAsync(int maximumRecords, CancellationToken cancellationToken)
    {
        if (maximumRecords is < 1 or > MaximumReviewedHistoryRecords) throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await RefuseFutureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        // Bound every projected value before the provider materializes it.
        command.CommandText = """
            SELECT CASE WHEN typeof(job_id)='text' AND length(job_id)=36 THEN job_id END,
                CASE WHEN typeof(canonical_payload)='blob' AND length(canonical_payload) BETWEEN 1 AND 16384 THEN canonical_payload END,
                CASE WHEN typeof(canonical_digest)='text' AND length(canonical_digest)=64 THEN canonical_digest END,
                CASE WHEN typeof(state)='text' AND state IN ('DispatchClaimed','Final','Uncertain') THEN state END,
                CASE WHEN typeof(claimed_at)='text' AND length(claimed_at)=33 THEN claimed_at END,
                CASE WHEN typeof(terminal_payload)='blob' AND length(terminal_payload)=16384 THEN terminal_payload END,
                CASE WHEN typeof(terminal_digest)='text' AND length(terminal_digest)=64 THEN terminal_digest END
            FROM update_dispatch_claims
            ORDER BY CASE WHEN typeof(state)='text' AND state='Final' THEN 1 ELSE 0 END, claimed_at DESC, job_id LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", maximumRecords);
        List<ReviewedUpdateHistoryRecord> records = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            records.Add(ReadHistoryRow(reader));
        return records.AsReadOnly();
    }

    private ReviewedUpdateHistoryRecord ReadHistoryRow(SqliteDataReader reader)
    {
        Guid jobId = reader.GetValue(0) is string jobText && Guid.TryParseExact(jobText, "D", out Guid parsedJob) && parsedJob != Guid.Empty ? parsedJob : Guid.Empty;
        // An unreadable state is presented as unresolved, matching the barrier predicate.
        UpdateDispatchState state = reader.GetValue(3) is string stateText && Enum.TryParse(stateText, false, out UpdateDispatchState parsedState)
            ? parsedState : UpdateDispatchState.Uncertain;
        DateTimeOffset claimedAt = reader.GetValue(4) is string atText &&
            DateTimeOffset.TryParseExact(atText, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedAt) ? parsedAt : default;
        bool integrity = jobId != Guid.Empty && claimedAt != default && reader.GetValue(3) is string;
        ReviewedUpdateOperation? operation = null;
        if (reader.GetValue(1) is byte[] payload && reader.GetValue(2) is string digest &&
            ReviewedUpdateOperation.TryRestore(payload, digest, out ReviewedUpdateOperation? restored) && restored is not null &&
            restored.JobId == jobId && restored.HasCurrentSchema)
            operation = restored;
        else integrity = false;

        ReviewedUpdateTerminalRecord? terminal = null;
        byte[]? reserved = reader.GetValue(5) as byte[];
        if (state == UpdateDispatchState.DispatchClaimed)
        {
            if (reserved is null || reserved.Any(value => value != 0) || !reader.IsDBNull(6)) integrity = false;
        }
        else if (reserved is not null && reader.GetValue(6) is string terminalDigest &&
            terminalDigest == Convert.ToHexString(SHA256.HashData(reserved)) &&
            ReviewedUpdateTerminalRecord.TryParse(reserved, helperVersionGrammar, out ReviewedUpdateTerminalRecord? parsedTerminal) && parsedTerminal is not null &&
            AgreesWithRow(parsedTerminal, integrity ? operation : null, state, claimedAt))
            terminal = parsedTerminal;
        else integrity = false;
        return new(jobId, operation, state, claimedAt, terminal, integrity);
    }

    // B2-L2: the store refuses to commit a version-3 terminal that this same history reader would reject.
    private bool IsReadableHelperTerminal(byte[] reserved, ReviewedUpdateOperation operation, UpdateDispatchState state, DateTimeOffset claimedAt) =>
        helperVersionGrammar is not null &&
        ReviewedUpdateTerminalRecord.TryParse(reserved, helperVersionGrammar, out ReviewedUpdateTerminalRecord? parsed) && parsed is not null &&
        parsed.HelperCheck.Kind != RecordedHelperCheckKind.NotObserved && AgreesWithRow(parsed, operation, state, claimedAt);

    // A recorded helper check needs the restored, integrity-verified operation (O-none); NotObserved keeps the v2 rules.
    private static bool AgreesWithRow(ReviewedUpdateTerminalRecord terminal, ReviewedUpdateOperation? operation, UpdateDispatchState state,
        DateTimeOffset claimedAt) =>
        (terminal.Outcome == UpdateDispatchOutcome.Uncertain) == (state == UpdateDispatchState.Uncertain) &&
        SatisfiesWriteInvariants(terminal) && (claimedAt == default || terminal.OccurredAt >= claimedAt) &&
        terminal.HelperCheck.IsConsistentWith(operation, terminal.Outcome, terminal.Settlement);

    // Mirrors RecordFinalAsync: a row the store could never have written is corruption, not history. This detects
    // corruption only; an unkeyed digest cannot stop a same-user writer from forging a consistent row.
    private static bool SatisfiesWriteInvariants(ReviewedUpdateTerminalRecord terminal) =>
        (terminal.Settlement != UpdateDispatchSettlementState.NotInvoked || terminal.ExitCode is null) && terminal.Outcome switch
    {
        UpdateDispatchOutcome.Uncertain => true,
        UpdateDispatchOutcome.Cancelled => terminal.Settlement == UpdateDispatchSettlementState.NotInvoked,
        UpdateDispatchOutcome.Success or UpdateDispatchOutcome.RestartRequired or UpdateDispatchOutcome.Unverified =>
            terminal.Settlement == UpdateDispatchSettlementState.OwnedWorkStopped,
        _ => terminal.Settlement is UpdateDispatchSettlementState.NotInvoked or UpdateDispatchSettlementState.OwnedWorkStopped,
    };
}
