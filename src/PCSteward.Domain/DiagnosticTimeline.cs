namespace PCSteward.Domain;

/// <summary>
/// FR-DIA-01 (ADR-027 Q1): the five diagnostic sources in their closed collection and tie-break order. PC Steward history is first
/// so operating-system saturation can never remove the app changes that proximity labels need.
/// </summary>
public enum DiagnosticSourceKind
{
    PcStewardHistory = 0,
    SystemEventLog = 1,
    ApplicationEventLog = 2,
    Reliability = 3,
    WindowsUpdateHistory = 4
}

/// <summary>FR-DIA-01: each source reports exactly one of these results in every snapshot, including sources the owner left out.</summary>
public enum DiagnosticSourceOutcome
{
    Available,
    Empty,
    Partial,
    Denied,
    Unsupported,
    TimedOut,
    Cancelled,
    Failed,
    NotSelected
}

/// <summary>FR-DIA-01/03: the closed, typed reason behind a source result. Never free text from a reader.</summary>
public enum DiagnosticSourceReason
{
    None,
    SourceRecordLimit,
    CollectionRecordLimit,
    CollectionByteLimit,
    InvalidRecordsRejected,
    ReaderDeadline,
    CollectionDeadline,
    AccessDenied,
    NotSupported,
    CancelledByOwner,
    ReaderFault,
    NotSelectedByOwner
}

/// <summary>FR-DIA-02 (ADR-027 Q1/Q6): the closed event kinds a projected record can carry.</summary>
public enum DiagnosticEventKind
{
    PcStewardUpdate,
    UnexpectedShutdown,
    BugCheck,
    ApplicationCrash,
    ApplicationHang,
    ErrorReport,
    SoftwareInstalled,
    SoftwareRemoved,
    WindowsUpdate,
    OtherReliabilityProblem
}

/// <summary>FR-DIA-02: the closed outcome of a projected record. Source text never sets it.</summary>
public enum DiagnosticOutcomeKind
{
    NotApplicable,
    Succeeded,
    Failed,
    RestartPending,
    Cancelled,
    InProgress,
    Unknown
}

/// <summary>
/// FR-DIA-02 (ADR-027 Q6): how far an occurrence time can be trusted. Only <see cref="Known"/> times are ordered for proximity;
/// <see cref="Future"/> and <see cref="Conflicting"/> stay in time order but are flagged and never labelled "occurred near";
/// <see cref="Unknown"/> and <see cref="Ambiguous"/> go to a separate untimed section rather than being guessed.
/// </summary>
public enum DiagnosticTimeQuality
{
    Known,
    Future,
    Conflicting,
    Ambiguous,
    Unknown
}

/// <summary>FR-DIA-02: a normalized occurrence. <see cref="Utc"/> is null when the time is unknown or ambiguous.</summary>
public sealed record DiagnosticOccurrence(DateTimeOffset? Utc, TimeSpan? OriginalOffset, DiagnosticTimeQuality Quality)
{
    public static DiagnosticOccurrence Unknown { get; } = new(null, null, DiagnosticTimeQuality.Unknown);

    public bool IsOrdered => Utc is not null && Quality is DiagnosticTimeQuality.Known or DiagnosticTimeQuality.Future or DiagnosticTimeQuality.Conflicting;

    public bool CanLabelProximity => Utc is not null && Quality == DiagnosticTimeQuality.Known;
}

/// <summary>
/// FR-DIA-02 (ADR-027 Q6): normalizes source times to UTC without guessing. A local wall-clock time that a DST change makes
/// ambiguous or invalid stays ambiguous; a time after the captured collection instant is flagged as future, never clamped.
/// Receipt time is never accepted as event time: callers pass only the source's own occurrence time.
/// </summary>
public static class DiagnosticTimeNormalizer
{
    public static DiagnosticOccurrence FromInstant(DateTimeOffset? instant, DateTimeOffset collectionStartUtc, bool sourceReportsConflict = false)
    {
        if (instant is null) return DiagnosticOccurrence.Unknown;
        DateTimeOffset utc = instant.Value.ToUniversalTime();
        DiagnosticTimeQuality quality = sourceReportsConflict ? DiagnosticTimeQuality.Conflicting
            : utc > collectionStartUtc.ToUniversalTime() ? DiagnosticTimeQuality.Future
            : DiagnosticTimeQuality.Known;
        return new(utc, instant.Value.Offset, quality);
    }

    public static DiagnosticOccurrence FromLocalWallClock(DateTime? localTime, TimeZoneInfo zone, DateTimeOffset collectionStartUtc)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (localTime is null) return DiagnosticOccurrence.Unknown;
        DateTime unspecified = DateTime.SpecifyKind(localTime.Value, DateTimeKind.Unspecified);
        if (zone.IsAmbiguousTime(unspecified) || zone.IsInvalidTime(unspecified)) return new(null, null, DiagnosticTimeQuality.Ambiguous);
        TimeSpan offset = zone.GetUtcOffset(unspecified);
        return FromInstant(new DateTimeOffset(unspecified, offset), collectionStartUtc);
    }
}

/// <summary>
/// FR-DIA-03 (ADR-027 Q1, owner-accepted v1): the collection, size, time and retention limits. MiB means 1,048,576 bytes.
/// These are policy values, not performance evidence.
/// </summary>
public static class DiagnosticCollectionPolicy
{
    public const int Version = 1;
    public const int MaximumRecordsPerSource = 500;
    public const int MaximumRecordsOverall = 2000;
    public const int OperatingSystemSourceCount = 4;
    public const int MaximumRecordBytes = 4096;
    public const int MaximumSummaryScalars = 512;
    public const int MaximumSubjectScalars = 256;
    public const int MaximumDetailScalars = 128;
    public const int MaximumSourceRecordIdLength = 128;
    public const long MaximumCollectionBytes = 8L * 1024 * 1024;
    public const long MaximumRetainedBytes = 64L * 1024 * 1024;

    /// <summary>Bytes held back from the 8 MiB budget for the snapshot envelope and all five source results before any record is accepted.</summary>
    public const long ReservedEnvelopeBytes = 16 * 1024;

    public static TimeSpan Lookback { get; } = TimeSpan.FromDays(7);
    public static TimeSpan OverallDeadline { get; } = TimeSpan.FromSeconds(60);
    public static TimeSpan PerReaderCeiling { get; } = TimeSpan.FromSeconds(15);

    /// <summary>Held back from the overall deadline for validation and the snapshot write, so the 60 seconds include them.</summary>
    public static TimeSpan WriteReserve { get; } = TimeSpan.FromSeconds(5);

    public static TimeSpan ProximityWindow { get; } = TimeSpan.FromMinutes(15);
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(30);

    /// <summary>ADR-027 R2: each OS reader gets <c>min(500, floor((2000 - a) / 4))</c> matching-record slots; unused slots are not redistributed.</summary>
    public static int OperatingSystemSlots(int appHistoryRecords)
    {
        if (appHistoryRecords is < 0 or > MaximumRecordsPerSource) throw new ArgumentOutOfRangeException(nameof(appHistoryRecords));
        return Math.Min(MaximumRecordsPerSource, (MaximumRecordsOverall - appHistoryRecords) / OperatingSystemSourceCount);
    }

    /// <summary>ADR-027 Q1: each OS reader gets <c>min(15 s, remaining / readers still to start)</c>; zero when no time remains.</summary>
    public static TimeSpan ReaderBudget(TimeSpan remaining, int readersStillToStart)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(readersStillToStart, 1);
        if (remaining <= TimeSpan.Zero) return TimeSpan.Zero;
        TimeSpan share = remaining / readersStillToStart;
        return share < PerReaderCeiling ? share : PerReaderCeiling;
    }

    /// <summary>ADR-027 Q1: lookback is seven elapsed days ending at the captured collection-start instant; a narrower window is allowed.</summary>
    public static DateTimeOffset LookbackStart(DateTimeOffset collectionStartUtc, TimeSpan? requestedLookback = null)
    {
        TimeSpan lookback = requestedLookback ?? Lookback;
        if (lookback <= TimeSpan.Zero || lookback > Lookback) throw new ArgumentOutOfRangeException(nameof(requestedLookback));
        return collectionStartUtc.ToUniversalTime() - lookback;
    }

    public static DateTimeOffset ExpiresAt(DateTimeOffset collectionStartUtc) => collectionStartUtc.ToUniversalTime() + Retention;

    /// <summary>
    /// ADR-027 byte limits: what one saved snapshot counts against the 64 MiB allowance. Its record bytes plus the fixed envelope
    /// charge, which covers the snapshot row and its five source results, so even an empty or all-denied snapshot uses space.
    /// </summary>
    public static long RetainedBytes(long payloadBytes) => payloadBytes + ReservedEnvelopeBytes;
}

/// <summary>
/// FR-DIA-01/03: one sanitized, minimal, projected record as stored in a snapshot. It carries no computer name, account, SID,
/// full path, command line, raw message or XML. Identifiers are source-local evidence only and never execution authority.
/// </summary>
public sealed record DiagnosticRecord(
    DiagnosticSourceKind Source,
    string SourceRecordId,
    bool WeakIdentity,
    DiagnosticOccurrence Occurrence,
    DiagnosticEventKind Kind,
    DiagnosticOutcomeKind Outcome,
    int? EventId,
    long? ResultCode,
    string? Subject,
    string? Detail,
    string? Summary,
    bool TextTruncated)
{
    public DiagnosticRecordKey Key => new(Source, SourceRecordId);
}

/// <summary>A record's stable identity inside one snapshot: source plus validated source-local identity.</summary>
public readonly record struct DiagnosticRecordKey(DiagnosticSourceKind Source, string SourceRecordId);

/// <summary>FR-DIA-01: one source's result, the interval it was asked for, and how many matching records it returned, kept or rejected.</summary>
public sealed record DiagnosticSourceResult(
    DiagnosticSourceKind Source,
    DiagnosticSourceOutcome Outcome,
    DiagnosticSourceReason Reason,
    DateTimeOffset QueriedFromUtc,
    DateTimeOffset QueriedToUtc,
    int AllowedRecords,
    int MatchingRecordsReturned,
    int RecordsKept,
    int RecordsRejected);

/// <summary>
/// FR-DIA-03 (ADR-027): an immutable whole snapshot. Policy version, coverage and records are frozen together so the same snapshot
/// always replays to the same timeline; viewing or exporting never refreshes <see cref="ExpiresAtUtc"/>.
/// </summary>
public sealed record DiagnosticSnapshot(
    Guid Id,
    int PolicyVersion,
    DateTimeOffset CollectedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset LookbackFromUtc,
    IReadOnlyList<DiagnosticSourceResult> Sources,
    IReadOnlyList<DiagnosticRecord> Records,
    long PayloadBytes)
{
    /// <summary>FR-DIA-01: zero readable sources is "no diagnostic coverage", never a clean-health result.</summary>
    public bool HasCoverage => Sources.Any(source => source.Outcome is DiagnosticSourceOutcome.Available or DiagnosticSourceOutcome.Empty or DiagnosticSourceOutcome.Partial);

    public bool IsPartial => Sources.Any(source => source.Outcome is not (DiagnosticSourceOutcome.Available or DiagnosticSourceOutcome.Empty));

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc.ToUniversalTime() >= ExpiresAtUtc;

    /// <summary>Record bytes plus the fixed envelope charge: what this snapshot counts against the 64 MiB allowance.</summary>
    public long RetainedBytes => DiagnosticCollectionPolicy.RetainedBytes(PayloadBytes);
}

/// <summary>FR-DIA-02: one PC Steward change whose completion time falls inside the inclusive window around the selected incident.</summary>
public sealed record DiagnosticNearbyChange(DiagnosticRecord Change, TimeSpan OffsetFromIncident)
{
    public const string Label = "Occurred near";
}

/// <summary>FR-DIA-02: the deterministic ordered view of a snapshot.</summary>
public sealed record DiagnosticTimeline(
    DiagnosticSnapshot Snapshot,
    IReadOnlyList<DiagnosticRecord> Timed,
    IReadOnlyList<DiagnosticRecord> Untimed,
    int DuplicatesRemoved);

/// <summary>
/// FR-DIA-02 (ADR-027 Q6): deterministic ordering, within-source de-duplication and "occurred near" labelling. There is no
/// cross-source merge, no transitive chain, no ranking, no cause and no health score.
/// </summary>
public static class DiagnosticTimelineBuilder
{
    /// <summary>FR-DIA-02: shown with every unexpected-shutdown record so its reporting time is never read as a cause or exact instant.</summary>
    public const string UnexpectedShutdownNote =
        "Windows recorded that the PC did not shut down cleanly. The time is when Windows logged it, which can be after the shutdown itself. It does not say what caused it.";

    /// <summary>FR-DIA-02: shown with every "occurred near" list.</summary>
    public const string ProximityNote =
        "These changes happened within 15 minutes of the selected event. Happening nearby does not mean one caused the other.";

    public static DiagnosticTimeline Build(DiagnosticSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<DiagnosticRecord> kept = [];
        HashSet<DiagnosticRecordKey> strongKeys = [];
        HashSet<WeakIdentityKey> weakKeys = [];
        int duplicates = 0;
        foreach (DiagnosticRecord record in snapshot.Records)
        {
            bool added = record.WeakIdentity ? weakKeys.Add(WeakIdentityKey.From(record)) : strongKeys.Add(record.Key);
            if (added) kept.Add(record); else duplicates++;
        }

        List<DiagnosticRecord> timed = [.. kept.Where(record => record.Occurrence.IsOrdered)];
        timed.Sort(CompareTimed);
        List<DiagnosticRecord> untimed = [.. kept.Where(record => !record.Occurrence.IsOrdered)];
        untimed.Sort(CompareIdentity);
        return new(snapshot, timed.AsReadOnly(), untimed.AsReadOnly(), duplicates);
    }

    /// <summary>
    /// FR-DIA-02: PC Steward changes with a known completion time within ±15 minutes of the incident, inclusive at both ends.
    /// An incident without a known time, or an in-progress/unknown-time change, produces no label.
    /// </summary>
    public static IReadOnlyList<DiagnosticNearbyChange> NearbyChanges(DiagnosticTimeline timeline, DiagnosticRecordKey incident)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        DiagnosticRecord? selected = timeline.Timed.FirstOrDefault(record => record.Key == incident);
        if (selected is null || !selected.Occurrence.CanLabelProximity) return [];
        DateTimeOffset incidentUtc = selected.Occurrence.Utc!.Value;
        return timeline.Timed
            .Where(record => record.Source == DiagnosticSourceKind.PcStewardHistory &&
                record.Key != incident &&
                record.Occurrence.CanLabelProximity &&
                record.Outcome != DiagnosticOutcomeKind.InProgress &&
                (record.Occurrence.Utc!.Value - incidentUtc).Duration() <= DiagnosticCollectionPolicy.ProximityWindow)
            .Select(record => new DiagnosticNearbyChange(record, record.Occurrence.Utc!.Value - incidentUtc))
            .ToArray();
    }

    private static int CompareTimed(DiagnosticRecord left, DiagnosticRecord right)
    {
        int byTime = left.Occurrence.Utc!.Value.UtcTicks.CompareTo(right.Occurrence.Utc!.Value.UtcTicks);
        return byTime != 0 ? byTime : CompareIdentity(left, right);
    }

    private static int CompareIdentity(DiagnosticRecord left, DiagnosticRecord right)
    {
        int bySource = ((int)left.Source).CompareTo((int)right.Source);
        if (bySource != 0) return bySource;
        int byId = string.CompareOrdinal(left.SourceRecordId, right.SourceRecordId);
        return byId != 0 ? byId : string.CompareOrdinal(DiagnosticRecordJson.Serialize(left), DiagnosticRecordJson.Serialize(right));
    }

    /// <summary>ADR-027 Q6: without a reliable identity, only identical normalized projected fields inside one source are duplicates.</summary>
    private readonly record struct WeakIdentityKey(DiagnosticSourceKind Source, string Projection)
    {
        public static WeakIdentityKey From(DiagnosticRecord record) =>
            new(record.Source, DiagnosticRecordJson.Serialize(record with { SourceRecordId = string.Empty }));
    }
}
