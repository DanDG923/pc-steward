using System.Globalization;
using System.Text;

namespace PCSteward.Domain;

/// <summary>
/// FR-AI-01 (ADR-027 F5, ADR-028): the two closed things an explanation can be about. The kind decides which clear removes a
/// saved explanation: Clear scan data removes <see cref="DiagnosticTimeline"/>, Clear update history removes <see cref="FailedUpdate"/>.
/// </summary>
public enum AdvisoryAiSourceKind
{
    FailedUpdate,
    DiagnosticTimeline
}

/// <summary>FR-AI-02: the closed role of one evidence item. It is set by PC Steward and shown to the owner and the model; an edit never changes it.</summary>
public enum AdvisoryAiEvidenceRole
{
    SelectedIncident,
    NearbyPcStewardChange,
    OtherNearbyRecord,
    Coverage,
    FailedUpdateSummary,
    FailedUpdateEvent
}

/// <summary>FR-AI-02: one redacted, editable evidence item.</summary>
public sealed record AdvisoryAiEvidenceItem(AdvisoryAiEvidenceRole Role, string Text, bool TextTruncated, bool EditedByOwner = false);

/// <summary>FR-AI-02 (ADR-028 R4): how many items of one role were left out of the draft to fit the input limit.</summary>
public sealed record AdvisoryAiOmission(AdvisoryAiEvidenceRole Role, int Count);

/// <summary>Why a draft could not be made or changed.</summary>
public enum AdvisoryAiDraftProblem
{
    None,
    IncidentNotFound,
    IncidentHasNoKnownTime,
    RequiredEvidenceDoesNotFit,
    NoEvidenceLeft,
    EditIsEmpty,
    NoSuchItem
}

/// <summary>
/// FR-AI-02 (ADR-028 consent item 2 and R4): the editable draft of exactly what may leave the PC. It carries only redacted text,
/// the closed roles, the interval and what was left out. <see cref="OriginId"/> is the app-local identity of what is explained
/// (a job, or a snapshot and record); it is never sent. Every change makes a new draft with a higher <see cref="Version"/>.
/// </summary>
public sealed record AdvisoryAiEvidenceDraft(
    AdvisoryAiSourceKind Source,
    string OriginId,
    IReadOnlyList<AdvisoryAiEvidenceItem> Items,
    IReadOnlyList<AdvisoryAiOmission> Omitted,
    DateTimeOffset? IntervalFromUtc,
    DateTimeOffset? IntervalToUtc,
    int Version)
{
    /// <summary>The longest text one evidence item may hold, after redaction.</summary>
    public const int MaximumItemScalars = 2048;

    /// <summary>
    /// ADR-028 consent item 4: replaces one item's text with the owner's edit, redacted again exactly as generated text is. The role
    /// is kept. An edit that redacts to nothing is refused; the owner removes the item instead.
    /// </summary>
    public AdvisoryAiDraftProblem TryEdit(int index, string? text, DiagnosticRedactionContext? context, out AdvisoryAiEvidenceDraft edited)
    {
        edited = this;
        if (index < 0 || index >= Items.Count) return AdvisoryAiDraftProblem.NoSuchItem;
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(text, MaximumItemScalars, context);
        if (clean.Value is null) return AdvisoryAiDraftProblem.EditIsEmpty;
        AdvisoryAiEvidenceItem[] items = [.. Items];
        items[index] = items[index] with { Text = clean.Value, TextTruncated = clean.Truncated, EditedByOwner = true };
        edited = this with { Items = items, Version = checked(Version + 1) };
        return AdvisoryAiDraftProblem.None;
    }

    /// <summary>ADR-028 consent item 4: removes one item. At least one item must remain; removed items are not counted as omitted, because the owner chose to leave them out.</summary>
    public AdvisoryAiDraftProblem TryRemove(int index, out AdvisoryAiEvidenceDraft edited)
    {
        edited = this;
        if (index < 0 || index >= Items.Count) return AdvisoryAiDraftProblem.NoSuchItem;
        if (Items.Count == 1) return AdvisoryAiDraftProblem.NoEvidenceLeft;
        edited = this with { Items = [.. Items.Where((_, position) => position != index)], Version = checked(Version + 1) };
        return AdvisoryAiDraftProblem.None;
    }
}

/// <summary>
/// FR-AI-02 (ADR-028 R4): how much input text a draft may use, worked out from the input token limit, the model's framing allowance
/// and the fixed instructions. Text is measured in UTF-8 bytes, which bound its tokens (see <see cref="AdvisoryAiModelProfile.TokenizerIsByteLevel"/>).
/// </summary>
public readonly record struct AdvisoryAiInputAllowance(int InputTextBytes)
{
    public static AdvisoryAiInputAllowance For(AdvisoryAiLimits limits, AdvisoryAiModelProfile model, string instructions)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(instructions);
        long left = (long)limits.MaximumInputTokens - model.FramingAllowanceTokens - Encoding.UTF8.GetByteCount(instructions);
        return new((int)Math.Clamp(left, 0, int.MaxValue));
    }

    public bool Fits(AdvisoryAiEvidenceDraft draft) => AdvisoryAiInputText.ByteCount(draft) <= InputTextBytes;
}

/// <summary>
/// FR-AI-02 (ADR-028 R4): makes the default draft for a diagnostic explanation. The selected incident and a summary of what each
/// source returned always come first; then PC Steward changes within 15 minutes; then other records within 15 minutes, in frozen
/// timeline order. Items are taken in that order until the next one does not fit, and everything left out is counted by role.
/// If the incident and the coverage summary alone do not fit, there is no draft.
/// </summary>
public static class AdvisoryAiDiagnosticDraftBuilder
{
    public static AdvisoryAiDraftProblem TryBuild(DiagnosticTimeline timeline, DiagnosticRecordKey incident, AdvisoryAiInputAllowance allowance,
        DiagnosticRedactionContext? context, out AdvisoryAiEvidenceDraft? draft)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        draft = null;
        DiagnosticRecord? selected = timeline.Timed.FirstOrDefault(record => record.Key == incident) ??
            timeline.Untimed.FirstOrDefault(record => record.Key == incident);
        if (selected is null) return AdvisoryAiDraftProblem.IncidentNotFound;
        if (!selected.Occurrence.CanLabelProximity) return AdvisoryAiDraftProblem.IncidentHasNoKnownTime;

        DateTimeOffset at = selected.Occurrence.Utc!.Value;
        DateTimeOffset from = at - DiagnosticCollectionPolicy.ProximityWindow;
        DateTimeOffset to = at + DiagnosticCollectionPolicy.ProximityWindow;
        IReadOnlyList<DiagnosticNearbyChange> changes = DiagnosticTimelineBuilder.NearbyChanges(timeline, incident);
        HashSet<DiagnosticRecordKey> changeKeys = [.. changes.Select(change => change.Change.Key)];
        DiagnosticRecord[] others = [.. timeline.Timed.Where(record =>
            record.Key != incident && !changeKeys.Contains(record.Key) && record.Source != DiagnosticSourceKind.PcStewardHistory &&
            record.Occurrence.CanLabelProximity && (record.Occurrence.Utc!.Value - at).Duration() <= DiagnosticCollectionPolicy.ProximityWindow)];

        List<AdvisoryAiEvidenceItem> required = [Item(AdvisoryAiEvidenceRole.SelectedIncident, DescribeRecord(selected), context),
            Item(AdvisoryAiEvidenceRole.Coverage, DescribeCoverage(timeline.Snapshot), context)];
        List<AdvisoryAiEvidenceItem> optional = [.. changes.Select(change => Item(AdvisoryAiEvidenceRole.NearbyPcStewardChange, DescribeRecord(change.Change), context)),
            .. others.Select(record => Item(AdvisoryAiEvidenceRole.OtherNearbyRecord, DescribeRecord(record), context))];
        string origin = string.Create(CultureInfo.InvariantCulture, $"{timeline.Snapshot.Id:D}/{incident.Source}/{incident.SourceRecordId}");
        return Fit(AdvisoryAiSourceKind.DiagnosticTimeline, origin, required, optional, from, to, allowance, out draft);
    }

    /// <summary>Takes the required items, then optional items in order until the first that does not fit; counts the rest by role.</summary>
    internal static AdvisoryAiDraftProblem Fit(AdvisoryAiSourceKind source, string origin, IReadOnlyList<AdvisoryAiEvidenceItem> required,
        IReadOnlyList<AdvisoryAiEvidenceItem> optional, DateTimeOffset? from, DateTimeOffset? to, AdvisoryAiInputAllowance allowance, out AdvisoryAiEvidenceDraft? draft)
    {
        draft = null;
        // The canonical text is an object whose evidence array holds each item's own object, comma separated. So its size is the
        // size with no items plus each item's size plus the commas: linear, however many records fall in the window.
        AdvisoryAiEvidenceDraft empty = new(source, origin, [], [], from, to, 1);
        int emptyBytes = AdvisoryAiInputText.ByteCount(empty);
        long Size(AdvisoryAiEvidenceItem item) => AdvisoryAiInputText.ByteCount(empty with { Items = [item] }) - emptyBytes;
        long Total(long itemBytes, int itemCount, int takenOptional) =>
            AdvisoryAiInputText.ByteCount(empty with { Omitted = Omissions(optional, takenOptional) }) + itemBytes + Math.Max(0, itemCount - 1);

        long itemBytes = required.Sum(Size);
        int next = 0;
        if (Total(itemBytes, required.Count, 0) > allowance.InputTextBytes) return AdvisoryAiDraftProblem.RequiredEvidenceDoesNotFit;
        while (next < optional.Count)
        {
            long withNext = itemBytes + Size(optional[next]);
            if (Total(withNext, required.Count + next + 1, next + 1) > allowance.InputTextBytes) break;
            itemBytes = withNext;
            next++;
        }
        draft = new(source, origin, [.. required, .. optional.Take(next)], Omissions(optional, next), from, to, 1);
        // The arithmetic above must agree with the canonical text itself; if it ever does not, no draft is made.
        if (!allowance.Fits(draft) || AdvisoryAiInputText.ByteCount(draft) != Total(itemBytes, draft.Items.Count, next))
        {
            draft = null;
            return AdvisoryAiDraftProblem.RequiredEvidenceDoesNotFit;
        }
        return AdvisoryAiDraftProblem.None;
    }

    private static AdvisoryAiOmission[] Omissions(IReadOnlyList<AdvisoryAiEvidenceItem> optional, int taken) =>
        [.. optional.Skip(taken).GroupBy(item => item.Role).OrderBy(group => (int)group.Key).Select(group => new AdvisoryAiOmission(group.Key, group.Count()))];

    private static AdvisoryAiEvidenceItem Item(AdvisoryAiEvidenceRole role, string text, DiagnosticRedactionContext? context)
    {
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(text, AdvisoryAiEvidenceDraft.MaximumItemScalars, context);
        return new(role, clean.Value ?? "(no text)", clean.Truncated);
    }

    /// <summary>One record as one line of plain facts. Only the stored, already sanitized projection is used.</summary>
    public static string DescribeRecord(DiagnosticRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        List<string> parts = [record.Occurrence.Utc is DateTimeOffset utc ? AdvisoryAiInputText.FormatUtc(utc) : "time unknown",
            Source(record.Source), record.Kind.ToString(), "outcome " + record.Outcome];
        if (record.Subject is not null) parts.Add(record.Subject);
        if (record.Detail is not null) parts.Add(record.Detail);
        if (record.EventId is int eventId) parts.Add(string.Create(CultureInfo.InvariantCulture, $"event {eventId}"));
        if (record.ResultCode is long code) parts.Add(code >= 0 ? string.Create(CultureInfo.InvariantCulture, $"code 0x{code:X}") : string.Create(CultureInfo.InvariantCulture, $"code {code}"));
        if (record.Summary is not null) parts.Add(record.Summary);
        if (record.TextTruncated) parts.Add("(text was shortened)");
        return string.Join(" | ", parts);
    }

    /// <summary>ADR-028 R4: what every source returned and the interval it was asked for, so the model can see what is missing.</summary>
    public static string DescribeCoverage(DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return "Sources read: " + string.Join("; ", snapshot.Sources.OrderBy(source => (int)source.Source).Select(source => string.Create(CultureInfo.InvariantCulture,
            $"{Source(source.Source)}: {source.Outcome}{(source.Reason == DiagnosticSourceReason.None ? string.Empty : " (" + source.Reason + ")")}, {source.RecordsKept} records kept, {AdvisoryAiInputText.FormatUtc(source.QueriedFromUtc)} to {AdvisoryAiInputText.FormatUtc(source.QueriedToUtc)}")));
    }

    private static string Source(DiagnosticSourceKind source) => source switch
    {
        DiagnosticSourceKind.PcStewardHistory => "PC Steward history",
        DiagnosticSourceKind.SystemEventLog => "System event log",
        DiagnosticSourceKind.ApplicationEventLog => "Application event log",
        DiagnosticSourceKind.Reliability => "Reliability history",
        DiagnosticSourceKind.WindowsUpdateHistory => "Windows Update history",
        _ => "Unknown source"
    };
}

/// <summary>FR-AI-02: the facts about one failed update, from PC Steward's own recorded history only.</summary>
public sealed record AdvisoryAiFailedUpdateFacts(
    string AppName,
    string Provider,
    string InstalledVersion,
    string TargetVersion,
    string Outcome,
    int? ExitCode,
    IReadOnlyList<AdvisoryAiFailedUpdateEvent> Events);

/// <summary>One recorded event of the selected job. <see cref="Diagnostic"/> is PC Steward's own sanitized summary, never an installer log.</summary>
public sealed record AdvisoryAiFailedUpdateEvent(DateTimeOffset AtUtc, string Status, int? ExitCode, string? Diagnostic);

/// <summary>
/// FR-AI-02 (ADR-028 R4): makes the default draft for a failed update: a summary of the job first, then the job's own recorded
/// events in time order until the next does not fit. Nothing from other jobs, the inventory or the file system is used.
/// </summary>
public static class AdvisoryAiFailedUpdateDraftBuilder
{
    public static AdvisoryAiDraftProblem TryBuild(Guid jobId, AdvisoryAiFailedUpdateFacts facts, AdvisoryAiInputAllowance allowance,
        DiagnosticRedactionContext? context, out AdvisoryAiEvidenceDraft? draft)
    {
        ArgumentNullException.ThrowIfNull(facts);
        AdvisoryAiFailedUpdateEvent[] events = [.. facts.Events.OrderBy(item => item.AtUtc.UtcTicks)];
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"Update of {facts.AppName} from version {facts.InstalledVersion} to {facts.TargetVersion} through {facts.Provider}: {facts.Outcome}{(facts.ExitCode is int code ? $", exit code {code} (0x{code:X8})" : string.Empty)}. {events.Length} events were recorded for this update. Their text is PC Steward's own short summary; no installer log is included.");
        AdvisoryAiEvidenceItem[] required = [Item(AdvisoryAiEvidenceRole.FailedUpdateSummary, summary, context)];
        AdvisoryAiEvidenceItem[] optional = [.. events.Select(item => Item(AdvisoryAiEvidenceRole.FailedUpdateEvent, string.Create(CultureInfo.InvariantCulture,
            $"{AdvisoryAiInputText.FormatUtc(item.AtUtc)} | {item.Status}{(item.ExitCode is int exit ? $" | exit code {exit} (0x{exit:X8})" : string.Empty)}{(string.IsNullOrWhiteSpace(item.Diagnostic) ? string.Empty : " | " + item.Diagnostic)}"), context))];
        DateTimeOffset? from = events.Length == 0 ? null : events[0].AtUtc.ToUniversalTime();
        DateTimeOffset? to = events.Length == 0 ? null : events[^1].AtUtc.ToUniversalTime();
        return AdvisoryAiDiagnosticDraftBuilder.Fit(AdvisoryAiSourceKind.FailedUpdate, jobId.ToString("D"), required, optional, from, to, allowance, out draft);
    }

    private static AdvisoryAiEvidenceItem Item(AdvisoryAiEvidenceRole role, string text, DiagnosticRedactionContext? context)
    {
        DiagnosticSanitizedText clean = AdvisoryAiRedaction.SanitizeEvidence(text, AdvisoryAiEvidenceDraft.MaximumItemScalars, context);
        return new(role, clean.Value ?? "(no text)", clean.Truncated);
    }
}
