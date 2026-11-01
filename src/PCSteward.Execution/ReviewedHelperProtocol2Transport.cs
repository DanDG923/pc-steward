using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Execution;

/// <summary>
/// The raw result of one protocol-2 helper exchange: the closed transport phase, the single response frame (empty unless
/// <see cref="ReviewedHelperTransportOutcome.ResponseFrameReceived"/>), and desktop-observed facts only. Nothing here is
/// interpreted; <see cref="ReviewedUpdateProtocol2Interpreter"/> alone turns it into a workflow observation.
/// </summary>
internal sealed record ReviewedHelperExchange(ReviewedHelperTransportOutcome Outcome, byte[] ResponseFrame, ReviewedHelperDesktopObservation Desktop);

/// <summary>One protocol-2 exchange for one frozen operation and one fresh nonce. Must not throw for expected failures.</summary>
internal interface IReviewedHelperTransport
{
    Task<ReviewedHelperExchange> ExchangeAsync(ReviewedUpdateOperation operation, string nonce, CancellationToken cancellationToken);
}

/// <summary>Reviewed fact extension rev 3 §6: connection 30 s, execution 11 min, exit 5 s. Unchanged from protocol 1.</summary>
internal sealed record ReviewedHelperBudgets(TimeSpan Connection, TimeSpan Execution, TimeSpan Exit)
{
    internal static ReviewedHelperBudgets Production { get; } = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(11), TimeSpan.FromSeconds(5));
}

/// <summary>The authenticated pipe for one launched helper. <see cref="VerifyPeer"/> must not throw.</summary>
internal interface IReviewedHelperChannel
{
    Stream Stream { get; }
    Task WaitForConnectionAsync(CancellationToken cancellationToken);
    /// <summary>Peer PID, path, session, SID, signer, and the A.4 step 3.4 file-identity check. False means "do not write".</summary>
    bool VerifyPeer();
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; reviewed fact extension rev 3 §4.1 and §6: the post-launch part of one protocol-2 exchange,
/// independent of Windows so every phase boundary is testable. It reports only the interpreter's closed transport phases:
/// <list type="bullet">
/// <item>no connection, a failed peer check, cancellation before writing, or a request that could not be built:
/// <see cref="ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten"/> (the helper is terminated and its exit is
/// awaited for H);</item>
/// <item>once writing has begun, anything other than exactly one frame followed by pipe EOF within the execution budget:
/// <see cref="ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse"/>;</item>
/// <item>exactly one frame, then EOF: <see cref="ReviewedHelperTransportOutcome.ResponseFrameReceived"/>, followed by the
/// exit wait for H. A missing exit leaves H false, which the interpreter turns into Uncertain.</item>
/// </list>
/// It never retries, never writes a second request, and seals the transport before reporting.
/// </summary>
internal static class ReviewedHelperExchangeSession
{
    internal static async Task<ReviewedHelperExchange> RunAsync(IReviewedHelperChannel channel, HelperProcessWork owned, Func<bool> terminate,
        Func<byte[]> createRequest, ReviewedHelperBudgets budgets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(owned);
        ArgumentNullException.ThrowIfNull(terminate);
        ArgumentNullException.ThrowIfNull(createRequest);
        ArgumentNullException.ThrowIfNull(budgets);
        ReviewedHelperTransportOutcome outcome = ReviewedHelperTransportOutcome.HelperStartedRequestNotWritten;
        byte[] frame = [];
        bool terminationFailed = false, ioComplete = false, endOfFile = false, exitObserved = false;
        void Terminate()
        {
            bool terminated;
            try { terminated = terminate(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { terminated = false; }
            if (!terminated) terminationFailed = true;
        }

        try
        {
            byte[]? request = await ConnectVerifyAndBuildAsync(channel, owned, createRequest, budgets, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                Terminate();
                exitObserved = await ObserveExitAsync(owned, budgets.Exit).ConfigureAwait(false);
            }
            else
            {
                // From the first written byte the helper may act, so every later failure is "request written".
                outcome = ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse;
                using CancellationTokenSource executionTimeout = new(budgets.Execution);
                using CancellationTokenSource execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, executionTimeout.Token);
                try
                {
                    await PipeMessageFraming.WriteAsync(channel.Stream, request, execution.Token).ConfigureAwait(false);
                    byte[] received = await PipeMessageFraming.ReadAsync(channel.Stream, execution.Token).ConfigureAwait(false);
                    byte[] probe = new byte[1];
                    int trailing = await channel.Stream.ReadAsync(probe, execution.Token).ConfigureAwait(false);
                    ioComplete = true;
                    if (trailing == 0)
                    {
                        endOfFile = true;
                        frame = received;
                        outcome = ReviewedHelperTransportOutcome.ResponseFrameReceived;
                    }
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidDataException
                    or ObjectDisposedException or InvalidOperationException or UnauthorizedAccessException)
                {
                    // Lost, late, oversized, truncated or disconnected: no valid response.
                }

                if (outcome == ReviewedHelperTransportOutcome.ResponseFrameReceived)
                {
                    exitObserved = await ObserveExitAsync(owned, budgets.Exit).ConfigureAwait(false);
                    if (!exitObserved) Terminate();
                }
                else
                {
                    Terminate();
                    exitObserved = await ObserveExitAsync(owned, budgets.Exit).ConfigureAwait(false);
                }
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
        return new(outcome, frame, new ReviewedHelperDesktopObservation(owned.OwnedCompletion, exitObserved, TransportSealed: true,
            terminationFailed, ioComplete, endOfFile));
    }

    /// <summary>Returns the request to write, or null when nothing may be written (no connection, peer refused, cancelled).</summary>
    private static async Task<byte[]?> ConnectVerifyAndBuildAsync(IReviewedHelperChannel channel, HelperProcessWork owned,
        Func<byte[]> createRequest, ReviewedHelperBudgets budgets, CancellationToken cancellationToken)
    {
        using CancellationTokenSource connectionTimeout = new(budgets.Connection);
        using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionTimeout.Token);
        Task connect;
        try { connect = channel.WaitForConnectionAsync(connection.Token); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
        Task first = await Task.WhenAny(connect, owned.ExitObserved).ConfigureAwait(false);
        if (first != connect) await connection.CancelAsync().ConfigureAwait(false);
        try { await connect.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
        if (first != connect || cancellationToken.IsCancellationRequested) return null;

        bool verified;
        try { verified = channel.VerifyPeer(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { verified = false; }
        if (!verified || cancellationToken.IsCancellationRequested) return null;
        try { return createRequest(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
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

/// <summary>Launches the fixed helper with <c>runas</c>. Returns null when no process exists; <paramref name="elevationDeclined"/> marks UAC denial.</summary>
internal delegate Process? ReviewedHelperLauncher(string helperPath, string pipeName, string nonce, out bool elevationDeclined);

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; adapter gates §A.4 step 3 and reviewed fact extension rev 3 §4.1/§6: the Windows
/// protocol-2 transport for the single route <see cref="UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3"/>.
/// <para>
/// Pre-launch order: provider caller trust, fixed paths, initial launch trust, restricted per-request pipe,
/// <see cref="IHelperProtocolCapability.TryHold"/> (the binding set stays held), final launch trust, cancellation, then one
/// <c>runas</c> launch with <c>--protocol 2</c>. Any pre-launch failure is
/// <see cref="ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation"/> with no UAC prompt; UAC denial is
/// <see cref="ReviewedHelperTransportOutcome.ElevationDeclined"/>. After connection the peer check adds the A.4 step 3.4
/// file-identity comparison against the held helper image.
/// </para>
/// <para>
/// Binding lifetime (A-M2, review-16 L2): the binding is released only by <see cref="ReleaseWithoutHelper"/> when no helper
/// process exists, or by <see cref="ReleaseAfterOwnership"/> once the helper exit has been observed and the transport is
/// sealed. A helper that never exits keeps the binding held. The transport also keeps a strong reference until after seal.
/// </para>
/// No production composition constructs this type yet: the route is not admitted and the helper endpoint does not exist.
/// </summary>
internal sealed class WindowsReviewedHelperTransport : IReviewedHelperTransport
{
    internal const string PipeNamePrefix = "pcsteward-reviewed-";
    private static readonly ReviewedHelperDesktopObservation NoHelper =
        new(Task.CompletedTask, HelperExitObserved: false, TransportSealed: true, TerminationFailed: false, OwnedIoComplete: false, PipeEndOfFile: false);
    private readonly IHelperProtocolCapability capability;
    private readonly Func<HelperLaunchPaths?> resolvePaths;
    private readonly Func<HelperLaunchPaths, HelperLaunchTrustResult> validatePreLaunchTrust;
    private readonly Func<bool> providerCallerTrusted;
    private readonly ReviewedHelperLauncher launchHelper;
    private readonly ReviewedHelperBudgets budgets;

    internal WindowsReviewedHelperTransport(IHelperProtocolCapability capability) : this(capability,
        InstalledHelperLaunchTrust.ResolveFixedPaths, InstalledHelperLaunchTrust.Validate, ChocolateyCallerTrusted, LaunchHelper,
        ReviewedHelperBudgets.Production)
    {
    }

    internal WindowsReviewedHelperTransport(IHelperProtocolCapability capability, Func<HelperLaunchPaths?> resolvePaths,
        Func<HelperLaunchPaths, HelperLaunchTrustResult> validatePreLaunchTrust, Func<bool> providerCallerTrusted,
        ReviewedHelperLauncher launchHelper, ReviewedHelperBudgets budgets)
    {
        this.capability = capability ?? throw new ArgumentNullException(nameof(capability));
        this.resolvePaths = resolvePaths ?? throw new ArgumentNullException(nameof(resolvePaths));
        this.validatePreLaunchTrust = validatePreLaunchTrust ?? throw new ArgumentNullException(nameof(validatePreLaunchTrust));
        this.providerCallerTrusted = providerCallerTrusted ?? throw new ArgumentNullException(nameof(providerCallerTrusted));
        this.launchHelper = launchHelper ?? throw new ArgumentNullException(nameof(launchHelper));
        this.budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
    }

    public async Task<ReviewedHelperExchange> ExchangeAsync(ReviewedUpdateOperation operation, string nonce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!OperatingSystem.IsWindows() || !ReviewedUpdateProtocol2Interpreter.TryGetProfileDigest(operation, out _) ||
            !TryCreateRequest(operation, nonce, out _) || !Trusted(providerCallerTrusted))
            return Refused();
        HelperLaunchPaths? paths;
        try { paths = resolvePaths(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return Refused(); }
        if (paths is null || !LaunchTrusted(paths)) return Refused();
        string currentSid;
        int currentSession;
        try
        {
            currentSid = WindowsIdentity.GetCurrent().User?.Value ?? string.Empty;
            using Process current = Process.GetCurrentProcess();
            currentSession = current.SessionId;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return Refused(); }
        if (currentSid.Length == 0) return Refused();

        string pipeName = PipeNamePrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        NamedPipeServerStream pipe;
        try { pipe = RestrictedNamedPipe.Create(pipeName, currentSid); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return Refused(); }
        using (pipe)
        {
            HeldHelperBinding? binding;
            HelperProtocolCapabilityResult held;
            try { held = capability.TryHold(operation.RouteContract, out binding); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { return Refused(); }
            if (binding is null) return Refused();
            if (!held.IsSupported || !binding.IsHolding ||
                !string.Equals(Path.GetFullPath(binding.HelperPath), Path.GetFullPath(paths.HelperPath), StringComparison.OrdinalIgnoreCase) ||
                !LaunchTrusted(paths) || cancellationToken.IsCancellationRequested)
            {
                ReleaseWithoutHelper(binding);
                return Refused();
            }

            Process? helper;
            bool declined;
            try { helper = launchHelper(paths.HelperPath, pipeName, nonce, out declined); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { helper = null; declined = false; }
            if (helper is null)
            {
                ReleaseWithoutHelper(binding);
                return declined
                    ? new(ReviewedHelperTransportOutcome.ElevationDeclined, [], NoHelper)
                    : Refused();
            }

            HelperProcessWork owned = new(helper);
            ReleaseAfterOwnership(owned.OwnedCompletion, binding);
            WindowsReviewedHelperChannel channel = new(pipe, helper, paths, currentSession, currentSid, binding);
            ReviewedHelperExchange exchange = await ReviewedHelperExchangeSession.RunAsync(channel, owned, () => TryTerminate(helper),
                () => ReviewedUpdateWireCodec.CreateRequest(operation.GetCanonicalBytes(), operation.Digest, nonce, DateTimeOffset.UtcNow),
                budgets, cancellationToken).ConfigureAwait(false);
            // Strong reference until after seal (review-16 L2); release itself waits for the ownership task.
            GC.KeepAlive(binding);
            return exchange;
        }
    }

    /// <summary>No helper process exists, so nothing can load from the binding set: release it now.</summary>
    private static void ReleaseWithoutHelper(HeldHelperBinding binding) => binding.Dispose();

    /// <summary>
    /// Release once helper exit has been observed and the transport is sealed (the ownership task). If exit is never
    /// observed the ownership task never completes and the binding stays held (A-M2).
    /// </summary>
    private static void ReleaseAfterOwnership(Task ownedCompletion, HeldHelperBinding binding) =>
        _ = ownedCompletion.ContinueWith(static (_, state) => ((HeldHelperBinding)state!).Dispose(), binding,
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static ReviewedHelperExchange Refused() => new(ReviewedHelperTransportOutcome.LaunchRefusedBeforeElevation, [], NoHelper);

    private static bool TryCreateRequest(ReviewedUpdateOperation operation, string nonce, out byte[] request)
    {
        try
        {
            request = ReviewedUpdateWireCodec.CreateRequest(operation.GetCanonicalBytes(), operation.Digest, nonce, DateTimeOffset.UtcNow);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            request = [];
            return false;
        }
    }

    private bool LaunchTrusted(HelperLaunchPaths paths)
    {
        try { return validatePreLaunchTrust(paths).Trusted; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static bool Trusted(Func<bool> check)
    {
        try { return check(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return false; }
    }

    private static bool ChocolateyCallerTrusted()
    {
        try
        {
            ChocolateyMachineUpdateExecutor.ValidateDesktopCallerClientTrust();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or Win32Exception)
        {
            return false;
        }
    }

    private static Process? LaunchHelper(string helperPath, string pipeName, string nonce, out bool elevationDeclined)
    {
        elevationDeclined = false;
        ProcessStartInfo start = new() { FileName = helperPath, UseShellExecute = true, Verb = "runas" };
        foreach (string argument in new[]
        {
            "--protocol", ReviewedUpdateWireCodec.ProtocolVersion,
            "--desktop-pid", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
            "--pipe", pipeName,
            "--nonce", nonce,
        })
            start.ArgumentList.Add(argument);
        try { return Process.Start(start); }
        catch (Win32Exception exception) when (exception.NativeErrorCode == NativeMethods.ErrorCancelled)
        {
            elevationDeclined = true;
            return null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException) { return null; }
    }

    /// <summary>True once the helper is gone or termination was requested without error; false records a termination failure.</summary>
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

    private sealed class WindowsReviewedHelperChannel(NamedPipeServerStream pipe, Process helper, HelperLaunchPaths paths, int session,
        string sid, HeldHelperBinding binding) : IReviewedHelperChannel
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
