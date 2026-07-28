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

    [TestMethod]
    public async Task LockedOrphanIsMeasuredAndPreventsFalseCleanupSuccess()
    {
        await using ArtifactFixture fixture = new();
        string orphan = Path.Combine(fixture.Root, "crash-before-registration.bin");
        await File.WriteAllBytesAsync(orphan, Enumerable.Repeat((byte)3, 9).ToArray());
        await using FileStream lockHandle = new(orphan, FileMode.Open, FileAccess.Read, FileShare.None);

        DownloadCleanupResult result = await fixture.CreateCache().ClearInactiveAsync(CancellationToken.None);
        DownloadCacheSummary summary = await fixture.CreateCache().GetSummaryAsync(CancellationToken.None);

        Assert.IsFalse(result.Succeeded); Assert.AreEqual(1, result.RemainingItems); Assert.AreEqual(9, result.RemainingBytes);
        Assert.IsTrue(result.Residuals.Any(residual => residual.RelativePath == "crash-before-registration.bin"));
        Assert.AreEqual(9, summary.TotalBytes);
    }

    [TestMethod]
    public async Task QuotaAccountsForAndEvictsAnInactiveCrashOrphan()
    {
        await using ArtifactFixture fixture = new();
        DownloadedArtifact active = fixture.Write("active.bin", 20, DateTimeOffset.UtcNow, active: true);
        await fixture.CreateCache(40).RegisterAsync(active, CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(fixture.Root, "crash-orphan.bin"), Enumerable.Repeat((byte)4, 30).ToArray());

        DownloadCleanupResult result = await fixture.CreateCache(40).EnforceRetentionAndQuotaAsync(DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "active.bin")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "crash-orphan.bin")));
        Assert.AreEqual(20, result.RemainingBytes);
    }

    [TestMethod]
    public async Task ProductionCacheRejectsJunctionWithoutTouchingOutsideTarget()
    {
        await using ArtifactFixture fixture = new();
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-outside-" + Guid.NewGuid().ToString("N"));
        string junction = Path.Combine(fixture.Root, "junction");
        Directory.CreateDirectory(outside);
        string sentinel = Path.Combine(outside, "must-survive.txt");
        await File.WriteAllTextAsync(sentinel, "outside");
        try
        {
            await CreateJunctionAsync(junction, outside);

            DownloadCleanupResult result = await fixture.CreateCache().ClearInactiveAsync(CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(residual => residual.RelativePath == "junction" && residual.Reason == "UnsafePath"));
            Assert.AreEqual("outside", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            if (Directory.Exists(junction)) new DirectoryInfo(junction).Delete();
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task ProductionCacheRejectsRepeatedAncestorJunctionsWithoutTouchingOutsideTarget()
    {
        await using ArtifactFixture fixture = new();
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-outside-" + Guid.NewGuid().ToString("N"));
        string ancestor = Path.Combine(fixture.Root, "ancestor");
        Directory.CreateDirectory(outside);
        string sentinel = Path.Combine(outside, "must-survive.txt");
        await File.WriteAllTextAsync(sentinel, "outside");
        try
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await CreateJunctionAsync(ancestor, outside);
                DownloadCleanupResult result = await fixture.CreateCache().ClearInactiveAsync(CancellationToken.None);
                Assert.IsFalse(result.Succeeded);
                Assert.IsTrue(result.Residuals.Any(residual => residual.RelativePath == "ancestor" && residual.Reason == "UnsafePath"));
                Assert.AreEqual("outside", await File.ReadAllTextAsync(sentinel));
                new DirectoryInfo(ancestor).Delete();
            }
        }
        finally
        {
            if (Directory.Exists(ancestor)) new DirectoryInfo(ancestor).Delete();
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task ProductionCacheRejectsAJunctionAsTheConfiguredRoot()
    {
        string container = Path.Combine(Path.GetTempPath(), "pcsteward-root-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(container, "outside");
        string rootLink = Path.Combine(container, "root-link");
        Directory.CreateDirectory(outside);
        try
        {
            await CreateJunctionAsync(rootLink, outside);
            Assert.ThrowsExactly<System.Security.SecurityException>(() => new DownloadedArtifactCache(new MemoryArtifactStore(), rootLink));
        }
        finally
        {
            if (Directory.Exists(rootLink)) new DirectoryInfo(rootLink).Delete();
            if (Directory.Exists(container)) Directory.Delete(container, true);
        }
    }

    private static async Task CreateJunctionAsync(string path, string target)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        process.Start();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
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
