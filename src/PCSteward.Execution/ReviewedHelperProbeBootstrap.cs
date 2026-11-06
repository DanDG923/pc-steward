using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §2 (grammar, dispatch, mutual refusal) and §3 P0: the
/// helper bootstrap of the owner-started qualification probe. <see cref="TryParse"/> accepts exactly one ten-argument line, in
/// this one order, case-sensitive ordinal:
/// <c>--kind qualify-chocolatey-helper --protocol 2 --desktop-pid &lt;n&gt; --pipe &lt;pcsteward-reviewed-[a-f0-9]{32}&gt; --nonce
/// &lt;64 uppercase hex&gt;</c>. Any other length, order, key, kind value, case or whitespace refuses, so no update line (eight
/// arguments) and no permutation of the probe line is accepted. <see cref="RunAsync"/> runs after the entry point's Windows and
/// elevation checks: the host facts (21), the connection with <c>TokenImpersonationLevel.Identification</c> within 30 s (22),
/// the server PID check, the held peer handle through the probe port and the handle-based peer verification (23), then
/// <see cref="ReviewedHelperProbeEndpoint"/>, which never reads a request frame. P0 never writes a response.
/// </summary>
internal sealed record ReviewedHelperProbeBootstrap(int DesktopProcessId, string PipeName, string Nonce)
{
    /// <summary>§2: the fixed literal pair at indices 0–1 selects the probe; the value is the probe codec's kind literal.</summary>
    internal const string KindKey = "--kind";

    /// <summary>§3 P1: an inbound byte stopped the run; no response was written.</summary>
    internal const int InboundByteReceived = 27;

    private const int ArgumentCount = 10;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>§2 dispatch step 1: exactly ten arguments with <c>--kind</c> first; the full parse decides afterwards.</summary>
    internal static bool SelectsProbe(string[]? arguments) =>
        arguments is { Length: ArgumentCount } && string.Equals(arguments[0], KindKey, StringComparison.Ordinal);

    internal static bool TryParse(string[] arguments, out ReviewedHelperProbeBootstrap? bootstrap)
    {
        bootstrap = null;
        if (arguments is null || arguments.Length != ArgumentCount || arguments.Any(static argument => argument is null)) return false;
        if (!string.Equals(arguments[0], KindKey, StringComparison.Ordinal) ||
            !string.Equals(arguments[1], QualificationProbeWireCodec.Kind, StringComparison.Ordinal) ||
            !string.Equals(arguments[2], "--protocol", StringComparison.Ordinal) ||
            !string.Equals(arguments[3], ReviewedUpdateWireCodec.ProtocolVersion, StringComparison.Ordinal) ||
            !string.Equals(arguments[4], "--desktop-pid", StringComparison.Ordinal) ||
            !string.Equals(arguments[6], "--pipe", StringComparison.Ordinal) ||
            !string.Equals(arguments[8], "--nonce", StringComparison.Ordinal))
            return false;
        if (!int.TryParse(arguments[5], NumberStyles.None, CultureInfo.InvariantCulture, out int desktopPid) || desktopPid <= 0 ||
            !WindowsReviewedHelperTransport.PipeNamePattern().IsMatch(arguments[7]) ||
            !IsUpperHex64(arguments[9]))
            return false;
        bootstrap = new ReviewedHelperProbeBootstrap(desktopPid, arguments[7], arguments[9]);
        return true;
    }

    /// <summary>
    /// The connected part of the probe bootstrap (§3 P0 after the parse): the caller has parsed the command line and checked
    /// Windows and elevation. A null <paramref name="host"/> is <see cref="ReviewedHelperBootstrap.LayoutUnavailable"/> before
    /// any connection. The peer handle stays open until the probe entry has finished (the entry disposes its item first).
    /// </summary>
    internal static async Task<int> RunAsync(ReviewedHelperProbeBootstrap bootstrap, ReviewedHelperHost? host, IChocolateyProbePort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        if (host is null || !OperatingSystem.IsWindows()) return ReviewedHelperBootstrap.LayoutUnavailable;
        try
        {
            using NamedPipeClientStream pipe = new(".", bootstrap.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            using (CancellationTokenSource connection = new(ConnectionTimeout))
            {
                try { await pipe.ConnectAsync(connection.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException or TimeoutException)
                {
                    return ReviewedHelperBootstrap.ConnectionFailed;
                }
            }
            // §3: t0 is taken at pipe connection.
            long connected = clock.GetTimestamp();
            using IChocolateyPeer? peer = OpenVerifiedProbePeer(pipe, bootstrap, host, port);
            if (peer is null) return ReviewedHelperBootstrap.PeerNotVerified;
            ReviewedHelperProbeResult result = await ReviewedHelperProbeEndpoint.RunAsync(pipe, peer, port, capability, clock, connected,
                host.UserSid, bootstrap.Nonce, CancellationToken.None).ConfigureAwait(false);
            return ExitCode(result.Exit);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ReviewedHelperBootstrap.ResponseWriteFailed;
        }
    }

    /// <summary>§3: written 0; no response 24; an inbound byte 27; a failed or late write 26.</summary>
    internal static int ExitCode(ReviewedHelperProbeExit exit) => exit switch
    {
        ReviewedHelperProbeExit.ResponseWritten => 0,
        ReviewedHelperProbeExit.NoResponse => ReviewedHelperBootstrap.NoResponse,
        ReviewedHelperProbeExit.InboundByte => InboundByteReceived,
        _ => ReviewedHelperBootstrap.ResponseWriteFailed,
    };

    /// <summary>
    /// §3 P0 (review M2): the server PID check, the held peer handle through the <b>probe</b> port and the handle-based peer
    /// verification, the same calls as the update bootstrap's own peer check but typed on <see cref="IChocolateyProbePort"/>.
    /// Null on any refusal or exception; a peer opened but not verified is closed.
    /// </summary>
    private static IChocolateyPeer? OpenVerifiedProbePeer(NamedPipeClientStream pipe, ReviewedHelperProbeBootstrap bootstrap, ReviewedHelperHost host,
        IChocolateyProbePort port)
    {
        IChocolateyPeer? peer = null;
        try
        {
            if (!RestrictedNamedPipe.TryGetServerProcessId(pipe, out int serverProcessId) || serverProcessId != bootstrap.DesktopProcessId) return null;
            peer = port.OpenPeer(serverProcessId);
            if (peer is not null &&
                WindowsPeerIdentityVerifier.VerifyPeer(peer, serverProcessId, host.ExpectedDesktopPath, host.HelperPath, host.Session, host.UserSid, out _))
                return peer;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Refused below.
        }
        try { peer?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        return null;
    }

    private static bool IsUpperHex64(string value) => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>
/// Probe spec rev 4 §2 and §7 H5: the ElevatedHelper composition root for the probe. It constructs only the native probe port,
/// the Helper-role capability check and the system clock. A refused parse, a non-Windows host or a non-elevated process exits
/// with code 20 before any connection, as both existing paths do.
/// </summary>
public static partial class ElevatedHelperEntryPoint
{
    internal static bool SelectsProbe(string[] arguments) => ReviewedHelperProbeBootstrap.SelectsProbe(arguments);

    private static Task<int> RunProbeAsync(string[] arguments)
    {
        if (!OperatingSystem.IsWindows() || !ReviewedHelperProbeBootstrap.TryParse(arguments, out ReviewedHelperProbeBootstrap? bootstrap) ||
            bootstrap is null || !IsElevated())
            return Task.FromResult(20);
        return ReviewedHelperProbeBootstrap.RunAsync(bootstrap, ReviewedHelperHost.TryResolve(), new NativeChocolateyProbePort(),
            new HelperDeploymentCapabilityCheck(), TimeProvider.System);
    }
}
