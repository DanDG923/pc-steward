using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

public sealed class SqliteDatabase : IScanDataStore, IStableDecisionStore, IConnectorSettingsStore, ISourceSnapshotStore
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
        if (await MigrationRequiredAsync(connection, transaction, 2, cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE installation_instances ADD COLUMN publisher TEXT; ALTER TABLE installation_instances ADD COLUMN install_date TEXT; ALTER TABLE installation_instances ADD COLUMN install_path TEXT; ALTER TABLE installation_instances ADD COLUMN channel TEXT; ALTER TABLE installation_instances ADD COLUMN classification TEXT NOT NULL DEFAULT 'Unknown';", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (2);", cancellationToken, transaction);
        }
        if (await MigrationRequiredAsync(connection, transaction, 3, cancellationToken))
        {
            await ExecuteAsync(connection, "CREATE TABLE source_snapshots (source_id TEXT PRIMARY KEY, provider_version TEXT, last_success_at TEXT NOT NULL); CREATE TABLE source_package_cache (source_id TEXT NOT NULL, package_id TEXT NOT NULL, display_name TEXT NOT NULL, publisher TEXT, installed_version TEXT, available_version TEXT, scope TEXT NOT NULL, architecture TEXT NOT NULL, channel TEXT, connector_semantics INTEGER NOT NULL, stable_product_key TEXT, PRIMARY KEY(source_id, package_id), FOREIGN KEY(source_id) REFERENCES source_snapshots(source_id) ON DELETE CASCADE);", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (3);", cancellationToken, transaction);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PromoteAsync(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "DELETE FROM installation_instances; DELETE FROM canonical_products;", cancellationToken, transaction);
        foreach (CanonicalProduct product in products) await ExecuteAsync(connection, "INSERT INTO canonical_products VALUES ($id,$key,$name);", cancellationToken, transaction, ("$id", product.Id), ("$key", Text(product.StableKey)), ("$name", Text(product.DisplayName)));
        foreach (InstallationInstance instance in instances) await InsertInstanceAsync(connection, instance, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PersistScanAsync(ScanRun run, IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, IReadOnlyCollection<RawProviderRecord> rawRecords, IReadOnlyCollection<ScanProviderTiming> timings, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "DELETE FROM raw_provider_records; DELETE FROM scan_provider_timings; DELETE FROM installation_instances; DELETE FROM canonical_products;", cancellationToken, transaction);
        foreach (CanonicalProduct product in products) await ExecuteAsync(connection, "INSERT INTO canonical_products VALUES ($id,$key,$name);", cancellationToken, transaction, ("$id", product.Id), ("$key", Text(product.StableKey)), ("$name", Text(product.DisplayName)));
        foreach (InstallationInstance instance in instances) await InsertInstanceAsync(connection, instance, transaction, cancellationToken);
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
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, product_id, provider, provider_identity, version, scope, architecture, publisher, install_date, install_path, channel, classification FROM installation_instances ORDER BY provider, provider_identity;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new InstallationInstance(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11)));
        return result;
    }

    public async Task<IReadOnlyCollection<RawProviderRecord>> GetRawProviderRecordsAsync(CancellationToken cancellationToken)
    {
        List<RawProviderRecord> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, scan_run_id, provider, record_key, payload FROM raw_provider_records ORDER BY provider, record_key;"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new RawProviderRecord(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return result;
    }

    public async Task<DateTimeOffset?> GetLastCompletedScanAtAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key = 'scan_last_completed';"; object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;
    }

    public async Task<bool> GetConnectorEnabledAsync(string connectorName, bool defaultValue, CancellationToken cancellationToken)
    {
        return (await GetConnectorStateAsync(connectorName, defaultValue, cancellationToken)).Enabled;
    }

    public async Task SaveConnectorEnabledAsync(string connectorName, bool enabled, CancellationToken cancellationToken)
    {
        ConnectorState existing = await GetConnectorStateAsync(connectorName, false, cancellationToken); await SaveConnectorStateAsync(connectorName, new ConnectorState(enabled, existing.SourceDisclosureApproved), cancellationToken);
    }

    public async Task<ConnectorState> GetConnectorStateAsync(string connectorName, bool defaultEnabled, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); bool enabled = await GetMetadataBoolAsync(connection, ConnectorKey(connectorName), defaultEnabled, cancellationToken); bool approved = await GetMetadataBoolAsync(connection, ConnectorApprovalKey(connectorName), false, cancellationToken);
        return new ConnectorState(enabled && (!IsWinget(connectorName) || approved), approved);
    }

    public async Task SaveConnectorStateAsync(string connectorName, ConnectorState state, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, null, ("$key", ConnectorKey(connectorName)), ("$value", state.Enabled));
        if (IsWinget(connectorName)) await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, null, ("$key", ConnectorApprovalKey(connectorName)), ("$value", state.SourceDisclosureApproved));
    }

    public async Task SaveSourceSnapshotAsync(SourceSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.State != SourceSnapshotState.Live || snapshot.LastSuccessAt is null) throw new ArgumentException("Only a successful live source snapshot may replace the cache.", nameof(snapshot));
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "INSERT INTO source_snapshots(source_id,provider_version,last_success_at) VALUES ($source,$version,$success) ON CONFLICT(source_id) DO UPDATE SET provider_version = excluded.provider_version, last_success_at = excluded.last_success_at;", cancellationToken, transaction, ("$source", SourceId(snapshot.SourceId)), ("$version", Text(snapshot.ProviderVersion)), ("$success", snapshot.LastSuccessAt.Value));
        await ExecuteAsync(connection, "DELETE FROM source_package_cache WHERE source_id = $source;", cancellationToken, transaction, ("$source", SourceId(snapshot.SourceId)));
        foreach (SourcePackageRecord package in snapshot.Packages)
        {
            if (!string.Equals(snapshot.SourceId, package.SourceId, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Cached package source does not match its snapshot.", nameof(snapshot));
            await ExecuteAsync(connection, "INSERT INTO source_package_cache(source_id,package_id,display_name,publisher,installed_version,available_version,scope,architecture,channel,connector_semantics,stable_product_key) VALUES ($source,$package,$name,$publisher,$installed,$available,$scope,$architecture,$channel,$semantics,$stable);", cancellationToken, transaction, ("$source", SourceId(package.SourceId)), ("$package", Text(package.PackageId)), ("$name", Text(package.DisplayName)), ("$publisher", Text(package.Publisher)), ("$installed", Text(package.InstalledVersion)), ("$available", Text(package.AvailableVersion)), ("$scope", Text(package.Scope)), ("$architecture", Text(package.Architecture)), ("$channel", Text(package.Channel)), ("$semantics", package.RequiresConnectorSpecificVersionSemantics), ("$stable", Text(package.ProviderSuppliedStableProductKey)));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<SourceSnapshot?> GetSourceSnapshotAsync(string sourceId, CancellationToken cancellationToken)
    {
        string source = SourceId(sourceId); await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand snapshot = connection.CreateCommand(); snapshot.CommandText = "SELECT provider_version,last_success_at FROM source_snapshots WHERE source_id = $source;"; snapshot.Parameters.AddWithValue("$source", source); await using SqliteDataReader header = await snapshot.ExecuteReaderAsync(cancellationToken); if (!await header.ReadAsync(cancellationToken)) return null;
        string? providerVersion = header.IsDBNull(0) ? null : header.GetString(0); DateTimeOffset lastSuccess = DateTimeOffset.Parse(header.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        await header.CloseAsync(); List<SourcePackageRecord> packages = []; await using SqliteCommand rows = connection.CreateCommand(); rows.CommandText = "SELECT package_id,display_name,publisher,installed_version,available_version,scope,architecture,channel,connector_semantics,stable_product_key FROM source_package_cache WHERE source_id = $source ORDER BY package_id;"; rows.Parameters.AddWithValue("$source", source); await using SqliteDataReader reader = await rows.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) packages.Add(new(source, reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8) != 0, reader.IsDBNull(9) ? null : reader.GetString(9)));
        return new(source, providerVersion, lastSuccess, SourceSnapshotState.Cached, packages, []);
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
        command.CommandText = "SELECT (SELECT COUNT(*) FROM canonical_products) + (SELECT COUNT(*) FROM installation_instances) + (SELECT COUNT(*) FROM raw_provider_records) + (SELECT COUNT(*) FROM scan_provider_timings) + (SELECT COUNT(*) FROM scan_runs) + (SELECT COUNT(*) FROM update_candidates), COALESCE((SELECT SUM(LENGTH(display_name) + LENGTH(stable_key)) FROM canonical_products), 0) + COALESCE((SELECT SUM(LENGTH(provider) + LENGTH(provider_identity) + COALESCE(LENGTH(version), 0) + LENGTH(scope) + LENGTH(architecture) + COALESCE(LENGTH(publisher), 0) + COALESCE(LENGTH(install_date), 0) + COALESCE(LENGTH(install_path), 0) + COALESCE(LENGTH(channel), 0) + LENGTH(classification)) FROM installation_instances), 0) + COALESCE((SELECT SUM(LENGTH(provider) + LENGTH(record_key) + LENGTH(payload)) FROM raw_provider_records), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(started_at) + COALESCE(LENGTH(completed_at), 0) + LENGTH(status) + LENGTH(correlation_id)) FROM scan_runs), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(scan_run_id) + LENGTH(provider) + LENGTH(duration_ms) + COALESCE(LENGTH(warning), 0)) FROM scan_provider_timings), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(product_id) + LENGTH(provider) + LENGTH(package_id) + COALESCE(LENGTH(version), 0)) FROM update_candidates), 0), (SELECT value FROM app_metadata WHERE key = 'scan_data_last_cleared');";
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
    private static string SourceId(string value) => value.Trim().ToLowerInvariant() switch { "winget" => "winget", "msstore" => "msstore", _ => throw new ArgumentOutOfRangeException(nameof(value), "Only supported connector sources may be cached.") };
    private static string ConnectorKey(string connectorName) => "connector." + Text(connectorName).ToLowerInvariant();
    private static string ConnectorApprovalKey(string connectorName) => ConnectorKey(connectorName) + ".source-disclosure-approved";
    private static bool IsWinget(string connectorName) => string.Equals(connectorName, "WinGet installed inventory", StringComparison.Ordinal);
    private static async Task<bool> GetMetadataBoolAsync(SqliteConnection connection, string key, bool defaultValue, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key = $key;"; command.Parameters.AddWithValue("$key", key); object? value = await command.ExecuteScalarAsync(token); return value is string text && bool.TryParse(text, out bool parsed) ? parsed : defaultValue; }
    private static async Task<bool> MigrationRequiredAsync(SqliteConnection connection, SqliteTransaction transaction, int version, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;"; command.Parameters.AddWithValue("$version", version); return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 0; }
    private static async Task InsertInstanceAsync(SqliteConnection connection, InstallationInstance instance, SqliteTransaction transaction, CancellationToken token) => await ExecuteAsync(connection, "INSERT INTO installation_instances(id,product_id,provider,provider_identity,version,scope,architecture,publisher,install_date,install_path,channel,classification) VALUES ($id,$product,$provider,$identity,$version,$scope,$architecture,$publisher,$installDate,$installPath,$channel,$classification);", token, transaction, ("$id", instance.Id), ("$product", instance.ProductId), ("$provider", Text(instance.Provider)), ("$identity", Text(instance.ProviderIdentity)), ("$version", Text(instance.Version)), ("$scope", Text(instance.Scope)), ("$architecture", Text(instance.Architecture)), ("$publisher", Text(instance.Publisher)), ("$installDate", Text(instance.InstallDate)), ("$installPath", Text(instance.InstallPath)), ("$channel", Text(instance.Channel)), ("$classification", Text(instance.Classification)));
    private static async Task InsertRunAsync(SqliteConnection connection, ScanRun run, IReadOnlyCollection<ScanProviderTiming> timings, SqliteTransaction transaction, CancellationToken token)
    {
        await ExecuteAsync(connection, "INSERT INTO scan_runs VALUES ($id,$started,$completed,$status,$correlation);", token, transaction, ("$id", run.Id), ("$started", run.StartedAt), ("$completed", run.CompletedAt), ("$status", Text(run.Status)), ("$correlation", run.CorrelationId));
        foreach (ScanProviderTiming timing in timings) await ExecuteAsync(connection, "INSERT INTO scan_provider_timings VALUES ($id,$scan,$provider,$duration,$warning);", token, transaction, ("$id", timing.Id), ("$scan", timing.ScanRunId), ("$provider", Text(timing.Provider)), ("$duration", (long)timing.Duration.TotalMilliseconds), ("$warning", Text(timing.Warning)));
    }
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value is DateTimeOffset timestamp ? timestamp.ToString("O", CultureInfo.InvariantCulture) : value?.ToString() is string text ? text : DBNull.Value); await command.ExecuteNonQueryAsync(token); }
    private static async Task<int> ExecuteCountAsync(SqliteConnection connection, string sql, SqliteTransaction transaction, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; return await command.ExecuteNonQueryAsync(token); }
}
