using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;

namespace PCSteward.Execution;

/// <summary>How one launch of the self-update session ended, as the desktop saw it.</summary>
internal enum SelfUpdateLaunchOutcome
{
    /// <summary>The helper prepared the job and started the finish copy: PC Steward must now close.</summary>
    Ready,
    /// <summary>The helper refused with one of its fixed codes; nothing was installed.</summary>
    Refused,
    /// <summary>The owner declined the Windows prompt.</summary>
    ElevationDeclined,
    /// <summary>A check before the prompt failed (fixed paths, launch trust, install check, pipe), so no prompt was shown.</summary>
    NotStarted,
    /// <summary>The helper started but no valid answer arrived; whether a finish copy started is unknown.</summary>
    NoValidAnswer,
}

/// <summary>The launch's result: the outcome and, for Ready and Refused, the accepted answer.</summary>
internal sealed record SelfUpdateLaunchResult(SelfUpdateLaunchOutcome Outcome, SelfUpdateResponse? Response);

/// <summary>Launches the fixed helper with <c>runas</c> and the session line. Returns null when no process exists.</summary>
internal delegate Process? SelfUpdateHelperLauncher(string helperPath, IReadOnlyList<string> arguments, out bool elevationDeclined);

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-032 revision 2 §6 (desktop side): one launch of the elevated self-update session.
/// <para>
/// Pre-launch order, as for the reviewed protocol-2 transport: fixed paths, initial launch trust, a restricted per-launch
/// pipe, the Desktop-role release check of this install with its binding set held (an owner-only install refuses here),
/// final launch trust, cancellation, the runtime-environment guard, then one <c>runas</c> launch. After connection the peer
/// check adds the file-identity comparison against the held helper image. The exchange itself is
/// <see cref="ReviewedHelperExchangeSession"/>: one request, exactly one answer, then the helper's exit.
/// </para>
/// The binding set is released only when no helper exists or once the helper's exit has been observed.
/// </summary>
internal sealed class SelfUpdateDesktopTransport
{
    /// <summary>Connection 30 s; the helper's own 11-minute budget plus a minute; exit 10 s.</summary>
    internal static ReviewedHelperBudgets Budgets { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(12), TimeSpan.FromSeconds(10));

    private readonly Func<HelperLaunchPaths?> resolvePaths;
    private readonly Func<HelperLaunchPaths, HelperLaunchTrustResult> validateLaunchTrust;
    private readonly OwnerOnlyDeploymentCapabilityReader capability;
    private readonly SelfUpdateHelperLauncher launch;
    private readonly ReviewedHelperBudgets budgets;

    internal SelfUpdateDesktopTransport() : this(InstalledHelperLaunchTrust.ResolveFixedPaths, InstalledHelperLaunchTrust.Validate,
        new OwnerOnlyDeploymentCapabilityReader(), LaunchHelper, Budgets)
    {
    }

    /// <summary>Fixture seam (architecture test: no <c>src</c> caller other than the parameterless constructor).</summary>
    internal SelfUpdateDesktopTransport(Func<HelperLaunchPaths?> resolvePaths, Func<HelperLaunchPaths, HelperLaunchTrustResult> validateLaunchTrust,
        OwnerOnlyDeploymentCapabilityReader capability, SelfUpdateHelperLauncher launch, ReviewedHelperBudgets budgets)
    {
        this.resolvePaths = resolvePaths ?? throw new ArgumentNullException(nameof(resolvePaths));
        this.validateLaunchTrust = validateLaunchTrust ?? throw new ArgumentNullException(nameof(validateLaunchTrust));
        this.capability = capability ?? throw new ArgumentNullException(nameof(capability));
        this.launch = launch ?? throw new ArgumentNullException(nameof(launch));
        this.budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
    }

    /// <summary>Runs one session for <paramref name="request"/> (its nonce is this launch's). Must not throw for expected failures.</summary>
    internal async Task<SelfUpdateLaunchResult> InstallAsync(SelfUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[] requestBytes;
        try { requestBytes = SelfUpdateInstallPolicy.WriteRequest(request); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return NotStarted(); }
        if (!OperatingSystem.IsWindows() || !SelfUpdateInstallPolicy.TryReadRequest(requestBytes, out _)) return NotStarted();

        HelperLaunchPaths? paths;
        try { paths = resolvePaths(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return NotStarted(); }
        if (paths is null || !LaunchTrusted(paths)) return NotStarted();
        string currentSid;
        int currentSession;
        try
        {
            currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
            using Process current = Process.GetCurrentProcess();
            currentSession = current.SessionId;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return NotStarted(); }
        if (currentSid.Length == 0) return NotStarted();

        string pipeName = WindowsReviewedHelperTransport.CreatePipeName();
        IReadOnlyList<string> arguments;
        try { arguments = SelfUpdateDesktop.SessionArguments(Environment.ProcessId, pipeName, request.Nonce); }
        catch (ArgumentException) { return NotStarted(); }
        NamedPipeServerStream pipe;
        try { pipe = RestrictedNamedPipe.Create(pipeName, currentSid); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return NotStarted(); }
        using (pipe)
        {
            HeldHelperBinding? binding;
            HelperProtocolCapabilityResult held;
            try { held = capability.TryHoldInstalledRelease(out binding); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return NotStarted(); }
            if (binding is null) return NotStarted();
            if (!held.IsSupported || !binding.IsHolding ||
                !string.Equals(Path.GetFullPath(binding.HelperPath), Path.GetFullPath(paths.HelperPath), StringComparison.OrdinalIgnoreCase) ||
                !LaunchTrusted(paths) || cancellationToken.IsCancellationRequested)
            {
                ReleaseWithoutHelper(binding);
                return NotStarted();
            }

            Process? helper;
            bool declined;
            try { helper = launch(paths.HelperPath, arguments, out declined); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { helper = null; declined = false; }
            if (helper is null)
            {
                ReleaseWithoutHelper(binding);
                return declined ? new(SelfUpdateLaunchOutcome.ElevationDeclined, null) : NotStarted();
            }

            HelperProcessWork owned;
            try { owned = new(helper); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A helper exists but cannot be owned: tear it down and keep the binding held.
                TryTerminate(helper);
                return new(SelfUpdateLaunchOutcome.NoValidAnswer, null);
            }
            ReleaseAfterOwnership(owned.OwnedCompletion, binding);
            Channel channel = new(pipe, helper, paths, currentSession, currentSid, binding);
            ReviewedHelperExchange exchange = await ReviewedHelperExchangeSession.RunAsync(channel, owned, () => TryTerminate(helper),
                () => requestBytes, budgets, cancellationToken).ConfigureAwait(false);
            GC.KeepAlive(binding);
            if (exchange.Outcome != ReviewedHelperTransportOutcome.ResponseFrameReceived ||
                !SelfUpdateDesktop.TryAcceptResponse(exchange.ResponseFrame, request, out SelfUpdateResponse? response) || response is null)
                return new(exchange.Outcome == ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten
                    ? SelfUpdateLaunchOutcome.NotStarted : SelfUpdateLaunchOutcome.NoValidAnswer, null);
            return new(response.Ready ? SelfUpdateLaunchOutcome.Ready : SelfUpdateLaunchOutcome.Refused, response);
        }
    }

    private static SelfUpdateLaunchResult NotStarted() => new(SelfUpdateLaunchOutcome.NotStarted, null);

    /// <summary>No helper process exists, so nothing can load from the binding set: release it now.</summary>
    private static void ReleaseWithoutHelper(HeldHelperBinding binding) => binding.Dispose();

    /// <summary>Release once the helper's exit has been observed and the transport sealed; a helper that never exits keeps it held (A-M2).</summary>
    private static void ReleaseAfterOwnership(Task ownedCompletion, HeldHelperBinding binding) =>
        _ = ownedCompletion.ContinueWith(static (_, state) => ((HeldHelperBinding)state!).Dispose(), binding,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private bool LaunchTrusted(HelperLaunchPaths paths)
    {
        try { return validateLaunchTrust(paths).Trusted; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static Process? LaunchHelper(string helperPath, IReadOnlyList<string> arguments, out bool elevationDeclined)
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

    private static bool TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>The peer is the launched helper: PID, fixed path, session, SID, signer, and the held image's file identity.</summary>
    private sealed class Channel(NamedPipeServerStream pipe, Process helper, HelperLaunchPaths paths, int session, string sid, HeldHelperBinding binding)
        : IReviewedHelperChannel
    {
        public Stream Stream => pipe;

        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken);

        public bool VerifyPeer()
        {
            try
            {
                return RestrictedNamedPipe.TryGetClientProcessId(pipe, out int clientProcessId) &&
                    clientProcessId == helper.Id &&
                    WindowsPeerIdentityVerifier.VerifyPeer(clientProcessId, paths.HelperPath, paths.DesktopPath, session, sid, out _) &&
                    NativeMethods.TryGetProcessIdentity(clientProcessId, out string peerImage, out _, out _) &&
                    binding.IsHolding && binding.PeerImageMatchesHelper(peerImage);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
        }
    }
}
