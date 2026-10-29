using System.Text.Json;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
[TestCategory("FR-UPD-14")]
public sealed class UpdateProgressTests
{
    private static readonly Guid Job = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Correlation = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly string[] ExpectedFields = ["jobId", "correlationId", "sequence", "phase", "finalStatus", "completion", "degraded"];

    [TestMethod]
    public void UnreadProgressCoalescesAndReservedTerminalSurvivesSealing()
    {
        UpdateProgressMailbox mailbox = Create();
        Assert.AreEqual(UpdateProgressOfferResult.Accepted, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued));
        Assert.AreEqual(UpdateProgressOfferResult.Coalesced, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Preparing));
        Assert.AreEqual(UpdateProgressOfferResult.Coalesced, mailbox.Offer(Job, Correlation, UpdateProgressPhase.ProcessStarted));
        Assert.AreEqual(UpdateProgressOfferResult.Coalesced, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Verifying));
        Assert.IsTrue(mailbox.Complete(UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded));
        mailbox.SealProducer();

        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateProgressPhase.Final, frame!.Phase);
        Assert.AreEqual(UpdateHistoryStatus.Success, frame.RecordedStatus);
        Assert.AreEqual(1U, frame.Sequence);
        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.IsNotNull(mailbox.RetainedTerminal);
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
    }

    [TestMethod]
    public void EmissionUsesMonotonicSpacingWithNoCatchUpAndNoCoalescingGaps()
    {
        ManualClock clock = new();
        UpdateProgressMailbox mailbox = Create(clock: clock);
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? first));
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Preparing);
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.ProcessStarted);
        clock.Advance(249);
        Assert.IsFalse(mailbox.TryTake(out _));
        clock.Advance(1);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? second));
        Assert.AreEqual(UpdateProgressPhase.ProcessStarted, second!.Phase);
        Assert.AreEqual(first!.Sequence + 1, second.Sequence);
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Verifying);
        clock.Advance(10_000);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? third));
        mailbox.Complete(UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded);
        Assert.IsFalse(mailbox.TryTake(out _), "Elapsed idle time must not permit a catch-up burst.");
        clock.Advance(250);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? terminal));
        Assert.AreEqual(third!.Sequence + 1, terminal!.Sequence);
    }

    [TestMethod]
    public void DuplicateFloodChargesAttemptsButForeignIdentityCannotCancelThisItem()
    {
        UpdateProgressMailbox mailbox = Create();
        Guid foreign = Guid.Parse("33333333-3333-3333-3333-333333333333");
        for (int i = 0; i < 10_000; i++)
            Assert.AreEqual(UpdateProgressOfferResult.RejectedForeign, mailbox.Offer(foreign, Correlation, UpdateProgressPhase.Queued));
        Assert.AreEqual(0, mailbox.Counters.Attempts);
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued);
        for (int i = 1; i < 255; i++)
            Assert.AreEqual(UpdateProgressOfferResult.RejectedInvalid, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued));
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
        Assert.AreEqual(UpdateProgressOfferResult.CapacityFaulted, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued));
        Assert.IsTrue(mailbox.CapacityFault.IsCompletedSuccessfully);
        int attempts = mailbox.Counters.Attempts;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++) mailbox.Offer(Job, Correlation, UpdateProgressPhase.Preparing);
        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.AreEqual(attempts, mailbox.Counters.Attempts);
        Assert.IsTrue(mailbox.Complete(UpdateHistoryStatus.VerificationRequired, UpdateProgressCompletion.Recorded));
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.IsTrue(frame!.Degraded);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, frame.RecordedStatus);
    }

    [TestMethod]
    public void TerminalClosesProductionAndCannotBeOverwrittenOrRetroactivelyCancelled()
    {
        UpdateProgressMailbox mailbox = Create();
        Assert.IsTrue(mailbox.Complete(UpdateHistoryStatus.ProductFailed, UpdateProgressCompletion.Recorded));
        Assert.IsFalse(mailbox.Complete(UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded));
        for (int i = 0; i < 1_000; i++)
            Assert.AreEqual(UpdateProgressOfferResult.Closed, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued));
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, frame!.RecordedStatus);
    }

    [TestMethod]
    [DataRow(UpdateScope.User, UpdateProgressPhase.ElevationRequested)]
    [DataRow(UpdateScope.User, UpdateProgressPhase.HandedToHelper)]
    [DataRow(UpdateScope.Machine, UpdateProgressPhase.ProcessStarted)]
    public void RouteForbiddenOrUnobservedPhasesAreRejectedWithoutCancellation(UpdateScope scope, UpdateProgressPhase forbidden)
    {
        UpdateProgressMailbox mailbox = Create(scope);
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued);
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Preparing);
        Assert.AreEqual(UpdateProgressOfferResult.RejectedInvalid, mailbox.Offer(Job, Correlation, forbidden));
        Assert.AreEqual(UpdateProgressOfferResult.RejectedInvalid, mailbox.Offer(Job, Correlation, UpdateProgressPhase.Verifying));
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
    }

    [TestMethod]
    public void MachineRouteReportsOnlyAuthenticatedRequestHandoffAndAllowsVerification()
    {
        ManualClock clock = new();
        UpdateProgressMailbox mailbox = Create(UpdateScope.Machine, clock);
        UpdateProgressPhase[] phases = [UpdateProgressPhase.Queued, UpdateProgressPhase.Preparing,
            UpdateProgressPhase.ElevationRequested, UpdateProgressPhase.HandedToHelper, UpdateProgressPhase.Verifying];
        uint sequence = 0;
        foreach (UpdateProgressPhase phase in phases)
        {
            Assert.AreEqual(UpdateProgressOfferResult.Accepted, mailbox.Offer(Job, Correlation, phase));
            Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
            Assert.AreEqual(phase, frame!.Phase);
            Assert.AreEqual(++sequence, frame.Sequence);
            clock.Advance(250);
        }
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
    }

    [TestMethod]
    public void RecordingFailureHasNoInventedRecordedOutcome()
    {
        UpdateProgressMailbox mailbox = Create();
        Assert.IsFalse(mailbox.Complete(UpdateHistoryStatus.Success, UpdateProgressCompletion.RecordingFailed));
        Assert.IsTrue(mailbox.Complete(null, UpdateProgressCompletion.RecordingFailed));
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.IsNull(frame!.RecordedStatus);
        Assert.AreEqual(UpdateProgressCompletion.RecordingFailed, frame.Completion);
    }

    [TestMethod]
    public void CorrelationMismatchAndUnknownPhaseCannotBecomeObservedMilestones()
    {
        UpdateProgressMailbox mailbox = Create();
        Assert.AreEqual(UpdateProgressOfferResult.RejectedForeign,
            mailbox.Offer(Job, Guid.Empty, UpdateProgressPhase.Queued));
        Assert.AreEqual(0, mailbox.Counters.Attempts);
        Assert.AreEqual(UpdateProgressOfferResult.RejectedInvalid,
            mailbox.Offer(Job, Correlation, (UpdateProgressPhase)int.MaxValue));
        Assert.IsFalse(mailbox.TryTake(out _));
        Assert.AreEqual(UpdateProgressOfferResult.Accepted,
            mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued));
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
    }

    [TestMethod]
    public void MachineEmissionsRespectEveryHalfOpenSecondAcross999And1000Milliseconds()
    {
        ManualClock clock = new();
        UpdateProgressMailbox mailbox = Create(UpdateScope.Machine, clock);
        List<long> emissions = [];
        UpdateProgressPhase[] phases = [UpdateProgressPhase.Queued, UpdateProgressPhase.Preparing,
            UpdateProgressPhase.ElevationRequested, UpdateProgressPhase.HandedToHelper, UpdateProgressPhase.Verifying];
        foreach (UpdateProgressPhase phase in phases)
        {
            mailbox.Offer(Job, Correlation, phase);
            Assert.IsTrue(mailbox.TryTake(out _));
            emissions.Add(clock.GetTimestamp());
            clock.Advance(250);
        }
        Assert.AreEqual(4, emissions.Count(value => value >= 0 && value < 1_000));
        Assert.AreEqual(4, emissions.Count(value => value >= 1 && value < 1_001));
        Assert.AreEqual(1, emissions.Count(value => value >= 999 && value < 1_001));
        foreach (long start in emissions)
            Assert.IsTrue(emissions.Count(value => value >= start && value < start + 1_000) <= 4);
    }

    [TestMethod]
    public async Task TerminalRacingANonterminalIsRetainedAndClosesAllFutureOffers()
    {
        UpdateProgressMailbox mailbox = Create();
        mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued);
        await Task.WhenAll(
            Task.Run(() => mailbox.Offer(Job, Correlation, UpdateProgressPhase.Preparing)),
            Task.Run(() => mailbox.Complete(UpdateHistoryStatus.VerificationRequired, UpdateProgressCompletion.Recorded)));
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateProgressPhase.Final, frame!.Phase);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, frame.RecordedStatus);
        Assert.AreEqual(UpdateProgressOfferResult.Closed, mailbox.Offer(Job, Correlation, UpdateProgressPhase.ProcessStarted));
        Assert.IsFalse(mailbox.CapacityFault.IsCompleted);
    }

    [TestMethod]
    public void RealByteQuotaHonoursFramePendingAndCumulativeExactBoundaries()
    {
        UpdateProgressQuota frameQuota = new(512);
        Assert.IsTrue(frameQuota.Reserve(new byte[16_384], 32));
        Assert.IsFalse(new UpdateProgressQuota(512).Reserve(new byte[16_385], 1));
        Assert.IsFalse(new UpdateProgressQuota(512).Reserve(new byte[1], 33));
        UpdateProgressQuota total = new(512);
        int remaining = 1_048_576 - 512;
        while (remaining > 0)
        {
            int size = Math.Min(remaining, 16_384);
            Assert.IsTrue(total.Reserve(new byte[size], 2));
            remaining -= size;
        }
        Assert.IsFalse(total.Reserve(new byte[1], 2));
    }

    [TestMethod]
    public void CanonicalEncodingHasClosedFieldsAndProvesProductionByteBounds()
    {
        int maximum = 0;
        foreach (UpdateProgressPhase phase in Enum.GetValues<UpdateProgressPhase>())
        foreach (UpdateHistoryStatus status in Enum.GetValues<UpdateHistoryStatus>())
        foreach (UpdateProgressCompletion completion in Enum.GetValues<UpdateProgressCompletion>())
        {
            byte[] encoded = UpdateProgressEncoding.Encode(new(Job, Correlation, 256, phase, status, completion, true));
            maximum = Math.Max(maximum, encoded.Length);
            using JsonDocument json = JsonDocument.Parse(encoded);
            string[] fields = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
            CollectionAssert.AreEqual(ExpectedFields, fields);
        }
        Assert.IsTrue(maximum <= 512);
        Assert.IsTrue(maximum * 256 <= 1_048_576);
        Assert.IsFalse(typeof(UpdateProgressFrame).GetProperties().Any(property =>
            property.PropertyType == typeof(string) || property.PropertyType == typeof(double) ||
            property.Name.Contains("Percent", StringComparison.OrdinalIgnoreCase)));
    }

    private static UpdateProgressMailbox Create(UpdateScope scope = UpdateScope.User, ManualClock? clock = null) =>
        new(Job, Correlation, scope, clock ?? new ManualClock());

    private sealed class ManualClock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance(long value) => milliseconds += value;
    }
}
