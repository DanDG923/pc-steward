using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Application;

public interface IUpdateSourceConnector
{
    string SourceId { get; }
    TimeSpan Timeout { get; }
    Task<SourceSnapshot> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>Runs only caller-enabled connectors, retains the last good snapshot, and bounds online fan-out.</summary>
public sealed partial class UpdateSourceRefreshService
{
    private readonly ISourceSnapshotStore cache;
    private readonly int maximumConcurrency;
    public UpdateSourceRefreshService(ISourceSnapshotStore cache, int maximumConcurrency = 2, TimeSpan? cacheRetention = null)
    {
        this.cache = cache;
        if (maximumConcurrency is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        this.maximumConcurrency = maximumConcurrency;
        CacheRetention = cacheRetention ?? TimeSpan.FromDays(30);
        if (CacheRetention is { TotalDays: < 1 or > 90 }) throw new ArgumentOutOfRangeException(nameof(cacheRetention));
    }

    public TimeSpan CacheRetention { get; }

    public async Task<IReadOnlyCollection<SourceSnapshot>> RefreshAsync(IEnumerable<IUpdateSourceConnector> enabledConnectors, bool online, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enabledConnectors);
        IUpdateSourceConnector[] connectors = enabledConnectors.OrderBy(connector => connector.SourceId, StringComparer.Ordinal).ToArray();
        await cache.PruneSourceSnapshotsAsync(DateTimeOffset.UtcNow - CacheRetention, cancellationToken).ConfigureAwait(false);
        if (!online)
        {
            List<SourceSnapshot> cached = [];
            foreach (IUpdateSourceConnector connector in connectors) cached.Add(await CachedOrLocalOnlyAsync(connector.SourceId, "source:offline", cancellationToken));
            return cached;
        }
        using SemaphoreSlim slots = new(maximumConcurrency, maximumConcurrency);
        return await Task.WhenAll(connectors.Select(async connector =>
        {
            await slots.WaitAsync(cancellationToken);
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(connector.Timeout);
                try
                {
                    SourceSnapshot fresh = await connector.RefreshAsync(timeout.Token).ConfigureAwait(false);
                    if (!string.Equals(fresh.SourceId, connector.SourceId, StringComparison.OrdinalIgnoreCase) || fresh.State != SourceSnapshotState.Live) throw new InvalidOperationException("source:invalid-connector-result");
                    await cache.SaveSourceSnapshotAsync(fresh, cancellationToken).ConfigureAwait(false);
                    return fresh;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return await CachedOrUnavailableAsync(connector.SourceId, ["source:timeout"], cancellationToken); }
                catch (Exception exception) { return await CachedOrUnavailableAsync(connector.SourceId, ["source:unavailable", .. ConnectorReasons(exception)], cancellationToken); }
            }
            finally { slots.Release(); }
        })).ConfigureAwait(false);
    }

    private async Task<SourceSnapshot> CachedOrLocalOnlyAsync(string sourceId, string warning, CancellationToken cancellationToken)
    {
        SourceSnapshot? cached = await cache.GetSourceSnapshotAsync(sourceId, cancellationToken).ConfigureAwait(false);
        return cached is null ? new(sourceId, null, null, SourceSnapshotState.LocalOnly, [], [warning]) : cached with { State = SourceSnapshotState.Cached, Warnings = cached.Warnings.Concat([warning]).Distinct(StringComparer.Ordinal).ToArray() };
    }
    private async Task<SourceSnapshot> CachedOrUnavailableAsync(string sourceId, string[] warnings, CancellationToken cancellationToken)
    {
        SourceSnapshot? cached = await cache.GetSourceSnapshotAsync(sourceId, cancellationToken).ConfigureAwait(false);
        return cached is null ? new(sourceId, null, null, SourceSnapshotState.Unavailable, [], warnings) : cached with { State = SourceSnapshotState.Cached, Warnings = cached.Warnings.Concat(warnings).Append("source:stale").Distinct(StringComparer.Ordinal).ToArray() };
    }

    /// <summary>
    /// The connector's own reason codes for a failed refresh (a provider connector throws with its warning codes joined by commas),
    /// kept only when every part is a short fixed-shape code such as <c>winget:malformed-update-row</c>, so the owner can see why a
    /// source is unavailable. Free text from an exception is never shown.
    /// </summary>
    internal static string[] ConnectorReasons(Exception exception)
    {
        string[] parts = exception.Message.Split(',');
        return parts.Length is > 0 and <= 4 && parts.All(static part => ReasonCode().IsMatch(part)) ? parts.Distinct(StringComparer.Ordinal).ToArray() : [];
    }

    [GeneratedRegex("^(?:winget|chocolatey):[a-z0-9-]{1,48}$")]
    private static partial Regex ReasonCode();
}
