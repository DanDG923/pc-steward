using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

[TestClass]
[TestCategory("FR-UPD-14")]
[TestCategory("FR-UPD-03")]
public sealed class Task49MilestoneTests
{
    [TestMethod]
    public async Task UserMilestoneFlowsThroughApprovedRunnerAndConstrainedProviderOnlyAfterValidation()
    {
        ImmutableUpdateJob job = Job(UpdateScope.User);
        UpdateProgressMailbox mailbox = Prepared(job);
        FixtureProcess process = new();
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetReviewedUserUpdateExecutor(process)));
        UpdateProviderAttempt result = await executor.ExecuteAsync(job, Reviewed(job), new(job.Selection, true, true), new UpdateItemProgress(job, mailbox), CancellationToken.None);
        Assert.AreEqual(UpdateProviderAttemptStatus.AwaitingVerification, result.Status);
        Assert.AreEqual(1, process.Starts);
        Assert.AreEqual(3, mailbox.Counters.Attempts);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateProgressPhase.ProcessStarted, frame!.Phase);
        Assert.IsNull(frame.RecordedStatus);

        ImmutableUpdateJob rejected = Job(UpdateScope.User);
        UpdateProgressMailbox rejectedMailbox = Prepared(rejected);
        UpdateProviderAttempt denial = await executor.ExecuteAsync(rejected, Reviewed(rejected),
            new(rejected.Selection, false, true), new UpdateItemProgress(rejected, rejectedMailbox), CancellationToken.None);
        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, denial.Status);
        Assert.AreEqual(1, process.Starts);
        Assert.AreEqual(2, rejectedMailbox.Counters.Attempts);
    }

    [TestMethod]
    public async Task PreCancelledMachineRouteDoesNotLaunchOrPublishElevation()
    {
        // D4b stage 3: the whole-PC route is the protocol-2 exchange; a pre-cancelled run never reaches it or publishes elevation.
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = WingetMachineReviewedDispatchTests.Frozen();
        UpdateProgressMailbox mailbox = Prepared(job);
        CountingExchange exchange = new();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        UpdateExecutionResult result = await new ElevatedUpdateRunner(exchange).RunReviewedAsync(job, operation, new(job.Selection, true, true),
            cancellation.Token, new UpdateItemProgress(job, mailbox));
        Assert.AreEqual(UpdateExecutionStatus.Cancelled, result.Status);
        Assert.AreEqual(0, exchange.Calls);
        Assert.AreEqual(2, mailbox.Counters.Attempts);
    }

    [TestMethod]
    public async Task MachineElevationIsPublishedOnlyWhenTheExchangeStartsAndANotStartedReplyHasNoHandOff()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = WingetMachineReviewedDispatchTests.Frozen();
        UpdateProgressMailbox mailbox = Prepared(job);
        CountingExchange exchange = new();
        UpdateExecutionResult result = await new ElevatedUpdateRunner(exchange).RunReviewedAsync(job, operation, new(job.Selection, true, true),
            CancellationToken.None, new UpdateItemProgress(job, mailbox));
        Assert.AreEqual(UpdateExecutionStatus.ElevationDenied, result.Status);
        Assert.AreEqual(1, exchange.Calls);
        Assert.AreEqual(3, mailbox.Counters.Attempts, "Denial must never publish request sent or provider started.");
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateProgressPhase.ElevationRequested, frame!.Phase);
    }

    private static UpdateProgressMailbox Prepared(ImmutableUpdateJob job)
    {
        UpdateProgressMailbox mailbox = new(job.Id, job.CorrelationId, job.Selection.Scope);
        mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Queued);
        mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing);
        return mailbox;
    }
    /// <summary>Stage C4: the per-user reviewed operation frozen from <paramref name="job"/>.</summary>
    private static ReviewedUpdateOperation Reviewed(ImmutableUpdateJob job) =>
        WingetUserReviewedRoute.Freeze(job, ReviewedInstallerMode.Selected(InstallerMode.Silent), "S-1-5-21-11-22-33-1001", "inventory:fixture", job.ApprovedAt)
        ?? throw new InvalidOperationException("The fixture job must freeze on the per-user route.");

    private static ImmutableUpdateJob Job(UpdateScope scope) => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(),
        new("winget:contoso.fixture", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Fixture", "1.0", "2.0",
            scope, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture")).Confirm(DateTimeOffset.UtcNow);
    private sealed class FixtureProcess : IWingetProcess
    {
        public int Starts { get; private set; }
        public Task<WingetProcessResult> RunAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token) => throw new AssertFailedException("Inventory process path is forbidden in this fixture.");
        public Task<WingetProcessResult> RunUpgradeAsync(IReadOnlyList<string> args, TimeSpan timeout, UpdateItemProgress? progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.AreEqual("upgrade", args[0]);
            Starts++;
            // Synthetic process observation exercises plumbing, not a real provider capability.
            progress?.ProcessStarted();
            return Task.FromResult(new WingetProcessResult(0, "fixture", string.Empty, false));
        }
    }
    private sealed class CountingExchange : IWingetMachineReviewedExchange
    {
        internal int Calls { get; private set; }

        public Task<ReviewedUpdateDispatchObservation> RunAsync(ReviewedUpdateOperation operation, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.ElevationDenied, "synthetic denial"));
        }
    }
}
