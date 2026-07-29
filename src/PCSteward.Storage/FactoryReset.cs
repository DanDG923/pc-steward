using System.Text;

namespace PCSteward.Storage;

/// <summary>Fixed, per-user reset protocol. It accepts no caller supplied deletion paths.</summary>
public static class FactoryReset
{
    public const string MarkerFileName = "pending.v1.json";
    public const string TemporaryMarkerFileName = "pending.v1.tmp";
    public const string DataOperationMutexName = "Local\\PCSteward.DataOperation.v1";

    public static string GetStateRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward");
    public static string GetMarkerPath(string? stateRoot = null) => Path.Combine(stateRoot ?? GetStateRoot(), "reset", MarkerFileName);

    public static bool IsPending(string? stateRoot = null)
    {
        string root = stateRoot ?? GetStateRoot();
        return Exists(Path.Combine(root, "reset", MarkerFileName)) || Exists(Path.Combine(root, "reset", TemporaryMarkerFileName));
    }

    /// <summary>Point of no return. No deletion is performed by this method.</summary>
    public static void Arm(string? stateRoot = null)
    {
        ArmCore(ValidateRoot(stateRoot ?? GetStateRoot()));
    }

    private static void ArmCore(string root)
    {
        string reset = Path.Combine(root, "reset");
        Directory.CreateDirectory(reset);
        string temporary = Path.Combine(reset, TemporaryMarkerFileName);
        string marker = Path.Combine(reset, MarkerFileName);
        using (System.IO.FileStream stream = global::System.IO.File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] payload = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"state\":\"ResetPending\"}");
            stream.Write(payload, 0, payload.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, marker, overwrite: false);
    }

    /// <summary>Runs only when a reset sentinel already exists. Residuals keep the sentinel in place.</summary>
    public static async Task<FactoryResetResult> CompletePendingAsync(Func<CancellationToken, Task>? removeScheduledTask, CancellationToken cancellationToken, string? stateRoot = null)
    {
        string root;
        try { root = ValidateRoot(stateRoot ?? GetStateRoot()); }
        catch (Exception exception) { return new(false, [new FactoryResetResidual("State root", "UnsafePath", exception.Message)]); }
        return await CompletePendingCoreAsync(root, removeScheduledTask, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<FactoryResetResult> CompletePendingCoreAsync(string root, Func<CancellationToken, Task>? removeScheduledTask, CancellationToken cancellationToken)
    {
        if (!IsPending(root)) return new(true, []);

        using Mutex mutex = new(false, DataOperationMutexName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }
        try
        {
            List<FactoryResetResidual> residuals = [];
            if (removeScheduledTask is not null)
            {
                try { await removeScheduledTask(cancellationToken).ConfigureAwait(false); }
                catch (Exception exception) { residuals.Add(new("Scheduled check", "ScheduleRemovalFailed", exception.Message)); }
            }
            DeleteTree(root, "downloads", residuals);
            DeleteTree(root, "logs", residuals);
            foreach (string leaf in new[] { "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal", "pcsteward.db" }) DeleteLeaf(root, leaf, residuals);
            VerifyAbsent(root, "downloads", residuals);
            VerifyAbsent(root, "logs", residuals);
            foreach (string leaf in new[] { "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal", "pcsteward.db" }) VerifyAbsent(root, leaf, residuals);
            if (residuals.Count != 0) return new(false, residuals);

            DeleteLeaf(Path.Combine(root, "reset"), MarkerFileName, residuals);
            DeleteLeaf(Path.Combine(root, "reset"), TemporaryMarkerFileName, residuals);
            if (Directory.Exists(Path.Combine(root, "reset")) && !Directory.EnumerateFileSystemEntries(Path.Combine(root, "reset")).Any()) Directory.Delete(Path.Combine(root, "reset"));
            return new(residuals.Count == 0, residuals);
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static Task<FactoryResetResult> CompletePendingAsync(CancellationToken cancellationToken, string? stateRoot = null) => CompletePendingAsync(null, cancellationToken, stateRoot);

    /// <summary>Routine log clear uses the same literal root and non-following primitive as reset.</summary>
    public static Task<FactoryResetResult> ClearLogsAsync(CancellationToken cancellationToken)
    {
        string root;
        try { root = ValidateRoot(GetStateRoot()); }
        catch (Exception exception) { return Task.FromResult(new FactoryResetResult(false, [new FactoryResetResidual("Logs", "UnsafePath", exception.Message)])); }
        List<FactoryResetResidual> residuals = [];
        DeleteTree(root, "logs", residuals);
        VerifyAbsent(root, "logs", residuals);
        return Task.FromResult(new FactoryResetResult(residuals.Count == 0, residuals));
    }

    // Test-only seam. Production callers cannot supply deletion roots; this remains internal and
    // exercises the same non-following primitive under a disposable fixture root.
    internal static void ArmForTesting(string stateRoot) => ArmCore(ValidateRoot(stateRoot, allowTestRoot: true));
    internal static Task<FactoryResetResult> CompletePendingForTestingAsync(string stateRoot, Func<CancellationToken, Task>? removeScheduledTask, CancellationToken cancellationToken) => CompletePendingCoreAsync(ValidateRoot(stateRoot, allowTestRoot: true), removeScheduledTask, cancellationToken);

    private static string ValidateRoot(string root, bool allowTestRoot = false)
    {
        string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetStateRoot()));
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!allowTestRoot && !string.Equals(expected, candidate, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Factory reset only accepts the compiled PC Steward state root.");
        return PathSafety.EnsureRoot(candidate);
    }

    private static void DeleteTree(string root, string child, List<FactoryResetResidual> residuals)
    {
        string path = Path.Combine(root, child);
        if (!Exists(path)) return;
        foreach (FileSystemObject item in PathSafety.EnumerateOwnedObjects(path).OrderByDescending(item => item.RelativePath.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar)).ThenByDescending(item => item.IsDirectory))
        {
            if (item.IsUnsafe) { residuals.Add(new(child, "UnsafeReparsePoint", item.RelativePath)); continue; }
            PathSafety.DeleteResult result = PathSafety.DeleteExactObject(path, item.RelativePath, item.IsDirectory);
            if (!result.Deleted && !result.AlreadyAbsent) residuals.Add(new(child, result.Reason, item.RelativePath));
        }
        if (residuals.All(item => item.Category != child))
        {
            try { Directory.Delete(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { residuals.Add(new(child, "LockedOrInUse", exception.Message)); }
        }
    }

    private static void DeleteLeaf(string root, string leaf, List<FactoryResetResidual> residuals)
    {
        if (!Exists(Path.Combine(root, leaf))) return;
        PathSafety.DeleteResult result = PathSafety.DeleteExactFile(root, leaf);
        if (!result.Deleted && !result.AlreadyAbsent) residuals.Add(new(leaf, result.Reason, leaf));
    }

    private static void VerifyAbsent(string root, string child, List<FactoryResetResidual> residuals)
    {
        string path = Path.Combine(root, child);
        if (!Exists(path)) return;
        try
        {
            if (Directory.EnumerateFileSystemEntries(path).Any()) residuals.Add(new(child, "VerificationFailed", "Residual objects remain."));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { residuals.Add(new(child, "VerificationFailed", exception.Message)); }
    }

    private static bool Exists(string path) { try { _ = File.GetAttributes(path); return true; } catch (FileNotFoundException) { return false; } catch (DirectoryNotFoundException) { return false; } }
}

public sealed record FactoryResetResidual(string Category, string Reason, string Detail);
public sealed record FactoryResetResult(bool Succeeded, IReadOnlyCollection<FactoryResetResidual> Residuals);
