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
    private Window? activeWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Common current-user gate (Global SID gate, then legacy Local v1). The held session is passed through reset
        // completion and initial loading via the bootstrap scope: no recursive acquisition.
        CurrentUserDataOperationSession? session;
        try { session = CurrentUserDataOperationSession.TryAcquire(TimeSpan.Zero, CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ActivateWindow(new StartupFailureWindow(exception));
            return;
        }
        if (session is null)
        {
            ActivateWindow(new DataOperationBusyWindow());
            return;
        }
        using IDisposable bootstrapScope = DataOperationLease.EnterBootstrap(session);
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
                ActivateWindow(new ResetRecoveryWindow(reset));
                return;
            }
            showPostResetOnboarding = true;
        }
        else if (markerState == FactoryResetMarkerState.OnboardingPending) showPostResetOnboarding = true;
        else if (markerState == FactoryResetMarkerState.Invalid)
        {
            ActivateWindow(new ResetRecoveryWindow(new FactoryResetResult(false, [new FactoryResetResidual("Reset marker", "InvalidMarker", "The reset control state is invalid.")])));
            return;
        }
        if (showPostResetOnboarding)
        {
            FactoryResetResult onboardingAuthority = FactoryReset.VerifyOnboardingBootstrap(stateDirectory);
            if (!onboardingAuthority.Succeeded)
            {
                ActivateWindow(new ResetRecoveryWindow(onboardingAuthority));
                return;
            }
        }
        Directory.CreateDirectory(stateDirectory);
        SqliteDatabase database = new(Path.Combine(stateDirectory, "pcsteward.db"), new ChocolateyHelperVersionGrammar());
        database.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        DataOperationLease.ConfigureReconciliation(new UpdateDispatchReconciliation(database).ReconcileAsync);
        // An unresolved update barrier keeps read-only UI available; every mutating operation fails closed until reconciled.
        try { DataOperationLease.ReconcileBootstrap(CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException and not OperationCanceledException) { }
        if (showPostResetOnboarding && !database.IsFreshResetStoreAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            ActivateWindow(new ResetRecoveryWindow(new FactoryResetResult(false, [new FactoryResetResidual("Fresh store", "VerificationFailed", "Post-reset defaults or prior state could not be verified.")])));
            return;
        }
        List<ILocalInventoryProvider> providers = [];
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
        SequentialUpdateBatchCoordinator coordinator = new(executor, workflow, database, runningApplications,
            events: new BoundedUpdateExecutionEventSink());
        ReviewedUpdateExecutionWorkflow updates = new(workflow.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database), coordinator, runningApplications);
        MainWindow window = new(workflow, viewModel, downloads, updates, database, new ScheduledCheckRegistrationService(scheduleRegistrar), database, ChocolateyReviewedRouteProbe.ProbeSplitTokenState, showPostResetOnboarding, () => AcknowledgeOnboarding(stateDirectory));
        ActivateWindow(window);
        }
        catch (Exception exception)
        {
            ActivateWindow(new StartupFailureWindow(exception));
        }
        finally { session.Dispose(); }
    }

    private static FactoryResetResult AcknowledgeOnboarding(string stateDirectory)
    {
        try { return DataOperationLease.RunResetControlSynchronous(() => FactoryReset.AcknowledgeOnboarding(stateDirectory)); }
        catch (InvalidOperationException)
        {
            return new(false, [new FactoryResetResidual("Data operation", "ActiveOperation", "Another PC Steward operation still owns the data-operation gate.")]);
        }
    }

    private void ActivateWindow(Window window)
    {
        activeWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(activeWindow, window)) activeWindow = null;
        };
        window.Activate();
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
