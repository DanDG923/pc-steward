using System.Globalization;
using System.Text.Json;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>One source the owner can choose on the collection consent dialog: its name and exactly what it reads.</summary>
public sealed record DiagnosticsSourceChoice(DiagnosticSourceKind Source, string Label, string Reads);

/// <summary>One saved timeline as listed on the Diagnostics page.</summary>
public sealed record DiagnosticsSnapshotRow(Guid Id, string Title, string Summary, string AccessibleName, bool IsPartial, bool HasCoverage);

/// <summary>One source's result in words, with the tone of its icon.</summary>
public sealed record DiagnosticsCoverageLine(DiagnosticSourceKind Source, string Label, string Result, StatusTone Tone);

/// <summary>A PC Steward change near the selected record, in words ("Updated Firefox 128.0 to 129.0, 4 minutes before").</summary>
public sealed record DiagnosticsNearbyLine(string Text);

/// <summary>
/// One timeline record: icon and tone, a heading in words, one sentence, its time and source, the facts behind its expander, the
/// fixed notes it must carry, and the PC Steward changes recorded near it.
/// </summary>
public sealed record DiagnosticsEntry(
    string Heading,
    string Summary,
    string TimeText,
    string SourceLabel,
    string Glyph,
    StatusTone Tone,
    IReadOnlyList<DetailFact> Facts,
    IReadOnlyList<string> Notes,
    IReadOnlyList<DiagnosticsNearbyLine> Nearby)
{
    public string AccessibleName => $"{Heading}. {SourceLabel}. {TimeText}";
}

/// <summary>One day of the timeline, newest day first.</summary>
public sealed record DiagnosticsDay(string Heading, IReadOnlyList<DiagnosticsEntry> Entries);

/// <summary>A loaded snapshot as the page shows it.</summary>
public sealed record DiagnosticsTimelinePresentation(
    string Heading,
    string Summary,
    string? CoverageWarning,
    IReadOnlyList<DiagnosticsCoverageLine> Coverage,
    IReadOnlyList<DiagnosticsDay> Days,
    IReadOnlyList<DiagnosticsEntry> Untimed,
    string? DuplicatesNote,
    int RecordCount);

/// <summary>What the export preview says before the owner chooses where to save.</summary>
public sealed record DiagnosticsExportPreview(string Title, IReadOnlyList<string> Lines, string Warning, string SuggestedFileName, bool CanExport, string? RefusalReason);

/// <summary>
/// FR-DIA-01–05, FR-UI-01 (Task 64 stage 4, ADR-027): everything the Diagnostics page says, portable and testable. It starts no work
/// and reads nothing; the page asks for consent each time, and a timeline only ever shows records already in a saved snapshot.
/// Wording never claims a cause, a health score or that the PC is fine: nearby only means nearby, and no coverage is never "clean".
/// </summary>
public static class DiagnosticsPresenter
{
    public const string Title = "Diagnostics";

    public const string PageDescription =
        "A short record of what Windows logged in the last seven days, next to what PC Steward changed. It is collected only when you ask, kept on this PC for up to 30 days and never sent anywhere.";

    public const string CollectLabel = "Collect a timeline…";
    public const string CollectHelp = "(asks first; reads Windows records without administrator permission and changes nothing)";
    public const string CollectingText = "Collecting the timeline. This takes up to a minute; nothing is changed.";
    public const string CancelCollectLabel = "Stop collecting";
    public const string ExportLabel = "Export…";
    public const string ExportHelp = "(shows what the file will contain, then asks where to save it)";
    public const string SavedHeading = "Saved timelines";
    public const string CoverageHeading = "What was read";
    public const string UntimedHeading = "Records without a usable time";
    public const string UntimedNote = "Windows did not give these a time PC Steward can place safely, so they are listed apart rather than guessed.";
    public const string NearbyHeading = DiagnosticNearbyChange.Label;

    public const string EmptyTitle = "No timeline yet";
    public const string EmptyText = "Collect one when something went wrong, such as a crash or a PC that restarted by itself. It shows Windows' own records next to the updates PC Steward made.";

    public const string ConsentTitle = "Collect a diagnostic timeline?";
    public const string ConsentIntro = "PC Steward reads these records from the last 7 days, once, as you, without administrator permission. It changes nothing on this PC.";
    public const string ConsentNotRead = "It never reads message text, user or computer names, full file paths or command lines. Your account name and this PC's name are replaced before anything is saved.";
    public const string ConsentKeep = "The timeline is kept on this PC for 30 days, or less if space runs out, until you clear scan data or reset PC Steward. Nothing is sent anywhere.";
    public const string ConsentConfirm = "Collect";
    public const string ConsentNoneSelected = "Choose at least one source.";

    public const string NoCoverageWarning = "No source could be read, so this timeline says nothing about how the PC is doing. It is not a clean result.";
    public const string PartialWarning = "Some sources were not read in full. The timeline shows what was read; the list below says what is missing and why.";

    public const string CancelledText = "Collection stopped. Nothing new was saved; any earlier timeline is unchanged.";
    public const string SaveFailedText = "PC Steward collected the timeline but couldn't save it. Nothing was changed; any earlier timeline is unchanged.";
    public const string CapacityHeldText = "The timeline was not saved: the timeline you are viewing already uses the space allowed. Close it and try again.";
    public const string UnreadableText = "This saved timeline couldn't be read in full, so none of it is shown. Collect a new one.";
    public const string UnavailableText = "This timeline is no longer kept. Timelines are removed after 30 days, when space runs out, or when scan data is cleared.";

    public const string ExportWarning = "The file can include app names, update titles and Windows error codes. Names of people and this PC were replaced, but check the file before you share it.";

    /// <summary>The sources in their closed collection order, PC Steward's own history first.</summary>
    public static IReadOnlyList<DiagnosticsSourceChoice> SourceChoices { get; } =
    [
        new(DiagnosticSourceKind.PcStewardHistory, SourceLabel(DiagnosticSourceKind.PcStewardHistory), "The updates PC Steward made: app, versions, result and exit code."),
        new(DiagnosticSourceKind.SystemEventLog, SourceLabel(DiagnosticSourceKind.SystemEventLog), "Unexpected shutdowns and blue screens: the event number, time and stop code only."),
        new(DiagnosticSourceKind.ApplicationEventLog, SourceLabel(DiagnosticSourceKind.ApplicationEventLog), "App crashes, hangs and error reports: the event number and time only."),
        new(DiagnosticSourceKind.Reliability, SourceLabel(DiagnosticSourceKind.Reliability), "Reliability Monitor's crashes, installs, removals and Windows updates: the event, time and product name."),
        new(DiagnosticSourceKind.WindowsUpdateHistory, SourceLabel(DiagnosticSourceKind.WindowsUpdateHistory), "Windows Update's own history: update title, result and error code.")
    ];

    public static string SourceLabel(DiagnosticSourceKind source) => source switch
    {
        DiagnosticSourceKind.PcStewardHistory => "PC Steward update history",
        DiagnosticSourceKind.SystemEventLog => "Windows System log",
        DiagnosticSourceKind.ApplicationEventLog => "Windows Application log",
        DiagnosticSourceKind.Reliability => "Reliability Monitor",
        DiagnosticSourceKind.WindowsUpdateHistory => "Windows Update history",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    public static string KindLabel(DiagnosticEventKind kind) => kind switch
    {
        DiagnosticEventKind.PcStewardUpdate => "PC Steward update",
        DiagnosticEventKind.UnexpectedShutdown => "PC didn't shut down cleanly",
        DiagnosticEventKind.BugCheck => "Windows stopped with an error (blue screen)",
        DiagnosticEventKind.ApplicationCrash => "App crashed",
        DiagnosticEventKind.ApplicationHang => "App stopped responding",
        DiagnosticEventKind.ErrorReport => "Error report",
        DiagnosticEventKind.SoftwareInstalled => "Software installed",
        DiagnosticEventKind.SoftwareRemoved => "Software removed",
        DiagnosticEventKind.WindowsUpdate => "Windows update",
        DiagnosticEventKind.OtherReliabilityProblem => "Other problem",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string OutcomeLabel(DiagnosticOutcomeKind outcome) => outcome switch
    {
        DiagnosticOutcomeKind.NotApplicable => "",
        DiagnosticOutcomeKind.Succeeded => "Succeeded",
        DiagnosticOutcomeKind.Failed => "Failed",
        DiagnosticOutcomeKind.RestartPending => "Needs a restart",
        DiagnosticOutcomeKind.Cancelled => "Cancelled",
        DiagnosticOutcomeKind.InProgress => "Not finished",
        DiagnosticOutcomeKind.Unknown => "Result unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome))
    };

    /// <summary>A source's result in words. Every source is listed, including the ones the owner left out.</summary>
    public static DiagnosticsCoverageLine Coverage(DiagnosticSourceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        string kept = DisplayFormat.Plural(result.RecordsKept, "record");
        (string text, StatusTone tone) = result.Outcome switch
        {
            DiagnosticSourceOutcome.Available => (kept, StatusTone.Success),
            DiagnosticSourceOutcome.Empty => ("Nothing recorded in these seven days", StatusTone.Success),
            DiagnosticSourceOutcome.Partial => ($"{kept}, not all of them: {PartialReason(result)}", StatusTone.Attention),
            DiagnosticSourceOutcome.Denied => ("Not read: Windows did not allow it for this account", StatusTone.Attention),
            DiagnosticSourceOutcome.Unsupported => ("Not read: not available on this PC", StatusTone.Neutral),
            DiagnosticSourceOutcome.TimedOut => ("Not read: it took too long, so it was stopped", StatusTone.Attention),
            DiagnosticSourceOutcome.Cancelled => ("Not read: collection was stopped", StatusTone.Neutral),
            DiagnosticSourceOutcome.Failed => ("Not read: something went wrong reading it", StatusTone.Attention),
            DiagnosticSourceOutcome.NotSelected => ("Not read: you left it out", StatusTone.Neutral),
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
        return new(result.Source, SourceLabel(result.Source), text, tone);
    }

    private static string PartialReason(DiagnosticSourceResult result) => result.Reason switch
    {
        DiagnosticSourceReason.SourceRecordLimit => $"only the newest {result.AllowedRecords} are kept",
        DiagnosticSourceReason.CollectionRecordLimit => "the timeline reached its record limit",
        DiagnosticSourceReason.CollectionByteLimit => "the timeline reached its size limit",
        DiagnosticSourceReason.InvalidRecordsRejected => DisplayFormat.Plural(result.RecordsRejected, "record") + " couldn't be read safely and were left out",
        DiagnosticSourceReason.ReaderDeadline or DiagnosticSourceReason.CollectionDeadline => "it ran out of time",
        _ => "some records were left out"
    };

    /// <summary>One saved timeline in the list: when it was collected, how much it holds and until when it is kept.</summary>
    public static DiagnosticsSnapshotRow SnapshotRow(DiagnosticSnapshotSummary summary, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        string title = "Collected " + DisplayFormat.DayAndTime(summary.CollectedAtUtc, now, culture, zone);
        int read = summary.Sources.Count(source => source.Outcome is DiagnosticSourceOutcome.Available or DiagnosticSourceOutcome.Empty or DiagnosticSourceOutcome.Partial);
        string text = $"{DisplayFormat.Plural(summary.RecordCount, "record")} from {read} of {summary.Sources.Count} sources · kept until {DisplayFormat.Date(summary.ExpiresAtUtc, culture, zone)}";
        if (!summary.HasCoverage) text += " · no source could be read";
        else if (summary.IsPartial) text += " · partial";
        return new(summary.Id, title, text, $"{title}. {text}", summary.IsPartial, summary.HasCoverage);
    }

    /// <summary>
    /// FR-DIA-02: the deterministic timeline of a loaded snapshot, newest day first and newest record first inside a day. Records
    /// without a usable time are listed apart. A known-time incident lists the PC Steward changes within 15 minutes of it.
    /// </summary>
    public static DiagnosticsTimelinePresentation Timeline(DiagnosticSnapshot snapshot, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        DiagnosticTimeline timeline = DiagnosticTimelineBuilder.Build(snapshot);
        List<DiagnosticsDay> days = [];
        foreach (IGrouping<DateOnly, DiagnosticRecord> day in timeline.Timed.Reverse().GroupBy(record => DisplayFormat.LocalDay(record.Occurrence.Utc!.Value, zone)))
            days.Add(new(DisplayFormat.DayHeading(day.Key, now, culture, zone), [.. day.Select(record => Entry(timeline, record, culture, zone))]));
        List<DiagnosticsEntry> untimed = [.. timeline.Untimed.Select(record => Entry(timeline, record, culture, zone))];
        IReadOnlyList<DiagnosticsCoverageLine> coverage = [.. snapshot.Sources.OrderBy(source => source.Source).Select(Coverage)];
        int read = snapshot.Sources.Count(source => source.Outcome is DiagnosticSourceOutcome.Available or DiagnosticSourceOutcome.Empty or DiagnosticSourceOutcome.Partial);
        int shown = timeline.Timed.Count + timeline.Untimed.Count;
        int sourceCount = snapshot.Sources.Count;
        string fromText = DisplayFormat.Date(snapshot.LookbackFromUtc, culture, zone), toText = DisplayFormat.Date(snapshot.CollectedAtUtc, culture, zone), keptText = DisplayFormat.Date(snapshot.ExpiresAtUtc, culture, zone);
        string summary = $"{DisplayFormat.Plural(shown, "record")} from {read} of {sourceCount} sources, {fromText} to {toText} · kept until {keptText}";
        string? warning = !snapshot.HasCoverage ? NoCoverageWarning : snapshot.IsPartial ? PartialWarning : null;
        string? duplicates = timeline.DuplicatesRemoved == 0 ? null : $"{DisplayFormat.Plural(timeline.DuplicatesRemoved, "repeated record")} from the same source shown once.";
        string heading = "Timeline collected " + DisplayFormat.DayAndTime(snapshot.CollectedAtUtc, now, culture, zone);
        return new(heading, summary, warning, coverage, days, untimed, duplicates, shown);
    }

    private static DiagnosticsEntry Entry(DiagnosticTimeline timeline, DiagnosticRecord record, CultureInfo? culture, TimeZoneInfo? zone)
    {
        string kind = KindLabel(record.Kind);
        string heading = string.IsNullOrWhiteSpace(record.Subject) ? kind : $"{kind}: {record.Subject}";
        string outcome = OutcomeLabel(record.Outcome);
        List<string> sentence = [];
        if (!string.IsNullOrEmpty(outcome)) sentence.Add(outcome);
        if (!string.IsNullOrWhiteSpace(record.Detail)) sentence.Add(record.Detail);
        string summary = sentence.Count == 0 ? SourceLabel(record.Source) : string.Join(" · ", sentence);

        string time = record.Occurrence.Utc is { } utc ? DisplayFormat.Time(utc, culture, zone) : "Time unknown";
        List<DetailFact> facts = [new("Source", SourceLabel(record.Source))];
        if (record.Occurrence.Utc is { } at) facts.Add(new("Recorded at", DisplayFormat.DateTime(at, culture, zone)));
        string? quality = record.Occurrence.Quality switch
        {
            DiagnosticTimeQuality.Future => "Later than the collection itself; the PC's clock may have changed",
            DiagnosticTimeQuality.Conflicting => "The source gave conflicting times",
            DiagnosticTimeQuality.Ambiguous => "Falls in a daylight-saving change, so it can't be placed",
            DiagnosticTimeQuality.Unknown => "Not recorded",
            _ => null
        };
        if (quality is not null) facts.Add(new("Time", quality));
        if (record.EventId is { } eventId) facts.Add(new("Event ID", eventId.ToString(CultureInfo.InvariantCulture)));
        if (record.ResultCode is { } code)
            facts.Add(record.Source == DiagnosticSourceKind.PcStewardHistory ? new("Exit code", code.ToString(CultureInfo.InvariantCulture)) : new("Code", ResultCodeText(code)));
        if (!string.IsNullOrWhiteSpace(record.Summary)) facts.Add(new("Recorded text", record.Summary + (record.TextTruncated ? " (shortened)" : "")));
        facts.Add(new("Record", record.SourceRecordId));

        List<string> notes = [];
        if (record.Kind == DiagnosticEventKind.UnexpectedShutdown) notes.Add(DiagnosticTimelineBuilder.UnexpectedShutdownNote);
        List<DiagnosticsNearbyLine> nearby = [];
        if (record.Source != DiagnosticSourceKind.PcStewardHistory)
            foreach (DiagnosticNearbyChange change in DiagnosticTimelineBuilder.NearbyChanges(timeline, record.Key))
                nearby.Add(new(NearbyText(change)));
        if (nearby.Count != 0) notes.Add(DiagnosticTimelineBuilder.ProximityNote);

        (string glyph, StatusTone tone) = record.Kind switch
        {
            DiagnosticEventKind.PcStewardUpdate => ("", record.Outcome == DiagnosticOutcomeKind.Failed ? StatusTone.Critical : StatusTone.Information),
            DiagnosticEventKind.UnexpectedShutdown or DiagnosticEventKind.BugCheck => ("", StatusTone.Critical),
            DiagnosticEventKind.ApplicationCrash or DiagnosticEventKind.ApplicationHang or DiagnosticEventKind.ErrorReport or DiagnosticEventKind.OtherReliabilityProblem => ("", StatusTone.Attention),
            DiagnosticEventKind.WindowsUpdate => ("", record.Outcome == DiagnosticOutcomeKind.Failed ? StatusTone.Attention : StatusTone.Neutral),
            _ => ("", StatusTone.Neutral)
        };
        return new(heading, summary, time, SourceLabel(record.Source), glyph, tone, facts, notes, nearby);
    }

    private static string NearbyText(DiagnosticNearbyChange change)
    {
        DiagnosticRecord update = change.Change;
        string what = string.IsNullOrWhiteSpace(update.Subject) ? "An app" : update.Subject;
        string versions = string.IsNullOrWhiteSpace(update.Detail) ? "" : $" {update.Detail}";
        string outcome = OutcomeLabel(update.Outcome);
        int minutes = (int)Math.Round(Math.Abs(change.OffsetFromIncident.TotalMinutes), MidpointRounding.AwayFromZero);
        string when = minutes == 0 ? "at about the same time" : $"{DisplayFormat.Plural(minutes, "minute")} {(change.OffsetFromIncident < TimeSpan.Zero ? "before" : "after")}";
        return $"PC Steward updated {what}{versions}{(string.IsNullOrEmpty(outcome) ? "" : $" ({outcome.ToLowerInvariant()})")}, {when}";
    }

    /// <summary>A Windows result code as Windows shows it: 0x followed by eight hex digits for an HRESULT-sized value.</summary>
    public static string ResultCodeText(long code) =>
        code is >= int.MinValue and <= uint.MaxValue ? "0x" + unchecked((uint)code).ToString("X8", CultureInfo.InvariantCulture) : code.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// FR-DIA-03: what the page says after a save. Null when the timeline was saved and nothing else changed; older timelines removed
    /// to make room are disclosed, because that shortens their 30 days.
    /// </summary>
    public static string? SaveNote(DiagnosticSnapshotSaveResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Saved) return result.Failure == DiagnosticSnapshotSaveFailure.CapacityHeldByProtectedSnapshots ? CapacityHeldText : SaveFailedText;
        return result.PrunedForCapacity == 0 ? null
            : $"{DisplayFormat.Plural(result.PrunedForCapacity, "older timeline")} {(result.PrunedForCapacity == 1 ? "was" : "were")} removed to make room, before {(result.PrunedForCapacity == 1 ? "its" : "their")} 30 days were up.";
    }

    /// <summary>
    /// FR-DIA-05 (ADR-027 Export): what the export file will hold, said before the owner picks a destination. A snapshot over the
    /// 8 MiB export ceiling is refused rather than cut.
    /// </summary>
    public static DiagnosticsExportPreview ExportPreview(DiagnosticSnapshot snapshot, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string records = DisplayFormat.Plural(snapshot.Records.Count, "record"), collected = DisplayFormat.DateTime(snapshot.CollectedAtUtc, culture, zone);
        List<string> lines = [$"{records}, exactly as shown on this page, collected {collected}."];
        foreach (DiagnosticSourceResult source in snapshot.Sources.OrderBy(source => source.Source))
        {
            DiagnosticsCoverageLine line = Coverage(source);
            lines.Add($"{line.Label}: {line.Result}");
        }
        lines.Add("No message text, user or computer names, full file paths or command lines. One JSON file; nothing is sent anywhere.");
        long size = DiagnosticSnapshotExport.Serialize(snapshot).LongLength;
        bool fits = size <= DiagnosticCollectionPolicy.MaximumCollectionBytes;
        string name = "pc-steward-diagnostics-" + DisplayFormat.LocalDay(snapshot.CollectedAtUtc, zone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json";
        return new("Export this timeline?", lines, ExportWarning, name, fits, fits ? null : "This timeline is larger than the 8 MB export limit, so it can't be exported.");
    }
}

/// <summary>
/// FR-DIA-01/03 (Task 64 stage 4): what the Diagnostics page needs from the composition root: the snapshot store and a fresh
/// collection for each consent. Nothing here starts on its own; there is no scheduled or resident collector.
/// </summary>
public sealed class DiagnosticsServices(IDiagnosticSnapshotStore store, Func<DiagnosticCollectionOrchestrator> createCollection)
{
    public IDiagnosticSnapshotStore Store { get; } = store ?? throw new ArgumentNullException(nameof(store));

    private readonly Func<DiagnosticCollectionOrchestrator> create = createCollection ?? throw new ArgumentNullException(nameof(createCollection));

    public DiagnosticCollectionOrchestrator CreateCollection() => create();
}

/// <summary>
/// FR-DIA-05 (ADR-027 Export): the one export format, a UTF-8 JSON projection of a whole saved snapshot. Each record is written
/// with the same canonical projection used for storage and size, so the file holds exactly what the page shows: no raw log,
/// message, path, account or computer name. It is built only when the owner asks and saved only where the owner chooses.
/// </summary>
public static class DiagnosticSnapshotExport
{
    public const string Format = "pc-steward-diagnostic-timeline";
    public const int FormatVersion = 1;

    public static byte[] Serialize(DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("formatVersion", FormatVersion);
            writer.WriteNumber("policyVersion", snapshot.PolicyVersion);
            writer.WriteString("collectedAtUtc", Utc(snapshot.CollectedAtUtc));
            writer.WriteString("lookbackFromUtc", Utc(snapshot.LookbackFromUtc));
            writer.WriteString("expiresAtUtc", Utc(snapshot.ExpiresAtUtc));
            writer.WriteStartArray("sources");
            foreach (DiagnosticSourceResult source in snapshot.Sources.OrderBy(source => source.Source))
            {
                writer.WriteStartObject();
                writer.WriteString("source", source.Source.ToString());
                writer.WriteString("outcome", source.Outcome.ToString());
                writer.WriteString("reason", source.Reason.ToString());
                writer.WriteString("queriedFromUtc", Utc(source.QueriedFromUtc));
                writer.WriteString("queriedToUtc", Utc(source.QueriedToUtc));
                writer.WriteNumber("allowedRecords", source.AllowedRecords);
                writer.WriteNumber("matchingRecordsReturned", source.MatchingRecordsReturned);
                writer.WriteNumber("recordsKept", source.RecordsKept);
                writer.WriteNumber("recordsRejected", source.RecordsRejected);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("records");
            foreach (DiagnosticRecord record in snapshot.Records) DiagnosticRecordJson.Write(writer, record);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
