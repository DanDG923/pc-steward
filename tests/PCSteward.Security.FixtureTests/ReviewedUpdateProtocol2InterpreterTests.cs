using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.Security.FixtureTests;

/// <summary>Shared frozen Chocolatey runtime-limits operation and frames, built through the real protocol-2 codec.</summary>
internal static class Protocol2InterpreterFixture
{
    internal const string User = "S-1-5-21-11-22-33-1001";
    internal const string Installed = ReviewedUpdateWireCodecFixture.Installed;
    internal const string Candidate = ReviewedUpdateWireCodecFixture.Candidate;
    internal const string OtherInstalled = "1.2.2";
    internal const string OtherCandidate = "1.2.5";
    internal static readonly string Fingerprint = ReviewedUpdateWireCodecFixture.Fingerprint;
    internal static readonly string OtherFingerprint = Convert.ToHexString(SHA256.HashData("choco-profile-v2 recomputed for another profile"u8));
    internal static readonly string Nonce = new('D', 64);
    internal static readonly DateTimeOffset ReviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    internal static readonly ReviewedHelperDesktopObservation SettledDesktop = new(Task.CompletedTask, true, true, false, true, true);

    internal static ReviewedUpdateOperation Operation(string? evidenceId = null, string installed = Installed, string candidate = Candidate,
        UpdateRouteContract route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, Guid? jobId = null)
    {
        ImmutableUpdateJob job = new UpdateJobReview(jobId ?? Guid.Parse("71000000-0000-0000-0000-000000000001"),
            Guid.Parse("71000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:git", UpdateProvider.Chocolatey,
                UpdateSource.ChocolateyCommunity, "git", installed, candidate, UpdateScope.Machine, UpdateArchitecture.Neutral,
                UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, User, "choco:git", "machine:choco", evidenceId ?? "choco-profile-v2:" + Fingerprint, ReviewedAt.AddSeconds(-1), route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    internal static byte[] Frame(ReviewedUpdateOperation operation, UpdateWireFacts facts, ReviewedHelperFacts reviewed, string diagnostic = "helper ok") =>
        ReviewedUpdateWireCodec.CreateResponse(operation.JobId, operation.Digest, Nonce, facts, reviewed, diagnostic);

    internal static ReviewedUpdateProtocol2Interpretation Interpret(ReviewedUpdateOperation operation, byte[] frame,
        ReviewedHelperDesktopObservation? desktop = null) =>
        ReviewedUpdateProtocol2Interpreter.Interpret(operation, ReviewedHelperTransportOutcome.ResponseFrameReceived, frame, Nonce, desktop ?? SettledDesktop);

    internal static UpdateWireFacts Started(int? exit = 0, bool cancelled = false, bool timedOut = false, bool outputLimit = false) =>
        new(UpdateWireInvocation.Started, exit, exit.HasValue, true, true, false, exit.HasValue, cancelled, timedOut, outputLimit);

    internal static UpdateWireFacts NotStarted(bool cancelled = false, bool timedOut = false, bool outputLimit = false, bool settled = true) =>
        new(UpdateWireInvocation.NotStarted, null, false, false, false, false, settled, cancelled, timedOut, outputLimit);

    internal static UpdateWireFacts Unknown() => new(UpdateWireInvocation.Unknown, null, false, false, false, false, false, false, false, false);

    internal static ReviewedHelperFacts Matched(string? installed = Installed, string? candidate = Candidate, string? fingerprint = null) =>
        new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, installed, candidate, fingerprint ?? Fingerprint);

    internal static ReviewedHelperFacts Undetermined(string? installed = null, string? candidate = null, string? fingerprint = null) =>
        new(ReviewedHelperQualification.Undetermined, ReviewedHelperRefusalReason.None, installed, candidate, fingerprint);

    internal static ReviewedHelperFacts Refused(ReviewedHelperRefusalReason reason, string? installed, string? candidate, string? fingerprint) =>
        new(ReviewedHelperQualification.Refused, reason, installed, candidate, fingerprint);

    internal static void AssertRejected(ReviewedUpdateProtocol2Interpretation result)
    {
        ReviewedUpdateDispatchObservation observation = result.Observation;
        Assert.AreEqual(ReviewedUpdateInvocation.Unknown, observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Failed, observation.Status);
        Assert.AreEqual(ReviewedUpdateProtocol2Interpreter.ResponseRejectedDiagnostic, observation.Diagnostic);
        Assert.IsNull(observation.ExitCode);
        Assert.IsTrue(observation.ExecutionUncertain);
        Assert.IsFalse(observation.DirectExitObserved || observation.OwnedIoComplete || observation.PipeEndOfFile);
        Assert.IsFalse(observation.RestartReported);
        Assert.AreSame(ReviewedHelperCandidateCheck.NotObserved, result.CandidateCheck);
        Assert.IsNull(result.CandidateCheck.LiveText);
    }

    /// <summary>True when the workflow would treat the observation as positively settled (mirrors its settlement inputs).</summary>
    internal static bool IsSettled(ReviewedUpdateDispatchObservation o) => o.Invocation switch
    {
        ReviewedUpdateInvocation.NotStarted => !o.ExecutionUncertain && !o.TerminationFailed && o.ExitCode is null && o.OwnedRoot.IsCompletedSuccessfully,
        ReviewedUpdateInvocation.Started => o.OwnedRoot.IsCompletedSuccessfully && o.DirectExitObserved && o.OwnedIoComplete && o.PipeEndOfFile &&
            !o.TerminationFailed && !o.ExecutionUncertain,
        _ => false,
    };
}

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13: the desktop protocol-2 interpreter (reviewed fact extension rev 3 §3.3, §4.1, §8,
/// §10 N1–N4), fed through the real <see cref="ReviewedUpdateWireCodec"/>. Transport, helper, dispatcher composition,
/// capability seam, history format and verifier are later slices.
/// </summary>
[TestClass]
public sealed class ReviewedUpdateProtocol2InterpreterTests
{
    private const string Installed = Protocol2InterpreterFixture.Installed;
    private const string Candidate = Protocol2InterpreterFixture.Candidate;
    private const string OtherInstalled = Protocol2InterpreterFixture.OtherInstalled;
    private const string OtherCandidate = Protocol2InterpreterFixture.OtherCandidate;
    private static readonly string Fingerprint = Protocol2InterpreterFixture.Fingerprint;
    private static readonly string OtherFingerprint = Protocol2InterpreterFixture.OtherFingerprint;
    private static readonly string Nonce = Protocol2InterpreterFixture.Nonce;
    private static readonly string[] CheckKindNames = ["NotObserved", "Confirmed", "Refused", "Unconfirmed"];

    [TestMethod]
    public void MatchedStartedSettledZeroExitIsCompletedAndConfirmed()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched()));
        ReviewedUpdateDispatchObservation observation = result.Observation;
        Assert.AreEqual(ReviewedUpdateInvocation.Started, observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, observation.Status);
        Assert.AreEqual(0, observation.ExitCode);
        Assert.AreEqual("helper ok", observation.Diagnostic);
        Assert.IsTrue(observation.DirectExitObserved && observation.OwnedIoComplete && observation.PipeEndOfFile);
        Assert.IsFalse(observation.TerminationFailed || observation.ExecutionUncertain || observation.RestartReported);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(observation));
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Confirmed, result.CandidateCheck.Kind);
        Assert.AreEqual(Installed, result.CandidateCheck.ReportedInstalledVersion);
        Assert.AreEqual(Candidate, result.CandidateCheck.ReportedCandidateVersion);
        Assert.AreEqual(ReviewedHelperCandidateCheck.ConfirmedText, result.CandidateCheck.LiveText);
        Assert.IsFalse(result.CandidateCheck.LiveText!.Contains("success", StringComparison.OrdinalIgnoreCase));
        // The candidate check never changes the operation's frozen provenance.
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, operation.CandidateProvenance);
    }

    [TestMethod]
    [DataRow("matched-installed", "P1")]
    [DataRow("matched-candidate", "P1")]
    [DataRow("matched-fingerprint-recomputed", "P1")]
    [DataRow("profile-fingerprint-equals-F", "P3")]
    [DataRow("profile-installed-changed", "P3")]
    [DataRow("candidate-equal", "P4")]
    [DataRow("candidate-installed-changed", "P4")]
    [DataRow("candidate-fingerprint-differs", "P4")]
    [DataRow("candidate-fingerprint-null", "P4")]
    [DataRow("installed-equal", "P2")]
    [DataRow("stage-d-installed-mismatch-candidate-differs", "P5")]
    [DataRow("machine-inputs-candidate-differs", "P5")]
    [DataRow("approval-expired-candidate-differs", "P5")]
    [DataRow("source-not-community-candidate-differs", "P5")]
    [DataRow("undetermined-candidate-differs", "P5")]
    public void EachOperationBoundViolationDiscardsTheResponseEvenWhenItSaysNotStarted(string name, string predicate)
    {
        ReviewedHelperFacts reviewed = name switch
        {
            "matched-installed" => Protocol2InterpreterFixture.Matched(installed: OtherInstalled),
            "matched-candidate" => Protocol2InterpreterFixture.Matched(candidate: OtherCandidate),
            "matched-fingerprint-recomputed" => Protocol2InterpreterFixture.Matched(fingerprint: OtherFingerprint),
            "profile-fingerprint-equals-F" => Refused(ReviewedHelperRefusalReason.ProfileMismatch, Installed, null, Fingerprint),
            "profile-installed-changed" => Refused(ReviewedHelperRefusalReason.ProfileMismatch, OtherInstalled, null, OtherFingerprint),
            "candidate-equal" => Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, Candidate, Fingerprint),
            "candidate-installed-changed" => Refused(ReviewedHelperRefusalReason.CandidateMismatch, OtherInstalled, OtherCandidate, Fingerprint),
            "candidate-fingerprint-differs" => Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, OtherCandidate, OtherFingerprint),
            "candidate-fingerprint-null" => Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, OtherCandidate, null),
            "installed-equal" => Refused(ReviewedHelperRefusalReason.InstalledMismatch, Installed, null, Fingerprint),
            "stage-d-installed-mismatch-candidate-differs" => Refused(ReviewedHelperRefusalReason.InstalledMismatch, OtherInstalled, OtherCandidate, Fingerprint),
            "machine-inputs-candidate-differs" => Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, Installed, OtherCandidate, Fingerprint),
            "approval-expired-candidate-differs" => Refused(ReviewedHelperRefusalReason.ApprovalExpired, Installed, OtherCandidate, Fingerprint),
            "source-not-community-candidate-differs" => Refused(ReviewedHelperRefusalReason.SourceNotCommunity, Installed, OtherCandidate, null),
            "undetermined-candidate-differs" => Protocol2InterpreterFixture.Undetermined(Installed, OtherCandidate, Fingerprint),
            _ => throw new AssertFailedException(name),
        };
        CollectionAssert.Contains(ReviewedUpdateProtocol2Interpreter.OperationBoundViolations(Installed, Candidate, Fingerprint, reviewed).ToArray(), predicate);
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        UpdateWireFacts notStarted = Protocol2InterpreterFixture.NotStarted(cancelled: reviewed.Qualification == ReviewedHelperQualification.Undetermined);
        Assert.IsEmpty(ReviewedUpdateWireCodec.StructuralViolations(notStarted, reviewed), name);
        Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation, Protocol2InterpreterFixture.Frame(operation, notStarted, reviewed)));
        if (reviewed.Qualification == ReviewedHelperQualification.Matched)
            Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation,
                Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), reviewed)));
    }

    [TestMethod]
    [DataRow("request-not-admitted")]
    [DataRow("machine-inputs-stage-d-candidate-retained")]
    [DataRow("machine-inputs-fault-settled")]
    [DataRow("source-not-community")]
    [DataRow("installed-mismatch-stage-c-value")]
    [DataRow("installed-mismatch-registration-vanished")]
    [DataRow("profile-mismatch-stage-d-drift")]
    [DataRow("preflight-query-failed")]
    [DataRow("preflight-query-failed-output-limit")]
    [DataRow("candidate-mismatch-stage-c")]
    [DataRow("candidate-mismatch-no-unique-candidate")]
    [DataRow("approval-expired")]
    public void SettledRefusalIsRefusedWithItsReasonAndReportedValues(string name)
    {
        (ReviewedHelperFacts reviewed, bool outputLimit) = name switch
        {
            "request-not-admitted" => (Refused(ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null), false),
            "machine-inputs-stage-d-candidate-retained" => (Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, Installed, Candidate, Fingerprint), false),
            "machine-inputs-fault-settled" => (Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, OtherInstalled, null, OtherFingerprint), false),
            "source-not-community" => (Refused(ReviewedHelperRefusalReason.SourceNotCommunity, Installed, null, null), false),
            // N3: a well-formed stage-C list result that disagrees with stage B carries the stage-C installed value.
            "installed-mismatch-stage-c-value" => (Refused(ReviewedHelperRefusalReason.InstalledMismatch, OtherInstalled, null, Fingerprint), false),
            "installed-mismatch-registration-vanished" => (Refused(ReviewedHelperRefusalReason.InstalledMismatch, null, null, null), false),
            "profile-mismatch-stage-d-drift" => (Refused(ReviewedHelperRefusalReason.ProfileMismatch, Installed, Candidate, OtherFingerprint), false),
            "preflight-query-failed" => (Refused(ReviewedHelperRefusalReason.PreflightQueryFailed, Installed, null, Fingerprint), false),
            "preflight-query-failed-output-limit" => (Refused(ReviewedHelperRefusalReason.PreflightQueryFailed, Installed, null, Fingerprint), true),
            // N1: stage-C installed (list recheck) and stage-B fingerprint satisfy P4, so the refusal settles, not Uncertain.
            "candidate-mismatch-stage-c" => (Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, OtherCandidate, Fingerprint), false),
            "candidate-mismatch-no-unique-candidate" => (Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, null, Fingerprint), false),
            "approval-expired" => (Refused(ReviewedHelperRefusalReason.ApprovalExpired, Installed, Candidate, Fingerprint), false),
            _ => throw new AssertFailedException(name),
        };
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.NotStarted(outputLimit: outputLimit), reviewed));
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, result.Observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, result.Observation.Status);
        Assert.IsFalse(result.Observation.ExecutionUncertain);
        Assert.IsFalse(result.Observation.RestartReported);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(result.Observation), name);
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Refused, result.CandidateCheck.Kind);
        Assert.AreEqual(reviewed.RefusalReason, result.CandidateCheck.RefusalReason);
        Assert.AreEqual(reviewed.ObservedInstalledVersion, result.CandidateCheck.ReportedInstalledVersion);
        Assert.AreEqual(reviewed.ObservedCandidateVersion, result.CandidateCheck.ReportedCandidateVersion);
        Assert.IsNull(result.CandidateCheck.LiveText);
    }

    [TestMethod]
    [DataRow("started", 0, false, false, false, ReviewedUpdateProviderStatus.Completed)]
    [DataRow("started", 0, true, false, false, ReviewedUpdateProviderStatus.Cancelled)]
    [DataRow("started", 1, true, false, false, ReviewedUpdateProviderStatus.Cancelled)]
    [DataRow("started", 0, true, true, false, ReviewedUpdateProviderStatus.TimedOut)]
    [DataRow("started", 0, true, true, true, ReviewedUpdateProviderStatus.Failed)]
    [DataRow("started", 0, false, false, true, ReviewedUpdateProviderStatus.Failed)]
    [DataRow("started", 1, false, false, false, ReviewedUpdateProviderStatus.ProviderFailed)]
    [DataRow("started", 3010, false, false, false, ReviewedUpdateProviderStatus.ProviderFailed)]
    [DataRow("started", 1641, false, false, false, ReviewedUpdateProviderStatus.ProviderFailed)]
    [DataRow("undetermined", null, true, true, false, ReviewedUpdateProviderStatus.TimedOut)]
    [DataRow("undetermined", null, false, true, false, ReviewedUpdateProviderStatus.TimedOut)]
    [DataRow("undetermined", null, true, false, false, ReviewedUpdateProviderStatus.Cancelled)]
    [DataRow("undetermined", null, true, true, true, ReviewedUpdateProviderStatus.Failed)]
    [DataRow("matched-not-started", null, true, false, false, ReviewedUpdateProviderStatus.Cancelled)]
    [DataRow("matched-not-started", null, true, true, false, ReviewedUpdateProviderStatus.TimedOut)]
    [DataRow("matched-not-started", null, false, false, false, ReviewedUpdateProviderStatus.Failed)]
    public void QualificationSelectsTheRowThenFlagPrecedenceSelectsTheStatus(string row, int? exit, bool cancelled, bool timedOut,
        bool outputLimit, ReviewedUpdateProviderStatus expected)
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        (UpdateWireFacts facts, ReviewedHelperFacts reviewed, ReviewedHelperCandidateCheckKind kind) = row switch
        {
            "started" => (Protocol2InterpreterFixture.Started(exit, cancelled, timedOut, outputLimit), Protocol2InterpreterFixture.Matched(),
                ReviewedHelperCandidateCheckKind.Confirmed),
            "undetermined" => (Protocol2InterpreterFixture.NotStarted(cancelled, timedOut, outputLimit), Protocol2InterpreterFixture.Undetermined(),
                ReviewedHelperCandidateCheckKind.Unconfirmed),
            _ => (Protocol2InterpreterFixture.NotStarted(cancelled, timedOut, outputLimit), Protocol2InterpreterFixture.Matched(),
                ReviewedHelperCandidateCheckKind.Confirmed),
        };
        ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation, Protocol2InterpreterFixture.Frame(operation, facts, reviewed));
        Assert.AreEqual(expected, result.Observation.Status);
        Assert.AreEqual(exit, result.Observation.ExitCode);
        Assert.IsFalse(result.Observation.RestartReported, "O4: this route never reports a restart, even for 3010/1641.");
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(result.Observation));
        Assert.AreEqual(kind, result.CandidateCheck.Kind);
    }

    [TestMethod]
    public void StageDBudgetLapseIsUndeterminedTimedOutNeverRefused()
    {
        // N4/N2: a budget lapse at stage D is Undetermined + timedOut. A Refused frame with an interrupt flag cannot be created and is rejected if forged.
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation lapse = Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.NotStarted(timedOut: true), Protocol2InterpreterFixture.Undetermined(Installed, Candidate, Fingerprint)));
        Assert.AreEqual(ReviewedUpdateProviderStatus.TimedOut, lapse.Observation.Status);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, lapse.Observation.Invocation);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(lapse.Observation));
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Unconfirmed, lapse.CandidateCheck.Kind);
        Assert.IsNull(lapse.CandidateCheck.ReportedCandidateVersion);

        ReviewedHelperFacts expired = Refused(ReviewedHelperRefusalReason.ApprovalExpired, Installed, Candidate, Fingerprint);
        foreach (string flag in new[] { "\"timedOut\":", "\"cancelled\":" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => Protocol2InterpreterFixture.Frame(operation,
                flag.Contains("timed", StringComparison.Ordinal) ? Protocol2InterpreterFixture.NotStarted(timedOut: true) : Protocol2InterpreterFixture.NotStarted(cancelled: true), expired));
            string forged = Encoding.UTF8.GetString(Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.NotStarted(), expired))
                .Replace(flag + "false", flag + "true", StringComparison.Ordinal);
            Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation, Encoding.UTF8.GetBytes(forged)));
        }
    }

    [TestMethod]
    public void MatchedNotStartedWithOutputLimitIsRejectedExplicitly()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        UpdateWireFacts facts = Protocol2InterpreterFixture.NotStarted(outputLimit: true);
        Assert.IsEmpty(ReviewedUpdateWireCodec.StructuralViolations(facts, Protocol2InterpreterFixture.Matched()));
        Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, facts, Protocol2InterpreterFixture.Matched())));
    }

    [TestMethod]
    [DataRow("helper-exit-unobserved")]
    [DataRow("transport-not-sealed")]
    [DataRow("desktop-termination-failed")]
    public void SettledResponseWithoutHIsRejected(string missing)
    {
        ReviewedHelperDesktopObservation desktop = missing switch
        {
            "helper-exit-unobserved" => Protocol2InterpreterFixture.SettledDesktop with { HelperExitObserved = false },
            "transport-not-sealed" => Protocol2InterpreterFixture.SettledDesktop with { TransportSealed = false },
            _ => Protocol2InterpreterFixture.SettledDesktop with { TerminationFailed = true },
        };
        Assert.IsFalse(desktop.H);
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed) in new[]
        {
            (Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched()),
            (Protocol2InterpreterFixture.NotStarted(), Refused(ReviewedHelperRefusalReason.CandidateMismatch, Installed, OtherCandidate, Fingerprint)),
        })
        {
            ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation, Protocol2InterpreterFixture.Frame(operation, facts, reviewed), desktop);
            Protocol2InterpreterFixture.AssertRejected(result);
            Assert.AreEqual(desktop.TerminationFailed, result.Observation.TerminationFailed);
        }
    }

    [TestMethod]
    public void DesktopOwnedIoPipeAndRootObservationsAreConjoinedWithTheWire()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] frame = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        TaskCompletionSource running = new();
        foreach (ReviewedHelperDesktopObservation desktop in new[]
        {
            Protocol2InterpreterFixture.SettledDesktop with { OwnedIoComplete = false },
            Protocol2InterpreterFixture.SettledDesktop with { PipeEndOfFile = false },
            Protocol2InterpreterFixture.SettledDesktop with { OwnedRootSnapshot = running.Task },
        })
        {
            ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation, frame, desktop);
            Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, result.Observation.Status);
            Assert.AreEqual(desktop.OwnedIoComplete, result.Observation.OwnedIoComplete);
            Assert.AreEqual(desktop.PipeEndOfFile, result.Observation.PipeEndOfFile);
            Assert.AreSame(desktop.OwnedRootSnapshot, result.Observation.OwnedRoot);
            Assert.IsFalse(Protocol2InterpreterFixture.IsSettled(result.Observation));
        }
        // A wire termination failure is carried even when the desktop saw none.
        UpdateWireFacts failedTermination = new(UpdateWireInvocation.Started, 0, true, true, true, true, false, false, false, false);
        ReviewedUpdateProtocol2Interpretation terminated = Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, failedTermination, Protocol2InterpreterFixture.Matched()));
        Assert.IsTrue(terminated.Observation.TerminationFailed);
        Assert.IsTrue(terminated.Observation.ExecutionUncertain);
    }

    [TestMethod]
    public void UnknownInvocationAndUnsettledFactsStayUnsettled()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateProtocol2Interpretation unknown = Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Unknown(), Protocol2InterpreterFixture.Matched()));
        Assert.AreEqual(ReviewedUpdateInvocation.Unknown, unknown.Observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Failed, unknown.Observation.Status);
        Assert.IsTrue(unknown.Observation.ExecutionUncertain);
        Assert.IsFalse(Protocol2InterpreterFixture.IsSettled(unknown.Observation));

        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed) in new[]
        {
            (Protocol2InterpreterFixture.NotStarted(settled: false), Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, null, null, null)),
            (Protocol2InterpreterFixture.NotStarted(settled: false), Protocol2InterpreterFixture.Undetermined()),
            (Protocol2InterpreterFixture.Started(exit: null), Protocol2InterpreterFixture.Matched()),
        })
        {
            ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation, Protocol2InterpreterFixture.Frame(operation, facts, reviewed));
            Assert.IsTrue(result.Observation.ExecutionUncertain);
            Assert.IsFalse(Protocol2InterpreterFixture.IsSettled(result.Observation));
        }
    }

    [TestMethod]
    public void PreRequestPhasesFollowTheThreePhaseRule()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        byte[] validFrame = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        ReviewedUpdateProtocol2Interpretation Run(ReviewedHelperTransportOutcome outcome, ReviewedHelperDesktopObservation desktop, bool withFrame = false) =>
            ReviewedUpdateProtocol2Interpreter.Interpret(operation, outcome, withFrame ? validFrame : [], Nonce, desktop);

        ReviewedUpdateProtocol2Interpretation launch = Run(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, Protocol2InterpreterFixture.SettledDesktop);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, launch.Observation.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Failed, launch.Observation.Status);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(launch.Observation));
        ReviewedUpdateProtocol2Interpretation uac = Run(ReviewedHelperTransportOutcome.ElevationDeclined, Protocol2InterpreterFixture.SettledDesktop);
        Assert.AreEqual(ReviewedUpdateProviderStatus.ElevationDenied, uac.Observation.Status);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(uac.Observation));
        ReviewedUpdateProtocol2Interpretation exited = Run(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, Protocol2InterpreterFixture.SettledDesktop);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, exited.Observation.Invocation);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(exited.Observation));
        foreach (ReviewedUpdateProtocol2Interpretation result in new[] { launch, uac, exited })
            Assert.AreSame(ReviewedHelperCandidateCheck.NotObserved, result.CandidateCheck);

        // Review 9 L1: a frame or unsettled desktop facts beside a pre-request row are inconsistent input and fail closed.
        foreach (ReviewedHelperTransportOutcome preRequest in new[] { ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation,
            ReviewedHelperTransportOutcome.ElevationDeclined, ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten })
        {
            Protocol2InterpreterFixture.AssertRejected(Run(preRequest, Protocol2InterpreterFixture.SettledDesktop, withFrame: true));
            Protocol2InterpreterFixture.AssertRejected(Run(preRequest, Protocol2InterpreterFixture.SettledDesktop with { TerminationFailed = true }));
            Protocol2InterpreterFixture.AssertRejected(Run(preRequest, Protocol2InterpreterFixture.SettledDesktop with { OwnedRootSnapshot = new TaskCompletionSource().Task }));
        }

        foreach (ReviewedHelperDesktopObservation desktop in new[]
        {
            Protocol2InterpreterFixture.SettledDesktop with { HelperExitObserved = false },
            Protocol2InterpreterFixture.SettledDesktop with { TerminationFailed = true },
            Protocol2InterpreterFixture.SettledDesktop with { OwnedRootSnapshot = new TaskCompletionSource().Task },
        })
        {
            ReviewedUpdateProtocol2Interpretation unobserved = Run(ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten, desktop);
            Assert.AreEqual(ReviewedUpdateInvocation.Unknown, unobserved.Observation.Invocation);
            Assert.IsTrue(unobserved.Observation.ExecutionUncertain);
            Assert.AreSame(ReviewedHelperCandidateCheck.NotObserved, unobserved.CandidateCheck);
        }
        // After the request is written, a missing or invalid response discards everything, even if a valid frame is passed alongside.
        Protocol2InterpreterFixture.AssertRejected(Run(ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse, Protocol2InterpreterFixture.SettledDesktop, withFrame: true));
        Protocol2InterpreterFixture.AssertRejected(Run((ReviewedHelperTransportOutcome)99, Protocol2InterpreterFixture.SettledDesktop, withFrame: true));
    }

    [TestMethod]
    [DataRow("wrong-nonce")]
    [DataRow("wrong-job")]
    [DataRow("mutated-operation-recomputed-digest")]
    [DataRow("non-canonical-diagnostic")]
    [DataRow("escaped-qualification")]
    [DataRow("trailing-byte")]
    [DataRow("legacy-fifteen-field")]
    [DataRow("empty")]
    public void UncorrelatedOrNonCanonicalFramesAreRejected(string change)
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        ReviewedUpdateOperation mutated = Protocol2InterpreterFixture.Operation(candidate: OtherCandidate);
        ReviewedUpdateOperation otherJob = Protocol2InterpreterFixture.Operation(jobId: Guid.Parse("71000000-0000-0000-0000-000000000009"));
        UpdateWireFacts facts = Protocol2InterpreterFixture.Started();
        ReviewedHelperFacts matched = Protocol2InterpreterFixture.Matched();
        byte[] valid = Protocol2InterpreterFixture.Frame(operation, facts, matched);
        string text = Encoding.UTF8.GetString(valid);
        byte[] frame = change switch
        {
            "wrong-nonce" => ReviewedUpdateWireCodec.CreateResponse(operation.JobId, operation.Digest, new string('E', 64), facts, matched, "helper ok"),
            "wrong-job" => ReviewedUpdateWireCodec.CreateResponse(otherJob.JobId, operation.Digest, Nonce, facts, matched, "helper ok"),
            // The helper hashed a different (mutated) canonical payload: its recomputed digest never equals the frozen one.
            "mutated-operation-recomputed-digest" => Protocol2InterpreterFixture.Frame(mutated, facts,
                Protocol2InterpreterFixture.Matched(candidate: OtherCandidate)),
            "non-canonical-diagnostic" => Encoding.UTF8.GetBytes(text.Replace("\"diagnostic\":\"helper ok\"", "\"diagnostic\":\"helper ok \"", StringComparison.Ordinal)),
            "escaped-qualification" => Encoding.UTF8.GetBytes(text.Replace("\"Matched\"", "\"\\u004datched\"", StringComparison.Ordinal)),
            "trailing-byte" => [.. valid, (byte)' '],
            "legacy-fifteen-field" => Encoding.UTF8.GetBytes(text[..text.IndexOf(",\"qualification\"", StringComparison.Ordinal)] + ",\"diagnostic\":\"helper ok\"}"),
            _ => [],
        };
        Assert.AreNotEqual(operation.Digest, mutated.Digest);
        Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation, frame));
        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, Protocol2InterpreterFixture.Interpret(operation, valid).Observation.Status);
    }

    [TestMethod]
    [DataRow("winget-route")]
    [DataRow("provisional-route")]
    [DataRow("evidence-without-profile-prefix")]
    [DataRow("evidence-lowercase-digest")]
    [DataRow("evidence-short-digest")]
    public void OperationsOutsideTheSingleRouteOrWithoutProfileDigestAreRejected(string change)
    {
        ReviewedUpdateOperation operation = change switch
        {
            "winget-route" => WinGetOperation(),
            "provisional-route" => Protocol2InterpreterFixture.Operation(route: UpdateRouteContract.ChocolateyMachineProvisionalV3),
            "evidence-without-profile-prefix" => Protocol2InterpreterFixture.Operation(evidenceId: "evidence:" + Fingerprint),
            "evidence-lowercase-digest" => Protocol2InterpreterFixture.Operation(evidenceId: "choco-profile-v2:" + Fingerprint.ToLowerInvariant()),
            _ => Protocol2InterpreterFixture.Operation(evidenceId: "choco-profile-v2:" + Fingerprint[..63]),
        };
        Assert.IsFalse(ReviewedUpdateProtocol2Interpreter.TryGetProfileDigest(operation, out _));
        byte[] frame = Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched());
        Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation, frame));
        Protocol2InterpreterFixture.AssertRejected(ReviewedUpdateProtocol2Interpreter.Interpret(operation,
            ReviewedHelperTransportOutcome.ElevationDeclined, [], Nonce, Protocol2InterpreterFixture.SettledDesktop));
        Assert.IsTrue(ReviewedUpdateProtocol2Interpreter.TryGetProfileDigest(Protocol2InterpreterFixture.Operation(), out string digest));
        Assert.AreEqual(Fingerprint, digest);
    }

    [TestMethod]
    public void DiagnosticTextNeverProducesConfirmation()
    {
        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
        string phrase = ReviewedHelperCandidateCheck.ConfirmedText;
        StringAssert.Contains(phrase, "matched its protected machine checks");
        foreach ((UpdateWireFacts facts, ReviewedHelperFacts reviewed, ReviewedHelperCandidateCheckKind kind) in new[]
        {
            (Protocol2InterpreterFixture.NotStarted(cancelled: true), Protocol2InterpreterFixture.Undetermined(), ReviewedHelperCandidateCheckKind.Unconfirmed),
            (Protocol2InterpreterFixture.NotStarted(), Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, null, null, null), ReviewedHelperCandidateCheckKind.Refused),
        })
        {
            ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation, Protocol2InterpreterFixture.Frame(operation, facts, reviewed, phrase));
            StringAssert.Contains(result.Observation.Diagnostic, "matched its protected machine checks");
            Assert.AreEqual(kind, result.CandidateCheck.Kind);
            Assert.IsNull(result.CandidateCheck.LiveText);
        }
        // A rejected Matched frame carrying the phrase is not confirmed either.
        ReviewedUpdateProtocol2Interpretation rejected = Protocol2InterpreterFixture.Interpret(operation,
            Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched(fingerprint: OtherFingerprint), phrase));
        Protocol2InterpreterFixture.AssertRejected(rejected);
    }

    [TestMethod]
    public void CandidateCheckIsClosedAndNeverConvertibleToProvenance()
    {
        Type type = typeof(ReviewedHelperCandidateCheck);
        Assert.IsTrue(type.IsSealed);
        Assert.IsTrue(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(c => c.IsPrivate));
        MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.IsFalse(methods.Any(m => m.Name is "op_Implicit" or "op_Explicit"));
        Assert.IsFalse(methods.Any(m => m.ReturnType == typeof(ReviewedCandidateProvenance) ||
            m.GetParameters().Any(p => p.ParameterType == typeof(ReviewedCandidateProvenance))));
        Assert.IsFalse(type.GetProperties(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(p => p.PropertyType == typeof(ReviewedCandidateProvenance)));
        Assert.IsFalse(typeof(ReviewedHelperCandidateCheckKind).GetMethods().Any(m => m.ReturnType == typeof(ReviewedCandidateProvenance)));
        CollectionAssert.AreEqual(CheckKindNames, Enum.GetNames<ReviewedHelperCandidateCheckKind>());
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.NotObserved, ReviewedHelperCandidateCheck.NotObserved.Kind);
        Assert.IsNull(ReviewedHelperCandidateCheck.NotObserved.LiveText);
    }

    private static ReviewedHelperFacts Refused(ReviewedHelperRefusalReason reason, string? installed, string? candidate, string? fingerprint) =>
        Protocol2InterpreterFixture.Refused(reason, installed, candidate, fingerprint);

    private static ReviewedUpdateOperation WinGetOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet,
            "Contoso.Widget", Installed, Candidate, UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1"))
            .Confirm(Protocol2InterpreterFixture.ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "native:1", "context:1", "choco-profile-v2:" + Fingerprint,
            Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }
}
