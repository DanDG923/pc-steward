using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/13; Task 50 design rev 6 §2.1 (H3): one handle to the desktop peer, opened once immediately after
/// the pipe connects and held for the whole request. The open handle prevents PID reuse, and every identity fact
/// (image path, session, user SID, token) is read from it, never by process ID.
/// </summary>
internal sealed class PeerProcess : IDisposable
{
    private readonly SafeProcessHandle handle;

    private PeerProcess(SafeProcessHandle handle, int processId)
    {
        this.handle = handle;
        ProcessId = processId;
    }

    /// <summary><c>GetProcessId</c> of the held handle, read once at open.</summary>
    internal int ProcessId { get; }

    /// <summary>True when the process has exited or its state cannot be read (fail closed).</summary>
    internal bool HasExited
    {
        get
        {
            if (handle.IsClosed || handle.IsInvalid) return true;
            return Win32.WaitForSingleObject(handle, 0) != Win32.WaitTimeout;
        }
    }

    /// <summary>
    /// Opens <paramref name="processId"/> with <c>PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE</c>; null when the open
    /// fails or the handle's process ID differs from the requested one.
    /// </summary>
    internal static PeerProcess? TryOpen(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return null;
        SafeProcessHandle opened = Win32.OpenProcess(Win32.ProcessQueryLimitedInformation | Win32.Synchronize, false, processId);
        if (opened.IsInvalid)
        {
            opened.Dispose();
            return null;
        }
        uint id = Win32.GetProcessId(opened);
        if (id == 0 || id != (uint)processId)
        {
            opened.Dispose();
            return null;
        }
        return new PeerProcess(opened, processId);
    }

    /// <summary>Image path, session and user SID, all read from the held handle and its token.</summary>
    internal bool TryReadIdentity(out string imagePath, out int session, out string userSid)
    {
        imagePath = userSid = string.Empty;
        session = -1;
        if (handle.IsClosed || handle.IsInvalid) return false;
        char[] buffer = new char[32768];
        uint length = (uint)buffer.Length;
        if (!Win32.QueryFullProcessImageName(handle, 0, buffer, ref length) || length == 0 || length >= buffer.Length) return false;
        using SafeAccessTokenHandle? token = TryOpenToken();
        if (token is null || !NativeTokenReader.TryReadSessionId(token, out int sessionId) ||
            !NativeTokenReader.TryReadUserSid(token, out string sid))
            return false;
        imagePath = new string(buffer, 0, (int)length);
        session = sessionId;
        userSid = sid;
        return true;
    }

    /// <summary>The peer's primary token with <c>TOKEN_QUERY</c> only; null on failure.</summary>
    internal SafeAccessTokenHandle? TryOpenToken()
    {
        if (handle.IsClosed || handle.IsInvalid) return null;
        if (Win32.OpenProcessToken(handle, Win32.TokenQuery, out SafeAccessTokenHandle token) && !token.IsInvalid) return token;
        token.Dispose();
        return null;
    }

    public void Dispose() => handle.Dispose();
}
