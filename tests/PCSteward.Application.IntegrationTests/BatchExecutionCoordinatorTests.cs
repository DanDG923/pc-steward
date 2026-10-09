using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>Task 49: FR-BAT-04–08, FR-INS-05/07, AC-V3-04/06/07 — batch coordination, cancel, verification and running apps.</summary>
[TestClass]
public sealed class BatchExecutionCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] UnelevatedOrder = ["winget:user", "pcs:app"];
    private static readonly string[] CancelledItems = ["winget:b", "winget:m"];

    [TestMethod]
    public async Task UnelevatedItemsRunFirstThenOneElevatedSessionAndEveryItemIsVerified()
    {
        Fixture fixture = new();
        ApprovedOperationBatch batch = Batch(Upgrade("winget:machine", "Vendor.Machine", UpdateScope.Machine), Upgrade("winget:user", "Vendor.User", UpdateScope.User), Install("pcs:app", "Vendor.App"));
        fixture.Elevated.Respond = (b, _) => [.. b.ElevatedItems.Select(job => Reported(job, UpdateProviderAttemptStatus.AwaitingVerification))];

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);

        Assert.IsTrue(result.Started);
        CollectionAssert.AreEqual(UnelevatedOrder, fixture.Unelevated.Jobs.Select(job => job.Selection.StableProductKey).ToArray());
        Assert.AreEqual(1, fixture.Elevated.Calls, "One helper session, one UAC prompt (AC-V3-04).");
        Assert.HasCount(3, result.Summary.Succeeded);
        Assert.IsTrue(fixture.History.Entries.Any(entry => entry.StableProductKey == "winget:machine" && entry.Status == UpdateHistoryStatus.Running), "Recorded before UAC for crash recovery.");
        Assert.HasCount(3, fixture.Verifier.Jobs);
        UpdateHistoryEntry install = result.Summary.Succeeded.Single(entry => entry.StableProductKey == "pcs:app");
        Assert.AreEqual(UpdateHistoryEntry.NotInstalledVersion, install.InstalledVersion);
    }

    [TestMethod]
    public async Task ProviderExitIsNeverSuccessWithoutVerification()
    {
        Fixture fixture = new();
        fixture.Verifier.Result = new(false, false);
        ApprovedOperationBatch batch = Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User), Upgrade("winget:b", "Vendor.B", UpdateScope.User));
        int calls = 0;
        fixture.Unelevated.Outcome = _ => ++calls == 1
            ? new(UpdateProviderAttemptStatus.AwaitingVerification, true, "restart", 3010)
            : new(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok", 0);

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);

        Assert.AreEqual(UpdateHistoryStatus.RestartRequired, result.Summary.RestartRequired.Single().Status);
        Assert.AreEqual(3010, result.Summary.RestartRequired.Single().ExitCode);
        Assert.AreEqual(UpdateHistoryStatus.Inconclusive, result.Summary.NotVerified.Single().Status);
        Assert.HasCount(0, result.Summary.Succeeded);
        Assert.AreEqual("winget:b", result.Summary.RetryCandidates.Single(), "Restart required is not a retry candidate.");
    }

    [TestMethod]
    public async Task CancelRemainingStopsBeforeTheNextItemAndNeverStartsTheHelper()
    {
        Fixture fixture = new();
        using CancellationTokenSource cancelRemaining = new();
        fixture.Unelevated.Outcome = _ =>
        {
            cancelRemaining.Cancel();
            return new(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok", 0);
        };
        ApprovedOperationBatch batch = Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User), Upgrade("winget:b", "Vendor.B", UpdateScope.User), Upgrade("winget:m", "Vendor.M", UpdateScope.Machine));

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, cancelRemaining.Token, CancellationToken.None);

        Assert.HasCount(1, fixture.Unelevated.Jobs);
        Assert.AreEqual("winget:a", result.Summary.Succeeded.Single().StableProductKey, "The running item finishes and is verified.");
        CollectionAssert.AreEquivalent(CancelledItems, result.Summary.Skipped.Select(entry => entry.StableProductKey).ToArray());
        Assert.IsTrue(result.Summary.Skipped.All(entry => entry.Status == UpdateHistoryStatus.Cancelled));
        Assert.AreEqual(0, fixture.Elevated.Calls);
    }

    [TestMethod]
    public async Task CancelRemainingDuringTheElevatedPreCheckRaisesNoPrompt()
    {
        Fixture fixture = new();
        using CancellationTokenSource cancelRemaining = new();
        fixture.Revalidator.Changed = _ =>
        {
            cancelRemaining.Cancel();
            return false;
        };
        ApprovedOperationBatch batch = Batch(Upgrade("winget:m1", "Vendor.M1", UpdateScope.Machine), Upgrade("winget:m2", "Vendor.M2", UpdateScope.Machine));

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, cancelRemaining.Token, CancellationToken.None);

        Assert.AreEqual(0, fixture.Elevated.Calls);
        Assert.HasCount(2, result.Summary.Skipped);
        Assert.IsTrue(result.Summary.Skipped.All(entry => entry.Status == UpdateHistoryStatus.Cancelled));
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task CancelRemainingDuringLastElevatedPreCheckNeverStartsHelper(int itemCount)
    {
        Fixture fixture = new();
        using CancellationTokenSource cancelRemaining = new();
        int checks = 0;
        fixture.Revalidator.Changed = _ =>
        {
            if (++checks == itemCount) cancelRemaining.Cancel();
            return false;
        };
        ApprovedOperationBatch batch = Batch(Enumerable.Range(1, itemCount)
            .Select(index => Upgrade($"winget:m{index}", $"Vendor.M{index}", UpdateScope.Machine)).ToArray());

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, cancelRemaining.Token, CancellationToken.None);

        Assert.AreEqual(0, fixture.Elevated.Calls, "FR-BAT-06: cancellation during the last precheck must prevent UAC.");
        Assert.HasCount(0, fixture.Unelevated.Jobs);
        Assert.HasCount(itemCount, result.Summary.Skipped);
        Assert.IsTrue(result.Summary.Skipped.All(entry => entry.Status == UpdateHistoryStatus.Cancelled));
    }

    [TestMethod]
    public async Task CancelRemainingDuringUnelevatedRevalidationNeverStartsProvider()
    {
        Fixture fixture = new();
        using CancellationTokenSource cancelRemaining = new();
        fixture.Revalidator.Changed = _ =>
        {
            cancelRemaining.Cancel();
            return false;
        };

        BatchRunResult result = await fixture.Coordinator.RunAsync(Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User)), BatchRunOptions.Default, null, cancelRemaining.Token, CancellationToken.None);

        Assert.HasCount(0, fixture.Unelevated.Jobs);
        Assert.AreEqual(0, fixture.Elevated.Calls);
        Assert.AreEqual(UpdateHistoryStatus.Cancelled, result.Summary.Skipped.Single().Status);
    }

    [TestMethod]
    public async Task AbortDuringAProviderRecoversToVerificationRequired()
    {
        Fixture fixture = new();
        using CancellationTokenSource abort = new();
        fixture.Unelevated.Run = async (_, token) =>
        {
            await abort.CancelAsync();
            token.ThrowIfCancellationRequested();
            return new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "never");
        };
        ApprovedOperationBatch batch = Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User), Upgrade("winget:b", "Vendor.B", UpdateScope.User));

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, abort.Token);

        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, result.Summary.NotVerified.Single().Status);
        Assert.AreEqual(UpdateHistoryStatus.Cancelled, result.Summary.Skipped.Single().Status);
    }

    [TestMethod]
    public async Task ExpiredBatchOrConcurrentRunStartsNothing()
    {
        Fixture fixture = new(Now.AddMinutes(31));
        BatchRunResult expired = await fixture.Coordinator.RunAsync(Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User)), BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);
        Assert.IsFalse(expired.Started);
        StringAssert.Contains(expired.Message, "expired");
        Assert.HasCount(0, fixture.History.Entries);

        Fixture busy = new();
        TaskCompletionSource release = new();
        busy.Unelevated.Run = async (_, _) =>
        {
            await release.Task;
            return new ProviderExitAssessment(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok");
        };
        Task<BatchRunResult> first = busy.Coordinator.RunAsync(Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User)), BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);
        BatchRunResult second = await busy.Coordinator.RunAsync(Batch(Upgrade("winget:b", "Vendor.B", UpdateScope.User)), BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);
        release.SetResult();
        Assert.IsTrue((await first).Started);
        Assert.IsFalse(second.Started, "FR-INS-07: one active operation run.");
    }

    [TestMethod]
    public async Task ChangedEvidenceIsSkippedAndAllSkippedElevatedItemsRaiseNoPrompt()
    {
        Fixture fixture = new();
        fixture.Revalidator.Changed = _ => true;
        ApprovedOperationBatch batch = Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User), Upgrade("winget:m", "Vendor.M", UpdateScope.Machine));

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);

        Assert.HasCount(0, fixture.Unelevated.Jobs);
        Assert.AreEqual(0, fixture.Elevated.Calls, "Nothing left to run: no UAC prompt.");
        Assert.HasCount(2, result.Summary.Skipped);
        Assert.IsTrue(result.Summary.Skipped.All(entry => entry.Status == UpdateHistoryStatus.Skipped && entry.Diagnostic.Contains("revalidation-failed", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task ElevatedOutcomesMapToHistoryWithoutGuessing()
    {
        Fixture fixture = new();
        ApprovedOperationBatch batch = Batch(
            Upgrade("winget:1", "Vendor.One", UpdateScope.Machine), Upgrade("winget:2", "Vendor.Two", UpdateScope.Machine),
            Upgrade("winget:3", "Vendor.Three", UpdateScope.Machine), Upgrade("winget:4", "Vendor.Four", UpdateScope.Machine));
        fixture.Verifier.Result = new(false, false);
        fixture.Elevated.Respond = (b, _) =>
        [
            new(b.ElevatedItems[0].Id, ElevatedItemDisposition.Reported, UpdateProviderAttemptStatus.AwaitingVerification, true, 3010, "restart"),
            new(b.ElevatedItems[1].Id, ElevatedItemDisposition.Reported, UpdateProviderAttemptStatus.InstallerFailed, false, 1603, "failed"),
            new(b.ElevatedItems[2].Id, ElevatedItemDisposition.Interrupted, UpdateProviderAttemptStatus.ProductFailed, false, null, "crash")
            // Item 4 has no outcome at all.
        ];

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);

        Assert.AreEqual(UpdateHistoryStatus.RestartRequired, Final(result, "winget:1").Status);
        Assert.AreEqual(UpdateHistoryStatus.InstallerFailed, Final(result, "winget:2").Status);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, Final(result, "winget:3").Status);
        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, Final(result, "winget:4").Status, "Missing evidence is a possible start.");
        Assert.HasCount(1, fixture.Verifier.Jobs, "Only the provider-completed item is verified.");
    }

    [TestMethod]
    public async Task DeclinedUacIsElevationDeniedForEveryElevatedItem()
    {
        Fixture fixture = new();
        ApprovedOperationBatch batch = Batch(Upgrade("winget:1", "Vendor.One", UpdateScope.Machine), Upgrade("winget:2", "Vendor.Two", UpdateScope.Machine));
        fixture.Elevated.Respond = (b, _) => [.. b.ElevatedItems.Select(job => new ElevatedItemOutcome(job.Id, ElevatedItemDisposition.NotStarted, UpdateProviderAttemptStatus.ElevationDenied, false, null, "user-declined-elevation"))];

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);

        Assert.HasCount(2, result.Summary.Failed);
        Assert.IsTrue(result.Summary.Failed.All(entry => entry.Status == UpdateHistoryStatus.ElevationDenied));
        Assert.HasCount(0, fixture.Verifier.Jobs);
    }

    [TestMethod]
    public async Task StandaloneDeploymentWithoutElevationRunsOnlyUnelevatedItems()
    {
        Fixture fixture = new(elevated: false);
        BatchRunResult result = await fixture.Coordinator.RunAsync(Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User), Upgrade("winget:m", "Vendor.M", UpdateScope.Machine)), BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.Success, Final(result, "winget:a").Status);
        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, Final(result, "winget:m").Status);
    }

    [TestMethod]
    public async Task RunningAppIsSkippedByDefaultAndClosedOnlyWithConsent()
    {
        ApprovedOperationBatch Single() => Batch(Upgrade("winget:a", "Vendor.A", UpdateScope.User));

        Fixture byDefault = new();
        byDefault.Detector.Running = true;
        BatchRunResult skipped = await byDefault.Coordinator.RunAsync(Single(), BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.Skipped, Final(skipped, "winget:a").Status);
        Assert.AreEqual("running-application-skipped", Final(skipped, "winget:a").Diagnostic);
        Assert.HasCount(0, byDefault.Closer.Calls);
        Assert.HasCount(0, byDefault.Unelevated.Jobs);

        Fixture perRun = new();
        perRun.Detector.Running = true;
        await perRun.Coordinator.RunAsync(Single(), new(new(CloseForThisRun: true), new Dictionary<string, ProductUpdatePolicy>()), null, CancellationToken.None, CancellationToken.None);
        Assert.IsFalse(perRun.Closer.Calls.Single().AllowForce, "Graceful only without the second consent.");
        Assert.AreEqual(RunningApplicationPolicy.GracefulWait, perRun.Closer.Calls.Single().Wait);
        Assert.HasCount(1, perRun.Unelevated.Jobs);

        Fixture perApp = new();
        perApp.Detector.Running = true;
        await perApp.Coordinator.RunAsync(Single(), new(new(ForceCloseForThisRun: true), new Dictionary<string, ProductUpdatePolicy> { ["winget:a"] = new(CloseAppConsent: true) }), null, CancellationToken.None, CancellationToken.None);
        Assert.IsTrue(perApp.Closer.Calls.Single().AllowForce);

        Fixture refused = new();
        refused.Detector.Running = true;
        refused.Closer.Closes = false;
        BatchRunResult stillRunning = await refused.Coordinator.RunAsync(Single(), new(new(CloseForThisRun: true), new Dictionary<string, ProductUpdatePolicy>()), null, CancellationToken.None, CancellationToken.None);
        Assert.AreEqual("running-application-did-not-close", Final(stillRunning, "winget:a").Diagnostic);
        Assert.HasCount(0, refused.Unelevated.Jobs);

        Fixture automation = new();
        automation.Detector.Running = true;
        BatchRunResult automated = await automation.Coordinator.RunAsync(Single(), new(new(true, true), new Dictionary<string, ProductUpdatePolicy> { ["winget:a"] = new(CloseAppConsent: true) }, Automation: true), null, CancellationToken.None, CancellationToken.None);
        Assert.AreEqual(UpdateHistoryStatus.Skipped, Final(automated, "winget:a").Status);
        Assert.HasCount(0, automation.Closer.Calls, "AC-V3-07: automation never closes or force-terminates an app.");
    }

    [TestMethod]
    public async Task RunningElevatedAppIsWithheldFromTheHelperAsASkip()
    {
        Fixture fixture = new();
        fixture.Detector.RunningKeys.Add("winget:m1");
        ApprovedOperationBatch batch = Batch(Upgrade("winget:m1", "Vendor.M1", UpdateScope.Machine), Upgrade("winget:m2", "Vendor.M2", UpdateScope.Machine));
        fixture.Elevated.Respond = (b, skips) => [.. b.ElevatedItems.Select(job => skips.ContainsKey(job.Id)
            ? new ElevatedItemOutcome(job.Id, ElevatedItemDisposition.Reported, UpdateProviderAttemptStatus.Skipped, false, null, "skipped-before-elevation: " + skips[job.Id])
            : Reported(job, UpdateProviderAttemptStatus.AwaitingVerification))];

        BatchRunResult result = await fixture.Coordinator.RunAsync(batch, BatchRunOptions.Default, null, CancellationToken.None, CancellationToken.None);

        Assert.AreEqual(BatchSkipReasons.RunningApplication, fixture.Elevated.LastSkips.Single().Value);
        Assert.AreEqual(UpdateHistoryStatus.Skipped, Final(result, "winget:m1").Status);
        Assert.AreEqual(UpdateHistoryStatus.Success, Final(result, "winget:m2").Status);
    }

    [TestMethod]
    public void PolicyMatrixIsDeterministic()
    {
        RunningApplicationDetection running = new(true, ["vendor"]);
        Assert.AreEqual(RunningApplicationAction.Proceed, RunningApplicationPolicy.Decide(RunningApplicationDetection.NotDetected, false, new(), false));
        Assert.AreEqual(RunningApplicationAction.Skip, RunningApplicationPolicy.Decide(running, false, new(), false));
        Assert.AreEqual(RunningApplicationAction.Close, RunningApplicationPolicy.Decide(running, true, new(), false));
        Assert.AreEqual(RunningApplicationAction.Close, RunningApplicationPolicy.Decide(running, false, new(CloseForThisRun: true), false));
        Assert.AreEqual(RunningApplicationAction.Skip, RunningApplicationPolicy.Decide(running, true, new(true, true), true));
        Assert.IsFalse(RunningApplicationPolicy.AllowsForce(new(true, true), automation: true));
        Assert.IsFalse(RunningApplicationPolicy.AllowsForce(new(CloseForThisRun: true), automation: false));
        Assert.IsTrue(RunningApplicationPolicy.AllowsForce(new(ForceCloseForThisRun: true), automation: false));
    }

    [TestMethod]
    public void SummarySeparatesTheFiveGroups()
    {
        UpdateHistoryEntry Entry(UpdateHistoryStatus status) => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "k:" + status, UpdateProvider.WinGet, "Vendor.App", "1", "2", Now, status, null, string.Empty);
        BatchCompletionSummary summary = BatchCompletionSummary.From(Enum.GetValues<UpdateHistoryStatus>()
            .Where(status => status is not (UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running or UpdateHistoryStatus.RunningApplicationWarning))
            .Select(Entry));
        Assert.HasCount(1, summary.Succeeded);
        Assert.HasCount(1, summary.RestartRequired);
        Assert.HasCount(7, summary.Failed);
        Assert.HasCount(2, summary.NotVerified);
        Assert.HasCount(2, summary.Skipped);
    }

    private static UpdateHistoryEntry Final(BatchRunResult result, string key) =>
        result.Entries.Last(entry => entry.StableProductKey == key);

    private static ElevatedItemOutcome Reported(ImmutableUpdateJob job, UpdateProviderAttemptStatus status) =>
        new(job.Id, ElevatedItemDisposition.Reported, status, false, 0, "reported");

    private static ApprovedOperationBatch Batch(params UpdateJobSelection[] selections) =>
        new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), selections).Confirm(Now);

    private static UpdateJobSelection Upgrade(string key, string packageId, UpdateScope scope) =>
        new(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "1.0.0", "1.1.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1");

    private static UpdateJobSelection Install(string key, string packageId) =>
        UpdateJobSelection.ForInstall(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "2.0.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false);

    private sealed class Fixture
    {
        public Fixture(DateTimeOffset? now = null, bool elevated = true)
        {
            Coordinator = new(Revalidator, Unelevated, elevated ? Elevated : null, Verifier, History, Detector, Closer, () => now ?? Now.AddMinutes(1));
        }

        public FakeRevalidator Revalidator { get; } = new();
        public FakeExecutor Unelevated { get; } = new();
        public FakeElevated Elevated { get; } = new();
        public FakeVerifier Verifier { get; } = new();
        public MemoryHistory History { get; } = new();
        public FakeDetector Detector { get; } = new();
        public FakeCloser Closer { get; } = new();
        public BatchExecutionCoordinator Coordinator { get; }
    }

    private sealed class FakeRevalidator : IOperationRevalidator
    {
        public Func<ImmutableUpdateJob, bool> Changed { get; set; } = _ => false;

        public Task<UpdateJobExecutionEvidence?> RevalidateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            UpdateJobSelection s = job.Selection;
            UpdateJobSelection selection = Changed(job)
                ? UpdateJobSelection.Restore(s.Operation, s.StableProductKey, s.Provider, s.Source, s.PackageId, s.InstalledVersion, "9.9.9", s.Scope, s.Architecture, s.Channel, s.MappingEvidenceVersion, s.AgreementHash, s.RunsElevated)
                : s;
            return Task.FromResult<UpdateJobExecutionEvidence?>(new(selection, true, true));
        }
    }

    private sealed class FakeExecutor : IOperationExecutor
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public Func<ImmutableUpdateJob, ProviderExitAssessment> Outcome { get; set; } = _ => new(UpdateProviderAttemptStatus.AwaitingVerification, false, "ok", 0);
        public Func<ImmutableUpdateJob, CancellationToken, Task<ProviderExitAssessment>>? Run { get; set; }

        public Task<ProviderExitAssessment> ExecuteAsync(ImmutableUpdateJob job, UpdateJobExecutionEvidence freshEvidence, CancellationToken cancellationToken)
        {
            Jobs.Add(job);
            Assert.IsTrue(job.Matches(freshEvidence));
            Assert.IsFalse(job.Selection.RunsElevated, "Elevated items never reach the desktop executor.");
            return Run?.Invoke(job, cancellationToken) ?? Task.FromResult(Outcome(job));
        }
    }

    private sealed class FakeElevated : IElevatedBatchExecutor
    {
        public int Calls { get; private set; }
        public IReadOnlyDictionary<Guid, string> LastSkips { get; private set; } = new Dictionary<Guid, string>();
        public Func<ApprovedOperationBatch, IReadOnlyDictionary<Guid, string>, IReadOnlyList<ElevatedItemOutcome>> Respond { get; set; } =
            (batch, _) => [.. batch.ElevatedItems.Select(job => Reported(job, UpdateProviderAttemptStatus.AwaitingVerification))];

        public Task<ElevatedBatchOutcome> ExecuteAsync(ApprovedOperationBatch batch, IReadOnlyDictionary<Guid, string> skipReasons, Action<Guid>? itemStarted, CancellationToken cancelRemaining, CancellationToken abort)
        {
            Calls++;
            LastSkips = new Dictionary<Guid, string>(skipReasons);
            foreach (ImmutableUpdateJob job in batch.ElevatedItems.Where(job => !skipReasons.ContainsKey(job.Id))) itemStarted?.Invoke(job.Id);
            return Task.FromResult(new ElevatedBatchOutcome(Respond(batch, skipReasons)));
        }
    }

    private sealed class FakeVerifier : IPostUpdateInventory
    {
        public List<ImmutableUpdateJob> Jobs { get; } = [];
        public PostUpdateInventoryResult Result { get; set; } = new(true, false);

        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            Jobs.Add(job);
            return Task.FromResult(Result);
        }
    }

    private sealed class MemoryHistory : IUpdateHistoryStore
    {
        public List<UpdateHistoryEntry> Entries { get; } = [];
        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>([.. Entries.Where(entry => entry.StableProductKey == stableProductKey)]);
        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeDetector : IRunningApplicationDetector
    {
        public bool Running { get; set; }
        public HashSet<string> RunningKeys { get; } = new(StringComparer.Ordinal);

        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(Running || RunningKeys.Contains(selection.StableProductKey) ? new RunningApplicationDetection(true, ["vendorapp"]) : RunningApplicationDetection.NotDetected);
    }

    private sealed class FakeCloser : IRunningApplicationCloser
    {
        public List<(IReadOnlyCollection<string> Names, bool AllowForce, TimeSpan Wait)> Calls { get; } = [];
        public bool Closes { get; set; } = true;

        public Task<bool> CloseAsync(IReadOnlyCollection<string> processNames, bool allowForce, TimeSpan gracefulWait, CancellationToken cancellationToken)
        {
            Calls.Add((processNames, allowForce, gracefulWait));
            return Task.FromResult(Closes);
        }
    }
}

