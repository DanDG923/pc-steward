using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.EndpointFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, ADR-031 design §8.1/§8.2 (owner-approved W1): the helper endpoint writes the
/// client reply shape for <c>ChocolateyClientSelfUpdateV1</c> only, populates <c>clientRunFault</c> from the launch's settled
/// binding state (watchdog stop, incomplete hold release, both), writes None on every other client decision, and never drops
/// a run fault on another route (nothing is written: Uncertain). Uses the fake item and the injected capability seam; a real
/// G1 refuses the client route (allow-list unchanged).
/// </summary>
[TestClass]
public sealed class ReviewedHelperEndpointClientRunFaultTests
{
    internal static ReviewedUpdateOperation ClientOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("75000000-0000-0000-0000-000000000001"),
            Guid.Parse("75000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, "chocolatey", F.Installed, F.Candidate, UpdateScope.Machine, UpdateArchitecture.Neutral,
                UpdateChannel.Stable, "mapping-v2")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "choco:chocolatey", "machine:choco", ReviewedUpdateOperation.ProfileEvidencePrefix + F.Fingerprint,
            Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyClientSelfUpdateV1,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    [TestMethod]
    [DataRow((int)ChocolateyClientWatchdogFault.None, false, (int)ReviewedClientRunFaultWire.None)]
    [DataRow((int)ChocolateyClientWatchdogFault.None, true, (int)ReviewedClientRunFaultWire.ReleaseIncomplete)]
    [DataRow((int)ChocolateyClientWatchdogFault.SetupOverrideDuringRun, false, (int)ReviewedClientRunFaultWire.SetupOverrideDuringRun)]
    [DataRow((int)ChocolateyClientWatchdogFault.SetupOverrideDuringRun, true, (int)ReviewedClientRunFaultWire.SetupOverrideDuringRunAndReleaseIncomplete)]
    [DataRow((int)ChocolateyClientWatchdogFault.RootWritableDuringRun, false, (int)ReviewedClientRunFaultWire.RootWritableDuringRun)]
    [DataRow((int)ChocolateyClientWatchdogFault.RootWritableDuringRun, true, (int)ReviewedClientRunFaultWire.RootWritableDuringRunAndReleaseIncomplete)]
    [DataRow((int)ChocolateyClientWatchdogFault.WatchFailedDuringRun, false, (int)ReviewedClientRunFaultWire.WatchFailedDuringRun)]
    [DataRow((int)ChocolateyClientWatchdogFault.WatchFailedDuringRun, true, (int)ReviewedClientRunFaultWire.WatchFailedDuringRunAndReleaseIncomplete)]
    [DataRow(99, false, (int)ReviewedClientRunFaultWire.WatchFailedDuringRun)]
    public async Task TheLaunchPopulatesTheFieldFromTheBindingState(int watchdog, bool releaseIncomplete, int expected)
    {
        foreach (int exit in new[] { 0, 1 })
        {
            EndpointHarness harness = new();
            harness.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(
                new ChocolateyUpgradeResult.Observed(F.Started(exit), (ChocolateyClientWatchdogFault)watchdog, releaseIncomplete));
            ReviewedUpdateOperation operation = ClientOperation();
            EndpointRun run = await harness.ExchangeAsync(operation);

            FramedUpdateResponse response = ReadClient(run);
            Assert.AreEqual((ReviewedClientRunFaultWire)expected, response.ClientRunFault);
            Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation);
            Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
            Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(run.Exchange!.ResponseFrame, operation.JobId, operation.Digest, F.Nonce, out _),
                "a client reply is never a valid package reply");
            ReviewedClientRunFault fault = run.Interpretation.WorkflowObservation.ClientRunFault!;
            Assert.AreEqual(watchdog == 0 && !releaseIncomplete, fault.IsNone);
            Assert.AreEqual(releaseIncomplete, fault.ReleaseIncomplete);
        }
    }

    [TestMethod]
    public async Task EveryOtherClientDecisionCarriesNone()
    {
        // A1 refusal (stale request), a stage-B refusal and a definitive launch failure.
        EndpointHarness stale = new();
        ReviewedUpdateOperation operation = ClientOperation();
        EndpointRun refused = await stale.ExchangeAsync(operation, () => F.Request(operation, stale.Clock.GetUtcNow().AddMinutes(-3)));
        Assert.AreEqual(ReviewedClientRunFaultWire.None, ReadClient(refused).ClientRunFault);
        Assert.AreEqual(ReviewedHelperRefusalReason.RequestNotAdmitted, ReadClient(refused).Reviewed.RefusalReason);

        EndpointHarness stageB = new();
        stageB.Item.Observe = (_, _, _, _) => new ChocolateyStageValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, null, null);
        EndpointRun b = await stageB.ExchangeAsync(ClientOperation());
        Assert.AreEqual(ReviewedClientRunFaultWire.None, ReadClient(b).ClientRunFault);
        Assert.AreEqual(ReviewedHelperRefusalReason.MachineInputsUnsupported, ReadClient(b).Reviewed.RefusalReason);

        EndpointHarness notStarted = new();
        notStarted.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.Observed(F.NotStarted()));
        EndpointRun launch = await notStarted.ExchangeAsync(ClientOperation());
        Assert.AreEqual(ReviewedClientRunFaultWire.None, ReadClient(launch).ClientRunFault);
        Assert.AreEqual(UpdateWireInvocation.NotStarted, ReadClient(launch).Facts.Invocation);
    }

    [TestMethod]
    public async Task AnInconsistentRunFaultWritesNothingAndTheDesktopIsUncertain()
    {
        // A fault reported with a NotStarted launch (S14c) on the client route, and any fault on the package route.
        EndpointHarness client = new();
        client.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(
            new ChocolateyUpgradeResult.Observed(F.NotStarted(), ChocolateyClientWatchdogFault.WatchFailedDuringRun, false));
        EndpointRun clientRun = await client.ExchangeAsync(ClientOperation());
        Assert.AreEqual(ReviewedHelperEndpointExit.NoResponse, clientRun.Result.Exit, clientRun.Result.Diagnostic);
        StringAssert.StartsWith(clientRun.Result.Diagnostic, "response-refused-by-codec");
        Protocol2InterpreterFixture.AssertRejected(clientRun.Interpretation);

        EndpointHarness package = new();
        package.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(
            new ChocolateyUpgradeResult.Observed(F.Started(), ChocolateyClientWatchdogFault.None, true));
        EndpointRun packageRun = await package.ExchangeAsync(F.Operation());
        Assert.AreEqual(ReviewedHelperEndpointExit.NoResponse, packageRun.Result.Exit, packageRun.Result.Diagnostic);
        Protocol2InterpreterFixture.AssertRejected(packageRun.Interpretation);
    }

    [TestMethod]
    public async Task ThePackageRouteReplyIsUnchanged()
    {
        EndpointHarness harness = new();
        EndpointRun run = await harness.ExchangeAsync(F.Operation());
        Assert.IsNull(run.Response.ClientRunFault, "the package reply has no clientRunFault");
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(run.Exchange!.ResponseFrame, run.Operation.JobId, run.Operation.Digest, F.Nonce,
            ReviewedHelperReplyShape.ClientSelfUpdate, out _));
        Assert.IsNull(run.Interpretation.WorkflowObservation.ClientRunFault);
    }

    private static FramedUpdateResponse ReadClient(EndpointRun run)
    {
        Assert.AreEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, run.Exchange!.Outcome, run.Result.Diagnostic);
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(run.Exchange.ResponseFrame, run.Operation.JobId, run.Operation.Digest, F.Nonce,
            ReviewedHelperReplyShape.ClientSelfUpdate, out FramedUpdateResponse? response), run.Result.Diagnostic);
        return response!;
    }
}
