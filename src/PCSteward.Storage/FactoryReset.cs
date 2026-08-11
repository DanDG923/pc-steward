using System.Text;
using System.Text.Json;
using System.Security;
using System.Globalization;

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
        return GetMarkerState(stateRoot) is not FactoryResetMarkerState.None;
    }

    public static FactoryResetMarkerState GetMarkerState(string? stateRoot = null)
    {
        List<FactoryResetResidual> residuals = [];
        return ReadMarkerState(stateRoot ?? GetStateRoot(), residuals);
    }

    /// <summary>Point of no return. No deletion is performed by this method.</summary>
    public static void Arm(string? stateRoot = null)
    {
        string root = ValidateRoot(stateRoot ?? GetStateRoot());
        using Mutex mutex = new(false, DataOperationMutexName);
        if (!TryAcquire(mutex)) throw new InvalidOperationException("PC Steward still has an active data operation. Wait for it to finish before starting factory reset.");
        try { ArmCore(root); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void ArmCore(string root)
    {
        try
        {
            NativeResetControl.CreateResetPending(root);
        }
        catch
        {
            // A temporary marker is itself recovery authority.  Never remove it through a
            // path after a failed bounded operation: bootstrap will validate and recover it.
            throw;
        }
    }

    /// <summary>Runs only when a reset sentinel already exists. Residuals keep the sentinel in place.</summary>
    public static async Task<FactoryResetResult> CompletePendingAsync(Func<CancellationToken, Task>? removeScheduledTask, CancellationToken cancellationToken, string? stateRoot = null)
    {
        string root;
        try { root = ValidateRoot(stateRoot ?? GetStateRoot()); }
        catch (Exception exception) { return new(false, [new FactoryResetResidual("State root", "UnsafePath", exception.Message)]); }
        return await CompletePendingCoreAsync(root, removeScheduledTask, cancellationToken).ConfigureAwait(false);
    }

    private static Task<FactoryResetResult> CompletePendingCoreAsync(string root, Func<CancellationToken, Task>? removeScheduledTask, CancellationToken cancellationToken)
    {
        if (!IsPending(root)) return Task.FromResult(new FactoryResetResult(true, []));

        using Mutex mutex = new(false, DataOperationMutexName);
        if (!TryAcquire(mutex)) return Task.FromResult(new FactoryResetResult(false, [new FactoryResetResidual("Data operation", "ActiveOperation", "Another PC Steward process still owns the data-operation lease.")]));
        try
        {
            List<FactoryResetResidual> residuals = [];
            FactoryResetMarkerState markerState = ReadMarkerState(root, residuals);
            if (markerState == FactoryResetMarkerState.OnboardingPending)
            {
                // A durable onboarding marker is accepted only while the fixed root remains
                // exactly the reset control directory.  This keeps a recoverable transition or
                // hostile same-user marker write from bypassing the manifest verification.
                NativeResetControl.VerifyOnlyExpectedRootChild(root, residuals);
                return Task.FromResult(new FactoryResetResult(residuals.Count == 0, residuals));
            }
            if (markerState is not (FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.TransitionPending)) return Task.FromResult(new FactoryResetResult(false, residuals));
            try
            {
                bool transitionCompleted = NativeResetControl.PromoteTemporaryResetMarker(root);
                if (transitionCompleted)
                {
                    NativeResetControl.VerifyOnlyExpectedRootChild(root, residuals);
                    return Task.FromResult(new FactoryResetResult(residuals.Count == 0, residuals));
                }
            }
            catch (Exception exception)
            {
                residuals.Add(exception is UnexpectedOwnedRootException
                    ? new FactoryResetResidual("State root", "UnexpectedOwnedObject", exception.GetType().Name)
                    : new FactoryResetResidual("Reset marker", "ControlStateRecoveryFailed", exception.GetType().Name));
                return Task.FromResult(new FactoryResetResult(false, residuals));
            }
            if (!TryReadCurrentMarker(root, residuals, out FactoryResetMarker marker)) return Task.FromResult(new FactoryResetResult(false, residuals));

            // A durable attempt boundary makes a restart distinguishable from the original arm.
            // It is written before work starts; a write failure remains blocking and never widens
            // the manifest, but completed safe deletions may still be retried idempotently.
            if (!marker.TryBeginAttempt(out marker))
            {
                residuals.Add(new("Reset marker", "AttemptLimitReached", "The bounded reset retry limit was reached."));
                PersistProgress(root, marker, residuals);
                return Task.FromResult(new FactoryResetResult(false, residuals));
            }
            PersistProgress(root, marker, residuals);
            int categoryStart = residuals.Count;
            if (removeScheduledTask is not null)
            {
                // Mutex ownership is thread-affine.  Keep cleanup synchronous while the
                // bootstrap thread owns it; asynchronous scheduler work may complete on
                // another thread but does not own this mutex.
                try { removeScheduledTask(cancellationToken).GetAwaiter().GetResult(); }
                catch (Exception exception) { residuals.Add(new("Scheduled check", "ScheduleRemovalFailed", exception.Message)); }
            }
            marker = RecordCategory(root, marker, FactoryResetCategory.ScheduledCheck, categoryStart, residuals);
            categoryStart = residuals.Count;
            DeleteTree(root, "downloads", residuals);
            VerifyAbsent(root, "downloads", residuals);
            marker = RecordCategory(root, marker, FactoryResetCategory.Downloads, categoryStart, residuals);
            categoryStart = residuals.Count;
            DeleteTree(root, "logs", residuals);
            VerifyAbsent(root, "logs", residuals);
            marker = RecordCategory(root, marker, FactoryResetCategory.Logs, categoryStart, residuals);
            categoryStart = residuals.Count;
            foreach (string leaf in new[] { "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal", "pcsteward.db" }) DeleteLeaf(root, leaf, residuals);
            foreach (string leaf in new[] { "pcsteward.db-wal", "pcsteward.db-shm", "pcsteward.db-journal", "pcsteward.db" }) VerifyAbsent(root, leaf, residuals);
            marker = RecordCategory(root, marker, FactoryResetCategory.Database, categoryStart, residuals);
            categoryStart = residuals.Count;
            NativeResetControl.VerifyOnlyResetControl(root, residuals);
            NativeResetControl.VerifyOnlyExpectedRootChild(root, residuals);
            marker = RecordCategory(root, marker, FactoryResetCategory.OwnedRoot, categoryStart, residuals);
            if (residuals.Count != 0) return Task.FromResult(new FactoryResetResult(false, residuals));

            try { NativeResetControl.ReplaceWithOnboardingPending(root, marker.WithState(FactoryResetMarkerState.OnboardingPending)); }
            catch (Exception exception) { residuals.Add(new("Reset marker", "ControlStateWriteFailed", exception.GetType().Name)); }
            return Task.FromResult(new FactoryResetResult(residuals.Count == 0, residuals));
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static Task<FactoryResetResult> CompletePendingAsync(CancellationToken cancellationToken, string? stateRoot = null) => CompletePendingAsync(null, cancellationToken, stateRoot);

    /// <summary>Validates the exact durable onboarding authority before a fresh store is created.</summary>
    public static FactoryResetResult VerifyOnboardingBootstrap(string? stateRoot = null)
    {
        string root;
        try { root = ValidateRoot(stateRoot ?? GetStateRoot()); }
        catch (Exception exception) { return new(false, [new FactoryResetResidual("State root", "UnsafePath", exception.Message)]); }
        return VerifyOnboardingBootstrapCore(root);
    }

    /// <summary>Only the explicit first-run acknowledgement can remove the durable reset authority.</summary>
    public static FactoryResetResult AcknowledgeOnboarding(string? stateRoot = null)
    {
        string root;
        try { root = ValidateRoot(stateRoot ?? GetStateRoot()); }
        catch (Exception exception) { return new(false, [new FactoryResetResidual("State root", "UnsafePath", exception.Message)]); }
        return AcknowledgeOnboardingCore(root);
    }

    private static FactoryResetResult AcknowledgeOnboardingCore(string root)
    {
        using Mutex mutex = new(false, DataOperationMutexName);
        if (!TryAcquire(mutex)) return new(false, [new FactoryResetResidual("Data operation", "ActiveOperation", "Another PC Steward process still owns the data-operation lease.")]);
        try
        {
            List<FactoryResetResidual> residuals = [];
            if (ReadMarkerState(root, residuals) is not FactoryResetMarkerState.OnboardingPending) return new(false, residuals);
            NativeResetControl.VerifyOnlyOnboardingRoot(root, residuals);
            if (residuals.Count != 0) return new(false, residuals);
            PathSafety.DeleteResult result = NativeResetControl.AcknowledgeAndDelete(root);
            if (!result.Deleted) residuals.Add(new("Reset marker", result.Reason, MarkerFileName));
            return new(residuals.Count == 0, residuals);
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static FactoryResetResult VerifyOnboardingBootstrapCore(string root)
    {
        List<FactoryResetResidual> residuals = [];
        if (ReadMarkerState(root, residuals) is not FactoryResetMarkerState.OnboardingPending) return new(false, residuals);
        NativeResetControl.VerifyOnlyOnboardingRoot(root, residuals);
        return new(residuals.Count == 0, residuals);
    }

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
    internal static void ArmForTesting(string stateRoot)
    {
        using Mutex mutex = new(false, DataOperationMutexName);
        if (!TryAcquire(mutex)) throw new InvalidOperationException("PC Steward still has an active data operation. Wait for it to finish before starting factory reset.");
        try { ArmCore(ValidateRoot(stateRoot, allowTestRoot: true)); }
        finally { mutex.ReleaseMutex(); }
    }
    internal static Task<FactoryResetResult> CompletePendingForTestingAsync(string stateRoot, Func<CancellationToken, Task>? removeScheduledTask, CancellationToken cancellationToken) => CompletePendingCoreAsync(ValidateRoot(stateRoot, allowTestRoot: true), removeScheduledTask, cancellationToken);
    internal static FactoryResetResult AcknowledgeOnboardingForTesting(string stateRoot) => AcknowledgeOnboardingCore(ValidateRoot(stateRoot, allowTestRoot: true));
    internal static FactoryResetResult VerifyOnboardingBootstrapForTesting(string stateRoot) => VerifyOnboardingBootstrapCore(ValidateRoot(stateRoot, allowTestRoot: true));

    private static string ValidateRoot(string root, bool allowTestRoot = false)
    {
        string expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(GetStateRoot()));
        string candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (allowTestRoot)
        {
            string testParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
            string? candidateParent = Path.GetDirectoryName(candidate);
            string candidateName = Path.GetFileName(candidate);
            if (!string.Equals(testParent, candidateParent, StringComparison.OrdinalIgnoreCase) ||
                !candidateName.StartsWith("pcsteward-reset-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Factory reset tests accept only direct, dedicated PC Steward temporary roots.");
        }
        else if (!string.Equals(expected, candidate, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Factory reset only accepts the compiled PC Steward state root.");
        return PathSafety.EnsureRoot(candidate);
    }

    private static bool TryAcquire(Mutex mutex)
    {
        try { return mutex.WaitOne(0); }
        catch (AbandonedMutexException) { return true; }
    }

    private static FactoryResetMarkerState ReadMarkerState(string root, List<FactoryResetResidual> residuals)
    {
        try
        {
            // A first-ever launch has no application state root yet.  Distinguish that exact
            // absence from an inaccessible or unsafe existing root; the latter must continue
            // through the handle-bound reader and fail closed.
            try { _ = File.GetAttributes(root); }
            catch (DirectoryNotFoundException) { return FactoryResetMarkerState.None; }
            catch (FileNotFoundException) { return FactoryResetMarkerState.None; }

            NativeResetControl.ControlSnapshot snapshot = NativeResetControl.Read(root);
            if (!snapshot.ResetDirectoryExists) return FactoryResetMarkerState.None;
            if (snapshot.MarkerPayload is null && snapshot.TemporaryPayload is null) return InvalidMarker(residuals, "The reset control directory is empty.");
            if (snapshot.MarkerPayload is not null && snapshot.TemporaryPayload is not null)
            {
                if (FactoryResetMarker.TryParse(snapshot.MarkerPayload, out FactoryResetMarker primaryMarker) &&
                    FactoryResetMarker.TryParse(snapshot.TemporaryPayload, out FactoryResetMarker temporary) &&
                    primaryMarker.SameReset(temporary) &&
                    primaryMarker.State is FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial &&
                    temporary.State is FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.OnboardingPending)
                    return FactoryResetMarkerState.TransitionPending;
                return InvalidMarker(residuals, "Multiple reset control leaves are present.");
            }
            string payload = snapshot.MarkerPayload ?? snapshot.TemporaryPayload!;
            if (!FactoryResetMarker.TryParse(payload, out FactoryResetMarker marker)) return InvalidMarker(residuals, "The reset marker is malformed or has an unknown schema.");
            return marker.State;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or FormatException or InvalidOperationException)
        {
            return InvalidMarker(residuals, exception.GetType().Name);
        }
    }

    private static FactoryResetMarkerState InvalidMarker(List<FactoryResetResidual> residuals, string detail)
    {
        residuals.Add(new FactoryResetResidual("Reset marker", "InvalidMarker", detail));
        return FactoryResetMarkerState.Invalid;
    }

    private static bool TryReadCurrentMarker(string root, List<FactoryResetResidual> residuals, out FactoryResetMarker marker)
    {
        marker = null!;
        try
        {
            if (!FactoryResetMarker.TryParse(NativeResetControl.ReadCurrentMarker(root), out marker))
            {
                InvalidMarker(residuals, "The reset marker is malformed or has an unknown schema.");
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            InvalidMarker(residuals, exception.GetType().Name);
            return false;
        }
    }

    private static FactoryResetMarker RecordCategory(string root, FactoryResetMarker marker, FactoryResetCategory category, int residualCountAtStart, List<FactoryResetResidual> residuals)
    {
        FactoryResetCategoryResult result = residuals.Count == residualCountAtStart ? FactoryResetCategoryResult.VerifiedAbsent : FactoryResetCategoryResult.Residual;
        FactoryResetMarker updated = marker.WithCategoryResult(category, result);
        PersistProgress(root, updated, residuals);
        return updated;
    }

    private static void PersistProgress(string root, FactoryResetMarker marker, List<FactoryResetResidual> residuals)
    {
        try { NativeResetControl.ReplaceResetProgress(root, marker.WithState(residuals.Count == 0 ? FactoryResetMarkerState.ResetPending : FactoryResetMarkerState.Partial)); }
        catch (Exception exception) { residuals.Add(new FactoryResetResidual("Reset marker", "ControlStateWriteFailed", exception.GetType().Name)); }
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
public enum FactoryResetMarkerState { None, ResetPending, Partial, OnboardingPending, TransitionPending, Invalid }

// The marker is deliberately a fixed, bounded recovery record.  It supplies neither a path nor
// any other deletion authority; every category remains compiled into the reset manifest.
internal enum FactoryResetCategory { ScheduledCheck, Downloads, Logs, Database, OwnedRoot }
internal enum FactoryResetCategoryResult { Pending, VerifiedAbsent, Residual }

internal sealed record FactoryResetMarker(
    Guid ResetId,
    DateTimeOffset RequestedUtc,
    int Attempt,
    FactoryResetMarkerState State,
    IReadOnlyDictionary<FactoryResetCategory, FactoryResetCategoryResult> Categories)
{
    private const int SchemaVersion = 1;
    private const int MaximumAttempts = 100000;

    internal static FactoryResetMarker CreatePending() => new(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow,
        0,
        FactoryResetMarkerState.ResetPending,
        Enum.GetValues<FactoryResetCategory>().ToDictionary(category => category, _ => FactoryResetCategoryResult.Pending));

    internal bool TryBeginAttempt(out FactoryResetMarker next)
    {
        if (Attempt >= MaximumAttempts)
        {
            next = this with { State = FactoryResetMarkerState.Partial };
            return false;
        }
        next = this with { Attempt = Attempt + 1, State = FactoryResetMarkerState.ResetPending };
        return true;
    }

    internal FactoryResetMarker WithState(FactoryResetMarkerState state) => this with { State = state };

    internal FactoryResetMarker WithCategoryResult(FactoryResetCategory category, FactoryResetCategoryResult result)
    {
        Dictionary<FactoryResetCategory, FactoryResetCategoryResult> categories = Categories.ToDictionary(pair => pair.Key, pair => pair.Value);
        categories[category] = result;
        return this with { Categories = categories };
    }

    internal bool SameReset(FactoryResetMarker other) => ResetId == other.ResetId && RequestedUtc == other.RequestedUtc;

    internal string ToPayload() => JsonSerializer.Serialize(new MarkerPayload(
        SchemaVersion,
        ResetId.ToString("D"),
        RequestedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        Attempt,
        State.ToString(),
        Enum.GetValues<FactoryResetCategory>().ToDictionary(category => category.ToString(), category => Categories[category].ToString())));

    internal static bool TryParse(string payload, out FactoryResetMarker marker)
    {
        marker = null!;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 6 ||
                !root.TryGetProperty("schemaVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != SchemaVersion ||
                !root.TryGetProperty("resetId", out JsonElement id) || id.ValueKind != JsonValueKind.String || !Guid.TryParseExact(id.GetString(), "D", out Guid resetId) ||
                !root.TryGetProperty("requestedUtc", out JsonElement requested) || requested.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParseExact(requested.GetString(), "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset requestedUtc) || requestedUtc.Offset != TimeSpan.Zero ||
                !root.TryGetProperty("attempt", out JsonElement attempt) || attempt.ValueKind != JsonValueKind.Number || !attempt.TryGetInt32(out int attemptCount) || attemptCount < 0 || attemptCount > MaximumAttempts ||
                !root.TryGetProperty("state", out JsonElement state) || state.ValueKind != JsonValueKind.String || !Enum.TryParse(state.GetString(), false, out FactoryResetMarkerState markerState) || markerState is not (FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.OnboardingPending) ||
                !root.TryGetProperty("categories", out JsonElement categoriesElement) || categoriesElement.ValueKind != JsonValueKind.Object || categoriesElement.EnumerateObject().Count() != Enum.GetValues<FactoryResetCategory>().Length ||
                root.EnumerateObject().Any(property => property.Name is not ("schemaVersion" or "resetId" or "requestedUtc" or "attempt" or "state" or "categories"))) return false;

            Dictionary<FactoryResetCategory, FactoryResetCategoryResult> categories = [];
            foreach (JsonProperty property in categoriesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || !Enum.TryParse(property.Name, false, out FactoryResetCategory category) || !Enum.IsDefined(category) ||
                    !Enum.TryParse(property.Value.GetString(), false, out FactoryResetCategoryResult result) || !Enum.IsDefined(result) || !categories.TryAdd(category, result)) return false;
            }
            if (categories.Count != Enum.GetValues<FactoryResetCategory>().Length || Enum.GetValues<FactoryResetCategory>().Any(category => !categories.ContainsKey(category))) return false;
            marker = new FactoryResetMarker(resetId, requestedUtc, attemptCount, markerState, categories);
            return true;
        }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
    }

    private sealed record MarkerPayload(int schemaVersion, string resetId, string requestedUtc, int attempt, string state, IReadOnlyDictionary<string, string> categories);
}

/// <summary>Owns the process-wide reset mutex on a dedicated thread for an asynchronous data operation.</summary>
public static class DataOperationLease
{
    public static void RunSynchronous(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        using Mutex mutex = new(false, FactoryReset.DataOperationMutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("Another PC Steward data operation is still active.");
        try
        {
            if (FactoryReset.GetMarkerState() is not FactoryResetMarkerState.None) throw new InvalidOperationException("Factory reset recovery is pending; PC Steward will not open or change prior state.");
            operation();
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        RunAsync(async token => { await operation(token).ConfigureAwait(false); return true; }, cancellationToken);

    public static Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Task.Factory.StartNew(() =>
        {
            using Mutex mutex = new(false, FactoryReset.DataOperationMutexName);
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("Another PC Steward data operation is still active.");
            try
            {
                if (FactoryReset.GetMarkerState() is not FactoryResetMarkerState.None) throw new InvalidOperationException("Factory reset recovery is pending; PC Steward will not open or change prior state.");
                return operation(cancellationToken).GetAwaiter().GetResult();
            }
            finally { mutex.ReleaseMutex(); }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
