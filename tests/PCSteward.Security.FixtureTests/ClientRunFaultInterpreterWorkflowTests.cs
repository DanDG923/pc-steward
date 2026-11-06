using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, ADR-031 design §8 (owner-approved W1, owner decision 4): real client-route
/// frames through the protocol-2 interpreter and the workflow's settled classification. Owner conditions pinned here: a
/// missing or invalid <c>clientRunFault</c> never allows success; the field only preserves or lowers the outcome; every
/// recovery warning is retained for a watchdog stop (each kind), a protection-release failure, and both combined; the
/// package route is unaffected. Written before the Task 50 activation, when <c>HasSupportedMode</c> refused the client route, so
/// the settled classification is driven directly (<see cref="ReviewedUpdateDispatchWorkflow.ClassifyAfterSettlementAsync"/>).
/// </summary>
[TestClass]
public sealed class ClientRunFaultInterpreterWorkflowTests
{
    private const string Installed = "2.7.5";
    private const string Target = "2.8.0";
    private static readonly string Fingerprint = Protocol2InterpreterFixture.Fingerprint;
    private static readonly string Nonce = Protocol2InterpreterFixture.Nonce;

    private static readonly (ReviewedClientRunFaultWire Wire, ReviewedClientWatchdogStop Stop, bool Release, string Message)[] Faults =
    [
        (ReviewedClientRunFaultWire.SetupOverrideDuringRun, ReviewedClientWatchdogStop.SetupOverrideDuringRun, false,
            ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage),
        (ReviewedClientRunFaultWire.RootWritableDuringRun, ReviewedClientWatchdogStop.RootWritableDuringRun, false,
            ChocolateyClientUpdatePresentation.StoppedRootWritableMessage),
        (ReviewedClientRunFaultWire.WatchFailedDuringRun, ReviewedClientWatchdogStop.WatchFailedDuringRun, false,
            ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage),
        (ReviewedClientRunFaultWire.ReleaseIncomplete, ReviewedClientWatchdogStop.None, true,
            ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage),
        (ReviewedClientRunFaultWire.SetupOverrideDuringRunAndReleaseIncomplete, ReviewedClientWatchdogStop.SetupOverrideDuringRun, true,
            ChocolateyClientUpdatePresentation.StoppedSetupOverrideReleaseIncompleteMessage),
        (ReviewedClientRunFaultWire.RootWritableDuringRunAndReleaseIncomplete, ReviewedClientWatchdogStop.RootWritableDuringRun, true,
            ChocolateyClientUpdatePresentation.StoppedRootWritableReleaseIncompleteMessage),
        (ReviewedClientRunFaultWire.WatchFailedDuringRunAndReleaseIncomplete, ReviewedClientWatchdogStop.WatchFailedDuringRun, true,
            ChocolateyClientUpdatePresentation.StoppedWatchFailedReleaseIncompleteMessage),
    ];

    internal static ReviewedUpdateOperation ClientOperation(string installed = Installed, string target = Target)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("74000000-0000-0000-0000-000000000001"),
            Guid.Parse("74000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, "chocolatey", installed, target, UpdateScope.Machine, UpdateArchitecture.Neutral,
                UpdateChannel.Stable, "mapping-v2")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "choco:chocolatey", "machine:choco", ReviewedUpdateOperation.ProfileEvidencePrefix + Fingerprint,
            Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyClientSelfUpdateV1,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    internal static ReviewedHelperFacts ClientMatched() =>
        new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, Installed, Target, Fingerprint);

    internal static byte[] ClientFrame(ReviewedUpdateOperation operation, UpdateWireFacts facts, ReviewedHelperFacts reviewed, ReviewedClientRunFaultWire fault) =>
        ReviewedUpdateWireCodec.CreateClientResponse(operation.JobId, operation.Digest, Nonce, facts, reviewed, fault, "launch-started");

    [TestMethod]
    public void TheClientRouteShapeIsSeparateFromThePackageRoute()
    {
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateOperation package = Protocol2InterpreterFixture.Operation();
        Assert.IsTrue(ReviewedUpdateProtocol2Interpreter.TryGetClientProfileDigest(client, out string digest));
        Assert.AreEqual(Fingerprint, digest);
        Assert.IsFalse(ReviewedUpdateProtocol2Interpreter.TryGetProfileDigest(client, out _), "the package arm never accepts the client route");
        Assert.IsFalse(ReviewedUpdateProtocol2Interpreter.TryGetClientProfileDigest(package, out _), "the client arm never accepts the package route");
        Assert.IsTrue(ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(client, out _, out ReviewedHelperReplyShape clientShape));
        Assert.AreEqual(ReviewedHelperReplyShape.ClientSelfUpdate, clientShape);
        Assert.IsTrue(ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(package, out _, out ReviewedHelperReplyShape packageShape));
        Assert.AreEqual(ReviewedHelperReplyShape.Package, packageShape);
    }

    [TestMethod]
    public void EveryAcceptedClientReplyCarriesItsRunFaultAndNoOtherRouteDoes()
    {
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateProtocol2Interpretation none = Protocol2InterpreterFixture.Interpret(client,
            ClientFrame(client, Protocol2InterpreterFixture.Started(), ClientMatched(), ReviewedClientRunFaultWire.None));
        Assert.AreEqual(ReviewedUpdateInvocation.Started, none.Observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, none.Observation.Status);
        Assert.IsTrue(none.WorkflowObservation.ClientRunFault!.IsNone);

        foreach ((ReviewedClientRunFaultWire wire, ReviewedClientWatchdogStop stop, bool release, _) in Faults)
            foreach (UpdateWireFacts facts in new[] { Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Started(exit: 1), Protocol2InterpreterFixture.Unknown() })
            {
                ReviewedUpdateDispatchObservation observation = Protocol2InterpreterFixture.Interpret(client, ClientFrame(client, facts, ClientMatched(), wire))
                    .WorkflowObservation;
                Assert.AreEqual(stop, observation.ClientRunFault!.WatchdogStop, wire.ToString());
                Assert.AreEqual(release, observation.ClientRunFault.ReleaseIncomplete, wire.ToString());
            }

        // Refusals carry None (S14c) and still settle; the package route never carries a value.
        ReviewedUpdateDispatchObservation refused = Protocol2InterpreterFixture.Interpret(client, ClientFrame(client, Protocol2InterpreterFixture.NotStarted(),
            Protocol2InterpreterFixture.Refused(ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null), ReviewedClientRunFaultWire.None)).WorkflowObservation;
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, refused.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, refused.Status);
        Assert.IsTrue(refused.ClientRunFault!.IsNone);
        ReviewedUpdateOperation package = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateDispatchObservation packageObservation = Protocol2InterpreterFixture.Interpret(package,
            Protocol2InterpreterFixture.Frame(package, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched())).WorkflowObservation;
        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, packageObservation.Status);
        Assert.IsNull(packageObservation.ClientRunFault);
    }

    /// <summary>
    /// Independent review M1: the recorded helper candidate check is defined for the package route only, so an accepted
    /// client-route reply (Matched, Refused or Undetermined) carries NotObserved. The workflow therefore never drops a check
    /// and never prefixes the false <c>helper-check-not-recorded</c> marker. The package route still derives its check.
    /// </summary>
    [TestMethod]
    public void AnAcceptedClientReplyCarriesNoHelperCandidateCheck()
    {
        ReviewedUpdateOperation client = ClientOperation();
        (UpdateWireFacts Facts, ReviewedHelperFacts Reviewed, UpdateDispatchOutcome Outcome, UpdateDispatchSettlementState Settled)[] replies =
        [
            (Protocol2InterpreterFixture.Started(), ClientMatched(), UpdateDispatchOutcome.Success, UpdateDispatchSettlementState.OwnedWorkStopped),
            (Protocol2InterpreterFixture.NotStarted(), Protocol2InterpreterFixture.Refused(ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null),
                UpdateDispatchOutcome.Refused, UpdateDispatchSettlementState.NotInvoked),
            (Protocol2InterpreterFixture.NotStarted(cancelled: true), Protocol2InterpreterFixture.Undetermined(), UpdateDispatchOutcome.Cancelled,
                UpdateDispatchSettlementState.NotInvoked),
        ];
        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed, UpdateDispatchOutcome outcome, UpdateDispatchSettlementState settled) in replies)
        {
            ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(client,
                ClientFrame(client, facts, reviewed, ReviewedClientRunFaultWire.None));
            Assert.AreNotEqual(ReviewedUpdateProtocol2Interpreter.ResponseRejectedDiagnostic, result.Observation.Diagnostic, reviewed.Qualification.ToString());
            Assert.IsNotNull(result.WorkflowObservation.ClientRunFault, "the reply was accepted");
            Assert.AreSame(ReviewedHelperCandidateCheck.NotObserved, result.CandidateCheck, reviewed.Qualification.ToString());
            Assert.IsNull(result.CandidateCheck.LiveText);
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, result.WorkflowObservation.HelperCheck);
            Assert.IsTrue(result.WorkflowObservation.HelperCheck!.IsConsistentWith(client, outcome, settled), "nothing for the workflow to drop");
        }

        ReviewedUpdateOperation package = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation packageResult = Protocol2InterpreterFixture.Interpret(package,
            Protocol2InterpreterFixture.Frame(package, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched()));
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Confirmed, packageResult.CandidateCheck.Kind, "the package route keeps its check");
        Assert.AreEqual(RecordedHelperCheckKind.Confirmed, packageResult.WorkflowObservation.HelperCheck!.Kind);
    }

    [TestMethod]
    public void AMissingWrongRouteOrInvalidFieldIsRejectedAndNeverSucceeds()
    {
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateOperation package = Protocol2InterpreterFixture.Operation();
        // Missing on the client route (a package-shape frame, e.g. from a helper that does not know the field).
        byte[] missing = ReviewedUpdateWireCodec.CreateResponse(client.JobId, client.Digest, Nonce, Protocol2InterpreterFixture.Started(), ClientMatched(), "x");
        AssertRejectedWithoutFault(Protocol2InterpreterFixture.Interpret(client, missing));
        // Present on the package route.
        byte[] wrongRoute = ReviewedUpdateWireCodec.CreateClientResponse(package.JobId, package.Digest, Nonce, Protocol2InterpreterFixture.Started(),
            Protocol2InterpreterFixture.Matched(), ReviewedClientRunFaultWire.None, "x");
        AssertRejectedWithoutFault(Protocol2InterpreterFixture.Interpret(package, wrongRoute));
        // Unknown value, invalid combination, S14c violation.
        string valid = Encoding.UTF8.GetString(ClientFrame(client, Protocol2InterpreterFixture.Started(), ClientMatched(), ReviewedClientRunFaultWire.None));
        foreach (string invalid in new[] { "Unknown", "none", "SetupOverrideDuringRun+WatchFailedDuringRun", "ReleaseIncomplete+RootWritableDuringRun", "" })
            AssertRejectedWithoutFault(Protocol2InterpreterFixture.Interpret(client,
                Encoding.UTF8.GetBytes(valid.Replace("\"clientRunFault\":\"None\"", "\"clientRunFault\":\"" + invalid + "\"", StringComparison.Ordinal))));
        string notStarted = Encoding.UTF8.GetString(ClientFrame(client, Protocol2InterpreterFixture.NotStarted(), ClientMatched(), ReviewedClientRunFaultWire.None));
        AssertRejectedWithoutFault(Protocol2InterpreterFixture.Interpret(client, Encoding.UTF8.GetBytes(
            notStarted.Replace("\"clientRunFault\":\"None\"", "\"clientRunFault\":\"WatchFailedDuringRun\"", StringComparison.Ordinal))));
        // A valid field never rescues an operation-bound failure (P1: the helper derived another target).
        ReviewedHelperFacts otherTarget = new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, Installed, "2.9.0", Fingerprint);
        AssertRejectedWithoutFault(Protocol2InterpreterFixture.Interpret(client, ClientFrame(client, Protocol2InterpreterFixture.Started(), otherTarget,
            ReviewedClientRunFaultWire.None)));
    }

    [TestMethod]
    public async Task OnlyNoneWithAnExactPostcheckMatchIsSuccess()
    {
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateDispatchObservation observation = Accepted(client, Protocol2InterpreterFixture.Started(), ReviewedClientRunFaultWire.None);
        ClientVerifier verified = new(new(true, false));
        (UpdateDispatchOutcome outcome, string message) = await Workflow(verified).ClassifyAfterSettlementAsync(client, Owner(), observation, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Success, outcome);
        Assert.AreEqual(ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage, message);
        Assert.AreEqual(1, verified.ClientCalls);
        Assert.AreEqual(0, verified.PlainCalls, "the client route is verified only through the fact-taking entry");
        Assert.IsTrue(verified.LastFault!.IsNone);

        ClientVerifier miss = new(new(false, false, PostUpdateVerificationDetail.ClientChanged));
        (outcome, message) = await Workflow(miss).ClassifyAfterSettlementAsync(client, Owner(), observation, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, outcome);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.NotVerifiedReleaseMessage, message);

        // A verifier without the client entry cannot verify the route.
        PlainVerifier plain = new(new(true, false));
        (outcome, message) = await Workflow(plain).ClassifyAfterSettlementAsync(client, Owner(), observation, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, outcome);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.NotVerifiedUnreadableMessage, message);
        Assert.AreEqual(0, plain.Calls);
    }

    [TestMethod]
    public async Task EveryRunFaultIsUnverifiedWithItsRecoveryTextAndSkipsVerification()
    {
        ReviewedUpdateOperation client = ClientOperation();
        foreach ((ReviewedClientRunFaultWire wire, ReviewedClientWatchdogStop stop, bool release, string expected) in Faults)
            // exit 0 (would otherwise verify: Success → Unverified), exit 1 (a watchdog kill: Failed → Unverified), cancelled.
            foreach (UpdateWireFacts facts in new[] { Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Started(exit: 1),
                         Protocol2InterpreterFixture.Started(cancelled: true) })
            {
                ClientVerifier verifier = new(new(true, false));
                ReviewedUpdateDispatchObservation observation = Accepted(client, facts, wire);
                (UpdateDispatchOutcome outcome, string message) =
                    await Workflow(verifier).ClassifyAfterSettlementAsync(client, Owner(), observation, CancellationToken.None);
                Assert.AreEqual(UpdateDispatchOutcome.Unverified, outcome, wire.ToString());
                Assert.AreEqual(expected, message, wire.ToString());
                Assert.AreEqual(0, verifier.ClientCalls + verifier.PlainCalls, "verification cannot raise a faulted run");
                Assert.IsTrue(ChocolateyClientUpdatePresentation.IsNotVerifiedMessage(message), "history recognises the fixed text");
                AssertRecoveryText(stop, release, message);
            }
    }

    [TestMethod]
    public async Task AMissingRunFaultOnASettledClientRunIsNeverSuccess()
    {
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateDispatchObservation accepted = Accepted(client, Protocol2InterpreterFixture.Started(), ReviewedClientRunFaultWire.None);
        ClientVerifier verifier = new(new(true, false));
        (UpdateDispatchOutcome outcome, string message) = await Workflow(verifier)
            .ClassifyAfterSettlementAsync(client, Owner(), accepted with { ClientRunFault = null }, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, outcome);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.RunFaultNotReportedMessage, message);
        Assert.AreEqual(0, verifier.ClientCalls + verifier.PlainCalls);
        // Independent review L1: the run did not happen under the reviewed conditions, so the text never claims "Installed".
        StringAssert.StartsWith(message, "Ran, not verified:");
        Assert.IsFalse(message.Contains("Installed", StringComparison.Ordinal), message);
        StringAssert.EndsWith(message, ChocolateyClientUpdatePresentation.StaysUnavailableUntilReviewedText);
    }

    [TestMethod]
    public void ALostLaunchOnTheClientRouteIsUncertainWithTheUntrustedFolderCaution()
    {
        // Independent review M1 (design §8.6): the helper's UpgradeAsync timeout ("launch-unresponsive"), throw or unknown arm
        // ("launch-unknown") and a fault escaping after launch ("internal-fault-after-launch") reply Unknown with run fault None.
        ReviewedUpdateOperation client = ClientOperation();
        foreach (string diagnostic in new[] { "launch-unresponsive", "launch-unknown", "internal-fault-after-launch" })
        {
            ReviewedUpdateDispatchObservation observation = Protocol2InterpreterFixture.Interpret(client, ReviewedUpdateWireCodec.CreateClientResponse(
                client.JobId, client.Digest, Nonce, Protocol2InterpreterFixture.Unknown(), ClientMatched(), ReviewedClientRunFaultWire.None, diagnostic))
                .WorkflowObservation;
            Assert.AreEqual(ReviewedUpdateInvocation.Unknown, observation.Invocation, diagnostic);
            Assert.IsTrue(observation.ExecutionUncertain, "an Unknown launch never settles, so the outcome is Uncertain");
            Assert.IsTrue(observation.ClientRunFault!.IsNone);
            AssertLostTrackCaution(client, observation);
        }

        // A started launch whose settlement was not confirmed (Uncertain) with run fault None.
        ReviewedUpdateDispatchObservation startedUnsettled = Accepted(client, Protocol2InterpreterFixture.Started(exit: null), ReviewedClientRunFaultWire.None);
        Assert.AreEqual(ReviewedUpdateInvocation.Started, startedUnsettled.Invocation);
        Assert.IsTrue(startedUnsettled.ExecutionUncertain);
        AssertLostTrackCaution(client, startedUnsettled);

        // Desktop-side: the dispatcher threw (the workflow's synthetic Unknown observation) or the reply was rejected (no field).
        AssertLostTrackCaution(client, new ReviewedUpdateDispatchObservation(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Failed, null,
            "dispatch-interrupted; invocation-unknown", Task.CompletedTask, false, false, false, false, true));
        string valid = Encoding.UTF8.GetString(ClientFrame(client, Protocol2InterpreterFixture.Started(), ClientMatched(), ReviewedClientRunFaultWire.None));
        ReviewedUpdateProtocol2Interpretation rejected = Protocol2InterpreterFixture.Interpret(client,
            Encoding.UTF8.GetBytes(valid.Replace("\"clientRunFault\":\"None\"", "\"clientRunFault\":\"Unknown\"", StringComparison.Ordinal)));
        AssertRejectedWithoutFault(rejected);
        Assert.AreEqual(ReviewedUpdateInvocation.Unknown, rejected.WorkflowObservation.Invocation);
        AssertLostTrackCaution(client, rejected.WorkflowObservation);
    }

    [TestMethod]
    public void TheLostTrackCautionIsOnlyForAnUnreportedClientLaunch()
    {
        ReviewedUpdateOperation client = ClientOperation();
        string caution = ChocolateyClientUpdatePresentation.LostTrackCautionMessage;
        // A reported fault keeps its own message only (its advice is never mixed with another).
        ReviewedUpdateDispatchObservation faulted = Protocol2InterpreterFixture.Interpret(client,
            ClientFrame(client, Protocol2InterpreterFixture.Unknown(), ClientMatched(), ReviewedClientRunFaultWire.WatchFailedDuringRun)).WorkflowObservation;
        string message = ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, faulted);
        StringAssert.EndsWith(message, " " + ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage);
        Assert.IsFalse(message.Contains(caution, StringComparison.Ordinal));
        // Independent review L1: the codec-validated fault value selects the fixed leading marker; never the lost-track marker.
        StringAssert.StartsWith(message, "client-run-fault-watch-failed Owned work");
        Assert.IsFalse(message.Contains(ChocolateyClientUpdatePresentation.LostTrackMarker, StringComparison.Ordinal));
        // Nothing was launched: no caution and no marker.
        ReviewedUpdateDispatchObservation notStarted = new(ReviewedUpdateInvocation.NotStarted, ReviewedUpdateProviderStatus.Failed, null, "x",
            Task.CompletedTask, false, false, false, false, true);
        Assert.IsFalse(ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, notStarted).Contains(caution, StringComparison.Ordinal));
        StringAssert.StartsWith(ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, notStarted), "Owned work");
        // The package route is unchanged.
        ReviewedUpdateOperation package = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateDispatchObservation packageUnknown = Protocol2InterpreterFixture.Interpret(package,
            Protocol2InterpreterFixture.Frame(package, Protocol2InterpreterFixture.Unknown(), Protocol2InterpreterFixture.Matched(), "launch-unresponsive"))
            .WorkflowObservation;
        Assert.AreEqual("m", ReviewedUpdateDispatchWorkflow.WithClientLostTrackCaution(package, packageUnknown, "m"));
        Assert.IsFalse(ReviewedUpdateDispatchWorkflow.UnsettledMessage(package, packageUnknown).Contains(caution, StringComparison.Ordinal));
        Assert.AreEqual("m", ReviewedUpdateDispatchWorkflow.WithClientUncertainMarker(package, packageUnknown, "m"));
        string marker = ChocolateyClientUpdatePresentation.LostTrackMarker;
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(package, UncertainTerminal(marker + " x")));
        // Independent review L1: history shows the caution only for an Uncertain client record that starts with the workflow's
        // fixed marker; the caution's text (at the end or anywhere) is never interpreted.
        Assert.AreEqual(caution, ReviewedUpdateHistoryPresentation.UncertainDetail(client, UncertainTerminal(marker + " x")));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, UncertainTerminal("x " + caution)));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, UncertainTerminal(caution + " x")));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, UncertainTerminal("x " + marker + " " + caution)));
        Assert.IsNull(ReviewedUpdateHistoryPresentation.UncertainDetail(client, new ReviewedUpdateTerminalRecord(UpdateDispatchOutcome.Unverified,
            Protocol2InterpreterFixture.ReviewedAt, null, UpdateDispatchSettlementState.OwnedWorkStopped, marker + " x " + caution)));
    }

    private static void AssertLostTrackCaution(ReviewedUpdateOperation client, ReviewedUpdateDispatchObservation observation)
    {
        string caution = ChocolateyClientUpdatePresentation.LostTrackCautionMessage;
        string message = ReviewedUpdateDispatchWorkflow.UnsettledMessage(client, observation);
        StringAssert.EndsWith(message, " " + caution, observation.Diagnostic);
        StringAssert.StartsWith(message, ChocolateyClientUpdatePresentation.LostTrackMarker + " ", "independent review L1: a leading fixed marker");
        StringAssert.Contains(caution, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
        Assert.IsFalse(message.Contains("choco upgrade", StringComparison.Ordinal) || message.Contains("--force", StringComparison.Ordinal), message);
        // As FinalizeAsync records it (sanitized, bounded), history recognises the caution on the Uncertain record.
        string recorded = SanitizedDiagnosticBundle.Truncate(SanitizedDiagnosticBundle.Sanitize(message), 4000);
        ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(new ReviewedUpdateHistoryRecord(client.JobId, client,
            UpdateDispatchState.Uncertain, Protocol2InterpreterFixture.ReviewedAt, UncertainTerminal(recorded), true));
        Assert.AreEqual(caution, view.VerificationDetail);
        StringAssert.EndsWith(view.Diagnostic, caution);
        Assert.IsFalse(view.Diagnostic.Contains(ChocolateyClientUpdatePresentation.LostTrackMarker, StringComparison.Ordinal), "the marker is not shown");
        StringAssert.StartsWith(view.Status, "Uncertain:");
    }

    private static ReviewedUpdateTerminalRecord UncertainTerminal(string diagnostic) =>
        new(UpdateDispatchOutcome.Uncertain, Protocol2InterpreterFixture.ReviewedAt, null, UpdateDispatchSettlementState.InvocationUnknown, diagnostic);

    [TestMethod]
    public async Task ThePackageRouteIgnoresTheFieldAndKeepsItsOutcomes()
    {
        ReviewedUpdateOperation package = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateDispatchObservation observation = Protocol2InterpreterFixture.Interpret(package,
            Protocol2InterpreterFixture.Frame(package, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched())).WorkflowObservation;
        PlainVerifier verifier = new(new(true, false));
        (UpdateDispatchOutcome outcome, _) = await Workflow(verifier).ClassifyAfterSettlementAsync(package, Owner(), observation, CancellationToken.None);
        Assert.AreEqual(UpdateDispatchOutcome.Success, outcome);
        Assert.AreEqual(1, verifier.Calls);
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.ClientRunFaultOutcome(package, observation));
        ReviewedClientRunFault forged = ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.WatchFailedDuringRun, true);
        Assert.IsNull(ReviewedUpdateDispatchWorkflow.ClientRunFaultOutcome(package, observation with { ClientRunFault = forged }));
        Assert.AreEqual("m", ReviewedUpdateDispatchWorkflow.WithClientRunFaultWarning(package, observation with { ClientRunFault = forged }, "m"));
    }

    [TestMethod]
    public void UncertainAndNotInvokedPathsKeepTheirOutcomeButRetainTheWarning()
    {
        // An Unknown launch with a fault is Uncertain (barrier kept); the warning is appended, never dropped.
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateProtocol2Interpretation unknown = Protocol2InterpreterFixture.Interpret(client,
            ClientFrame(client, Protocol2InterpreterFixture.Unknown(), ClientMatched(), ReviewedClientRunFaultWire.RootWritableDuringRunAndReleaseIncomplete));
        Assert.IsTrue(unknown.Observation.ExecutionUncertain);
        string message = ReviewedUpdateDispatchWorkflow.WithClientRunFaultWarning(client, unknown.WorkflowObservation, "Owned work is unresolved.");
        Assert.AreEqual("Owned work is unresolved. " + ChocolateyClientUpdatePresentation.StoppedRootWritableReleaseIncompleteMessage, message);
        ReviewedUpdateDispatchObservation none = Accepted(client, Protocol2InterpreterFixture.Started(), ReviewedClientRunFaultWire.None);
        Assert.AreEqual("m", ReviewedUpdateDispatchWorkflow.WithClientRunFaultWarning(client, none, "m"));
        Assert.AreEqual("m", ReviewedUpdateDispatchWorkflow.WithClientRunFaultWarning(client, none with { ClientRunFault = null }, "m"));
    }

    [TestMethod]
    public void TheLingeringMarkerCoversTheClientRoute()
    {
        ReviewedUpdateOperation client = ClientOperation();
        ReviewedUpdateDispatchObservation observation = Accepted(client, Protocol2InterpreterFixture.Started(), ReviewedClientRunFaultWire.None)
            with { Diagnostic = "launch-started; " + ReviewedUpdateLingeringProcesses.Token };
        Assert.AreEqual(ReviewedUpdateLingeringProcesses.Token + " m", ReviewedUpdateDispatchWorkflow.WithLingeringMarker(client, observation, "m"));
        Assert.AreEqual("m", ReviewedUpdateDispatchWorkflow.WithLingeringMarker(client, observation with { Invocation = ReviewedUpdateInvocation.NotStarted }, "m"));
    }

    private static void AssertRecoveryText(ReviewedClientWatchdogStop stop, bool release, string message)
    {
        bool untrusted = stop is ReviewedClientWatchdogStop.SetupOverrideDuringRun or ReviewedClientWatchdogStop.RootWritableDuringRun;
        if (untrusted)
        {
            // Independent review H1: the untrusted-folder advice, never a choco command to run elevated.
            StringAssert.Contains(message, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
            Assert.IsFalse(message.Contains("choco upgrade", StringComparison.Ordinal), message);
            Assert.IsFalse(message.Contains("--force", StringComparison.Ordinal), message);
        }
        if (stop == ReviewedClientWatchdogStop.WatchFailedDuringRun) StringAssert.Contains(message, ChocolateyClientUpdatePresentation.WatchdogRecoveryStep);
        if (release && stop != ReviewedClientWatchdogStop.None) StringAssert.Contains(message, ChocolateyClientUpdatePresentation.ReleaseIncompleteAlsoNote);
        if (release && stop == ReviewedClientWatchdogStop.None) Assert.AreEqual(ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage, message);
        // Independent review I2: every release-incomplete message, alone or combined, keeps the "stay unavailable" sentence.
        if (release) StringAssert.EndsWith(message, ChocolateyClientUpdatePresentation.StaysUnavailableUntilReviewedText);
        Assert.IsFalse(message.StartsWith("Installed", StringComparison.Ordinal), message);
        if (!release) Assert.IsFalse(message.Contains(ChocolateyClientUpdatePresentation.ReleaseIncompleteAlsoNote, StringComparison.Ordinal));
    }

    private static ReviewedUpdateDispatchObservation Accepted(ReviewedUpdateOperation operation, UpdateWireFacts facts, ReviewedClientRunFaultWire fault)
    {
        ReviewedUpdateDispatchObservation observation = Protocol2InterpreterFixture.Interpret(operation, ClientFrame(operation, facts, ClientMatched(), fault))
            .WorkflowObservation;
        Assert.AreNotEqual(ReviewedUpdateProtocol2Interpreter.ResponseRejectedDiagnostic, observation.Diagnostic);
        Assert.IsNotNull(observation.ClientRunFault);
        return observation;
    }

    private static void AssertRejectedWithoutFault(ReviewedUpdateProtocol2Interpretation result)
    {
        Protocol2InterpreterFixture.AssertRejected(result);
        Assert.IsNull(result.WorkflowObservation.ClientRunFault);
    }

    private static ReviewedUpdateDispatchWorkflow Workflow(IReviewedUpdateVerifier verifier) =>
        new((work, _, ct) => work(Owner(), ct), new NoEvidence(), new NoDispatcher(), verifier, new NoStore());

    private static OperationOwnership Owner() =>
        new(Protocol2InterpreterFixture.User, OperationGateContract.NamePrefix + Protocol2InterpreterFixture.User, false, reconciled: true);

    private sealed class ClientVerifier(PostUpdateInventoryResult result) : IReviewedUpdateVerifier, IReviewedClientUpdateVerifier
    {
        internal int ClientCalls;
        internal int PlainCalls;
        internal ReviewedClientRunFault? LastFault;

        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken)
        {
            PlainCalls++;
            return Task.FromResult(new PostUpdateInventoryResult(true, false));
        }

        public Task<PostUpdateInventoryResult> VerifyClientAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
            ReviewedClientRunFault runFault, CancellationToken cancellationToken)
        {
            ClientCalls++;
            LastFault = runFault;
            return Task.FromResult(result);
        }
    }

    private sealed class PlainVerifier(PostUpdateInventoryResult result) : IReviewedUpdateVerifier
    {
        internal int Calls;

        public Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class NoEvidence : IReviewedUpdateDispatchEvidenceSource
    {
        public Task<FreshUpdateDispatchEvidence?> ObserveAsync(ReviewedUpdateOperation operation, OperationOwnership ownership, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoDispatcher : IReviewedUpdateRouteDispatcher
    {
        public bool Supports(ReviewedUpdateOperation operation) => false;
        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoStore : IUpdateDispatchStore
    {
        public Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership,
            DateTimeOffset claimedAt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal,
            UpdateDispatchSettlement settlement, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
