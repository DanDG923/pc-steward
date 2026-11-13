using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class UpdateSourceRefreshServiceTests
{
    [TestMethod]
    public async Task HealthyRefreshPersistsLiveSourceAndOfflineModeUsesClearlyCachedData()
    {
        MemoryCache cache = new(); UpdateSourceRefreshService service = new(cache); FakeConnector connector = new("winget", Source("winget", SourceSnapshotState.Live));
        SourceSnapshot live = (await service.RefreshAsync([connector], true, CancellationToken.None)).Single(); SourceSnapshot cached = (await service.RefreshAsync([connector], false, CancellationToken.None)).Single();
        Assert.AreEqual(SourceSnapshotState.Live, live.State); Assert.AreEqual(SourceSnapshotState.Cached, cached.State); Assert.IsNotNull(cached.LastSuccessAt); CollectionAssert.Contains(cached.Warnings.ToArray(), "source:offline"); Assert.AreEqual(1, connector.Calls);
    }

    [TestMethod]
    public async Task TimeoutOrFailureKeepsLastGoodResultStaleAndDoesNotDiscardOtherSource()
    {
        MemoryCache cache = new(); await cache.SaveSourceSnapshotAsync(Source("winget", SourceSnapshotState.Live), CancellationToken.None); UpdateSourceRefreshService service = new(cache); FakeConnector healthy = new("msstore", Source("msstore", SourceSnapshotState.Live)); FakeConnector failed = new("winget", null, fail: true);
        SourceSnapshot[] result = (await service.RefreshAsync([healthy, failed], true, CancellationToken.None)).OrderBy(snapshot => snapshot.SourceId).ToArray();
        Assert.AreEqual(SourceSnapshotState.Cached, result[1].State); CollectionAssert.Contains(result[1].Warnings.ToArray(), "source:stale"); Assert.AreEqual(SourceSnapshotState.Live, result[0].State);
    }

    [TestMethod]
    public async Task NoCacheFailureIsUnavailableAndConnectorConcurrencyIsBounded()
    {
        MemoryCache cache = new(); UpdateSourceRefreshService service = new(cache, 2); List<FakeConnector> connectors = Enumerable.Range(0, 4).Select(index => new FakeConnector(index % 2 == 0 ? "winget" : "msstore", null, delay: TimeSpan.FromMilliseconds(40), fail: true)).ToList();
        SourceSnapshot[] result = (await service.RefreshAsync(connectors, true, CancellationToken.None)).ToArray();
        Assert.IsTrue(result.All(snapshot => snapshot.State == SourceSnapshotState.Unavailable)); Assert.IsTrue(FakeConnector.MaximumActive <= 2);
    }

    [TestMethod]
    public async Task AFailedRefreshShowsTheConnectorsReasonCodesButNeverFreeText()
    {
        MemoryCache cache = new(); UpdateSourceRefreshService service = new(cache);
        SourceSnapshot coded = (await service.RefreshAsync([new FakeConnector("winget", null, failure: new InvalidOperationException("winget:malformed-update-row,winget:exit-1"))], true, CancellationToken.None)).Single();
        SourceSnapshot text = (await service.RefreshAsync([new FakeConnector("msstore", null, failure: new InvalidOperationException(@"Could not open C:\Users\someone\file"))], true, CancellationToken.None)).Single();

        CollectionAssert.AreEqual(new[] { "source:unavailable", "winget:malformed-update-row", "winget:exit-1" }, coded.Warnings.ToArray());
        CollectionAssert.AreEqual(new[] { "source:unavailable" }, text.Warnings.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), UpdateSourceRefreshService.ConnectorReasons(new InvalidOperationException("winget:a,winget:b,winget:c,winget:d,winget:e")));
        CollectionAssert.AreEqual(Array.Empty<string>(), UpdateSourceRefreshService.ConnectorReasons(new InvalidOperationException("source:unavailable")));
    }

    [TestMethod]
    public async Task RetentionPrunesExpiredSourceCacheBeforeAnOfflineRefresh()
    {
        MemoryCache cache = new();
        await cache.SaveSourceSnapshotAsync(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow.AddDays(-31), SourceSnapshotState.Live, [], []), CancellationToken.None);
        UpdateSourceRefreshService service = new(cache, cacheRetention: TimeSpan.FromDays(30));

        SourceSnapshot result = (await service.RefreshAsync([new FakeConnector("winget", Source("winget", SourceSnapshotState.Live))], false, CancellationToken.None)).Single();

        Assert.AreEqual(SourceSnapshotState.LocalOnly, result.State);
        Assert.AreEqual(1, cache.PruneCalls);
    }

    private static SourceSnapshot Source(string id, SourceSnapshotState state) => new(id, "v1", DateTimeOffset.UtcNow, state, [new SourcePackageRecord(id, "Contoso.Widget", "Contoso Widget", null, "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);
    private sealed class MemoryCache : ISourceSnapshotStore
    {
        private readonly Dictionary<string, SourceSnapshot> snapshots = [];
        public Task SaveSourceSnapshotAsync(SourceSnapshot snapshot, CancellationToken cancellationToken) { snapshots[snapshot.SourceId] = snapshot; return Task.CompletedTask; }
        public Task<SourceSnapshot?> GetSourceSnapshotAsync(string sourceId, CancellationToken cancellationToken) => Task.FromResult(snapshots.GetValueOrDefault(sourceId));
        public int PruneCalls { get; private set; }
        public Task PruneSourceSnapshotsAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) { PruneCalls++; foreach (string id in snapshots.Where(pair => pair.Value.LastSuccessAt < olderThan).Select(pair => pair.Key).ToArray()) snapshots.Remove(id); return Task.CompletedTask; }
    }
    private sealed class FakeConnector(string sourceId, SourceSnapshot? result, TimeSpan? delay = null, bool fail = false, Exception? failure = null) : IUpdateSourceConnector
    {
        private static int active; public static int MaximumActive { get; private set; } public string SourceId { get; } = sourceId; public TimeSpan Timeout => TimeSpan.FromSeconds(1); public int Calls { get; private set; }
        public async Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken) { Calls++; int now = Interlocked.Increment(ref active); MaximumActive = Math.Max(MaximumActive, now); try { if (delay is not null) await Task.Delay(delay.Value, cancellationToken); if (failure is not null) throw failure; if (fail) throw new TimeoutException(); return result!; } finally { Interlocked.Decrement(ref active); } }
    }
}
