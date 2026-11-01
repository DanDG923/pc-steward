using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Execution;

/// <summary>
/// The helper-side G1 seam (Task 50 design rev 6 §5.2 A3, §6): the pure <see cref="DeploymentCapabilityPolicy"/> evaluated
/// over inputs the helper reads itself, with the item's requester check as the protection principal, plus
/// the helper's own image hash and the peer desktop image hash at the given image path (read from the held
/// peer handle) equal to their ledger entries. The Helper-role reader is N4b; any exception means unsupported.
/// </summary>
internal interface IReviewedHelperCapabilityCheck
{
    HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route, DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath);
}

/// <summary>
/// Endpoint budgets (design rev 6 §4, fact extension rev 3 §6). <see cref="Execution"/> is the desktop's 11-minute execution
/// budget; <c>remaining = Execution − (now − t0) − ExitMargin − ResponseMargin</c> with t0 taken at pipe connection.
/// <see cref="LaunchFloor"/> is the Launch-row minimum before process creation. <see cref="FrameRead"/> bounds the A0 read
/// and <see cref="ResponseWrite"/> the single response write and the monitor stop.
/// </summary>
internal sealed record ReviewedHelperEndpointBudgets(TimeSpan FrameRead, TimeSpan Execution, TimeSpan ExitMargin, TimeSpan ResponseMargin,
    TimeSpan LaunchFloor, TimeSpan ResponseWrite)
{
    internal static ReviewedHelperEndpointBudgets Production { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(11),
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
}

/// <summary>What the endpoint did with the pipe. Only <see cref="ResponseWritten"/> means one frame was written.</summary>
internal enum ReviewedHelperEndpointExit { ResponseWritten, NoResponse, ResponseWriteFailed }

/// <summary>The endpoint's own result: the exit kind and a fixed diagnostic token (never wire data).</summary>
internal sealed record ReviewedHelperEndpointResult(ReviewedHelperEndpointExit Exit, string Diagnostic);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §4 (deadlines), §5.2 (stage machine), §5.3, §5.4 (rev-5 F2) and
/// fact extension rev 3 §3.1, §3.2, §6 and §10 N1–N4: the protocol-2 helper stage machine over an already connected,
/// authenticated pipe and the qualification port. It handles values and opaque interfaces only.
/// <list type="bullet">
/// <item><b>A0</b> one frame within <see cref="ReviewedHelperEndpointBudgets.FrameRead"/>, exact request codec, received
/// payload hash equal to the digest, <c>TryRestore</c>, fixed-time nonce: any failure sends <b>no response</b>.</item>
/// <item><b>A1</b> request at most 2 minutes old and not from the future, review at most 30 minutes old, exact route shape and
/// O2 profile digest, payload SID equal to the held peer's SID; <b>A2</b> <c>BeginItem</c> and peer not exited; <b>A3</b>
/// G1 through <see cref="IReviewedHelperCapabilityCheck"/>: any failure is <c>Refused/RequestNotAdmitted</c>, fields 16–18
/// null.</item>
/// <item><b>B</b>, <b>C</b>, <b>D</b> and <b>Launch</b> per the §5.2 rows, with the N2 internal-fault rules: before process
/// creation a fault is <c>MachineInputsUnsupported</c> only when every owned launch job is settled and no interrupt is
/// present, otherwise <c>Undetermined</c>; from entering process creation any fault is <c>Unknown</c>. An out-of-order item
/// value maps per stage (rev-5 F2) to <c>MachineInputsUnsupported</c> only when <c>AggregateSettled</c>.</item>
/// <item>A refusal is reported only with every owned job settled; a settled-but-unprovable refusal becomes
/// <c>Undetermined</c> with <c>¬aggregateSettlementConfirmed</c>. The candidate is nulled whenever installed is null (S7).</item>
/// </list>
/// The response is built only through <see cref="ReviewedUpdateWireCodec.CreateResponse"/>; if it throws, nothing is
/// written. At most one frame is written. A disconnect monitor cancels provider work when the desktop closes the pipe.
/// It never retries, never upgrades twice, and never reports success. No production composition calls it yet (N4b).
/// </summary>
internal static class ReviewedHelperEndpoint
{
    /// <summary>Request freshness (fact extension rev 3 §6): same machine clock, no tolerance.</summary>
    internal static readonly TimeSpan RequestLifetime = TimeSpan.FromMinutes(2);

    /// <summary>The review ceiling (fact extension rev 3 §6).</summary>
    internal static readonly TimeSpan ReviewCeiling = TimeSpan.FromMinutes(30);

    internal static Task<ReviewedHelperEndpointResult> RunAsync(Stream pipe, IChocolateyPeer peer, IChocolateyHelperQualificationPort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock, long connectedTimestamp, string bootstrapNonce,
        CancellationToken cancellationToken) =>
        RunAsync(pipe, peer, port, capability, clock, connectedTimestamp, bootstrapNonce, ReviewedHelperEndpointBudgets.Production, cancellationToken);

    internal static async Task<ReviewedHelperEndpointResult> RunAsync(Stream pipe, IChocolateyPeer peer, IChocolateyHelperQualificationPort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock, long connectedTimestamp, string bootstrapNonce,
        ReviewedHelperEndpointBudgets budgets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(budgets);

        // A0: an uncorrelatable request gets no response (fact extension rev 3 §6).
        CorrelatedRequest? request = await ReadAndCorrelateAsync(pipe, bootstrapNonce, budgets.FrameRead, cancellationToken).ConfigureAwait(false);
        if (request is null) return new(ReviewedHelperEndpointExit.NoResponse, "a0-request-not-correlated");

        using CancellationTokenSource provider = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource stopMonitor = new();
        Task monitor = MonitorDesktopDisconnectAsync(pipe, provider, stopMonitor.Token);
        using Stages stages = new(request, peer, port, capability, clock, connectedTimestamp, budgets, provider.Token);
        Decision decision;
        try
        {
            decision = await stages.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            decision = stages.UnexpectedFault();
        }
        finally
        {
            await stopMonitor.CancelAsync().ConfigureAwait(false);
            await ObserveMonitorAsync(monitor, budgets.ResponseWrite).ConfigureAwait(false);
        }

        byte[] frame;
        try
        {
            frame = ReviewedUpdateWireCodec.CreateResponse(request.Operation.JobId, request.Digest, bootstrapNonce, decision.Facts,
                decision.Reviewed, decision.Diagnostic);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // S1–S13 or fact consistency failed: no response, so the desktop records Uncertain (§5.2).
            return new(ReviewedHelperEndpointExit.NoResponse, "response-refused-by-codec; " + decision.Diagnostic);
        }

        using CancellationTokenSource write = new(budgets.ResponseWrite);
        try
        {
            await PipeMessageFraming.WriteAsync(pipe, frame, write.Token).ConfigureAwait(false);
            return new(ReviewedHelperEndpointExit.ResponseWritten, decision.Diagnostic);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(ReviewedHelperEndpointExit.ResponseWriteFailed, decision.Diagnostic);
        }
    }

    private static async Task<CorrelatedRequest?> ReadAndCorrelateAsync(Stream pipe, string bootstrapNonce, TimeSpan budget,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsUpperHex64(bootstrapNonce)) return null;
            using CancellationTokenSource read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            read.CancelAfter(budget);
            byte[] frame = await PipeMessageFraming.ReadAsync(pipe, read.Token).ConfigureAwait(false);
            if (!ReviewedUpdateWireCodec.TryReadRequest(frame, out FramedUpdateRequest? framed) || framed is null) return null;
            byte[] payload = framed.GetPayload();
            if (!FixedEquals(Convert.ToHexString(SHA256.HashData(payload)), framed.Digest)) return null;
            if (!ReviewedUpdateOperation.TryRestore(payload, framed.Digest, out ReviewedUpdateOperation? operation) || operation is null ||
                !FixedEquals(operation.Digest, framed.Digest)) return null;
            if (!FixedEquals(framed.Nonce, bootstrapNonce)) return null;
            return new(operation, framed.Digest, framed.IssuedAt);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>Any read completion (end of file or unexpected data) or read failure cancels provider work, unless stopping.</summary>
    private static async Task MonitorDesktopDisconnectAsync(Stream pipe, CancellationTokenSource provider, CancellationToken stop)
    {
        byte[] unexpected = new byte[1];
        try
        {
            _ = await pipe.ReadAsync(unexpected, stop).ConfigureAwait(false);
            if (!stop.IsCancellationRequested) await provider.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!stop.IsCancellationRequested)
            {
                try { await provider.CancelAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private static async Task ObserveMonitorAsync(Task monitor, TimeSpan budget)
    {
        try { await monitor.WaitAsync(budget).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private static bool IsUpperHex64(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool FixedEquals(string? value, string? expected) => IsUpperHex64(value) && IsUpperHex64(expected) &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(value!), Encoding.ASCII.GetBytes(expected!));

    private sealed record CorrelatedRequest(ReviewedUpdateOperation Operation, string Digest, DateTimeOffset IssuedAt);

    private sealed record Decision(UpdateWireFacts Facts, ReviewedHelperFacts Reviewed, string Diagnostic);

    /// <summary>The stages for one admitted request. Owns the item from A2 until after the single response write.</summary>
    private sealed class Stages(CorrelatedRequest request, IChocolateyPeer peer, IChocolateyHelperQualificationPort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock, long connectedTimestamp, ReviewedHelperEndpointBudgets budgets,
        CancellationToken token) : IDisposable
    {
        private readonly ReviewedUpdateOperation operation = request.Operation;
        private readonly DateTimeOffset issuedAt = request.IssuedAt;
        private IChocolateyHelperItem? item;
        private bool launchEntered;
        private string profileDigest = string.Empty;
        private string? stageBInstalled, stageBFingerprint, stageCCandidate, stageDInstalled, stageDFingerprint;

        private string FrozenInstalled => operation.Selection.InstalledVersion;

        private string FrozenCandidate => operation.Selection.CandidateVersion;

        internal async Task<Decision> RunAsync()
        {
            // ---- A1: admission (correlatable failures are RequestNotAdmitted, fields 16–18 null) ----
            if (ApprovalLapsed(clock.GetUtcNow())) return NotAdmitted("a1-request-not-fresh");
            if (!ReviewedUpdateProtocol2Interpreter.TryGetProfileDigest(operation, out string digest) || !HasExactRouteShape())
                return NotAdmitted("a1-route-not-admitted");
            if (!TryReadPeer(out string peerImage, out string peerSid) ||
                !string.Equals(peerSid, operation.RequestingUserSid, StringComparison.Ordinal))
                return NotAdmitted("a1-requester-not-peer");
            profileDigest = digest;

            // ---- A2: the item (evaluation token, linked-token rules) and a live peer ----
            ChocolateyItemRefusal refusal;
            try { item = port.BeginItem(peer, operation.RequestingUserSid, out refusal); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { item = null; refusal = ChocolateyItemRefusal.RequesterTokenRefused; }
            if (item is null || refusal != ChocolateyItemRefusal.None) return NotAdmitted("a2-item-refused");
            if (PeerExited()) return NotAdmitted("a2-peer-exited");

            // ---- A3 (G1): helper capability with the requester as protection principal, plus image hashes ----
            if (!CapabilitySupported(item, peerImage)) return NotAdmitted("a3-capability-refused");

            return await StageBAsync(item).ConfigureAwait(false);
        }

        private async Task<Decision> StageBAsync(IChocolateyHelperItem current)
        {
            if (InterruptBefore("b") is { } interrupted) return interrupted;
            ChocolateyStageValues values;
            try { values = current.ObserveProfile(operation, profileDigest, Remaining(), token); }
            catch (OperationCanceledException) { return Undetermined(true, false, Settled(), false, "b-cancelled"); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Fault(null, null, null, "b-fault"); }
            switch (values)
            {
                case ChocolateyStageValues.Observed observed:
                    // Defence in depth: Observed must carry the frozen installed value and F; anything else is an internal fault.
                    if (!string.Equals(observed.Installed, FrozenInstalled, StringComparison.Ordinal) || !FixedEquals(observed.Fingerprint, profileDigest))
                        return Fault(null, null, null, "b-observed-inconsistent");
                    stageBInstalled = observed.Installed;
                    stageBFingerprint = observed.Fingerprint;
                    return await StageCAsync(current).ConfigureAwait(false);
                case ChocolateyStageValues.Refused refused:
                    return Refused(Reason(refused.Reason), refused.Installed, null, refused.Fingerprint, "b-refused");
                case ChocolateyStageValues.TimedOut:
                    return Undetermined(false, true, Settled(), false, "b-timed-out");
                case ChocolateyStageValues.Cancelled:
                    return Undetermined(true, false, Settled(), false, "b-cancelled");
                case ChocolateyStageValues.OutOfOrder:
                    return Fault(null, null, null, "b-out-of-order");
                default:
                    return Fault(null, null, null, "b-fault");
            }
        }

        private async Task<Decision> StageCAsync(IChocolateyHelperItem current)
        {
            if (InterruptBefore("c") is { } interrupted) return interrupted;
            TimeSpan remaining = Remaining();
            ChocolateyCandidateValues values;
            try { values = await current.QueryCandidateAsync(remaining, token).WaitAsync(remaining + budgets.ExitMargin).ConfigureAwait(false); }
            catch (TimeoutException) { return Undetermined(false, true, false, false, "c-unresponsive"); }
            catch (OperationCanceledException) { return Undetermined(true, false, Settled(), false, "c-cancelled"); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Fault(stageBInstalled, null, stageBFingerprint, "c-fault"); }
            switch (values)
            {
                case ChocolateyCandidateValues.Matched matched:
                    if (!string.Equals(matched.Candidate, FrozenCandidate, StringComparison.Ordinal))
                        return Fault(stageBInstalled, null, stageBFingerprint, "c-matched-inconsistent");
                    stageCCandidate = matched.Candidate;
                    return await StageDAsync(current).ConfigureAwait(false);
                case ChocolateyCandidateValues.Refused refused:
                    return Refused(Reason(refused.Reason), refused.Installed, refused.Candidate, refused.Fingerprint, "c-refused", refused.OutputLimitExceeded);
                case ChocolateyCandidateValues.Undetermined undetermined:
                    return Undetermined(undetermined.Cancelled, undetermined.TimedOut, undetermined.AggregateSettlementConfirmed && Settled(),
                        undetermined.OutputLimitExceeded, "c-undetermined");
                case ChocolateyCandidateValues.OutOfOrder:
                    // rev-5 F2 stage C: the stage-B installed value and fingerprint, candidate null.
                    return Fault(stageBInstalled, null, stageBFingerprint, "c-out-of-order");
                default:
                    return Fault(stageBInstalled, null, stageBFingerprint, "c-fault");
            }
        }

        private async Task<Decision> StageDAsync(IChocolateyHelperItem current)
        {
            // Rechecks in order: cancellation, the 2-minute and 30-minute limits, then the budget (N4).
            if (token.IsCancellationRequested) return Undetermined(true, false, Settled(), false, "d-cancelled");
            if (ApprovalLapsed(clock.GetUtcNow())) return Refused(ReviewedHelperRefusalReason.ApprovalExpired, null, null, null, "d-approval-expired");
            if (Remaining() <= TimeSpan.Zero) return Undetermined(false, true, Settled(), false, "d-timed-out");
            ChocolateyStageValues values;
            try { values = current.RevalidateBeforeLaunch(Remaining(), token); }
            catch (OperationCanceledException) { return Undetermined(true, false, Settled(), false, "d-cancelled"); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Fault(null, null, null, "d-fault"); }
            switch (values)
            {
                case ChocolateyStageValues.Observed observed:
                    if (!string.Equals(observed.Installed, FrozenInstalled, StringComparison.Ordinal) || !FixedEquals(observed.Fingerprint, profileDigest))
                        return Fault(null, null, null, "d-observed-inconsistent");
                    stageDInstalled = observed.Installed;
                    stageDFingerprint = observed.Fingerprint;
                    return await LaunchAsync(current).ConfigureAwait(false);
                case ChocolateyStageValues.Refused refused:
                    // Drift: the stage-B reason with stage-D values; the stage-C candidate stays unless installed is null (S7).
                    return Refused(Reason(refused.Reason), refused.Installed, stageCCandidate, refused.Fingerprint, "d-drift");
                case ChocolateyStageValues.TimedOut:
                    return Undetermined(false, true, Settled(), false, "d-timed-out");
                case ChocolateyStageValues.Cancelled:
                    return Undetermined(true, false, Settled(), false, "d-cancelled");
                case ChocolateyStageValues.OutOfOrder:
                    // rev-5 F2 stage D: no stage-D values exist, so installed, fingerprint and candidate are null.
                    return Fault(null, null, null, "d-out-of-order");
                default:
                    return Fault(null, null, null, "d-fault");
            }
        }

        private async Task<Decision> LaunchAsync(IChocolateyHelperItem current)
        {
            // Matched needs every preflight root settled (rev 3 §3.1); nothing has been launched yet.
            if (!Settled()) return Undetermined(false, false, false, false, "launch-preflight-unsettled");
            TimeSpan remaining = Remaining();
            if (remaining < budgets.LaunchFloor) return MatchedNotStarted(false, true, "launch-floor-not-met");
            if (token.IsCancellationRequested) return MatchedNotStarted(true, false, "launch-cancelled-before-creation");

            // From here any fault follows the Launch row: Unknown (N2).
            launchEntered = true;
            ChocolateyUpgradeResult result;
            try { result = await current.UpgradeAsync(remaining, token).WaitAsync(remaining + budgets.ExitMargin).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Unknown(false, false, false, false, "launch-unknown"); }
            switch (result)
            {
                case ChocolateyUpgradeResult.Observed observed:
                    return FromObservation(observed.Observation);
                case ChocolateyUpgradeResult.OutOfOrder:
                    // rev-5 F2 Launch: the stage-D values plus the stage-C candidate; nothing was launched.
                    return Fault(stageDInstalled, stageCCandidate, stageDFingerprint, "launch-out-of-order");
                default:
                    return Unknown(false, false, false, false, "launch-unknown");
            }
        }

        private Decision FromObservation(NativeProcessObservation observation)
        {
            bool itemSettled = Settled();
            switch (observation.Invocation)
            {
                case NativeInvocation.NotStarted:
                    // A definitive launch failure: settled, nothing ran (S13). Anything less is not known to have failed.
                    if (observation.AggregateSettled && itemSettled && !observation.TerminationFailed && !observation.OutputLimitExceeded)
                        return Matched(new(UpdateWireInvocation.NotStarted, null, false, false, false, false, true,
                            observation.Cancelled, observation.TimedOut, false), "launch-not-started");
                    return Unknown(observation.TerminationFailed, observation.Cancelled, observation.TimedOut, observation.OutputLimitExceeded,
                        "launch-not-started-unsettled");
                case NativeInvocation.Started:
                    // Facts 4–13 as observed; an inconsistent set makes CreateResponse throw, so nothing is written.
                    return Matched(new(UpdateWireInvocation.Started, observation.ExitCode, observation.DirectExitObserved,
                        observation.OwnedIoCompleted, observation.PipeEofObserved, observation.TerminationFailed,
                        observation.AggregateSettled && itemSettled, observation.Cancelled, observation.TimedOut,
                        observation.OutputLimitExceeded), "launch-started");
                default:
                    return Unknown(observation.TerminationFailed, observation.Cancelled, observation.TimedOut, observation.OutputLimitExceeded,
                        "launch-unknown");
            }
        }

        /// <summary>A fault escaping the stage code itself: Unknown once process creation was entered, else the N2 rules.</summary>
        internal Decision UnexpectedFault() => launchEntered
            ? Unknown(false, false, false, false, "internal-fault-after-launch")
            : Fault(null, null, null, "internal-fault");

        public void Dispose()
        {
            try { item?.Dispose(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }

        private TimeSpan Remaining() =>
            budgets.Execution - clock.GetElapsedTime(connectedTimestamp) - budgets.ExitMargin - budgets.ResponseMargin;

        private bool ApprovalLapsed(DateTimeOffset now) =>
            issuedAt > now || now - issuedAt > RequestLifetime ||
            operation.ReviewedAt > now || now - operation.ReviewedAt > ReviewCeiling;

        /// <summary>Exact route shape: the same predicates as the fixed upgrade arguments (fact extension rev 3 §1).</summary>
        private bool HasExactRouteShape()
        {
            try { return ChocolateyReviewedUpdateArguments.Build(operation).Count > 0; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }

        private bool TryReadPeer(out string imagePath, out string userSid)
        {
            imagePath = userSid = string.Empty;
            try
            {
                if (!peer.TryReadIdentity(out string image, out _, out string sid) || string.IsNullOrEmpty(image) || string.IsNullOrEmpty(sid))
                    return false;
                imagePath = image;
                userSid = sid;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }

        private bool PeerExited()
        {
            try { return peer.HasExited; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return true; }
        }

        private bool CapabilitySupported(IChocolateyHelperItem current, string peerImage)
        {
            bool Protection(string root, IReadOnlyList<string> files)
            {
                try { return current.RequesterCannotModify(root, files); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
            }
            try { return capability.Evaluate(operation.RouteContract, Protection, peerImage) is { IsSupported: true }; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }

        /// <summary>True only when every owned launch job is known settled; before A2 nothing is owned.</summary>
        private bool Settled()
        {
            try { return item?.AggregateSettled ?? true; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }

        /// <summary>Before stages B and C: cancellation, then a budget lapse (N4), each Undetermined with its flag.</summary>
        private Decision? InterruptBefore(string stage)
        {
            if (token.IsCancellationRequested) return Undetermined(true, false, Settled(), false, stage + "-cancelled");
            if (Remaining() <= TimeSpan.Zero) return Undetermined(false, true, Settled(), false, stage + "-timed-out");
            return null;
        }

        /// <summary>
        /// N2 internal fault (also rev-5 F2 out-of-order) before process creation: an interrupt forces Undetermined; otherwise
        /// MachineInputsUnsupported with the stage's values when settled, else Undetermined with ¬aggregateSettlementConfirmed.
        /// </summary>
        private Decision Fault(string? installed, string? candidate, string? fingerprint, string diagnostic)
        {
            if (token.IsCancellationRequested) return Undetermined(true, false, Settled(), false, diagnostic);
            return Refused(ReviewedHelperRefusalReason.MachineInputsUnsupported, installed, candidate, fingerprint, diagnostic);
        }

        private Decision NotAdmitted(string diagnostic) =>
            Refused(ReviewedHelperRefusalReason.RequestNotAdmitted, null, null, null, diagnostic);

        /// <summary>A settled refusal (S10: no interrupting cause). Unprovable settlement becomes Undetermined (N2).</summary>
        private Decision Refused(ReviewedHelperRefusalReason reason, string? installed, string? candidate, string? fingerprint, string diagnostic,
            bool outputLimitExceeded = false)
        {
            // S7: no candidate without an installed value, nor with RequestNotAdmitted or PreflightQueryFailed.
            if (installed is null || reason is ReviewedHelperRefusalReason.RequestNotAdmitted or ReviewedHelperRefusalReason.PreflightQueryFailed)
                candidate = null;
            if (reason == ReviewedHelperRefusalReason.RequestNotAdmitted) installed = fingerprint = null;
            if (!Settled()) return Undetermined(false, false, false, outputLimitExceeded, diagnostic + "; unsettled");
            return new(new(UpdateWireInvocation.NotStarted, null, false, false, false, false, true, false, false,
                outputLimitExceeded && reason == ReviewedHelperRefusalReason.PreflightQueryFailed),
                new(ReviewedHelperQualification.Refused, reason, installed, candidate, fingerprint), diagnostic);
        }

        /// <summary>S11: Undetermined always carries cancelled, timedOut or ¬aggregateSettlementConfirmed.</summary>
        private static Decision Undetermined(bool cancelled, bool timedOut, bool settled, bool outputLimitExceeded, string diagnostic)
        {
            if (!cancelled && !timedOut) settled = false;
            return new(new(UpdateWireInvocation.NotStarted, null, false, false, false, false, settled, cancelled, timedOut, outputLimitExceeded),
                new(ReviewedHelperQualification.Undetermined, ReviewedHelperRefusalReason.None, null, null, null), diagnostic);
        }

        /// <summary>Matched ∧ NotStarted: stages A–D passed and nothing ran; settled (S13).</summary>
        private Decision MatchedNotStarted(bool cancelled, bool timedOut, string diagnostic) =>
            Matched(new(UpdateWireInvocation.NotStarted, null, false, false, false, false, true, cancelled, timedOut, false), diagnostic);

        private Decision Unknown(bool terminationFailed, bool cancelled, bool timedOut, bool outputLimitExceeded, string diagnostic) =>
            Matched(new(UpdateWireInvocation.Unknown, null, false, false, false, terminationFailed, false, cancelled, timedOut, outputLimitExceeded),
                diagnostic);

        /// <summary>Matched values: the stage-D installed value and fingerprint plus the stage-C candidate (rev 3 §3.1).</summary>
        private Decision Matched(UpdateWireFacts facts, string diagnostic) =>
            new(facts, new(ReviewedHelperQualification.Matched, ReviewedHelperRefusalReason.None, stageDInstalled, stageCCandidate, stageDFingerprint),
                diagnostic);

        private static ReviewedHelperRefusalReason Reason(ChocolateyStageRefusal reason) => reason switch
        {
            ChocolateyStageRefusal.MachineInputsUnsupported => ReviewedHelperRefusalReason.MachineInputsUnsupported,
            ChocolateyStageRefusal.SourceNotCommunity => ReviewedHelperRefusalReason.SourceNotCommunity,
            ChocolateyStageRefusal.InstalledMismatch => ReviewedHelperRefusalReason.InstalledMismatch,
            ChocolateyStageRefusal.ProfileMismatch => ReviewedHelperRefusalReason.ProfileMismatch,
            _ => ReviewedHelperRefusalReason.MachineInputsUnsupported,
        };

        private static ReviewedHelperRefusalReason Reason(ChocolateyCandidateRefusal reason) => reason switch
        {
            ChocolateyCandidateRefusal.InstalledMismatch => ReviewedHelperRefusalReason.InstalledMismatch,
            ChocolateyCandidateRefusal.PreflightQueryFailed => ReviewedHelperRefusalReason.PreflightQueryFailed,
            ChocolateyCandidateRefusal.CandidateMismatch => ReviewedHelperRefusalReason.CandidateMismatch,
            _ => ReviewedHelperRefusalReason.MachineInputsUnsupported,
        };
    }
}
