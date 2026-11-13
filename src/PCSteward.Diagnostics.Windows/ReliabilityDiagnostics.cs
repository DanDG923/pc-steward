using System.Globalization;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Diagnostics.Windows;

/// <summary>One allowlisted Reliability record source and event identifier, and how it is projected.</summary>
public sealed record ReliabilityMapping(string SourceName, int EventIdentifier, DiagnosticEventKind Kind, DiagnosticOutcomeKind Outcome);

/// <summary>The only Win32_ReliabilityRecords properties read. Message, user, computer and insertion strings are never requested.</summary>
public sealed record ReliabilityRawRecord(string? LogFile, long? RecordNumber, string? SourceName, long? EventIdentifier, string? TimeGenerated, string? ProductName);

/// <summary>
/// FR-DIA-01 (ADR-027 Q1): the closed Reliability allowlist, the one narrow time-filtered WQL query that applies it at the source,
/// and the projection (record identity and time, closed problem kind and the sanitized product name). No report files, device or
/// user details and no other properties are read. Exact source names are proven only by the separately approved host check.
/// </summary>
public static class ReliabilityDiagnosticCatalog
{
    public const string WmiClass = "Win32_ReliabilityRecords";

    public static IReadOnlyList<string> SelectedProperties { get; } = ["LogFile", "RecordNumber", "SourceName", "EventIdentifier", "TimeGenerated", "ProductName"];

    public static IReadOnlyList<ReliabilityMapping> Records { get; } =
    [
        new("Application Error", 1000, DiagnosticEventKind.ApplicationCrash, DiagnosticOutcomeKind.NotApplicable),
        new("Application Hang", 1002, DiagnosticEventKind.ApplicationHang, DiagnosticOutcomeKind.NotApplicable),
        new("Windows Error Reporting", 1001, DiagnosticEventKind.ErrorReport, DiagnosticOutcomeKind.NotApplicable),
        new("EventLog", 6008, DiagnosticEventKind.UnexpectedShutdown, DiagnosticOutcomeKind.NotApplicable),
        new("MsiInstaller", 11707, DiagnosticEventKind.SoftwareInstalled, DiagnosticOutcomeKind.Succeeded),
        new("MsiInstaller", 11708, DiagnosticEventKind.SoftwareInstalled, DiagnosticOutcomeKind.Failed),
        new("MsiInstaller", 11724, DiagnosticEventKind.SoftwareRemoved, DiagnosticOutcomeKind.Succeeded),
        new("Microsoft-Windows-WindowsUpdateClient", 19, DiagnosticEventKind.WindowsUpdate, DiagnosticOutcomeKind.Succeeded),
        new("Microsoft-Windows-WindowsUpdateClient", 20, DiagnosticEventKind.WindowsUpdate, DiagnosticOutcomeKind.Failed)
    ];

    /// <summary>One WQL query: the named properties only, the inclusive time window and the allowlist, all applied by WMI.</summary>
    public static string BuildQuery(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        string allowlist = string.Join(" OR ", Records.Select(record => string.Create(CultureInfo.InvariantCulture,
            $"(SourceName = '{record.SourceName}' AND EventIdentifier = {record.EventIdentifier})")));
        return $"SELECT {string.Join(", ", SelectedProperties)} FROM {WmiClass} WHERE TimeGenerated >= '{ToDmtf(fromUtc)}' AND TimeGenerated <= '{ToDmtf(toUtc)}' AND ({allowlist})";
    }

    public static string ToDmtf(DateTimeOffset value) => value.UtcDateTime.ToString("yyyyMMddHHmmss'.'ffffff", CultureInfo.InvariantCulture) + "+000";

    /// <summary>Parses a CIM datetime (<c>yyyyMMddHHmmss.ffffff±UUU</c>, offset in minutes) to an instant; anything else is unknown.</summary>
    public static DateTimeOffset? FromDmtf(string? value)
    {
        if (value is null || value.Length != 25 || value[14] != '.' || value[21] is not ('+' or '-')) return null;
        if (!DateTime.TryParseExact(value[..21], "yyyyMMddHHmmss'.'ffffff", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local)) return null;
        if (!int.TryParse(value.AsSpan(22, 3), NumberStyles.None, CultureInfo.InvariantCulture, out int minutes) || minutes > 14 * 60) return null;
        TimeSpan offset = TimeSpan.FromMinutes(value[21] == '-' ? -minutes : minutes);
        return new DateTimeOffset(local, offset);
    }

    public static DiagnosticRecordCandidate? Project(ReliabilityRawRecord raw, DateTimeOffset collectionStartUtc)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ReliabilityMapping? mapping = Records.FirstOrDefault(item =>
            string.Equals(item.SourceName, raw.SourceName, StringComparison.OrdinalIgnoreCase) && item.EventIdentifier == raw.EventIdentifier);
        if (mapping is null) return null;

        string? recordId = raw.RecordNumber is >= 0 && raw.LogFile is { Length: > 0 and <= 32 } log && log.All(char.IsAsciiLetter)
            ? string.Create(CultureInfo.InvariantCulture, $"{log}:{raw.RecordNumber}")
            : null;
        return new(DiagnosticSourceKind.Reliability, recordId, DiagnosticTimeNormalizer.FromInstant(FromDmtf(raw.TimeGenerated), collectionStartUtc),
            mapping.Kind, mapping.Outcome, EventId: mapping.EventIdentifier, Subject: raw.ProductName);
    }
}

/// <summary>What the Reliability route answered for one window: at most the requested number of records, and whether more matched.</summary>
public sealed record ReliabilityQueryAnswer(DiagnosticReaderStatus Status, IReadOnlyList<ReliabilityRawRecord> Records, bool MoreMatched);

/// <summary>
/// One bounded Reliability query for an inclusive window, built by <see cref="ReliabilityDiagnosticCatalog.BuildQuery"/>. It
/// returns at most the requested number of records; when more matched it says so (it may then return any of them, because WQL
/// cannot order). Every wait is bounded and cancellable.
/// </summary>
public interface IReliabilityRecordSource
{
    ReliabilityQueryAnswer Query(DateTimeOffset fromUtc, DateTimeOffset toUtc, int maximumRecords, CancellationToken cancellationToken);
}

/// <summary>
/// FR-DIA-01 (ADR-027 Q1, Codex review): the Reliability source keeps the newest matching records. WQL cannot order or limit, so
/// the reader asks for the newest part of the window first and keeps a window's records only when all of them fit in the slots
/// left; a window holding more is split in two at a whole microsecond (the CIM time resolution) and the newer half is asked
/// first. What is kept is therefore always every record in the newest stretch of the window. It stops when the slots are full and
/// a further record is proven, when a window can't be split further, or after <see cref="MaximumQueries"/> queries; the last two
/// report the source as truncated without ever keeping an older record in place of a newer one.
/// </summary>
public sealed class ReliabilityDiagnosticReader(IReliabilityRecordSource source) : IDiagnosticSourceReader
{
    public const int MaximumQueries = 32;
    private static readonly TimeSpan Resolution = TimeSpan.FromTicks(10);

    public DiagnosticSourceKind Source => DiagnosticSourceKind.Reliability;

    public Task<DiagnosticReaderResult> ReadAsync(DiagnosticReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        int slots = request.MaximumMatchingRecords;
        List<ReliabilityRawRecord> kept = [];
        Stack<(DateTimeOffset From, DateTimeOffset To)> windows = new();
        windows.Push((request.FromUtc, request.ToUtc));
        bool more = false;
        int queries = 0;
        while (windows.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queries == MaximumQueries) { more = true; break; }
            (DateTimeOffset from, DateTimeOffset to) = windows.Pop();
            int room = slots - kept.Count;
            ReliabilityQueryAnswer answer = source.Query(from, to, room, cancellationToken);
            queries++;
            if (answer.Status != DiagnosticReaderStatus.Completed) return Task.FromResult(new DiagnosticReaderResult(answer.Status, [], false));
            if (!answer.MoreMatched && answer.Records.Count <= room)
            {
                kept.AddRange(answer.Records);
                continue;
            }
            // More records than slots left: with no room this proves more matched; otherwise split, newer half first.
            if (room == 0 || to - from < Resolution + Resolution) { more = true; break; }
            DateTimeOffset middle = AlignDown(from + TimeSpan.FromTicks((to - from).Ticks / 2));
            if (middle <= from) middle = AlignDown(from) + Resolution;
            windows.Push((from, middle - Resolution));
            windows.Push((middle, to));
        }

        DiagnosticRecordCandidate[] records = [.. kept
            .Select(raw => ReliabilityDiagnosticCatalog.Project(raw, request.ToUtc) ?? DiagnosticRecordCandidate.Unreadable(Source))
            .OrderByDescending(record => record.Occurrence.Utc ?? DateTimeOffset.MinValue)
            .ThenByDescending(record => record.SourceRecordId, StringComparer.Ordinal)];
        return Task.FromResult(DiagnosticReaderResult.Completed(records, more));
    }

    private static DateTimeOffset AlignDown(DateTimeOffset value) => new(value.UtcTicks - (value.UtcTicks % Resolution.Ticks), TimeSpan.Zero);
}
