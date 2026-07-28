using System.Runtime.InteropServices;
using System.Security;
using System.Globalization;
using Microsoft.Win32.SafeHandles;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Storage;

public sealed record DownloadCacheSummary(long TotalBytes, long RecoverableBytes, int ArtifactCount, int ActiveCount, DateTimeOffset? LastCleanedAt);
public sealed record DownloadCleanupResidual(string RelativePath, string Reason);
public sealed record DownloadCleanupResult(bool Succeeded, int DeletedItems, int RemainingItems, long RecoveredBytes, long RemainingBytes, IReadOnlyCollection<DownloadCleanupResidual> Residuals)
{
    public string Message => Succeeded ? DeletedItems == 0 ? "No inactive PC Steward downloaded files remain." : $"Removed {DeletedItems} PC Steward downloaded file(s), recovering {RecoveredBytes:N0} bytes." : $"Cleanup was partial. {RemainingItems} PC Steward downloaded file(s) remain.";
}

/// <summary>Deletion authority is a literal cache root, never a path stored in SQLite or received from a provider.</summary>
public sealed class DownloadedArtifactCache : IDisposable
{
    public const long DefaultSoftQuotaBytes = 2L * 1024 * 1024 * 1024;
    private readonly IDownloadedArtifactStore store;
    private readonly SemaphoreSlim cleanupGate = new(1, 1);

    public DownloadedArtifactCache(IDownloadedArtifactStore store, string? cacheRoot = null, long softQuotaBytes = DefaultSoftQuotaBytes)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        ArgumentOutOfRangeException.ThrowIfLessThan(softQuotaBytes, 1L);
        SoftQuotaBytes = softQuotaBytes;
        CacheRoot = PathSafety.EnsureRoot(cacheRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward", "downloads"));
    }

    public string CacheRoot { get; }
    public long SoftQuotaBytes { get; }
    public bool IsCleaning => cleanupGate.CurrentCount == 0;
    public void Dispose() => cleanupGate.Dispose();

    public async Task RegisterAsync(DownloadedArtifact artifact, CancellationToken cancellationToken)
    {
        if (artifact.SizeBytes < 0 || string.IsNullOrWhiteSpace(artifact.Category)) throw new ArgumentOutOfRangeException(nameof(artifact));
        _ = PathSafety.ResolveLedgerPath(CacheRoot, artifact.RelativePath);
        await store.SaveDownloadedArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloadCacheSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        DownloadedArtifact[] artifacts = (await store.GetDownloadedArtifactsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        FileSystemObject[] objects = PathSafety.EnumerateOwnedObjects(CacheRoot).ToArray();
        HashSet<string> active = artifacts.Where(artifact => artifact.IsActive).Select(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long total = objects.Sum(item => item.SizeBytes);
        long recoverable = objects.Where(item => !active.Contains(item.RelativePath)).Sum(item => item.SizeBytes);
        return new DownloadCacheSummary(total, recoverable, objects.Length, active.Count, await store.GetDownloadedArtifactsLastCleanedAtAsync(cancellationToken).ConfigureAwait(false));
    }

    public Task<DownloadCleanupResult> ClearInactiveAsync(CancellationToken cancellationToken) => CleanupAsync(clearAllInactive: true, DateTimeOffset.UtcNow, cancellationToken);

    public Task<DownloadCleanupResult> EnforceRetentionAndQuotaAsync(DateTimeOffset now, CancellationToken cancellationToken) => CleanupAsync(clearAllInactive: false, now, cancellationToken);

    private async Task<DownloadCleanupResult> CleanupAsync(bool clearAllInactive, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await cleanupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DownloadedArtifact[] all = (await store.GetDownloadedArtifactsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            FileSystemObject[] initialObjects = PathSafety.EnumerateOwnedObjects(CacheRoot).ToArray();
            Dictionary<string, long> initialSizes = initialObjects.ToDictionary(item => item.RelativePath, item => item.SizeBytes, StringComparer.OrdinalIgnoreCase);
            long retainedBytes = initialObjects.Sum(item => item.SizeBytes);
            List<DownloadCleanupResidual> residuals = [];
            int deleted = 0;
            long recovered = 0;
            Dictionary<string, DownloadedArtifact> ledger = all.ToDictionary(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath), StringComparer.OrdinalIgnoreCase);
            HashSet<string> active = all.Where(artifact => artifact.IsActive).Select(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> attempted = [];
            foreach (FileSystemObject item in initialObjects.OrderBy(item => item.IsDirectory).ThenBy(item => ledger.TryGetValue(item.RelativePath, out DownloadedArtifact? registered) ? registered.CreatedAt.UtcDateTime : item.LastWriteTimeUtc).ThenBy(item => item.RelativePath, StringComparer.Ordinal))
            {
                if (active.Contains(item.RelativePath)) continue;
                ledger.TryGetValue(item.RelativePath, out DownloadedArtifact? artifact);
                bool expired = artifact is not null && (artifact.Outcome == DownloadedArtifactOutcome.VerifiedSuccess ? artifact.CreatedAt < now.AddDays(-7) : artifact.CreatedAt < now.AddDays(-1));
                bool quotaExceeded = retainedBytes > SoftQuotaBytes;
                if (!clearAllInactive && !expired && !quotaExceeded)
                {
                    continue;
                }

                attempted.Add(item.RelativePath);
                PathSafety.DeleteResult result = PathSafety.DeleteExactObject(CacheRoot, item.RelativePath, item.IsDirectory);
                if (result.Deleted || result.AlreadyAbsent)
                {
                    if (artifact is not null) await store.DeleteDownloadedArtifactAsync(artifact.Id, cancellationToken).ConfigureAwait(false);
                    deleted++; recovered += result.Deleted ? item.SizeBytes : 0; retainedBytes -= item.SizeBytes;
                }
                else residuals.Add(new DownloadCleanupResidual(item.RelativePath, result.Reason));
            }

            DownloadedArtifact[] remaining = (await store.GetDownloadedArtifactsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            ledger = remaining.ToDictionary(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath), StringComparer.OrdinalIgnoreCase);
            active = remaining.Where(artifact => artifact.IsActive).Select(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            FileSystemObject[] filesystemRemaining = PathSafety.EnumerateOwnedObjects(CacheRoot).ToArray();
            foreach (FileSystemObject item in filesystemRemaining.Where(item => !active.Contains(item.RelativePath) && (clearAllInactive || ledger.ContainsKey(item.RelativePath) || attempted.Contains(item.RelativePath)) && (!ledger.ContainsKey(item.RelativePath) || attempted.Contains(item.RelativePath)) && !residuals.Any(residual => string.Equals(residual.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase))))
                residuals.Add(new DownloadCleanupResidual(item.RelativePath, ledger.ContainsKey(item.RelativePath) ? "UndeletedOwnedObject" : "UnknownOwnedObject"));
            bool succeeded = residuals.Count == 0;
            if (succeeded) await store.SetDownloadedArtifactsLastCleanedAtAsync(now, cancellationToken).ConfigureAwait(false);
            return new DownloadCleanupResult(succeeded, deleted, filesystemRemaining.Length, recovered, filesystemRemaining.Sum(item => item.SizeBytes), residuals);
        }
        finally { cleanupGate.Release(); }
    }
}

internal static class PathSafety
{
    internal sealed record DeleteResult(bool Deleted, bool AlreadyAbsent, string Reason);

    internal static string EnsureRoot(string root)
    {
        RejectNonLocalOrStreamPath(root, "root");
        string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        RejectReparseAncestors(canonical);
        Directory.CreateDirectory(canonical);
        RejectReparseComponents(canonical, canonical);
        return canonical;
    }

    internal static string ResolveLedgerPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':')) throw new SecurityException("Only a relative cache ledger path is accepted.");
        string candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        string relative = Path.GetRelativePath(root, candidate);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) throw new SecurityException("Artifact path is outside the fixed download root.");
        RejectReparseComponents(root, candidate);
        return candidate;
    }

    internal static string NormalizeRelativePath(string relativePath) => Path.GetRelativePath("x:\\", Path.Combine("x:\\", relativePath)).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    internal static IEnumerable<FileSystemObject> EnumerateOwnedObjects(string root)
    {
        Stack<(string FullPath, string RelativePath)> pending = new();
        pending.Push((root, string.Empty));
        while (pending.Count != 0)
        {
            (string parent, string parentRelative) = pending.Pop();
            IEnumerable<string> children;
            bool inaccessible = false;
            try { children = Directory.EnumerateFileSystemEntries(parent).ToArray(); }
            catch (UnauthorizedAccessException) { children = []; inaccessible = true; }
            catch (IOException) { children = []; inaccessible = true; }
            if (inaccessible) { yield return new FileSystemObject(parentRelative, 0, false, true, DateTime.MinValue); continue; }
            foreach (string child in children)
            {
                string relative = parentRelative.Length == 0 ? Path.GetFileName(child) : Path.Combine(parentRelative, Path.GetFileName(child));
                FileAttributes attributes;
                bool unreadable = false;
                try { attributes = File.GetAttributes(child); }
                catch (IOException) { attributes = FileAttributes.Normal; unreadable = true; }
                if (unreadable) { yield return new FileSystemObject(relative, 0, false, true, DateTime.MinValue); continue; }
                bool directory = (attributes & FileAttributes.Directory) != 0;
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    yield return new FileSystemObject(relative, 0, directory, true, DateTime.MinValue);
                    continue;
                }
                if (directory) { pending.Push((child, relative)); yield return new FileSystemObject(relative, 0, true, false, Directory.GetLastWriteTimeUtc(child)); }
                else
                {
                    long size;
                    try { size = new FileInfo(child).Length; }
                    catch (IOException) { size = 0; }
                    yield return new FileSystemObject(relative, size, false, false, File.GetLastWriteTimeUtc(child));
                }
            }
        }
    }

    internal static DeleteResult DeleteExactFile(string root, string relativePath) => DeleteExactObject(root, relativePath, false);

    internal static DeleteResult DeleteExactObject(string root, string relativePath, bool directory)
    {
        try
        {
            string path = ResolveLedgerPath(root, relativePath);
            if (!Exists(path)) return new(false, true, "Absent");
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0 || directory != ((attributes & FileAttributes.Directory) != 0)) return new(false, false, "UnsafePath");
            NativeDelete.DeleteExactObject(root, relativePath, directory);
            return new(true, false, "Deleted");
        }
        catch (FileNotFoundException) { return new(false, true, "Absent"); }
        catch (DirectoryNotFoundException) { return new(false, true, "Absent"); }
        catch (SecurityException) { return new(false, false, "UnsafePath"); }
        catch (UnauthorizedAccessException) { return new(false, false, "AccessDenied"); }
        catch (IOException exception) { return new(false, false, Classify(exception)); }
    }

    private static void RejectNonLocalOrStreamPath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)) throw new SecurityException($"UNC, device and relative {name} paths are denied.");
        int firstColon = path.IndexOf(':'); if (firstColon != 1 || path.IndexOf(':', firstColon + 1) >= 0) throw new SecurityException("Alternate data streams are denied.");
    }

    private static void RejectReparseComponents(string root, string candidate)
    {
        string current = root;
        if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new SecurityException("A reparse-point cache root is denied.");
        foreach (string component in Path.GetRelativePath(root, candidate).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (!Exists(current)) break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new SecurityException("Reparse-point traversal is denied.");
        }
    }

    private static void RejectReparseAncestors(string candidate)
    {
        string volume = Path.GetPathRoot(candidate) ?? throw new SecurityException("A cache root volume is required.");
        string current = Path.TrimEndingDirectorySeparator(volume);
        foreach (string component in Path.GetRelativePath(volume, candidate).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            if (!Exists(current)) continue;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new SecurityException("A reparse-point cache-root ancestor is denied.");
        }
    }

    private static bool Exists(string path) { try { _ = File.GetAttributes(path); return true; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } }
    private static string Classify(IOException exception) { int code = exception.HResult & 0xFFFF; return code is 32 or 33 or 67 ? "LockedOrInUse" : "IoError:" + code; }
}

internal sealed record FileSystemObject(string RelativePath, long SizeBytes, bool IsDirectory, bool IsUnsafe, DateTime LastWriteTimeUtc);

internal static class NativeDelete
{
    // Internal test seam: production never assigns this hook. It lets the hostile fixture replace
    // an ancestor only after the fixed root handle has been bound.
    internal static Action? BeforeDescendantOpenForTesting { get; set; }
    private const uint DeleteAccess = 0x00010000, ReadAttributes = 0x00000080, ShareRead = 1, ShareWrite = 2, OpenExisting = 3, NtOpen = 1, OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfo = 4;
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.Bool)] public bool DeleteFile; }
    [StructLayout(LayoutKind.Sequential)] private struct ByHandleFileInformation { public uint Attributes; public uint CreationTimeLow; public uint CreationTimeHigh; public uint LastAccessTimeLow; public uint LastAccessTimeHigh; public uint LastWriteTimeLow; public uint LastWriteTimeHigh; public uint VolumeSerialNumber; public uint FileSizeHigh; public uint FileSizeLow; public uint NumberOfLinks; public uint FileIndexHigh; public uint FileIndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass, ref FileDisposition fileInformation, uint bufferSize);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
    internal static void DeleteExactObject(string root, string relativePath, bool directory)
    {
        // A root handle is opened without following a reparse point and every descendant is opened
        // relative to that already-bound parent handle.  An attacker can replace names in the tree,
        // but cannot redirect this operation through an ancestor replacement after it is opened.
        using SafeFileHandle rootHandle = OpenVerifiedRoot(root);
        BeforeDescendantOpenForTesting?.Invoke();
        using SafeFileHandle handle = OpenRelative(rootHandle, relativePath, directory);
        FileDisposition disposition = new() { DeleteFile = true };
        if (!SetFileInformationByHandle(handle, FileDispositionInfo, ref disposition, (uint)Marshal.SizeOf<FileDisposition>())) throw Error("Handle-bound deletion failed.");
    }

    private static SafeFileHandle OpenVerifiedRoot(string root)
    {
        string volume = Path.GetPathRoot(root) ?? throw new SecurityException("A cache root volume is required.");
        SafeFileHandle current = CreateFileW(volume, ReadAttributes, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, OpenReparsePoint | 0x02000000, IntPtr.Zero);
        if (current.IsInvalid) throw Error("Opening the cache volume failed.");
        try
        {
            foreach (string component in Path.GetRelativePath(volume, root).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                SafeFileHandle next = OpenRelativeObject(current, component, ReadAttributes, true);
                current.Dispose();
                current = next;
            }
            SafeFileHandle result = current;
            current = null!;
            return result;
        }
        finally { current?.Dispose(); }
    }

    private static SafeFileHandle OpenRelative(SafeFileHandle root, string relativePath, bool directory)
    {
        SafeFileHandle parent = root;
        List<SafeFileHandle> parents = [];
        try
        {
            string[] parts = relativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (int index = 0; index < parts.Length; index++)
            {
                bool leaf = index == parts.Length - 1;
                SafeFileHandle opened = OpenRelativeObject(parent, parts[index], leaf ? DeleteAccess | ReadAttributes : ReadAttributes, !leaf || directory);
                if (!leaf) { parents.Add(opened); parent = opened; }
                else return opened;
            }
            throw new SecurityException("An empty cache path cannot be deleted.");
        }
        finally { foreach (SafeFileHandle item in parents) item.Dispose(); }
    }

    private static SafeFileHandle OpenRelativeObject(SafeFileHandle parent, string name, uint access, bool directory)
    {
        IntPtr nameBuffer = Marshal.StringToHGlobalUni(name);
        try
        {
            UnicodeString unicode = new() { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)((name.Length + 1) * 2)), Buffer = nameBuffer };
            ObjectAttributes attributes = new() { Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(), ObjectName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>()), Attributes = 0x40 };
            try
            {
                Marshal.StructureToPtr(unicode, attributes.ObjectName, false);
                int status = NtCreateFile(out SafeFileHandle handle, access | 0x00100000u, ref attributes, out _, IntPtr.Zero, 0, ShareRead | ShareWrite, NtOpen, (directory ? 0x1u : 0u) | 0x00200000u | 0x20u, IntPtr.Zero, 0);
                if (status != 0 || handle.IsInvalid) { handle?.Dispose(); throw new IOException("Opening a cache descendant relative to its parent failed. NTSTATUS=" + status.ToString("X8", CultureInfo.InvariantCulture), status); }
                if (!GetFileInformationByHandle(handle, out ByHandleFileInformation info) ||
                    (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                    directory != ((info.Attributes & (uint)FileAttributes.Directory) != 0))
                {
                    handle.Dispose();
                    throw new SecurityException("A reparse point or unexpected object type was opened under the cache root.");
                }
                return handle;
            }
            finally { Marshal.FreeHGlobal(attributes.ObjectName); }
        }
        finally { Marshal.FreeHGlobal(nameBuffer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }
    [DllImport("ntdll.dll")] private static extern int NtCreateFile(out SafeFileHandle fileHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, out IoStatusBlock ioStatusBlock, IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);
    private static IOException Error(string message) { int code = Marshal.GetLastWin32Error(); return new IOException(message + " Win32=" + code, unchecked((int)(0x80070000u | (uint)code))); }
}
