using System.Diagnostics;
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
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetUpdateExecutor(process)));
        UpdateProviderAttempt result = await executor.ExecuteAsync(job, new(job.Selection, true, true), new UpdateItemProgress(job, mailbox), CancellationToken.None);
        Assert.AreEqual(UpdateProviderAttemptStatus.AwaitingVerification, result.Status);
        Assert.AreEqual(1, process.Starts);
        Assert.AreEqual(3, mailbox.Counters.Attempts);
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateProgressPhase.ProcessStarted, frame!.Phase);
        Assert.IsNull(frame.RecordedStatus);

        ImmutableUpdateJob rejected = Job(UpdateScope.User);
        UpdateProgressMailbox rejectedMailbox = Prepared(rejected);
        UpdateProviderAttempt denial = await executor.ExecuteAsync(rejected,
            new(rejected.Selection, false, true), new UpdateItemProgress(rejected, rejectedMailbox), CancellationToken.None);
        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, denial.Status);
        Assert.AreEqual(1, process.Starts);
        Assert.AreEqual(2, rejectedMailbox.Counters.Attempts);
    }

    [TestMethod]
    public async Task MachineFinalTrustFailureHasNoElevationOrLaunchAndDenialHasNoRequestSentMilestone()
    {
        HelperLaunchPaths paths = new("C:\\Program Files\\PC Steward\\PCSteward.Desktop.exe", "C:\\Program Files\\PC Steward\\PCSteward.ElevatedHelper.exe");
        int validations = 0;
        int launches = 0;
        ImmutableUpdateJob job = Job(UpdateScope.Machine);
        UpdateProgressMailbox mailbox = Prepared(job);
        Process? Launcher(string helperPath, string pipeName, string nonce, out ElevatedHelperResult? failure)
        {
            launches++;
            Assert.AreEqual(2, validations);
            Assert.AreEqual(3, mailbox.Counters.Attempts, "Elevation is observed after final trust and immediately before launch.");
            failure = new(ElevatedHelperOutcome.UserDenied, null, "synthetic denial");
            return null;
        }
        WindowsElevatedHelperTransport rejected = new(() => paths,
            _ => ++validations == 1 ? new(true, string.Empty) : new(false, "synthetic-final-trust-failure"), Launcher);
        ElevatedHelperResult failure = await rejected.ExecuteAsync(job, new UpdateItemProgress(job, mailbox), CancellationToken.None);
        Assert.AreEqual(ElevatedHelperOutcome.Failed, failure.Outcome);
        Assert.AreEqual(0, launches);
        Assert.AreEqual(2, mailbox.Counters.Attempts);

        validations = 0;
        WindowsElevatedHelperTransport denied = new(() => paths, _ => { validations++; return new(true, string.Empty); }, Launcher);
        ElevatedHelperResult denial = await denied.ExecuteAsync(job, new UpdateItemProgress(job, mailbox), CancellationToken.None);
        Assert.AreEqual(ElevatedHelperOutcome.UserDenied, denial.Outcome);
        Assert.AreEqual(1, launches);
        Assert.AreEqual(3, mailbox.Counters.Attempts, "Denial must never publish request sent or provider started.");
        Assert.IsTrue(mailbox.TryTake(out UpdateProgressFrame? frame));
        Assert.AreEqual(UpdateProgressPhase.ElevationRequested, frame!.Phase);
    }

    [TestMethod]
    public async Task PreCancelledMachineRouteDoesNotLaunchOrPublishElevation()
    {
        ImmutableUpdateJob job = Job(UpdateScope.Machine);
        UpdateProgressMailbox mailbox = Prepared(job);
        int launches = 0;
        Process? Launcher(string helperPath, string pipeName, string nonce, out ElevatedHelperResult? failure)
        {
            launches++;
            failure = null;
            return null;
        }
        WindowsElevatedHelperTransport transport = new(
            () => new("C:\\Program Files\\PC Steward\\PCSteward.Desktop.exe", "C:\\Program Files\\PC Steward\\PCSteward.ElevatedHelper.exe"),
            _ => new(true, string.Empty), Launcher);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ElevatedHelperResult result = await transport.ExecuteAsync(job, new UpdateItemProgress(job, mailbox), cancellation.Token);
        Assert.AreEqual(ElevatedHelperOutcome.Cancelled, result.Outcome);
        Assert.AreEqual(0, launches);
        Assert.AreEqual(2, mailbox.Counters.Attempts);
    }

    private static UpdateProgressMailbox Prepared(ImmutableUpdateJob job)
    {
        UpdateProgressMailbox mailbox = new(job.Id, job.CorrelationId, job.Selection.Scope);
        mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Queued);
        mailbox.Offer(job.Id, job.CorrelationId, UpdateProgressPhase.Preparing);
        return mailbox;
    }
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
}
