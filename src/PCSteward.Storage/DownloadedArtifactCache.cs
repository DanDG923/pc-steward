using System.Runtime.InteropServices;
using System.Security;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
        long recoverable = objects.Where(item => !IsActivePath(item.RelativePath, active)).Sum(item => item.SizeBytes);
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
            foreach (FileSystemObject item in initialObjects
                .OrderBy(item => item.IsDirectory)
                .ThenBy(item => item.IsDirectory ? -PathDepth(item.RelativePath) : 0)
                .ThenBy(item => ledger.TryGetValue(item.RelativePath, out DownloadedArtifact? registered) ? registered.CreatedAt.UtcDateTime : item.LastWriteTimeUtc)
                .ThenBy(item => item.RelativePath, StringComparer.Ordinal))
            {
                if (IsActivePath(item.RelativePath, active)) continue;
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
            foreach (DownloadedArtifact missing in remaining.Where(artifact => !artifact.IsActive && !initialSizes.ContainsKey(PathSafety.NormalizeRelativePath(artifact.RelativePath))))
                await store.DeleteDownloadedArtifactAsync(missing.Id, cancellationToken).ConfigureAwait(false);
            remaining = (await store.GetDownloadedArtifactsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            ledger = remaining.ToDictionary(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath), StringComparer.OrdinalIgnoreCase);
            active = remaining.Where(artifact => artifact.IsActive).Select(artifact => PathSafety.NormalizeRelativePath(artifact.RelativePath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            FileSystemObject[] filesystemRemaining = PathSafety.EnumerateOwnedObjects(CacheRoot).ToArray();
            foreach (FileSystemObject item in filesystemRemaining.Where(item => !IsActivePath(item.RelativePath, active) && (clearAllInactive || ledger.ContainsKey(item.RelativePath) || attempted.Contains(item.RelativePath)) && (!ledger.ContainsKey(item.RelativePath) || attempted.Contains(item.RelativePath)) && !residuals.Any(residual => string.Equals(residual.RelativePath, item.RelativePath, StringComparison.OrdinalIgnoreCase))))
                residuals.Add(new DownloadCleanupResidual(item.RelativePath, ledger.ContainsKey(item.RelativePath) ? "UndeletedOwnedObject" : "UnknownOwnedObject"));
            bool succeeded = residuals.Count == 0;
            if (succeeded) await store.SetDownloadedArtifactsLastCleanedAtAsync(now, cancellationToken).ConfigureAwait(false);
            return new DownloadCleanupResult(succeeded, deleted, filesystemRemaining.Length, recovered, filesystemRemaining.Sum(item => item.SizeBytes), residuals);
        }
        finally { cleanupGate.Release(); }
    }

    private static bool IsActivePath(string relativePath, IReadOnlySet<string> active) => active.Any(path => string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase) || relativePath.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    private static int PathDepth(string path) => path.Count(character => character is '\\' or '/') + 1;
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
    internal const uint DeleteAccess = 0x00010000, ReadData = 0x00000001, WriteData = 0x00000002, AddSubdirectory = 0x00000004, ReadAttributes = 0x00000080, WriteAttributes = 0x00000100, Synchronize = 0x00100000, ShareRead = 1, ShareWrite = 2, OpenExisting = 3, NtOpen = 1, NtCreate = 2, NtOpenIf = 3, OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfo = 4;
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.Bool)] public bool DeleteFile; }
    [StructLayout(LayoutKind.Sequential)] internal struct ByHandleFileInformation { public uint Attributes; public uint CreationTimeLow; public uint CreationTimeHigh; public uint LastAccessTimeLow; public uint LastAccessTimeHigh; public uint LastWriteTimeLow; public uint LastWriteTimeHigh; public uint VolumeSerialNumber; public uint FileSizeHigh; public uint FileSizeLow; public uint NumberOfLinks; public uint FileIndexHigh; public uint FileIndexLow; }
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

    internal static void DeleteRelativeObject(SafeFileHandle parent, string name, bool directory)
    {
        using SafeFileHandle handle = OpenRelativeObject(parent, name, DeleteAccess | ReadAttributes, directory);
        MarkForDeletion(handle);
    }

    internal static void MarkForDeletion(SafeFileHandle handle)
    {
        FileDisposition disposition = new() { DeleteFile = true };
        if (!SetFileInformationByHandle(handle, FileDispositionInfo, ref disposition, (uint)Marshal.SizeOf<FileDisposition>())) throw Error("Handle-bound deletion failed.");
    }

    internal static SafeFileHandle OpenVerifiedRoot(string root, uint finalAccess = ReadAttributes)
    {
        string volume = Path.GetPathRoot(root) ?? throw new SecurityException("A cache root volume is required.");
        string[] components = Path.GetRelativePath(volume, root).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        SafeFileHandle current = CreateFileW(volume, components.Length == 0 ? finalAccess : ReadAttributes, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, OpenReparsePoint | 0x02000000, IntPtr.Zero);
        if (current.IsInvalid) throw Error("Opening the cache volume failed.");
        try
        {
            for (int index = 0; index < components.Length; index++)
            {
                SafeFileHandle next = OpenRelativeObject(current, components[index], index == components.Length - 1 ? finalAccess : ReadAttributes, true);
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

    internal static SafeFileHandle OpenRelativeObject(SafeFileHandle parent, string name, uint access, bool directory, uint createDisposition = NtOpen)
    {
        IntPtr nameBuffer = Marshal.StringToHGlobalUni(name);
        try
        {
            UnicodeString unicode = new() { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)((name.Length + 1) * 2)), Buffer = nameBuffer };
            ObjectAttributes attributes = new() { Length = Marshal.SizeOf<ObjectAttributes>(), RootDirectory = parent.DangerousGetHandle(), ObjectName = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>()), Attributes = 0x40 };
            try
            {
                Marshal.StructureToPtr(unicode, attributes.ObjectName, false);
                int status = NtCreateFile(out SafeFileHandle handle, access | Synchronize, ref attributes, out _, IntPtr.Zero, 0, ShareRead | ShareWrite, createDisposition, (directory ? 0x1u : 0u) | 0x00200000u | 0x20u, IntPtr.Zero, 0);
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

    internal static ByHandleFileInformation GetInformation(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out ByHandleFileInformation information)) throw Error("Reading handle information failed.");
        return information;
    }

    [StructLayout(LayoutKind.Sequential)] private struct UnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)] private struct ObjectAttributes { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }
    [DllImport("ntdll.dll")] private static extern int NtCreateFile(out SafeFileHandle fileHandle, uint desiredAccess, ref ObjectAttributes objectAttributes, out IoStatusBlock ioStatusBlock, IntPtr allocationSize, uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);
    private static IOException Error(string message) { int code = Marshal.GetLastWin32Error(); return new IOException(message + " Win32=" + code, unchecked((int)(0x80070000u | (uint)code))); }
}

/// <summary>
/// Fixed-name reset control state.  The public reset API supplies only a root already validated
/// by <see cref="PathSafety"/>; every control child is then opened relative to that root handle.
/// </summary>
internal static class NativeResetControl
{
    private const string ResetDirectory = "reset";
    private const string Marker = "pending.v1.json";
    private const string Temporary = "pending.v1.tmp";
    // Bounded marker data includes fixed recovery metadata and five fixed category results.
    // It never holds a path or any deletion authority.
    private const int MaximumMarkerBytes = 2048;
    private const int FileRenameInformation = 10;
    private const int FileDirectoryInformation = 1;
    private const int StatusNoMoreFiles = unchecked((int)0x80000006);

    internal sealed record ControlSnapshot(bool ResetDirectoryExists, string? MarkerPayload, string? TemporaryPayload);
    // Test-only cutpoints: the handles are already bound before either callback runs.
    internal static Action? BeforeMarkerDispositionForTesting { get; set; }
    internal static Action? BeforeDirectoryDispositionForTesting { get; set; }
    internal static Action? BeforeProgressWriteForTesting { get; set; }
    internal static Action? BeforeFinalOnboardingRootVerificationForTesting { get; set; }
    internal static Action? BeforeOnboardingControlVerificationForTesting { get; set; }

    internal static ControlSnapshot Read(string root)
    {
        using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
        using SafeFileHandle? resetHandle = TryOpenReset(rootHandle);
        if (resetHandle is null) return new(false, null, null);
        HashSet<string> names = EnumerateExactChildren(resetHandle);
        return new(true, names.Contains(Marker) ? ReadLeaf(resetHandle, Marker) : null, names.Contains(Temporary) ? ReadLeaf(resetHandle, Temporary) : null);
    }

    internal static void CreateResetPending(string root)
    {
        using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
        using (SafeFileHandle? existing = TryOpenReset(rootHandle))
        {
            if (existing is not null) throw new InvalidOperationException("Reset control state already exists, including an empty crash-state directory.");
        }
        using SafeFileHandle resetHandle = OpenOrCreateReset(rootHandle);
        if (EnumerateExactChildren(resetHandle).Count != 0) throw new InvalidOperationException("Reset control state already exists or contains an unexpected child.");
        WriteNewLeaf(resetHandle, Temporary, FactoryResetMarker.CreatePending().ToPayload());
        RenameLeaf(resetHandle, Temporary, Marker, replace: false);
    }

    internal static void ReplaceWithOnboardingPending(string root, FactoryResetMarker marker)
    {
        using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
        EnsureOnlyResetControlAtRoot(rootHandle);
        using SafeFileHandle resetHandle = OpenExistingReset(rootHandle);
        HashSet<string> names = EnumerateExactChildren(resetHandle);
        if (!names.SetEquals([Marker])) throw new InvalidOperationException("Reset control state is not exactly the expected marker.");
        if (!FactoryResetMarker.TryParse(ReadLeaf(resetHandle, Marker), out _)) throw new InvalidOperationException("Reset control marker is invalid.");
        WriteNewLeaf(resetHandle, Temporary, marker.ToPayload());
        RenameLeaf(resetHandle, Temporary, Marker, replace: true);
        EnsureExactOnboardingControl(rootHandle, resetHandle);
    }

    internal static void ReplaceResetProgress(string root, FactoryResetMarker marker)
    {
        using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root);
        using SafeFileHandle resetHandle = OpenExistingReset(rootHandle);
        HashSet<string> names = EnumerateExactChildren(resetHandle);
        if (!names.SetEquals([Marker])) throw new InvalidOperationException("Reset control state is not exactly the expected marker.");
        if (!FactoryResetMarker.TryParse(ReadLeaf(resetHandle, Marker), out _)) throw new InvalidOperationException("Reset control marker is invalid.");
        BeforeProgressWriteForTesting?.Invoke();
        WriteNewLeaf(resetHandle, Temporary, marker.ToPayload());
        RenameLeaf(resetHandle, Temporary, Marker, replace: true);
    }

    internal static string ReadCurrentMarker(string root)
    {
        using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root);
        using SafeFileHandle resetHandle = OpenExistingReset(rootHandle);
        HashSet<string> names = EnumerateExactChildren(resetHandle);
        if (!names.SetEquals([Marker])) throw new InvalidOperationException("Reset control state is not exactly the expected marker.");
        return ReadLeaf(resetHandle, Marker);
    }

    /// <summary>A crash after durable temporary creation is recoverable without dropping authority.</summary>
    /// <returns>True when a previously-written onboarding temporary marker was atomically promoted.</returns>
    internal static bool PromoteTemporaryResetMarker(string root)
    {
        using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
        using SafeFileHandle resetHandle = OpenExistingReset(rootHandle);
        HashSet<string> names = EnumerateExactChildren(resetHandle);
        if (names.SetEquals([Temporary]))
        {
            if (!FactoryResetMarker.TryParse(ReadLeaf(resetHandle, Temporary), out FactoryResetMarker temporary)) throw new InvalidOperationException("Reset control temporary marker is invalid.");
            RenameLeaf(resetHandle, Temporary, Marker, replace: false);
            if (temporary.State != FactoryResetMarkerState.OnboardingPending) return false;
            BeforeOnboardingControlVerificationForTesting?.Invoke();
            EnsureExactOnboardingControl(rootHandle, resetHandle);
            return true;
        }
        if (names.SetEquals([Marker])) return false;
        if (names.SetEquals([Marker, Temporary]))
        {
            string marker = ReadLeaf(resetHandle, Marker);
            string temporary = ReadLeaf(resetHandle, Temporary);
            if (!FactoryResetMarker.TryParse(marker, out FactoryResetMarker current) || !FactoryResetMarker.TryParse(temporary, out FactoryResetMarker next) ||
                !current.SameReset(next) || current.State is not (FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial) ||
                next.State is not (FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.OnboardingPending))
                throw new InvalidOperationException("Reset control state contains an invalid transition combination.");
            RenameLeaf(resetHandle, Temporary, Marker, replace: true);
            if (next.State != FactoryResetMarkerState.OnboardingPending) return false;
            BeforeOnboardingControlVerificationForTesting?.Invoke();
            EnsureExactOnboardingControl(rootHandle, resetHandle);
            return true;
        }
        throw new InvalidOperationException("Reset control state is not exactly one recoverable marker.");
    }

    internal static void VerifyOnlyResetControl(string root, List<FactoryResetResidual> residuals)
    {
        try
        {
            using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root);
            using SafeFileHandle resetHandle = OpenExistingReset(rootHandle);
            HashSet<string> names = EnumerateExactChildren(resetHandle);
            if (!names.SetEquals([Marker])) throw new InvalidOperationException("Reset control state is not exactly the expected marker.");
            _ = ReadLeaf(resetHandle, Marker);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            residuals.Add(new FactoryResetResidual("Reset marker", "ControlStateVerificationFailed", exception.GetType().Name));
        }
    }

    internal static void VerifyOnlyExpectedRootChild(string root, List<FactoryResetResidual> residuals)
    {
        try
        {
            using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
            EnsureOnlyResetControlAtRoot(rootHandle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            residuals.Add(new FactoryResetResidual("State root", "UnexpectedOwnedObject", exception.GetType().Name));
        }
    }

    internal static void VerifyOnlyOnboardingRoot(string root, List<FactoryResetResidual> residuals)
    {
        try
        {
            using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
            EnsureOnlyOnboardingRoot(rootHandle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            residuals.Add(new FactoryResetResidual("State root", "UnexpectedOwnedObject", exception.GetType().Name));
        }
    }

    internal static void VerifyOnlyOnboardingBootstrapRoot(string root, List<FactoryResetResidual> residuals)
    {
        try
        {
            using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes);
            using SafeFileHandle resetHandle = OpenExistingReset(rootHandle);
            EnsureExactOnboardingControl(rootHandle, resetHandle);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            residuals.Add(new FactoryResetResidual("Reset marker", "ControlStateVerificationFailed", exception.GetType().Name));
        }
    }

    internal static PathSafety.DeleteResult AcknowledgeAndDelete(string root)
    {
        try
        {
            using SafeFileHandle rootHandle = NativeDelete.OpenVerifiedRoot(root, NativeDelete.ReadData | NativeDelete.ReadAttributes | NativeDelete.AddSubdirectory);
            EnsureOnlyOnboardingRoot(rootHandle);
            FactoryResetMarker? onboardingMarker = null;
            using (SafeFileHandle resetHandle = OpenExistingReset(rootHandle, NativeDelete.DeleteAccess | NativeDelete.ReadData | NativeDelete.ReadAttributes))
            {
                HashSet<string> names = EnumerateExactChildren(resetHandle);
                string authority = names.SetEquals([Marker]) ? Marker : names.SetEquals([Temporary]) ? Temporary : string.Empty;
                if (authority.Length == 0) return new(false, false, "UnexpectedControlState");
                using (SafeFileHandle markerHandle = NativeDelete.OpenRelativeObject(resetHandle, authority, NativeDelete.DeleteAccess | NativeDelete.ReadData | NativeDelete.ReadAttributes, directory: false))
                {
                    if (NativeDelete.GetInformation(markerHandle).NumberOfLinks != 1) return new(false, false, "UnsafePath");
                    if (!FactoryResetMarker.TryParse(ReadLeaf(markerHandle), out FactoryResetMarker parsedMarker) || parsedMarker.State != FactoryResetMarkerState.OnboardingPending) return new(false, false, "InvalidMarker");
                    onboardingMarker = parsedMarker;
                    BeforeMarkerDispositionForTesting?.Invoke();
                    NativeDelete.MarkForDeletion(markerHandle);
                }
                try
                {
                    BeforeDirectoryDispositionForTesting?.Invoke();
                    NativeDelete.MarkForDeletion(resetHandle);
                }
                catch
                {
                    // The marker is already unlinked.  Retain a durable, valid temporary authority
                    // through the still-bound reset handle before reporting failure.
                    try
                    {
                        if (onboardingMarker is not null) WriteNewLeaf(resetHandle, Temporary, onboardingMarker.ToPayload());
                    }
                    catch { }
                    throw;
                }
            }
            try
            {
                BeforeFinalOnboardingRootVerificationForTesting?.Invoke();
                // The root handle is still the same verified file object.  No reset control,
                // unexpected child, reparse point, type collision, or hard-linked SQLite leaf
                // can be acknowledged as success after the control directory is disposed.
                EnsureOnlyFreshStoreRoot(rootHandle);
                return new(true, false, "Deleted");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
            {
                try
                {
                    if (onboardingMarker is null) return new(false, false, "ControlStateRecoveryFailed");
                    RestoreOnboardingAuthority(rootHandle, onboardingMarker);
                    return new(false, false, "FinalRootVerificationFailed");
                }
                catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
                {
                    return new(false, false, "ControlStateRecoveryFailed");
                }
            }
        }
        catch (FileNotFoundException) { return new(false, true, "Absent"); }
        catch (DirectoryNotFoundException) { return new(false, true, "Absent"); }
        catch (SecurityException) { return new(false, false, "UnsafePath"); }
        catch (UnauthorizedAccessException) { return new(false, false, "AccessDenied"); }
        catch (IOException exception) { return new(false, false, Classify(exception)); }
    }

    private static SafeFileHandle OpenOrCreateReset(SafeFileHandle root) => NativeDelete.OpenRelativeObject(root, ResetDirectory, NativeDelete.ReadData | NativeDelete.ReadAttributes | NativeDelete.AddSubdirectory, directory: true, createDisposition: NativeDelete.NtOpenIf);
    private static SafeFileHandle OpenExistingReset(SafeFileHandle root, uint access = NativeDelete.ReadData | NativeDelete.ReadAttributes) => NativeDelete.OpenRelativeObject(root, ResetDirectory, access, directory: true);
    private static SafeFileHandle? TryOpenReset(SafeFileHandle root)
    {
        try { return OpenExistingReset(root); }
        catch (IOException exception) when ((uint)exception.HResult == 0xC0000034u || (exception.HResult & 0xFFFF) is 2 or 3) { return null; }
    }

    private static HashSet<string> EnumerateExactChildren(SafeFileHandle reset)
    {
        HashSet<string> names = EnumerateNames(reset).ToHashSet(StringComparer.Ordinal);
        if (names.Any(name => name is not Marker and not Temporary)) throw new SecurityException("The reset control directory contains an unknown child.");
        return names;
    }

    private static void EnsureOnlyResetControlAtRoot(SafeFileHandle root)
    {
        HashSet<string> names = EnumerateNames(root).ToHashSet(StringComparer.Ordinal);
        if (!names.SetEquals([ResetDirectory])) throw new UnexpectedOwnedRootException();
    }

    private static void EnsureExactOnboardingControl(SafeFileHandle root, SafeFileHandle boundReset)
    {
        EnsureOnlyResetControlAtRoot(root);
        using SafeFileHandle namedReset = OpenExistingReset(root);
        NativeDelete.ByHandleFileInformation expected = NativeDelete.GetInformation(boundReset);
        NativeDelete.ByHandleFileInformation current = NativeDelete.GetInformation(namedReset);
        if (expected.VolumeSerialNumber != current.VolumeSerialNumber || expected.FileIndexHigh != current.FileIndexHigh || expected.FileIndexLow != current.FileIndexLow)
            throw new SecurityException("The reset control directory was replaced during verification.");
        HashSet<string> names = EnumerateExactChildren(namedReset);
        if (!names.SetEquals([Marker]) || !FactoryResetMarker.TryParse(ReadLeaf(namedReset, Marker), out FactoryResetMarker marker) || marker.State != FactoryResetMarkerState.OnboardingPending)
            throw new InvalidOperationException("Reset control state is not the exact onboarding marker.");
    }

    private static void EnsureOnlyOnboardingRoot(SafeFileHandle root)
    {
        HashSet<string> names = EnumerateNames(root).ToHashSet(StringComparer.Ordinal);
        string[] allowed = [ResetDirectory, "pcsteward.db", "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal"];
        if (!names.IsSubsetOf(allowed)) throw new UnexpectedOwnedRootException();
        ValidateFreshSqliteLeaves(root, names);
    }

    private static void EnsureOnlyFreshStoreRoot(SafeFileHandle root)
    {
        HashSet<string> names = EnumerateNames(root).ToHashSet(StringComparer.Ordinal);
        string[] allowed = ["pcsteward.db", "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal"];
        if (!names.IsSubsetOf(allowed)) throw new UnexpectedOwnedRootException();
        ValidateFreshSqliteLeaves(root, names);
    }

    private static void ValidateFreshSqliteLeaves(SafeFileHandle root, HashSet<string> names)
    {
        foreach (string leafName in new[] { "pcsteward.db", "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal" })
        {
            if (!names.Contains(leafName)) continue;
            using SafeFileHandle leaf = NativeDelete.OpenRelativeObject(root, leafName, NativeDelete.ReadAttributes, directory: false);
            if (NativeDelete.GetInformation(leaf).NumberOfLinks != 1) throw new SecurityException("A fresh SQLite leaf has an unsafe link count.");
        }
    }

    private static void RestoreOnboardingAuthority(SafeFileHandle root, FactoryResetMarker marker)
    {
        using SafeFileHandle? existing = TryOpenReset(root);
        if (existing is not null)
        {
            HashSet<string> names = EnumerateExactChildren(existing);
            if (names.SetEquals([Marker]) && FactoryResetMarker.TryParse(ReadLeaf(existing, Marker), out FactoryResetMarker current) && current.State == FactoryResetMarkerState.OnboardingPending) return;
            if (names.Count != 0) throw new InvalidOperationException("A reset control collision prevents recovery authority restoration.");
            WriteNewLeaf(existing, Temporary, marker.ToPayload());
            RenameLeaf(existing, Temporary, Marker, replace: false);
            return;
        }

        using SafeFileHandle reset = OpenOrCreateReset(root);
        if (EnumerateExactChildren(reset).Count != 0) throw new InvalidOperationException("A reset control collision prevents recovery authority restoration.");
        WriteNewLeaf(reset, Temporary, marker.ToPayload());
        RenameLeaf(reset, Temporary, Marker, replace: false);
    }

    private static string ReadLeaf(SafeFileHandle reset, string name)
    {
        using SafeFileHandle leaf = NativeDelete.OpenRelativeObject(reset, name, NativeDelete.ReadData | NativeDelete.ReadAttributes, directory: false);
        return ReadLeaf(leaf);
    }

    private static string ReadLeaf(SafeFileHandle leaf)
    {
        NativeDelete.ByHandleFileInformation info = NativeDelete.GetInformation(leaf);
        long bytes = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        if (info.NumberOfLinks != 1 || bytes is <= 0 or > MaximumMarkerBytes) throw new SecurityException("A reset marker has an unsafe link count or size.");
        byte[] buffer = new byte[bytes];
        if (!ReadFile(leaf, buffer, (uint)buffer.Length, out uint read, IntPtr.Zero) || read != buffer.Length) throw Error("Reading the reset marker through its bound handle failed.");
        return Encoding.UTF8.GetString(buffer);
    }

    private static void WriteNewLeaf(SafeFileHandle reset, string name, string payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        if (bytes.Length > MaximumMarkerBytes) throw new InvalidOperationException("The fixed reset marker exceeds its bounded size.");
        using SafeFileHandle leaf = NativeDelete.OpenRelativeObject(reset, name, NativeDelete.ReadData | NativeDelete.WriteData | NativeDelete.ReadAttributes | NativeDelete.WriteAttributes, directory: false, createDisposition: NativeDelete.NtCreate);
        NativeDelete.ByHandleFileInformation info = NativeDelete.GetInformation(leaf);
        if (info.NumberOfLinks != 1) throw new SecurityException("A reset marker with multiple links is unsafe.");
        if (!WriteFile(leaf, bytes, (uint)bytes.Length, out uint written, IntPtr.Zero) || written != bytes.Length || !FlushFileBuffers(leaf)) throw Error("Writing the durable reset marker through its bound handle failed.");
    }

    private static void RenameLeaf(SafeFileHandle reset, string source, string target, bool replace)
    {
        using SafeFileHandle leaf = NativeDelete.OpenRelativeObject(reset, source, NativeDelete.DeleteAccess | NativeDelete.ReadAttributes, directory: false);
        NativeDelete.ByHandleFileInformation info = NativeDelete.GetInformation(leaf);
        if (info.NumberOfLinks != 1) throw new SecurityException("A reset marker with multiple links is unsafe.");
        int rootOffset = (int)Marshal.OffsetOf<FileRenameInformationData>(nameof(FileRenameInformationData.RootDirectory));
        int nameOffset = (int)Marshal.OffsetOf<FileRenameInformationData>(nameof(FileRenameInformationData.FileName));
        byte[] name = Encoding.Unicode.GetBytes(target);
        IntPtr buffer = Marshal.AllocHGlobal(nameOffset + name.Length);
        try
        {
            Marshal.Copy(new byte[nameOffset + name.Length], 0, buffer, nameOffset + name.Length);
            Marshal.WriteInt32(buffer, replace ? 1 : 0);
            Marshal.WriteIntPtr(buffer, rootOffset, reset.DangerousGetHandle());
            Marshal.WriteInt32(buffer, nameOffset - sizeof(uint), name.Length);
            Marshal.Copy(name, 0, IntPtr.Add(buffer, nameOffset), name.Length);
            int status = NtSetInformationFile(leaf, out _, buffer, (uint)(nameOffset + name.Length), FileRenameInformation);
            if (status != 0) throw new IOException("Atomically replacing the bound reset marker failed. NTSTATUS=" + status.ToString("X8", CultureInfo.InvariantCulture), status);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static IEnumerable<string> EnumerateNames(SafeFileHandle directory)
    {
        const int bufferSize = 4096;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            bool restart = true;
            while (true)
            {
                int status = NtQueryDirectoryFile(directory, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, buffer, bufferSize, FileDirectoryInformation, false, IntPtr.Zero, restart);
                restart = false;
                if (status == StatusNoMoreFiles) yield break;
                if (status != 0) throw new IOException("Enumerating reset control state through its bound handle failed. NTSTATUS=" + status.ToString("X8", CultureInfo.InvariantCulture), status);
                int offset = 0;
                while (true)
                {
                    uint next = unchecked((uint)Marshal.ReadInt32(buffer, offset));
                    uint attributes = unchecked((uint)Marshal.ReadInt32(buffer, offset + 56));
                    int nameBytes = Marshal.ReadInt32(buffer, offset + 60);
                    if (nameBytes < 0 || nameBytes > 1024 || (nameBytes & 1) != 0 || (attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new SecurityException("An unsafe reset control entry was enumerated.");
                    string name = Marshal.PtrToStringUni(IntPtr.Add(buffer, offset + 64), nameBytes / 2) ?? throw new SecurityException("An unnamed reset control entry was enumerated.");
                    if (name is not "." and not "..") yield return name;
                    if (next == 0) break;
                    if (next > bufferSize - offset) throw new SecurityException("A malformed reset control enumeration was returned.");
                    offset += checked((int)next);
                }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct FileRenameInformationData { public int ReplaceIfExists; public IntPtr RootDirectory; public uint FileNameLength; public char FileName; }
    [StructLayout(LayoutKind.Sequential)] private struct IoStatusBlock { public IntPtr Status; public UIntPtr Information; }
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, uint count, out uint read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool WriteFile(SafeFileHandle file, byte[] buffer, uint count, out uint written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool FlushFileBuffers(SafeFileHandle file);
    [DllImport("ntdll.dll")] private static extern int NtSetInformationFile(SafeFileHandle file, out IoStatusBlock ioStatus, IntPtr information, uint length, int informationClass);
    [DllImport("ntdll.dll")] private static extern int NtQueryDirectoryFile(SafeFileHandle file, IntPtr evt, IntPtr apcRoutine, IntPtr apcContext, out IoStatusBlock ioStatus, IntPtr information, uint length, int informationClass, [MarshalAs(UnmanagedType.Bool)] bool returnSingleEntry, IntPtr fileName, [MarshalAs(UnmanagedType.Bool)] bool restartScan);
    private static IOException Error(string message) { int code = Marshal.GetLastWin32Error(); return new IOException(message + " Win32=" + code, unchecked((int)(0x80070000u | (uint)code))); }
    private static string Classify(IOException exception) { int code = exception.HResult & 0xFFFF; return code is 32 or 33 or 67 ? "LockedOrInUse" : "IoError:" + code; }
}

internal sealed class UnexpectedOwnedRootException : SecurityException
{
    internal UnexpectedOwnedRootException() : base("The owned state root contains an unclassified direct child.") { }
}
