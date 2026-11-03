using PCSteward.Domain;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Execution;

/// <summary>
/// Probe budgets (probe spec rev 4 §3): <see cref="Probe"/> is the 10-minute probe budget from t0 (pipe connection), below the
/// desktop's 11-minute response read. <see cref="ExitMargin"/> extends the B′ outer cap; the inbound-monitor stop and the single
/// response write share one window of <c>min(</c><see cref="ResponseWrite"/><c>,</c> <see cref="ResponseMargin"/><c>)</c>.
/// Production uses the update endpoint's 5-s values (a test pins the equality), so the last write ends at most 10 min 10 s
/// after t0.
/// </summary>
internal sealed record ReviewedHelperProbeBudgets(TimeSpan Probe, TimeSpan ExitMargin, TimeSpan ResponseMargin, TimeSpan ResponseWrite)
{
    internal static ReviewedHelperProbeBudgets Production { get; } = new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(5));
}

/// <summary>What the probe entry did with the pipe. Only <see cref="ResponseWritten"/> means one frame was written.</summary>
internal enum ReviewedHelperProbeExit { ResponseWritten, NoResponse, InboundByte, ResponseWriteFailed }

/// <summary>The probe entry's own result: the exit kind and a fixed diagnostic token (never wire data).</summary>
internal sealed record ReviewedHelperProbeResult(ReviewedHelperProbeExit Exit, string Diagnostic);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §3 (P1, A2, A3, B′, Stop, in-flight disposal) and §7 H1–H3,
/// H7, H10: the probe entry over an already connected, verified pipe and the probe port. It handles values and opaque
/// interfaces only, never reads a request frame and never launches anything.
/// <list type="bullet">
/// <item><b>P1</b> one pending 1-byte read on the pipe whose byte is never parsed: a byte stops the run with no response
/// (<see cref="ReviewedHelperProbeExit.InboundByte"/>, exit 27); end of file or a read failure cancels or abandons any running
/// stage, with no response (exit 24).</item>
/// <item><b>A2</b> <see cref="IChocolateyProbePort.BeginProbe"/> with the host's SID: a verdict other than <c>Accepted</c>
/// reports every <c>g1Status</c> as <c>NotEvaluated</c> and <c>observation = NotRun</c>.</item>
/// <item><b>A3</b> every compiled allow-list token in ordinal order, each evaluated independently through
/// <see cref="IReviewedHelperCapabilityCheck"/> with the item's requester check as the protection principal, each under a
/// real-time cap of what is left of the budget. The cap, a budget already spent, end of file or an inbound byte abandons the
/// evaluation (never awaited); afterwards the protection check refuses without reaching the item; no response.</item>
/// <item><b>B′</b> exactly one <see cref="IChocolateyProbeItem.ObserveClient"/>, bounded by <c>remaining</c> and capped at
/// <c>remaining + ExitMargin</c>; a spent budget is <c>TimedOut</c> without any read. A cancelled, faulted, capped or
/// interrupted observation is never written.</item>
/// <item><b>Stop</b> the item is disposed (the native item has already disposed the observation's binding), then the frame
/// is built only through <see cref="QualificationProbeWireCodec.CreateProbeResponse(string, QualificationProbeFacts)"/>, the
/// monitor is stopped and must have seen nothing, and the frame is written once; the stop and the write share one window. A
/// codec throw or any fault writes nothing.</item>
/// </list>
/// Its only caller is <see cref="ReviewedHelperProbeBootstrap"/>.
/// </summary>
internal static class ReviewedHelperProbeEndpoint
{
    internal static Task<ReviewedHelperProbeResult> RunAsync(Stream pipe, IChocolateyPeer peer, IChocolateyProbePort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock, long connectedTimestamp, string expectedSid, string bootstrapNonce,
        CancellationToken cancellationToken) =>
        RunAsync(pipe, peer, port, capability, clock, connectedTimestamp, expectedSid, bootstrapNonce, ReviewedHelperProbeBudgets.Production,
            cancellationToken);

    internal static async Task<ReviewedHelperProbeResult> RunAsync(Stream pipe, IChocolateyPeer peer, IChocolateyProbePort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock, long connectedTimestamp, string expectedSid, string bootstrapNonce,
        ReviewedHelperProbeBudgets budgets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(peer);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(budgets);
        if (!IsUpperHex64(bootstrapNonce) || string.IsNullOrEmpty(expectedSid)) return new(ReviewedHelperProbeExit.NoResponse, "p0-bootstrap-invalid");

        // P1: the inbound monitor starts before any stage; its byte is never parsed.
        using CancellationTokenSource inbound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using CancellationTokenSource stopMonitor = new();
        InboundMonitor monitor = new();
        Task watching = monitor.WatchAsync(pipe, inbound, stopMonitor.Token);
        // One response window on the injected clock, armed when the stages end: the monitor stop and the single write draw on it.
        using CancellationTokenSource window = new(Timeout.InfiniteTimeSpan, clock);
        Decision decision;
        using (Stages stages = new(peer, port, capability, clock, connectedTimestamp, expectedSid, budgets, inbound.Token))
        {
            try
            {
                decision = await stages.RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                decision = Decision.None("probe-fault");
            }
            // Stop: the item (and with it the evaluation token) is disposed before the response is built (H3). An abandoned
            // protection check or observation may still be running; the item tolerates that (N4b).
            stages.DisposeItem();
        }

        byte[]? frame = null;
        string diagnostic = decision.Diagnostic;
        if (decision.Facts is { } facts)
        {
            try { frame = QualificationProbeWireCodec.CreateProbeResponse(bootstrapNonce, facts); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { diagnostic = "response-refused-by-codec; " + diagnostic; }
        }

        window.CancelAfter(ResponseWindow(budgets));
        await stopMonitor.CancelAsync().ConfigureAwait(false);
        await ObserveMonitorAsync(watching, window.Token).ConfigureAwait(false);
        switch (monitor.Seen)
        {
            case InboundMonitor.InboundByte: return new(ReviewedHelperProbeExit.InboundByte, "p1-inbound-byte; " + diagnostic);
            case InboundMonitor.EndOfFile: return new(ReviewedHelperProbeExit.NoResponse, "p1-desktop-closed; " + diagnostic);
        }
        if (frame is null) return new(ReviewedHelperProbeExit.NoResponse, diagnostic);
        if (window.IsCancellationRequested) return new(ReviewedHelperProbeExit.ResponseWriteFailed, "response-window-spent; " + diagnostic);
        try
        {
            // The window also bounds a write that does not observe its token (review L-3).
            await PipeMessageFraming.WriteAsync(pipe, frame, window.Token).WaitAsync(window.Token).ConfigureAwait(false);
            return new(ReviewedHelperProbeExit.ResponseWritten, diagnostic);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new(ReviewedHelperProbeExit.ResponseWriteFailed, diagnostic);
        }
    }

    /// <summary>The shared monitor-stop and response-write window, drawn from the response margin.</summary>
    private static TimeSpan ResponseWindow(ReviewedHelperProbeBudgets budgets) =>
        budgets.ResponseWrite < budgets.ResponseMargin ? budgets.ResponseWrite : budgets.ResponseMargin;

    private static async Task ObserveMonitorAsync(Task monitor, CancellationToken window)
    {
        try { await monitor.WaitAsync(window).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    private static bool IsUpperHex64(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    /// <summary>
    /// P1: one pending 1-byte read. A completed read with a byte is <see cref="InboundByte"/> (even if the stop was already
    /// requested), a completed read of zero bytes or a read failure is <see cref="EndOfFile"/>; either cancels the stages. The
    /// byte is never inspected and the pipe is never read again.
    /// </summary>
    private sealed class InboundMonitor
    {
        internal const int Nothing = 0;
        internal const int InboundByte = 1;
        internal const int EndOfFile = 2;
        private int seen;

        internal int Seen => Volatile.Read(ref seen);

        internal async Task WatchAsync(Stream pipe, CancellationTokenSource stages, CancellationToken stop)
        {
            byte[] one = new byte[1];
            try
            {
                int read = await pipe.ReadAsync(one, stop).ConfigureAwait(false);
                Volatile.Write(ref seen, read > 0 ? InboundByte : EndOfFile);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                Volatile.Write(ref seen, EndOfFile);
            }
            try { await stages.CancelAsync().ConfigureAwait(false); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>One decision: the facts to encode, or none (no response), and a fixed diagnostic token.</summary>
    private sealed record Decision(QualificationProbeFacts? Facts, string Diagnostic)
    {
        internal static Decision None(string diagnostic) => new(null, diagnostic);
    }

    /// <summary>The stages for one probe run. Owns the item from A2 until it is disposed at Stop.</summary>
    private sealed class Stages(IChocolateyPeer peer, IChocolateyProbePort port, IReviewedHelperCapabilityCheck capability, TimeProvider clock,
        long connectedTimestamp, string expectedSid, ReviewedHelperProbeBudgets budgets, CancellationToken token) : IDisposable
    {
        private IChocolateyProbeItem? item;
        private int capabilityAbandoned;

        internal async Task<Decision> RunAsync()
        {
            // ---- A2: the probe item (evaluation token, linked-token rules) and a live peer ----
            IChocolateyProbeItem? begun;
            TokenVerdict verdict;
            try { begun = port.BeginProbe(peer, expectedSid, out verdict); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Decision.None("a2-fault"); }
            item = begun;
            if (!TryMapByName(verdict, out QualificationProbeTokenVerdict wireVerdict)) return Decision.None("a2-verdict-undefined");
            if (wireVerdict != QualificationProbeTokenVerdict.Accepted)
                return begun is null ? new(TokenRefused(wireVerdict), "a2-token-refused") : Decision.None("a2-inconsistent");
            if (begun is null) return Decision.None("a2-inconsistent");
            if (token.IsCancellationRequested) return Decision.None("a2-interrupted");

            // ---- A3 (G1) per compiled allow-list token, each independently, each under the cap ----
            if (!TryReadPeerImage(out string peerImage)) return Decision.None("a3-peer-unreadable");
            List<QualificationProbeCapabilityEntry> g1 = [];
            foreach (UpdateRouteContract route in DeploymentCapabilityPolicy.CapabilityOperationAllowlist
                         .OrderBy(DeploymentCapabilityPolicy.OperationToken, StringComparer.Ordinal))
            {
                TimeSpan remaining = Remaining();
                if (remaining <= TimeSpan.Zero)
                {
                    // A budget already spent before a token starts counts as the cap; the evaluation is not started.
                    Volatile.Write(ref capabilityAbandoned, 1);
                    return Decision.None("a3-budget-spent");
                }
                HelperProtocolCapabilityStatus status;
                try
                {
                    status = await Task.Run(() => Evaluate(route, begun, peerImage), CancellationToken.None)
                        .WaitAsync(remaining, clock, token).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    Volatile.Write(ref capabilityAbandoned, 1);
                    return Decision.None("a3-unresponsive");
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    Volatile.Write(ref capabilityAbandoned, 1);
                    return Decision.None("a3-interrupted");
                }
                g1.Add(new(DeploymentCapabilityPolicy.OperationToken(route), Status(status)));
            }
            if (g1.Any(entry => entry.Status != QualificationProbeCapabilityStatus.Supported))
                return new(CapabilityRefused(g1), "a3-capability-refused");

            // ---- B′: exactly one client-mode observation under the outer cap ----
            TimeSpan left = Remaining();
            if (left <= TimeSpan.Zero) return new(TimedOut(g1, 0, 0), "b-timed-out");
            ChocolateyProbeValues values;
            try
            {
                values = await Task.Run(() => begun.ObserveClient(left, token), CancellationToken.None)
                    .WaitAsync(left + budgets.ExitMargin, clock, token).ConfigureAwait(false);
            }
            catch (TimeoutException) { return Decision.None("b-unresponsive"); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return Decision.None("b-interrupted"); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Decision.None("b-fault"); }
            return values switch
            {
                ChocolateyProbeValues.Observed observed => new(Observed(g1, observed), "b-observed"),
                ChocolateyProbeValues.Refused refused => Refused(g1, refused) is { } facts ? new(facts, "b-refused") : Decision.None("b-refusal-unmapped"),
                ChocolateyProbeValues.TimedOut timedOut => new(TimedOut(g1, timedOut.NodesEvaluated, timedOut.AncestorsEvaluated), "b-timed-out"),
                // Cancelled is never written (review L4); a faulted or unknown value writes nothing.
                ChocolateyProbeValues.Cancelled => Decision.None("b-cancelled"),
                _ => Decision.None("b-faulted"),
            };
        }

        /// <summary>Stop: disposes the item once; an abandoned call may still be in flight (N4b).</summary>
        internal void DisposeItem()
        {
            IChocolateyProbeItem? current = Interlocked.Exchange(ref item, null);
            try { current?.Dispose(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }

        public void Dispose() => DisposeItem();

        private TimeSpan Remaining() => budgets.Probe - clock.GetElapsedTime(connectedTimestamp);

        /// <summary>
        /// One token's G1 evaluation. After the cap the protection check refuses without reaching the item. A thrown
        /// evaluation or an undefined status is <c>Unreadable</c>, as the Helper-role check itself reports any exception.
        /// </summary>
        private HelperProtocolCapabilityStatus Evaluate(UpdateRouteContract route, IChocolateyProbeItem current, string peerImage)
        {
            bool Protection(string root, IReadOnlyList<string> files)
            {
                if (Volatile.Read(ref capabilityAbandoned) != 0) return false;
                try { return current.RequesterCannotModify(root, files); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
            }
            try { return capability.Evaluate(route, Protection, peerImage)?.Status ?? HelperProtocolCapabilityStatus.Unreadable; }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return HelperProtocolCapabilityStatus.Unreadable; }
        }

        private bool TryReadPeerImage(out string imagePath)
        {
            imagePath = string.Empty;
            try
            {
                if (!peer.TryReadIdentity(out string image, out _, out _) || string.IsNullOrEmpty(image)) return false;
                imagePath = image;
                return true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }
    }

    // ---- Facts (values only; the codec enforces Q1–Q16 and throws on any violation) ----

    private static QualificationProbeFacts TokenRefused(QualificationProbeTokenVerdict verdict) =>
        Facts(verdict, [.. QualificationProbeContract.Compiled.G1Keys.Select(key => new QualificationProbeCapabilityEntry(key, QualificationProbeCapabilityStatus.NotEvaluated))],
            QualificationProbeObservation.NotRun, null, QualificationProbeFailedCheck.None, QualificationProbeFailedAccess.None,
            QualificationProbeRefusalReason.None, 0, 0, null);

    private static QualificationProbeFacts CapabilityRefused(IReadOnlyList<QualificationProbeCapabilityEntry> g1) =>
        Facts(QualificationProbeTokenVerdict.Accepted, g1, QualificationProbeObservation.NotRun, null, QualificationProbeFailedCheck.None,
            QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, 0, 0, null);

    private static QualificationProbeFacts TimedOut(IReadOnlyList<QualificationProbeCapabilityEntry> g1, int nodes, int ancestors) =>
        Facts(QualificationProbeTokenVerdict.Accepted, g1, QualificationProbeObservation.TimedOut, null, QualificationProbeFailedCheck.None,
            QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, nodes, ancestors, null);

    private static QualificationProbeFacts Observed(IReadOnlyList<QualificationProbeCapabilityEntry> g1, ChocolateyProbeValues.Observed observed) =>
        Facts(QualificationProbeTokenVerdict.Accepted, g1, QualificationProbeObservation.Observed, null, QualificationProbeFailedCheck.None,
            QualificationProbeFailedAccess.None, QualificationProbeRefusalReason.None, observed.NodesEvaluated, observed.AncestorsEvaluated,
            observed.ClientVersion, observed.FingerprintLength);

    /// <summary>A refusal mapped by exact name; the disclosed detail follows from the check (Q9). Null when unmappable.</summary>
    private static QualificationProbeFacts? Refused(IReadOnlyList<QualificationProbeCapabilityEntry> g1, ChocolateyProbeValues.Refused refused)
    {
        QualificationProbeRefusalReason? reason = refused.Reason switch
        {
            ChocolateyStageRefusal.MachineInputsUnsupported => QualificationProbeRefusalReason.MachineInputsUnsupported,
            ChocolateyStageRefusal.SourceNotCommunity => QualificationProbeRefusalReason.SourceNotCommunity,
            ChocolateyStageRefusal.InstalledMismatch => QualificationProbeRefusalReason.InstalledMismatch,
            _ => null,
        };
        if (reason is null || !TryMapByName(refused.FailedCheck, out QualificationProbeFailedCheck check) ||
            !TryMapByName(refused.FailedAccess, out QualificationProbeFailedAccess access))
            return null;
        return Facts(QualificationProbeTokenVerdict.Accepted, g1, QualificationProbeObservation.Refused, refused.FailedStep, check, access,
            reason.Value, refused.NodesEvaluated, refused.AncestorsEvaluated, refused.ObservedInstalled);
    }

    private static QualificationProbeFacts Facts(QualificationProbeTokenVerdict verdict, IReadOnlyList<QualificationProbeCapabilityEntry> g1,
        QualificationProbeObservation observation, int? step, QualificationProbeFailedCheck check, QualificationProbeFailedAccess access,
        QualificationProbeRefusalReason reason, int nodes, int ancestors, string? version, int fingerprintLength = 0)
    {
        QualificationProbeRefusalDetail detail = check switch
        {
            QualificationProbeFailedCheck.SetupAccessGuard => QualificationProbeRefusalDetail.SetupAccessOverride,
            QualificationProbeFailedCheck.ReleaseLeftover => QualificationProbeRefusalDetail.UnlistedReleaseLeftover,
            _ => QualificationProbeRefusalDetail.None,
        };
        return new(verdict, g1, observation, step, check, access, reason, detail, nodes, ancestors, version,
            observation == QualificationProbeObservation.Observed, fingerprintLength,
            QualificationProbeWireCodec.ExpectedDiagnostic(verdict, observation) ?? string.Empty);
    }

    /// <summary>§4.1 field 4: the closed G1 status by explicit row (an undefined value is <c>Unreadable</c>).</summary>
    private static QualificationProbeCapabilityStatus Status(HelperProtocolCapabilityStatus status) => status switch
    {
        HelperProtocolCapabilityStatus.Supported => QualificationProbeCapabilityStatus.Supported,
        HelperProtocolCapabilityStatus.Absent => QualificationProbeCapabilityStatus.Absent,
        HelperProtocolCapabilityStatus.Incomplete => QualificationProbeCapabilityStatus.Incomplete,
        HelperProtocolCapabilityStatus.Mismatch => QualificationProbeCapabilityStatus.Mismatch,
        _ => QualificationProbeCapabilityStatus.Unreadable,
    };

    /// <summary>
    /// §4.4: a native value maps to the wire enum by exact name (the enum name is the wire value); an undefined or unmatched
    /// value does not map. No numeric cast is used.
    /// </summary>
    internal static bool TryMapByName<TNative, TWire>(TNative value, out TWire mapped) where TNative : struct, Enum where TWire : struct, Enum
    {
        mapped = default;
        if (!Enum.IsDefined(value)) return false;
        string name = value.ToString();
        return Enum.TryParse(name, ignoreCase: false, out mapped) && Enum.IsDefined(mapped) &&
            string.Equals(mapped.ToString(), name, StringComparison.Ordinal);
    }
}
