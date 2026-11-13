using Microsoft.Data.Sqlite;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-DIA-01–04 (ADR-015/027, Task 64 stage 2): whole immutable snapshots in SQLite with synthetic data in isolated temporary
/// databases. Covers round trip and frozen replay, 30-day whole-snapshot expiry, 64 MiB oldest-first pruning with protected
/// snapshots, Clear scan data removing snapshots, Clear update history preserving captured copies, strict reads of tampered rows,
/// invalid snapshots refused, and the bounded PC Steward history source.
/// </summary>
[TestClass]
public sealed class DiagnosticSnapshotStoreTests
{
    private static readonly DateTimeOffset Collected = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task ASavedSnapshotReplaysToTheSameTimeline()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DiagnosticSnapshot snapshot = Snapshot(Collected, ("app-1", -30), ("app-2", -5));

        DiagnosticSnapshotSaveResult saved = await fixture.Store.SaveDiagnosticSnapshotAsync(snapshot, Collected, [], CancellationToken.None);
        DiagnosticSnapshotLoad load = await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, Collected.AddDays(1), CancellationToken.None);
        IReadOnlyList<DiagnosticSnapshotSummary> list = await fixture.Store.ListDiagnosticSnapshotsAsync(Collected.AddDays(1), CancellationToken.None);

        Assert.IsTrue(saved.Saved, saved.Failure.ToString());
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Loaded, load.Status);
        CollectionAssert.AreEqual(snapshot.Records.ToArray(), load.Snapshot!.Records.ToArray());
        CollectionAssert.AreEqual(snapshot.Sources.ToArray(), load.Snapshot.Sources.ToArray());
        Assert.AreEqual(snapshot.ExpiresAtUtc, load.Snapshot.ExpiresAtUtc);
        Assert.AreEqual(snapshot.PayloadBytes, load.Snapshot.PayloadBytes);
        DiagnosticTimeline original = DiagnosticTimelineBuilder.Build(snapshot), replayed = DiagnosticTimelineBuilder.Build(load.Snapshot);
        CollectionAssert.AreEqual(original.Timed.ToArray(), replayed.Timed.ToArray());
        Assert.AreEqual(snapshot.Id, list.Single().Id);
        Assert.AreEqual(2, list.Single().RecordCount);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task WholeSnapshotsExpireThirtyDaysAfterCollectionAndViewingDoesNotRefreshThem()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DiagnosticSnapshot snapshot = Snapshot(Collected, ("app-1", -1));
        await fixture.Store.SaveDiagnosticSnapshotAsync(snapshot, Collected, [], CancellationToken.None);
        await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, Collected.AddDays(29), CancellationToken.None);

        DateTimeOffset expiry = Collected.AddDays(30);
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Loaded, (await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, expiry.AddTicks(-10), CancellationToken.None)).Status);
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Unavailable, (await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, expiry, CancellationToken.None)).Status, "expired before deletion still never replays");
        Assert.IsEmpty(await fixture.Store.ListDiagnosticSnapshotsAsync(expiry, CancellationToken.None));
        Assert.AreEqual(1, await fixture.Store.DeleteExpiredDiagnosticSnapshotsAsync(expiry, CancellationToken.None));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT (SELECT COUNT(*) FROM diagnostic_snapshots) + (SELECT COUNT(*) FROM diagnostic_records) + (SELECT COUNT(*) FROM diagnostic_source_results);"));
        Assert.AreEqual(DiagnosticSnapshotSaveFailure.AlreadyExpired, (await fixture.Store.SaveDiagnosticSnapshotAsync(Snapshot(Collected, ("x", -1)), expiry, [], CancellationToken.None)).Failure);
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task CapacityPrunesOldestUnprotectedSnapshotsFirstAndNeverExceedsTheCap()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DiagnosticSnapshot oldest = Snapshot(Collected.AddDays(-2), ("a", -1));
        DiagnosticSnapshot middle = Snapshot(Collected.AddDays(-1), ("b", -1));
        await fixture.Store.SaveDiagnosticSnapshotAsync(oldest, Collected, [], CancellationToken.None);
        await fixture.Store.SaveDiagnosticSnapshotAsync(middle, Collected, [], CancellationToken.None);
        const long FortyMiB = 40L * 1024 * 1024;
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE diagnostic_snapshots SET payload_bytes = $bytes;", ("$bytes", FortyMiB));

        DiagnosticSnapshotSaveResult protectedOldest = await fixture.Store.SaveDiagnosticSnapshotAsync(Snapshot(Collected, ("c", -1)), Collected, [oldest.Id], CancellationToken.None);

        Assert.IsTrue(protectedOldest.Saved);
        Assert.AreEqual(1, protectedOldest.PrunedForCapacity, "pruning is disclosed because it shortens retention");
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Unavailable, (await fixture.Store.LoadDiagnosticSnapshotAsync(middle.Id, Collected, CancellationToken.None)).Status);
        Assert.AreEqual(1L, await fixture.ScalarAsync("SELECT COUNT(*) FROM diagnostic_snapshots WHERE id = $id;", ("$id", oldest.Id.ToString("D"))));

        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE diagnostic_snapshots SET payload_bytes = $bytes;", ("$bytes", FortyMiB));
        long before = (long)(await fixture.ScalarAsync("SELECT COUNT(*) FROM diagnostic_snapshots;"))!;
        string[] ids = [.. (await fixture.Store.ListDiagnosticSnapshotsAsync(Collected, CancellationToken.None)).Select(summary => summary.Id.ToString("D"))];
        DiagnosticSnapshotSaveResult blocked = await fixture.Store.SaveDiagnosticSnapshotAsync(Snapshot(Collected.AddMinutes(1), ("d", -1)), Collected.AddMinutes(1), [.. ids.Select(Guid.Parse)], CancellationToken.None);

        Assert.IsFalse(blocked.Saved);
        Assert.AreEqual(DiagnosticSnapshotSaveFailure.CapacityHeldByProtectedSnapshots, blocked.Failure);
        Assert.AreEqual(before, (long)(await fixture.ScalarAsync("SELECT COUNT(*) FROM diagnostic_snapshots;"))!, "a refused save leaves every earlier snapshot as it was");
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task EverySnapshotIsChargedItsEnvelopeEvenWhenItHasNoRecords()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DiagnosticSnapshot first = Snapshot(Collected.AddDays(-1));
        DiagnosticSnapshot second = Snapshot(Collected);
        Assert.AreEqual(DiagnosticCollectionPolicy.ReservedEnvelopeBytes, first.RetainedBytes, "an empty snapshot still uses its envelope");
        await fixture.Store.SaveDiagnosticSnapshotAsync(first, Collected, [], CancellationToken.None);
        // Leave room for exactly one more envelope minus one byte, so the record bytes alone would still fit.
        long almostFull = DiagnosticCollectionPolicy.MaximumRetainedBytes - (2 * DiagnosticCollectionPolicy.ReservedEnvelopeBytes) + 1;
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; UPDATE diagnostic_snapshots SET payload_bytes = $bytes;", ("$bytes", almostFull));

        DiagnosticSnapshotSaveResult saved = await fixture.Store.SaveDiagnosticSnapshotAsync(second, Collected, [], CancellationToken.None);

        Assert.IsTrue(saved.Saved);
        Assert.AreEqual(1, saved.PrunedForCapacity, "the second envelope takes the total past 64 MiB");
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Unavailable, (await fixture.Store.LoadDiagnosticSnapshotAsync(first.Id, Collected, CancellationToken.None)).Status);
        Assert.AreEqual(DiagnosticCollectionPolicy.ReservedEnvelopeBytes, (await fixture.Store.ListDiagnosticSnapshotsAsync(Collected, CancellationToken.None)).Single().RetainedBytes);
    }

    [TestMethod]
    [TestCategory("FR-DIA-04")]
    public async Task ClearScanDataRemovesSnapshotsButClearUpdateHistoryKeepsTheCapturedCopies()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        UpdateHistoryEntry history = History(Collected.AddHours(-1), UpdateHistoryStatus.Success);
        await fixture.Store.RecordAsync(history, CancellationToken.None);
        DiagnosticCollectionOrchestrator orchestrator = new([new PcStewardHistoryDiagnosticReader(fixture.Store)], DiagnosticRedactionContext.None, new FixedClock(Collected));
        DiagnosticSnapshot snapshot = (await orchestrator.CollectAsync(new(Enum.GetValues<DiagnosticSourceKind>(), TimeSpan.FromDays(7), DiagnosticCollectionPolicy.Version), CancellationToken.None)).Snapshot!;
        await fixture.Store.SaveDiagnosticSnapshotAsync(snapshot, Collected, [], CancellationToken.None);
        int scanItemsWithSnapshot = (await fixture.Store.GetScanDataSummaryAsync(CancellationToken.None)).ItemCount;

        Assert.IsTrue((await fixture.Store.ClearUpdateHistoryAsync(CancellationToken.None)).Succeeded);
        DiagnosticSnapshotLoad afterHistoryClear = await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, Collected, CancellationToken.None);
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Loaded, afterHistoryClear.Status);
        Assert.AreEqual(history.EventId.ToString("D"), afterHistoryClear.Snapshot!.Records.Single().SourceRecordId, "the captured copy is snapshot-owned");
        Assert.AreEqual(1, scanItemsWithSnapshot);

        ClearScanDataResult cleared = await fixture.Store.ClearScanDataAsync(CancellationToken.None);
        ClearScanDataResult again = await fixture.Store.ClearScanDataAsync(CancellationToken.None);

        Assert.IsTrue(cleared.Succeeded);
        Assert.IsGreaterThanOrEqualTo(3, cleared.DeletedItems);
        Assert.AreEqual(0, again.DeletedItems, "clearing is idempotent");
        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Unavailable, (await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, Collected, CancellationToken.None)).Status);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT (SELECT COUNT(*) FROM diagnostic_snapshots) + (SELECT COUNT(*) FROM diagnostic_records) + (SELECT COUNT(*) FROM diagnostic_source_results);"));
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    [DataRow("UPDATE diagnostic_records SET payload = replace(payload, 'Contoso Widget', 'Contoso') WHERE ordinal = 0;", DisplayName = "record changed so the stored size no longer matches")]
    [DataRow("UPDATE diagnostic_records SET payload = replace(payload, '\"kind\":\"PcStewardUpdate\"', '\"kind\":\"0\"');", DisplayName = "numeric enum name")]
    [DataRow("UPDATE diagnostic_records SET payload = replace(payload, '{\"source\"', '{\"extra\":1,\"source\"');", DisplayName = "unknown property")]
    [DataRow("UPDATE diagnostic_records SET payload = replace(payload, '\"subject\":\"Contoso Widget\"', '\"subject\":\"C:\\\\Users\\\\x\"');", DisplayName = "unsanitized text")]
    [DataRow("DELETE FROM diagnostic_records WHERE ordinal = 1;", DisplayName = "missing record")]
    [DataRow("UPDATE diagnostic_source_results SET outcome = 'Healthy' WHERE source = 'SystemEventLog';", DisplayName = "unknown source outcome")]
    [DataRow("DELETE FROM diagnostic_source_results WHERE source = 'Reliability';", DisplayName = "missing source result")]
    [DataRow("UPDATE diagnostic_snapshots SET collected_at = 'not-a-time-at-all-xxxxxxxxxx';", DisplayName = "bad collection time")]
    public async Task TamperedRowsMakeTheWholeSnapshotUnreadable(string tamper)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DiagnosticSnapshot snapshot = Snapshot(Collected, ("app-1", -30), ("app-2", -5));
        await fixture.Store.SaveDiagnosticSnapshotAsync(snapshot, Collected, [], CancellationToken.None);
        await fixture.ExecuteAsync("PRAGMA ignore_check_constraints=ON; " + tamper);

        DiagnosticSnapshotLoad load = await fixture.Store.LoadDiagnosticSnapshotAsync(snapshot.Id, Collected, CancellationToken.None);

        Assert.AreEqual(DiagnosticSnapshotLoadStatus.Unreadable, load.Status);
        Assert.IsNull(load.Snapshot, "a damaged snapshot is never replayed with fewer or altered records");
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task InvalidSnapshotsAreRefusedBeforeAnythingIsWritten()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DiagnosticSnapshot valid = Snapshot(Collected, ("app-1", -1));
        DiagnosticSnapshot[] invalid =
        [
            valid with { PolicyVersion = 2 },
            valid with { Sources = [.. valid.Sources.Skip(1)] },
            valid with { ExpiresAtUtc = valid.ExpiresAtUtc.AddDays(1) },
            valid with { LookbackFromUtc = Collected.AddDays(-8) },
            valid with { Records = [valid.Records[0] with { Subject = @"C:\Users\someone\x.exe" }] },
            valid with { Records = [valid.Records[0] with { Source = DiagnosticSourceKind.SystemEventLog }] },
            valid with { Id = Guid.Empty }
        ];

        foreach (DiagnosticSnapshot snapshot in invalid)
            Assert.AreEqual(DiagnosticSnapshotSaveFailure.InvalidSnapshot, (await fixture.Store.SaveDiagnosticSnapshotAsync(snapshot, Collected, [], CancellationToken.None)).Failure);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM diagnostic_snapshots;"));
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public async Task AppHistorySourceFiltersByInstantInclusivelyNewestFirstWithItsLimit()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        DateTimeOffset from = Collected.AddDays(-7);
        UpdateHistoryEntry atStart = History(from, UpdateHistoryStatus.Success);
        UpdateHistoryEntry beforeStart = History(from.AddSeconds(-1), UpdateHistoryStatus.Success);
        UpdateHistoryEntry atEnd = History(Collected, UpdateHistoryStatus.InstallerFailed);
        UpdateHistoryEntry offsetInside = History(new DateTimeOffset(2026, 11, 13, 13, 30, 0, TimeSpan.FromHours(2)), UpdateHistoryStatus.RestartRequired);
        UpdateHistoryEntry afterEnd = History(Collected.AddSeconds(1), UpdateHistoryStatus.Running);
        foreach (UpdateHistoryEntry entry in new[] { atStart, beforeStart, atEnd, offsetInside, afterEnd }) await fixture.Store.RecordAsync(entry, CancellationToken.None);
        await fixture.Store.PromoteAsync([new CanonicalProduct(Guid.NewGuid(), atEnd.StableProductKey, "Contoso Widget")], [], CancellationToken.None);

        (IReadOnlyList<DiagnosticAppHistoryEntry> all, bool moreAll) = await fixture.Store.GetUpdateHistoryBetweenAsync(from, Collected, 500, CancellationToken.None);
        (IReadOnlyList<DiagnosticAppHistoryEntry> two, bool moreTwo) = await fixture.Store.GetUpdateHistoryBetweenAsync(from, Collected, 2, CancellationToken.None);
        DiagnosticReaderResult read = await new PcStewardHistoryDiagnosticReader(fixture.Store).ReadAsync(new(DiagnosticSourceKind.PcStewardHistory, from, Collected, 500), CancellationToken.None);

        Guid[] expected = [atEnd.EventId, offsetInside.EventId, atStart.EventId];
        CollectionAssert.AreEqual(expected, all.Select(entry => entry.Entry.EventId).ToArray(), "inclusive at both ends, ordered by instant whatever the stored offset");
        Assert.IsFalse(moreAll);
        Assert.HasCount(2, two);
        Assert.IsTrue(moreTwo);
        Assert.AreEqual("Contoso Widget", read.Records[0].Subject, "the app's display name is used when the inventory still knows it");
        Assert.AreEqual(DiagnosticOutcomeKind.Failed, read.Records[0].Outcome);
        Assert.AreEqual(DiagnosticOutcomeKind.RestartPending, read.Records[1].Outcome);
        Assert.AreEqual(DiagnosticTimeQuality.Known, read.Records[1].Occurrence.Quality);
        Assert.AreEqual(TimeSpan.FromHours(2), read.Records[1].Occurrence.OriginalOffset);
        Assert.AreEqual("1.0 to 1.1", read.Records[2].Detail);
    }

    [TestMethod]
    [TestCategory("FR-DIA-01")]
    public void EveryUpdateHistoryStatusMapsToAClosedOutcome()
    {
        foreach (UpdateHistoryStatus status in Enum.GetValues<UpdateHistoryStatus>())
            Assert.AreEqual(status == UpdateHistoryStatus.Inconclusive, PcStewardHistoryDiagnosticReader.Outcome(status) == DiagnosticOutcomeKind.Unknown, status.ToString());
        Assert.AreEqual(DiagnosticOutcomeKind.InProgress, PcStewardHistoryDiagnosticReader.Outcome(UpdateHistoryStatus.VerificationRequired));
        Assert.AreEqual(DiagnosticOutcomeKind.Failed, PcStewardHistoryDiagnosticReader.Outcome(UpdateHistoryStatus.ElevationDenied));
    }

    [TestMethod]
    [TestCategory("FR-DIA-03")]
    public async Task FreshDatabaseIsAtSchemaThirteenAndRefusesAFutureSchema()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        Assert.AreEqual(13L, await fixture.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"));
        await fixture.Store.MigrateAsync(CancellationToken.None);
        Assert.AreEqual(13L, await fixture.ScalarAsync("SELECT MAX(version) FROM schema_migrations;"), "migration is idempotent");
        await fixture.ExecuteAsync("INSERT INTO schema_migrations(version) VALUES(14);");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Store.SaveDiagnosticSnapshotAsync(Snapshot(Collected, ("x", -1)), Collected, [], CancellationToken.None));
    }

    private static UpdateHistoryEntry History(DateTimeOffset at, UpdateHistoryStatus status) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "product-key-contoso", UpdateProvider.WinGet, "Contoso.Widget", "1.0", "1.1", at, status, status == UpdateHistoryStatus.InstallerFailed ? 1603 : 0, "fixture diagnostic");

    private static DiagnosticSnapshot Snapshot(DateTimeOffset collected, params (string Id, int MinutesBefore)[] records)
    {
        DiagnosticRecord[] admitted = [.. records.Select(record => DiagnosticRecordAdmission.Admit(new DiagnosticRecordCandidate(
            DiagnosticSourceKind.PcStewardHistory, record.Id, DiagnosticTimeNormalizer.FromInstant(collected.AddMinutes(record.MinutesBefore), collected),
            DiagnosticEventKind.PcStewardUpdate, DiagnosticOutcomeKind.Succeeded, Subject: "Contoso Widget", Detail: "1.0 to 1.1")).Record!)];
        DiagnosticSourceResult[] sources = [.. Enum.GetValues<DiagnosticSourceKind>().Select(source => source == DiagnosticSourceKind.PcStewardHistory
            ? new DiagnosticSourceResult(source, DiagnosticSourceOutcome.Available, DiagnosticSourceReason.None, collected.AddDays(-7), collected, 500, admitted.Length, admitted.Length, 0)
            : new DiagnosticSourceResult(source, DiagnosticSourceOutcome.Denied, DiagnosticSourceReason.AccessDenied, collected.AddDays(-7), collected, 375, 0, 0, 0))];
        long bytes = admitted.Sum(record => DiagnosticRecordJson.ByteCount(record) + 1L);
        return new(Guid.NewGuid(), DiagnosticCollectionPolicy.Version, collected, DiagnosticCollectionPolicy.ExpiresAt(collected), collected.AddDays(-7), sources, admitted, bytes);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path;
        private Fixture(string path, SqliteDatabase store) { this.path = path; Store = store; }
        public SqliteDatabase Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            string path = Path.Combine(Path.GetTempPath(), $"pcsteward-diagnostic-{Guid.NewGuid():N}.db");
            SqliteDatabase store = new(path);
            await store.MigrateAsync(CancellationToken.None);
            return new(path, store);
        }

        public async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using SqliteConnection connection = new($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
            return await command.ExecuteScalarAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
            return ValueTask.CompletedTask;
        }
    }
}
