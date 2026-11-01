using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Win32 = PCSteward.Provider.Chocolatey.Native.ProtectedNativeMethods;

namespace PCSteward.Provider.Chocolatey.Native;

/// <summary>A node opened through <see cref="ProtectedNodeReader"/>: its final path, walk-relative path, kind, identity and length.</summary>
internal readonly record struct NativeNode(string FullPath, string RelativePath, bool Directory, NativeFileIdentity Identity, long Length);

/// <summary>The specific reason <see cref="ProtectedNodeReader.TryOpen(string, bool, out NativeNode, out ProtectedOpenFailure)"/> refused.</summary>
internal enum ProtectedOpenFailure
{
    None,
    /// <summary>Not a canonical, fully qualified local drive path within the length bound.</summary>
    InvalidPath,
    OpenFailed,
    /// <summary>The handle is not a disk file (for example a device name).</summary>
    NotDisk,
    Unreadable,
    /// <summary>The node itself carries a reparse attribute (junction, symbolic link, mount point or other).</summary>
    Reparse,
    /// <summary>A directory was requested and a file opened, or the reverse.</summary>
    KindMismatch,
    DeletePending,
    /// <summary>The handle's final normalized path differs from the requested canonical path.</summary>
    FinalPathMismatch,
    ZeroFileId
}

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §2.3: opens protected nodes read-only. Every open uses
/// <c>FILE_GENERIC_READ</c>, <c>FILE_SHARE_READ</c> only, <c>OPEN_EXISTING</c> and <c>FILE_FLAG_OPEN_REPARSE_POINT</c>
/// (plus <c>FILE_FLAG_BACKUP_SEMANTICS</c> for directories), so while a handle is held nobody can write, delete, rename
/// or replace the node. A reparse attribute, a final path different from the requested canonical path, a zero file ID,
/// a kind mismatch, a pending delete or any failed query refuses. Inputs are canonical paths chosen by the caller.
/// </summary>
internal static class ProtectedNodeReader
{
    internal const int MaximumPathLength = 4096;
    private const int FinalPathBufferChars = 32768;
    private const int ReadChunkBytes = 81920;
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
    };

    internal static SafeFileHandle? TryOpen(string canonicalPath, bool directory, out NativeNode node) =>
        TryOpen(canonicalPath, directory, out node, out _);

    internal static SafeFileHandle? TryOpen(string canonicalPath, bool directory, out NativeNode node, out ProtectedOpenFailure failure)
    {
        node = default;
        failure = ProtectedOpenFailure.InvalidPath;
        if (!OperatingSystem.IsWindows() || !IsCanonicalLocalPath(canonicalPath)) return null;
        uint flags = Win32.FileFlagOpenReparsePoint | (directory ? Win32.FileFlagBackupSemantics : 0);
        SafeFileHandle handle = Win32.CreateFile(canonicalPath, Win32.ReadOnlyOpenAccess, Win32.FileShareRead, IntPtr.Zero,
            Win32.OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            failure = ProtectedOpenFailure.OpenFailed;
            return null;
        }
        failure = Inspect(handle, canonicalPath, directory, out node);
        if (failure == ProtectedOpenFailure.None) return handle;
        handle.Dispose();
        node = default;
        return null;
    }

    /// <summary>Reads the whole file when its length is at most <paramref name="maximum"/>, requiring end of file at that length.</summary>
    internal static bool TryReadBounded(SafeFileHandle file, int maximum, out byte[] bytes)
    {
        bytes = [];
        if (maximum < 0 || !IsRegularFile(file)) return false;
        try
        {
            long length = RandomAccess.GetLength(file);
            if (length < 0 || length > maximum) return false;
            byte[] buffer = new byte[(int)length];
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = RandomAccess.Read(file, buffer.AsSpan(offset), offset);
                if (read <= 0) return false;
                offset += read;
            }
            Span<byte> probe = stackalloc byte[1];
            if (RandomAccess.Read(file, probe, offset) != 0) return false;
            bytes = buffer;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>SHA-256 of the whole file when its length is at most <paramref name="maximum"/>, requiring end of file at that length.</summary>
    internal static bool TryHashBounded(SafeFileHandle file, long maximum, out NativeObservedFile observed)
    {
        observed = default;
        if (maximum < 0 || !IsRegularFile(file)) return false;
        try
        {
            long length = RandomAccess.GetLength(file);
            if (length < 0 || length > maximum) return false;
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[ReadChunkBytes];
            long offset = 0;
            while (offset < length)
            {
                int want = (int)Math.Min(buffer.Length, length - offset);
                int read = RandomAccess.Read(file, buffer.AsSpan(0, want), offset);
                if (read <= 0) return false;
                hash.AppendData(buffer, 0, read);
                offset += read;
            }
            if (RandomAccess.Read(file, buffer.AsSpan(0, 1), offset) != 0) return false;
            observed = new NativeObservedFile(length, Convert.ToHexString(hash.GetHashAndReset()));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// True for <c>X:\</c> or <c>X:\seg\…\seg</c> with no empty, <c>.</c> or <c>..</c> segment, no segment ending in a dot or
    /// space, no device name, no wildcard, stream, quote, pipe, angle bracket, forward slash or control character, and at
    /// most <see cref="MaximumPathLength"/> characters. Such a path is its own full path, so no lexical normalization can
    /// change which directory entry it names. Pure and portable.
    /// </summary>
    internal static bool IsCanonicalLocalPath(string? path)
    {
        if (path is null || path.Length < 3 || path.Length > MaximumPathLength) return false;
        if (!char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\') return false;
        if (path.Length == 3) return true;
        foreach (string segment in path[3..].Split('\\'))
            if (!IsCanonicalName(segment)) return false;
        return true;
    }

    /// <summary>One canonical directory-entry name (see <see cref="IsCanonicalLocalPath"/>).</summary>
    internal static bool IsCanonicalName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255 || name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (name.Any(character => char.IsControl(character) || character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
            return false;
        int dot = name.IndexOf('.', StringComparison.Ordinal);
        return !DeviceNames.Contains((dot < 0 ? name : name[..dot]).TrimEnd(' '));
    }

    /// <summary>The handle's final normalized DOS path without the <c>\\?\</c> prefix; UNC results are refused.</summary>
    internal static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        if (!OperatingSystem.IsWindows() || handle is null || handle.IsInvalid || handle.IsClosed) return false;
        char[] buffer = new char[FinalPathBufferChars];
        uint length = Win32.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, Win32.FileNameNormalizedVolumeNameDos);
        if (length == 0 || length >= buffer.Length) return false;
        string final = new(buffer, 0, (int)length);
        const string LocalPrefix = @"\\?\";
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || !final.StartsWith(LocalPrefix, StringComparison.Ordinal))
            return false;
        path = final[LocalPrefix.Length..];
        return path.Length > 0;
    }

    /// <summary><c>FileIdInfo</c> of the handle; a zero file ID is unreadable.</summary>
    internal static bool TryReadIdentity(SafeFileHandle handle, out NativeFileIdentity identity)
    {
        identity = default;
        if (!OperatingSystem.IsWindows() || handle is null || handle.IsInvalid || handle.IsClosed) return false;
        if (!Win32.GetFileInformationByHandleEx(handle, Win32.FileIdInfoClass, out Win32.FileIdInformation information,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.FileIdInformation>()))
            return false;
        UInt128 fileId = new(information.FileIdHigh, information.FileIdLow);
        if (fileId == UInt128.Zero) return false;
        identity = new NativeFileIdentity(information.VolumeSerialNumber, fileId);
        return true;
    }

    /// <summary>Directory and reparse attributes from <c>FileAttributeTagInfo</c> on the handle.</summary>
    internal static bool TryReadKind(SafeFileHandle handle, out bool directory, out bool reparse)
    {
        directory = false;
        reparse = true;
        if (!OperatingSystem.IsWindows() || handle is null || handle.IsInvalid || handle.IsClosed) return false;
        if (!Win32.GetFileInformationByHandleEx(handle, Win32.FileAttributeTagInfoClass, out Win32.FileAttributeTagInformation tag,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.FileAttributeTagInformation>()))
            return false;
        directory = (tag.FileAttributes & Win32.FileAttributeDirectory) != 0;
        reparse = (tag.FileAttributes & Win32.FileAttributeReparsePoint) != 0 || tag.ReparseTag != 0;
        return true;
    }

    private static ProtectedOpenFailure Inspect(SafeFileHandle handle, string canonicalPath, bool directory, out NativeNode node)
    {
        node = default;
        if (Win32.GetFileType(handle) != Win32.FileTypeDisk) return ProtectedOpenFailure.NotDisk;
        if (!TryReadKind(handle, out bool isDirectory, out bool reparse)) return ProtectedOpenFailure.Unreadable;
        if (reparse) return ProtectedOpenFailure.Reparse;
        if (isDirectory != directory) return ProtectedOpenFailure.KindMismatch;
        if (!Win32.GetFileInformationByHandleEx(handle, Win32.FileStandardInfoClass, out Win32.FileStandardInformation standard,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.FileStandardInformation>()))
            return ProtectedOpenFailure.Unreadable;
        if (standard.DeletePending != 0) return ProtectedOpenFailure.DeletePending;
        if ((standard.Directory != 0) != directory || standard.EndOfFile < 0) return ProtectedOpenFailure.KindMismatch;
        if (!TryGetFinalPath(handle, out string finalPath)) return ProtectedOpenFailure.Unreadable;
        if (!string.Equals(finalPath, canonicalPath, StringComparison.OrdinalIgnoreCase)) return ProtectedOpenFailure.FinalPathMismatch;
        if (!Win32.GetFileInformationByHandleEx(handle, Win32.FileIdInfoClass, out Win32.FileIdInformation id,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.FileIdInformation>()))
            return ProtectedOpenFailure.Unreadable;
        UInt128 fileId = new(id.FileIdHigh, id.FileIdLow);
        if (fileId == UInt128.Zero) return ProtectedOpenFailure.ZeroFileId;
        node = new NativeNode(finalPath, string.Empty, directory, new NativeFileIdentity(id.VolumeSerialNumber, fileId),
            directory ? 0 : standard.EndOfFile);
        return ProtectedOpenFailure.None;
    }

    private static bool IsRegularFile(SafeFileHandle file) =>
        file is not null && !file.IsInvalid && !file.IsClosed && TryReadKind(file, out bool directory, out bool reparse) && !directory && !reparse;
}
