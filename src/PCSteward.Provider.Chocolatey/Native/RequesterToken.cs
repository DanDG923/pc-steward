using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.1 (H2, L3, M5): the requester's identification-level token, used
/// only as the <c>AccessCheck</c> client token, for SID/IL/LUID comparison and for <c>GetUserProfileDirectory</c>. It is
/// never used to impersonate for I/O, never used to start a process and never persisted, and no input comes from a
/// payload. Construction is private: only the native factories below can produce one, and the production factories
/// accept a token only when <see cref="RequesterTokenPolicy.Validate"/> returns <see cref="TokenVerdict.Accepted"/>.
/// </summary>
internal sealed class RequesterToken : IDisposable
{
    private const string MediumIntegritySid = "S-1-16-8192";

    private RequesterToken(SafeAccessTokenHandle handle, TokenFacts facts, IReadOnlySet<string> ownerSids)
    {
        Handle = handle;
        Facts = facts;
        OwnerSids = ownerSids;
    }

    internal string Sid => Facts.UserSid;

    internal TokenFacts Facts { get; }

    /// <summary>The user SID, the logon SID and every enabled, non-deny-only group SID (L4).</summary>
    internal IReadOnlySet<string> OwnerSids { get; }

    /// <summary>Identification-level impersonation token with which <c>AccessCheck</c> evaluates the requester.</summary>
    internal SafeAccessTokenHandle Handle { get; }

    /// <summary>
    /// Helper role: the helper's own <c>TokenLinkedToken</c> (the filtered Medium token of the same logon), accepted only
    /// when it matches the verified desktop peer. Order: the held peer handle's PID must equal
    /// <paramref name="pipeProcessId"/>; the peer must be running; the peer token is read from the held handle; the
    /// helper token must have a linked token at identification level; the peer must still be running after the token was
    /// obtained; then the pure policy decides.
    /// </summary>
    internal static RequesterToken? TryForVerifiedPeer(PeerProcess peer, int pipeProcessId, string expectedSid, out TokenVerdict verdict)
    {
        ArgumentNullException.ThrowIfNull(peer);
        verdict = TokenVerdict.EvaluationTokenUnreadable;
        if (!OperatingSystem.IsWindows()) return null;
        if (pipeProcessId <= 0 || peer.ProcessId != pipeProcessId)
        {
            verdict = TokenVerdict.PeerProcessIdMismatch;
            return null;
        }
        if (peer.HasExited)
        {
            verdict = TokenVerdict.PeerExited;
            return null;
        }

        TokenFacts peerFacts;
        using (SafeAccessTokenHandle? peerToken = peer.TryOpenToken())
        {
            if (peerToken is null || !NativeTokenReader.TryReadFacts(peerToken, out peerFacts, out _))
            {
                verdict = TokenVerdict.PeerTokenUnreadable;
                return null;
            }
        }

        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TokenQuery, out SafeAccessTokenHandle own) || own.IsInvalid)
        {
            own.Dispose();
            return null;
        }
        SafeAccessTokenHandle? linked;
        using (own)
        {
            if (!NativeTokenReader.TryReadInt32(own, Win32.TokenElevationTypeClass, out int ownElevation)) return null;
            // Elevation type Default means no split token exists (UAC off, or not an administrator): nothing is linked.
            if (ownElevation == (int)TokenElevationKind.Default)
            {
                verdict = TokenVerdict.NoLinkedToken;
                return null;
            }
            linked = NativeTokenReader.TryGetLinkedToken(own);
        }
        if (linked is null)
        {
            verdict = TokenVerdict.NoLinkedToken;
            return null;
        }
        return Admit(linked, peerFacts, expectedSid, peer, out verdict);
    }

    /// <summary>
    /// Desktop role: the current process token, duplicated to an identification-level impersonation token with
    /// <c>TOKEN_QUERY</c> only (L3), which must satisfy the same rules as both peer and evaluation token.
    /// </summary>
    internal static RequesterToken? TryFromCurrentProcess(out TokenVerdict verdict)
    {
        verdict = TokenVerdict.EvaluationTokenUnreadable;
        if (!OperatingSystem.IsWindows()) return null;
        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TokenQuery | Win32.TokenDuplicate, out SafeAccessTokenHandle own) ||
            own.IsInvalid)
        {
            own.Dispose();
            return null;
        }
        SafeAccessTokenHandle identification;
        using (own)
        {
            if (!Win32.DuplicateTokenEx(own, Win32.TokenQuery, IntPtr.Zero, Win32.SecurityIdentification, Win32.TokenTypeImpersonation,
                    out identification) || identification.IsInvalid)
            {
                identification.Dispose();
                return null;
            }
        }
        if (!NativeTokenReader.IsIdentificationImpersonation(identification) ||
            !NativeTokenReader.TryReadFacts(identification, out TokenFacts facts, out IReadOnlySet<string> owners))
        {
            identification.Dispose();
            return null;
        }
        verdict = RequesterTokenPolicy.Validate(facts, facts, facts.UserSid);
        if (verdict == TokenVerdict.Accepted) return new RequesterToken(identification, facts, owners);
        identification.Dispose();
        return null;
    }

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): derives an evaluation token from the current process
    /// token with <c>CreateRestrictedToken(LUA_TOKEN)</c>, which also deletes every privilege outside the allow-list and
    /// makes the logon SID deny-only, sets exactly Medium integrity, and duplicates it to identification level. It bypasses
    /// <see cref="RequesterTokenPolicy.Validate"/>, so callers must assert the shape through <see cref="Facts"/>. The
    /// deny-only logon SID lets a fixture grant its own cleanup rights to the logon SID without crediting them to this
    /// token: on a standard-user runner <c>LUA_TOKEN</c> alone changes nothing, and the runner cannot set a SYSTEM owner.
    /// </summary>
    internal static RequesterToken ForTestsFromDerivedToken()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Requester tokens exist only on Windows.");
        const uint Access = Win32.TokenQuery | Win32.TokenDuplicate | Win32.TokenAdjustDefault | Win32.TokenAssignPrimary;
        if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Access, out SafeAccessTokenHandle own)) throw new Win32Exception();
        using (own)
        {
            if (!NativeTokenReader.TryReadGroups(own, out IReadOnlyList<NativeTokenGroup> groups) ||
                !NativeTokenReader.TryReadPrivileges(own, out IReadOnlyList<(Win32.Luid Luid, string Name)> privileges))
                throw new InvalidOperationException("The current process token could not be read.");
            string[] logonSids = [.. groups.Where(group => (group.Attributes & Win32.SeGroupLogonId) == Win32.SeGroupLogonId).Select(group => group.Sid)];
            if (logonSids.Length == 0) throw new InvalidOperationException("The current process token has no logon SID.");
            Win32.LuidAndAttributes[] delete = [.. privileges
                .Where(privilege => !RequesterTokenPolicy.AllowedPrivileges.Contains(privilege.Name))
                .Select(privilege => new Win32.LuidAndAttributes { Luid = privilege.Luid })];
            List<IntPtr> allocated = [];
            try
            {
                Win32.SidAndAttributes[] disable = [.. logonSids.Select(sid => new Win32.SidAndAttributes { Sid = Allocate(sid, allocated) })];
                if (!Win32.CreateRestrictedToken(own, Win32.LuaToken, (uint)disable.Length, disable, (uint)delete.Length,
                        delete.Length == 0 ? null : delete, 0, IntPtr.Zero, out SafeAccessTokenHandle restricted))
                    throw new Win32Exception();
                using (restricted)
                {
                    SetIntegrity(restricted, MediumIntegritySid);
                    if (!Win32.DuplicateTokenEx(restricted, Win32.TokenQuery, IntPtr.Zero, Win32.SecurityIdentification,
                            Win32.TokenTypeImpersonation, out SafeAccessTokenHandle identification))
                        throw new Win32Exception();
                    if (!NativeTokenReader.IsIdentificationImpersonation(identification) ||
                        !NativeTokenReader.TryReadFacts(identification, out TokenFacts facts, out IReadOnlySet<string> owners))
                    {
                        identification.Dispose();
                        throw new InvalidOperationException("The derived token could not be read.");
                    }
                    return new RequesterToken(identification, facts, owners);
                }
            }
            finally
            {
                foreach (IntPtr pointer in allocated) Marshal.FreeHGlobal(pointer);
            }
        }
    }

    public void Dispose() => Handle.Dispose();

    private static RequesterToken? Admit(SafeAccessTokenHandle linked, TokenFacts peerFacts, string expectedSid, PeerProcess peer,
        out TokenVerdict verdict)
    {
        verdict = TokenVerdict.EvaluationTokenUnreadable;
        bool keep = false;
        try
        {
            if (!NativeTokenReader.IsIdentificationImpersonation(linked))
            {
                verdict = TokenVerdict.EvaluationNotIdentificationLevel;
                return null;
            }
            if (!NativeTokenReader.TryReadFacts(linked, out TokenFacts evaluation, out IReadOnlySet<string> owners)) return null;
            if (peer.HasExited)
            {
                verdict = TokenVerdict.PeerExited;
                return null;
            }
            verdict = RequesterTokenPolicy.Validate(peerFacts, evaluation, expectedSid);
            if (verdict != TokenVerdict.Accepted) return null;
            keep = true;
            return new RequesterToken(linked, evaluation, owners);
        }
        finally
        {
            if (!keep) linked.Dispose();
        }
    }

    private static IntPtr Allocate(string sid, List<IntPtr> allocated)
    {
        SecurityIdentifier identifier = new(sid);
        byte[] binary = new byte[identifier.BinaryLength];
        identifier.GetBinaryForm(binary, 0);
        IntPtr pointer = Marshal.AllocHGlobal(binary.Length);
        allocated.Add(pointer);
        Marshal.Copy(binary, 0, pointer, binary.Length);
        return pointer;
    }

    private static void SetIntegrity(SafeAccessTokenHandle token, string labelSid)
    {
        SecurityIdentifier identifier = new(labelSid);
        byte[] binary = new byte[identifier.BinaryLength];
        identifier.GetBinaryForm(binary, 0);
        int header = IntPtr.Size * 2; // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label }
        IntPtr buffer = Marshal.AllocHGlobal(header + binary.Length);
        try
        {
            Marshal.WriteIntPtr(buffer, buffer + header);
            Marshal.WriteInt32(buffer, IntPtr.Size, unchecked((int)Win32.SeGroupIntegrity));
            Marshal.Copy(binary, 0, buffer + header, binary.Length);
            if (!Win32.SetTokenInformation(token, Win32.TokenIntegrityLevelClass, buffer, header + binary.Length)) throw new Win32Exception();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
