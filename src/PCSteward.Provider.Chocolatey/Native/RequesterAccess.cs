using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>Closed access verdict. <see cref="Granted"/> and <see cref="Unreadable"/> both refuse.</summary>
internal enum RequesterModification
{
    Denied,
    Granted,
    Unreadable
}

/// <summary>Node: every protected node. Ancestor: every directory from the lexical drive root to the protected root's parent (M2, M3).</summary>
internal enum AccessMode
{
    Node,
    Ancestor
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.2 (M2, M3, L4): decides whether the requester can modify a node,
/// with no probing open. The security descriptor (<c>OWNER | GROUP | DACL | LABEL</c>) is read from the <b>held</b>
/// handle with <c>GetSecurityInfo</c>, and <c>AccessCheck(MAXIMUM_ALLOWED, FILE_GENERIC_MAPPING)</c> evaluates the
/// requester's identification token, which accounts for owner-implied rights, OWNER RIGHTS ACEs and the mandatory
/// integrity label. Every API failure is <see cref="RequesterModification.Unreadable"/>.
/// </summary>
internal static class RequesterAccess
{
    internal const uint FileWriteData = 0x00000002;
    internal const uint FileAppendData = 0x00000004;
    internal const uint FileWriteEa = 0x00000010;
    internal const uint FileDeleteChild = 0x00000040;
    internal const uint FileWriteAttributes = 0x00000100;
    internal const uint Delete = 0x00010000;
    internal const uint WriteDac = 0x00040000;
    internal const uint WriteOwner = 0x00080000;
    /// <summary>FILE_ADD_FILE and FILE_ADD_SUBDIRECTORY share the bits of FILE_WRITE_DATA and FILE_APPEND_DATA.</summary>
    internal const uint FileAddFile = FileWriteData;
    internal const uint FileAddSubdirectory = FileAppendData;

    internal const uint FileModificationRights = FileWriteData | FileAppendData | FileWriteEa | FileWriteAttributes | Delete | WriteDac | WriteOwner;
    internal const uint DirectoryModificationRights = FileModificationRights | FileAddFile | FileAddSubdirectory | FileDeleteChild;
    /// <summary>The set of Execution's <c>ProtectedLaunchLayout.AncestorReplacementRights</c>.</summary>
    internal const uint AncestorReplacementRights = FileDeleteChild | Delete | WriteDac | WriteOwner;

    private const int ErrorAccessDenied = 5;
    private const int MaximumPrivilegeSetBytes = 64 * 1024;

    /// <summary>
    /// Node mode: Granted when the node grants any of the file (or directory) modification rights, or when
    /// <paramref name="parentDirectory"/> grants <c>FILE_DELETE_CHILD</c>. Ancestor mode (directories only): Granted when
    /// the directory grants <c>FILE_DELETE_CHILD | DELETE | WRITE_DAC | WRITE_OWNER</c> (or the parent grants
    /// <c>FILE_DELETE_CHILD</c>); <c>FILE_ADD_SUBDIRECTORY</c> is tolerated. A reparse attribute or a kind different from
    /// <paramref name="directory"/> is Unreadable.
    /// </summary>
    internal static RequesterModification Evaluate(RequesterToken token, SafeFileHandle node, bool directory,
        SafeFileHandle? parentDirectory, AccessMode mode)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(node);
        if (!OperatingSystem.IsWindows() || !Enum.IsDefined(mode) || (mode == AccessMode.Ancestor && !directory))
            return RequesterModification.Unreadable;
        if (!ProtectedNodeReader.TryReadKind(node, out bool isDirectory, out bool reparse) || reparse || isDirectory != directory)
            return RequesterModification.Unreadable;
        if (!TryGrantedAccess(token, node, out uint granted)) return RequesterModification.Unreadable;
        bool modifiable = (granted & Mask(directory, mode)) != 0;
        if (parentDirectory is not null)
        {
            if (!ProtectedNodeReader.TryReadKind(parentDirectory, out bool parentIsDirectory, out bool parentReparse) ||
                !parentIsDirectory || parentReparse || !TryGrantedAccess(token, parentDirectory, out uint parentGranted))
                return RequesterModification.Unreadable;
            modifiable |= (parentGranted & FileDeleteChild) != 0;
        }
        return modifiable ? RequesterModification.Granted : RequesterModification.Denied;
    }

    /// <summary>
    /// Ancestor mode on every directory from the lexical drive root of <paramref name="canonicalPath"/> down to its parent,
    /// top-down, each opened through <see cref="ProtectedNodeReader"/> (which refuses reparse points, so a mounted-folder
    /// ancestor is never skipped). The first verdict other than Denied decides. A drive root as the protected root, an
    /// ancestor on another volume or any open failure is Unreadable. Runs before any node of the root is opened.
    /// </summary>
    internal static RequesterModification EvaluateAncestry(RequesterToken token, string canonicalPath)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (!OperatingSystem.IsWindows() || !ProtectedNodeReader.IsCanonicalLocalPath(canonicalPath) || canonicalPath.Length <= 3)
            return RequesterModification.Unreadable;
        string[] segments = canonicalPath[3..].Split('\\');
        string ancestor = canonicalPath[..3];
        ulong? volume = null;
        for (int index = 0; ; index++)
        {
            using (SafeFileHandle? handle = ProtectedNodeReader.TryOpen(ancestor, directory: true, out NativeNode node))
            {
                if (handle is null || (volume is not null && volume != node.Identity.VolumeSerial)) return RequesterModification.Unreadable;
                volume = node.Identity.VolumeSerial;
                RequesterModification verdict = Evaluate(token, handle, directory: true, parentDirectory: null, AccessMode.Ancestor);
                if (verdict != RequesterModification.Denied) return verdict;
            }
            if (index == segments.Length - 1) return RequesterModification.Denied;
            ancestor = index == 0 ? ancestor + segments[index] : ancestor + '\\' + segments[index];
        }
    }

    /// <summary>
    /// L4: true when the node's owner is the token's user SID, its logon SID or any enabled group SID, and also when the
    /// owner cannot be read (fail closed: callers refuse a requester-owned node).
    /// </summary>
    internal static bool IsRequesterOwned(RequesterToken token, SafeFileHandle node)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(node);
        if (!OperatingSystem.IsWindows() || node.IsInvalid || node.IsClosed) return true;
        uint error = Win32.GetSecurityInfo(node, Win32.SeFileObject, Win32.OwnerSecurityInformation,
            out IntPtr owner, out _, out _, out _, out IntPtr descriptor);
        if (error != 0 || descriptor == IntPtr.Zero) return true;
        try
        {
            if (owner == IntPtr.Zero || !Win32.IsValidSecurityDescriptor(descriptor)) return true;
            return token.OwnerSids.Contains(new SecurityIdentifier(owner).Value);
        }
        catch (ArgumentException)
        {
            return true;
        }
        finally
        {
            Win32.LocalFree(descriptor);
        }
    }

    /// <summary>
    /// Test-only (an architecture test bans every <c>src</c> caller): the same AccessCheck and mask decision as
    /// <see cref="Evaluate"/>, on a self-relative security descriptor supplied by the test instead of one read from a held
    /// handle. It covers descriptors a standard-user runner cannot apply to a file (a SYSTEM owner, a High label).
    /// </summary>
    internal static RequesterModification EvaluateDescriptorForTests(RequesterToken token, byte[] selfRelativeDescriptor, bool directory,
        AccessMode mode)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(selfRelativeDescriptor);
        if (!OperatingSystem.IsWindows() || !Enum.IsDefined(mode) || (mode == AccessMode.Ancestor && !directory))
            return RequesterModification.Unreadable;
        GCHandle pinned = GCHandle.Alloc(selfRelativeDescriptor, GCHandleType.Pinned);
        try
        {
            if (!TryAccessCheck(token, pinned.AddrOfPinnedObject(), out uint granted)) return RequesterModification.Unreadable;
            return (granted & Mask(directory, mode)) != 0 ? RequesterModification.Granted : RequesterModification.Denied;
        }
        finally
        {
            pinned.Free();
        }
    }

    private static uint Mask(bool directory, AccessMode mode) =>
        mode == AccessMode.Ancestor ? AncestorReplacementRights : directory ? DirectoryModificationRights : FileModificationRights;

    private static bool TryGrantedAccess(RequesterToken token, SafeFileHandle handle, out uint granted)
    {
        granted = 0;
        if (handle.IsInvalid || handle.IsClosed) return false;
        // N1 review H1: resource attributes are read too, and TryAccessCheck refuses any DACL entry that is not a plain
        // allow/deny ACE, so a conditional ACE can never be evaluated without the attributes it depends on.
        const uint Information = Win32.OwnerSecurityInformation | Win32.GroupSecurityInformation | Win32.DaclSecurityInformation |
            Win32.LabelSecurityInformation | Win32.AttributeSecurityInformation;
        uint error = Win32.GetSecurityInfo(handle, Win32.SeFileObject, Information, out _, out _, out _, out _, out IntPtr descriptor);
        if (error != 0 || descriptor == IntPtr.Zero) return false;
        try
        {
            return TryAccessCheck(token, descriptor, out granted);
        }
        finally
        {
            Win32.LocalFree(descriptor);
        }
    }

    /// <summary>
    /// N1 review H1: only ACCESS_ALLOWED_ACE_TYPE (0) and ACCESS_DENIED_ACE_TYPE (1) are evaluated. Conditional (callback),
    /// object or any other ACE type, or an unparsable ACL, is Unreadable, because the verdict could depend on claims or
    /// resource attributes. A NULL DACL (grants everything) has no entries and is left to AccessCheck.
    /// </summary>
    private static bool DaclHasOnlyPlainAces(IntPtr descriptor)
    {
        if (!Win32.GetSecurityDescriptorDacl(descriptor, out bool present, out IntPtr dacl, out _)) return false;
        if (!present || dacl == IntPtr.Zero) return true;
        // ACL header: AclRevision (1), Sbz1 (1), AclSize (2), AceCount (2), Sbz2 (2).
        int aclSize = (ushort)Marshal.ReadInt16(dacl, 2);
        int aceCount = (ushort)Marshal.ReadInt16(dacl, 4);
        int offset = 8;
        for (int index = 0; index < aceCount; index++)
        {
            if (offset + 4 > aclSize) return false;
            byte aceType = Marshal.ReadByte(dacl, offset);
            int aceSize = (ushort)Marshal.ReadInt16(dacl, offset + 2);
            if (aceType is not (0 or 1) || aceSize < 4 || offset + aceSize > aclSize) return false;
            offset += aceSize;
        }
        return true;
    }

    private static bool TryAccessCheck(RequesterToken token, IntPtr descriptor, out uint granted)
    {
        granted = 0;
        if (token.Handle.IsInvalid || token.Handle.IsClosed || !Win32.IsValidSecurityDescriptor(descriptor)) return false;
        if (!DaclHasOnlyPlainAces(descriptor)) return false;
        Win32.GenericMapping mapping = new()
        {
            GenericRead = 0x00120089,    // FILE_GENERIC_READ
            GenericWrite = 0x00120116,   // FILE_GENERIC_WRITE
            GenericExecute = 0x001200A0, // FILE_GENERIC_EXECUTE
            GenericAll = 0x001F01FF      // FILE_ALL_ACCESS
        };
        uint length = 256;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] privileges = new byte[length];
            if (Win32.AccessCheck(descriptor, token.Handle, Win32.MaximumAllowed, ref mapping, privileges, ref length,
                    out uint access, out bool status))
            {
                // MAXIMUM_ALLOWED that grants nothing reports status false with ERROR_ACCESS_DENIED; anything else is unknown.
                if (status)
                {
                    granted = access;
                    return true;
                }
                return Marshal.GetLastWin32Error() == ErrorAccessDenied;
            }
            if (Marshal.GetLastWin32Error() != Win32.ErrorInsufficientBuffer || length <= privileges.Length || length > MaximumPrivilegeSetBytes)
                return false;
        }
        return false;
    }
}
