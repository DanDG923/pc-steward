using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

public sealed partial class SqliteDatabase : IScanDataStore, IUserDecisionStore, IConnectorSettingsStore, IScheduledCheckSettingsStore, ISourceSnapshotStore, IUpdateJobStore, IUpdateHistoryStore, IDownloadedArtifactStore, IAdvancedDataControlStore
{
    private const int MaxTextLength = 1024;
    private readonly string connectionString;
    private readonly IReviewedHelperVersionGrammar? helperVersionGrammar;

    /// <summary>
    /// Tests only (adapter gates §B.2, §R B2-L1). Without a grammar every version-3 terminal reads as an integrity failure
    /// and none can be written. Production composition roots use the grammar overload.
    /// </summary>
    internal SqliteDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, ForeignKeys = true }.ToString();
    }

    /// <summary>Production constructor: the injected grammar validates persisted helper-reported versions on write readback and read.</summary>
    public SqliteDatabase(string databasePath, IReviewedHelperVersionGrammar helperVersionGrammar) : this(databasePath)
    {
        this.helperVersionGrammar = helperVersionGrammar ?? throw new ArgumentNullException(nameof(helperVersionGrammar));
    }

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await RefuseFutureSchemaAsync(connection, cancellationToken);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;", cancellationToken);
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY);", cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await RefuseFutureSchemaAsync(connection, cancellationToken, transaction);
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
        if (await MigrationRequiredAsync(connection, transaction, 4, cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE stable_decisions ADD COLUMN reason TEXT; ALTER TABLE stable_decisions ADD COLUMN expires_at TEXT; ALTER TABLE manual_mappings ADD COLUMN reason TEXT;", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (4);", cancellationToken, transaction);
        }
        if (await MigrationRequiredAsync(connection, transaction, 5, cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE installation_instances ADD COLUMN source_id TEXT;", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (5);", cancellationToken, transaction);
        }
        if (await MigrationRequiredAsync(connection, transaction, 6, cancellationToken))
        {
            await ExecuteAsync(connection, "CREATE TABLE approved_update_jobs (id TEXT PRIMARY KEY, correlation_id TEXT NOT NULL, stable_product_key TEXT NOT NULL, provider TEXT NOT NULL, source_id TEXT NOT NULL, package_id TEXT NOT NULL, installed_version TEXT NOT NULL, candidate_version TEXT NOT NULL, scope TEXT NOT NULL, architecture TEXT NOT NULL, channel TEXT NOT NULL, mapping_evidence_version TEXT NOT NULL, approved_at TEXT NOT NULL, digest TEXT NOT NULL);", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (6);", cancellationToken, transaction);
        }
        if (await MigrationRequiredAsync(connection, transaction, 7, cancellationToken))
        {
            await ExecuteAsync(connection, "CREATE TABLE update_job_history (event_id TEXT PRIMARY KEY, job_id TEXT NOT NULL, correlation_id TEXT NOT NULL, stable_product_key TEXT NOT NULL, provider TEXT NOT NULL, package_id TEXT NOT NULL, installed_version TEXT NOT NULL, target_version TEXT NOT NULL, occurred_at TEXT NOT NULL, status TEXT NOT NULL, exit_code INTEGER, diagnostic TEXT NOT NULL); CREATE INDEX ix_update_job_history_product_time ON update_job_history(stable_product_key, occurred_at DESC); CREATE TABLE update_job_execution_state (job_id TEXT PRIMARY KEY, correlation_id TEXT NOT NULL, stable_product_key TEXT NOT NULL, provider TEXT NOT NULL, package_id TEXT NOT NULL, installed_version TEXT NOT NULL, target_version TEXT NOT NULL, occurred_at TEXT NOT NULL, status TEXT NOT NULL, exit_code INTEGER, diagnostic TEXT NOT NULL);", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (7);", cancellationToken, transaction);
        }
        if (await MigrationRequiredAsync(connection, transaction, 8, cancellationToken))
        {
            await ExecuteAsync(connection, "ALTER TABLE downloaded_artifacts ADD COLUMN sha256 TEXT; ALTER TABLE downloaded_artifacts ADD COLUMN job_id TEXT; ALTER TABLE downloaded_artifacts ADD COLUMN outcome TEXT NOT NULL DEFAULT 'Incomplete'; ALTER TABLE downloaded_artifacts ADD COLUMN is_active INTEGER NOT NULL DEFAULT 0; CREATE INDEX ix_downloaded_artifacts_created ON downloaded_artifacts(created_at);", cancellationToken, transaction);
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version) VALUES (8);", cancellationToken, transaction);
        }
        if (await MigrationRequiredAsync(connection, transaction, 9, cancellationToken))
            await CreateReviewedOperationsSchemaAsync(connection, transaction, cancellationToken);
        if (await MigrationRequiredAsync(connection, transaction, 10, cancellationToken))
            await CreateReviewedDraftGenerationSchemaAsync(connection, transaction, cancellationToken);
        if (await MigrationRequiredAsync(connection, transaction, 11, cancellationToken))
            await CreateUpdateDispatchSchemaAsync(connection, transaction, cancellationToken);
        if (await MigrationRequiredAsync(connection, transaction, 12, cancellationToken))
            await CreateReviewedArchitectureSchemaAsync(connection, transaction, cancellationToken);
        if (await MigrationRequiredAsync(connection, transaction, 13, cancellationToken))
            await CreateDiagnosticSnapshotSchemaAsync(connection, transaction, cancellationToken);
        await ValidateReviewedOperationsSchemaAsync(connection, transaction, cancellationToken);
        await ValidateUpdateDispatchSchemaAsync(connection, transaction, cancellationToken);
        await ValidateDiagnosticSnapshotSchemaAsync(connection, transaction, cancellationToken);
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
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                if (!Guid.TryParse(reader.GetString(0), out Guid id) || id == Guid.Empty) continue;
                result.Add(new CanonicalProduct(id, reader.GetString(1), reader.GetString(2)));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidCastException or FormatException) { }
        }
        return result;
    }

    public async Task<IReadOnlyCollection<InstallationInstance>> GetLastKnownGoodInstancesAsync(CancellationToken cancellationToken)
    {
        List<InstallationInstance> result = [];
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, product_id, provider, provider_identity, version, scope, architecture, publisher, install_date, install_path, channel, classification, source_id FROM installation_instances ORDER BY provider, provider_identity;";
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                if (!Guid.TryParse(reader.GetString(0), out Guid id) || id == Guid.Empty ||
                    !Guid.TryParse(reader.GetString(1), out Guid productId) || productId == Guid.Empty) continue;
                result.Add(new InstallationInstance(id, productId, reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10), reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12)));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidCastException or FormatException) { }
        }
        return result;
    }

    public async Task<IReadOnlyCollection<RawProviderRecord>> GetRawProviderRecordsAsync(CancellationToken cancellationToken)
    {
        List<RawProviderRecord> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, scan_run_id, provider, record_key, payload FROM raw_provider_records ORDER BY provider, record_key;"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                if (!Guid.TryParse(reader.GetString(0), out Guid id) || id == Guid.Empty ||
                    !Guid.TryParse(reader.GetString(1), out Guid scanRunId) || scanRunId == Guid.Empty) continue;
                result.Add(new RawProviderRecord(id, scanRunId, reader.GetString(2), reader.GetString(3), reader.GetString(4)));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidCastException or FormatException) { }
        }
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
        return new ConnectorState(enabled && (!RequiresSourceDisclosure(connectorName) || approved), approved);
    }

    public async Task SaveConnectorStateAsync(string connectorName, ConnectorState state, CancellationToken cancellationToken)
    {
        await SaveConnectorStatesAsync(new Dictionary<string, ConnectorState> { [connectorName] = state }, cancellationToken);
    }

    public async Task SaveConnectorStatesAsync(IReadOnlyDictionary<string, ConnectorState> states, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(states);
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        foreach ((string connectorName, ConnectorState state) in states)
        {
            await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", ConnectorKey(connectorName)), ("$value", state.Enabled));
            if (RequiresSourceDisclosure(connectorName))
            {
                await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", ConnectorApprovalKey(connectorName)), ("$value", state.SourceDisclosureApproved));
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ScheduledCheckSettings> GetScheduledCheckSettingsAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        string cadenceValue = await GetMetadataAsync(connection, "schedule.cadence", cancellationToken) ?? ScheduleCadence.Manual.ToString();
        ScheduleCadence cadence = Enum.TryParse(cadenceValue, true, out ScheduleCadence parsedCadence) && Enum.IsDefined(parsedCadence) ? parsedCadence : ScheduleCadence.Manual;
        bool enabled = await GetMetadataBoolAsync(connection, "schedule.enabled", false, cancellationToken);
        bool notifications = await GetMetadataBoolAsync(connection, "schedule.notifications-enabled", false, cancellationToken);
        string? dayValue = await GetMetadataAsync(connection, "schedule.weekly-day", cancellationToken);
        DayOfWeek day = Enum.TryParse(dayValue, true, out DayOfWeek parsedDay) && Enum.IsDefined(parsedDay) ? parsedDay : DayOfWeek.Monday;
        string? timeValue = await GetMetadataAsync(connection, "schedule.local-start-time", cancellationToken);
        TimeOnly time = TimeOnly.TryParseExact(timeValue, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly parsedTime) ? parsedTime : new TimeOnly(9, 0);
        return new ScheduledCheckSettings(cadence, enabled, notifications, day, time).Normalize();
    }

    public async Task SaveScheduledCheckSettingsAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken)
    {
        ScheduledCheckSettings normalized = settings.Normalize();
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.cadence"), ("$value", normalized.Cadence));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.enabled"), ("$value", normalized.IsEnabled));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.notifications-enabled"), ("$value", normalized.NotificationsEnabled));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.weekly-day"), ("$value", normalized.WeeklyDay));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.local-start-time"), ("$value", normalized.LocalStartTime.ToString("HH:mm", CultureInfo.InvariantCulture)));
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Checks the newly migrated reset store before the desktop is allowed to show onboarding.</summary>
    public async Task<bool> IsFreshResetStoreAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM canonical_products) + (SELECT COUNT(*) FROM installation_instances) + (SELECT COUNT(*) FROM stable_decisions) + (SELECT COUNT(*) FROM scan_runs) + (SELECT COUNT(*) FROM raw_provider_records) + (SELECT COUNT(*) FROM scan_provider_timings) + (SELECT COUNT(*) FROM update_candidates) + (SELECT COUNT(*) FROM manual_mappings) + (SELECT COUNT(*) FROM update_jobs) + (SELECT COUNT(*) FROM downloaded_artifacts) + (SELECT COUNT(*) FROM source_snapshots) + (SELECT COUNT(*) FROM source_package_cache) + (SELECT COUNT(*) FROM approved_update_jobs) + (SELECT COUNT(*) FROM update_job_history) + (SELECT COUNT(*) FROM update_job_execution_state) + (SELECT COUNT(*) FROM reviewed_update_operations) + (SELECT COUNT(*) FROM update_dispatch_claims) + (SELECT COUNT(*) FROM diagnostic_snapshots) + (SELECT COUNT(*) FROM app_metadata);";
        long count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return count == 0 && (await GetScheduledCheckSettingsAsync(cancellationToken)).Equals(ScheduledCheckSettings.Manual);
    }

    public async Task<ScheduledCheckStatus> GetScheduledCheckStatusAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        string? completed = await GetMetadataAsync(connection, "schedule.last-completed-at", cancellationToken);
        DateTimeOffset? completedAt = DateTimeOffset.TryParse(completed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed : null;
        string result = await GetMetadataAsync(connection, "schedule.last-result", cancellationToken) ?? "Not scheduled";
        string? failures = await GetMetadataAsync(connection, "schedule.consecutive-failures", cancellationToken);
        int consecutiveFailures = int.TryParse(failures, CultureInfo.InvariantCulture, out int parsedFailures) && parsedFailures >= 0 ? parsedFailures : 0;
        return new ScheduledCheckStatus(completedAt, result, consecutiveFailures);
    }

    public async Task SaveScheduledCheckStatusAsync(ScheduledCheckStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);
        int failures = Math.Max(0, status.ConsecutiveFailures);
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.last-completed-at"), ("$value", status.LastCompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.last-result"), ("$value", Text(status.LastResult)));
        await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ($key,$value) ON CONFLICT(key) DO UPDATE SET value = excluded.value;", cancellationToken, transaction, ("$key", "schedule.consecutive-failures"), ("$value", failures));
        await transaction.CommitAsync(cancellationToken);
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

    public async Task PruneSourceSnapshotsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "DELETE FROM source_snapshots WHERE last_success_at < $cutoff;", cancellationToken, null, ("$cutoff", olderThan));
    }

    public async Task SaveAsync(StableDecision decision, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO stable_decisions(id,stable_product_key,kind,value,created_at,reason,expires_at) VALUES ($id,$key,$kind,$value,$created,$reason,$expires);", cancellationToken, null, ("$id", decision.Id), ("$key", Text(decision.StableProductKey)), ("$kind", Text(decision.Kind)), ("$value", Text(decision.Value)), ("$created", decision.CreatedAt), ("$reason", Text(decision.Reason)), ("$expires", decision.ExpiresAt));
    }

    public async Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        List<StableDecision> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, stable_product_key, kind, value, created_at, reason, expires_at FROM stable_decisions WHERE stable_product_key = $key ORDER BY created_at;"; command.Parameters.AddWithValue("$key", Text(stableProductKey)); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(ReadDecision(reader)); return result;
    }

    public async Task<IReadOnlyCollection<StableDecision>> GetAllAsync(CancellationToken cancellationToken)
    {
        List<StableDecision> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, stable_product_key, kind, value, created_at, reason, expires_at FROM stable_decisions ORDER BY created_at DESC;"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(ReadDecision(reader)); return result;
    }

    public async Task DeleteAsync(Guid decisionId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await ExecuteAsync(connection, "DELETE FROM stable_decisions WHERE id = $id;", cancellationToken, null, ("$id", decisionId));
    }

    public async Task<ScanDataSummary> GetScanDataSummaryAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM canonical_products) + (SELECT COUNT(*) FROM installation_instances) + (SELECT COUNT(*) FROM raw_provider_records) + (SELECT COUNT(*) FROM scan_provider_timings) + (SELECT COUNT(*) FROM scan_runs) + (SELECT COUNT(*) FROM update_candidates) + (SELECT COUNT(*) FROM diagnostic_snapshots), COALESCE((SELECT SUM(payload_bytes) FROM diagnostic_snapshots), 0) + COALESCE((SELECT SUM(LENGTH(display_name) + LENGTH(stable_key)) FROM canonical_products), 0) + COALESCE((SELECT SUM(LENGTH(provider) + LENGTH(provider_identity) + COALESCE(LENGTH(version), 0) + LENGTH(scope) + LENGTH(architecture) + COALESCE(LENGTH(publisher), 0) + COALESCE(LENGTH(install_date), 0) + COALESCE(LENGTH(install_path), 0) + COALESCE(LENGTH(channel), 0) + LENGTH(classification)) FROM installation_instances), 0) + COALESCE((SELECT SUM(LENGTH(provider) + LENGTH(record_key) + LENGTH(payload)) FROM raw_provider_records), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(started_at) + COALESCE(LENGTH(completed_at), 0) + LENGTH(status) + LENGTH(correlation_id)) FROM scan_runs), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(scan_run_id) + LENGTH(provider) + LENGTH(duration_ms) + COALESCE(LENGTH(warning), 0)) FROM scan_provider_timings), 0) + COALESCE((SELECT SUM(LENGTH(id) + LENGTH(product_id) + LENGTH(provider) + LENGTH(package_id) + COALESCE(LENGTH(version), 0)) FROM update_candidates), 0), (SELECT value FROM app_metadata WHERE key = 'scan_data_last_cleared');";
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
            // FR-DIA-04 (ADR-027): diagnostic snapshots are scan data; their records and source results go with them in the same transaction.
            foreach (string table in DiagnosticTables) deleted += await ExecuteCountAsync(connection, $"DELETE FROM {table};", transaction, cancellationToken);
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

    public Task<AdvancedDataClearResult> ClearSourceMetadataAsync(CancellationToken cancellationToken) => ClearAdvancedAsync("Source metadata", ["source_package_cache", "source_snapshots"], ["source."], cancellationToken);

    public Task<AdvancedDataClearResult> ClearUpdateHistoryAsync(CancellationToken cancellationToken) => ClearAdvancedAsync("Update history", ["update_job_history", "update_job_execution_state"], ["history."], cancellationToken);

    public Task<AdvancedDataClearResult> ClearMappingsAndPoliciesAsync(CancellationToken cancellationToken) => ClearAdvancedAsync("Mappings and policies", ["manual_mappings", "stable_decisions"], [], cancellationToken);

    private async Task<AdvancedDataClearResult> ClearAdvancedAsync(string category, IReadOnlyCollection<string> tables, IReadOnlyCollection<string> metadataPrefixes, CancellationToken cancellationToken)
    {
        try
        {
            await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction();
            int deleted = 0;
            foreach (string table in tables) deleted += await ExecuteCountAsync(connection, $"DELETE FROM {table};", transaction, cancellationToken);
            foreach (string prefix in metadataPrefixes) deleted += await ExecuteCountAsync(connection, "DELETE FROM app_metadata WHERE key LIKE $prefix;", transaction, cancellationToken, ("$prefix", prefix + "%"));
            await transaction.CommitAsync(cancellationToken);
            return new(category, true, deleted, deleted == 0 ? $"No {category.ToLowerInvariant()} was stored." : $"{category} was cleared.");
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            return new(category, false, 0, $"{category} could not be cleared: {exception.Message}");
        }
    }

    public async Task SaveApprovedJobAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!job.HasValidDigest) throw new InvalidOperationException("The update job failed its integrity check.");
        UpdateJobSelection selection = job.Selection;
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection,
            "INSERT INTO approved_update_jobs(id,correlation_id,stable_product_key,provider,source_id,package_id,installed_version,candidate_version,scope,architecture,channel,mapping_evidence_version,approved_at,digest) VALUES ($id,$correlation,$stable,$provider,$source,$package,$installed,$candidate,$scope,$architecture,$channel,$mapping,$approved,$digest);",
            cancellationToken,
            null,
            ("$id", job.Id),
            ("$correlation", job.CorrelationId),
            ("$stable", selection.StableProductKey),
            ("$provider", selection.Provider),
            ("$source", selection.Source),
            ("$package", selection.PackageId),
            ("$installed", selection.InstalledVersion),
            ("$candidate", selection.CandidateVersion),
            ("$scope", selection.Scope),
            ("$architecture", selection.Architecture),
            ("$channel", selection.Channel),
            ("$mapping", selection.MappingEvidenceVersion),
            ("$approved", job.ApprovedAt),
            ("$digest", job.Digest));
    }

    public async Task<ImmutableUpdateJob?> GetApprovedJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty) throw new ArgumentException("A job ID is required.", nameof(jobId));
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id,correlation_id,stable_product_key,provider,source_id,package_id,installed_version,candidate_version,scope,architecture,channel,mapping_evidence_version,approved_at,digest FROM approved_update_jobs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", jobId.ToString("D", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        try
        {
            UpdateJobSelection selection = new(
                reader.GetString(2),
                Enum.Parse<UpdateProvider>(reader.GetString(3), ignoreCase: false),
                Enum.Parse<UpdateSource>(reader.GetString(4), ignoreCase: false),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                Enum.Parse<UpdateScope>(reader.GetString(8), ignoreCase: false),
                Enum.Parse<UpdateArchitecture>(reader.GetString(9), ignoreCase: false),
                Enum.Parse<UpdateChannel>(reader.GetString(10), ignoreCase: false),
                reader.GetString(11));
            return ImmutableUpdateJob.Restore(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                selection,
                DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.GetString(13));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new InvalidOperationException("The persisted update job is invalid or has been modified.", exception);
        }
    }

    public async Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.EventId == Guid.Empty || entry.JobId == Guid.Empty || entry.CorrelationId == Guid.Empty) throw new ArgumentException("Update history needs event, job and correlation IDs.", nameof(entry));
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, "INSERT INTO update_job_history(event_id,job_id,correlation_id,stable_product_key,provider,package_id,installed_version,target_version,occurred_at,status,exit_code,diagnostic) VALUES ($event,$job,$correlation,$stable,$provider,$package,$installed,$target,$occurred,$status,$exit,$diagnostic);", cancellationToken, transaction, ("$event", entry.EventId), ("$job", entry.JobId), ("$correlation", entry.CorrelationId), ("$stable", Text(entry.StableProductKey)), ("$provider", entry.Provider), ("$package", Text(entry.PackageId)), ("$installed", Text(entry.InstalledVersion)), ("$target", Text(entry.TargetVersion)), ("$occurred", entry.OccurredAt), ("$status", entry.Status), ("$exit", entry.ExitCode), ("$diagnostic", DiagnosticText(entry.Diagnostic)));
        await ExecuteAsync(connection, "INSERT INTO update_job_execution_state(job_id,correlation_id,stable_product_key,provider,package_id,installed_version,target_version,occurred_at,status,exit_code,diagnostic) VALUES ($job,$correlation,$stable,$provider,$package,$installed,$target,$occurred,$status,$exit,$diagnostic) ON CONFLICT(job_id) DO UPDATE SET correlation_id=excluded.correlation_id,stable_product_key=excluded.stable_product_key,provider=excluded.provider,package_id=excluded.package_id,installed_version=excluded.installed_version,target_version=excluded.target_version,occurred_at=excluded.occurred_at,status=excluded.status,exit_code=excluded.exit_code,diagnostic=excluded.diagnostic;", cancellationToken, transaction, ("$job", entry.JobId), ("$correlation", entry.CorrelationId), ("$stable", Text(entry.StableProductKey)), ("$provider", entry.Provider), ("$package", Text(entry.PackageId)), ("$installed", Text(entry.InstalledVersion)), ("$target", Text(entry.TargetVersion)), ("$occurred", entry.OccurredAt), ("$status", entry.Status), ("$exit", entry.ExitCode), ("$diagnostic", DiagnosticText(entry.Diagnostic)));
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stableProductKey)) throw new ArgumentException("A stable product key is required.", nameof(stableProductKey));
        List<UpdateHistoryEntry> entries = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT event_id,job_id,correlation_id,stable_product_key,provider,package_id,installed_version,target_version,occurred_at,status,exit_code,diagnostic FROM update_job_history WHERE stable_product_key = $stable ORDER BY occurred_at DESC,event_id DESC;"; command.Parameters.AddWithValue("$stable", Text(stableProductKey)); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) entries.Add(new UpdateHistoryEntry(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), Guid.Parse(reader.GetString(2)), reader.GetString(3), Enum.Parse<UpdateProvider>(reader.GetString(4), false), reader.GetString(5), reader.GetString(6), reader.GetString(7), DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), Enum.Parse<UpdateHistoryStatus>(reader.GetString(9), false), reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.GetString(11)));
        return entries;
    }

    public async Task RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteTransaction transaction = connection.BeginTransaction();
        List<UpdateHistoryEntry> interrupted = []; await using (SqliteCommand command = connection.CreateCommand()) { command.Transaction = transaction; command.CommandText = "SELECT job_id,correlation_id,stable_product_key,provider,package_id,installed_version,target_version FROM update_job_execution_state WHERE status IN ('Running','VerificationRequired');"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) interrupted.Add(new UpdateHistoryEntry(Guid.NewGuid(), Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2), Enum.Parse<UpdateProvider>(reader.GetString(3), false), reader.GetString(4), reader.GetString(5), reader.GetString(6), DateTimeOffset.UtcNow, UpdateHistoryStatus.VerificationRequired, null, "recovered-after-interrupted-started-update; verification-required")); }
        foreach (UpdateHistoryEntry entry in interrupted)
        {
            await ExecuteAsync(connection, "INSERT INTO update_job_history(event_id,job_id,correlation_id,stable_product_key,provider,package_id,installed_version,target_version,occurred_at,status,exit_code,diagnostic) VALUES ($event,$job,$correlation,$stable,$provider,$package,$installed,$target,$occurred,$status,NULL,$diagnostic);", cancellationToken, transaction, ("$event", entry.EventId), ("$job", entry.JobId), ("$correlation", entry.CorrelationId), ("$stable", Text(entry.StableProductKey)), ("$provider", entry.Provider), ("$package", Text(entry.PackageId)), ("$installed", Text(entry.InstalledVersion)), ("$target", Text(entry.TargetVersion)), ("$occurred", entry.OccurredAt), ("$status", entry.Status), ("$diagnostic", entry.Diagnostic));
            await ExecuteAsync(connection, "UPDATE update_job_execution_state SET occurred_at=$occurred,status=$status,exit_code=NULL,diagnostic=$diagnostic WHERE job_id=$job;", cancellationToken, transaction, ("$occurred", entry.OccurredAt), ("$status", entry.Status), ("$diagnostic", entry.Diagnostic), ("$job", entry.JobId));
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retention); int maximum = Math.Clamp(retention.MaximumEventsPerJob, 1, 1024);
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "DELETE FROM update_job_history WHERE status IN ('Queued','Running','RunningApplicationWarning') AND occurred_at < $before; DELETE FROM update_job_history WHERE event_id IN (SELECT event_id FROM (SELECT event_id, ROW_NUMBER() OVER (PARTITION BY job_id ORDER BY occurred_at DESC,event_id DESC) AS row_number FROM update_job_history) WHERE row_number > $maximum);", cancellationToken, null, ("$before", retention.RemoveVerboseBefore), ("$maximum", maximum));
    }

    public async Task SaveDownloadedArtifactAsync(DownloadedArtifact artifact, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO downloaded_artifacts(id,category,relative_path,size_bytes,created_at,sha256,job_id,outcome,is_active) VALUES ($id,$category,$path,$size,$created,$hash,$job,$outcome,$active) ON CONFLICT(id) DO UPDATE SET category=excluded.category,relative_path=excluded.relative_path,size_bytes=excluded.size_bytes,created_at=excluded.created_at,sha256=excluded.sha256,job_id=excluded.job_id,outcome=excluded.outcome,is_active=excluded.is_active;", cancellationToken, null, ("$id", artifact.Id), ("$category", Text(artifact.Category)), ("$path", Text(artifact.RelativePath)), ("$size", artifact.SizeBytes), ("$created", artifact.CreatedAt), ("$hash", Text(artifact.Sha256)), ("$job", artifact.JobId?.ToString()), ("$outcome", artifact.Outcome), ("$active", artifact.IsActive ? 1 : 0));
    }

    public async Task<IReadOnlyCollection<DownloadedArtifact>> GetDownloadedArtifactsAsync(CancellationToken cancellationToken)
    {
        List<DownloadedArtifact> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id,category,relative_path,size_bytes,created_at,sha256,job_id,outcome,is_active FROM downloaded_artifacts ORDER BY created_at;"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new DownloadedArtifact(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)), Enum.Parse<DownloadedArtifactOutcome>(reader.GetString(7), false), reader.GetInt64(8) != 0));
        return result;
    }

    public async Task DeleteDownloadedArtifactAsync(Guid artifactId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await ExecuteAsync(connection, "DELETE FROM downloaded_artifacts WHERE id=$id;", cancellationToken, null, ("$id", artifactId));
    }

    public async Task<DateTimeOffset?> GetDownloadedArtifactsLastCleanedAtAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key='downloads_last_cleaned';"; object? value = await command.ExecuteScalarAsync(cancellationToken); return value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset result) ? result : null;
    }

    public async Task SetDownloadedArtifactsLastCleanedAtAsync(DateTimeOffset cleanedAt, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await ExecuteAsync(connection, "INSERT INTO app_metadata(key,value) VALUES ('downloads_last_cleaned',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;", cancellationToken, null, ("$value", cleanedAt));
    }

    public async Task SaveManualMappingAsync(ManualMapping mapping, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO manual_mappings(id,stable_product_key,source_id,package_id,created_at,reason) VALUES ($id,$key,$source,$package,$created,$reason);", cancellationToken, null, ("$id", mapping.Id), ("$key", Text(mapping.StableProductKey)), ("$source", Text(mapping.SourceId)), ("$package", Text(mapping.PackageId)), ("$created", mapping.CreatedAt), ("$reason", Text(mapping.Reason)));
    }

    public async Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken)
    {
        List<ManualMapping> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id,stable_product_key,source_id,package_id,created_at,reason FROM manual_mappings ORDER BY created_at DESC;"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(new ManualMapping(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(5) ? null : reader.GetString(5))); return result;
    }

    public async Task DeleteManualMappingAsync(Guid mappingId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await ExecuteAsync(connection, "DELETE FROM manual_mappings WHERE id = $id;", cancellationToken, null, ("$id", mappingId));
    }

    public async Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<StableDecision> decisions = await GetForProductAsync(stableProductKey, cancellationToken);
        IReadOnlyCollection<UpdateHistoryEntry> updates = await GetHistoryForProductAsync(stableProductKey, cancellationToken);
        return decisions.Select(decision => new ProductTimelineEntry(decision.CreatedAt, decision.Kind, $"{decision.Kind}: {decision.Value}", decision.Reason))
            .Concat(updates.Select(update => new ProductTimelineEntry(update.OccurredAt, "Update", $"Update {update.Status}: {update.TargetVersion}", update.Diagnostic, update.ExitCode)))
            .OrderByDescending(entry => entry.OccurredAt)
            .ToArray();
    }

    public async Task<(int UpdateJobs, int DownloadedArtifacts, int StableDecisions, int ManualMappings)> GetPreservedDataCountsAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT ((SELECT COUNT(*) FROM update_jobs) + (SELECT COUNT(*) FROM approved_update_jobs)), (SELECT COUNT(*) FROM downloaded_artifacts), (SELECT COUNT(*) FROM stable_decisions), (SELECT COUNT(*) FROM manual_mappings);"; await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken); return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, MaxTextLength)];
    private static string DiagnosticText(string? value) => SanitizedDiagnosticBundle.Sanitize(value)[..Math.Min(SanitizedDiagnosticBundle.Sanitize(value).Length, 4096)];
    private static StableDecision ReadDecision(SqliteDataReader reader) => new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    private static string SourceId(string value) => value.Trim().ToLowerInvariant() switch { "winget" => "winget", "msstore" => "msstore", "chocolatey-community" => "chocolatey-community", _ => throw new ArgumentOutOfRangeException(nameof(value), "Only supported connector sources may be cached.") };
    private static string ConnectorKey(string connectorName) => "connector." + Text(connectorName).ToLowerInvariant();
    private static string ConnectorApprovalKey(string connectorName) => ConnectorKey(connectorName) + ".source-disclosure-approved";
    private static bool RequiresSourceDisclosure(string connectorName) => connectorName is "WinGet installed inventory" or "WinGet package sources";
    private static async Task<bool> GetMetadataBoolAsync(SqliteConnection connection, string key, bool defaultValue, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key = $key;"; command.Parameters.AddWithValue("$key", key); object? value = await command.ExecuteScalarAsync(token); return value is string text && bool.TryParse(text, out bool parsed) ? parsed : defaultValue; }
    private static async Task<string?> GetMetadataAsync(SqliteConnection connection, string key, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT value FROM app_metadata WHERE key = $key;"; command.Parameters.AddWithValue("$key", key); return await command.ExecuteScalarAsync(token) as string; }
    private static async Task<bool> MigrationRequiredAsync(SqliteConnection connection, SqliteTransaction transaction, int version, CancellationToken token) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = $version;"; command.Parameters.AddWithValue("$version", version); return Convert.ToInt32(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture) == 0; }
    private static async Task InsertInstanceAsync(SqliteConnection connection, InstallationInstance instance, SqliteTransaction transaction, CancellationToken token) => await ExecuteAsync(connection, "INSERT INTO installation_instances(id,product_id,provider,provider_identity,version,scope,architecture,publisher,install_date,install_path,channel,classification,source_id) VALUES ($id,$product,$provider,$identity,$version,$scope,$architecture,$publisher,$installDate,$installPath,$channel,$classification,$source);", token, transaction, ("$id", instance.Id), ("$product", instance.ProductId), ("$provider", Text(instance.Provider)), ("$identity", Text(instance.ProviderIdentity)), ("$version", Text(instance.Version)), ("$scope", Text(instance.Scope)), ("$architecture", Text(instance.Architecture)), ("$publisher", Text(instance.Publisher)), ("$installDate", Text(instance.InstallDate)), ("$installPath", Text(instance.InstallPath)), ("$channel", Text(instance.Channel)), ("$classification", Text(instance.Classification)), ("$source", Text(instance.SourceId)));
    private static async Task InsertRunAsync(SqliteConnection connection, ScanRun run, IReadOnlyCollection<ScanProviderTiming> timings, SqliteTransaction transaction, CancellationToken token)
    {
        await ExecuteAsync(connection, "INSERT INTO scan_runs VALUES ($id,$started,$completed,$status,$correlation);", token, transaction, ("$id", run.Id), ("$started", run.StartedAt), ("$completed", run.CompletedAt), ("$status", Text(run.Status)), ("$correlation", run.CorrelationId));
        foreach (ScanProviderTiming timing in timings) await ExecuteAsync(connection, "INSERT INTO scan_provider_timings VALUES ($id,$scan,$provider,$duration,$warning);", token, transaction, ("$id", timing.Id), ("$scan", timing.ScanRunId), ("$provider", Text(timing.Provider)), ("$duration", (long)timing.Duration.TotalMilliseconds), ("$warning", Text(timing.Warning)));
    }
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value is DateTimeOffset timestamp ? timestamp.ToString("O", CultureInfo.InvariantCulture) : value?.ToString() is string text ? text : DBNull.Value); await command.ExecuteNonQueryAsync(token); }
    private static async Task<int> ExecuteCountAsync(SqliteConnection connection, string sql, SqliteTransaction transaction, CancellationToken token, params (string Name, object? Value)[] parameters) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value); return await command.ExecuteNonQueryAsync(token); }
}
