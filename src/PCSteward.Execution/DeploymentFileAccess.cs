using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace PCSteward.Execution;

/// <summary>
/// NTFS/ReFS file identity from <c>GetFileInformationByHandleEx(FileIdInfo)</c>: the volume serial number plus the
/// 128-bit file ID (adapter gates §A.4 step 3.4, A-M1). Equal identities name the same file record; a replaced file (a new
/// record moved or written over the path) has a different identity even when its path and bytes are equal.
/// </summary>
internal readonly record struct WindowsFileIdentity(ulong VolumeSerialNumber, UInt128 FileId);

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; adapter gates §A.4: Windows file primitives for the deployment-capability reader and the held
/// binding. Every open is read-only with <see cref="FileShare.Read"/> only (no write and no delete share), so while a handle
/// is open nobody can write, delete, rename or replace the file; reads by others stay possible. Every read is bounded by the
/// caller's limit and fails closed. Nothing is cached. Inputs are fixed paths chosen by the caller, never database,
/// environment or remote text.
/// </summary>
internal static class DeploymentFileAccess
{
    private const int FileIdInfoClass = 18;
    private const uint FileNameNormalizedVolumeNameDos = 0;
    private const int FinalPathBufferChars = 32768;
    private const int ReadChunkBytes = 81920;

    /// <summary>
    /// Opens an existing file for reading with read-only sharing and requires the handle's final normalized path to equal
    /// <paramref name="path"/> (case-insensitive), so a symbolic link, junction or mount point anywhere in the path is
    /// refused after the open as well as by the caller's pre-open reparse check. Returns null on any failure.
    /// </summary>
    internal static SafeFileHandle? TryOpenShareRead(string path)
    {
        SafeFileHandle? handle = TryOpenShareReadAnyPath(path);
        if (handle is null) return null;
        if (TryGetFinalPath(handle, out string finalPath) &&
            string.Equals(finalPath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            return handle;
        handle.Dispose();
        return null;
    }

    /// <summary>
    /// Opens an existing file for reading with read-only sharing without the final-path comparison. Used only for a file
    /// whose identity is then compared with a held reference identity (the peer's main-module image).
    /// </summary>
    internal static SafeFileHandle? TryOpenShareReadAnyPath(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)) return null;
        try
        {
            return File.OpenHandle(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The handle's final normalized DOS path without the <c>\\?\</c> prefix; UNC/network results are refused.</summary>
    internal static bool TryGetFinalPath(SafeFileHandle handle, out string path)
    {
        path = string.Empty;
        if (!OperatingSystem.IsWindows() || handle is null || handle.IsInvalid || handle.IsClosed) return false;
        char[] buffer = new char[FinalPathBufferChars];
        uint length = NativeMethods.GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, FileNameNormalizedVolumeNameDos);
        if (length == 0 || length >= buffer.Length) return false;
        string final = new(buffer, 0, (int)length);
        const string LocalPrefix = @"\\?\";
        if (final.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) || !final.StartsWith(LocalPrefix, StringComparison.Ordinal))
            return false;
        path = final[LocalPrefix.Length..];
        return path.Length > 0;
    }

    /// <summary>Reads <c>FileIdInfo</c> from an open handle. An all-zero identity is treated as unreadable.</summary>
    internal static bool TryReadIdentity(SafeFileHandle handle, out WindowsFileIdentity identity)
    {
        identity = default;
        if (!OperatingSystem.IsWindows() || handle is null || handle.IsInvalid || handle.IsClosed) return false;
        if (!NativeMethods.GetFileInformationByHandleEx(handle, FileIdInfoClass, out NativeMethods.FileIdInformation information,
                (uint)Marshal.SizeOf<NativeMethods.FileIdInformation>()))
            return false;
        UInt128 fileId = new(information.FileIdHigh, information.FileIdLow);
        // A zero file ID cannot identify a file uniquely (possible on some non-NTFS volumes): treat it as unreadable.
        if (fileId == UInt128.Zero) return false;
        identity = new(information.VolumeSerialNumber, fileId);
        return true;
    }

    /// <summary>Opens <paramref name="path"/> with read-only sharing, reads its identity and closes the handle.</summary>
    internal static bool TryReadIdentity(string path, out WindowsFileIdentity identity)
    {
        identity = default;
        using SafeFileHandle? handle = TryOpenShareReadAnyPath(path);
        return handle is not null && TryReadIdentity(handle, out identity);
    }

    /// <summary>
    /// Reads the whole file through the handle when its length is at most <paramref name="maximumBytes"/>; null when it is
    /// larger, changes length while read, or cannot be read.
    /// </summary>
    internal static byte[]? TryReadBounded(SafeFileHandle handle, int maximumBytes)
    {
        try
        {
            long length = RandomAccess.GetLength(handle);
            if (length < 0 || length > maximumBytes) return null;
            byte[] bytes = new byte[(int)length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
                if (read <= 0) return null;
                offset += read;
            }
            Span<byte> probe = stackalloc byte[1];
            return RandomAccess.Read(handle, probe, offset) == 0 ? bytes : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Hashes the whole file through the handle (SHA-256, uppercase hex) when its length is at most
    /// <paramref name="maximumBytes"/>; false when it is larger, changes length while read, or cannot be read.
    /// </summary>
    internal static bool TryHashBounded(SafeFileHandle handle, long maximumBytes, out DeploymentObservedFile observed)
    {
        observed = default;
        try
        {
            long length = RandomAccess.GetLength(handle);
            if (length < 0 || length > maximumBytes) return false;
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[ReadChunkBytes];
            long offset = 0;
            while (offset < length)
            {
                int want = (int)Math.Min(buffer.Length, length - offset);
                int read = RandomAccess.Read(handle, buffer.AsSpan(0, want), offset);
                if (read <= 0) return false;
                hash.AppendData(buffer, 0, read);
                offset += read;
            }
            if (RandomAccess.Read(handle, buffer.AsSpan(0, 1), offset) != 0) return false;
            observed = new(length, Convert.ToHexString(hash.GetHashAndReset()));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or ObjectDisposedException)
        {
            return false;
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct FileIdInformation
        {
            internal ulong VolumeSerialNumber;
            internal ulong FileIdLow;
            internal ulong FileIdHigh;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            out FileIdInformation fileInformation,
            uint bufferSize);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            [Out] char[] filePath,
            uint filePathLength,
            uint flags);
    }
}
