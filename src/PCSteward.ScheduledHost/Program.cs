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
    private const int UnresolvedOperationExitCode = 78;

    private static int Main(string[] args)
    {
        if (args.Length != 0) return 64;
        try { return RunAsync().GetAwaiter().GetResult(); }
        catch { return 1; }
    }

    private static Task<int> RunAsync() => Task.Factory.StartNew(RunWithOwnedMutexes, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    // Native ownership is thread-affine. This dedicated thread holds the common session through
    // reconciliation and the asynchronous scan body, then disposes it before releasing the scan guard.
    private static int RunWithOwnedMutexes()
    {
        using Mutex mutex = new(false, ScanMutexName);
        bool scanLeaseHeld;
        try { scanLeaseHeld = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { scanLeaseHeld = true; }
        if (!scanLeaseHeld) return 0;
        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
            using CurrentUserDataOperationSession? session = CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, timeout.Token);
            if (session is null) return 0;
            // Reset bootstrap precedes the state directory, SQLite, settings and provider access.
            if (FactoryReset.GetMarkerState() != FactoryResetMarkerState.None) return 0;
            string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StateDirectoryName);
            Directory.CreateDirectory(stateDirectory);
            SqliteDatabase database = new(Path.Combine(stateDirectory, "pcsteward.db"), new ChocolateyHelperVersionGrammar());
            database.MigrateAsync(timeout.Token).GetAwaiter().GetResult();
            if (!session.Reconcile(new UpdateDispatchReconciliation(database).ReconcileAsync, timeout.Token)) return UnresolvedOperationExitCode;
            timeout.Token.ThrowIfCancellationRequested();
            if (FactoryReset.GetMarkerState() != FactoryResetMarkerState.None ||
                !session.Context.CanReconcile || session.Context.RecoveryRequired) return 0;
            return RunWithLeasesAsync(database, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) { return 2; }
        finally { mutex.ReleaseMutex(); }
    }

    private static async Task<int> RunWithLeasesAsync(SqliteDatabase database, CancellationToken cancellationToken)
    {
        try
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            ScheduledCheckSettings settings = await database.GetScheduledCheckSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (!settings.IsEnabled || settings.Cadence == ScheduleCadence.Manual) return 0;

            ConnectorState wingetInventory = await database.GetConnectorStateAsync(TrustedSourceConnectorConsent.InstalledInventoryConnector, false, cancellationToken).ConfigureAwait(false);
            ConnectorState appxInventory = await database.GetConnectorStateAsync("Current-user AppX/MSIX catalog", true, cancellationToken).ConfigureAwait(false);
            ConnectorState sourceState = await database.GetConnectorStateAsync(TrustedSourceConnectorConsent.PackageSourcesConnector, false, cancellationToken).ConfigureAwait(false);
            bool updateSourcesEnabled = TrustedSourceConnectorConsent.IsEnabled(wingetInventory, sourceState);
            InventoryWorkflow workflow = new(database, database, database,
                [new ScheduledRegistryInventoryProvider(), new ScheduledMsixInventoryProvider(() => appxInventory.Enabled), new WingetLocalInventoryProvider(() => updateSourcesEnabled), new ChocolateyLocalInventoryProvider(() => updateSourcesEnabled)],
                new BoundedScanEventSink(), database, database,
                [new WingetUpdateSourceConnector("winget"), new WingetUpdateSourceConnector("msstore"), new ChocolateyUpdateSourceConnector()]);

            ScanResult scan = await workflow.ScanAsync(null, cancellationToken).ConfigureAwait(false);
            bool attention = false;
            if (scan.Succeeded && updateSourcesEnabled)
            {
                UpdateIntelligenceSummary updates = await workflow.RefreshUpdateIntelligenceAsync(true, cancellationToken).ConfigureAwait(false);
                attention = updates.AttentionNeeded.Count != 0;
            }

            ScheduledCheckStatus previous = await database.GetScheduledCheckStatusAsync(cancellationToken).ConfigureAwait(false);
            int failures = scan.Succeeded ? 0 : previous.ConsecutiveFailures + 1;
            string result = scan.Cancelled ? "Cancelled" : scan.Succeeded ? attention ? "Attention required" : "Completed" : "Failed";
            await database.SaveScheduledCheckStatusAsync(new ScheduledCheckStatus(DateTimeOffset.UtcNow, result, failures), cancellationToken).ConfigureAwait(false);
            if (ScheduledCheckNotificationPolicy.ShouldNotify(settings, attention, scan.Succeeded, failures)) WindowsScheduledCheckNotification.TryShow(result, attention ? "Updates or review items need your attention." : "PC Steward could not complete repeated scheduled checks.");
            return scan.Succeeded ? 0 : 1;
        }
        catch (OperationCanceledException) { return 2; }
    }
}
