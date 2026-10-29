using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class UpdateProgressPresentationTests
{
    private static readonly Guid Job = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Correlation = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static UpdateProgressFrame Frame(uint sequence, UpdateProgressPhase phase,
        UpdateHistoryStatus? status = null, UpdateProgressCompletion? completion = null) =>
        new(Job, Correlation, sequence, phase, status, completion, false);

    [TestMethod]
    [Description("FR-UPD-14; FR-UPD-06: real synthetic mailbox coalescing and delayed terminal remain valid for both routes.")]
    public void RateLimitedRetainedTerminalNeedsNoFabricatedSequence()
    {
        foreach (UpdateScope scope in new[] { UpdateScope.User, UpdateScope.Machine })
        {
            ManualClock clock = new();
            UpdateProgressMailbox mailbox = new(Job, Correlation, scope, clock);
            UpdateProgressPresentation consumer = new(Job, Correlation, scope);
            mailbox.Offer(Job, Correlation, UpdateProgressPhase.Queued);
            mailbox.Offer(Job, Correlation, UpdateProgressPhase.Preparing);
            if (scope == UpdateScope.Machine)
            {
                mailbox.Offer(Job, Correlation, UpdateProgressPhase.ElevationRequested);
                mailbox.Offer(Job, Correlation, UpdateProgressPhase.HandedToHelper);
            }
            else mailbox.Offer(Job, Correlation, UpdateProgressPhase.ProcessStarted);
            Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? observed));
            Assert.IsTrue(consumer.Consume(observed!));
            mailbox.Offer(Job, Correlation, UpdateProgressPhase.Verifying);
            mailbox.Complete(UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded);
            Assert.IsFalse(mailbox.TryTake(out _));
            Assert.IsFalse(consumer.IsFinal);
            clock.Advance();
            Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? terminal));
            Assert.AreEqual(2U, terminal!.Sequence);
            Assert.IsTrue(consumer.Consume(terminal));
            Assert.IsTrue(consumer.IsFinal);
            Assert.IsFalse(mailbox.TryTake(out _));
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => milliseconds;
        internal void Advance() => milliseconds += 250;
    }

    [TestMethod]
    [Description("FR-UPD-14; FR-UPD-01–11; FR-UI-01–07: observed user route stays opaque and only recorded verified success completes.")]
    public void UserRouteIsIndeterminateAndCompletesOnlyOnRecordedSuccess()
    {
        UpdateProgressPresentation consumer = new(Job, Correlation, UpdateScope.User);
        Assert.IsTrue(consumer.Consume(Frame(1, UpdateProgressPhase.Queued)));
        Assert.IsTrue(consumer.Consume(Frame(2, UpdateProgressPhase.Preparing)));
        Assert.IsTrue(consumer.Consume(Frame(3, UpdateProgressPhase.ProcessStarted)));
        StringAssert.Contains(consumer.Label, "Provider client started");
        StringAssert.Contains(consumer.Label, "installer progress indeterminate");
        Assert.IsTrue(consumer.IsIndeterminate);
        Assert.IsTrue(consumer.Consume(Frame(4, UpdateProgressPhase.Verifying)));
        Assert.AreEqual("Verifying installed version", consumer.Label);
        Assert.IsTrue(consumer.Consume(Frame(5, UpdateProgressPhase.Final, UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded)));
        Assert.AreEqual("Complete — expected version verified", consumer.Label);
        Assert.IsTrue(consumer.IsFinal);
    }

    [TestMethod]
    [Description("FR-UPD-14: coalescing permits skipped phases, never claims helper acceptance or provider start.")]
    public void MachineRouteAllowsCoalescingAndTerminalAsFirstFrame()
    {
        UpdateProgressPresentation consumer = new(Job, Correlation, UpdateScope.Machine);
        Assert.IsTrue(consumer.Consume(Frame(1, UpdateProgressPhase.HandedToHelper)));
        Assert.AreEqual("Update request sent; helper validation and provider execution indeterminate", consumer.Label);
        Assert.IsTrue(consumer.IsIndeterminate);
        Assert.IsTrue(consumer.Consume(Frame(2, UpdateProgressPhase.Final, UpdateHistoryStatus.RestartRequired, UpdateProgressCompletion.Recorded)));
        Assert.AreEqual("Restart required", consumer.Label);
        UpdateProgressPresentation fast = new(Job, Correlation, UpdateScope.User);
        Assert.IsTrue(fast.Consume(Frame(1, UpdateProgressPhase.Final, UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded)));
    }

    [TestMethod]
    [Description("FR-UPD-14; NFR-OBS-01–05: reject foreign, gaps, duplicates, regression, route conflict, invalid schema and duplicate terminal.")]
    public void InvalidFramesLatchProgressUnavailable()
    {
        UpdateProgressFrame[] invalid =
        [
            Frame(1, UpdateProgressPhase.Preparing) with { JobId = Guid.NewGuid() },
            Frame(1, UpdateProgressPhase.Preparing) with { CorrelationId = Guid.NewGuid() },
            Frame(0, UpdateProgressPhase.Preparing), Frame(2, UpdateProgressPhase.Preparing),
            Frame(1, UpdateProgressPhase.ElevationRequested), Frame(1, UpdateProgressPhase.HandedToHelper),
            Frame(1, (UpdateProgressPhase)99),
            Frame(1, UpdateProgressPhase.Preparing, UpdateHistoryStatus.Success),
            Frame(1, UpdateProgressPhase.Final),
            Frame(1, UpdateProgressPhase.Final, UpdateHistoryStatus.Running, UpdateProgressCompletion.Recorded),
            Frame(1, UpdateProgressPhase.Final, UpdateHistoryStatus.Success, UpdateProgressCompletion.RecordingFailed),
            Frame(1, UpdateProgressPhase.Final, (UpdateHistoryStatus)99, UpdateProgressCompletion.Recorded),
            Frame(1, UpdateProgressPhase.Final, null, (UpdateProgressCompletion)99),
            Frame(1, UpdateProgressPhase.Preparing) with { Degraded = true }
        ];
        foreach (UpdateProgressFrame frame in invalid)
        {
            UpdateProgressPresentation consumer = new(Job, Correlation, UpdateScope.User);
            Assert.IsFalse(consumer.Consume(frame), frame.ToString());
            Assert.IsTrue(consumer.IsUnavailable);
            Assert.AreEqual("Progress unavailable", consumer.Label);
            Assert.IsFalse(consumer.Consume(Frame(1, UpdateProgressPhase.Final, UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded)));
        }
        foreach (UpdateProgressFrame second in new[] { Frame(1, UpdateProgressPhase.Verifying), Frame(2, UpdateProgressPhase.Queued), Frame(2, UpdateProgressPhase.Preparing) })
        {
            UpdateProgressPresentation consumer = new(Job, Correlation, UpdateScope.User);
            Assert.IsTrue(consumer.Consume(Frame(1, UpdateProgressPhase.Preparing)));
            Assert.IsFalse(consumer.Consume(second));
        }
        UpdateProgressPresentation machine = new(Job, Correlation, UpdateScope.Machine);
        Assert.IsFalse(machine.Consume(Frame(1, UpdateProgressPhase.ProcessStarted)));
        UpdateProgressPresentation terminal = new(Job, Correlation, UpdateScope.User);
        Assert.IsTrue(terminal.Consume(Frame(1, UpdateProgressPhase.Final, null, UpdateProgressCompletion.RecordingFailed)));
        Assert.IsFalse(terminal.Consume(Frame(2, UpdateProgressPhase.Final, UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded)));
    }

    [TestMethod]
    [Description("FR-UPD-14; FR-UPD-06: failed recording, acknowledgment and all non-success history never display Complete.")]
    public void NonSuccessAndDegradedNeverClaimComplete()
    {
        foreach (UpdateHistoryStatus status in Enum.GetValues<UpdateHistoryStatus>().Where(status => status is not
            (UpdateHistoryStatus.Success or UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.RunningApplicationWarning)))
        {
            UpdateProgressPresentation consumer = new(Job, Correlation, UpdateScope.User);
            Assert.IsTrue(consumer.Consume(Frame(1, UpdateProgressPhase.Final, status, UpdateProgressCompletion.Recorded)));
            Assert.IsFalse(consumer.Label.StartsWith("Complete", StringComparison.Ordinal));
        }
        foreach (UpdateProgressCompletion completion in new[] { UpdateProgressCompletion.RecordingFailed, UpdateProgressCompletion.AcknowledgementRequired })
        {
            UpdateProgressPresentation consumer = new(Job, Correlation, UpdateScope.User);
            Assert.IsTrue(consumer.Consume(Frame(1, UpdateProgressPhase.Final, null, completion)));
            Assert.IsFalse(consumer.Label.StartsWith("Complete", StringComparison.Ordinal));
        }
        UpdateProgressPresentation degraded = new(Job, Correlation, UpdateScope.User);
        degraded.MarkUnavailable();
        Assert.AreEqual("Progress unavailable", degraded.Label);
        Assert.IsFalse(degraded.Consume(Frame(1, UpdateProgressPhase.Final, UpdateHistoryStatus.Success, UpdateProgressCompletion.Recorded)));
    }
}
