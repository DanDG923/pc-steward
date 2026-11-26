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
        Assert.AreEqual(SourceSnapshotState.Live, live.State); Assert.AreEqual(SourceSnapshotState.Cached, cached.State); Assert.IsNotNull(cached.LastSuccessAt); Assert.IsEmpty(cached.Warnings, "review F-22: a saved result shown again was not contacted, so it is not called offline"); Assert.AreEqual(1, connector.Calls);
    }

    [TestMethod]
    [Description("Review F-22 (FR-UI-05): reloading saved results after a good check never says a source couldn't be reached.")]
    public async Task ReloadingSavedResultsAfterAGoodCheckReadsCalmly()
    {
        MemoryCache cache = new(); UpdateSourceRefreshService service = new(cache);
        SourceSnapshot good = Source("winget", SourceSnapshotState.Live) with { Warnings = ["winget:duplicate-upgrade-id"] };
        await service.RefreshAsync([new FakeConnector("winget", good)], true, CancellationToken.None);
        SourceSnapshot[] reloaded = [.. await service.RefreshAsync([new FakeConnector("winget", good)], false, CancellationToken.None)];
        DateTimeOffset now = DateTimeOffset.UtcNow;

        FreshnessPresentation recent = FreshnessPresenter.Describe(true, reloaded, now);
        Assert.AreEqual(EvidenceFreshness.Live, recent.Freshness, "a provider's own warning saved with a good result is not a failed check");
        Assert.AreEqual(StatusTone.Information, recent.Tone);
        Assert.IsFalse(FreshnessPresenter.LastCheckFailed(reloaded[0]));

        FreshnessPresentation nextDay = FreshnessPresenter.Describe(true, reloaded, now.AddDays(1));
        Assert.AreEqual(EvidenceFreshness.Cached, nextDay.Freshness);
        StringAssert.StartsWith(nextDay.Sentence, "Cached: these are the results of the last check.");
        Assert.AreEqual(StatusTone.Information, nextDay.Tone);

        // A source never checked is local-only, with nothing said about being offline.
        SourceSnapshot never = (await new UpdateSourceRefreshService(new MemoryCache()).RefreshAsync([new FakeConnector("winget", good)], false, CancellationToken.None)).Single();
        Assert.AreEqual(SourceSnapshotState.LocalOnly, never.State); Assert.IsEmpty(never.Warnings);

        // Fix review RV-3: one source with a recent saved result never makes the summary Live or calm while another has none.
        FreshnessPresentation partial = FreshnessPresenter.Describe(true, [reloaded[0], never with { SourceId = "msstore" }], now);
        Assert.AreEqual(EvidenceFreshness.Cached, partial.Freshness);
        Assert.AreEqual(StatusTone.Attention, partial.Tone);
        StringAssert.EndsWith(partial.Sentence, "Some update sources have no saved result yet.");
    }

    [TestMethod]
    [Description("Review F-22, known limit: a failed check is not saved, so a reload after it shows the last good result and its age.")]
    public async Task AFailedCheckWarnsUntilTheSavedResultsAreReloaded()
    {
        MemoryCache cache = new(); UpdateSourceRefreshService service = new(cache);
        await service.RefreshAsync([new FakeConnector("winget", Source("winget", SourceSnapshotState.Live))], true, CancellationToken.None);
        SourceSnapshot failed = (await service.RefreshAsync([new FakeConnector("winget", null, fail: true)], true, CancellationToken.None)).Single();
        Assert.IsTrue(FreshnessPresenter.LastCheckFailed(failed));
        Assert.AreEqual(StatusTone.Attention, FreshnessPresenter.Describe(true, [failed], DateTimeOffset.UtcNow).Tone);

        SourceSnapshot reloaded = (await service.RefreshAsync([new FakeConnector("winget", null, fail: true)], false, CancellationToken.None)).Single();
        Assert.IsFalse(FreshnessPresenter.LastCheckFailed(reloaded), "the failure itself is not stored; the reload states only the last success and its age");
        Assert.IsNotNull(reloaded.LastSuccessAt);
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

    private static readonly string[] ExpectedCodedWarnings = ["source:unavailable", "winget:malformed-update-row", "winget:exit-1"];
    private static readonly string[] ExpectedTextWarnings = ["source:unavailable"];

    [TestMethod]
    public async Task AFailedRefreshShowsTheConnectorsReasonCodesButNeverFreeText()
    {
        MemoryCache cache = new(); UpdateSourceRefreshService service = new(cache);
        SourceSnapshot coded = (await service.RefreshAsync([new FakeConnector("winget", null, failure: new InvalidOperationException("winget:malformed-update-row,winget:exit-1"))], true, CancellationToken.None)).Single();
        SourceSnapshot text = (await service.RefreshAsync([new FakeConnector("msstore", null, failure: new InvalidOperationException(@"Could not open C:\Users\someone\file"))], true, CancellationToken.None)).Single();

        CollectionAssert.AreEqual(ExpectedCodedWarnings, coded.Warnings.ToArray());
        CollectionAssert.AreEqual(ExpectedTextWarnings, text.Warnings.ToArray());
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
