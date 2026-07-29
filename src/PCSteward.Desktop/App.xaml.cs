using Microsoft.UI.Xaml;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Execution;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.WinGet;
using PCSteward.Storage;
using PCSteward.Scheduling.Windows;

namespace PCSteward.Desktop;

public partial class App : global::Microsoft.UI.Xaml.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InventoryExperienceViewModel viewModel = new();
        string stateDirectory = FactoryReset.GetStateRoot();
        WindowsCurrentUserScheduledTaskRegistrar scheduleRegistrar = new(Path.Combine(AppContext.BaseDirectory, "PCSteward.ScheduledHost.exe"));
        if (FactoryReset.IsPending(stateDirectory))
        {
            FactoryResetResult reset = FactoryReset.CompletePendingAsync(scheduleRegistrar.RemoveAsync, CancellationToken.None, stateDirectory).GetAwaiter().GetResult();
            if (!reset.Succeeded)
            {
                new ResetRecoveryWindow(reset).Activate();
                return;
            }
        }
        Directory.CreateDirectory(stateDirectory);
        SqliteDatabase database = new(Path.Combine(stateDirectory, "pcsteward.db"));
        database.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        List<ILocalInventoryProvider> providers = [];
        ILocalInventoryProvider? evidenceDelay = EvidenceDelayInventoryProvider.FromEnvironment();
        if (evidenceDelay is not null) providers.Add(evidenceDelay);
        providers.AddRange([new RegistryLocalInventoryProvider(), new MsixLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog").IsEnabled), new WingetLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "WinGet installed inventory").IsEnabled)]);
        IReadOnlyCollection<IUpdateSourceConnector> updateSources = [
            new WingetUpdateSourceConnector("winget"),
            new WingetUpdateSourceConnector("msstore")
        ];
        InventoryWorkflow workflow = new(
            database,
            database,
            database,
            providers,
            new BoundedScanEventSink(),
            database,
            database,
            updateSources,
            new WingetPostUpdatePackageInventory(),
            new WingetStructuredUpdateCandidateAuthority(),
            database);
        DownloadedArtifactCache downloads = new(database);
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetUpdateExecutor()), new ElevatedUpdateRunner());
        PackageDerivedRunningApplicationDetector runningApplications = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, workflow, database, runningApplications);
        ReviewedUpdateExecutionWorkflow updates = new(workflow.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database), coordinator, runningApplications);
        MainWindow window = new(workflow, viewModel, downloads, updates, database, new ScheduledCheckRegistrationService(scheduleRegistrar));
        window.Activate();
    }
}

internal sealed class ResetRecoveryWindow : Window
{
    public ResetRecoveryWindow(FactoryResetResult result)
    {
        Title = "PC Steward reset needs attention";
        Content = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Padding = new Microsoft.UI.Xaml.Thickness(24),
            Spacing = 12,
            Children =
            {
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Factory reset is incomplete", Style = (Microsoft.UI.Xaml.Style)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardPageTitleStyle"] },
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = "PC Steward did not reopen prior data. Close applications holding the listed items and restart PC Steward to retry." , TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = string.Join("\n", result.Residuals.Select(item => $"{item.Category}: {item.Reason}")), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }
            }
        };
    }
}
