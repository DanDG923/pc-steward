using System.IO.Pipes;
using System.Security.Principal;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Execution;

/// <summary>
/// FR-UPD-15, TM-03/04/13; ADR-032 revision 2 §6 step 1: the connected part of the elevated self-update session, after the
/// entry point has parsed the session line and checked Windows and elevation. It connects with
/// <c>TokenImpersonationLevel.Identification</c> within 30 s (22), applies the protocol-2 peer checks through a held peer
/// handle: server PID equal to the desktop PID, fixed desktop path, same session, same SID, trusted same signer (23), takes
/// the requester token as the protection principal for the install check (23 when refused), then runs
/// <see cref="SelfUpdateSession"/>. Exit codes follow protocol 2: 0 written, 24 no response, 26 a failed write.
/// </summary>
internal static class SelfUpdateHelperBootstrap
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);

    internal static async Task<int> RunAsync(int desktopProcessId, string pipeName, string nonce, ReviewedHelperHost? host,
        IChocolateyHelperQualificationPort port, ISelfUpdateInstallCheck installCheck, ISelfUpdateSessionHost machine, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(installCheck);
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(clock);
        if (host is null || !OperatingSystem.IsWindows()) return ReviewedHelperBootstrap.LayoutUnavailable;
        try
        {
            using NamedPipeClientStream pipe = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            using (CancellationTokenSource connection = new(ConnectionTimeout))
            {
                try { await pipe.ConnectAsync(connection.Token).ConfigureAwait(false); }
                catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException or TimeoutException)
                {
                    return ReviewedHelperBootstrap.ConnectionFailed;
                }
            }
            long connected = clock.GetTimestamp();
            using IChocolateyPeer? peer = OpenVerifiedPeer(pipe, desktopProcessId, host, port);
            if (peer is null || !peer.TryReadIdentity(out string peerImage, out _, out _) || string.IsNullOrEmpty(peerImage))
                return ReviewedHelperBootstrap.PeerNotVerified;
            using IChocolateyHelperItem? item = port.BeginItem(peer, host.UserSid, out _);
            if (item is null) return ReviewedHelperBootstrap.PeerNotVerified;
            SelfUpdatePeer verified = new(desktopProcessId, host.UserSid, peerImage, (root, files) => item.RequesterCannotModify(root, files));
            SelfUpdateSessionResult result = await SelfUpdateSession.RunAsync(pipe, nonce, verified, installCheck, machine, clock, connected,
                CancellationToken.None).ConfigureAwait(false);
            return result.Exit switch
            {
                SelfUpdateSessionExit.ResponseWritten => 0,
                SelfUpdateSessionExit.NoResponse => ReviewedHelperBootstrap.NoResponse,
                _ => ReviewedHelperBootstrap.ResponseWriteFailed,
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return ReviewedHelperBootstrap.ResponseWriteFailed;
        }
    }

    /// <summary>The protocol-2 peer checks (server PID, held handle, handle-based identity and signer). Null on any refusal.</summary>
    private static IChocolateyPeer? OpenVerifiedPeer(NamedPipeClientStream pipe, int desktopProcessId, ReviewedHelperHost host, IChocolateyHelperQualificationPort port)
    {
        IChocolateyPeer? peer = null;
        try
        {
            if (!RestrictedNamedPipe.TryGetServerProcessId(pipe, out int serverProcessId) || serverProcessId != desktopProcessId) return null;
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
}

/// <summary>
/// ADR-032 revision 2 §6–§7: the ElevatedHelper composition roots of PC Steward's own update. The session constructs only
/// the native qualification port (for the peer handle and requester token), the Helper-role install check, the Windows host
/// and the system clock; the finish copy only the Windows host. A refused parse or a non-Windows host exits with code 20
/// before anything is read; the session also refuses a non-elevated process with 20, and the finish copy reports it the
/// same way.
/// </summary>
public static partial class ElevatedHelperEntryPoint
{
    internal static bool SelectsSelfUpdate(string[] arguments) => SelfUpdateHelperProtocol.SelectsSession(arguments);

    internal static bool SelectsSelfUpdateFinish(string[] arguments) => SelfUpdateHelperProtocol.SelectsFinish(arguments);

    private static Task<int> RunSelfUpdateAsync(string[] arguments)
    {
        if (!OperatingSystem.IsWindows() ||
            !SelfUpdateHelperProtocol.TryParseSession(arguments, out int desktopProcessId, out string pipeName, out string nonce) || !IsElevated())
            return Task.FromResult(20);
        WindowsSelfUpdateHost? machine = WindowsSelfUpdateHost.TryCreate();
        if (machine is null) return Task.FromResult(ReviewedHelperBootstrap.LayoutUnavailable);
        return SelfUpdateHelperBootstrap.RunAsync(desktopProcessId, pipeName, nonce, ReviewedHelperHost.TryResolve(), new NativeChocolateyHelperQualification(),
            new HelperDeploymentCapabilityCheck(), machine, TimeProvider.System);
    }

    private static Task<int> RunSelfUpdateFinishAsync(string[] arguments)
    {
        if (!OperatingSystem.IsWindows() || !SelfUpdateHelperProtocol.TryParseFinish(arguments, out string leaf)) return Task.FromResult(20);
        WindowsSelfUpdateHost? machine = WindowsSelfUpdateHost.TryCreate();
        if (machine is null) return Task.FromResult(SelfUpdateFinish.Refused);
        return SelfUpdateFinish.RunAsync(leaf, IsElevated(), machine, CancellationToken.None);
    }
}
