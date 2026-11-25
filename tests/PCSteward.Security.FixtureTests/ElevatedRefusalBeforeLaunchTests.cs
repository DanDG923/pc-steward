using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Four-model review F-03 (FR-UPD-14): a whole-PC WinGet job refused before any helper launch was attempted is recorded as a plain
/// failure, not "Result not verified". Only the exchange's positive proof withdraws the possible start. A launcher that ran, a helper
/// that started, an interrupted exchange or anything else unproven still needs verification, and a declined prompt keeps its own result.
/// Each case runs through the real batch coordinator, executor, runner and exchange.
/// </summary>
[TestClass]
public sealed class ElevatedRefusalBeforeLaunchTests
{
    private const string PossibleStart = "execution-interrupted-after-possible-start";
    private static readonly ReviewedHelperDesktopObservation NoHelper =
        new(Task.CompletedTask, HelperExitObserved: false, TransportSealed: true, TerminationFailed: false, OwnedIoComplete: false, PipeEndOfFile: false);
    private static readonly ReviewedHelperBudgets Fast = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

    [TestMethod]
    public async Task AnUnlistedCapabilityIsAPlainFailureWithNoPossibleStart()
    {
        CountingTransport transport = new(_ => throw new AssertFailedException("nothing reaches the transport"));

        UpdateHistoryEntry entry = await RunAsync(new FixedCapability(HelperProtocolCapabilityStatus.Absent), transport);

        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, entry.Status, entry.Diagnostic);
        Assert.IsFalse(entry.Diagnostic.Contains(PossibleStart, StringComparison.Ordinal), entry.Diagnostic);
        StringAssert.Contains(entry.Diagnostic, "helper-refused-before-update");
        Assert.AreEqual(0, transport.Calls);
    }

    [TestMethod]
    public async Task ATransportRefusalBeforeTheLauncherIsAPlainFailure()
    {
        CountingTransport transport = new(_ => new(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, [], NoHelper, NoHelperLaunchAttempted: true));

        UpdateHistoryEntry entry = await RunAsync(new FixedCapability(), transport);

        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, entry.Status, entry.Diagnostic);
        Assert.IsFalse(entry.Diagnostic.Contains(PossibleStart, StringComparison.Ordinal), entry.Diagnostic);
        StringAssert.Contains(entry.Diagnostic, ReviewedUpdateProtocol2Interpreter.LaunchRefusedDiagnostic);
        Assert.AreEqual(1, transport.Calls);
    }

    [TestMethod]
    [DataRow("launcher-ran-no-process")]
    [DataRow("helper-started-request-not-written")]
    [DataRow("proof-with-a-frame")]
    [DataRow("transport-throws")]
    public async Task AnythingThatMayHaveLaunchedStillNeedsVerification(string scenario)
    {
        CountingTransport transport = new(_ => scenario switch
        {
            // The launcher was invoked: a refusal without the proof (the production transport's no-process path).
            "launcher-ran-no-process" => new(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, [], NoHelper),
            // A helper process existed and exited before any request byte: it was launched, so the possible start stands.
            "helper-started-request-not-written" => new(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, [], Protocol2InterpreterFixture.SettledDesktop),
            // Inconsistent transport input is never read as "nothing launched", even when it claims the proof.
            "proof-with-a-frame" => new(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, [1], NoHelper, NoHelperLaunchAttempted: true),
            _ => throw new IOException("synthetic interrupted exchange"),
        });

        UpdateHistoryEntry entry = await RunAsync(new FixedCapability(), transport);

        Assert.AreEqual(UpdateHistoryStatus.VerificationRequired, entry.Status, $"{scenario}: {entry.Diagnostic}");
        StringAssert.Contains(entry.Diagnostic, PossibleStart, scenario);
    }

    /// <summary>A cancellation the transport saw before invoking the launcher: nothing started, so it is a plain failure, not "may have started".</summary>
    [TestMethod]
    public async Task ACancellationTheTransportRefusedBeforeTheLauncherIsAPlainFailure()
    {
        using CancellationTokenSource cancellation = new();
        CountingTransport transport = new(_ =>
        {
            cancellation.Cancel();
            return new(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, [], NoHelper, NoHelperLaunchAttempted: true);
        });

        UpdateHistoryEntry entry = await RunAsync(new FixedCapability(), transport, cancellation.Token);

        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, entry.Status, entry.Diagnostic);
        Assert.IsFalse(entry.Diagnostic.Contains(PossibleStart, StringComparison.Ordinal), entry.Diagnostic);
    }

    [TestMethod]
    public async Task ADeclinedPromptKeepsItsOwnOutcome()
    {
        CountingTransport transport = new(_ => new(ReviewedHelperTransportOutcome.ElevationDeclined, [], NoHelper));

        UpdateHistoryEntry entry = await RunAsync(new FixedCapability(), transport);

        Assert.AreEqual(UpdateHistoryStatus.ElevationDenied, entry.Status, entry.Diagnostic);
    }

    /// <summary>The production transport, with the production caller-check selection: a refusal before the launcher carries the proof.</summary>
    [TestMethod]
    public async Task TheProductionTransportProvesNoLaunchOnlyBeforeItInvokesTheLauncher()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows-native test (protocol-2 transport).");
        int launches = 0;
        WindowsReviewedHelperTransport beforeLaunch = new(new FixedCapability(), () => null, _ => new(true, string.Empty), () => false,
            (string _, string _, string _, out bool declined) => { launches++; declined = false; return null; }, Fast);

        UpdateHistoryEntry refused = await RunAsync(new FixedCapability(), beforeLaunch);

        Assert.AreEqual(UpdateHistoryStatus.ProductFailed, refused.Status, refused.Diagnostic);
        Assert.IsFalse(refused.Diagnostic.Contains(PossibleStart, StringComparison.Ordinal), refused.Diagnostic);
        Assert.AreEqual(0, launches);
    }

    [TestMethod]
    public void TheMarkIsWithdrawnOnlyWhileNothingFollowedTheElevationRequest()
    {
        ImmutableUpdateJob job = WingetMachineReviewedDispatchTests.Frozen().Job;

        UpdateItemProgress refused = new(job, null);
        refused.ElevationRequested();
        Assert.IsTrue(refused.InvocationPossible);
        refused.NoHelperLaunched();
        Assert.IsFalse(refused.InvocationPossible, "a positive no-launch proof withdraws the elevation mark");

        UpdateItemProgress handed = new(job, null);
        handed.ElevationRequested();
        handed.HandedToHelper();
        handed.NoHelperLaunched();
        Assert.IsTrue(handed.InvocationPossible, "once handed to the helper the possible start stands");

        UpdateItemProgress started = new(job, null);
        started.ProcessStarted();
        started.NoHelperLaunched();
        Assert.IsTrue(started.InvocationPossible, "a started process is never withdrawn");

        UpdateItemProgress untouched = new(job, null);
        untouched.NoHelperLaunched();
        Assert.IsFalse(untouched.InvocationPossible);
        untouched.ElevationRequested();
        Assert.IsTrue(untouched.InvocationPossible, "an earlier withdrawal does not suppress a later request");
    }

    private static async Task<UpdateHistoryEntry> RunAsync(IHelperProtocolCapability capability, IReviewedHelperTransport transport,
        CancellationToken cancellationToken = default)
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = WingetMachineReviewedDispatchTests.Frozen();
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new UnusedUserExecutor()),
            new ElevatedUpdateRunner(new WingetMachineReviewedExchange(capability, transport)));
        SequentialUpdateBatchCoordinator coordinator = new(executor, new UnusedInventory(), new MemoryHistory(), new NotRunning());

        UpdateBatchResult result = await coordinator.RunAsync([new UpdateBatchItem(job, new(job.Selection, true, true), Reviewed: operation)],
            cancellationToken).WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

        Assert.AreEqual(1, result.FinalEntries.Count);
        return result.FinalEntries.Single();
    }

    private sealed class FixedCapability(HelperProtocolCapabilityStatus status = HelperProtocolCapabilityStatus.Supported) : IHelperProtocolCapability
    {
        public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route) => HelperProtocolCapabilityResult.For(status);

        public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
        {
            binding = null;
            return HelperProtocolCapabilityResult.For(status);
        }
    }

    private sealed class CountingTransport(Func<ReviewedUpdateOperation, ReviewedHelperExchange> answer) : IReviewedHelperTransport
    {
        internal int Calls { get; private set; }

        public Task<ReviewedHelperExchange> ExchangeAsync(ReviewedUpdateOperation operation, string nonce, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(answer(operation));
        }
    }

    private sealed class MemoryHistory : IUpdateHistoryStore
    {
        private readonly List<UpdateHistoryEntry> entries = [];

        public Task RecordAsync(UpdateHistoryEntry entry, CancellationToken cancellationToken)
        {
            entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<UpdateHistoryEntry>> GetHistoryForProductAsync(string stableProductKey, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<UpdateHistoryEntry>>(entries.Where(entry => entry.StableProductKey == stableProductKey).ToArray());

        public Task RecoverInterruptedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PruneAsync(UpdateHistoryRetention retention, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class UnusedInventory : IPostUpdateInventory
    {
        public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
            throw new AssertFailedException("nothing ran, so nothing is verified");
    }

    private sealed class NotRunning : IRunningApplicationDetector
    {
        public Task<RunningApplicationDetection> DetectAsync(UpdateJobSelection selection, CancellationToken cancellationToken) =>
            Task.FromResult(RunningApplicationDetection.NotDetected);
    }

    private sealed class UnusedUserExecutor : IWingetReviewedUserUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ReviewedUpdateOperation operation, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
            throw new AssertFailedException("a whole-PC job never runs without elevation");
    }
}
