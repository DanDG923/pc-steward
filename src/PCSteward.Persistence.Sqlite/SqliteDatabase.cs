using System.Globalization;
using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Persistence.Sqlite;

public sealed class SqliteDatabase : IInventorySnapshotStore, IStableDecisionStore
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
        await ExecuteAsync(connection, "CREATE TABLE IF NOT EXISTS canonical_products (id TEXT PRIMARY KEY, stable_key TEXT NOT NULL UNIQUE, display_name TEXT NOT NULL); CREATE TABLE IF NOT EXISTS installation_instances (id TEXT PRIMARY KEY, product_id TEXT NOT NULL, provider TEXT NOT NULL, provider_identity TEXT NOT NULL, version TEXT, scope TEXT NOT NULL, architecture TEXT NOT NULL, FOREIGN KEY(product_id) REFERENCES canonical_products(id)); CREATE TABLE IF NOT EXISTS stable_decisions (id TEXT PRIMARY KEY, stable_product_key TEXT NOT NULL, kind TEXT NOT NULL, value TEXT NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS scan_runs (id TEXT PRIMARY KEY, started_at TEXT NOT NULL, completed_at TEXT, status TEXT NOT NULL, correlation_id TEXT NOT NULL); CREATE TABLE IF NOT EXISTS raw_provider_records (id TEXT PRIMARY KEY, scan_run_id TEXT NOT NULL, provider TEXT NOT NULL, record_key TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS scan_provider_timings (id TEXT PRIMARY KEY, scan_run_id TEXT NOT NULL, provider TEXT NOT NULL, duration_ms INTEGER NOT NULL, warning TEXT); CREATE TABLE IF NOT EXISTS manual_mappings (id TEXT PRIMARY KEY, stable_product_key TEXT NOT NULL, source_id TEXT NOT NULL, package_id TEXT NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS update_jobs (id TEXT PRIMARY KEY, provider TEXT NOT NULL, source_id TEXT NOT NULL, package_id TEXT NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS downloaded_artifacts (id TEXT PRIMARY KEY, category TEXT NOT NULL, relative_path TEXT NOT NULL, size_bytes INTEGER NOT NULL, created_at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS app_metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);", cancellationToken, transaction);
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

    public async Task SaveAsync(StableDecision decision, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "INSERT INTO stable_decisions VALUES ($id,$key,$kind,$value,$created);", cancellationToken, null, ("$id", decision.Id), ("$key", Text(decision.StableProductKey)), ("$kind", Text(decision.Kind)), ("$value", Text(decision.Value)), ("$created", decision.CreatedAt));
    }

    public async Task<IReadOnlyCollection<StableDecision>> GetForProductAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        List<StableDecision> result = []; await using SqliteConnection connection = new(connectionString); await connection.OpenAsync(cancellationToken); await using SqliteCommand command = connection.CreateCommand(); command.CommandText = "SELECT id, stable_product_key, kind, value, created_at FROM stable_decisions WHERE stable_product_key = $key;"; command.Parameters.AddWithValue("$key", Text(stableProductKey)); await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken); while (await reader.ReadAsync(cancellationToken)) result.Add(new StableDecision(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3), DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))); return result;
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, MaxTextLength)];
    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken token, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters) { await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql; foreach ((string name, object? value) in parameters) command.Parameters.AddWithValue(name, value is DateTimeOffset timestamp ? timestamp.ToString("O", CultureInfo.InvariantCulture) : value?.ToString() is string text ? text : DBNull.Value); await command.ExecuteNonQueryAsync(token); }
}
