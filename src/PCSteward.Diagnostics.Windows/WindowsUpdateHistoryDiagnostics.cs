using System.Globalization;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Diagnostics.Windows;

/// <summary>What the Windows Update history route answered: matching entries newest first, at most the requested number.</summary>
public sealed record WindowsUpdateHistoryAnswer(DiagnosticReaderStatus Status, IReadOnlyList<WindowsUpdateRawEntry> Entries, bool MoreMatched);

public interface IWindowsUpdateHistorySource
{
    WindowsUpdateHistoryAnswer Read(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken);
}

/// <summary>
/// FR-DIA-01 (ADR-027 Q1): Windows Update history paging and projection (update identity and time, operation and result, the
/// sanitized title and the numeric result code). History is returned newest first, so paging stops at the first entry older than
/// the window, after the requested number plus one, or at a fixed inspection ceiling (then reported as truncated).
/// </summary>
public static class WindowsUpdateHistoryCatalog
{
    public const int PageSize = 50;
    public const int InspectionCeiling = 4 * DiagnosticCollectionPolicy.MaximumRecordsPerSource;

    public static WindowsUpdateHistoryAnswer Page(int total, Func<int, int, IReadOnlyList<WindowsUpdateRawEntry>> page, DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumEntries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEntries);
        List<WindowsUpdateRawEntry> matching = [];
        int limit = Math.Min(Math.Max(total, 0), InspectionCeiling);
        int inspected = 0;
        while (inspected < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<WindowsUpdateRawEntry> entries = page(inspected, Math.Min(PageSize, limit - inspected));
            if (entries.Count == 0) break;
            foreach (WindowsUpdateRawEntry entry in entries.Take(limit - inspected))
            {
                inspected++;
                if (entry.DateUtc is { } date && date < fromUtc.UtcDateTime) return Answer(matching, maximumEntries, false);
                if (entry.DateUtc is { } later && later > toUtc.UtcDateTime) continue;
                matching.Add(entry);
                if (matching.Count > maximumEntries) return Answer(matching, maximumEntries, true);
            }
        }

        return Answer(matching, maximumEntries, inspected >= InspectionCeiling && total > InspectionCeiling);
    }

    public static DiagnosticRecordCandidate Project(WindowsUpdateRawEntry entry, DateTimeOffset collectionStartUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DateTimeOffset? occurred = entry.DateUtc is { } date ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)) : null;
        string? recordId = Guid.TryParse(entry.UpdateId, out Guid update) && entry.RevisionNumber is >= 0 && occurred is { } at && entry.Operation is >= 0 and <= 9
            ? string.Create(CultureInfo.InvariantCulture, $"{update:D}:{entry.RevisionNumber}:{at.UtcDateTime:yyyyMMddHHmmss}:{entry.Operation}")
            : null;
        string? operation = entry.Operation switch { 1 => "Install", 2 => "Uninstall", _ => null };
        string? detail = entry.ResultCode == 3 ? (operation is null ? "Completed with errors" : operation + ", completed with errors") : operation;
        return new(DiagnosticSourceKind.WindowsUpdateHistory, recordId, DiagnosticTimeNormalizer.FromInstant(occurred, collectionStartUtc), DiagnosticEventKind.WindowsUpdate,
            Outcome(entry.ResultCode), ResultCode: entry.HResult is null or 0 ? null : entry.HResult, Subject: entry.Title, Detail: detail);
    }

    public static DiagnosticOutcomeKind Outcome(int? resultCode) => resultCode switch
    {
        1 => DiagnosticOutcomeKind.InProgress,
        2 => DiagnosticOutcomeKind.Succeeded,
        3 or 4 => DiagnosticOutcomeKind.Failed,
        5 => DiagnosticOutcomeKind.Cancelled,
        _ => DiagnosticOutcomeKind.Unknown
    };

    private static WindowsUpdateHistoryAnswer Answer(List<WindowsUpdateRawEntry> matching, int maximumEntries, bool more) =>
        new(DiagnosticReaderStatus.Completed, matching.Count > maximumEntries ? matching.GetRange(0, maximumEntries) : matching, more || matching.Count > maximumEntries);
}

/// <summary>FR-DIA-01: the read-only Windows Update history source.</summary>
public sealed class WindowsUpdateHistoryDiagnosticReader(IWindowsUpdateHistorySource source) : IDiagnosticSourceReader
{
    public DiagnosticSourceKind Source => DiagnosticSourceKind.WindowsUpdateHistory;

    public Task<DiagnosticReaderResult> ReadAsync(DiagnosticReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        WindowsUpdateHistoryAnswer answer = source.Read(request.FromUtc, request.ToUtc, request.MaximumMatchingRecords, cancellationToken);
        if (answer.Status != DiagnosticReaderStatus.Completed) return Task.FromResult(new DiagnosticReaderResult(answer.Status, [], false));
        DiagnosticRecordCandidate[] records = [.. answer.Entries.Take(request.MaximumMatchingRecords).Select(entry => WindowsUpdateHistoryCatalog.Project(entry, request.ToUtc))];
        return Task.FromResult(DiagnosticReaderResult.Completed(records, answer.MoreMatched || answer.Entries.Count > request.MaximumMatchingRecords));
    }
}

/// <summary>FR-DIA-01: the four Windows readers, ready to give to the collection orchestrator alongside PC Steward's own history.</summary>
public static class WindowsDiagnosticReaders
{
    public static IReadOnlyList<IDiagnosticSourceReader> Create()
    {
        WindowsEventLogQueryRunner eventLog = new();
        return
        [
            new EventLogDiagnosticReader(DiagnosticSourceKind.SystemEventLog, eventLog),
            new EventLogDiagnosticReader(DiagnosticSourceKind.ApplicationEventLog, eventLog),
            new ReliabilityDiagnosticReader(new WmiReliabilityRecordSource()),
            new WindowsUpdateHistoryDiagnosticReader(new WuaHistorySource())
        ];
    }
}
