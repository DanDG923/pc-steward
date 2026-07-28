using System.Runtime.InteropServices;
using System.Security;
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
        return new DownloadCacheSummary(artifacts.Sum(artifact => artifact.SizeBytes), artifacts.Where(artifact => !artifact.IsActive).Sum(artifact => artifact.SizeBytes), artifacts.Length, artifacts.Count(artifact => artifact.IsActive), await store.GetDownloadedArtifactsLastCleanedAtAsync(cancellationToken).ConfigureAwait(false));
    }

    public Task<DownloadCleanupResult> ClearInactiveAsync(CancellationToken cancellationToken) => CleanupAsync(clearAllInactive: true, DateTimeOffset.UtcNow, cancellationToken);

    public Task<DownloadCleanupResult> EnforceRetentionAndQuotaAsync(DateTimeOffset now, CancellationToken cancellationToken) => CleanupAsync(clearAllInactive: false, now, cancellationToken);

    private async Task<DownloadCleanupResult> CleanupAsync(bool clearAllInactive, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await cleanupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DownloadedArtifact[] all = (await store.GetDownloadedArtifactsAsync(cancellationToken).ConfigureAwait(false)).OrderBy(artifact => artifact.CreatedAt).ThenBy(artifact => artifact.Id).ToArray();
            long retainedBytes = all.Sum(artifact => artifact.SizeBytes);
            List<DownloadCleanupResidual> residuals = [];
            int deleted = 0;
            long recovered = 0;
            foreach (DownloadedArtifact artifact in all.Where(artifact => !artifact.IsActive))
            {
                bool expired = artifact.Outcome == DownloadedArtifactOutcome.VerifiedSuccess ? artifact.CreatedAt < now.AddDays(-7) : artifact.CreatedAt < now.AddDays(-1);
                bool quotaExceeded = retainedBytes > SoftQuotaBytes;
                if (!clearAllInactive && !expired && !quotaExceeded) continue;
                PathSafety.DeleteResult result = PathSafety.DeleteExactFile(CacheRoot, artifact.RelativePath);
                if (result.Deleted || result.AlreadyAbsent)
                {
                    await store.DeleteDownloadedArtifactAsync(artifact.Id, cancellationToken).ConfigureAwait(false);
                    deleted++;
                    recovered += result.Deleted ? artifact.SizeBytes : 0;
                    retainedBytes -= artifact.SizeBytes;
                }
                else residuals.Add(new DownloadCleanupResidual(artifact.RelativePath, result.Reason));
            }

            DownloadedArtifact[] remaining = (await store.GetDownloadedArtifactsAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            if (residuals.Count == 0) await store.SetDownloadedArtifactsLastCleanedAtAsync(now, cancellationToken).ConfigureAwait(false);
            long remainingBytes = remaining.Sum(artifact => artifact.SizeBytes);
            return new DownloadCleanupResult(residuals.Count == 0, deleted, remaining.Length, recovered, remainingBytes, residuals);
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

    internal static DeleteResult DeleteExactFile(string root, string relativePath)
    {
        try
        {
            string path = ResolveLedgerPath(root, relativePath);
            if (!Exists(path)) return new(false, true, "Absent");
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return new(false, false, "UnsafePath");
            // Resolve and inspect immediately before acquiring a non-following delete handle.
            _ = ResolveLedgerPath(root, relativePath);
            NativeDelete.DeleteExactObject(path);
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

    private static bool Exists(string path) { try { _ = File.GetAttributes(path); return true; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } }
    private static string Classify(IOException exception) { int code = exception.HResult & 0xFFFF; return code is 32 or 33 ? "LockedOrInUse" : "IoError:" + code; }
}

internal static class NativeDelete
{
    private const uint DeleteAccess = 0x00010000, ReadAttributes = 0x00000080, ShareRead = 1, ShareWrite = 2, OpenExisting = 3, OpenReparsePoint = 0x00200000;
    private const int FileDispositionInfo = 4;
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.Bool)] public bool DeleteFile; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
    [DllImport("kernel32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass, ref FileDisposition fileInformation, uint bufferSize);
    internal static void DeleteExactObject(string path)
    {
        using SafeFileHandle handle = CreateFileW(path, DeleteAccess | ReadAttributes, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw Error("Opening the exact object for deletion failed.");
        FileDisposition disposition = new() { DeleteFile = true };
        if (!SetFileInformationByHandle(handle, FileDispositionInfo, ref disposition, (uint)Marshal.SizeOf<FileDisposition>())) throw Error("Handle-bound deletion failed.");
    }
    private static IOException Error(string message) { int code = Marshal.GetLastWin32Error(); return new IOException(message + " Win32=" + code, unchecked((int)(0x80070000u | (uint)code))); }
}
