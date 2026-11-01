using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>
/// P/Invoke surface of the N1 primitives (Task 50 design rev 6 §2). Files are only ever opened with
/// <see cref="ReadOnlyOpenAccess"/> and <see cref="FileShareRead"/>; no write, delete, security-write or impersonation
/// API is declared here.
/// </summary>
internal static class ProtectedNativeMethods
{
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint Synchronize = 0x00100000;
    internal const uint WaitObject0 = 0x00000000;
    internal const uint WaitTimeout = 0x00000102;

    internal const uint TokenAssignPrimary = 0x0001;
    internal const uint TokenDuplicate = 0x0002;
    internal const uint TokenQuery = 0x0008;
    internal const uint TokenAdjustDefault = 0x0080;
    internal const int TokenUserClass = 1;
    internal const int TokenGroupsClass = 2;
    internal const int TokenPrivilegesClass = 3;
    internal const int TokenTypeClass = 8;
    internal const int TokenImpersonationLevelClass = 9;
    internal const int TokenStatisticsClass = 10;
    internal const int TokenSessionIdClass = 12;
    internal const int TokenElevationTypeClass = 18;
    internal const int TokenLinkedTokenClass = 19;
    internal const int TokenIntegrityLevelClass = 25;
    internal const int TokenIsAppContainerClass = 29;
    internal const int TokenTypeImpersonation = 2;
    internal const int SecurityIdentification = 1;
    internal const uint LuaToken = 0x4;
    internal const uint SeGroupEnabled = 0x00000004;
    internal const uint SeGroupUseForDenyOnly = 0x00000010;
    internal const uint SeGroupIntegrity = 0x00000020;
    internal const uint SeGroupLogonId = 0xC0000000;

    /// <summary><c>FILE_GENERIC_READ</c>: READ_CONTROL | SYNCHRONIZE | FILE_READ_DATA | FILE_READ_ATTRIBUTES | FILE_READ_EA.</summary>
    internal const uint ReadOnlyOpenAccess = 0x00120089;
    internal const uint FileShareRead = 0x00000001;
    internal const uint OpenExisting = 3;
    internal const uint FileFlagBackupSemantics = 0x02000000;
    internal const uint FileFlagOpenReparsePoint = 0x00200000;
    internal const uint FileAttributeDirectory = 0x00000010;
    internal const uint FileAttributeReparsePoint = 0x00000400;
    internal const uint FileTypeDisk = 1;
    internal const int FileStandardInfoClass = 1;
    internal const int FileAttributeTagInfoClass = 9;
    internal const int FileIdBothDirectoryInfoClass = 10;
    internal const int FileIdBothDirectoryRestartInfoClass = 11;
    internal const int FileIdInfoClass = 18;
    internal const uint FileNameNormalizedVolumeNameDos = 0;
    internal const int ErrorNoMoreFiles = 18;
    internal const int ErrorInsufficientBuffer = 122;

    internal const int SeFileObject = 1;
    internal const uint OwnerSecurityInformation = 0x00000001;
    internal const uint GroupSecurityInformation = 0x00000002;
    internal const uint DaclSecurityInformation = 0x00000004;
    internal const uint LabelSecurityInformation = 0x00000010;
    internal const uint MaximumAllowed = 0x02000000;

    /// <summary>Same layout as Execution's <c>FILE_ID_INFO</c> declaration (architecture-tested).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct FileIdInformation
    {
        internal ulong VolumeSerialNumber;
        internal ulong FileIdLow;
        internal ulong FileIdHigh;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileAttributeTagInformation
    {
        internal uint FileAttributes;
        internal uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileStandardInformation
    {
        internal long AllocationSize;
        internal long EndOfFile;
        internal uint NumberOfLinks;
        internal byte DeletePending;
        internal byte Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GenericMapping
    {
        internal uint GenericRead;
        internal uint GenericWrite;
        internal uint GenericExecute;
        internal uint GenericAll;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SidAndAttributes
    {
        internal IntPtr Sid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct LuidAndAttributes
    {
        internal Luid Luid;
        internal uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid
    {
        internal uint LowPart;
        internal int HighPart;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetProcessId(SafeProcessHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, [Out] char[] name, ref uint size);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information,
        int length, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DuplicateTokenEx(SafeAccessTokenHandle existing, uint desiredAccess, IntPtr attributes,
        int impersonationLevel, int tokenType, out SafeAccessTokenHandle duplicate);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateRestrictedToken(SafeAccessTokenHandle existing, uint flags, uint disableSidCount,
        SidAndAttributes[]? sidsToDisable, uint deletePrivilegeCount, LuidAndAttributes[]? privilegesToDelete,
        uint restrictedSidCount, IntPtr sidsToRestrict, out SafeAccessTokenHandle restricted);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsTokenRestricted(SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", EntryPoint = "LookupPrivilegeNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LookupPrivilegeName(string? systemName, ref Luid luid, [Out] char[] name, ref int length);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetFileType(SafeFileHandle file);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileIdInformation information, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileAttributeTagInformation information, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileStandardInformation information, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        [Out] byte[] buffer, uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    [DllImport("advapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint securityInformation,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr securityDescriptor);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AccessCheck(IntPtr securityDescriptor, SafeAccessTokenHandle clientToken, uint desiredAccess,
        ref GenericMapping genericMapping, [Out] byte[] privilegeSet, ref uint privilegeSetLength, out uint grantedAccess,
        [MarshalAs(UnmanagedType.Bool)] out bool accessStatus);

    [DllImport("advapi32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsValidSecurityDescriptor(IntPtr securityDescriptor);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("userenv.dll", EntryPoint = "GetUserProfileDirectoryW", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetUserProfileDirectory(SafeAccessTokenHandle token, [Out] char[]? profileDirectory, ref uint size);

    [DllImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int SHGetKnownFolderPath(in Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
