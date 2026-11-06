using System.Security.Cryptography;
using System.Text;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>
/// Closed transport phase fact for one protocol-2 helper exchange, reported by the future transport after the one-use
/// permit was consumed (Task 50 reviewed fact extension rev 3 §4.1). <see cref="ResponseFrameReceived"/> means exactly one
/// authenticated frame was read, then pipe EOF, within the execution budget; every other post-request failure (invalid,
/// lost, late, uncorrelated, trailing bytes or a second frame) is <see cref="RequestWrittenNoValidResponse"/>.
/// </summary>
internal enum ReviewedHelperTransportOutcome
{
    LaunchRefusedBeforeElevation, ElevationDeclined, HelperStartedRequestNotWritten, RequestWrittenNoValidResponse, ResponseFrameReceived
}

/// <summary>
/// Desktop-observed facts only (never wire data). <see cref="H"/> is §4.1 H: the helper exit was observed within the
/// existing exit timeout and the transport was sealed without a termination failure.
/// </summary>
internal sealed record ReviewedHelperDesktopObservation(Task OwnedRootSnapshot, bool HelperExitObserved, bool TransportSealed,
    bool TerminationFailed, bool OwnedIoComplete, bool PipeEndOfFile)
{
    internal bool H => HelperExitObserved && TransportSealed && !TerminationFailed;
}

/// <summary>§4.1 candidate-check kinds: Matched → Confirmed, Refused → Refused, Undetermined → Unconfirmed.</summary>
internal enum ReviewedHelperCandidateCheckKind { NotObserved, Confirmed, Refused, Unconfirmed }

/// <summary>
/// Desktop-derived candidate check (§4.1, §4.2) for later history persistence and live display. Only
/// <see cref="ReviewedUpdateProtocol2Interpreter"/> produces a value other than <see cref="NotObserved"/> (architecture
/// test). It never changes status, settlement or outcome and is never convertible to or from
/// <see cref="ReviewedCandidateProvenance"/>; the frozen operation remains UserCacheIntent.
/// </summary>
internal sealed class ReviewedHelperCandidateCheck
{
    /// <summary>The only live text (§4.1), for <see cref="ReviewedHelperCandidateCheckKind.Confirmed"/> only; never worded as success.</summary>
    internal const string ConfirmedText =
        "Reported by PC Steward's administrator helper: the exact reviewed version matched its protected machine checks.";

    private ReviewedHelperCandidateCheck(ReviewedHelperCandidateCheckKind kind, ReviewedHelperRefusalReason reason,
        string? reportedInstalledVersion, string? reportedCandidateVersion)
    {
        Kind = kind;
        RefusalReason = reason;
        ReportedInstalledVersion = reportedInstalledVersion;
        ReportedCandidateVersion = reportedCandidateVersion;
    }

    internal static ReviewedHelperCandidateCheck NotObserved { get; } =
        new(ReviewedHelperCandidateCheckKind.NotObserved, ReviewedHelperRefusalReason.None, null, null);

    internal ReviewedHelperCandidateCheckKind Kind { get; }
    /// <summary><c>None</c> unless <see cref="Kind"/> is Refused.</summary>
    internal ReviewedHelperRefusalReason RefusalReason { get; }
    /// <summary>Field 16 from an accepted frame, for Confirmed/Refused only; display as "reported by the helper", never as fact.</summary>
    internal string? ReportedInstalledVersion { get; }
    /// <summary>Field 17 from an accepted frame, for Confirmed/Refused only; display as "reported by the helper", never as fact.</summary>
    internal string? ReportedCandidateVersion { get; }
    internal string? LiveText => Kind == ReviewedHelperCandidateCheckKind.Confirmed ? ConfirmedText : null;

    /// <summary>Single factory. Call only from the interpreter, after authentication, correlation, S1–S13, P1–P6 and H.</summary>
    internal static ReviewedHelperCandidateCheck FromAcceptedFacts(ReviewedHelperFacts accepted) => accepted.Qualification switch
    {
        ReviewedHelperQualification.Matched => new(ReviewedHelperCandidateCheckKind.Confirmed, ReviewedHelperRefusalReason.None,
            accepted.ObservedInstalledVersion, accepted.ObservedCandidateVersion),
        ReviewedHelperQualification.Refused => new(ReviewedHelperCandidateCheckKind.Refused, accepted.RefusalReason,
            accepted.ObservedInstalledVersion, accepted.ObservedCandidateVersion),
        ReviewedHelperQualification.Undetermined => new(ReviewedHelperCandidateCheckKind.Unconfirmed, ReviewedHelperRefusalReason.None, null, null),
        _ => NotObserved,
    };

    /// <summary>
    /// The single adapter function to the Application history type (adapter gates §B.2): explicit exhaustive mapping by
    /// name (no numeric cast) and fields 16/17 copied verbatim. The only <c>FromDesktopDerived</c> call site in <c>src</c>.
    /// </summary>
    internal RecordedHelperCandidateCheck ToRecorded() => RecordedHelperCandidateCheck.FromDesktopDerived(
        Kind switch
        {
            ReviewedHelperCandidateCheckKind.NotObserved => RecordedHelperCheckKind.NotObserved,
            ReviewedHelperCandidateCheckKind.Confirmed => RecordedHelperCheckKind.Confirmed,
            ReviewedHelperCandidateCheckKind.Refused => RecordedHelperCheckKind.Refused,
            ReviewedHelperCandidateCheckKind.Unconfirmed => RecordedHelperCheckKind.Unconfirmed,
            _ => throw new InvalidOperationException("Undefined candidate-check kind."),
        },
        RefusalReason switch
        {
            ReviewedHelperRefusalReason.None => RecordedHelperRefusalReason.None,
            ReviewedHelperRefusalReason.RequestNotAdmitted => RecordedHelperRefusalReason.RequestNotAdmitted,
            ReviewedHelperRefusalReason.MachineInputsUnsupported => RecordedHelperRefusalReason.MachineInputsUnsupported,
            ReviewedHelperRefusalReason.SourceNotCommunity => RecordedHelperRefusalReason.SourceNotCommunity,
            ReviewedHelperRefusalReason.InstalledMismatch => RecordedHelperRefusalReason.InstalledMismatch,
            ReviewedHelperRefusalReason.ProfileMismatch => RecordedHelperRefusalReason.ProfileMismatch,
            ReviewedHelperRefusalReason.PreflightQueryFailed => RecordedHelperRefusalReason.PreflightQueryFailed,
            ReviewedHelperRefusalReason.CandidateMismatch => RecordedHelperRefusalReason.CandidateMismatch,
            ReviewedHelperRefusalReason.ApprovalExpired => RecordedHelperRefusalReason.ApprovalExpired,
            _ => throw new InvalidOperationException("Undefined refusal reason."),
        },
        ReportedInstalledVersion, ReportedCandidateVersion);
}

/// <summary>Interpreter result: the native observation for the workflow plus the desktop-derived candidate check.</summary>
internal sealed record ReviewedUpdateProtocol2Interpretation(ReviewedUpdateDispatchObservation Observation, ReviewedHelperCandidateCheck CandidateCheck)
{
    /// <summary>
    /// What the future route dispatcher returns to the workflow: <see cref="Observation"/> with the appended, history-only
    /// helper check (§4.1, adapter gates §B.2). Every other member is unchanged, so the outcome cannot depend on the check.
    /// </summary>
    internal ReviewedUpdateDispatchObservation WorkflowObservation => Observation with { HelperCheck = CandidateCheck.ToRecorded() };
}

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13: pure desktop interpretation of one protocol-2 helper exchange for the single
/// route schema 3 <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/> (reviewed fact extension rev 3
/// §3.3, §4.1, §10 N1–N4). Passive: no transport, endpoint or dispatcher uses it yet. A wire label or Boolean never
/// qualifies anything alone: any correlation, structural (S1–S13), operation-bound (P1–P6) or H failure discards every
/// wire fact and yields an Unknown, unsettled observation, which the workflow records as Uncertain.
/// </summary>
internal static class ReviewedUpdateProtocol2Interpreter
{
    internal const string ResponseRejectedDiagnostic = "helper-response-rejected";
    internal const string LaunchRefusedDiagnostic = "helper-launch-refused-before-elevation";
    internal const string ElevationDeclinedDiagnostic = "helper-elevation-declined";
    internal const string RequestNotWrittenDiagnostic = "helper-request-not-written";
    internal const string RequestNotWrittenUnsettledDiagnostic = "helper-request-not-written; helper-exit-unobserved";
    private static readonly Task Pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

    internal static ReviewedUpdateProtocol2Interpretation Interpret(ReviewedUpdateOperation operation, ReviewedHelperTransportOutcome outcome,
        ReadOnlySpan<byte> responseFrame, string requestNonce, ReviewedHelperDesktopObservation desktop)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(desktop);
        // ADR-031 design §8: the frozen route selects the reply shape; the client self-update requires clientRunFault (S14).
        if (!TryGetRouteProfileDigest(operation, out string profileDigest, out ReviewedHelperReplyShape shape)) return Rejected(desktop);
        // Pre-request rows (review 9 L1): an accompanying frame or unsettled desktop facts are inconsistent transport input and
        // fail closed instead of settling as NotStarted.
        bool quietPreRequest = responseFrame.IsEmpty && !desktop.TerminationFailed && desktop.OwnedRootSnapshot is { IsCompletedSuccessfully: true };
        switch (outcome)
        {
            case ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation or ReviewedHelperTransportOutcome.ElevationDeclined
                or ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten when !quietPreRequest:
                return Rejected(desktop);
            case ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation:
                return new(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Failed, LaunchRefusedDiagnostic),
                    ReviewedHelperCandidateCheck.NotObserved);
            case ReviewedHelperTransportOutcome.ElevationDeclined:
                return new(ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.ElevationDenied, ElevationDeclinedDiagnostic),
                    ReviewedHelperCandidateCheck.NotObserved);
            case ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten:
                // NotStarted only with an observed helper exit; otherwise Uncertain (§4.1).
                return new(desktop.H && desktop.OwnedRootSnapshot is { IsCompletedSuccessfully: true }
                    ? ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Failed, RequestNotWrittenDiagnostic)
                    : Unsettled(desktop, RequestNotWrittenUnsettledDiagnostic), ReviewedHelperCandidateCheck.NotObserved);
            case ReviewedHelperTransportOutcome.ResponseFrameReceived:
                break;
            default:
                return Rejected(desktop);
        }

        if (!desktop.H ||
            !ReviewedUpdateWireCodec.TryReadResponse(responseFrame, operation.JobId, operation.Digest, requestNonce, shape, out FramedUpdateResponse? response) ||
            response is null || ReviewedUpdateWireCodec.StructuralViolations(response.Facts, response.Reviewed).Count != 0 ||
            (shape == ReviewedHelperReplyShape.ClientSelfUpdate) != response.ClientRunFault.HasValue ||
            (response.ClientRunFault is { } run && ReviewedUpdateWireCodec.ClientRunFaultViolations(response.Facts, response.Reviewed, run).Count != 0) ||
            OperationBoundViolations(operation.Selection.InstalledVersion, operation.Selection.CandidateVersion, profileDigest, response.Reviewed).Count != 0 ||
            IsImpossibleLaunchFailure(response))
            return Rejected(desktop);

        UpdateWireFacts wire = response.Facts;
        bool h = desktop.H;
        ReviewedUpdateDispatchObservation observation = new(
            Invocation(wire.Invocation),
            SelectStatus(wire, response.Reviewed.Qualification),
            wire.ExitCode,
            response.Diagnostic,
            h ? desktop.OwnedRootSnapshot : Pending,
            DirectExitObserved: wire.DirectProcessExitObserved && h,
            OwnedIoComplete: wire.OwnedIoCompleted && desktop.OwnedIoComplete,
            PipeEndOfFile: wire.PipeEofObserved && desktop.PipeEndOfFile,
            TerminationFailed: wire.AggregateTerminationFailed || desktop.TerminationFailed,
            ExecutionUncertain: !wire.AggregateSettlementConfirmed || !h || wire.Invocation == UpdateWireInvocation.Unknown,
            RestartReported: false,
            ClientRunFault: response.ClientRunFault is { } accepted ? ToRunFault(accepted) : null);
        // Review M1: the recorded helper check is defined for the package route only (RecordedHelperCandidateCheck.IsConsistentWith),
        // so a client self-update records NotObserved instead of a check the workflow would drop with a false inconsistency marker.
        // History-only either way; never authority.
        return new(observation, operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1
            ? ReviewedHelperCandidateCheck.NotObserved
            : ReviewedHelperCandidateCheck.FromAcceptedFacts(response.Reviewed));
    }

    /// <summary>
    /// Returns the identifier of every failed operation-bound predicate P1–P5 (§3.3) for structurally valid facts; P6
    /// reasons make no claim beyond P5. Versions compare ordinally; the fingerprint compares in fixed time with F.
    /// </summary>
    internal static IReadOnlyList<string> OperationBoundViolations(string frozenInstalled, string frozenCandidate, string profileDigest,
        ReviewedHelperFacts reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        bool installedEqual = reviewed.ObservedInstalledVersion is { } installed && string.Equals(installed, frozenInstalled, StringComparison.Ordinal);
        bool candidateEqual = reviewed.ObservedCandidateVersion is { } candidate && string.Equals(candidate, frozenCandidate, StringComparison.Ordinal);
        bool fingerprintPresent = reviewed.ObservedProfileFingerprint is not null;
        bool fingerprintEqual = fingerprintPresent && FixedEquals(reviewed.ObservedProfileFingerprint!, profileDigest);
        ReviewedHelperRefusalReason reason = reviewed.RefusalReason;
        List<string> violations = [];
        void Require(string id, bool holds)
        {
            if (!holds) violations.Add(id);
        }
        switch (reviewed.Qualification)
        {
            case ReviewedHelperQualification.Matched:
                Require("P1", installedEqual && candidateEqual && fingerprintEqual);
                break;
            case ReviewedHelperQualification.Refused:
                if (reason == ReviewedHelperRefusalReason.InstalledMismatch) Require("P2", !installedEqual);
                else if (reason == ReviewedHelperRefusalReason.ProfileMismatch) Require("P3", installedEqual && fingerprintPresent && !fingerprintEqual);
                else if (reason == ReviewedHelperRefusalReason.CandidateMismatch) Require("P4", installedEqual && fingerprintEqual && !candidateEqual);
                else Require("P6", reason is ReviewedHelperRefusalReason.RequestNotAdmitted or ReviewedHelperRefusalReason.MachineInputsUnsupported or
                    ReviewedHelperRefusalReason.SourceNotCommunity or ReviewedHelperRefusalReason.PreflightQueryFailed or ReviewedHelperRefusalReason.ApprovalExpired);
                break;
            case ReviewedHelperQualification.Undetermined:
                break;
            default:
                violations.Add("Qualification");
                break;
        }
        Require("P5", reviewed.ObservedCandidateVersion is null || reason == ReviewedHelperRefusalReason.CandidateMismatch || candidateEqual);
        return violations;
    }

    /// <summary>
    /// Route shape (same fixed predicates as the Chocolatey argument mapping, minus native checks the helper owns) and
    /// O2: <c>EvidenceId = "choco-profile-v2:" + F</c> with F exactly 64 uppercase hex. Anything else is unsupported.
    /// </summary>
    internal static bool TryGetProfileDigest(ReviewedUpdateOperation operation, out string profileDigest)
    {
        ArgumentNullException.ThrowIfNull(operation);
        profileDigest = string.Empty;
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 ||
            operation.ProviderGuardContract != ReviewedProviderGuardContract.CommunityScriptRuntimeLimits ||
            operation.CandidateProvenance != ReviewedCandidateProvenance.UserCacheIntent ||
            selection.Provider != UpdateProvider.Chocolatey || selection.Source != UpdateSource.ChocolateyCommunity ||
            selection.Scope != UpdateScope.Machine || selection.Channel != UpdateChannel.Stable ||
            selection.Architecture.Binding != ReviewedArchitectureBinding.ProviderScriptSelected ||
            operation.Mode != ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) ||
            !operation.EvidenceId.StartsWith(ReviewedUpdateOperation.ProfileEvidencePrefix, StringComparison.Ordinal)) return false;
        // Defence in depth: the Domain constructor already enforces this form for the route (adapter gates §A.6).
        string digest = operation.EvidenceId[ReviewedUpdateOperation.ProfileEvidencePrefix.Length..];
        if (!IsUpperHex64(digest)) return false;
        profileDigest = digest;
        return true;
    }

    /// <summary>
    /// ADR-031 design §8.3: the only <c>src</c> call site of <see cref="ReviewedClientRunFault.FromAcceptedHelperReply"/>
    /// (architecture test), reached only for an accepted client self-update reply. Explicit mapping of each table row.
    /// </summary>
    private static ReviewedClientRunFault ToRunFault(ReviewedClientRunFaultWire accepted) => accepted switch
    {
        ReviewedClientRunFaultWire.None => ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.None, false),
        ReviewedClientRunFaultWire.SetupOverrideDuringRun => ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.SetupOverrideDuringRun, false),
        ReviewedClientRunFaultWire.RootWritableDuringRun => ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.RootWritableDuringRun, false),
        ReviewedClientRunFaultWire.WatchFailedDuringRun => ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.WatchFailedDuringRun, false),
        ReviewedClientRunFaultWire.ReleaseIncomplete => ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.None, true),
        ReviewedClientRunFaultWire.SetupOverrideDuringRunAndReleaseIncomplete =>
            ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.SetupOverrideDuringRun, true),
        ReviewedClientRunFaultWire.RootWritableDuringRunAndReleaseIncomplete =>
            ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.RootWritableDuringRun, true),
        ReviewedClientRunFaultWire.WatchFailedDuringRunAndReleaseIncomplete =>
            ReviewedClientRunFault.FromAcceptedHelperReply(ReviewedClientWatchdogStop.WatchFailedDuringRun, true),
        _ => throw new InvalidOperationException("Not a row of the clientRunFault table."),
    };

    /// <summary>
    /// The route-discriminated form of <see cref="TryGetProfileDigest"/> (ADR-031 design §7.1, §8.2): the package route gives
    /// the package reply shape, the exact client self-update shape (<see cref="TryGetClientProfileDigest"/>) the client shape.
    /// Anything else is unsupported.
    /// </summary>
    internal static bool TryGetRouteProfileDigest(ReviewedUpdateOperation operation, out string profileDigest, out ReviewedHelperReplyShape shape)
    {
        shape = ReviewedHelperReplyShape.Package;
        if (TryGetProfileDigest(operation, out profileDigest)) return true;
        shape = ReviewedHelperReplyShape.ClientSelfUpdate;
        if (TryGetClientProfileDigest(operation, out profileDigest)) return true;
        shape = ReviewedHelperReplyShape.Package;
        return false;
    }

    /// <summary>
    /// ADR-031 design §7.1 A1 client arm: schema 3 <see cref="UpdateRouteContract.ChocolateyClientSelfUpdateV1"/> with guard
    /// <see cref="ReviewedProviderGuardContract.ChocolateyClientSelfUpdate"/>, provenance
    /// <see cref="ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired"/>, Chocolatey/ChocolateyCommunity/Machine/Stable,
    /// provider-script architecture and mode, package ID exactly <c>chocolatey</c>, and <c>EvidenceId = "choco-profile-v2:" + F</c>.
    /// Never accepts a mixed route/guard/provenance. Shape only: it admits nothing by itself (admission is the review gate's
    /// <c>HasSupportedMode</c> and the capability allow-list, Task 50 activation).
    /// </summary>
    internal static bool TryGetClientProfileDigest(ReviewedUpdateOperation operation, out string profileDigest)
    {
        ArgumentNullException.ThrowIfNull(operation);
        profileDigest = string.Empty;
        ReviewedUpdateSelection selection = operation.Selection;
        if (!operation.HasCurrentSchema || operation.RouteContract != UpdateRouteContract.ChocolateyClientSelfUpdateV1 ||
            operation.ProviderGuardContract != ReviewedProviderGuardContract.ChocolateyClientSelfUpdate ||
            operation.CandidateProvenance != ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired ||
            selection.Provider != UpdateProvider.Chocolatey || selection.Source != UpdateSource.ChocolateyCommunity ||
            selection.Scope != UpdateScope.Machine || selection.Channel != UpdateChannel.Stable ||
            selection.Architecture.Binding != ReviewedArchitectureBinding.ProviderScriptSelected ||
            operation.Mode != ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled) ||
            !string.Equals(selection.PackageId, ReviewedUpdateOperation.ChocolateyClientPackageId, StringComparison.Ordinal) ||
            !operation.EvidenceId.StartsWith(ReviewedUpdateOperation.ProfileEvidencePrefix, StringComparison.Ordinal)) return false;
        string digest = operation.EvidenceId[ReviewedUpdateOperation.ProfileEvidencePrefix.Length..];
        if (!IsUpperHex64(digest)) return false;
        profileDigest = digest;
        return true;
    }

    // §4.1 status table: qualification (with invocation) selects the row first; flag precedence
    // outputLimitExceeded > timedOut > cancelled > exit code applies only inside Undetermined, Matched ∧ NotStarted and Started.
    private static ReviewedUpdateProviderStatus SelectStatus(UpdateWireFacts wire, ReviewedHelperQualification qualification)
    {
        if (qualification == ReviewedHelperQualification.Refused) return ReviewedUpdateProviderStatus.Refused;
        if (wire.Invocation == UpdateWireInvocation.Unknown) return ReviewedUpdateProviderStatus.Failed;
        if (wire.OutputLimitExceeded) return ReviewedUpdateProviderStatus.Failed;
        if (wire.TimedOut) return ReviewedUpdateProviderStatus.TimedOut;
        if (wire.Cancelled) return ReviewedUpdateProviderStatus.Cancelled;
        if (wire.Invocation != UpdateWireInvocation.Started) return ReviewedUpdateProviderStatus.Failed;
        return wire.ExitCode switch
        {
            0 => ReviewedUpdateProviderStatus.Completed,
            int => ReviewedUpdateProviderStatus.ProviderFailed,
            null => ReviewedUpdateProviderStatus.Failed,
        };
    }

    // §10 nit: Matched ∧ NotStarted with outputLimitExceeded is impossible in practice; reject it explicitly.
    private static bool IsImpossibleLaunchFailure(FramedUpdateResponse response) =>
        response.Reviewed.Qualification == ReviewedHelperQualification.Matched &&
        response.Facts.Invocation == UpdateWireInvocation.NotStarted && response.Facts.OutputLimitExceeded;

    private static ReviewedUpdateInvocation Invocation(UpdateWireInvocation invocation) => invocation switch
    {
        UpdateWireInvocation.NotStarted => ReviewedUpdateInvocation.NotStarted,
        UpdateWireInvocation.Started => ReviewedUpdateInvocation.Started,
        _ => ReviewedUpdateInvocation.Unknown,
    };

    // §4.1 post-request row: discard every wire fact; Unknown invocation is always unsettled, so the workflow records Uncertain.
    private static ReviewedUpdateProtocol2Interpretation Rejected(ReviewedHelperDesktopObservation desktop) =>
        new(Unsettled(desktop, ResponseRejectedDiagnostic), ReviewedHelperCandidateCheck.NotObserved);

    private static ReviewedUpdateDispatchObservation Unsettled(ReviewedHelperDesktopObservation desktop, string diagnostic) =>
        new(ReviewedUpdateInvocation.Unknown, ReviewedUpdateProviderStatus.Failed, null, diagnostic, desktop.OwnedRootSnapshot ?? Pending,
            false, false, false, desktop.TerminationFailed, ExecutionUncertain: true, RestartReported: false);

    private static bool IsUpperHex64(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool FixedEquals(string value, string expected) => IsUpperHex64(value) && IsUpperHex64(expected) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value), Encoding.ASCII.GetBytes(expected));
}
