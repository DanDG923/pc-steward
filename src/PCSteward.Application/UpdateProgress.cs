using PCSteward.Domain;
using System.Text.Json;

namespace PCSteward.Application;

public enum UpdateProgressPhase { Queued = 1, Preparing, ElevationRequested, HandedToHelper, ProcessStarted, Verifying, Final }
public enum UpdateProgressCompletion { Recorded, AcknowledgementRequired, RecordingFailed }
public enum UpdateProgressOfferResult { Accepted, Coalesced, RejectedForeign, RejectedInvalid, CapacityFaulted, Closed }
public sealed record UpdateProgressFrame(Guid JobId, Guid CorrelationId, uint Sequence, UpdateProgressPhase Phase,
    UpdateHistoryStatus? RecordedStatus, UpdateProgressCompletion? Completion, bool Degraded);
internal readonly record struct UpdateProgressCounters(int Attempts, int ForeignRejected, int InvalidRejected, int Coalesced);

/// <summary>
/// Desktop-local, job-bound progress. Producers never invoke consumer code or cancel an operation.
/// A consumer pulls at most four frames per half-open second; an unread consumer uses two slots.
/// </summary>
public sealed class UpdateProgressMailbox
{
    private static readonly TimeSpan MinimumEmissionInterval = TimeSpan.FromMilliseconds(250);
    private readonly object sync = new();
    private readonly TaskCompletionSource capacityFault = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Guid jobId;
    private readonly Guid correlationId;
    private readonly UpdateScope scope;
    private readonly TimeProvider clock;
    private readonly UpdateProgressQuota quota = new(UpdateProgressEncoding.MaximumEncodedFrameBytes);
    private UpdateProgressFrame? pending;
    private UpdateProgressFrame? retainedTerminal;
    private bool terminalPending;
    private bool closed;
    private bool offersClosed;
    private bool faulted;
    private bool emitted;
    private long lastEmission;
    private uint sequence;
    private UpdateProgressPhase lastPhase;
    private int attempts;
    private int foreignRejected;
    private int invalidRejected;
    private int coalesced;
    internal UpdateProgressMailbox(Guid jobId, Guid correlationId, UpdateScope scope, TimeProvider? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(jobId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(correlationId, Guid.Empty);
        if (scope is not (UpdateScope.User or UpdateScope.Machine)) throw new ArgumentOutOfRangeException(nameof(scope));
        this.jobId = jobId;
        this.correlationId = correlationId;
        this.scope = scope;
        this.clock = clock ?? TimeProvider.System;
    }

    internal UpdateProgressCounters Counters { get { lock (sync) return new(attempts, foreignRejected, invalidRejected, coalesced); } }
    public bool HasDegraded { get { lock (sync) return faulted || invalidRejected > 0; } }
    internal Task CapacityFault => capacityFault.Task;
    internal bool Matches(ImmutableUpdateJob job) => Matches(job.Id, job.CorrelationId, job.Selection.Scope);
    internal bool Matches(Guid job, Guid correlation, UpdateScope route) => job == jobId && correlation == correlationId && route == scope;
    internal UpdateProgressFrame? RetainedTerminal { get { lock (sync) return retainedTerminal; } }

    internal UpdateProgressOfferResult Offer(Guid jobId, Guid correlationId, UpdateProgressPhase phase)
    {
        lock (sync)
        {
            if (closed || offersClosed) return UpdateProgressOfferResult.Closed;
            if (jobId != this.jobId || correlationId != this.correlationId)
            {
                foreignRejected = IncrementSaturated(foreignRejected);
                return UpdateProgressOfferResult.RejectedForeign;
            }
            if (faulted) return UpdateProgressOfferResult.CapacityFaulted;
            if (++attempts > 255) return FaultCapacity();
            if (!CanAdvance(phase))
            {
                invalidRejected = IncrementSaturated(invalidRejected);
                return UpdateProgressOfferResult.RejectedInvalid;
            }

            // Reserve the widest possible sequence before emission assigns its actual value.
            UpdateProgressFrame next = new(jobId, correlationId, 256, phase, null, null, false);
            if (!quota.Reserve(UpdateProgressEncoding.Encode(next), 1)) return FaultCapacity();
            bool replacesPending = pending is not null;
            if (replacesPending) coalesced = IncrementSaturated(coalesced);
            pending = next;
            lastPhase = phase;
            return replacesPending ? UpdateProgressOfferResult.Coalesced : UpdateProgressOfferResult.Accepted;
        }
    }

    internal bool Complete(UpdateHistoryStatus? status, UpdateProgressCompletion completion)
    {
        lock (sync)
        {
            if (closed || !Enum.IsDefined(completion)) return false;
            if (completion == UpdateProgressCompletion.Recorded)
            {
                if (status is null || !Enum.IsDefined(status.Value) || status is
                    UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.RunningApplicationWarning)
                    return false;
            }
            else if (status is not null) return false;

            retainedTerminal = new(jobId, correlationId, 0, UpdateProgressPhase.Final,
                status, completion, faulted || invalidRejected > 0);
            pending = null;
            terminalPending = true;
            closed = true;
            return true;
        }
    }

    /// <summary>Ends production without erasing a retained terminal or invoking consumer code.</summary>
    internal void SealProducer() { lock (sync) closed = true; }
    internal void StopOffers() { lock (sync) offersClosed = true; }

    public bool TryTake(out UpdateProgressFrame? frame)
    {
        lock (sync)
        {
            frame = null;
            UpdateProgressFrame? available = terminalPending ? retainedTerminal : pending;
            if (available is null) return false;
            long now = clock.GetTimestamp();
            if (emitted && clock.GetElapsedTime(lastEmission, now) < MinimumEmissionInterval) return false;
            frame = available with { Sequence = ++sequence, Degraded = faulted || invalidRejected > 0 };
            // Counted conservatively at offer; verify actual canonical encoding at emission too.
            _ = UpdateProgressEncoding.Encode(frame);
            lastEmission = now;
            emitted = true;
            if (terminalPending)
            {
                retainedTerminal = frame;
                terminalPending = false;
            }
            else pending = null;
            return true;
        }
    }

    private bool CanAdvance(UpdateProgressPhase phase) => phase switch
    {
        UpdateProgressPhase.Queued => lastPhase == 0,
        UpdateProgressPhase.Preparing => lastPhase == UpdateProgressPhase.Queued,
        UpdateProgressPhase.ElevationRequested => scope == UpdateScope.Machine && lastPhase == UpdateProgressPhase.Preparing,
        UpdateProgressPhase.HandedToHelper => scope == UpdateScope.Machine && lastPhase == UpdateProgressPhase.ElevationRequested,
        UpdateProgressPhase.ProcessStarted => scope == UpdateScope.User && lastPhase == UpdateProgressPhase.Preparing,
        UpdateProgressPhase.Verifying => lastPhase == (scope == UpdateScope.User
            ? UpdateProgressPhase.ProcessStarted : UpdateProgressPhase.HandedToHelper),
        _ => false
    };

    private UpdateProgressOfferResult FaultCapacity()
    {
        faulted = true;
        pending = null;
        capacityFault.TrySetResult();
        return UpdateProgressOfferResult.CapacityFaulted;
    }

    private static int IncrementSaturated(int value) => value == int.MaxValue ? value : value + 1;
}

/// <summary>Job-bound local observations only; carries no execution authority or consumer callbacks.</summary>
public sealed class UpdateItemProgress
{
    private readonly ImmutableUpdateJob job;
    private readonly UpdateProgressMailbox? mailbox;
    private int invocationPossible;

    internal UpdateItemProgress(ImmutableUpdateJob job, UpdateProgressMailbox? mailbox)
    {
        this.job = job;
        if (mailbox is not null && !mailbox.Matches(job)) throw new ArgumentException("Progress identity does not match the approved job.", nameof(mailbox));
        this.mailbox = mailbox;
    }

    internal bool InvocationPossible => Volatile.Read(ref invocationPossible) != 0;
    public void ElevationRequested() => Observe(UpdateProgressPhase.ElevationRequested);
    public void HandedToHelper() => Observe(UpdateProgressPhase.HandedToHelper);
    public void ProcessStarted() => Observe(UpdateProgressPhase.ProcessStarted);

    private void Observe(UpdateProgressPhase phase)
    {
        if (phase is UpdateProgressPhase.ElevationRequested or UpdateProgressPhase.ProcessStarted)
            Interlocked.Exchange(ref invocationPossible, 1);
        mailbox?.Offer(job.Id, job.CorrelationId, phase);
    }
}

internal sealed class UpdateProgressCancellation
{
    private readonly CancellationTokenSource item;
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task monitor;
    private readonly UpdateProgressMailbox? mailbox;
    internal UpdateProgressCancellation(UpdateProgressMailbox? mailbox, CancellationToken caller)
    {
        this.mailbox = mailbox;
        item = CancellationTokenSource.CreateLinkedTokenSource(caller);
        monitor = MonitorAsync();
    }
    internal CancellationToken Token => item.Token;
    internal bool CapacityFaulted => mailbox?.CapacityFault.IsCompleted == true;
    private async Task MonitorAsync()
    {
        if (mailbox is null) return;
        await Task.WhenAny(mailbox.CapacityFault, finished.Task).ConfigureAwait(false);
        if (CapacityFaulted) await item.CancelAsync().ConfigureAwait(false);
    }
    internal async Task FinishAsync()
    {
        finished.TrySetResult();
        try { await monitor.ConfigureAwait(false); }
        finally { item.Dispose(); }
    }
}

internal sealed class UpdateProgressQuota
{
    private readonly int terminalReservation;
    private int reserved;

    internal UpdateProgressQuota(int terminalReservation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(terminalReservation);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(terminalReservation, 16_384);
        this.terminalReservation = terminalReservation;
    }

    internal bool Reserve(ReadOnlySpan<byte> encoded, int pending)
    {
        if (encoded.IsEmpty || encoded.Length > 16_384 || pending is < 1 or > 32 ||
            reserved > 1_048_576 - terminalReservation - encoded.Length) return false;
        reserved += encoded.Length;
        return true;
    }
}

internal static class UpdateProgressEncoding
{
    internal const int MaximumEncodedFrameBytes = 512;

    internal static byte[] Encode(UpdateProgressFrame frame)
    {
        using MemoryStream buffer = new(MaximumEncodedFrameBytes);
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("jobId", frame.JobId);
            writer.WriteString("correlationId", frame.CorrelationId);
            writer.WriteNumber("sequence", frame.Sequence);
            writer.WriteNumber("phase", (int)frame.Phase);
            if (frame.RecordedStatus is { } status) writer.WriteNumber("finalStatus", (int)status);
            else writer.WriteNull("finalStatus");
            if (frame.Completion is { } completion) writer.WriteNumber("completion", (int)completion);
            else writer.WriteNull("completion");
            writer.WriteBoolean("degraded", frame.Degraded);
            writer.WriteEndObject();
        }
        if (buffer.Length > MaximumEncodedFrameBytes)
            throw new InvalidOperationException("Closed progress schema exceeded its reserved encoding bound.");
        return buffer.ToArray();
    }
}
