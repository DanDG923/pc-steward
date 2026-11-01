namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §2.1 (H2, H3), §5.1 and §5.4 (N4b): the single native
/// implementation of the helper qualification port. <see cref="OpenPeer"/> opens the held peer handle
/// (<see cref="PeerProcess"/>) for the pipe server process; <see cref="BeginItem(IChocolateyPeer, string, out ChocolateyItemRefusal)"/>
/// acquires the requester evaluation token under the §2.1 linked-token rules (<see cref="RequesterToken.TryForVerifiedPeer"/>)
/// from that same handle and hands it to a new <see cref="NativeChocolateyHelperItem"/>, which owns it. A peer that this
/// port did not open, a missing SID, an exited peer or any token refusal is refused; every uncertainty refuses. The
/// ElevatedHelper composition root constructs only this port (architecture test).
/// </summary>
internal sealed class NativeChocolateyHelperQualification : IChocolateyHelperQualificationPort
{
    public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => NativeChocolateyPeer.TryOpen(pipeServerProcessId);

    public IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal) =>
        BeginItemWithVerdict(peer, expectedSid, out refusal, out _);

    /// <summary>As the port method, also returning the specific token verdict (diagnostics and Windows CI refusal evidence).</summary>
    internal static IChocolateyHelperItem? BeginItemWithVerdict(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal,
        out TokenVerdict verdict)
    {
        refusal = ChocolateyItemRefusal.RequesterTokenRefused;
        verdict = TokenVerdict.EvaluationTokenUnreadable;
        if (peer is not NativeChocolateyPeer native || string.IsNullOrEmpty(expectedSid)) return null;
        if (native.HasExited)
        {
            refusal = ChocolateyItemRefusal.PeerExited;
            verdict = TokenVerdict.PeerExited;
            return null;
        }
        RequesterToken? token = native.TryAcquireRequesterToken(expectedSid, out verdict);
        if (token is null)
        {
            refusal = verdict == TokenVerdict.PeerExited ? ChocolateyItemRefusal.PeerExited : ChocolateyItemRefusal.RequesterTokenRefused;
            return null;
        }
        refusal = ChocolateyItemRefusal.None;
        return NativeChocolateyHelperItem.Create(token);
    }
}

/// <summary>
/// The held peer handle (H3) behind <see cref="IChocolateyPeer"/>: one <see cref="PeerProcess"/> opened once for the pipe
/// server process ID and kept for the whole request. Every identity fact and the requester token come from that handle.
/// Private construction; only <see cref="TryOpen"/> (called by the native port) creates one.
/// </summary>
internal sealed class NativeChocolateyPeer : IChocolateyPeer
{
    private readonly PeerProcess process;
    private readonly int pipeServerProcessId;

    private NativeChocolateyPeer(PeerProcess process, int pipeServerProcessId)
    {
        this.process = process;
        this.pipeServerProcessId = pipeServerProcessId;
    }

    public int ProcessId => process.ProcessId;

    public bool HasExited => process.HasExited;

    public bool TryReadIdentity(out string imagePath, out int session, out string userSid) =>
        process.TryReadIdentity(out imagePath, out session, out userSid);

    /// <summary>Port only (architecture test): opens the held handle; null on any failure.</summary>
    internal static NativeChocolateyPeer? TryOpen(int pipeServerProcessId)
    {
        if (!OperatingSystem.IsWindows() || pipeServerProcessId <= 0) return null;
        PeerProcess? process = PeerProcess.TryOpen(pipeServerProcessId);
        return process is null ? null : new NativeChocolateyPeer(process, pipeServerProcessId);
    }

    /// <summary>§2.1: the helper's linked token, accepted only when it matches this held peer and the expected SID.</summary>
    internal RequesterToken? TryAcquireRequesterToken(string expectedSid, out TokenVerdict verdict) =>
        RequesterToken.TryForVerifiedPeer(process, pipeServerProcessId, expectedSid, out verdict);

    public void Dispose() => process.Dispose();
}
