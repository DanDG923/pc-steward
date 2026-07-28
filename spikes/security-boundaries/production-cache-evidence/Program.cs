using System.Security;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Storage;

if (args.Length is not 3 and not 4)
{
    Console.Error.WriteLine("Usage: <cache-root> <relative-path> <outside-sentinel> [--root-must-reject]");
    return 2;
}

string cacheRoot = Path.GetFullPath(args[0]);
string relativePath = args[1];
string sentinel = Path.GetFullPath(args[2]);
bool rootMustReject = args.Length == 4 && args[3] == "--root-must-reject";
string expected = File.ReadAllText(sentinel);

try
{
    using DownloadedArtifactCache cache = new(new EmptyArtifactStore(), cacheRoot);
    if (rootMustReject)
    {
        Console.Error.WriteLine("FAIL: a reparse cache root was accepted.");
        return 3;
    }

    DownloadCleanupResult result = await cache.ClearInactiveAsync(CancellationToken.None);
    if (result.Succeeded || !result.Residuals.Any(residual => residual.RelativePath == relativePath && residual.Reason == "UnsafePath"))
    {
        Console.Error.WriteLine("FAIL: production cache did not fail closed with an UnsafePath residual.");
        return 4;
    }
}
catch (SecurityException) when (rootMustReject)
{
    // Expected: the production constructor rejects a reparse cache root before cleanup.
}

if (!File.Exists(sentinel) || File.ReadAllText(sentinel) != expected)
{
    Console.Error.WriteLine("FAIL: the outside sentinel changed.");
    return 5;
}

Console.WriteLine($"PASS: production cache rejected {relativePath}; outside sentinel survived.");
return 0;

sealed class EmptyArtifactStore : IDownloadedArtifactStore
{
    public Task SaveDownloadedArtifactAsync(DownloadedArtifact artifact, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyCollection<DownloadedArtifact>> GetDownloadedArtifactsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<DownloadedArtifact>>([]);
    public Task DeleteDownloadedArtifactAsync(Guid artifactId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<DateTimeOffset?> GetDownloadedArtifactsLastCleanedAtAsync(CancellationToken cancellationToken) => Task.FromResult<DateTimeOffset?>(null);
    public Task SetDownloadedArtifactsLastCleanedAtAsync(DateTimeOffset cleanedAt, CancellationToken cancellationToken) => Task.CompletedTask;
}
