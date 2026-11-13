using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>FR-DIA-01: what one reader is asked for. The reader must filter by time and its allowlist at the source.</summary>
public sealed record DiagnosticReadRequest(DiagnosticSourceKind Source, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int MaximumMatchingRecords);

/// <summary>FR-DIA-01: a reader's own closed result. Exceptions are mapped by the orchestrator, never shown as text.</summary>
public enum DiagnosticReaderStatus
{
    Completed,
    Denied,
    Unsupported,
    Failed
}

/// <summary>
/// FR-DIA-01: a reader's answer. <see cref="Records"/> are the newest matching records first, at most the requested number;
/// <see cref="MoreMatchingRecordsExist"/> says the source had more matching records than it returned.
/// </summary>
public sealed record DiagnosticReaderResult(DiagnosticReaderStatus Status, IReadOnlyList<DiagnosticRecordCandidate> Records, bool MoreMatchingRecordsExist)
{
    public static DiagnosticReaderResult Completed(IReadOnlyList<DiagnosticRecordCandidate> records, bool moreMatchingRecordsExist) => new(DiagnosticReaderStatus.Completed, records, moreMatchingRecordsExist);
    public static DiagnosticReaderResult Denied() => new(DiagnosticReaderStatus.Denied, [], false);
    public static DiagnosticReaderResult Unsupported() => new(DiagnosticReaderStatus.Unsupported, [], false);
    public static DiagnosticReaderResult Failed() => new(DiagnosticReaderStatus.Failed, [], false);
}

/// <summary>
/// FR-DIA-01 (ADR-027): one bounded, read-only, unelevated source. A reader never changes OS state, never elevates, never invokes
/// an update provider and never runs from the scheduled host. A route that cannot apply a bounded matching query, bounded
/// enumeration and cancellation must return <see cref="DiagnosticReaderStatus.Unsupported"/> instead of reading.
/// </summary>
public interface IDiagnosticSourceReader
{
    DiagnosticSourceKind Source { get; }

    Task<DiagnosticReaderResult> ReadAsync(DiagnosticReadRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// FR-DIA-01 (ADR-027 per-collection consent): the owner's approval for exactly one collection. It names the selected sources,
/// lookback and disclosed policy version; it is spent by the first collection that uses it and is never remembered.
/// </summary>
public sealed class DiagnosticCollectionConsent
{
    private int spent;

    public DiagnosticCollectionConsent(IReadOnlyCollection<DiagnosticSourceKind> selectedSources, TimeSpan lookback, int disclosedPolicyVersion)
    {
        ArgumentNullException.ThrowIfNull(selectedSources);
        if (selectedSources.Count == 0 || selectedSources.Any(source => !Enum.IsDefined(source))) throw new ArgumentException("Choose at least one known source.", nameof(selectedSources));
        if (lookback <= TimeSpan.Zero || lookback > DiagnosticCollectionPolicy.Lookback) throw new ArgumentOutOfRangeException(nameof(lookback));
        SelectedSources = selectedSources.Distinct().Order().ToArray();
        Lookback = lookback;
        DisclosedPolicyVersion = disclosedPolicyVersion;
    }

    public IReadOnlyList<DiagnosticSourceKind> SelectedSources { get; }

    public TimeSpan Lookback { get; }

    public int DisclosedPolicyVersion { get; }

    public bool IsSpent => Volatile.Read(ref spent) != 0;

    internal bool TrySpend() => Interlocked.Exchange(ref spent, 1) == 0;
}

/// <summary>FR-DIA-01: how one collection attempt ended. Only <see cref="Completed"/> carries a snapshot.</summary>
public enum DiagnosticCollectionStatus
{
    Completed,
    Cancelled,
    ConsentRejected
}

public sealed record DiagnosticCollectionResult(DiagnosticCollectionStatus Status, DiagnosticSnapshot? Snapshot);

/// <summary>
/// FR-DIA-01/03 (ADR-027 Q1): runs the readers one at a time in the closed source order, PC Steward history first, with the
/// accepted record slots, byte budget and time shares. It admits and sanitizes every record before keeping it, reports every
/// source separately, and returns a snapshot without writing anything. Owner cancellation discards the whole attempt.
/// </summary>
public sealed class DiagnosticCollectionOrchestrator
{
    private readonly Dictionary<DiagnosticSourceKind, IDiagnosticSourceReader> readers;
    private readonly DiagnosticRedactionContext redaction;
    private readonly TimeProvider clock;
    private readonly Func<Guid> newId;
    private readonly long recordByteBudget;

    public DiagnosticCollectionOrchestrator(IEnumerable<IDiagnosticSourceReader> readers, DiagnosticRedactionContext redaction, TimeProvider? clock = null, Func<Guid>? newId = null)
        : this(readers, redaction, clock, newId, DiagnosticCollectionPolicy.MaximumCollectionBytes - DiagnosticCollectionPolicy.ReservedEnvelopeBytes)
    {
    }

    /// <summary>Test seam: with the v1 limits 2,000 records of at most 4 KiB cannot exhaust 8 MiB, so the byte cap is a backstop that fixtures exercise with a smaller budget.</summary>
    internal DiagnosticCollectionOrchestrator(IEnumerable<IDiagnosticSourceReader> readers, DiagnosticRedactionContext redaction, TimeProvider? clock, Func<Guid>? newId, long recordByteBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(recordByteBudget);
        this.recordByteBudget = recordByteBudget;
        ArgumentNullException.ThrowIfNull(readers);
        this.readers = [];
        foreach (IDiagnosticSourceReader reader in readers)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (!Enum.IsDefined(reader.Source) || !this.readers.TryAdd(reader.Source, reader)) throw new ArgumentException("Each source may have exactly one reader.", nameof(readers));
        }
        this.redaction = redaction ?? throw new ArgumentNullException(nameof(redaction));
        this.clock = clock ?? TimeProvider.System;
        this.newId = newId ?? Guid.NewGuid;
    }

    public async Task<DiagnosticCollectionResult> CollectAsync(DiagnosticCollectionConsent consent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (consent.DisclosedPolicyVersion != DiagnosticCollectionPolicy.Version || !consent.TrySpend()) return new(DiagnosticCollectionStatus.ConsentRejected, null);
        if (cancellationToken.IsCancellationRequested) return new(DiagnosticCollectionStatus.Cancelled, null);

        DateTimeOffset collectedAt = clock.GetUtcNow().ToUniversalTime();
        long started = clock.GetTimestamp();
        DateTimeOffset from = DiagnosticCollectionPolicy.LookbackStart(collectedAt, consent.Lookback);
        TimeSpan readingWindow = DiagnosticCollectionPolicy.OverallDeadline - DiagnosticCollectionPolicy.WriteReserve;
        CollectionState state = new(recordByteBudget);
        HashSet<DiagnosticSourceKind> selected = [.. consent.SelectedSources];

        TimeSpan appBudget = Min(DiagnosticCollectionPolicy.PerReaderCeiling, readingWindow - clock.GetElapsedTime(started));
        SourceRun app = await RunAsync(DiagnosticSourceKind.PcStewardHistory, selected, DiagnosticCollectionPolicy.MaximumRecordsPerSource, appBudget, from, collectedAt, state, cancellationToken).ConfigureAwait(false);
        if (app.CancelledByOwner) return new(DiagnosticCollectionStatus.Cancelled, null);

        int operatingSystemSlots = DiagnosticCollectionPolicy.OperatingSystemSlots(Math.Min(app.Result.MatchingRecordsReturned, DiagnosticCollectionPolicy.MaximumRecordsPerSource));
        DiagnosticSourceKind[] operatingSystemSources = [DiagnosticSourceKind.SystemEventLog, DiagnosticSourceKind.ApplicationEventLog, DiagnosticSourceKind.Reliability, DiagnosticSourceKind.WindowsUpdateHistory];
        List<DiagnosticSourceResult> results = [app.Result];
        int stillToStart = operatingSystemSources.Count(source => selected.Contains(source) && readers.ContainsKey(source));
        foreach (DiagnosticSourceKind source in operatingSystemSources)
        {
            bool willStart = selected.Contains(source) && readers.ContainsKey(source);
            TimeSpan budget = willStart ? DiagnosticCollectionPolicy.ReaderBudget(readingWindow - clock.GetElapsedTime(started), stillToStart) : TimeSpan.Zero;
            SourceRun run = await RunAsync(source, selected, operatingSystemSlots, budget, from, collectedAt, state, cancellationToken).ConfigureAwait(false);
            if (run.CancelledByOwner) return new(DiagnosticCollectionStatus.Cancelled, null);
            results.Add(run.Result);
            if (willStart) stillToStart--;
        }

        if (cancellationToken.IsCancellationRequested) return new(DiagnosticCollectionStatus.Cancelled, null);
        DiagnosticSnapshot snapshot = new(newId(), DiagnosticCollectionPolicy.Version, collectedAt, DiagnosticCollectionPolicy.ExpiresAt(collectedAt), from,
            results.AsReadOnly(), state.Records.AsReadOnly(), state.PayloadBytes);
        return new(DiagnosticCollectionStatus.Completed, snapshot);
    }

    private async Task<SourceRun> RunAsync(DiagnosticSourceKind source, HashSet<DiagnosticSourceKind> selected, int allowed, TimeSpan budget, DateTimeOffset from, DateTimeOffset to, CollectionState state, CancellationToken cancellationToken)
    {
        if (!selected.Contains(source)) return Done(DiagnosticSourceOutcome.NotSelected, DiagnosticSourceReason.NotSelectedByOwner);
        if (!readers.TryGetValue(source, out IDiagnosticSourceReader? reader)) return Done(DiagnosticSourceOutcome.Unsupported, DiagnosticSourceReason.NotSupported);
        if (budget <= TimeSpan.Zero) return Done(DiagnosticSourceOutcome.TimedOut, DiagnosticSourceReason.CollectionDeadline);
        if (state.RemainingBytes <= 0) return Done(DiagnosticSourceOutcome.Partial, DiagnosticSourceReason.CollectionByteLimit);
        if (allowed <= 0) return Done(DiagnosticSourceOutcome.Partial, DiagnosticSourceReason.CollectionRecordLimit);

        DiagnosticReaderResult answer;
        long readStarted = clock.GetTimestamp();
        using CancellationTokenSource readerDeadline = new(budget, clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readerDeadline.Token);
        try
        {
            answer = await reader.ReadAsync(new(source, from, to, allowed), linked.Token).WaitAsync(budget, clock, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(Result(DiagnosticSourceOutcome.Cancelled, DiagnosticSourceReason.CancelledByOwner, 0, 0, 0), true);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            return Done(DiagnosticSourceOutcome.TimedOut, DiagnosticSourceReason.ReaderDeadline);
        }
        catch (UnauthorizedAccessException)
        {
            return Done(DiagnosticSourceOutcome.Denied, DiagnosticSourceReason.AccessDenied);
        }
#pragma warning disable CA1031 // FR-DIA-01: any reader fault is reported as that source's typed failure; it never ends the whole collection.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Done(DiagnosticSourceOutcome.Failed, DiagnosticSourceReason.ReaderFault);
        }

        // A reader that returns only after its share has passed (for example by blocking instead of honouring cancellation) is
        // treated as timed out and its records are discarded, so the overall deadline still bounds every later source.
        if (clock.GetElapsedTime(readStarted) >= budget) return Done(DiagnosticSourceOutcome.TimedOut, DiagnosticSourceReason.ReaderDeadline);

        switch (answer?.Status)
        {
            case DiagnosticReaderStatus.Completed when answer.Records is not null: break;
            case DiagnosticReaderStatus.Denied: return Done(DiagnosticSourceOutcome.Denied, DiagnosticSourceReason.AccessDenied);
            case DiagnosticReaderStatus.Unsupported: return Done(DiagnosticSourceOutcome.Unsupported, DiagnosticSourceReason.NotSupported);
            default: return Done(DiagnosticSourceOutcome.Failed, DiagnosticSourceReason.ReaderFault);
        }

        int matching = Math.Min(answer.Records.Count, allowed);
        DiagnosticSourceReason limit = answer.Records.Count > allowed || answer.MoreMatchingRecordsExist ? DiagnosticSourceReason.SourceRecordLimit : DiagnosticSourceReason.None;
        int kept = 0, rejected = 0;
        for (int index = 0; index < matching; index++)
        {
            DiagnosticRecordCandidate? candidate = answer.Records[index];
            DiagnosticAdmission admission = candidate is null || candidate.Source != source ? default : DiagnosticRecordAdmission.Admit(candidate, redaction);
            if (!admission.Admitted) { rejected++; continue; }
            if (state.Records.Count >= DiagnosticCollectionPolicy.MaximumRecordsOverall) { limit = DiagnosticSourceReason.CollectionRecordLimit; break; }
            if (!state.TryAccept(admission.Record!, admission.SerializedBytes)) { limit = DiagnosticSourceReason.CollectionByteLimit; break; }
            kept++;
        }

        DiagnosticSourceOutcome outcome = limit != DiagnosticSourceReason.None ? DiagnosticSourceOutcome.Partial
            : rejected > 0 ? DiagnosticSourceOutcome.Partial
            : kept == 0 ? DiagnosticSourceOutcome.Empty
            : DiagnosticSourceOutcome.Available;
        DiagnosticSourceReason reason = limit != DiagnosticSourceReason.None ? limit : rejected > 0 ? DiagnosticSourceReason.InvalidRecordsRejected : DiagnosticSourceReason.None;
        return new(Result(outcome, reason, matching, kept, rejected), false);

        SourceRun Done(DiagnosticSourceOutcome outcome, DiagnosticSourceReason reason) => new(Result(outcome, reason, 0, 0, 0), false);
        DiagnosticSourceResult Result(DiagnosticSourceOutcome outcome, DiagnosticSourceReason reason, int returned, int keptCount, int rejectedCount) =>
            new(source, outcome, reason, from, to, Math.Max(allowed, 0), returned, keptCount, rejectedCount);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left < right ? left : right;

    private sealed record SourceRun(DiagnosticSourceResult Result, bool CancelledByOwner);

    private sealed class CollectionState(long byteBudget)
    {
        public List<DiagnosticRecord> Records { get; } = [];

        public long PayloadBytes { get; private set; }

        public long RemainingBytes => byteBudget - PayloadBytes;

        /// <summary>Each record costs its canonical bytes plus one separator byte.</summary>
        public bool TryAccept(DiagnosticRecord record, int bytes)
        {
            if (bytes + 1L > RemainingBytes) return false;
            Records.Add(record);
            PayloadBytes += bytes + 1L;
            return true;
        }
    }
}
