using System.Globalization;

namespace PCSteward.Domain;

/// <summary>
/// FR-BUD-01, FR-AI-01 (ADR-029 Q7 and F6): one owner-confirmed rate card for one exact model and service tier, in micro-dollars per
/// million tokens. It is the owner's confirmation of the prices shown on the official pages, not software-verified pricing: it is
/// valid for at most 24 hours from confirmation and nothing refreshes it automatically. A free (zero) rate is never accepted.
/// </summary>
public sealed record AdvisoryAiTariff(
    long Version,
    string ModelId,
    string ServiceTier,
    MicroUsd InputPerMillion,
    MicroUsd CachedInputPerMillion,
    MicroUsd OutputPerMillion,
    DateTimeOffset ConfirmedAtUtc)
{
    public static TimeSpan Validity { get; } = TimeSpan.FromHours(24);

    public DateTimeOffset ExpiresAtUtc => ConfirmedAtUtc.ToUniversalTime() + Validity;

    /// <summary>Current only inside <c>[confirmed, confirmed + 24 h)</c>; a clock before the confirmation is not current either.</summary>
    public bool IsCurrent(DateTimeOffset nowUtc) => nowUtc >= ConfirmedAtUtc && nowUtc < ExpiresAtUtc;

    /// <summary>ADR-029 F6: complete, positive, finite rates for exactly this model and tier, with a positive version.</summary>
    public AdvisoryAiTariffProblem Validate(AdvisoryAiModelProfile model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (Version <= 0) return AdvisoryAiTariffProblem.InvalidVersion;
        if (!string.Equals(ModelId, model.ModelId, StringComparison.Ordinal) || !string.Equals(ServiceTier, model.ServiceTier, StringComparison.Ordinal))
            return AdvisoryAiTariffProblem.WrongModelOrTier;
        if (InputPerMillion.Value <= 0 || CachedInputPerMillion.Value <= 0 || OutputPerMillion.Value <= 0) return AdvisoryAiTariffProblem.MissingOrZeroRate;
        return AdvisoryAiTariffProblem.None;
    }
}

public enum AdvisoryAiTariffProblem
{
    None,
    InvalidVersion,
    WrongModelOrTier,
    MissingOrZeroRate
}

/// <summary>
/// FR-BUD-02/03 (ADR-029 "Reservation arithmetic"): checked, upward-rounded cost arithmetic. Every charge is
/// <c>ceil(tokens × rate ÷ 1,000,000)</c> micro-dollars, computed in 128 bits and refused if it does not fit.
/// </summary>
public static class AdvisoryAiCost
{
    public const long TokensPerRateUnit = 1_000_000;

    public static MicroUsd Charge(long tokens, MicroUsd ratePerMillion)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        ArgumentOutOfRangeException.ThrowIfNegative(ratePerMillion.Value);
        Int128 product = checked((Int128)tokens * ratePerMillion.Value);
        Int128 micro = (product + (TokensPerRateUnit - 1)) / TokensPerRateUnit;
        return new(checked((long)micro));
    }

    /// <summary>
    /// ADR-029: the complete worst case for one approved request with one attempt: every input token at the higher of the uncached
    /// and cached input rates (the provider decides caching), plus every allowed output token, reasoning included, at the output rate.
    /// </summary>
    public static MicroUsd Reservation(int inputTokenUpperBound, int maximumOutputTokens, AdvisoryAiTariff tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        MicroUsd inputRate = tariff.InputPerMillion >= tariff.CachedInputPerMillion ? tariff.InputPerMillion : tariff.CachedInputPerMillion;
        return MicroUsd.Add(Charge(inputTokenUpperBound, inputRate), Charge(maximumOutputTokens, tariff.OutputPerMillion));
    }

    /// <summary>
    /// ADR-029: the cost of reliable reported usage at the request's frozen rates. Reasoning is part of output and is not added again.
    /// Uncached and cached input are priced together and rounded up once, exactly as <see cref="Reservation"/> rounds its input
    /// bound, so usage within the approved bounds can never cost more than the reservation (rounding the two parts separately could
    /// add a micro-dollar).
    /// </summary>
    public static MicroUsd Actual(AdvisoryAiUsage usage, AdvisoryAiTariff tariff)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentOutOfRangeException.ThrowIfNegative(usage.InputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(usage.CachedInputTokens);
        if (usage.CachedInputTokens > usage.InputTokens) throw new ArgumentException("Cached input cannot exceed input.", nameof(usage));
        ArgumentOutOfRangeException.ThrowIfNegative(tariff.InputPerMillion.Value);
        ArgumentOutOfRangeException.ThrowIfNegative(tariff.CachedInputPerMillion.Value);
        Int128 input = checked(((Int128)(usage.InputTokens - usage.CachedInputTokens) * tariff.InputPerMillion.Value) +
            ((Int128)usage.CachedInputTokens * tariff.CachedInputPerMillion.Value));
        MicroUsd inputCharge = new(checked((long)((input + (TokensPerRateUnit - 1)) / TokensPerRateUnit)));
        return MicroUsd.Add(inputCharge, Charge(usage.OutputTokens, tariff.OutputPerMillion));
    }
}

/// <summary>
/// FR-BUD-01/03 (ADR-029 Q7, owner choice): an accounting month is a UTC calendar month, <c>[first day 00:00 UTC, next first day)</c>.
/// Host time zone and daylight saving never move it.
/// </summary>
public readonly record struct AdvisoryAiPeriod(int Year, int Month) : IComparable<AdvisoryAiPeriod>
{
    public static AdvisoryAiPeriod Of(DateTimeOffset instant)
    {
        DateTime utc = instant.UtcDateTime;
        return new(utc.Year, utc.Month);
    }

    public DateTimeOffset StartUtc => new(Year, Month, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset EndUtc => StartUtc.AddMonths(1);

    public int Key => checked(Year * 100 + Month);

    public int CompareTo(AdvisoryAiPeriod other) => Key.CompareTo(other.Key);

    public static bool operator <(AdvisoryAiPeriod left, AdvisoryAiPeriod right) => left.Key < right.Key;
    public static bool operator >(AdvisoryAiPeriod left, AdvisoryAiPeriod right) => left.Key > right.Key;
    public static bool operator <=(AdvisoryAiPeriod left, AdvisoryAiPeriod right) => left.Key <= right.Key;
    public static bool operator >=(AdvisoryAiPeriod left, AdvisoryAiPeriod right) => left.Key >= right.Key;

    public static bool TryParseKey(long key, out AdvisoryAiPeriod period)
    {
        period = default;
        if (key is < 200001 or > 999912) return false;
        int year = (int)(key / 100), month = (int)(key % 100);
        if (month is < 1 or > 12) return false;
        period = new(year, month);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}");
}

/// <summary>FR-BUD-02/03 (ADR-029 "Crash-safe state machine"): the durable states of one app request.</summary>
public enum AdvisoryAiRequestState
{
    /// <summary>Reserved before any transport access; no dispatch claim yet.</summary>
    Reserved,

    /// <summary>Released with transactional proof that no dispatch claim was ever committed.</summary>
    NotSent,

    /// <summary>The one durable claim to make the one network attempt. The whole reservation is kept from here on.</summary>
    DispatchClaimed,

    /// <summary>Reliable usage replaced the reservation with the actual cost at the frozen rates.</summary>
    Settled,

    /// <summary>No reliable usage: the whole reservation stays committed, possibly indefinitely, in its original period.</summary>
    Uncertain
}

/// <summary>ADR-029: the only transitions. Nothing ever returns to Reserved, and nothing after a dispatch claim can become NotSent.</summary>
public static class AdvisoryAiRequestStates
{
    public static bool CanMove(AdvisoryAiRequestState from, AdvisoryAiRequestState to) => (from, to) switch
    {
        (AdvisoryAiRequestState.Reserved, AdvisoryAiRequestState.NotSent) => true,
        (AdvisoryAiRequestState.Reserved, AdvisoryAiRequestState.DispatchClaimed) => true,
        (AdvisoryAiRequestState.DispatchClaimed, AdvisoryAiRequestState.Settled) => true,
        (AdvisoryAiRequestState.DispatchClaimed, AdvisoryAiRequestState.Uncertain) => true,
        _ => false
    };

    /// <summary>A request in this state still holds its whole reservation against its period and the test envelope.</summary>
    public static bool HoldsReservation(AdvisoryAiRequestState state) =>
        state is AdvisoryAiRequestState.Reserved or AdvisoryAiRequestState.DispatchClaimed or AdvisoryAiRequestState.Uncertain;

    /// <summary>ADR-028 one in flight: a request in this state blocks any other reservation, across processes.</summary>
    public static bool IsInFlight(AdvisoryAiRequestState state) => state is AdvisoryAiRequestState.Reserved or AdvisoryAiRequestState.DispatchClaimed;
}

/// <summary>
/// FR-BUD-01/02: what the ledger already holds for the request's period and for the first-live-test envelope, read inside the same
/// serialized write transaction that would insert the reservation, plus the ledger-wide conditions that block every new paid request.
/// </summary>
public sealed record AdvisoryAiLedgerSnapshot(
    MicroUsd SettledInPeriod,
    MicroUsd OutstandingInPeriod,
    MicroUsd TestSettled,
    MicroUsd TestOutstanding,
    bool RequestInFlight,
    bool ReviewBlock,
    bool LedgerFull,
    AdvisoryAiContinuity Continuity,
    AdvisoryAiClockVerdict Clock);

/// <summary>Why a reservation was refused. The first failing rule is reported.</summary>
public enum AdvisoryAiReservationProblem
{
    None,
    PaidRequestsDisabledInThisBuild,
    PaidRequestsDisabledBySettings,
    AnotherRequestInFlight,
    LedgerNeedsReview,
    LedgerFull,
    ContinuityNotEstablished,
    PriorUseUnknown,
    TestSpendingUnknown,
    ClockMovedBackwards,
    NewMonthNeedsConfirmation,
    TariffNotCurrent,
    TariffInvalid,
    AboveRequestCap,
    AboveMonthlyBudget,
    AboveFirstLiveTestEnvelope,
    AmountTooLarge,

    /// <summary>
    /// ADR-029 (concurrent edit and reserve): inside the reservation's write transaction the stored settings, consent, key generation,
    /// limits or newest prices no longer match what the owner approved, or AI was disconnected. A fresh preview is needed.
    /// </summary>
    ApprovalOutdated
}

/// <summary>
/// FR-BUD-01/02 (ADR-029 "Reservation arithmetic"): the admission rule run inside the ledger's serialized write transaction:
/// <c>R ≤ request cap</c>, <c>settled + outstanding + R ≤ monthly budget</c>, and in first-live-test mode
/// <c>test settled + test outstanding + R ≤ $1</c> across months, restarts and attempts. Exactly at a cap is allowed; one micro-dollar
/// over is not.
/// </summary>
public static class AdvisoryAiReservationRule
{
    /// <summary>ADR-029 D5: the separate cumulative envelope for the first live test. Not an editable setting.</summary>
    public static MicroUsd FirstLiveTestEnvelope { get; } = new(1_000_000);

    public static AdvisoryAiReservationProblem Check(MicroUsd reservation, AdvisoryAiLimits limits, AdvisoryAiLedgerSnapshot commitments,
        AdvisoryAiPaidRequestMode mode, AdvisoryAiTariff tariff, AdvisoryAiModelProfile model, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(commitments);
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentNullException.ThrowIfNull(model);
        if (mode == AdvisoryAiPaidRequestMode.Disabled) return AdvisoryAiReservationProblem.PaidRequestsDisabledInThisBuild;
        if (limits.PaidRequestsDisabled) return AdvisoryAiReservationProblem.PaidRequestsDisabledBySettings;
        if (commitments.RequestInFlight) return AdvisoryAiReservationProblem.AnotherRequestInFlight;
        if (commitments.ReviewBlock) return AdvisoryAiReservationProblem.LedgerNeedsReview;
        if (commitments.LedgerFull) return AdvisoryAiReservationProblem.LedgerFull;
        switch (commitments.Continuity)
        {
            case AdvisoryAiContinuity.NotEstablished: return AdvisoryAiReservationProblem.ContinuityNotEstablished;
            case AdvisoryAiContinuity.PriorUseUnknown: return AdvisoryAiReservationProblem.PriorUseUnknown;
            // ADR-029: a fresh month is not a refund of unknown first-live-test spending.
            case AdvisoryAiContinuity.FreshPeriodConfirmed when mode == AdvisoryAiPaidRequestMode.FirstLiveTest:
                return AdvisoryAiReservationProblem.TestSpendingUnknown;
            case AdvisoryAiContinuity.DeclaredNoPriorUse or AdvisoryAiContinuity.FreshPeriodConfirmed: break;
            default: return AdvisoryAiReservationProblem.ContinuityNotEstablished;
        }
        if (commitments.Clock == AdvisoryAiClockVerdict.MovedBackwards) return AdvisoryAiReservationProblem.ClockMovedBackwards;
        if (commitments.Clock != AdvisoryAiClockVerdict.Accepted) return AdvisoryAiReservationProblem.NewMonthNeedsConfirmation;
        if (tariff.Validate(model) != AdvisoryAiTariffProblem.None) return AdvisoryAiReservationProblem.TariffInvalid;
        if (!tariff.IsCurrent(nowUtc)) return AdvisoryAiReservationProblem.TariffNotCurrent;
        if (reservation.Value < 0) return AdvisoryAiReservationProblem.AmountTooLarge;
        try
        {
            if (reservation > limits.RequestCap) return AdvisoryAiReservationProblem.AboveRequestCap;
            MicroUsd month = MicroUsd.Add(MicroUsd.Add(commitments.SettledInPeriod, commitments.OutstandingInPeriod), reservation);
            if (month > limits.MonthlyBudget) return AdvisoryAiReservationProblem.AboveMonthlyBudget;
            if (mode == AdvisoryAiPaidRequestMode.FirstLiveTest)
            {
                MicroUsd test = MicroUsd.Add(MicroUsd.Add(commitments.TestSettled, commitments.TestOutstanding), reservation);
                if (test > FirstLiveTestEnvelope) return AdvisoryAiReservationProblem.AboveFirstLiveTestEnvelope;
            }
        }
        catch (OverflowException)
        {
            return AdvisoryAiReservationProblem.AmountTooLarge;
        }
        return AdvisoryAiReservationProblem.None;
    }

    public static string Explain(AdvisoryAiReservationProblem problem) => problem switch
    {
        AdvisoryAiReservationProblem.None => "This request fits your limits.",
        AdvisoryAiReservationProblem.PaidRequestsDisabledInThisBuild => "Paid AI requests are turned off in this version of PC Steward.",
        AdvisoryAiReservationProblem.PaidRequestsDisabledBySettings => "A spending limit is set to 0, which turns paid requests off.",
        AdvisoryAiReservationProblem.AnotherRequestInFlight => "Another AI request is still running. Only one runs at a time.",
        AdvisoryAiReservationProblem.LedgerNeedsReview => "PC Steward recorded a charge it can't reconcile, so paid requests are paused until it is reviewed.",
        AdvisoryAiReservationProblem.LedgerFull => "The AI spending record is full. Paid requests are paused; nothing owed is deleted to make room.",
        AdvisoryAiReservationProblem.ContinuityNotEstablished => "Before the first paid request, PC Steward needs to ask whether it has made AI requests on this PC before.",
        AdvisoryAiReservationProblem.PriorUseUnknown => "Earlier AI spending on this PC may be unknown, so paid requests wait until a new month is confirmed.",
        AdvisoryAiReservationProblem.TestSpendingUnknown => "Earlier spending on the first live test is unknown, so the test can't continue.",
        AdvisoryAiReservationProblem.ClockMovedBackwards => "The PC's clock moved backwards since the last AI request, so paid requests wait for your review.",
        AdvisoryAiReservationProblem.NewMonthNeedsConfirmation => "A new month has started. Confirm it before PC Steward starts counting it.",
        AdvisoryAiReservationProblem.TariffNotCurrent => "AI prices need confirming: confirmed prices last 24 hours.",
        AdvisoryAiReservationProblem.TariffInvalid => "The confirmed prices are incomplete or for another model.",
        AdvisoryAiReservationProblem.AboveRequestCap => "The most this request could cost is more than your limit per request.",
        AdvisoryAiReservationProblem.AboveMonthlyBudget => "The most this request could cost, added to this month's spending and reservations, is more than your monthly budget.",
        AdvisoryAiReservationProblem.AboveFirstLiveTestEnvelope => "This would take the first live test over its $1 total.",
        AdvisoryAiReservationProblem.AmountTooLarge => "The amount is too large to record.",
        _ => "This request can't be reserved."
    };
}

/// <summary>ADR-029 Q7 "Clock/rollover": what the wall clock says compared with the last accepted accounting time.</summary>
public enum AdvisoryAiClockVerdict
{
    /// <summary>Same period, not earlier.</summary>
    Accepted,

    /// <summary>A later UTC month than the last accepted one: the owner confirms the new month before it is used.</summary>
    NewPeriodNeedsConfirmation,

    /// <summary>Earlier than the last accepted time: new sends wait for the owner's review. An old period is never reopened.</summary>
    MovedBackwards
}

/// <summary>
/// ADR-029 Q7: conservative local detection of clock error or tampering. Not trusted time: a compromised user or OS can still lie.
/// </summary>
public static class AdvisoryAiClock
{
    /// <summary>A forward jump of more than this beyond monotonic elapsed time while running is suspicious.</summary>
    public static TimeSpan SuspiciousForwardJump { get; } = TimeSpan.FromHours(24);

    public static AdvisoryAiClockVerdict Evaluate(DateTimeOffset? lastAcceptedUtc, DateTimeOffset nowUtc)
    {
        if (lastAcceptedUtc is null) return AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation;
        if (nowUtc < lastAcceptedUtc.Value) return AdvisoryAiClockVerdict.MovedBackwards;
        return AdvisoryAiPeriod.Of(nowUtc) > AdvisoryAiPeriod.Of(lastAcceptedUtc.Value) ? AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation : AdvisoryAiClockVerdict.Accepted;
    }

    /// <summary>True when the wall clock advanced more than 24 hours further than the monotonic clock over the same interval.</summary>
    public static bool IsSuspiciousForwardJump(TimeSpan wallElapsed, TimeSpan monotonicElapsed) => wallElapsed - monotonicElapsed > SuspiciousForwardJump;
}

/// <summary>
/// FR-BUD-01, FR-DAT-12 (ADR-029 "Reset, restore and missing accounting continuity"): whether this PC's AI spending record can be
/// trusted to start from. Kept only in app state, so a factory reset removes it too.
/// </summary>
public enum AdvisoryAiContinuity
{
    /// <summary>No AI setup has happened on this app state: the owner is asked before the first paid request.</summary>
    NotEstablished,

    /// <summary>The owner declared PC Steward made no earlier AI requests on this PC. Labelled as the owner's declaration.</summary>
    DeclaredNoPriorUse,

    /// <summary>The owner said yes or unsure, or PC Steward recovered or found its accounting missing: paid requests wait.</summary>
    PriorUseUnknown,

    /// <summary>After <see cref="PriorUseUnknown"/>, a confirmed later UTC month started a fresh period for new requests only.</summary>
    FreshPeriodConfirmed
}

/// <summary>Why an AI request's cost is uncertain. Every reason keeps the whole reservation committed.</summary>
public enum AdvisoryAiUncertainReason
{
    None,

    /// <summary>The process ended after the dispatch claim, before an outcome was recorded.</summary>
    Interrupted,
    ConnectFailed,
    TimedOut,
    Cancelled,
    Redirected,
    ResponseTooLarge,
    TransportFailed,

    /// <summary>The transport reported nothing was sent, but only after the dispatch claim: that is not transactional proof.</summary>
    NotSentAfterClaim,
    MalformedResponse,
    UsageMissingOrUnreliable
}

/// <summary>How the transport ended, as the settlement rule needs it. Mirrors the Application transport outcome.</summary>
public enum AdvisoryAiDispatchOutcome
{
    ResponseReceived,
    NotSent,
    ConnectFailed,
    TimedOut,
    Cancelled,
    Redirected,
    ResponseTooLarge,
    Failed
}

/// <summary>
/// The ledger change after a dispatched request: the new state, the amount that stays committed (the actual cost when settled), and
/// whether paid requests must stop for review because the charge is above the reservation or usage is above the approved bounds.
/// </summary>
public sealed record AdvisoryAiSettlement(
    AdvisoryAiRequestState State,
    MicroUsd Committed,
    AdvisoryAiUncertainReason Reason,
    bool AboveReservation)
{
    public bool NeedsReview => AboveReservation;
}

/// <summary>
/// FR-BUD-02/03 (ADR-029 "Crash-safe state machine and reconciliation", F4): the one rule that turns a dispatched request's outcome
/// into a ledger change. Only a well-formed response with complete reliable usage settles, at the frozen rates; everything else keeps
/// the whole reservation. A charge above the reservation is recorded in full (never clamped) and blocks further paid requests, and so
/// does unreliable usage that would cost more than the reservation. Nothing here infers a free request from a status, an exception,
/// a missing body or elapsed time.
/// </summary>
public static class AdvisoryAiSettlementRule
{
    public static AdvisoryAiSettlement Decide(AdvisoryAiDispatchOutcome outcome, AdvisoryAiResponse? response, MicroUsd reservation, AdvisoryAiTariff tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);
        ArgumentOutOfRangeException.ThrowIfNegative(reservation.Value);
        if (outcome != AdvisoryAiDispatchOutcome.ResponseReceived) return Uncertain(reservation, ReasonFor(outcome));
        if (response is null) return Uncertain(reservation, AdvisoryAiUncertainReason.MalformedResponse);

        MicroUsd? reported = ReportedCost(response.Usage, tariff, out bool tooLargeToRecord);
        // Usage whose cost does not even fit the ledger is far above any reservation: keep the reservation and stop for review.
        if (tooLargeToRecord) return new(AdvisoryAiRequestState.Uncertain, reservation, AdvisoryAiUncertainReason.UsageMissingOrUnreliable, true);
        if (response.UsageReliable && response.Status != AdvisoryAiAnswerStatus.Malformed && reported is MicroUsd actual)
            return new(AdvisoryAiRequestState.Settled, actual, AdvisoryAiUncertainReason.None, actual > reservation || response.UsageAboveApprovedBounds);

        AdvisoryAiUncertainReason reason = response.Status == AdvisoryAiAnswerStatus.Malformed
            ? AdvisoryAiUncertainReason.MalformedResponse
            : AdvisoryAiUncertainReason.UsageMissingOrUnreliable;
        // Untrusted usage never lowers a commitment, but a larger figure is kept in full and stops paid requests for review.
        if (reported is MicroUsd larger && larger > reservation) return new(AdvisoryAiRequestState.Uncertain, larger, reason, true);
        return new(AdvisoryAiRequestState.Uncertain, reservation, reason, response.UsageAboveApprovedBounds);
    }

    /// <summary>The settlement a restart records for a request found in <see cref="AdvisoryAiRequestState.DispatchClaimed"/>.</summary>
    public static AdvisoryAiSettlement Interrupted(MicroUsd reservation) => Uncertain(reservation, AdvisoryAiUncertainReason.Interrupted);

    private static AdvisoryAiSettlement Uncertain(MicroUsd reservation, AdvisoryAiUncertainReason reason) =>
        new(AdvisoryAiRequestState.Uncertain, reservation, reason, false);

    private static MicroUsd? ReportedCost(AdvisoryAiUsage? usage, AdvisoryAiTariff tariff, out bool tooLargeToRecord)
    {
        tooLargeToRecord = false;
        if (usage is null || usage.InputTokens < 0 || usage.CachedInputTokens < 0 || usage.OutputTokens < 0 || usage.CachedInputTokens > usage.InputTokens)
            return null;
        try
        {
            return AdvisoryAiCost.Actual(usage, tariff);
        }
        catch (OverflowException)
        {
            tooLargeToRecord = true;
            return null;
        }
    }

    private static AdvisoryAiUncertainReason ReasonFor(AdvisoryAiDispatchOutcome outcome) => outcome switch
    {
        AdvisoryAiDispatchOutcome.NotSent => AdvisoryAiUncertainReason.NotSentAfterClaim,
        AdvisoryAiDispatchOutcome.ConnectFailed => AdvisoryAiUncertainReason.ConnectFailed,
        AdvisoryAiDispatchOutcome.TimedOut => AdvisoryAiUncertainReason.TimedOut,
        AdvisoryAiDispatchOutcome.Cancelled => AdvisoryAiUncertainReason.Cancelled,
        AdvisoryAiDispatchOutcome.Redirected => AdvisoryAiUncertainReason.Redirected,
        AdvisoryAiDispatchOutcome.ResponseTooLarge => AdvisoryAiUncertainReason.ResponseTooLarge,
        _ => AdvisoryAiUncertainReason.TransportFailed
    };
}
