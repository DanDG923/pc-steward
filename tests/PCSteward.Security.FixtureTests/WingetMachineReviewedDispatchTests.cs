using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Phase D stage D4b, stages 2c and 3 (design §5.4, §8; FR-UPD-15, FR-UPD-01/03, TM-03/04/07/13): an approved whole-PC WinGet job
/// runs only through its frozen <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation on the protocol-2 helper. Protocol 1
/// is retired, so a job without that operation has no route. The observation mapping is pinned here.
/// </summary>
[TestClass]
public sealed class WingetMachineReviewedDispatchTests
{
    // ---- The observation as a runner status ----

    [TestMethod]
    public void AnObservationThatMayHaveStartedButDidNotSettleIsAHelperFailure()
    {
        Assert.AreEqual(UpdateExecutionStatus.HelperFailed, Map(Observation(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Completed, 0)));
        Assert.AreEqual(UpdateExecutionStatus.HelperFailed,
            Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0, uncertain: true)), "never awaits verification");
        Assert.AreEqual(UpdateExecutionStatus.HelperFailed, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Failed, null)));
    }

    [TestMethod]
    public void NothingStartedMapsToItsReason()
    {
        (UpdateExecutionStatus status, UpdateExecutionDenial denial, _) =
            ElevatedUpdateRunner.MapReviewed(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Refused, "x"));
        Assert.AreEqual(UpdateExecutionStatus.Denied, status);
        Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, denial);
        Assert.AreEqual(UpdateExecutionStatus.ElevationDenied, Map(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.ElevationDenied, "x")));
        Assert.AreEqual(UpdateExecutionStatus.Cancelled, Map(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Cancelled, "x")));
        Assert.AreEqual(UpdateExecutionStatus.TimedOut, Map(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.TimedOut, "x")));
        Assert.AreEqual(UpdateExecutionStatus.HelperFailed, Map(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Failed, "x")));
        Assert.AreEqual(UpdateExecutionStatus.HelperFailed, Map(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Completed, "x")),
            "a completion that never started is not believed");
    }

    [TestMethod]
    public void AStartedUpdateAwaitsInventoryOrReportsWhatWinGetSaid()
    {
        Assert.AreEqual(UpdateExecutionStatus.AwaitingVerification, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 0)));
        Assert.AreEqual(UpdateExecutionStatus.RestartReported,
            Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.ProviderFailed, WingetRestartExitCodes.RebootRequiredToFinish)));
        Assert.AreEqual(UpdateExecutionStatus.RestartReported,
            Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, WingetRestartExitCodes.RebootInitiated)));
        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed,
            Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.ProviderFailed, WingetRestartExitCodes.RebootRequiredForInstall)),
            "a restart needed before installing means nothing was updated");
        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.ProviderFailed, 1603)));
        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, 1)));
        Assert.AreEqual(UpdateExecutionStatus.ProviderFailed, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Completed, null)),
            "a completion without an exit code is never a success");
        Assert.AreEqual(UpdateExecutionStatus.Cancelled, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Cancelled, null)));
        Assert.AreEqual(UpdateExecutionStatus.TimedOut, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.TimedOut, null)));
        Assert.AreEqual(UpdateExecutionStatus.HelperFailed, Map(Observation(ReviewedUpdateInvocation.Started, ReviewedUpdateProviderStatus.Refused, null)));
    }

    // ---- Dispatch on protocol 2 (admitted from stage 3) ----

    [TestMethod]
    public async Task TheExchangeRefusesAnotherRouteBeforeAnyCapabilityReadOrLaunch()
    {
        CountingCapability capability = new();
        ScriptedTransport transport = new(Protocol2InterpreterFixture.Started());
        int nonces = 0;
        WingetMachineReviewedExchange exchange = new(capability, transport, () => { nonces++; return new string('C', 64); });

        ReviewedUpdateDispatchObservation observation = await exchange.RunAsync(ReviewedHelperEndpointClientRunFaultTests.ClientOperation(), CancellationToken.None);

        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, observation.Status);
        Assert.AreEqual(WingetMachineReviewedExchange.RouteUnavailableDiagnostic, observation.Diagnostic);
        Assert.AreEqual(0, capability.Calls, "the gate refuses first, before the deployment capability is read");
        Assert.AreEqual(0, transport.Calls);
        Assert.AreEqual(0, nonces);
    }

    [TestMethod]
    public async Task AnUnlistedCapabilityRefusesBeforeTheHelperIsLaunched()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        CountingCapability capability = new(HelperProtocolCapabilityStatus.Absent);
        ScriptedTransport transport = new(Protocol2InterpreterFixture.Started());

        UpdateExecutionResult result = await Runner(capability, transport).RunReviewedAsync(job, operation, new(job.Selection, true, true), CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.Denied, result.Status);
        Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, result.Denial);
        Assert.AreEqual(1, capability.Calls);
        Assert.AreEqual(0, transport.Calls, "nothing is launched when the protected capability does not list the route");
    }

    [TestMethod]
    public async Task AnApprovedWholePcJobRunsOneProtocolTwoExchangeAndAwaitsInventory()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        CountingCapability capability = new();
        ScriptedTransport transport = new(Protocol2InterpreterFixture.Started());
        UpdateItemProgress progress = new(job, null);

        UpdateProviderAttempt attempt = await new ApprovedUpdateExecutor(new NonElevatedUpdateRunner(new UnusedUserExecutor()), Runner(capability, transport))
            .ExecuteAsync(job, operation, new(job.Selection, true, true), progress, CancellationToken.None);

        Assert.AreEqual(UpdateProviderAttemptStatus.AwaitingVerification, attempt.Status, attempt.SanitizedDiagnostic);
        Assert.AreEqual(1, transport.Calls, "exactly one exchange");
        Assert.AreSame(operation, transport.Operation, "the helper receives the frozen operation itself");
        Assert.AreEqual(64, transport.Nonce!.Length);
        Assert.AreEqual(1, capability.Calls);
    }

    [TestMethod]
    public async Task WinGetsRestartCodeIsReportedAsARestart()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        ScriptedTransport transport = new(Protocol2InterpreterFixture.Started(WingetRestartExitCodes.RebootRequiredToFinish));

        UpdateExecutionResult result = await Runner(new CountingCapability(), transport).RunReviewedAsync(job, operation, new(job.Selection, true, true),
            CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.RestartReported, result.Status, result.SanitizedOutput);
        Assert.AreEqual(WingetRestartExitCodes.RebootRequiredToFinish, result.ExitCode);
    }

    [TestMethod]
    public async Task AnInterruptedExchangeNeverClaimsThatNothingRan()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        ScriptedTransport transport = new(null);

        UpdateExecutionResult result = await Runner(new CountingCapability(), transport).RunReviewedAsync(job, operation, new(job.Selection, true, true),
            CancellationToken.None);

        Assert.AreEqual(UpdateExecutionStatus.HelperFailed, result.Status);
        StringAssert.Contains(result.SanitizedOutput, "invocation-unknown");
        Assert.IsNotNull(result.OwnedCompletion);
        Assert.IsFalse(result.OwnedCompletion.IsCompleted, "the owned work stays unresolved");
    }

    [TestMethod]
    public async Task AWholePcJobWithoutAReviewedOperationIsRefusedAndNothingIsLaunched()
    {
        (ImmutableUpdateJob job, _) = Frozen();
        ScriptedTransport transport = new(Protocol2InterpreterFixture.Started());

        UpdateProviderAttempt attempt = await new ApprovedUpdateExecutor(new NonElevatedUpdateRunner(new UnusedUserExecutor()),
            Runner(new CountingCapability(), transport)).ExecuteAsync(job, new(job.Selection, true, true), CancellationToken.None);

        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, attempt.Status);
        StringAssert.Contains(attempt.SanitizedDiagnostic, "reviewed-machine-route-unavailable");
        Assert.AreEqual(0, transport.Calls, "protocol 1 is retired: a mode-less machine job has no route");
    }

    [TestMethod]
    public async Task TheRunnerRefusesAForeignOperationOrChangedEvidence()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        ScriptedTransport transport = new(Protocol2InterpreterFixture.Started());
        ElevatedUpdateRunner runner = Runner(new CountingCapability(), transport);
        ImmutableUpdateJob other = Frozen("25.02").Job;

        UpdateExecutionResult foreign = await runner.RunReviewedAsync(other, operation, new(other.Selection, true, true), CancellationToken.None);
        Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, foreign.Denial);

        UpdateExecutionResult mismatch = await runner.RunReviewedAsync(job, operation, new(other.Selection, true, true), CancellationToken.None);
        Assert.AreEqual(UpdateExecutionDenial.CurrentEvidenceMismatch, mismatch.Denial);
        Assert.AreEqual(0, transport.Calls);
    }

    /// <summary>The whole-PC operation frozen from its approved job, as <see cref="WingetMachineHelperRouteTests.Operation"/> builds it.</summary>
    internal static (ImmutableUpdateJob Job, ReviewedUpdateOperation Operation) Frozen(string candidate = "25.01")
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("arp:hklm64:7zip", UpdateProvider.WinGet,
            UpdateSource.WinGet, "7zip.7zip", "24.09", candidate, UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable,
            "mapping-v2")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
        string evidence = WingetMachineHelperRouteTests.Operation().EvidenceId;
        return (job, new(job, Protocol2InterpreterFixture.User, "arp:hklm64:7zip", WingetMachineReviewedRoute.InstallationContextId, evidence,
            Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.WinGetMachineSilentV1,
            ReviewedInstallerMode.Selected(InstallerMode.Silent), null));
    }

    private static UpdateExecutionStatus Map(ReviewedUpdateDispatchObservation observation) => ElevatedUpdateRunner.MapReviewed(observation).Status;

    private static ReviewedUpdateDispatchObservation Observation(ReviewedUpdateInvocation invocation, ReviewedUpdateProviderStatus status, int? exitCode,
        bool uncertain = false) =>
        new(invocation, status, exitCode, "fixture", Task.CompletedTask, true, true, true, false, uncertain);

    private static ElevatedUpdateRunner Runner(IHelperProtocolCapability capability, IReviewedHelperTransport transport) =>
        new(new WingetMachineReviewedExchange(capability, transport));

    private sealed class CountingCapability(HelperProtocolCapabilityStatus status = HelperProtocolCapabilityStatus.Supported) : IHelperProtocolCapability
    {
        internal int Calls { get; private set; }

        public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route)
        {
            Calls++;
            return HelperProtocolCapabilityResult.For(status);
        }

        public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
        {
            Calls++;
            binding = null;
            return HelperProtocolCapabilityResult.For(status);
        }
    }

    /// <summary>Answers with a WinGet machine reply for the frozen operation and the nonce it was given; null facts throw instead.</summary>
    private sealed class ScriptedTransport(UpdateWireFacts? facts) : IReviewedHelperTransport
    {
        internal int Calls { get; private set; }
        internal ReviewedUpdateOperation? Operation { get; private set; }
        internal string? Nonce { get; private set; }

        public Task<ReviewedHelperExchange> ExchangeAsync(ReviewedUpdateOperation operation, string nonce, CancellationToken cancellationToken)
        {
            Calls++;
            Operation = operation;
            Nonce = nonce;
            if (facts is null) throw new IOException("synthetic interrupted exchange");
            Assert.IsTrue(WingetMachineReviewedRoute.TryGetShape(operation, out WinGetMachineEvidence? evidence, out _, out _));
            byte[] frame = ReviewedUpdateWireCodec.CreateWinGetMachineResponse(operation.JobId, operation.Digest, nonce, facts,
                Protocol2InterpreterFixture.Matched(operation.Selection.InstalledVersion, operation.Selection.CandidateVersion, evidence!.RegistrationDigest), "ok");
            return Task.FromResult(new ReviewedHelperExchange(ReviewedHelperTransportOutcome.ResponseFrameReceived, frame,
                Protocol2InterpreterFixture.SettledDesktop));
        }
    }

    private sealed class UnusedUserExecutor : IWingetReviewedUserUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ReviewedUpdateOperation operation, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
            throw new AssertFailedException("a whole-PC job never runs without elevation");
    }
}
