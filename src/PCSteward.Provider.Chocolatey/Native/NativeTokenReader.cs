using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>One entry of <c>TokenGroups</c>.</summary>
internal readonly record struct NativeTokenGroup(string Sid, uint Attributes);

/// <summary>
/// Reads <see cref="TokenFacts"/> and group facts from a token handle with <c>TOKEN_QUERY</c> only (Task 50 design rev 6
/// §2.1). Every read is bounded and parsed with explicit length checks; any failure or malformed buffer yields false.
/// </summary>
internal static class NativeTokenReader
{
    internal const string AdministratorsSid = "S-1-5-32-544";
    private const int MaximumInformationBytes = 64 * 1024;
    private const string MandatoryLabelPrefix = "S-1-16-";

    internal static bool TryReadFacts(SafeAccessTokenHandle token, out TokenFacts facts, out IReadOnlySet<string> ownerSids)
    {
        facts = null!;
        ownerSids = null!;
        if (!OperatingSystem.IsWindows() || token is null || token.IsInvalid || token.IsClosed) return false;
        if (!TryReadUserSid(token, out string user) || !TryReadGroups(token, out IReadOnlyList<NativeTokenGroup> groups) ||
            !TryReadAuthenticationId(token, out ulong authenticationId) || !TryReadInt32(token, Win32.TokenElevationTypeClass, out int elevation) ||
            !TryReadIntegrityRid(token, out uint integrity) || !TryReadInt32(token, Win32.TokenIsAppContainerClass, out int appContainer) ||
            !TryReadPrivilegeNames(token, out IReadOnlyList<string> privileges))
            return false;
        // IsTokenRestricted returns FALSE both for "not restricted" and for a failure; a failure refuses.
        bool restricted = Win32.IsTokenRestricted(token);
        if (!restricted && Marshal.GetLastWin32Error() != 0) return false;

        TokenElevationKind elevationKind = elevation is >= 1 and <= 3 ? (TokenElevationKind)elevation : TokenElevationKind.Unknown;
        AdministratorsMembership administrators = AdministratorsMembership.Absent;
        HashSet<string> owners = new(StringComparer.Ordinal) { user };
        foreach (NativeTokenGroup group in groups)
        {
            bool denyOnly = (group.Attributes & Win32.SeGroupUseForDenyOnly) != 0;
            if (group.Sid == AdministratorsSid)
                administrators = !denyOnly ? AdministratorsMembership.Enabled
                    : administrators == AdministratorsMembership.Enabled ? administrators : AdministratorsMembership.DenyOnly;
            // L4: the user SID, the logon SID and every enabled group can own a node on the requester's behalf.
            if ((group.Attributes & Win32.SeGroupLogonId) == Win32.SeGroupLogonId ||
                ((group.Attributes & Win32.SeGroupEnabled) != 0 && !denyOnly))
                owners.Add(group.Sid);
        }
        facts = new TokenFacts(user, authenticationId, elevationKind, integrity, restricted, appContainer != 0, administrators, privileges);
        ownerSids = owners;
        return true;
    }

    internal static bool TryReadUserSid(SafeAccessTokenHandle token, out string sid)
    {
        string? value = Query(token, Win32.TokenUserClass, (buffer, length) =>
            length >= IntPtr.Size * 2 ? SidAt(Marshal.ReadIntPtr(buffer), buffer, length) : null);
        sid = value ?? string.Empty;
        return value is not null;
    }

    internal static bool TryReadSessionId(SafeAccessTokenHandle token, out int session)
    {
        session = -1;
        if (!TryReadInt32(token, Win32.TokenSessionIdClass, out int value) || value < 0) return false;
        session = value;
        return true;
    }

    internal static bool TryReadGroups(SafeAccessTokenHandle token, out IReadOnlyList<NativeTokenGroup> groups)
    {
        List<NativeTokenGroup>? value = Query(token, Win32.TokenGroupsClass, (buffer, length) =>
        {
            if (length < IntPtr.Size) return null;
            uint count = (uint)Marshal.ReadInt32(buffer);
            int entry = IntPtr.Size * 2;
            if (count > (uint)((length - IntPtr.Size) / entry)) return null;
            List<NativeTokenGroup> result = new((int)count);
            for (int index = 0; index < count; index++)
            {
                int offset = IntPtr.Size + index * entry;
                string? sid = SidAt(Marshal.ReadIntPtr(buffer, offset), buffer, length);
                if (sid is null) return null;
                result.Add(new NativeTokenGroup(sid, (uint)Marshal.ReadInt32(buffer, offset + IntPtr.Size)));
            }
            return result;
        });
        groups = value ?? [];
        return value is not null;
    }

    /// <summary>True only for an identification-level impersonation token (§2.1, L3): it can never act for I/O.</summary>
    internal static bool IsIdentificationImpersonation(SafeAccessTokenHandle token) =>
        TryReadInt32(token, Win32.TokenTypeClass, out int type) && type == Win32.TokenTypeImpersonation &&
        TryReadInt32(token, Win32.TokenImpersonationLevelClass, out int level) && level == Win32.SecurityIdentification;

    /// <summary>The token's <c>TokenLinkedToken</c>, or null when it cannot be read.</summary>
    internal static SafeAccessTokenHandle? TryGetLinkedToken(SafeAccessTokenHandle token)
    {
        IntPtr linked = Query(token, Win32.TokenLinkedTokenClass, (buffer, length) =>
            length >= IntPtr.Size ? Marshal.ReadIntPtr(buffer) : IntPtr.Zero);
        if (linked == IntPtr.Zero || linked == new IntPtr(-1)) return null;
        return new SafeAccessTokenHandle(linked);
    }

    internal static bool TryReadInt32(SafeAccessTokenHandle token, int informationClass, out int value)
    {
        int? read = Query<int?>(token, informationClass, (buffer, length) => length >= sizeof(int) ? Marshal.ReadInt32(buffer) : null);
        value = read ?? 0;
        return read.HasValue;
    }

    private static bool TryReadAuthenticationId(SafeAccessTokenHandle token, out ulong authenticationId)
    {
        // TOKEN_STATISTICS: LUID TokenId; LUID AuthenticationId; ...
        ulong? read = Query<ulong?>(token, Win32.TokenStatisticsClass, (buffer, length) =>
            length >= 16 ? ((ulong)(uint)Marshal.ReadInt32(buffer, 12) << 32) | (uint)Marshal.ReadInt32(buffer, 8) : null);
        authenticationId = read ?? 0;
        return read is > 0;
    }

    private static bool TryReadIntegrityRid(SafeAccessTokenHandle token, out uint rid)
    {
        string? label = Query(token, Win32.TokenIntegrityLevelClass, (buffer, length) =>
            length >= IntPtr.Size * 2 ? SidAt(Marshal.ReadIntPtr(buffer), buffer, length) : null);
        rid = 0;
        return label is not null && label.StartsWith(MandatoryLabelPrefix, StringComparison.Ordinal) &&
            uint.TryParse(label.AsSpan(MandatoryLabelPrefix.Length), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out rid);
    }

    private static bool TryReadPrivilegeNames(SafeAccessTokenHandle token, out IReadOnlyList<string> names)
    {
        bool read = TryReadPrivileges(token, out IReadOnlyList<(Win32.Luid Luid, string Name)> privileges);
        names = read ? privileges.Select(privilege => privilege.Name).ToList() : [];
        return read;
    }

    /// <summary>Every privilege present in the token (enabled or not) with its canonical name.</summary>
    internal static bool TryReadPrivileges(SafeAccessTokenHandle token, out IReadOnlyList<(Win32.Luid Luid, string Name)> privileges)
    {
        privileges = [];
        List<Win32.Luid>? luids = Query(token, Win32.TokenPrivilegesClass, (buffer, length) =>
        {
            const int Entry = 12; // LUID_AND_ATTRIBUTES, 4-byte aligned
            if (length < sizeof(uint)) return null;
            uint count = (uint)Marshal.ReadInt32(buffer);
            if (count > (uint)((length - sizeof(uint)) / Entry)) return null;
            List<Win32.Luid> result = new((int)count);
            for (int index = 0; index < count; index++)
            {
                int offset = sizeof(uint) + index * Entry;
                result.Add(new Win32.Luid { LowPart = (uint)Marshal.ReadInt32(buffer, offset), HighPart = Marshal.ReadInt32(buffer, offset + 4) });
            }
            return result;
        });
        if (luids is null) return false;
        List<(Win32.Luid, string)> resolved = new(luids.Count);
        foreach (Win32.Luid luid in luids)
        {
            if (!TryLookupPrivilegeName(luid, out string name)) return false;
            resolved.Add((luid, name));
        }
        privileges = resolved;
        return true;
    }

    private static bool TryLookupPrivilegeName(Win32.Luid luid, out string name)
    {
        name = string.Empty;
        char[] buffer = new char[128];
        int length = buffer.Length;
        if (!Win32.LookupPrivilegeName(null, ref luid, buffer, ref length) || length <= 0 || length >= buffer.Length) return false;
        name = new string(buffer, 0, length);
        return true;
    }

    /// <summary>Reads a SID only when its pointer lies inside the returned buffer (all token SIDs are returned inline).</summary>
    private static string? SidAt(IntPtr sid, IntPtr buffer, int length)
    {
        long start = buffer.ToInt64(), at = sid.ToInt64();
        // Minimum SID: revision, count, 6-byte authority.
        if (at < start || at + 8 > start + length) return null;
        int subAuthorities = Marshal.ReadByte(sid, 1);
        if (Marshal.ReadByte(sid) != 1 || subAuthorities > 15 || at + 8 + 4 * subAuthorities > start + length) return null;
        try { return new SecurityIdentifier(sid).Value; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Two-call bounded <c>GetTokenInformation</c>; the parser runs while the buffer is still allocated.</summary>
    private static T? Query<T>(SafeAccessTokenHandle token, int informationClass, Func<IntPtr, int, T?> parse)
    {
        if (!OperatingSystem.IsWindows() || token is null || token.IsInvalid || token.IsClosed) return default;
        Win32.GetTokenInformation(token, informationClass, IntPtr.Zero, 0, out int required);
        if (required <= 0 || required > MaximumInformationBytes) return default;
        IntPtr buffer = Marshal.AllocHGlobal(required);
        try
        {
            return Win32.GetTokenInformation(token, informationClass, buffer, required, out int written) && written > 0
                ? parse(buffer, Math.Min(written, required)) : default;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
