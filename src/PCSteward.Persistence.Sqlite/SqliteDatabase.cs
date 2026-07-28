using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

public sealed class SqliteDatabase : IScanDataStore, IStableDecisionStore
{
    private const int MaxTextLength = 1024;
    private readonly string connectionString;

    public SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;", cancellationToken);
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY);", cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS canonical_products (id TEXT PRIMARY KEY, stable_key TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL); CREATE TABLE IF NOT EXISTS installation_instances (id TEXT PRIMARY KEY, product_id TEXT NOT NULL, provider TEXT NOT NULL, provider_identity TEXT NOT NULL, version TEXT, scope TEXT NOT NULL, architecture TEXT NOT NULL, FOREIGN KEY(product_id) REFERENCES canonical_products(id)); CREATE TABLE IF NOT EXISTS stable_decisions (id TEXT PRIMARY KEY, stable_product_key TEXT NOT NULL, kind TEXT NOT NULL, value TEXT NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS scan_runs (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, completed_at TEXT, status TEXT NOT NULL, correlation_id TEXT NOT NULL); CREATE TABLE IF NOT EXISTS raw_provider_records (id TEXT PRIMARY KEY, scan_run_id TEXT NOT NULL, provider TEXT NOT NULL, record_key TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS scan_provider_timings (id TEXT PRIMARY KEY, scan_run_id TEXT NOT NULL, provider TEXT NOT NULL, duration_ms INTEGER NOT NULL, warning TEXT); CREATE TABLE IF NOT EXISTS update_candidates (id TEXT PRIMARY KEY, product_id TEXT NOT NULL, provider TEXT NOT NULL, package_id TEXT NOT NULL, version TEXT); CREATE TABLE IF NOT EXISTS manual_mappings (id TEXT PRIMARY KEY, stable_product_key TEXT NOT NULL, source_id TEXT NOT NULL, package_id TEXT NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS update_jobs (id TEXT PRIMARY KEY, provider TEXT NOT NULL, source_id TEXT NOT NULL, package_id TEXT NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS downloaded_artifacts (id TEXT PRIMARY KEY, category TEXT NOT NULL, relative_path TEXT NOT NULL, size_bytes INTEGER NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS app_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);", cancellationToken, transaction);
        await ExecuteAsync(connection, "INSERT OR IGNORE INTO schema_migrations(version) VALUES (1);", cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "DELETE FROM installation_instances; DELETE FROM canonical_products;", cancellationToken, transaction);
        foreach (CanonicalProduct product in products) await ExecuteAsync(connection, "INSERT INTO canonical_products VALUES ($id,$key,$name);", cancellationToken, transaction, ("$id", product.Id), ("$key", Text(product.StableKey)), ("$name", Text(product.DisplayName)));
        foreach (InstallationInstance instance in instances) await ExecuteAsync(connection, "INSERT INTO installation_instances VALUES ($id,$product,$provider,$identity,$version,$scope,$architecture);", cancellationToken, transaction, ("$id", instance.Id), ("$product", instance.ProductId), ("$provider", Text(instance.Provider)), ("$identity", Text(instance.ProviderIdentity)), ("$version", Text(instance.Version)), ("$scope", Text(instance.Scope)), ("$architecture", Text(instance.Architecture)));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PersistScanAsync(ScanRun run, IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, IReadOnlyCollection<RawProviderRecord> rawRecords, IReadOnlyCollection<ScanProviderTiming> timings, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "DELETE FROM raw_provider_records; DELETE FROM scan_provider_timings; DELETE FROM installation_instances; DELETE FROM canonical_products;", cancellationToken, transaction);
        foreach (CanonicalProduct product in products) await ExecuteAsync(connection, "INSERT INTO canonical_products VALUES ($id,$key,$name);", cancellationToken, transaction, ("$id", product.Id), ("$key", Text(product.StableKey)), ("$name", Text(product.DisplayName)));
        foreach (InstallationInstance instance in instances) await ExecuteAsync(connection, "INSERT INTO installation_instances VALUES ($id,$product,$provider,$identity,$version,$scope,$architecture);", cancellationToken, transaction, ("$id", instance.Id), ("$product", instance.ProductId), ("$provider", Text(instance.Provider)), ("$identity", Text(instance.ProviderIdentity)), ("$version", Text(instance.Version)), ("$scope", Text(instance.Scope)), ("$architecture", Text(instance.Architecture)));
        await InsertRunAsync(connection, run, timings, transaction, cancellationToken);
        foreach (RawProviderRecord raw in rawRecords) await ExecuteAsync(connection, "INSERT INTO raw_provider_records VALUES ($id,$scan,$provider,$key,$payload);", cancellationToken, transaction, ("$id", raw.Id), ("$scan", raw.ScanRunId), ("$provider", Text(raw.Provider)), ("$key", Text(raw.RecordKey)), ("$payload", Text(raw.Payload)));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ('scan_last_completed', $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$value", run.CompletedAt));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordScanAsync(ScanRun run, IReadOnlyCollection<ScanProviderTiming> timings, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction(); await InsertRunAsync(connection, run, timings, transaction, cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<CanonicalProduct>> GetLastKnownGoodProductsAsync(CancellationToken cancellationToken)
    {
        List<CanonicalProduct> result = [];
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, stable_key, display_name FROM canonical_products ORDER BY stable_key;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new CanonicalProduct(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    public async Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken)
    {
        List<InstallationInstance> result = [];
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, product_id, provider, provider_identity, version, scope, architecture FROM installation_instances ORDER BY provider, provider_identity;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new InstallationInstance(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6)));
        return result;
    }

    public async Task<DateTimeOffset?> GetLastCompletedScanAtAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key = 'scan_last_completed';"; object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;
    }

    public async Task<bool> GetConnectorEnabledAsync(string connectorName, bool defaultValue, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key = $key;"; command.Parameters.AddWithValue("$key", ConnectorKey(connectorName)); object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text && bool.TryParse(text, out bool parsed) ? parsed : defaultValue;
    }

    public async Task SaveConnectorEnabledAsync(string connectorName, bool enabled, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, null, ("$key", ConnectorKey(connectorName)), ("$value", enabled));
    }

    public async Task SaveAsync(StableDecision decision, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO stable_decisions VALUES ($id,$key,$kind,$value,$created);", cancellationToken, null, ("$id", decision.Id), ("$key", Text(decision.StableProductKey)), ("$kind", Text(decision.Kind)), ("$value", Text(decision.Value)), ("$created", decision.CreatedAt));
    }

    public async Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        List<StableDecision> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, stable_product_key, kind, value, created_at FROM stable_decisions WHERE stable_product_key = $key;"; command.Parameters.AddWithValue("$key", Text(stableProductKey)); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(new StableDecision(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))); return result;
    }

    public async Task<ScanDataSummary> GetScanDataSummaryAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM canonical_products) + (SELECT COUNT(*) FROM installation_instances) + (SELECT COUNT(*) FROM raw_provider_records) + (SELECT COUNT(*) FROM scan_provider_timings) + (SELECT COUNT(*) FROM scan_runs) + (SELECT COUNT(*) FROM update_candidates), COALESCE((SELECT SUM(LENGTH(display_name) + LENGTH(stable_key)) FROM canonical_products), 0) + COALESCE((SELECT SUM(LENGTH(provider) + LENGTH(provider_identity) + COALESCE(LENGTH(version), 0) + LENGTH(scope) + LENGTH(architecture)) FROM installation_instances), 0) + COALESCE((SELECT SUM(LENGTH(provider) + LENGTH(record_key) + LENGTH(payload)) FROM raw_provider_records), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(started_at) + COALESCE(LENGTH(completed_at), 0) + LENGTH(status) + LENGTH(correlation_id)) FROM scan_runs), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(scan_run_id) + LENGTH(provider) + LENGTH(duration_ms) + COALESCE(LENGTH(warning), 0)) FROM scan_provider_timings), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(product_id) + LENGTH(provider) + LENGTH(package_id) + COALESCE(LENGTH(version), 0)) FROM update_candidates), 0), (SELECT value FROM app_metadata WHERE key = 'scan_data_last_cleared');";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        string? lastCleared = reader.IsDBNull(2) ? null : reader.GetString(2);
        return new ScanDataSummary(reader.GetInt64(1), DateTimeOffset.TryParse(lastCleared, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null, reader.GetInt32(0));
    }

    public async Task<ClearScanDataResult> ClearScanDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
            await using SqliteTransaction transaction = connection.BeginTransaction();
            int deleted = 0;
            foreach (string table in new[] { "raw_provider_records", "scan_provider_timings", "update_candidates", "scan_runs", "installation_instances", "canonical_products" }) deleted += await ExecuteCountAsync(connection, $"DELETE FROM {table};", transaction, cancellationToken);
            await ExecuteAsync(connection, "DELETE FROM app_metadata WHERE key = 'scan_last_completed';", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO app_metadata(key, value) VALUES ('scan_data_last_cleared', $cleared) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$cleared", DateTimeOffset.UtcNow));
            await transaction.CommitAsync(cancellationToken);
            return new ClearScanDataResult(true, deleted, 0, deleted == 0 ? "No scan data was stored." : "Scan data was cleared.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return new ClearScanDataResult(false, 0, -1, $"Scan data could not be cleared: {exception.Message}");
        }
    }

    public async Task SaveUpdateJobAsync(UpdateJobRecord job, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO update_jobs VALUES ($id,$provider,$source,$package,$created);", cancellationToken, null, ("$id", job.Id), ("$provider", Text(job.Provider)), ("$source", Text(job.SourceId)), ("$package", Text(job.PackageId)), ("$created", job.CreatedAt));
    }

    public async Task SaveDownloadedArtifactAsync(DownloadedArtifact artifact, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO downloaded_artifacts VALUES ($id,$category,$path,$size,$created);", cancellationToken, null, ("$id", artifact.Id), ("$category", Text(artifact.Category)), ("$path", Text(artifact.RelativePath)), ("$size", artifact.SizeBytes), ("$created", artifact.CreatedAt));
    }

    public async Task SaveManualMappingAsync(ManualMapping mapping, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO manual_mappings VALUES ($id,$key,$source,$package,$created);", cancellationToken, null, ("$id", mapping.Id), ("$key", Text(mapping.StableProductKey)), ("$source", Text(mapping.SourceId)), ("$package", Text(mapping.PackageId)), ("$created", mapping.CreatedAt));
    }

    public async Task<(int UpdateJobs, int DownloadedArtifacts, int StableDecisions, int ManualMappings)> GetPreservedDataCountsAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT (SELECT COUNT(*) FROM update_jobs), (SELECT COUNT(*) FROM downloaded_artifacts), (SELECT COUNT(*) FROM stable_decisions), (SELECT COUNT(*) FROM manual_mappings);"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, MaxTextLength)];
    private static string ConnectorKey(string connectorName) => "connector." + Text(connectorName).ToLowerInvariant();
    private static async Task InsertRunAsync(SqliteConnection connection, ScanRun run, IReadOnlyCollection<ScanProviderTiming> timings, SqliteTransaction transaction, CancellationToken token)
    {
        await ExecuteAsync(connection, "INSERT INTO scan_runs VALUES ($id,$started,$completed,$status,$correlation);", token, transaction, ("$id", run.Id), ("$started", run.StartedAt), ("$completed", run.CompletedAt), ("$status", Text(run.Status)), ("$correlation", run.CorrelationId));
        foreach (ScanProviderTiming timing in timings) await ExecuteAsync(connection, "INSERT INTO scan_provider_timings VALUES ($id,$scan,$provider,$duration,$warning);", token, transaction, ("$id", timing.Id), ("$scan", timing.ScanRunId), ("$provider", Text(timing.Provider)), ("$duration", (long)timing.Duration.TotalMilliseconds), ("$warning", Text(timing.Warning)));
    }
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value is DateTimeOffset timestamp ? timestamp.ToString("O", CultureInfo.InvariantCulture) : value?.ToString() is string text ? text : DBNull.Value); await command.ExecuteNonQueryAsync(token); }
    private static async Task<int> ExecuteCountAsync(SqliteConnection connection, string sql, SqliteTransaction transaction, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return await command.ExecuteNonQueryAsync(token); }
}
