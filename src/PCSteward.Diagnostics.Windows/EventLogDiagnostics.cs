using System.Globalization;
using System.Security;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Diagnostics.Windows;

/// <summary>One allowlisted Windows event: its channel, provider, event ID and the closed kind it is projected as.</summary>
public sealed record EventLogMapping(string Channel, string Provider, int EventId, DiagnosticEventKind Kind);

/// <summary>The System values rendered for one event. Nothing else (message text, event data, user, computer) is read.</summary>
public sealed record EventLogRawEvent(string? Channel, string? Provider, int? EventId, ulong? RecordId, DateTime? TimeCreatedUtc, long? BugcheckCode);

/// <summary>
/// FR-DIA-01 (ADR-027 Q1): the closed System and Application event log allowlist, the source-side query that applies it with the
/// time window, and the projection to candidates. The query names provider and event ID together, so no event-ID-only filter is
/// used. Exact provider/version coverage is proven only by the separately approved host check.
/// </summary>
public static class EventLogDiagnosticCatalog
{
    public const string SystemChannel = "System";
    public const string ApplicationChannel = "Application";

    public static IReadOnlyList<EventLogMapping> SystemEvents { get; } =
    [
        new(SystemChannel, "Microsoft-Windows-Kernel-Power", 41, DiagnosticEventKind.UnexpectedShutdown),
        new(SystemChannel, "EventLog", 6008, DiagnosticEventKind.UnexpectedShutdown),
        new(SystemChannel, "Microsoft-Windows-WER-SystemErrorReporting", 1001, DiagnosticEventKind.BugCheck)
    ];

    public static IReadOnlyList<EventLogMapping> ApplicationEvents { get; } =
    [
        new(ApplicationChannel, "Application Error", 1000, DiagnosticEventKind.ApplicationCrash),
        new(ApplicationChannel, "Application Hang", 1002, DiagnosticEventKind.ApplicationHang),
        new(ApplicationChannel, "Windows Error Reporting", 1001, DiagnosticEventKind.ErrorReport)
    ];

    /// <summary>The only event data value read: Kernel-Power 41's numeric bugcheck code, reported as the result code.</summary>
    public const string BugcheckCodePath = "Event/EventData/Data[@Name='BugcheckCode']";

    /// <summary>The System values rendered for each event, in this order.</summary>
    public static IReadOnlyList<string> RenderedValuePaths { get; } =
    [
        "Event/System/Channel",
        "Event/System/Provider/@Name",
        "Event/System/EventID",
        "Event/System/EventRecordID",
        "Event/System/TimeCreated/@SystemTime",
        BugcheckCodePath
    ];

    public static IReadOnlyList<EventLogMapping> For(DiagnosticSourceKind source) => source switch
    {
        DiagnosticSourceKind.SystemEventLog => SystemEvents,
        DiagnosticSourceKind.ApplicationEventLog => ApplicationEvents,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    /// <summary>A structured query with one Select per allowlisted provider and event ID, each bounded by the inclusive time window.</summary>
    public static string BuildQuery(DiagnosticSourceKind source, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        IReadOnlyList<EventLogMapping> mappings = For(source);
        string channel = mappings[0].Channel;
        string from = Time(fromUtc);
        string to = Time(toUtc);
        System.Text.StringBuilder query = new();
        query.Append("<QueryList><Query Id=\"0\" Path=\"").Append(channel).Append("\">");
        foreach (EventLogMapping mapping in mappings)
        {
            query.Append("<Select Path=\"").Append(channel).Append("\">*[System[Provider[@Name='").Append(SecurityElement.Escape(mapping.Provider))
                .Append("'] and (EventID=").Append(mapping.EventId.ToString(CultureInfo.InvariantCulture))
                .Append(") and TimeCreated[@SystemTime&gt;='").Append(from).Append("' and @SystemTime&lt;='").Append(to).Append("']]]</Select>");
        }

        return query.Append("</Query></QueryList>").ToString();
    }

    /// <summary>Projects one rendered event, or returns null when it is not exactly an allowlisted channel, provider and event ID.</summary>
    public static DiagnosticRecordCandidate? Project(DiagnosticSourceKind source, EventLogRawEvent raw, DateTimeOffset collectionStartUtc)
    {
        ArgumentNullException.ThrowIfNull(raw);
        EventLogMapping? mapping = For(source).FirstOrDefault(item =>
            string.Equals(item.Channel, raw.Channel, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Provider, raw.Provider, StringComparison.OrdinalIgnoreCase) &&
            item.EventId == raw.EventId);
        if (mapping is null) return null;

        DateTimeOffset? occurred = raw.TimeCreatedUtc is { } time ? new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc)) : null;
        long? code = mapping.Kind == DiagnosticEventKind.UnexpectedShutdown && mapping.EventId == 41 && raw.BugcheckCode is > 0 ? raw.BugcheckCode : null;
        string? recordId = raw.RecordId is { } id ? string.Create(CultureInfo.InvariantCulture, $"{mapping.Channel}:{id}") : null;
        return new(source, recordId, DiagnosticTimeNormalizer.FromInstant(occurred, collectionStartUtc), mapping.Kind, DiagnosticOutcomeKind.NotApplicable,
            EventId: mapping.EventId, ResultCode: code);
    }

    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
}

/// <summary>What the event log route answered: a closed status and at most the requested number of rendered events, newest first.</summary>
public sealed record EventLogQueryAnswer(DiagnosticReaderStatus Status, IReadOnlyList<EventLogRawEvent> Events, bool MoreMatched);

/// <summary>Runs one structured query, newest first, and renders only <see cref="EventLogDiagnosticCatalog.RenderedValuePaths"/>.</summary>
public interface IEventLogQueryRunner
{
    EventLogQueryAnswer Run(string channel, string structuredQuery, int maximumEvents, CancellationToken cancellationToken);
}

/// <summary>FR-DIA-01: the System or Application event log source. Read only and unelevated; no message text or event data beyond the bugcheck code.</summary>
public sealed class EventLogDiagnosticReader : IDiagnosticSourceReader
{
    private readonly IEventLogQueryRunner runner;

    public EventLogDiagnosticReader(DiagnosticSourceKind source, IEventLogQueryRunner runner)
    {
        if (source is not (DiagnosticSourceKind.SystemEventLog or DiagnosticSourceKind.ApplicationEventLog)) throw new ArgumentOutOfRangeException(nameof(source));
        Source = source;
        this.runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public DiagnosticSourceKind Source { get; }

    public Task<DiagnosticReaderResult> ReadAsync(DiagnosticReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        string channel = EventLogDiagnosticCatalog.For(Source)[0].Channel;
        EventLogQueryAnswer answer = runner.Run(channel, EventLogDiagnosticCatalog.BuildQuery(Source, request.FromUtc, request.ToUtc), request.MaximumMatchingRecords, cancellationToken);
        if (answer.Status != DiagnosticReaderStatus.Completed) return Task.FromResult(new DiagnosticReaderResult(answer.Status, [], false));

        List<DiagnosticRecordCandidate> records = [];
        foreach (EventLogRawEvent raw in answer.Events.Take(request.MaximumMatchingRecords))
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiagnosticRecordCandidate? candidate = EventLogDiagnosticCatalog.Project(Source, raw, request.ToUtc);
            // An event outside the allowlist or one that could not be rendered is passed on as unreadable, so admission counts it as rejected.
            records.Add(candidate ?? DiagnosticRecordCandidate.Unreadable(Source));
        }

        return Task.FromResult(DiagnosticReaderResult.Completed(records, answer.MoreMatched || answer.Events.Count > request.MaximumMatchingRecords));
    }
}
