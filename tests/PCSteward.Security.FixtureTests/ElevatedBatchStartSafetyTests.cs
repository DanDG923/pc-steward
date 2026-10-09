using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>FR-BAT-08 / AC-V3-07: fresh helper checks cannot inherit a stale desktop all-clear.</summary>
[TestClass]
public sealed class ElevatedBatchStartSafetyTests
{
    [TestMethod]
    public async Task AppOpenedWhileFirstProviderRunsIsSkippedBeforeItsOwnLaunch()
    {
        Fixture fixture = new();
        ApprovedOperationBatch batch = Batch("Vendor.First", "Vendor.Second");
        fixture.Provider.OnExecute = job =>
        {
            if (job.Id == batch.ElevatedItems[0].Id) fixture.Detector.Running.Add("Vendor.Second");
        };

        BatchRunResult result = await fixture.RunAsync(batch);

        Assert.HasCount(1, fixture.Provider.Launched);
        Assert.AreEqual("Vendor.First", fixture.Provider.Launched.Single());
        Assert.AreEqual(UpdateHistoryStatus.Success, result.Summary.Succeeded.Single().Status);
        Assert.AreEqual("Vendor.Second", result.Summary.Skipped.Single().PackageId);
        Assert.AreEqual(1, fixture.Helper.Started.Count);
        Assert.HasCount(0, fixture.Closer.Closed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AppReopenedDuringUacIsSkippedEvenAfterConsentedDesktopClose(bool consentedClose)
    {
        Fixture fixture = new();
        if (consentedClose) fixture.Detector.Running.Add("Vendor.Editor");
        fixture.Helper.BeforeSession = () => fixture.Detector.Running.Add("Vendor.Editor");
        BatchRunOptions options = new(new(CloseForThisRun: consentedClose), new Dictionary<string, ProductUpdatePolicy>());

        BatchRunResult result = await fixture.RunAsync(Batch("Vendor.Editor"), options);

        Assert.HasCount(0, fixture.Provider.Launched);
        Assert.HasCount(0, fixture.Helper.Started);
        Assert.AreEqual(UpdateHistoryStatus.Skipped, result.Summary.Skipped.Single().Status);
        Assert.AreEqual(consentedClose ? 1 : 0, fixture.Closer.Closed.Count, "Only the desktop consent path can close an app.");
        Assert.IsTrue(fixture.Detector.Running.Contains("Vendor.Editor"), "The elevated helper must never close it again.");
    }

    [TestMethod]
    public async Task FailedFreshHelperProcessCheckSkipsWithoutStartingProvider()
    {
        Fixture fixture = new();
        fixture.Helper.BeforeSession = () => fixture.Detector.Fail = true;

        BatchRunResult result = await fixture.RunAsync(Batch("Vendor.Editor"));

        Assert.HasCount(0, fixture.Provider.Launched);
        Assert.HasCount(0, fixture.Helper.Started);
        Assert.AreEqual(UpdateHistoryStatus.Skipped, result.Summary.Skipped.Single().Status);
        Assert.HasCount(0, fixture.Closer.Closed);
    }

    private static ApprovedOperationBatch Batch(params string[] packages) =>
        new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), packages.Select(package =>
            new UpdateJobSelection("winget:" + package, UpdateProvider.WinGet, UpdateSource.WinGet, package,
                "1.0.0", "1.1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1")).ToArray())
        .Confirm(DateTimeOffset.UtcNow);

    private sealed class Fixture
    {
        public MutableDetector Detector { get; } = new();
        public RecordingProvider Provider { get; } = new();
        public DesktopCloser Closer { get; }
        public InProcessHelper Helper { get; }
        private readonly BatchExecutionCoordinator coordinator;

        public Fixture()
        {
            Closer = new(Detector);
            Helper = new(Detector, Provider);
            coordinator = new(new MatchingRevalidator(), Provider, Helper, new VerifiedInventory(), new MemoryHistory(), Detector, Closer);
        }

        public Task<BatchRunResult> RunAsync(ApprovedOperationBatch batch, BatchRunOptions? options = null) =>
            coordinator.RunAsync(batch, options ?? BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);
    }

    private sealed class MutableDetector : IRunningApplicationDetector
    {
        public HashSet<string> Running { get; } = new(StringComparer.Ordinal);
        public bool Fail { get; set; }
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("process snapshot unavailable");
            return Task.FromResult(Running.Contains(selection.PackageId)
                ? new RunningApplicationDetection(true, [selection.PackageId]) : RunningApplicationDetection.NotDetected);
        }
    }

    private sealed class DesktopCloser(MutableDetector detector) : IRunningApplicationCloser
    {
        public List<string> Closed { get; } = [];
        public Task<bool> CloseAsync(IReadOnlyCollection<string> processNames, bool allowForce, TimeSpan gracefulWait, CancellationToken cancellationToken)
        {
            foreach (string name in processNames)
            {
                Closed.Add(name);
                detector.Running.Remove(name);
            }
            return Task.FromResult(true);
        }
    }

    private sealed class InProcessHelper(MutableDetector detector, RecordingProvider provider) : IElevatedBatchExecutor
    {
        public Action? BeforeSession { get; set; }
        public List<Guid> Started { get; } = [];
        public async Task<ElevatedBatchOutcome> ExecuteAsync(ApprovedOperationBatch batch, IReadOnlyDictionary<Guid, string> skipReasons, Action<Guid>? itemStarted, CancellationToken cancelRemaining, CancellationToken abort)
        {
            BeforeSession?.Invoke(); // Deterministic stand-in for time spent in the UAC prompt.
            string nonce = ElevatedHelperProtocol.CreateNonce();
            HelperBatchRequestReceiver receiver = new(nonce);
            foreach (byte[] frame in ElevatedHelperProtocolV2.CreateBatchRequest(batch, skipReasons, nonce, DateTimeOffset.UtcNow))
                Assert.IsTrue(receiver.Accept(frame), receiver.Rejection);
            HelperBatchRequest request = receiver.Request!;
            HelperBatchResponseReader reader = new(request.BatchId, nonce, [.. request.Items.Select(item => (item.Sequence, item.Job.Id))]);
            ElevatedHelperBatchSession session = new(request, nonce, new MatchingRevalidator(), provider, runningApplications: detector);
            using CancellationTokenRegistration registration = cancelRemaining.Register(session.RequestCancelRemaining);
            List<ElevatedItemOutcome> results = [];
            await session.RunAsync(frame =>
            {
                Assert.IsTrue(reader.TryAccept(frame, out HelperBatchEvent? message, out string rejection), rejection);
                if (message is HelperItemStartedEvent started)
                {
                    Started.Add(started.JobId);
                    itemStarted?.Invoke(started.JobId);
                }
                if (message is HelperItemResultEvent result)
                    results.Add(new(result.JobId, ElevatedItemDisposition.Reported, result.Status, result.RestartPending, result.ExitCode, result.Diagnostic));
                return Task.CompletedTask;
            }, abort);
            Assert.IsTrue(reader.IsComplete);
            Assert.HasCount(request.Items.Count, results);
            return new(results);
        }
    }

    private sealed class MatchingRevalidator : IOperationRevalidator
    {
        public Task<UpdateJobExecutionEvidence?> RevalidateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            Task.FromResult<UpdateJobExecutionEvidence?>(new(job.Selection, true, true));
    }

    private sealed class RecordingProvider : IOperationExecutor
    {
        public List<string> Launched { get; } = [];
        public Action<ImmutableUpdateJob>? OnExecute { get; set; }
        public Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
        {
            Launched.Add(job.Selection.PackageId);
            OnExecute?.Invoke(job);
            return Task.FromResult(new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok"));
        }
    }

    private sealed class VerifiedInventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult(new PostUpdateInventoryResult(true, false));
    }

    private sealed class MemoryHistory : IUpdateHistoryStore
    {
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>([]);
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
