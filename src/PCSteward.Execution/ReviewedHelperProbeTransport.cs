using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using PCSteward.Domain;

namespace PCSteward.Execution;

/// <summary>Qualification probe spec rev 4 §5.3: the closed <c>desktopOutcome</c> domain, in the spec's order.</summary>
internal enum QualificationProbeDesktopOutcome
{
    ResponseAccepted, GateBusy, GateUnavailable, GateRecoveryRequired, GateResetPending, DesktopNotLimited, ConflictingProcess,
    LaunchTrustFailed, CapabilityNotHeld, ElevationDeclined, HelperNotStarted, HelperNotConnected, PeerNotVerified, NoValidResponse,
    ResponseRejected
}

/// <summary>
/// §5.3 <c>pins</c> (review M-1): the deployment values the desktop's <see cref="IHelperProtocolCapability.TryHold"/> verified
/// (step 6). Hashes are 64 uppercase hex; the thumbprint is 40 uppercase hex. Values only; nothing here grants anything.
/// </summary>
internal sealed record QualificationProbePins(string LedgerSha256, string CapabilityFileSha256, string DesktopImageSha256,
    string HelperImageSha256, string SignerThumbprint, DateTimeOffset SignerNotAfterUtc);

/// <summary>§5.3: the desktop's transport facts. Every value is observed by the desktop itself; none is read from the helper.</summary>
internal sealed record QualificationProbeTransportFacts(
    bool InitialLaunchTrust,
    bool FinalLaunchTrust,
    bool HelperProcessCreated,
    bool PipeConnected,
    bool DesktopPeerVerified,
    bool HelperFileIdEqual,
    bool ResponseFrameReceived,
    bool PipeEofObserved,
    bool HelperExitObserved,
    bool BindingHeldUntilExit,
    bool TerminationFailed,
    int? HelperExitCode)
{
    internal static QualificationProbeTransportFacts None { get; } = new(false, false, false, false, false, false, false, false, false, false, false, null);
}

/// <summary>
/// The result of one desktop probe run (§5.2 steps 2–10): the closed outcome, the desktop's per-token <c>TryHold</c> statuses
/// (ordinal token order; <see cref="QualificationProbeCapabilityStatus.NotEvaluated"/> before step 6), the transport facts,
/// the pins (only once step 6 held) and the accepted response facts (only for <see cref="QualificationProbeDesktopOutcome.ResponseAccepted"/>).
/// </summary>
internal sealed record QualificationProbeRun(
    QualificationProbeDesktopOutcome Outcome,
    IReadOnlyList<QualificationProbeCapabilityEntry> CapabilityStatus,
    QualificationProbeTransportFacts Transport,
    QualificationProbePins? Pins,
    QualificationProbeFacts? Probe)
{
    /// <summary>A refusal before step 6: every token <c>NotEvaluated</c>, no pins, no response, no transport fact.</summary>
    internal static QualificationProbeRun Refused(QualificationProbeDesktopOutcome outcome, QualificationProbeTransportFacts? transport = null) =>
        new(outcome, NotEvaluated(), transport ?? QualificationProbeTransportFacts.None, null, null);

    internal static IReadOnlyList<QualificationProbeCapabilityEntry> NotEvaluated() =>
        [.. QualificationProbeContract.Compiled.G1Keys.Select(static key => new QualificationProbeCapabilityEntry(key, QualificationProbeCapabilityStatus.NotEvaluated))];
}

/// <summary>The authenticated pipe for one launched probe helper (§5.2 step 8). Neither check may throw.</summary>
internal interface IReviewedHelperProbeChannel
{
    Stream Stream { get; }
    Task WaitForConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Client PID equals the helper PID; path, session, SID and signer (the update transport's peer check).</summary>
    bool VerifyPeerIdentity();

    /// <summary>The peer image's <c>FileIdInfo</c> equals the helper identity of every held binding, all still holding.</summary>
    bool PeerImageMatchesEveryBinding();
}

/// <summary>What the post-launch part of a probe run (§5.2 steps 8–10) observed.</summary>
internal sealed record ReviewedHelperProbeSessionResult(
    QualificationProbeDesktopOutcome Outcome,
    QualificationProbeFacts? Probe,
    bool PipeConnected,
    bool DesktopPeerVerified,
    bool HelperFileIdEqual,
    bool ResponseFrameReceived,
    bool PipeEofObserved,
    bool HelperExitObserved,
    bool BindingHeldUntilExit,
    bool TerminationFailed,
    int? HelperExitCode);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; qualification probe spec rev 4 §5.2 steps 8–10: the post-launch part of one probe run,
/// independent of Windows so every boundary is testable over a real pipe pair. <b>Nothing is ever written to the pipe.</b>
/// <list type="bullet">
/// <item>no connection within the connection budget, or helper exit first: <see cref="QualificationProbeDesktopOutcome.HelperNotConnected"/>;</item>
/// <item>the peer identity or the file identity refused: <see cref="QualificationProbeDesktopOutcome.PeerNotVerified"/>;</item>
/// <item>anything other than exactly one frame followed by pipe EOF within the execution budget (counted from connection):
/// <see cref="QualificationProbeDesktopOutcome.NoValidResponse"/>;</item>
/// <item>a frame the probe codec refuses for this nonce: <see cref="QualificationProbeDesktopOutcome.ResponseRejected"/> (every
/// response field is discarded); otherwise <see cref="QualificationProbeDesktopOutcome.ResponseAccepted"/>.</item>
/// </list>
/// Exit is then observed within the exit budget; any failure path, including a missing exit, terminates the helper. The exit
/// code and the binding state are read when exit is observed, before the transport is sealed. It never retries.
/// </summary>
internal static class ReviewedHelperProbeSession
{
    internal static async Task<ReviewedHelperProbeSessionResult> RunAsync(IReviewedHelperProbeChannel channel, HelperProcessWork owned,
        Func<bool> terminate, Func<int?> exitCode, Func<bool> bindingsHolding, string nonce, ReviewedHelperBudgets budgets,
        QualificationProbeContract contract, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(terminate);
        ArgumentNullException.ThrowIfNull(exitCode);
        ArgumentNullException.ThrowIfNull(bindingsHolding);
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(budgets);
        ArgumentNullException.ThrowIfNull(contract);
        QualificationProbeDesktopOutcome outcome = QualificationProbeDesktopOutcome.HelperNotConnected;
        QualificationProbeFacts? accepted = null;
        bool connected = false, peerVerified = false, fileIdEqual = false, frameReceived = false, endOfFile = false;
        bool exitObserved = false, heldUntilExit = false, terminationFailed = false;
        int? code = null;
        void Terminate()
        {
            bool terminated;
            try { terminated = terminate(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { terminated = false; }
            if (!terminated) terminationFailed = true;
        }

        try
        {
            connected = await ConnectAsync(channel, owned, budgets, cancellationToken).ConfigureAwait(false);
            if (connected)
            {
                // §5.2 step 9: the 11-minute read is counted from connection.
                using CancellationTokenSource executionTimeout = new(budgets.Execution);
                peerVerified = Check(channel.VerifyPeerIdentity) && !cancellationToken.IsCancellationRequested;
                fileIdEqual = peerVerified && Check(channel.PeerImageMatchesEveryBinding) && !cancellationToken.IsCancellationRequested;
                if (!peerVerified || !fileIdEqual) outcome = QualificationProbeDesktopOutcome.PeerNotVerified;
                else
                {
                    outcome = QualificationProbeDesktopOutcome.NoValidResponse;
                    using CancellationTokenSource execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, executionTimeout.Token);
                    byte[] frame = [];
                    try
                    {
                        frame = await PipeMessageFraming.ReadAsync(channel.Stream, execution.Token).ConfigureAwait(false);
                        frameReceived = true;
                        byte[] probe = new byte[1];
                        endOfFile = await channel.Stream.ReadAsync(probe, execution.Token).ConfigureAwait(false) == 0;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        // Lost, late, oversized, truncated, disconnected or any unexpected fault: no valid response.
                    }
                    if (frameReceived && endOfFile)
                        outcome = QualificationProbeWireCodec.TryReadProbeResponse(frame, nonce, contract, out QualificationProbeFacts? facts) && facts is not null
                            ? Accept(facts, out accepted)
                            : QualificationProbeDesktopOutcome.ResponseRejected;
                }
            }

            if (outcome is QualificationProbeDesktopOutcome.ResponseAccepted or QualificationProbeDesktopOutcome.ResponseRejected)
            {
                exitObserved = await ObserveExitAsync(owned, budgets.Exit).ConfigureAwait(false);
                if (!exitObserved) Terminate();
            }
            else
            {
                Terminate();
                exitObserved = await ObserveExitAsync(owned, budgets.Exit).ConfigureAwait(false);
            }
            if (exitObserved)
            {
                heldUntilExit = Check(bindingsHolding);
                try { code = exitCode(); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { code = null; }
            }
        }
        finally
        {
            owned.SealTransport();
        }

        if (exitObserved)
        {
            // Ownership completes once exit is observed and the transport is sealed; give it the exit budget to run.
            try { await owned.OwnedCompletion.WaitAsync(budgets.Exit, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
        return new(outcome, accepted, connected, peerVerified, fileIdEqual, frameReceived, endOfFile, exitObserved, heldUntilExit,
            terminationFailed, code);
    }

    private static QualificationProbeDesktopOutcome Accept(QualificationProbeFacts facts, out QualificationProbeFacts? accepted)
    {
        accepted = facts;
        return QualificationProbeDesktopOutcome.ResponseAccepted;
    }

    private static async Task<bool> ConnectAsync(IReviewedHelperProbeChannel channel, HelperProcessWork owned, ReviewedHelperBudgets budgets,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource connectionTimeout = new(budgets.Connection);
        using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionTimeout.Token);
        Task connect;
        try { connect = channel.WaitForConnectionAsync(connection.Token); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        Task first = await Task.WhenAny(connect, owned.ExitObserved).ConfigureAwait(false);
        if (first != connect) await connection.CancelAsync().ConfigureAwait(false);
        try { await connect.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        return first == connect && !cancellationToken.IsCancellationRequested;
    }

    private static bool Check(Func<bool> check)
    {
        try { return check(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static async Task<bool> ObserveExitAsync(HelperProcessWork owned, TimeSpan budget)
    {
        try
        {
            await owned.ExitObserved.WaitAsync(budget).ConfigureAwait(false);
            return owned.ExitObserved.IsCompletedSuccessfully;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }
}

/// <summary>Launches the fixed helper with <c>runas</c> and the probe line. Null when no process exists; <paramref name="elevationDeclined"/> marks UAC denial.</summary>
internal delegate Process? ReviewedHelperProbeLauncher(string helperPath, IReadOnlyList<string> arguments, out bool elevationDeclined);

/// <summary>The current user's SID and session (§5.2 step 5, for the restricted pipe and the peer check).</summary>
internal sealed record ReviewedHelperProbeIdentity(string UserSid, int Session);

/// <summary>
/// Everything <see cref="ReviewedHelperProbeTransport"/> reaches outside its own logic. Production is
/// <see cref="ReviewedHelperProbeTransport.ProductionDependencies"/> only; the fixture tests substitute values (an architecture
/// test pins that no other <c>src</c> code constructs this record).
/// </summary>
internal sealed record ReviewedHelperProbeDependencies(
    Func<bool> DesktopTokenLimited,
    Func<IReadOnlyList<(int ProcessId, string Name)>> EnumerateProcesses,
    int CurrentProcessId,
    Func<HelperLaunchPaths?> ResolvePaths,
    Func<HelperLaunchPaths, HelperLaunchTrustResult> ValidateLaunchTrust,
    Func<ReviewedHelperProbeIdentity?> CurrentIdentity,
    Func<string, string, NamedPipeServerStream> CreatePipe,
    IHelperProtocolCapability Capability,
    ReviewedHelperProbeLauncher Launch,
    Func<NamedPipeServerStream, Process, HelperLaunchPaths, ReviewedHelperProbeIdentity, IReadOnlyList<HeldHelperBinding>, IReviewedHelperProbeChannel> CreateChannel,
    ReviewedHelperBudgets Budgets);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; qualification probe spec rev 4 §5.2 steps 3–10, §5.5 and §6: the desktop side of the
/// owner-started Chocolatey helper qualification probe. <see cref="ProbeAsync"/> reuses the update transport's fixed paths,
/// launch trust (<see cref="InstalledHelperLaunchTrust"/>), restricted per-request pipe (<see cref="RestrictedNamedPipe"/>),
/// capability hold (<see cref="IHelperProtocolCapability.TryHold"/>), <c>runas</c> launcher shape and peer check
/// (<see cref="WindowsPeerIdentityVerifier"/> and the held <c>FileIdInfo</c>), in this order:
/// <list type="number">
/// <item>(step 3) the process token's elevation type is <c>Limited</c>, or <see cref="QualificationProbeDesktopOutcome.DesktopNotLimited"/>;</item>
/// <item>(step 4) a read-only process-name enumeration finds no <c>choco</c> and no other <c>PCSteward*</c> process, or
/// <see cref="QualificationProbeDesktopOutcome.ConflictingProcess"/> (an enumeration failure too);</item>
/// <item>(step 5) fixed paths and the initial launch trust, then the current identity, the restricted pipe and a fresh
/// 32-byte nonce, or <see cref="QualificationProbeDesktopOutcome.LaunchTrustFailed"/>;</item>
/// <item>(step 6) <c>TryHold</c> for <b>every</b> compiled allow-list token: all <c>Supported</c>, holding, the same helper path
/// and <c>FileIdInfo</c>, and the same complete pins, or <see cref="QualificationProbeDesktopOutcome.CapabilityNotHeld"/> with every
/// binding released;</item>
/// <item>(step 7) the final launch trust, then one <c>runas</c> launch with the probe line (<see cref="ProbeArguments"/>: the
/// kind at indices 0–1); UAC denial is <see cref="QualificationProbeDesktopOutcome.ElevationDeclined"/>, no process
/// <see cref="QualificationProbeDesktopOutcome.HelperNotStarted"/>; both release the bindings;</item>
/// <item>(steps 8–10) <see cref="ReviewedHelperProbeSession"/>; the bindings are released only after exit is observed and the
/// transport is sealed (A-M2), as in the update transport.</item>
/// </list>
/// It builds no request (there is no operation) and omits the Chocolatey caller-trust check (§5.2; not covered by E3). It
/// never opens a database, takes a claim, scans or creates a window. Its only <c>src</c> caller is
/// <see cref="QualificationProbeEntry"/> (architecture test D1).
/// </summary>
internal sealed class ReviewedHelperProbeTransport
{
    private readonly ReviewedHelperProbeDependencies dependencies;

    internal ReviewedHelperProbeTransport() : this(ProductionDependencies())
    {
    }

    /// <summary>Fixture seam (architecture test: no <c>src</c> caller other than the parameterless constructor).</summary>
    internal ReviewedHelperProbeTransport(ReviewedHelperProbeDependencies dependencies) =>
        this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    /// <summary>The production composition: Windows primitives only, the Desktop-role capability reader and the production budgets.</summary>
    internal static ReviewedHelperProbeDependencies ProductionDependencies() => new(
        DesktopTokenLimited, EnumerateProcessNames, Environment.ProcessId, InstalledHelperLaunchTrust.ResolveFixedPaths,
        InstalledHelperLaunchTrust.Validate, CurrentIdentity, RestrictedNamedPipe.Create, new OwnerOnlyDeploymentCapabilityReader(),
        LaunchProbeHelper, static (pipe, helper, paths, identity, bindings) => new WindowsReviewedHelperProbeChannel(pipe, helper, paths, identity, bindings),
        ReviewedHelperBudgets.Production);

    /// <summary>
    /// §2: the probe line, exactly 10 arguments: <c>--kind qualify-chocolatey-helper --protocol 2 --desktop-pid &lt;n&gt; --pipe
    /// &lt;name&gt; --nonce &lt;hex&gt;</c>. The kind is first, so the UAC details show it right after the program path (review L10).
    /// This is the only <c>src</c> emitter of the kind literal (architecture test D3).
    /// </summary>
    internal static IReadOnlyList<string> ProbeArguments(int desktopProcessId, string pipeName, string nonce) =>
    [
        ReviewedHelperProbeBootstrap.KindKey, QualificationProbeWireCodec.Kind,
        "--protocol", ReviewedUpdateWireCodec.ProtocolVersion,
        "--desktop-pid", desktopProcessId.ToString(CultureInfo.InvariantCulture),
        "--pipe", pipeName,
        "--nonce", nonce,
    ];

    /// <summary>§5.4 budget: connection 30 s, response read 11 min from connection, exit 5 s (the update transport's budgets).</summary>
    internal static ReviewedHelperBudgets Budgets => ReviewedHelperBudgets.Production;

    /// <summary>§5.2 steps 3–10. Must not throw for expected failures; every failure ends the run with its stated outcome.</summary>
    internal async Task<QualificationProbeRun> ProbeAsync(CancellationToken cancellationToken)
    {
        ReviewedHelperProbeDependencies d = dependencies;
        // Step 3: the elevation type must be Limited (an elevated desktop, UAC off and standard users all refuse here).
        if (!Safe(d.DesktopTokenLimited)) return QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.DesktopNotLimited);
        // Step 4: read-only process-name enumeration; nothing is opened for access or signalled.
        if (!NoConflictingProcess(d)) return QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.ConflictingProcess);

        // Step 5: fixed paths and the initial launch trust, then the identity, the restricted pipe and a fresh nonce.
        HelperLaunchPaths? paths;
        try { paths = d.ResolvePaths(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { paths = null; }
        if (paths is null || !LaunchTrusted(d, paths)) return QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.LaunchTrustFailed);
        QualificationProbeTransportFacts facts = QualificationProbeTransportFacts.None with { InitialLaunchTrust = true };
        ReviewedHelperProbeIdentity? identity;
        try { identity = d.CurrentIdentity(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { identity = null; }
        if (identity is null || string.IsNullOrEmpty(identity.UserSid))
            return QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.LaunchTrustFailed, facts);
        string pipeName = WindowsReviewedHelperTransport.CreatePipeName();
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        NamedPipeServerStream pipe;
        try { pipe = d.CreatePipe(pipeName, identity.UserSid); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return QualificationProbeRun.Refused(QualificationProbeDesktopOutcome.LaunchTrustFailed, facts);
        }
        using (pipe)
        {
            // Step 6: TryHold for every compiled allow-list token, in ordinal token order.
            List<HeldHelperBinding> bindings = [];
            List<QualificationProbeCapabilityEntry> statuses = [];
            List<ProbeHold> holds = [];
            foreach (UpdateRouteContract route in DeploymentCapabilityPolicy.CapabilityOperationAllowlist
                         .OrderBy(DeploymentCapabilityPolicy.OperationToken, StringComparer.Ordinal))
            {
                HelperProtocolCapabilityResult held;
                HeldHelperBinding? binding;
                try { held = d.Capability.TryHold(route, out binding); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    held = HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Unreadable);
                    binding = null;
                }
                statuses.Add(new(DeploymentCapabilityPolicy.OperationToken(route), ProbeStatus(held.Status)));
                if (binding is not null) bindings.Add(binding);
                holds.Add(binding is null ? new(held.IsSupported, false, string.Empty, default, null) :
                    new(held.IsSupported, binding.IsHolding, binding.HelperPath, binding.HelperIdentity, PinsOf(binding)));
            }
            QualificationProbePins? pins = ReviewedHelperProbeHold.Agree(holds, paths.HelperPath);
            if (pins is null)
            {
                ReleaseWithoutHelper(bindings);
                return new(QualificationProbeDesktopOutcome.CapabilityNotHeld, statuses, facts, null, null);
            }
            // Step 7: the final launch trust, immediately before the only prompt.
            if (!LaunchTrusted(d, paths))
            {
                ReleaseWithoutHelper(bindings);
                return new(QualificationProbeDesktopOutcome.LaunchTrustFailed, statuses, facts, pins, null);
            }
            facts = facts with { FinalLaunchTrust = true };
            if (cancellationToken.IsCancellationRequested)
            {
                ReleaseWithoutHelper(bindings);
                return new(QualificationProbeDesktopOutcome.HelperNotStarted, statuses, facts, pins, null);
            }

            // Step 7: one runas launch with the probe line.
            Process? helper;
            bool declined;
            try { helper = d.Launch(paths.HelperPath, ProbeArguments(d.CurrentProcessId, pipeName, nonce), out declined); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { helper = null; declined = false; }
            if (helper is null)
            {
                ReleaseWithoutHelper(bindings);
                return new(declined ? QualificationProbeDesktopOutcome.ElevationDeclined : QualificationProbeDesktopOutcome.HelperNotStarted,
                    statuses, facts, pins, null);
            }
            facts = facts with { HelperProcessCreated = true };

            HelperProcessWork owned;
            try { owned = new(helper); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A helper exists but cannot be owned: tear it down. The bindings are not released here (A-M2); the run ends,
                // the result file is written, the gate is released and the desktop's Environment.Exit releases the holds right after.
                return new(QualificationProbeDesktopOutcome.HelperNotConnected, statuses, facts with { TerminationFailed = !TryTerminate(helper) }, pins, null);
            }
            ReleaseAfterOwnership(owned.OwnedCompletion, bindings);
            IReviewedHelperProbeChannel channel;
            try { channel = d.CreateChannel(pipe, helper, paths, identity, bindings); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                channel = new RefusingProbeChannel(pipe);
            }
            ReviewedHelperProbeSessionResult session = await ReviewedHelperProbeSession.RunAsync(channel, owned, () => TryTerminate(helper),
                () => ReadExitCode(helper), () => bindings.All(static binding => binding.IsHolding), nonce, d.Budgets,
                QualificationProbeContract.Compiled, cancellationToken).ConfigureAwait(false);
            // Strong references until after seal (review-16 L2); release itself waits for the ownership task.
            GC.KeepAlive(bindings);
            return new(session.Outcome, statuses, facts with
            {
                PipeConnected = session.PipeConnected,
                DesktopPeerVerified = session.DesktopPeerVerified,
                HelperFileIdEqual = session.HelperFileIdEqual,
                ResponseFrameReceived = session.ResponseFrameReceived,
                PipeEofObserved = session.PipeEofObserved,
                HelperExitObserved = session.HelperExitObserved,
                BindingHeldUntilExit = session.BindingHeldUntilExit,
                TerminationFailed = session.TerminationFailed,
                HelperExitCode = session.HelperExitCode,
            }, pins, session.Probe);
        }
    }

    /// <summary>§5.2 step 4 (pure): a <c>choco</c> process, or any process other than the current one named <c>PCSteward*</c>.</summary>
    internal static bool HasConflictingProcess(IEnumerable<(int ProcessId, string Name)> processes, int currentProcessId)
    {
        ArgumentNullException.ThrowIfNull(processes);
        foreach ((int processId, string name) in processes)
        {
            if (name is null) return true;
            if (string.Equals(name, "choco", StringComparison.OrdinalIgnoreCase)) return true;
            if (processId != currentProcessId && name.StartsWith("PCSteward", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>The closed capability status by exact name; an undefined value is <c>Unreadable</c>.</summary>
    internal static QualificationProbeCapabilityStatus ProbeStatus(HelperProtocolCapabilityStatus status) => status switch
    {
        HelperProtocolCapabilityStatus.Supported => QualificationProbeCapabilityStatus.Supported,
        HelperProtocolCapabilityStatus.Absent => QualificationProbeCapabilityStatus.Absent,
        HelperProtocolCapabilityStatus.Incomplete => QualificationProbeCapabilityStatus.Incomplete,
        HelperProtocolCapabilityStatus.Mismatch => QualificationProbeCapabilityStatus.Mismatch,
        _ => QualificationProbeCapabilityStatus.Unreadable,
    };

    private static bool NoConflictingProcess(ReviewedHelperProbeDependencies d)
    {
        try { return !HasConflictingProcess(d.EnumerateProcesses(), d.CurrentProcessId); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static QualificationProbePins? PinsOf(HeldHelperBinding binding)
    {
        IReadOnlyDictionary<string, DeploymentObservedFile> hashes = binding.VerifiedHashes;
        if (binding.LedgerSha256 is not { } ledger || binding.HelperSigner is not { } signer ||
            !hashes.TryGetValue(DeploymentCapabilityPolicy.CapabilityFileName, out DeploymentObservedFile capability) ||
            !hashes.TryGetValue(InstalledHelperLaunchTrust.DesktopExecutableName, out DeploymentObservedFile desktop) ||
            !hashes.TryGetValue(InstalledHelperLaunchTrust.HelperExecutableName, out DeploymentObservedFile helper))
            return null;
        return new(ledger, capability.Sha256, desktop.Sha256, helper.Sha256, signer.Thumbprint, signer.NotAfterUtc);
    }

    private static bool LaunchTrusted(ReviewedHelperProbeDependencies d, HelperLaunchPaths paths)
    {
        try { return d.ValidateLaunchTrust(paths).Trusted; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static bool Safe(Func<bool> check)
    {
        try { return check(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    /// <summary>No helper process exists, so nothing can load from the binding set: release every binding now.</summary>
    private static void ReleaseWithoutHelper(List<HeldHelperBinding> bindings)
    {
        foreach (HeldHelperBinding binding in bindings) binding.Dispose();
    }

    /// <summary>Release once exit has been observed and the transport is sealed; a helper that never exits keeps them held (A-M2).</summary>
    private static void ReleaseAfterOwnership(Task ownedCompletion, List<HeldHelperBinding> bindings) =>
        _ = ownedCompletion.ContinueWith(static (_, state) =>
        {
            foreach (HeldHelperBinding binding in (List<HeldHelperBinding>)state!) binding.Dispose();
        }, bindings, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static int? ReadExitCode(Process helper)
    {
        try { return helper.HasExited ? helper.ExitCode : null; }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException) { return null; }
    }

    private static bool TryTerminate(Process process) =>
        TryTerminate(() => process.HasExited, () => process.Kill(entireProcessTree: true));

    /// <summary>
    /// Kills the tree unless it has exited. False on the expected failures, including the <see cref="AggregateException"/>
    /// that <c>Kill(entireProcessTree: true)</c> throws when a descendant cannot be killed, so the caller still records an outcome.
    /// </summary>
    internal static bool TryTerminate(Func<bool> hasExited, Action killTree)
    {
        ArgumentNullException.ThrowIfNull(hasExited);
        ArgumentNullException.ThrowIfNull(killTree);
        try
        {
            if (!hasExited()) killTree();
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
        {
            return false;
        }
    }

    /// <summary>The update transport's launcher shape (<c>runas</c>, shell execute, structured arguments) with the probe line.</summary>
    private static Process? LaunchProbeHelper(string helperPath, IReadOnlyList<string> arguments, out bool elevationDeclined)
    {
        elevationDeclined = false;
        ProcessStartInfo start = new() { FileName = helperPath, UseShellExecute = true, Verb = "runas" };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        // Security review H1: no UAC prompt while a user-writable environment source holds a .NET runtime control name.
        if (!ElevatedRuntimeEnvironmentGuard.PermitsElevatedLaunch()) return null;
        try { return Process.Start(start); }
        catch (Win32Exception exception) when (exception.NativeErrorCode == NativeMethods.ErrorCancelled)
        {
            elevationDeclined = true;
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>A read-only snapshot of process IDs and names; every <see cref="Process"/> object is disposed.</summary>
    private static IReadOnlyList<(int ProcessId, string Name)> EnumerateProcessNames()
    {
        Process[] processes = Process.GetProcesses();
        try { return [.. processes.Select(static process => (process.Id, process.ProcessName))]; }
        finally
        {
            foreach (Process process in processes) process.Dispose();
        }
    }

    private static ReviewedHelperProbeIdentity? CurrentIdentity()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string? sid = identity.User?.Value;
        using Process current = Process.GetCurrentProcess();
        return string.IsNullOrEmpty(sid) ? null : new(sid, current.SessionId);
    }

    /// <summary>§5.2 step 3: true only when the current process token's elevation type is <c>TokenElevationTypeLimited</c>.</summary>
    private static bool DesktopTokenLimited()
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (!ElevationNativeMethods.OpenProcessToken(ElevationNativeMethods.GetCurrentProcess(), ElevationNativeMethods.TokenQuery, out SafeAccessTokenHandle token))
            return false;
        using (token)
        {
            return !token.IsInvalid &&
                ElevationNativeMethods.GetTokenInformation(token, ElevationNativeMethods.TokenElevationTypeClass, out int elevation, sizeof(int), out int returned) &&
                returned == sizeof(int) && elevation == ElevationNativeMethods.TokenElevationTypeLimited;
        }
    }

    private static class ElevationNativeMethods
    {
        internal const uint TokenQuery = 0x0008;
        internal const int TokenElevationTypeClass = 18;
        internal const int TokenElevationTypeLimited = 3;

        [DllImport("kernel32.dll")]
        internal static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information,
            int informationLength, out int returnLength);
    }

    /// <summary>A channel that never verifies (used only when the production channel cannot be constructed).</summary>
    private sealed class RefusingProbeChannel(NamedPipeServerStream pipe) : IReviewedHelperProbeChannel
    {
        public Stream Stream => pipe;
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken);
        public bool VerifyPeerIdentity() => false;
        public bool PeerImageMatchesEveryBinding() => false;
    }

    private sealed class WindowsReviewedHelperProbeChannel(NamedPipeServerStream pipe, Process helper, HelperLaunchPaths paths,
        ReviewedHelperProbeIdentity identity, IReadOnlyList<HeldHelperBinding> bindings) : IReviewedHelperProbeChannel
    {
        public Stream Stream => pipe;

        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken);

        public bool VerifyPeerIdentity()
        {
            try
            {
                return RestrictedNamedPipe.TryGetClientProcessId(pipe, out int clientProcessId) && clientProcessId == helper.Id &&
                    WindowsPeerIdentityVerifier.VerifyPeer(clientProcessId, paths.HelperPath, paths.DesktopPath, identity.Session, identity.UserSid, out _);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }

        public bool PeerImageMatchesEveryBinding()
        {
            try
            {
                return RestrictedNamedPipe.TryGetClientProcessId(pipe, out int clientProcessId) && clientProcessId == helper.Id &&
                    NativeMethods.TryGetProcessIdentity(clientProcessId, out string peerImage, out _, out _) && bindings.Count > 0 &&
                    bindings.All(binding => binding.IsHolding && binding.PeerImageMatchesHelper(peerImage));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }
    }
}

/// <summary>One step-6 hold as plain values: the status, whether it holds, its helper path and identity, and its complete pins.</summary>
internal readonly record struct ProbeHold(bool Supported, bool Holding, string HelperPath, WindowsFileIdentity HelperIdentity, QualificationProbePins? Pins);

/// <summary>§5.2 step 6 (pure): every token's hold must agree before any prompt.</summary>
internal static class ReviewedHelperProbeHold
{
    /// <summary>
    /// The common pins when every hold is <c>Supported</c>, holding, names the fixed helper path and one <c>FileIdInfo</c>, and
    /// carries complete, equal pins; otherwise null (<see cref="QualificationProbeDesktopOutcome.CapabilityNotHeld"/>). An empty
    /// list is null.
    /// </summary>
    internal static QualificationProbePins? Agree(IReadOnlyList<ProbeHold> holds, string expectedHelperPath)
    {
        ArgumentNullException.ThrowIfNull(holds);
        if (holds.Count == 0 || string.IsNullOrEmpty(expectedHelperPath)) return null;
        ProbeHold first = holds[0];
        if (first.Pins is not { } pins || !IsComplete(pins)) return null;
        foreach (ProbeHold hold in holds)
        {
            if (!hold.Supported || !hold.Holding || string.IsNullOrEmpty(hold.HelperPath) || hold.HelperIdentity == default ||
                !string.Equals(FullPath(hold.HelperPath), FullPath(expectedHelperPath), StringComparison.OrdinalIgnoreCase) ||
                hold.HelperIdentity != first.HelperIdentity || hold.Pins != pins)
                return null;
        }
        return pins;
    }

    private static bool IsComplete(QualificationProbePins pins) =>
        IsHex(pins.LedgerSha256, 64) && IsHex(pins.CapabilityFileSha256, 64) && IsHex(pins.DesktopImageSha256, 64) &&
        IsHex(pins.HelperImageSha256, 64) && IsHex(pins.SignerThumbprint, 40) && pins.SignerNotAfterUtc.Offset == TimeSpan.Zero;

    private static bool IsHex(string? value, int length) =>
        value is not null && value.Length == length && value.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static string FullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return string.Empty; }
    }
}
