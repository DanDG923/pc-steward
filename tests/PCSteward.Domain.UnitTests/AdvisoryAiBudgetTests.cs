using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-BUD-01/02/03 (ADR-029; Task 68 stage 2): checked, upward-rounded cost arithmetic; exact-cap and one-micro-dollar-over boundaries
/// for the request cap, the monthly budget and the cumulative $1 first-live-test envelope; owner-confirmed 24-hour prices; UTC months;
/// clock and continuity gates; the only state transitions; and settlement that never lowers a commitment without reliable usage.
/// Synthetic prices and model only: nothing here is a real rate or model.
/// </summary>
[TestClass]
public sealed class AdvisoryAiBudgetTests
{
    private static readonly AdvisoryAiModelProfile Model = AdvisoryAiPolicyTests.Profile;
    private static readonly DateTimeOffset Confirmed = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    // $2.00 per million input, $0.50 per million cached input, $8.00 per million output (synthetic).
    private static readonly AdvisoryAiTariff Tariff = new(1, Model.ModelId, Model.ServiceTier, new(2_000_000), new(500_000), new(8_000_000), Confirmed);

    private static readonly AdvisoryAiLedgerSnapshot Empty = new(MicroUsd.Zero, MicroUsd.Zero, MicroUsd.Zero, MicroUsd.Zero, false, false, false,
        AdvisoryAiContinuity.DeclaredNoPriorUse, AdvisoryAiClockVerdict.Accepted);

    private static AdvisoryAiReservationProblem Check(MicroUsd reservation, AdvisoryAiLedgerSnapshot? ledger = null, AdvisoryAiLimits? limits = null,
        AdvisoryAiPaidRequestMode mode = AdvisoryAiPaidRequestMode.Ordinary, DateTimeOffset? now = null, AdvisoryAiTariff? tariff = null) =>
        AdvisoryAiReservationRule.Check(reservation, limits ?? AdvisoryAiLimits.Defaults, ledger ?? Empty, mode, tariff ?? Tariff, Model, now ?? Confirmed.AddHours(1));

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow(0L, 1_000_000L, 0L)]
    [DataRow(1L, 1L, 1L)]
    [DataRow(1L, 999_999L, 1L)]
    [DataRow(1_000_000L, 1L, 1L)]
    [DataRow(1_000_001L, 1L, 2L)]
    [DataRow(8_192L, 2_000_000L, 16_384L)]
    [DataRow(3L, 333_333L, 1L)]
    public void EveryChargeIsRoundedUpToTheNextMicroDollar(long tokens, long rate, long expected) =>
        Assert.AreEqual(expected, AdvisoryAiCost.Charge(tokens, new(rate)).Value);

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void ArithmeticThatDoesNotFitIsRefusedNeverWrapped()
    {
        Assert.ThrowsExactly<OverflowException>(() => AdvisoryAiCost.Charge(long.MaxValue, new(long.MaxValue)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AdvisoryAiCost.Charge(-1, new(1)));
        Assert.ThrowsExactly<OverflowException>(() => MicroUsd.Add(new(long.MaxValue), new(1)));
        Assert.AreEqual(AdvisoryAiReservationProblem.AmountTooLarge,
            Check(new(1), Empty with { SettledInPeriod = new(long.MaxValue) }, AdvisoryAiLimits.Defaults with { MonthlyBudget = new(long.MaxValue) }));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void TheReservationIsEveryInputTokenAtTheHigherInputRateAndEveryOutputToken()
    {
        // 8,192 × $2/M = 16,384 µ$; 1,024 × $8/M = 8,192 µ$.
        Assert.AreEqual(24_576, AdvisoryAiCost.Reservation(8192, 1024, Tariff).Value);
        AdvisoryAiTariff cachedDearer = Tariff with { CachedInputPerMillion = new(3_000_000) };
        Assert.AreEqual(24_576 + 8_192, AdvisoryAiCost.Reservation(8192, 1024, cachedDearer).Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void ActualCostSplitsCachedInputAndCountsReasoningOnceAsOutput()
    {
        AdvisoryAiUsage usage = new(InputTokens: 1000, CachedInputTokens: 400, OutputTokens: 300, ReasoningTokens: 200, TotalTokens: 1300);
        // 600 × 2 = 1,200 µ$; 400 × 0.5 = 200 µ$; 300 × 8 = 2,400 µ$.
        Assert.AreEqual(3_800, AdvisoryAiCost.Actual(usage, Tariff).Value);
        Assert.ThrowsExactly<ArgumentException>(() => AdvisoryAiCost.Actual(usage with { CachedInputTokens = 1001 }, Tariff));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void ExactlyAtTheRequestCapIsAllowedAndOneMicroDollarOverIsNot()
    {
        AdvisoryAiLimits limits = AdvisoryAiLimits.Defaults;
        Assert.AreEqual(AdvisoryAiReservationProblem.None, Check(limits.RequestCap, limits: limits));
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveRequestCap, Check(new(limits.RequestCap.Value + 1), limits: limits));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void SettledAndOutstandingBothCountAgainstTheMonthlyBudget()
    {
        AdvisoryAiLimits limits = AdvisoryAiLimits.Defaults with { MonthlyBudget = new(1_000_000) };
        AdvisoryAiLedgerSnapshot ledger = Empty with { SettledInPeriod = new(700_000), OutstandingInPeriod = new(100_000) };
        Assert.AreEqual(AdvisoryAiReservationProblem.None, Check(new(200_000), ledger, limits));
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveMonthlyBudget, Check(new(200_001), ledger, limits));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void LoweringABudgetBelowCommitmentsKeepsThemAndBlocksNewRequestsAndRaisingItRefundsNothing()
    {
        AdvisoryAiLedgerSnapshot ledger = Empty with { SettledInPeriod = new(5_000_000), OutstandingInPeriod = new(250_000) };
        AdvisoryAiLimits lowered = AdvisoryAiLimits.Defaults with { MonthlyBudget = new(5_000_000) };
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveMonthlyBudget, Check(new(1), ledger, lowered));
        AdvisoryAiLimits raised = lowered with { MonthlyBudget = new(5_250_001) };
        Assert.AreEqual(AdvisoryAiReservationProblem.None, Check(new(1), ledger, raised));
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveMonthlyBudget, Check(new(2), ledger, raised));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void TheFirstLiveTestEnvelopeIsOneDollarAcrossEverythingAndOnlyInTestMode()
    {
        AdvisoryAiLedgerSnapshot ledger = Empty with { TestSettled = new(700_000), TestOutstanding = new(100_000) };
        Assert.AreEqual(AdvisoryAiReservationProblem.None, Check(new(200_000), ledger, mode: AdvisoryAiPaidRequestMode.FirstLiveTest));
        Assert.AreEqual(AdvisoryAiReservationProblem.AboveFirstLiveTestEnvelope, Check(new(200_001), ledger, mode: AdvisoryAiPaidRequestMode.FirstLiveTest));
        Assert.AreEqual(AdvisoryAiReservationProblem.None, Check(new(200_001), ledger, mode: AdvisoryAiPaidRequestMode.Ordinary));
        Assert.AreEqual(1_000_000, AdvisoryAiReservationRule.FirstLiveTestEnvelope.Value);
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void PaidRequestsAreRefusedWhileTheBuildOrTheSettingsTurnThemOff()
    {
        Assert.AreEqual(AdvisoryAiReservationProblem.PaidRequestsDisabledInThisBuild, Check(new(1), mode: AdvisoryAiPaidRequestMode.Disabled));
        Assert.AreEqual(AdvisoryAiReservationProblem.PaidRequestsDisabledInThisBuild, Check(new(1), mode: AdvisoryAiBuildPolicy.PaidRequests));
        Assert.AreEqual(AdvisoryAiReservationProblem.PaidRequestsDisabledBySettings, Check(new(1), limits: AdvisoryAiLimits.Defaults with { RequestCap = MicroUsd.Zero }));
        Assert.AreEqual(AdvisoryAiReservationProblem.PaidRequestsDisabledBySettings, Check(new(1), limits: AdvisoryAiLimits.Defaults with { MonthlyBudget = MicroUsd.Zero }));
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void OneRequestAtATimeAndAnyReviewOrFullLedgerBlocksNewRequests()
    {
        Assert.AreEqual(AdvisoryAiReservationProblem.AnotherRequestInFlight, Check(new(1), Empty with { RequestInFlight = true }));
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerNeedsReview, Check(new(1), Empty with { ReviewBlock = true }));
        Assert.AreEqual(AdvisoryAiReservationProblem.LedgerFull, Check(new(1), Empty with { LedgerFull = true }));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void ContinuityMustBeDeclaredAndUnknownPriorUseWaitsAndNeverReopensTheTest()
    {
        Assert.AreEqual(AdvisoryAiReservationProblem.ContinuityNotEstablished, Check(new(1), Empty with { Continuity = AdvisoryAiContinuity.NotEstablished }));
        Assert.AreEqual(AdvisoryAiReservationProblem.PriorUseUnknown, Check(new(1), Empty with { Continuity = AdvisoryAiContinuity.PriorUseUnknown }));
        AdvisoryAiLedgerSnapshot fresh = Empty with { Continuity = AdvisoryAiContinuity.FreshPeriodConfirmed };
        Assert.AreEqual(AdvisoryAiReservationProblem.None, Check(new(1), fresh));
        Assert.AreEqual(AdvisoryAiReservationProblem.TestSpendingUnknown, Check(new(1), fresh, mode: AdvisoryAiPaidRequestMode.FirstLiveTest));
        Assert.AreEqual(AdvisoryAiReservationProblem.ContinuityNotEstablished, Check(new(1), Empty with { Continuity = (AdvisoryAiContinuity)99 }));
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void PricesLastTwentyFourHoursFromConfirmationAndNoLonger()
    {
        Assert.IsTrue(Tariff.IsCurrent(Confirmed));
        Assert.IsTrue(Tariff.IsCurrent(Confirmed.AddHours(24).AddTicks(-1)));
        Assert.IsFalse(Tariff.IsCurrent(Confirmed.AddHours(24)));
        Assert.IsFalse(Tariff.IsCurrent(Confirmed.AddTicks(-1)), "a clock before the confirmation is not current");
        Assert.AreEqual(AdvisoryAiReservationProblem.TariffNotCurrent, Check(new(1), now: Confirmed.AddHours(24)));
        Assert.IsTrue(new AdvisoryAiTariff(1, Model.ModelId, Model.ServiceTier, new(1), new(1), new(1), new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(1)))
            .IsCurrent(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero)), "offsets compare as instants");
    }

    [TestMethod]
    [TestCategory("FR-BUD-01")]
    public void APriceIsForExactlyThisModelAndTierAndNoRateIsFree()
    {
        Assert.AreEqual(AdvisoryAiTariffProblem.None, Tariff.Validate(Model));
        Assert.AreEqual(AdvisoryAiTariffProblem.InvalidVersion, (Tariff with { Version = 0 }).Validate(Model));
        Assert.AreEqual(AdvisoryAiTariffProblem.WrongModelOrTier, (Tariff with { ModelId = "synthetic-model" }).Validate(Model));
        Assert.AreEqual(AdvisoryAiTariffProblem.WrongModelOrTier, (Tariff with { ModelId = "SYNTHETIC-MODEL-2026-01-01" }).Validate(Model));
        Assert.AreEqual(AdvisoryAiTariffProblem.WrongModelOrTier, (Tariff with { ServiceTier = "priority" }).Validate(Model));
        Assert.AreEqual(AdvisoryAiTariffProblem.MissingOrZeroRate, (Tariff with { CachedInputPerMillion = MicroUsd.Zero }).Validate(Model));
        Assert.AreEqual(AdvisoryAiTariffProblem.MissingOrZeroRate, (Tariff with { OutputPerMillion = new(-1) }).Validate(Model));
        Assert.AreEqual(AdvisoryAiReservationProblem.TariffInvalid, Check(new(1), tariff: Tariff with { ServiceTier = "flex" }));
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void AMonthIsTheUtcCalendarMonthWhateverTheLocalOffset()
    {
        Assert.AreEqual(new AdvisoryAiPeriod(2026, 11), AdvisoryAiPeriod.Of(new DateTimeOffset(2026, 11, 30, 23, 59, 59, TimeSpan.Zero)));
        Assert.AreEqual(new AdvisoryAiPeriod(2026, 12), AdvisoryAiPeriod.Of(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero)));
        // 00:30 on 1 December at UTC+1 is still November in UTC; 23:30 on 30 November at UTC−5 is already December.
        Assert.AreEqual(new AdvisoryAiPeriod(2026, 11), AdvisoryAiPeriod.Of(new DateTimeOffset(2026, 12, 1, 0, 30, 0, TimeSpan.FromHours(1))));
        Assert.AreEqual(new AdvisoryAiPeriod(2026, 12), AdvisoryAiPeriod.Of(new DateTimeOffset(2026, 11, 30, 23, 30, 0, TimeSpan.FromHours(-5))));
        AdvisoryAiPeriod december = new(2026, 12);
        Assert.AreEqual(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), december.StartUtc);
        Assert.AreEqual(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), december.EndUtc);
        Assert.IsTrue(new AdvisoryAiPeriod(2026, 12) < new AdvisoryAiPeriod(2027, 1));
        Assert.AreEqual("2026-12", december.ToString());
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    [DataRow(202611L, true)]
    [DataRow(202600L, false)]
    [DataRow(202613L, false)]
    [DataRow(199912L, false)]
    [DataRow(1_000_001L, false)]
    [DataRow(-202611L, false)]
    public void AStoredPeriodKeyIsCheckedNotTrusted(long key, bool valid)
    {
        Assert.AreEqual(valid, AdvisoryAiPeriod.TryParseKey(key, out AdvisoryAiPeriod period));
        if (valid) Assert.AreEqual(key, period.Key);
    }

    [TestMethod]
    [TestCategory("FR-BUD-03")]
    public void TheClockMayNotMoveBackAndANewMonthIsConfirmedBeforeUse()
    {
        DateTimeOffset last = new(2026, 11, 30, 23, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(AdvisoryAiClockVerdict.Accepted, AdvisoryAiClock.Evaluate(last, last));
        Assert.AreEqual(AdvisoryAiClockVerdict.Accepted, AdvisoryAiClock.Evaluate(last, last.AddMinutes(59)));
        Assert.AreEqual(AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation, AdvisoryAiClock.Evaluate(last, last.AddHours(1)));
        Assert.AreEqual(AdvisoryAiClockVerdict.MovedBackwards, AdvisoryAiClock.Evaluate(last, last.AddTicks(-1)));
        Assert.AreEqual(AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation, AdvisoryAiClock.Evaluate(null, last));
        Assert.AreEqual(AdvisoryAiReservationProblem.ClockMovedBackwards, Check(new(1), Empty with { Clock = AdvisoryAiClockVerdict.MovedBackwards }));
        Assert.AreEqual(AdvisoryAiReservationProblem.NewMonthNeedsConfirmation, Check(new(1), Empty with { Clock = AdvisoryAiClockVerdict.NewPeriodNeedsConfirmation }));
        Assert.IsFalse(AdvisoryAiClock.IsSuspiciousForwardJump(TimeSpan.FromHours(30), TimeSpan.FromHours(6)));
        Assert.IsTrue(AdvisoryAiClock.IsSuspiciousForwardJump(TimeSpan.FromHours(30).Add(TimeSpan.FromTicks(1)), TimeSpan.FromHours(6)));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void OnlyTheAcceptedTransitionsExist()
    {
        (AdvisoryAiRequestState From, AdvisoryAiRequestState To)[] allowed =
        [
            (AdvisoryAiRequestState.Reserved, AdvisoryAiRequestState.NotSent),
            (AdvisoryAiRequestState.Reserved, AdvisoryAiRequestState.DispatchClaimed),
            (AdvisoryAiRequestState.DispatchClaimed, AdvisoryAiRequestState.Settled),
            (AdvisoryAiRequestState.DispatchClaimed, AdvisoryAiRequestState.Uncertain)
        ];
        foreach (AdvisoryAiRequestState from in Enum.GetValues<AdvisoryAiRequestState>())
        foreach (AdvisoryAiRequestState to in Enum.GetValues<AdvisoryAiRequestState>())
            Assert.AreEqual(allowed.Contains((from, to)), AdvisoryAiRequestStates.CanMove(from, to), $"{from} -> {to}");
        Assert.IsFalse(AdvisoryAiRequestStates.HoldsReservation(AdvisoryAiRequestState.NotSent));
        Assert.IsFalse(AdvisoryAiRequestStates.HoldsReservation(AdvisoryAiRequestState.Settled));
        Assert.IsTrue(AdvisoryAiRequestStates.HoldsReservation(AdvisoryAiRequestState.Uncertain));
        Assert.IsFalse(AdvisoryAiRequestStates.IsInFlight(AdvisoryAiRequestState.Uncertain), "an uncertain charge stays owed but does not block the next request");
    }

    private static readonly AdvisoryAiUsage Usage = new(1000, 0, 500, 0, 1500);

    private static AdvisoryAiResponse Response(AdvisoryAiAnswerStatus status, AdvisoryAiUsage? usage, bool reliable, bool above = false) =>
        new(status, AdvisoryAiIncompleteReason.None, status == AdvisoryAiAnswerStatus.Malformed ? AdvisoryAiMalformedReason.UnexpectedOutputItem : AdvisoryAiMalformedReason.None,
            "resp_synthetic", "text", usage, reliable, above);

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow(AdvisoryAiAnswerStatus.Completed)]
    [DataRow(AdvisoryAiAnswerStatus.Incomplete)]
    [DataRow(AdvisoryAiAnswerStatus.Refused)]
    [DataRow(AdvisoryAiAnswerStatus.ProviderFailed)]
    public void ReliableUsageSettlesAtTheFrozenRatesWhateverTheAnswerStatus(AdvisoryAiAnswerStatus status)
    {
        AdvisoryAiSettlement settled = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, Response(status, Usage, true), new(24_576), Tariff);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, settled.State);
        Assert.AreEqual(6_000, settled.Committed.Value);
        Assert.IsFalse(settled.NeedsReview);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    [DataRow(AdvisoryAiDispatchOutcome.NotSent, AdvisoryAiUncertainReason.NotSentAfterClaim)]
    [DataRow(AdvisoryAiDispatchOutcome.ConnectFailed, AdvisoryAiUncertainReason.ConnectFailed)]
    [DataRow(AdvisoryAiDispatchOutcome.TimedOut, AdvisoryAiUncertainReason.TimedOut)]
    [DataRow(AdvisoryAiDispatchOutcome.Cancelled, AdvisoryAiUncertainReason.Cancelled)]
    [DataRow(AdvisoryAiDispatchOutcome.Redirected, AdvisoryAiUncertainReason.Redirected)]
    [DataRow(AdvisoryAiDispatchOutcome.ResponseTooLarge, AdvisoryAiUncertainReason.ResponseTooLarge)]
    [DataRow(AdvisoryAiDispatchOutcome.Failed, AdvisoryAiUncertainReason.TransportFailed)]
    public void WithoutAResponseTheWholeReservationStaysCommitted(AdvisoryAiDispatchOutcome outcome, AdvisoryAiUncertainReason reason)
    {
        AdvisoryAiSettlement uncertain = AdvisoryAiSettlementRule.Decide(outcome, null, new(24_576), Tariff);
        Assert.AreEqual(new AdvisoryAiSettlement(AdvisoryAiRequestState.Uncertain, new(24_576), reason, false), uncertain);
        Assert.AreEqual(new AdvisoryAiSettlement(AdvisoryAiRequestState.Uncertain, new(24_576), AdvisoryAiUncertainReason.Interrupted, false),
            AdvisoryAiSettlementRule.Interrupted(new(24_576)));
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void UnreliableUsageNeverLowersACommitmentButALargerFigureIsKeptAndStopsForReview()
    {
        AdvisoryAiSettlement missing = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, Response(AdvisoryAiAnswerStatus.Completed, null, false), new(24_576), Tariff);
        Assert.AreEqual(new AdvisoryAiSettlement(AdvisoryAiRequestState.Uncertain, new(24_576), AdvisoryAiUncertainReason.UsageMissingOrUnreliable, false), missing);

        AdvisoryAiSettlement smaller = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, Response(AdvisoryAiAnswerStatus.Malformed, Usage, false), new(24_576), Tariff);
        Assert.AreEqual(new AdvisoryAiSettlement(AdvisoryAiRequestState.Uncertain, new(24_576), AdvisoryAiUncertainReason.MalformedResponse, false), smaller);

        AdvisoryAiSettlement larger = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, Response(AdvisoryAiAnswerStatus.Malformed, Usage, false), new(5_000), Tariff);
        Assert.AreEqual(new AdvisoryAiSettlement(AdvisoryAiRequestState.Uncertain, new(6_000), AdvisoryAiUncertainReason.MalformedResponse, true), larger);

        AdvisoryAiSettlement huge = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived,
            Response(AdvisoryAiAnswerStatus.Completed, new(long.MaxValue, 0, long.MaxValue, 0, long.MaxValue), false), new(5_000), Tariff);
        Assert.AreEqual(new AdvisoryAiSettlement(AdvisoryAiRequestState.Uncertain, new(5_000), AdvisoryAiUncertainReason.UsageMissingOrUnreliable, true), huge);
    }

    [TestMethod]
    [TestCategory("FR-BUD-02")]
    public void AChargeAboveTheReservationIsRecordedInFullAndStopsPaidRequests()
    {
        AdvisoryAiSettlement above = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, Response(AdvisoryAiAnswerStatus.Completed, Usage, true), new(5_999), Tariff);
        Assert.AreEqual(AdvisoryAiRequestState.Settled, above.State);
        Assert.AreEqual(6_000, above.Committed.Value, "never clamped to the reservation");
        Assert.IsTrue(above.NeedsReview);

        AdvisoryAiSettlement exact = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived, Response(AdvisoryAiAnswerStatus.Completed, Usage, true), new(6_000), Tariff);
        Assert.IsFalse(exact.NeedsReview);

        AdvisoryAiSettlement boundsBreached = AdvisoryAiSettlementRule.Decide(AdvisoryAiDispatchOutcome.ResponseReceived,
            Response(AdvisoryAiAnswerStatus.Completed, Usage, true, above: true), new(24_576), Tariff);
        Assert.IsTrue(boundsBreached.NeedsReview, "usage above the approved token bounds stops paid requests even when the cost fits");
    }
}
