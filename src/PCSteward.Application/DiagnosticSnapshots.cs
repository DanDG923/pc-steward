using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>FR-DIA-03: why a snapshot was not saved. The previously saved snapshots are unchanged in every case.</summary>
public enum DiagnosticSnapshotSaveFailure
{
    None,
    InvalidSnapshot,
    AlreadyExpired,
    CapacityHeldByProtectedSnapshots,
    StorageFailed
}

/// <summary>
/// FR-DIA-03 (ADR-027 retention): the result of saving one snapshot. <see cref="PrunedForCapacity"/> counts older snapshots removed
/// to stay within 64 MiB, which can shorten their 30-day retention and must be disclosed.
/// </summary>
public sealed record DiagnosticSnapshotSaveResult(bool Saved, DiagnosticSnapshotSaveFailure Failure, int ExpiredRemoved, int PrunedForCapacity);

/// <summary>FR-DIA-01/03: one saved snapshot as listed, without its records. Expired snapshots are never listed.</summary>
public sealed record DiagnosticSnapshotSummary(
    Guid Id,
    DateTimeOffset CollectedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int RecordCount,
    long PayloadBytes,
    IReadOnlyList<DiagnosticSourceResult> Sources)
{
    public bool HasCoverage => Sources.Any(source => source.Outcome is DiagnosticSourceOutcome.Available or DiagnosticSourceOutcome.Empty or DiagnosticSourceOutcome.Partial);

    public bool IsPartial => Sources.Any(source => source.Outcome is not (DiagnosticSourceOutcome.Available or DiagnosticSourceOutcome.Empty));
}

/// <summary>FR-DIA-02/03: loading a snapshot either replays it whole or reports it unavailable; it never returns fewer records.</summary>
public enum DiagnosticSnapshotLoadStatus
{
    Loaded,
    Unavailable,
    Unreadable
}

public sealed record DiagnosticSnapshotLoad(DiagnosticSnapshotLoadStatus Status, DiagnosticSnapshot? Snapshot);

/// <summary>
/// FR-DIA-03/04 (ADR-015/027): app-owned storage for whole immutable diagnostic snapshots. Snapshots expire 30 days after their
/// collection instant or earlier at the 64 MiB capacity, Clear scan data or factory reset. Expired snapshots are excluded before
/// listing, loading or export even if their deletion has not happened yet. Captured PC Steward history is a snapshot-owned copy,
/// so Clear update history never changes a saved snapshot.
/// </summary>
public interface IDiagnosticSnapshotStore
{
    /// <summary>Saves the snapshot, removes expired ones, then removes the oldest snapshots not in <paramref name="protectedSnapshotIds"/> until the total fits 64 MiB. All in one transaction.</summary>
    Task<DiagnosticSnapshotSaveResult> SaveDiagnosticSnapshotAsync(DiagnosticSnapshot snapshot, DateTimeOffset nowUtc, IReadOnlyCollection<Guid> protectedSnapshotIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<DiagnosticSnapshotSummary>> ListDiagnosticSnapshotsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);

    Task<DiagnosticSnapshotLoad> LoadDiagnosticSnapshotAsync(Guid snapshotId, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Bounded opportunistic maintenance at start or use; there is no scheduled or resident collector.</summary>
    Task<int> DeleteExpiredDiagnosticSnapshotsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

/// <summary>One recorded update event and the app's display name from the current inventory, when it is still known.</summary>
public sealed record DiagnosticAppHistoryEntry(UpdateHistoryEntry Entry, string? DisplayName);

/// <summary>FR-DIA-01: PC Steward's own recorded update history inside an interval, newest first, read without changing anything.</summary>
public interface IDiagnosticAppHistorySource
{
    /// <summary>Returns at most <paramref name="maximumEntries"/> entries with <c>fromUtc &lt;= occurred &lt;= toUtc</c>, and whether more matched.</summary>
    Task<(IReadOnlyList<DiagnosticAppHistoryEntry> Entries, bool MoreMatched)> GetUpdateHistoryBetweenAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken);
}

/// <summary>
/// FR-DIA-01 (ADR-027 Q1): the first source. Projects only records actually present in PC Steward's verified update history: the
/// app's display name (or package ID when it is no longer in the inventory), versions, typed outcome, exit code and the already sanitized diagnostic. It never reads install/uninstall history that
/// does not exist yet.
/// </summary>
public sealed class PcStewardHistoryDiagnosticReader(IDiagnosticAppHistorySource history) : IDiagnosticSourceReader
{
    public DiagnosticSourceKind Source => DiagnosticSourceKind.PcStewardHistory;

    public async Task<DiagnosticReaderResult> ReadAsync(DiagnosticReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        (IReadOnlyList<DiagnosticAppHistoryEntry> entries, bool more) = await history.GetUpdateHistoryBetweenAsync(request.FromUtc, request.ToUtc, request.MaximumMatchingRecords, cancellationToken).ConfigureAwait(false);
        DiagnosticRecordCandidate[] records = [.. entries.Take(request.MaximumMatchingRecords).Select(entry => Project(entry, request.ToUtc))];
        return DiagnosticReaderResult.Completed(records, more || entries.Count > request.MaximumMatchingRecords);
    }

    internal static DiagnosticRecordCandidate Project(DiagnosticAppHistoryEntry item, DateTimeOffset collectionStartUtc)
    {
        UpdateHistoryEntry entry = item.Entry;
        return new(DiagnosticSourceKind.PcStewardHistory,
            entry.EventId.ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            DiagnosticTimeNormalizer.FromInstant(entry.OccurredAt, collectionStartUtc),
            DiagnosticEventKind.PcStewardUpdate,
            Outcome(entry.Status),
            ResultCode: entry.ExitCode,
            Subject: string.IsNullOrWhiteSpace(item.DisplayName) ? entry.PackageId : item.DisplayName,
            Detail: $"{entry.InstalledVersion} to {entry.TargetVersion}",
            Summary: entry.Diagnostic);
    }

    internal static DiagnosticOutcomeKind Outcome(UpdateHistoryStatus status) => status switch
    {
        UpdateHistoryStatus.Success => DiagnosticOutcomeKind.Succeeded,
        UpdateHistoryStatus.RestartRequired => DiagnosticOutcomeKind.RestartPending,
        UpdateHistoryStatus.Cancelled => DiagnosticOutcomeKind.Cancelled,
        UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.RunningApplicationWarning or UpdateHistoryStatus.VerificationRequired => DiagnosticOutcomeKind.InProgress,
        UpdateHistoryStatus.SourceFailed or UpdateHistoryStatus.DownloadFailed or UpdateHistoryStatus.InstallerFailed or UpdateHistoryStatus.ElevationDenied or
            UpdateHistoryStatus.TimedOut or UpdateHistoryStatus.VerificationFailed or UpdateHistoryStatus.ProductFailed => DiagnosticOutcomeKind.Failed,
        _ => DiagnosticOutcomeKind.Unknown
    };
}
