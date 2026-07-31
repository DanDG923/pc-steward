using PCSteward.Storage;
using PCSteward.Persistence.Sqlite;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class FactoryResetTests
{
    [TestMethod]
    public void CleanProfileWithoutStateRootStartsWithoutResetRecovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-first-launch-" + Guid.NewGuid().ToString("N"));
        Assert.IsFalse(Directory.Exists(root));

        Assert.AreEqual(FactoryResetMarkerState.None, FactoryReset.GetMarkerState(root));
        Assert.IsFalse(FactoryReset.IsPending(root));
        Assert.IsFalse(Directory.Exists(root), "Reading reset state must not create first-launch storage.");
    }

    [TestMethod]
    public async Task MarkerLedResetDeletesOnlyTheFixedManifestAndIsIdempotent()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "downloads", "extract"));
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(Path.Combine(root, "downloads", "extract", "payload.bin"), "owned");
            await File.WriteAllTextAsync(Path.Combine(root, "logs", "events.log"), "owned");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "database");
            await File.WriteAllTextAsync(Path.Combine(outside, "provider-cache.txt"), "preserved");
            int removedSchedules = 0;

            FactoryReset.ArmForTesting(root);
            Assert.IsTrue(FactoryReset.IsPending(root));
            FactoryResetResult first = await FactoryReset.CompletePendingForTestingAsync(root, _ => { removedSchedules++; return Task.CompletedTask; }, CancellationToken.None);
            FactoryResetResult second = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(second.Succeeded);
            Assert.AreEqual(1, removedSchedules);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "downloads")));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "logs")));
            Assert.IsTrue(File.Exists(Path.Combine(outside, "provider-cache.txt")));
            FactoryResetResult acknowledgement = FactoryReset.AcknowledgeOnboardingForTesting(root);
            Assert.IsTrue(acknowledgement.Succeeded, string.Join("; ", acknowledgement.Residuals.Select(item => item.Reason + ":" + item.Detail)));
            Assert.IsFalse(FactoryReset.IsPending(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
        }
    }

    [TestMethod]
    public async Task MalformedMarkerFailsClosedWithoutDeletingExistingState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-invalid-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), "{ not-json");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "preserved");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsTrue(FactoryReset.IsPending(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task UnknownMarkerSchemaFailsClosedWithoutDeletingExistingState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-unknown-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), "{\"schemaVersion\":2,\"state\":\"ResetPending\"}");
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "preserved");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task ScheduleRemovalFailureKeepsMarkerAndOldStateUnavailable()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-schedule-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "database");
            FactoryReset.ArmForTesting(root);

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.FromException(new InvalidOperationException("exact task still present")), CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "ScheduleRemovalFailed"));
            Assert.IsTrue(FactoryReset.IsPending(root));
            Assert.IsFalse(File.Exists(Path.Combine(root, "pcsteward.db")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task FreshStoreVerificationRejectsPriorStateAndAcceptsNewMigratedDatabase()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            SqliteDatabase database = new(Path.Combine(root, "pcsteward.db"));
            await database.MigrateAsync(CancellationToken.None);
            Assert.IsTrue(await database.IsFreshResetStoreAsync(CancellationToken.None));

            await database.SaveConnectorEnabledAsync("WinGet installed inventory", true, CancellationToken.None);

            Assert.IsFalse(await database.IsFreshResetStoreAsync(CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task AsyncDataOperationLeaseBlocksResetUntilTheOperationCompletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-lease-" + Guid.NewGuid().ToString("N"));
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Directory.CreateDirectory(root);
            Task operation = DataOperationLease.RunAsync(async _ => { started.SetResult(); await release.Task.ConfigureAwait(false); }, CancellationToken.None);
            await started.Task;

            try { FactoryReset.ArmForTesting(root); Assert.Fail("Reset must fail while an asynchronous data operation owns the lease."); }
            catch (InvalidOperationException) { }

            release.SetResult();
            await operation;
            FactoryReset.ArmForTesting(root);
            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, async _ => await Task.Yield(), CancellationToken.None);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
        }
        finally
        {
            release.TrySetResult();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ValidTemporaryMarkerRecoversButUnexpectedOrOversizedControlStateFailsClosed()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-temp-" + Guid.NewGuid().ToString("N"));
        string hostileRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-hostile-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "reset", FactoryReset.TemporaryMarkerFileName), "{\"schemaVersion\":1,\"state\":\"ResetPending\"}");
            await File.WriteAllTextAsync(Path.Combine(temporaryRoot, "pcsteward.db"), "old");
            FactoryResetResult recovered = await FactoryReset.CompletePendingForTestingAsync(temporaryRoot, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(recovered.Succeeded);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(temporaryRoot));
            Assert.IsFalse(File.Exists(Path.Combine(temporaryRoot, "pcsteward.db")));

            Directory.CreateDirectory(Path.Combine(hostileRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(hostileRoot, "reset", FactoryReset.MarkerFileName), new string('x', 129));
            await File.WriteAllTextAsync(Path.Combine(hostileRoot, "reset", "unknown-control"), "hostile");
            await File.WriteAllTextAsync(Path.Combine(hostileRoot, "pcsteward.db"), "must-survive");
            FactoryResetResult hostile = await FactoryReset.CompletePendingForTestingAsync(hostileRoot, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsFalse(hostile.Succeeded);
            Assert.IsTrue(hostile.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(hostileRoot, "pcsteward.db")));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
            if (Directory.Exists(hostileRoot)) Directory.Delete(hostileRoot, true);
        }
    }

    [TestMethod]
    public async Task ReparseCollisionAndLockedControlStateNeverDropsResetAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-control-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-control-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
            await CreateJunctionAsync(Path.Combine(root, "reset"), outside);
            Assert.ThrowsExactly<System.Security.SecurityException>(() => FactoryReset.ArmForTesting(root));
            Assert.IsFalse(File.Exists(Path.Combine(outside, FactoryReset.MarkerFileName)));
            new DirectoryInfo(Path.Combine(root, "reset")).Delete();

            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await CreateJunctionAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), outside);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "must-survive");
            FactoryResetResult reparse = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsFalse(reparse.Succeeded);
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            new DirectoryInfo(Path.Combine(root, "reset", FactoryReset.MarkerFileName)).Delete();
            Directory.Delete(Path.Combine(root, "reset"));

            Directory.CreateDirectory(Path.Combine(root, "reset"));
            await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), "{\"schemaVersion\":1,\"state\":\"ResetPending\"}");
            Assert.ThrowsExactly<InvalidOperationException>(() => FactoryReset.ArmForTesting(root));
            Assert.IsTrue(File.Exists(Path.Combine(root, "reset", FactoryReset.MarkerFileName)));

            FactoryResetResult completed = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);
            Assert.IsTrue(completed.Succeeded);
            await using (FileStream lockHandle = new(Path.Combine(root, "reset", FactoryReset.MarkerFileName), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                FactoryResetResult blocked = FactoryReset.AcknowledgeOnboardingForTesting(root);
                Assert.IsFalse(blocked.Succeeded);
                Assert.IsTrue(FactoryReset.IsPending(root));
            }
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task HardLinkedMarkerFailsClosedAndDoesNotDeletePriorState()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-hardlink-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-hardlink-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reset")); Directory.CreateDirectory(outside);
            string source = Path.Combine(outside, "control.json");
            await File.WriteAllTextAsync(source, "{\"schemaVersion\":1,\"state\":\"ResetPending\"}");
            await CreateHardLinkAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), source);
            await File.WriteAllTextAsync(Path.Combine(root, "pcsteward.db"), "must-survive");

            FactoryResetResult result = await FactoryReset.CompletePendingForTestingAsync(root, _ => Task.CompletedTask, CancellationToken.None);

            Assert.IsFalse(result.Succeeded);
            Assert.IsTrue(result.Residuals.Any(item => item.Reason == "InvalidMarker"));
            Assert.IsTrue(File.Exists(Path.Combine(root, "pcsteward.db")));
            Assert.IsTrue(File.Exists(source));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task EmptyControlDirectoryBlocksStartupAndTheOnlyValidTwoLeafTransitionIsPromoted()
    {
        string emptyRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-empty-" + Guid.NewGuid().ToString("N"));
        string transitionRoot = Path.Combine(Path.GetTempPath(), "pcsteward-reset-transition-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(emptyRoot, "reset"));
            Assert.AreEqual(FactoryResetMarkerState.Invalid, FactoryReset.GetMarkerState(emptyRoot));
            Assert.IsTrue(FactoryReset.IsPending(emptyRoot));
            Assert.ThrowsExactly<InvalidOperationException>(() => FactoryReset.ArmForTesting(emptyRoot));
            Assert.IsTrue(FactoryReset.IsPending(emptyRoot));

            Directory.CreateDirectory(Path.Combine(transitionRoot, "reset"));
            await File.WriteAllTextAsync(Path.Combine(transitionRoot, "reset", FactoryReset.MarkerFileName), "{\"schemaVersion\":1,\"state\":\"ResetPending\"}");
            await File.WriteAllTextAsync(Path.Combine(transitionRoot, "reset", FactoryReset.TemporaryMarkerFileName), "{\"schemaVersion\":1,\"state\":\"OnboardingPending\"}");
            FactoryResetResult transition = await FactoryReset.CompletePendingForTestingAsync(transitionRoot, _ => Task.FromException(new InvalidOperationException("must not replay cleanup")), CancellationToken.None);
            Assert.IsTrue(transition.Succeeded);
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(transitionRoot));
            Assert.IsFalse(File.Exists(Path.Combine(transitionRoot, "reset", FactoryReset.TemporaryMarkerFileName)));

            await File.WriteAllTextAsync(Path.Combine(emptyRoot, "reset", FactoryReset.MarkerFileName), "{\"schemaVersion\":1,\"state\":\"OnboardingPending\"}");
            await File.WriteAllTextAsync(Path.Combine(emptyRoot, "reset", FactoryReset.TemporaryMarkerFileName), "{\"schemaVersion\":1,\"state\":\"ResetPending\"}");
            Assert.AreEqual(FactoryResetMarkerState.Invalid, FactoryReset.GetMarkerState(emptyRoot));
        }
        finally
        {
            if (Directory.Exists(emptyRoot)) Directory.Delete(emptyRoot, true);
            if (Directory.Exists(transitionRoot)) Directory.Delete(transitionRoot, true);
        }
    }

    [TestMethod]
    public async Task BoundLeafAndDirectorySwapCutpointsCannotRedirectAcknowledgement()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-swap-" + Guid.NewGuid().ToString("N"));
        string outside = Path.Combine(Path.GetTempPath(), "pcsteward-reset-swap-outside-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
            string sentinel = Path.Combine(outside, "must-survive.txt");
            await File.WriteAllTextAsync(sentinel, "outside");

            await CreateOnboardingMarkerAsync(root);
            bool leafSwapBlocked = false;
            NativeResetControl.BeforeMarkerDispositionForTesting = () =>
            {
                try { File.Move(Path.Combine(root, "reset", FactoryReset.MarkerFileName), Path.Combine(outside, "moved-marker.json")); }
                catch (IOException) { leafSwapBlocked = true; }
            };
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
            Assert.IsTrue(leafSwapBlocked);
            Assert.AreEqual("outside", await File.ReadAllTextAsync(sentinel));

            await CreateOnboardingMarkerAsync(root);
            bool directorySwapBlocked = false;
            NativeResetControl.BeforeDirectoryDispositionForTesting = () =>
            {
                try { Directory.Move(Path.Combine(root, "reset"), Path.Combine(outside, "moved-reset")); }
                catch (IOException) { directorySwapBlocked = true; }
            };
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
            Assert.IsTrue(directorySwapBlocked);
            Assert.AreEqual("outside", await File.ReadAllTextAsync(sentinel));
        }
        finally
        {
            NativeResetControl.BeforeMarkerDispositionForTesting = null;
            NativeResetControl.BeforeDirectoryDispositionForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (Directory.Exists(outside)) Directory.Delete(outside, true);
        }
    }

    [TestMethod]
    public async Task LockedControlDirectoryRetainsDurableOnboardingAuthority()
    {
        string root = Path.Combine(Path.GetTempPath(), "pcsteward-reset-directory-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            await CreateOnboardingMarkerAsync(root);
            using (SafeFileHandle directoryLock = OpenDirectoryExclusive(Path.Combine(root, "reset")))
            {
                FactoryResetResult result = FactoryReset.AcknowledgeOnboardingForTesting(root);
                Assert.IsFalse(result.Succeeded);
                Assert.IsTrue(FactoryReset.IsPending(root));
                Assert.IsTrue(File.Exists(Path.Combine(root, "reset", FactoryReset.MarkerFileName)));
            }
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);

            await CreateOnboardingMarkerAsync(root);
            NativeResetControl.BeforeDirectoryDispositionForTesting = () => throw new IOException("simulated directory lock after leaf disposition");
            FactoryResetResult midCleanupFailure = FactoryReset.AcknowledgeOnboardingForTesting(root);
            Assert.IsFalse(midCleanupFailure.Succeeded);
            Assert.IsTrue(FactoryReset.IsPending(root));
            Assert.AreEqual(FactoryResetMarkerState.OnboardingPending, FactoryReset.GetMarkerState(root));
            Assert.IsTrue(File.Exists(Path.Combine(root, "reset", FactoryReset.TemporaryMarkerFileName)));
            NativeResetControl.BeforeDirectoryDispositionForTesting = null;
            Assert.IsTrue(FactoryReset.AcknowledgeOnboardingForTesting(root).Succeeded);
        }
        finally
        {
            NativeResetControl.BeforeDirectoryDispositionForTesting = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task CreateOnboardingMarkerAsync(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "reset"));
        await File.WriteAllTextAsync(Path.Combine(root, "reset", FactoryReset.MarkerFileName), "{\"schemaVersion\":1,\"state\":\"OnboardingPending\"}");
    }

    private static SafeFileHandle OpenDirectoryExclusive(string path)
    {
        SafeFileHandle handle = CreateFileW(path, 0x80000000, 0, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        Assert.IsFalse(handle.IsInvalid, "Opening the control directory exclusively failed. Win32=" + Marshal.GetLastWin32Error());
        return handle;
    }

    private static async Task CreateJunctionAsync(string path, string target)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /J \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        process.Start();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
    }

    private static async Task CreateHardLinkAsync(string path, string target)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c mklink /H \"{path}\" \"{target}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        process.Start();
        await process.WaitForExitAsync();
        Assert.AreEqual(0, process.ExitCode, await process.StandardError.ReadToEndAsync());
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
