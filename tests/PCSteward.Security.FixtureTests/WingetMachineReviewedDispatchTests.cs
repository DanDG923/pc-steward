using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Phase D stage D4b, stage 2c (design §5.4; FR-UPD-15, FR-UPD-01/03, TM-03/04/07/13): an approved whole-PC WinGet job with a frozen
/// <see cref="UpdateRouteContract.WinGetMachineSilentV1"/> operation runs only on the protocol-2 helper, never protocol 1, and the
/// route is still off: the review gate refuses it, so nothing is launched. The observation mapping is pinned here for stage 3.
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

    // ---- Dispatch, still off ----

    [TestMethod]
    public async Task TheExchangeRefusesTheUnadmittedRouteBeforeAnyCapabilityReadOrLaunch()
    {
        ReviewedUpdateOperation operation = WingetMachineHelperRouteTests.Operation();
        CountingCapability capability = new();
        CountingTransport transport = new();
        int nonces = 0;
        WingetMachineReviewedExchange exchange = new(capability, transport, () => { nonces++; return new string('C', 64); });

        ReviewedUpdateDispatchObservation observation = await exchange.RunAsync(operation, CancellationToken.None);

        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, observation.Status);
        Assert.AreEqual(WingetMachineReviewedExchange.RouteUnavailableDiagnostic, observation.Diagnostic);
        Assert.AreEqual(0, capability.Calls, "the gate refuses first, before the deployment capability is read");
        Assert.AreEqual(0, transport.Calls);
        Assert.AreEqual(0, nonces);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused,
            (await exchange.RunAsync(ReviewedHelperEndpointClientRunFaultTests.ClientOperation(), CancellationToken.None)).Status, "another route is refused");
        Assert.AreEqual(0, transport.Calls);
    }

    [TestMethod]
    public async Task AWholePcJobWithAReviewedOperationNeverReachesProtocolOne()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        UpdateJobExecutionEvidence fresh = new(job.Selection, true, true);
        ProtocolOneTransport protocolOne = new();
        CountingTransport protocolTwo = new();
        ElevatedUpdateRunner runner = new(protocolOne, reviewedExchange: new WingetMachineReviewedExchange(new CountingCapability(), protocolTwo));

        UpdateProviderAttempt attempt = await new ApprovedUpdateExecutor(new NonElevatedUpdateRunner(new UnusedUserExecutor()), runner).ExecuteAsync(job, operation, fresh, new UpdateItemProgress(job, null),
            CancellationToken.None);

        Assert.AreEqual(UpdateProviderAttemptStatus.ProductFailed, attempt.Status);
        StringAssert.Contains(attempt.SanitizedDiagnostic, "reviewed-machine-route-unavailable");
        Assert.AreEqual(0, protocolOne.Calls, "never a fallback to protocol 1");
        Assert.AreEqual(0, protocolTwo.Calls, "the route is still off");

        // Without a reviewed operation the job stays on protocol 1 until stage 3 retires it.
        _ = await new ApprovedUpdateExecutor(new NonElevatedUpdateRunner(new UnusedUserExecutor()), runner).ExecuteAsync(job, fresh, CancellationToken.None);
        Assert.AreEqual(1, protocolOne.Calls);
    }

    [TestMethod]
    public async Task TheRunnerRefusesAMissingExchangeAForeignOperationOrChangedEvidence()
    {
        (ImmutableUpdateJob job, ReviewedUpdateOperation operation) = Frozen();
        UpdateJobExecutionEvidence fresh = new(job.Selection, true, true);
        ProtocolOneTransport protocolOne = new();

        UpdateExecutionResult uncomposed = await new ElevatedUpdateRunner(protocolOne).RunReviewedAsync(job, operation, fresh, CancellationToken.None);
        Assert.AreEqual(UpdateExecutionStatus.Denied, uncomposed.Status);
        Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, uncomposed.Denial);

        ElevatedUpdateRunner runner = new(protocolOne, reviewedExchange: new WingetMachineReviewedExchange(new CountingCapability(), new CountingTransport()));
        ImmutableUpdateJob other = Frozen("25.02").Job;
        UpdateExecutionResult foreign = await runner.RunReviewedAsync(other, operation, new(other.Selection, true, true), CancellationToken.None);
        Assert.AreEqual(UpdateExecutionDenial.ReviewedModeUnavailable, foreign.Denial);

        UpdateJobExecutionEvidence changed = new(other.Selection, true, true);
        UpdateExecutionResult mismatch = await runner.RunReviewedAsync(job, operation, changed, CancellationToken.None);
        Assert.AreEqual(UpdateExecutionDenial.CurrentEvidenceMismatch, mismatch.Denial);
        Assert.AreEqual(0, protocolOne.Calls);
    }

    /// <summary>The whole-PC operation frozen from its approved job, as <see cref="WingetMachineHelperRouteTests.Operation"/> builds it.</summary>
    private static (ImmutableUpdateJob Job, ReviewedUpdateOperation Operation) Frozen(string candidate = "25.01")
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

    private sealed class CountingCapability : IHelperProtocolCapability
    {
        internal int Calls { get; private set; }

        public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route)
        {
            Calls++;
            return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Supported);
        }

        public HelperProtocolCapabilityResult TryHold(UpdateRouteContract route, out HeldHelperBinding? binding)
        {
            Calls++;
            binding = null;
            return HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Supported);
        }
    }

    private sealed class CountingTransport : IReviewedHelperTransport
    {
        internal int Calls { get; private set; }

        public Task<ReviewedHelperExchange> ExchangeAsync(ReviewedUpdateOperation operation, string nonce, CancellationToken cancellationToken)
        {
            Calls++;
            throw new AssertFailedException("the protocol-2 helper must not be launched while the route is off");
        }
    }

    private sealed class UnusedUserExecutor : IWingetReviewedUserUpdateExecutor
    {
        public Task<WingetProcessResult> ExecuteAsync(ReviewedUpdateOperation operation, UpdateItemProgress? progress, CancellationToken cancellationToken) =>
            throw new AssertFailedException("a whole-PC job never runs without elevation");
    }

    private sealed class ProtocolOneTransport : IElevatedHelperTransport
    {
        internal int Calls { get; private set; }

        public Task<ElevatedHelperResult> ExecuteAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new ElevatedHelperResult(ElevatedHelperOutcome.Completed, 1, "fixture"));
        }
    }
}
