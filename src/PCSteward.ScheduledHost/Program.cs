using System.Diagnostics;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Domain;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.WinGet;
using PCSteward.Provider.Chocolatey;
using PCSteward.Storage;

namespace PCSteward.ScheduledHost;

/// <summary>One-shot, scan-only Task Scheduler entry point. It never references update execution or elevation.</summary>
internal static class Program
{
    private const string StateDirectoryName = "PCSteward";
    private const string ScanMutexName = "Local\\PCSteward.ScheduledCheck.v1";

    private static int Main(string[] args)
    {
        if (args.Length != 0) return 64;
        try { return RunAsync().GetAwaiter().GetResult(); }
        catch { return 1; }
    }

    private static Task<int> RunAsync() => Task.Factory.StartNew(RunWithOwnedMutexes, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Named Mutex release is thread-affine. This dedicated owner thread waits synchronously for
    // the asynchronous scan work, then releases both mutexes itself.
    private static int RunWithOwnedMutexes()
    {
        using Mutex mutex = new(false, ScanMutexName);
        bool scanLeaseHeld;
        try { scanLeaseHeld = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { scanLeaseHeld = true; }
        if (!scanLeaseHeld) return 0;
        using Mutex dataMutex = new(false, FactoryReset.DataOperationMutexName);
        bool dataLeaseHeld;
        try { dataLeaseHeld = dataMutex.WaitOne(0); }
        catch (AbandonedMutexException) { dataLeaseHeld = true; }
        if (!dataLeaseHeld) { mutex.ReleaseMutex(); return 0; }
        try { return RunWithLeasesAsync().GetAwaiter().GetResult(); }
        finally { dataMutex.ReleaseMutex(); mutex.ReleaseMutex(); }
    }

    private static async Task<int> RunWithLeasesAsync()
    {
        try
        {
            // This preflight intentionally occurs before the state directory, SQLite, settings,
            // providers, or diagnostics are opened.
            if (FactoryReset.IsPending()) return 0;
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
            string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StateDirectoryName);
            Directory.CreateDirectory(stateDirectory);
            SqliteDatabase database = new(Path.Combine(stateDirectory, "pcsteward.db"));
            await database.MigrateAsync(timeout.Token).ConfigureAwait(false);
            ScheduledCheckSettings settings = await database.GetScheduledCheckSettingsAsync(timeout.Token).ConfigureAwait(false);
            if (!settings.IsEnabled || settings.Cadence == ScheduleCadence.Manual) return 0;

            ConnectorState wingetInventory = await database.GetConnectorStateAsync("WinGet installed inventory", false, timeout.Token).ConfigureAwait(false);
            ConnectorState appxInventory = await database.GetConnectorStateAsync("Current-user AppX/MSIX catalog", true, timeout.Token).ConfigureAwait(false);
            ConnectorState sourceState = await database.GetConnectorStateAsync("WinGet package sources", false, timeout.Token).ConfigureAwait(false);
            bool updateSourcesEnabled = sourceState.Enabled && sourceState.SourceDisclosureApproved;
            InventoryWorkflow workflow = new(database, database, database,
                [new ScheduledRegistryInventoryProvider(), new ScheduledMsixInventoryProvider(() => appxInventory.Enabled), new WingetLocalInventoryProvider(() => wingetInventory.Enabled && wingetInventory.SourceDisclosureApproved), new ChocolateyLocalInventoryProvider(() => updateSourcesEnabled)],
                new BoundedScanEventSink(), database, database,
                [new WingetUpdateSourceConnector("winget"), new WingetUpdateSourceConnector("msstore"), new ChocolateyUpdateSourceConnector()]);

            ScanResult scan = await workflow.ScanAsync(null, timeout.Token).ConfigureAwait(false);
            bool attention = false;
            if (scan.Succeeded && updateSourcesEnabled)
            {
                UpdateIntelligenceSummary updates = await workflow.RefreshUpdateIntelligenceAsync(true, timeout.Token).ConfigureAwait(false);
                attention = updates.AttentionNeeded.Count != 0;
            }

            ScheduledCheckStatus previous = await database.GetScheduledCheckStatusAsync(timeout.Token).ConfigureAwait(false);
            int failures = scan.Succeeded ? 0 : previous.ConsecutiveFailures + 1;
            string result = scan.Cancelled ? "Cancelled" : scan.Succeeded ? attention ? "Attention required" : "Completed" : "Failed";
            await database.SaveScheduledCheckStatusAsync(new ScheduledCheckStatus(DateTimeOffset.UtcNow, result, failures), timeout.Token).ConfigureAwait(false);
            if (ScheduledCheckNotificationPolicy.ShouldNotify(settings, attention, scan.Succeeded, failures)) WindowsScheduledCheckNotification.TryShow(result, attention ? "Updates or review items need your attention." : "PC Steward could not complete repeated scheduled checks.");
            return scan.Succeeded ? 0 : 1;
        }
        catch (OperationCanceledException) { return 2; }
    }
}
