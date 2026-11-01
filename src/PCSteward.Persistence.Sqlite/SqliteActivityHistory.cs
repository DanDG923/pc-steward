using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

/// <summary>
/// FR-HIS-01 (Task 73 Phase 3): bounded, read-only projections of recorded update history and scan runs for the Activity page.
/// No schema change and no write: the queries only read rows that already exist, newest first, and skip any row that does not
/// parse. Rows are untrusted data and never execution authority.
/// </summary>
public sealed partial class SqliteDatabase : IActivityHistoryReader
{
    public const int MaximumActivityRecords = 500;

    public async Task<IReadOnlyList<UpdateHistoryEntry>> GetRecentUpdateHistoryAsync(int maximumEntries, CancellationToken cancellationToken)
    {
        if (maximumEntries is < 1 or > MaximumActivityRecords) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        List<UpdateHistoryEntry> entries = [];
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT event_id,job_id,correlation_id,stable_product_key,provider,package_id,installed_version,target_version,occurred_at,status,exit_code,diagnostic FROM update_job_history ORDER BY occurred_at DESC,event_id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", maximumEntries);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (TryReadHistoryEntry(reader, out UpdateHistoryEntry? entry)) entries.Add(entry!);
        }
        return entries.AsReadOnly();
    }

    public async Task<IReadOnlyList<ScanRun>> GetRecentScanRunsAsync(int maximumRuns, CancellationToken cancellationToken)
    {
        if (maximumRuns is < 1 or > MaximumActivityRecords) throw new ArgumentOutOfRangeException(nameof(maximumRuns));
        List<ScanRun> runs = [];
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,started_at,completed_at,status,correlation_id FROM scan_runs ORDER BY started_at DESC,id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", maximumRuns);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (reader.GetValue(0) is string idText && Guid.TryParse(idText, out Guid id) &&
                reader.GetValue(1) is string startedText && TryParseTime(startedText, out DateTimeOffset started) &&
                reader.GetValue(3) is string status &&
                reader.GetValue(4) is string correlationText && Guid.TryParse(correlationText, out Guid correlation))
            {
                DateTimeOffset? completed = reader.GetValue(2) is string completedText && TryParseTime(completedText, out DateTimeOffset parsed) ? parsed : null;
                runs.Add(new ScanRun(id, started, completed, status, correlation));
            }
        }
        return runs.AsReadOnly();
    }

    private static bool TryReadHistoryEntry(SqliteDataReader reader, out UpdateHistoryEntry? entry)
    {
        entry = null;
        if (reader.GetValue(0) is not string eventText || !Guid.TryParse(eventText, out Guid eventId) ||
            reader.GetValue(1) is not string jobText || !Guid.TryParse(jobText, out Guid jobId) ||
            reader.GetValue(2) is not string correlationText || !Guid.TryParse(correlationText, out Guid correlationId) ||
            reader.GetValue(3) is not string stableKey ||
            reader.GetValue(4) is not string providerText || !Enum.TryParse(providerText, false, out UpdateProvider provider) || !Enum.IsDefined(provider) ||
            reader.GetValue(5) is not string packageId ||
            reader.GetValue(6) is not string installedVersion ||
            reader.GetValue(7) is not string targetVersion ||
            reader.GetValue(8) is not string occurredText || !TryParseTime(occurredText, out DateTimeOffset occurredAt) ||
            reader.GetValue(9) is not string statusText || !Enum.TryParse(statusText, false, out UpdateHistoryStatus status) || !Enum.IsDefined(status) ||
            reader.GetValue(11) is not string diagnostic)
            return false;
        int? exitCode = reader.GetValue(10) is long exit && exit is >= int.MinValue and <= int.MaxValue ? (int)exit : null;
        entry = new UpdateHistoryEntry(eventId, jobId, correlationId, stableKey, provider, packageId, installedVersion, targetVersion, occurredAt, status, exitCode, diagnostic);
        return true;
    }

    private static bool TryParseTime(string text, out DateTimeOffset value) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
}
