using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Principal;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §2.1 (H3), §4 (t0) and §5.1: the protocol-2 helper bootstrap.
/// <see cref="TryParse"/> accepts exactly <c>--protocol 2 --desktop-pid &lt;n&gt; --pipe &lt;pcsteward-reviewed-[a-f0-9]{32}&gt;
/// --nonce &lt;64 uppercase hex&gt;</c>, each key once, in any order; the protocol-1 locator, a lowercase or short nonce, a
/// non-positive or signed PID, an unknown or repeated key, or any other count refuses. <see cref="RunAsync"/> runs after the
/// entry point's elevation check: connect with <c>TokenImpersonationLevel.Identification</c> (30 s), take t0 at connection,
/// require the pipe server PID to equal the desktop PID, open the held peer handle through the port, verify the peer through
/// that handle (fixed desktop path, same session, same SID, trusted same signer), then run
/// <see cref="ReviewedHelperEndpoint"/> with the port, the capability check and the clock. The protocol-1 path never
/// references this type.
/// </summary>
internal sealed record ReviewedHelperBootstrap(int DesktopProcessId, string PipeName, string Nonce)
{
    /// <summary>Exit codes after a parsed, elevated bootstrap (20 is the entry point's refusal before any connection).</summary>
    internal const int LayoutUnavailable = 21;
    internal const int ConnectionFailed = 22;
    internal const int PeerNotVerified = 23;
    internal const int NoResponse = 24;
    internal const int ResponseWriteFailed = 26;

    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] Keys = ["--protocol", "--desktop-pid", "--pipe", "--nonce"];

    internal static bool TryParse(string[] arguments, out ReviewedHelperBootstrap? bootstrap)
    {
        bootstrap = null;
        if (arguments is null || arguments.Length != 8) return false;
        Dictionary<string, string> fields = new(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (arguments[index] is not { } key || !Keys.Contains(key, StringComparer.Ordinal) || arguments[index + 1] is not { } value ||
                !fields.TryAdd(key, value))
                return false;
        }
        if (fields.Count != Keys.Length ||
            !string.Equals(fields["--protocol"], ReviewedUpdateWireCodec.ProtocolVersion, StringComparison.Ordinal) ||
            !int.TryParse(fields["--desktop-pid"], NumberStyles.None, CultureInfo.InvariantCulture, out int desktopPid) || desktopPid <= 0 ||
            !WindowsReviewedHelperTransport.PipeNamePattern().IsMatch(fields["--pipe"]) ||
            !IsUpperHex64(fields["--nonce"]))
            return false;
        bootstrap = new ReviewedHelperBootstrap(desktopPid, fields["--pipe"], fields["--nonce"]);
        return true;
    }

    /// <summary>
    /// The connected part of the protocol-2 bootstrap; the caller has already parsed the command line and checked elevation.
    /// The peer handle stays open until the endpoint has finished (the endpoint disposes its item first).
    /// </summary>
    internal static async Task<int> RunAsync(ReviewedHelperBootstrap bootstrap, IChocolateyHelperQualificationPort port,
        IReviewedHelperCapabilityCheck capability, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        if (!OperatingSystem.IsWindows()) return LayoutUnavailable;
        string helperPath;
        string expectedDesktopPath;
        string currentSid;
        int currentSession;
        try
        {
            helperPath = Environment.ProcessPath ?? string.Empty;
            expectedDesktopPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, InstalledHelperLaunchTrust.DesktopExecutableName));
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent()) currentSid = identity.User?.Value ?? string.Empty;
            using (Process current = Process.GetCurrentProcess()) currentSession = current.SessionId;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return LayoutUnavailable; }
        if (helperPath.Length == 0 || currentSid.Length == 0 || !File.Exists(expectedDesktopPath)) return LayoutUnavailable;

        try
        {
            using NamedPipeClientStream pipe = new(".", bootstrap.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);
            using (CancellationTokenSource connection = new(ConnectionTimeout))
            {
                try { await pipe.ConnectAsync(connection.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException or TimeoutException)
                {
                    return ConnectionFailed;
                }
            }
            // §4: the endpoint clock starts when the pipe connects, before the frame read.
            long connected = clock.GetTimestamp();
            if (!RestrictedNamedPipe.TryGetServerProcessId(pipe, out int serverProcessId) || serverProcessId != bootstrap.DesktopProcessId)
                return PeerNotVerified;
            using IChocolateyPeer? peer = port.OpenPeer(serverProcessId);
            if (peer is null ||
                !WindowsPeerIdentityVerifier.VerifyPeer(peer, serverProcessId, expectedDesktopPath, helperPath, currentSession, currentSid, out _))
                return PeerNotVerified;
            ReviewedHelperEndpointResult result = await ReviewedHelperEndpoint.RunAsync(pipe, peer, port, capability, clock, connected,
                bootstrap.Nonce, CancellationToken.None).ConfigureAwait(false);
            return result.Exit switch
            {
                ReviewedHelperEndpointExit.ResponseWritten => 0,
                ReviewedHelperEndpointExit.NoResponse => NoResponse,
                _ => ResponseWriteFailed,
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ResponseWriteFailed;
        }
    }

    /// <summary>The dispatch rule: an eight-argument command line naming <c>--protocol 2</c>; the full parse decides afterwards.</summary>
    internal static bool SelectsProtocol2(string[]? arguments)
    {
        if (arguments is null || arguments.Length != 8) return false;
        for (int index = 0; index < arguments.Length; index += 2)
            if (string.Equals(arguments[index], "--protocol", StringComparison.Ordinal) &&
                string.Equals(arguments[index + 1], ReviewedUpdateWireCodec.ProtocolVersion, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool IsUpperHex64(string value) => value.Length == 64 && value.All(static c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>
/// The ElevatedHelper composition root for protocol 2 (Task 50 design rev 6 §5.1, §5.4): it constructs only the native
/// qualification port, the Helper-role capability check and the system clock. A refused parse, a non-Windows host or a
/// non-elevated process exits with code 20 before any connection, as protocol 1 does.
/// </summary>
public static partial class ElevatedHelperEntryPoint
{
    internal static bool SelectsProtocol2(string[] arguments) => ReviewedHelperBootstrap.SelectsProtocol2(arguments);

    private static Task<int> RunReviewedAsync(string[] arguments)
    {
        if (!OperatingSystem.IsWindows() || !ReviewedHelperBootstrap.TryParse(arguments, out ReviewedHelperBootstrap? bootstrap) ||
            bootstrap is null || !IsElevated())
            return Task.FromResult(20);
        return ReviewedHelperBootstrap.RunAsync(bootstrap, new NativeChocolateyHelperQualification(), new HelperDeploymentCapabilityCheck(),
            TimeProvider.System);
    }
}

/// <summary>Task 50 design rev 6 §2.1 (H3): the handle-based peer verification protocol 2 uses; protocol 1 keeps the PID overload.</summary>
internal static partial class WindowsPeerIdentityVerifier
{
    /// <summary>
    /// Every identity fact comes from the held peer handle: its process ID must equal the pipe server PID, it must still be
    /// running, and its image path (fixed desktop path), session and user SID must match; then the image must be trusted
    /// and carry the same signer as <paramref name="ownPath"/>. Any failure or exception refuses.
    /// </summary>
    internal static bool VerifyPeer(IChocolateyPeer peer, int pipeServerProcessId, string expectedPath, string ownPath, int expectedSession,
        string expectedUserSid, out string rejection)
    {
        rejection = "peer-identity-invalid";
        try
        {
            if (peer is null || pipeServerProcessId <= 0 || peer.ProcessId != pipeServerProcessId || peer.HasExited ||
                !peer.TryReadIdentity(out string actualPath, out int session, out string userSid) ||
                string.IsNullOrEmpty(actualPath) || !Path.IsPathFullyQualified(actualPath) ||
                !string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase) ||
                session != expectedSession ||
                !string.Equals(userSid, expectedUserSid, StringComparison.Ordinal))
                return false;
            if (!AuthenticodeIdentity.IsTrustedAndSameSigner(actualPath, ownPath))
            {
                rejection = "peer-signature-invalid";
                return false;
            }
            rejection = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            rejection = "peer-identity-invalid";
            return false;
        }
    }
}
