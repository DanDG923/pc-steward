using Microsoft.UI.Xaml;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Execution;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.WinGet;
using PCSteward.Provider.Chocolatey;
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
        using Mutex bootstrapMutex = new(false, FactoryReset.DataOperationMutexName);
        bool bootstrapLeaseHeld;
        try { bootstrapLeaseHeld = bootstrapMutex.WaitOne(0); }
        catch (AbandonedMutexException) { bootstrapLeaseHeld = true; }
        if (!bootstrapLeaseHeld)
        {
            new DataOperationBusyWindow().Activate();
            return;
        }
        try
        {
        InventoryExperienceViewModel viewModel = new();
        string stateDirectory = FactoryReset.GetStateRoot();
        WindowsCurrentUserScheduledTaskRegistrar scheduleRegistrar = new(Path.Combine(AppContext.BaseDirectory, "PCSteward.ScheduledHost.exe"));
        bool showPostResetOnboarding = false;
        FactoryResetMarkerState markerState = FactoryReset.GetMarkerState(stateDirectory);
        if (markerState is FactoryResetMarkerState.ResetPending or FactoryResetMarkerState.Partial or FactoryResetMarkerState.TransitionPending)
        {
            FactoryResetResult reset = FactoryReset.CompletePendingAsync(scheduleRegistrar.EnsureAbsentAsync, CancellationToken.None, stateDirectory).GetAwaiter().GetResult();
            if (!reset.Succeeded)
            {
                new ResetRecoveryWindow(reset).Activate();
                return;
            }
            showPostResetOnboarding = true;
        }
        else if (markerState == FactoryResetMarkerState.OnboardingPending) showPostResetOnboarding = true;
        else if (markerState == FactoryResetMarkerState.Invalid)
        {
            new ResetRecoveryWindow(new FactoryResetResult(false, [new FactoryResetResidual("Reset marker", "InvalidMarker", "The reset control state is invalid.")])).Activate();
            return;
        }
        if (showPostResetOnboarding)
        {
            FactoryResetResult onboardingAuthority = FactoryReset.VerifyOnboardingBootstrap(stateDirectory);
            if (!onboardingAuthority.Succeeded)
            {
                new ResetRecoveryWindow(onboardingAuthority).Activate();
                return;
            }
        }
        Directory.CreateDirectory(stateDirectory);
        SqliteDatabase database = new(Path.Combine(stateDirectory, "pcsteward.db"));
        database.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (showPostResetOnboarding && !database.IsFreshResetStoreAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            new ResetRecoveryWindow(new FactoryResetResult(false, [new FactoryResetResidual("Fresh store", "VerificationFailed", "Post-reset defaults or prior state could not be verified.")])).Activate();
            return;
        }
        List<ILocalInventoryProvider> providers = [];
        ILocalInventoryProvider? evidenceDelay = EvidenceDelayInventoryProvider.FromEnvironment();
        if (evidenceDelay is not null) providers.Add(evidenceDelay);
        providers.AddRange([new RegistryLocalInventoryProvider(), new MsixLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog").IsEnabled), new WingetLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "WinGet installed inventory").IsEnabled), new ChocolateyLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "WinGet package sources").IsEnabled)]);
        IReadOnlyCollection<IUpdateSourceConnector> updateSources = [
            new WingetUpdateSourceConnector("winget"),
            new WingetUpdateSourceConnector("msstore"),
            new ChocolateyUpdateSourceConnector()
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
            new CompositePostUpdatePackageInventory([new WingetPostUpdatePackageInventory(), new ChocolateyPostUpdatePackageInventory()]),
            new CompositeStructuredUpdateCandidateAuthority([new WingetStructuredUpdateCandidateAuthority(), new ChocolateyUpdateSourceConnector()]),
            database,
            UpdateExecutionCapabilities.UserAndMachine);
        DownloadedArtifactCache downloads = new(database);
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetUpdateExecutor()), new ElevatedUpdateRunner());
        PackageDerivedRunningApplicationDetector runningApplications = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, workflow, database, runningApplications);
        ReviewedUpdateExecutionWorkflow updates = new(workflow.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database), coordinator, runningApplications);
        MainWindow window = new(workflow, viewModel, downloads, updates, database, new ScheduledCheckRegistrationService(scheduleRegistrar), showPostResetOnboarding, () => FactoryReset.AcknowledgeOnboarding(stateDirectory));
        window.Activate();
        }
        catch (Exception exception)
        {
            new StartupFailureWindow(exception).Activate();
        }
        finally { bootstrapMutex.ReleaseMutex(); }
    }
}

internal sealed class DataOperationBusyWindow : Window
{
    public DataOperationBusyWindow()
    {
        Title = "PC Steward is busy";
        Content = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Padding = new Microsoft.UI.Xaml.Thickness(24),
            Spacing = 12,
            Children =
            {
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Another PC Steward operation is still active", Style = (Microsoft.UI.Xaml.Style)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardPageTitleStyle"] },
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = "PC Steward did not start a scan or change local state. Wait for the other operation to finish, then restart PC Steward.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }
            }
        };
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

internal sealed class StartupFailureWindow : Window
{
    public StartupFailureWindow(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Title = "PC Steward needs attention";
        Content = new Microsoft.UI.Xaml.Controls.StackPanel
        {
            Padding = new Microsoft.UI.Xaml.Thickness(24),
            Spacing = 12,
            Children =
            {
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = "PC Steward could not finish starting", Style = (Microsoft.UI.Xaml.Style)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardPageTitleStyle"] },
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = "No scan or update was started. Copy the diagnostic below when reporting this problem.", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                new Microsoft.UI.Xaml.Controls.TextBlock { Text = $"{exception.GetType().Name}: {exception.Message}", TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap }
            }
        };
    }
}
