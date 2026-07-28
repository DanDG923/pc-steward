using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class DownloadedArtifactCacheTests
{
    [TestMethod]
    public async Task QuotaEvictsOldestInactiveArtifactButNeverAnActiveArtifact()
    {
        await using ArtifactFixture fixture = new();
        DownloadedArtifact old = fixture.Write("old.bin", 20, DateTimeOffset.UtcNow.AddDays(-2));
        DownloadedArtifact active = fixture.Write("active.bin", 20, DateTimeOffset.UtcNow.AddDays(-1), active: true);
        DownloadedArtifact newest = fixture.Write("new.bin", 20, DateTimeOffset.UtcNow);
        DownloadedArtifactCache cache = fixture.CreateCache(45);
        await cache.RegisterAsync(old, CancellationToken.None); await cache.RegisterAsync(active, CancellationToken.None); await cache.RegisterAsync(newest, CancellationToken.None);

        DownloadCleanupResult result = await cache.EnforceRetentionAndQuotaAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.IsTrue(result.Succeeded); Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "old.bin"))); Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "active.bin"))); Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "new.bin")));
        Assert.AreEqual(2, result.RemainingItems);
    }

    [TestMethod]
    public async Task ClearIsIdempotentAndRejectsCraftedLedgerTraversalWithoutTouchingOutsideSentinel()
    {
        await using ArtifactFixture fixture = new();
        string outside = Path.Combine(Path.GetTempPath(), $"pcsteward-outside-{Guid.NewGuid():N}.txt"); await File.WriteAllTextAsync(outside, "sentinel");
        try
        {
            DownloadedArtifactCache cache = fixture.CreateCache();
            await Assert.ThrowsExactlyAsync<System.Security.SecurityException>(() => cache.RegisterAsync(new DownloadedArtifact(Guid.NewGuid(), "update", "..\\" + Path.GetFileName(outside), 1, DateTimeOffset.UtcNow), CancellationToken.None));
            DownloadedArtifact artifact = fixture.Write("payload.bin", 8, DateTimeOffset.UtcNow);
            await cache.RegisterAsync(artifact, CancellationToken.None);
            DownloadCleanupResult first = await cache.ClearInactiveAsync(CancellationToken.None); DownloadCleanupResult second = await cache.ClearInactiveAsync(CancellationToken.None);

            Assert.IsTrue(first.Succeeded); Assert.IsTrue(second.Succeeded); Assert.AreEqual("sentinel", await File.ReadAllTextAsync(outside)); Assert.AreEqual(0, second.RemainingItems);
        }
        finally { File.Delete(outside); }
    }

    [TestMethod]
    public async Task LockedArtifactProducesPartialAccurateCleanupAndItsLedgerEntryRemains()
    {
        await using ArtifactFixture fixture = new();
        DownloadedArtifact artifact = fixture.Write("locked.bin", 8, DateTimeOffset.UtcNow);
        DownloadedArtifactCache cache = fixture.CreateCache(); await cache.RegisterAsync(artifact, CancellationToken.None);
        await using FileStream lockHandle = new(Path.Combine(fixture.Root, "locked.bin"), FileMode.Open, FileAccess.Read, FileShare.None);

        DownloadCleanupResult result = await cache.ClearInactiveAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded); Assert.AreEqual(1, result.RemainingItems); Assert.IsTrue(result.Residuals.Single().Reason is "LockedOrInUse" or "AccessDenied");
        Assert.HasCount(1, await fixture.Store.GetDownloadedArtifactsAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RetentionUsesSevenDaysForVerifiedSuccessAndOneDayForFailedArtifacts()
    {
        await using ArtifactFixture fixture = new(); DownloadedArtifactCache cache = fixture.CreateCache();
        DownloadedArtifact success = fixture.Write("success.bin", 3, DateTimeOffset.UtcNow.AddDays(-8), outcome: DownloadedArtifactOutcome.VerifiedSuccess);
        DownloadedArtifact failed = fixture.Write("failed.bin", 3, DateTimeOffset.UtcNow.AddDays(-2), outcome: DownloadedArtifactOutcome.Failed);
        DownloadedArtifact retained = fixture.Write("retained.bin", 3, DateTimeOffset.UtcNow.AddDays(-2), outcome: DownloadedArtifactOutcome.VerifiedSuccess);
        await cache.RegisterAsync(success, CancellationToken.None); await cache.RegisterAsync(failed, CancellationToken.None); await cache.RegisterAsync(retained, CancellationToken.None);

        DownloadCleanupResult result = await cache.EnforceRetentionAndQuotaAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.IsTrue(result.Succeeded); Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "success.bin"))); Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "failed.bin"))); Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "retained.bin")));
    }

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        public ArtifactFixture() { Root = Path.Combine(Path.GetTempPath(), "pcsteward-cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root); Store = new MemoryArtifactStore(); }
        public string Root { get; } public MemoryArtifactStore Store { get; }
        public DownloadedArtifactCache CreateCache(long quota = DownloadedArtifactCache.DefaultSoftQuotaBytes) => new(Store, Root, quota);
        public DownloadedArtifact Write(string relativePath, int size, DateTimeOffset createdAt, bool active = false, DownloadedArtifactOutcome outcome = DownloadedArtifactOutcome.Incomplete) { File.WriteAllBytes(Path.Combine(Root, relativePath), Enumerable.Repeat((byte)7, size).ToArray()); return new DownloadedArtifact(Guid.NewGuid(), "update", relativePath, size, createdAt, "aabb", Guid.NewGuid(), outcome, active); }
        public ValueTask DisposeAsync() { try { Directory.Delete(Root, true); } catch (IOException) { } return ValueTask.CompletedTask; }
    }

    private sealed class MemoryArtifactStore : IDownloadedArtifactStore
    {
        private readonly Dictionary<Guid, DownloadedArtifact> artifacts = []; private DateTimeOffset? lastCleaned;
        public Task SaveDownloadedArtifactAsync(DownloadedArtifact artifact, CancellationToken cancellationToken) { artifacts[artifact.Id] = artifact; return Task.CompletedTask; }
        public Task<IReadOnlyCollection<DownloadedArtifact>> GetDownloadedArtifactsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<DownloadedArtifact>>(artifacts.Values.ToArray());
        public Task DeleteDownloadedArtifactAsync(Guid artifactId, CancellationToken cancellationToken) { artifacts.Remove(artifactId); return Task.CompletedTask; }
        public Task<DateTimeOffset?> GetDownloadedArtifactsLastCleanedAtAsync(CancellationToken cancellationToken) => Task.FromResult(lastCleaned);
        public Task SetDownloadedArtifactsLastCleanedAtAsync(DateTimeOffset cleanedAt, CancellationToken cancellationToken) { lastCleaned = cleanedAt; return Task.CompletedTask; }
    }
}
