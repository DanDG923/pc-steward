using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Execution;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.WinGet;
using PCSteward.Provider.Chocolatey;
using PCSteward.Storage;
using PCSteward.Scheduling.Windows;
using Windows.ApplicationModel.DataTransfer;

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
        // Task 73 Phase 6 review M2: a batch review shares one fresh inventory observation; each app is still re-prepared alone before it starts.
        ReviewedUpdateExecutionWorkflow updates = new(workflow.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database), coordinator, runningApplications,
            prepareSelections: workflow.PrepareApprovedUpdateSelectionsAsync);
        MainWindow window = new(workflow, viewModel, downloads, updates, database, new ScheduledCheckRegistrationService(scheduleRegistrar), database, database, ChocolateyReviewedRouteProbe.ProbeSplitTokenState, ChocolateyClientReleaseCatalog.Current, showPostResetOnboarding, () => AcknowledgeOnboarding(stateDirectory));
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
        Content = StartupNotice.Build(this, "", "Another PC Steward operation is still active",
            "PC Steward did not start a scan or change local state. Wait for the other operation to finish, then restart PC Steward.", null);
    }
}

internal sealed class ResetRecoveryWindow : Window
{
    public ResetRecoveryWindow(FactoryResetResult result)
    {
        Title = "PC Steward reset needs attention";
        Content = StartupNotice.Build(this, "", "Factory reset is incomplete",
            "PC Steward did not reopen prior data. Close applications holding the listed items and restart PC Steward to retry.",
            StartupNoticeText.ResidualDetails(result.Residuals.Select(item => (item.Category, item.Reason, item.Detail))));
    }
}

internal sealed class StartupFailureWindow : Window
{
    public StartupFailureWindow(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Title = "PC Steward needs attention";
        Content = StartupNotice.Build(this, "", "PC Steward could not finish starting",
            "No scan or update was started. Select Copy details and include the diagnostic when reporting this problem.",
            StartupNoticeText.ExceptionDetails(exception.GetType().Name, exception.Message));
    }
}

/// <summary>
/// Task 73 Phase 4 (audit P-L5): the shared look of the startup busy, reset-recovery and startup-failure windows — the app canvas, a
/// card with an icon and heading, selectable diagnostic details with "Copy details", and "Close" as the clear next action. It only
/// presents: it never retries, resets, deletes or starts anything, and each window's start-up decision is made before it is shown.
/// </summary>
internal static class StartupNotice
{
    public static UIElement Build(Window window, string glyph, string heading, string message, string? details)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(720, details is null ? 420 : 560));
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        StackPanel title = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Medium };
        title.Children.Add(new FontIcon { Glyph = glyph, FontSize = 24, Foreground = StewardVisuals.Brush("StewardBrandBrush"), VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(StewardVisuals.PageTitle(heading));
        content.Children.Add(title);
        content.Children.Add(new TextBlock { Text = message, Style = StewardVisuals.Style("StewardBodyStyle"), TextWrapping = TextWrapping.Wrap });
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        if (details is not null)
        {
            TextBlock detailText = new() { Text = details, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), Foreground = StewardVisuals.Brush("StewardTextBrush") };
            AutomationProperties.SetName(detailText, "Details: " + details);
            content.Children.Add(new Border { Background = StewardVisuals.Brush("StewardSurfaceSubtleBrush"), BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(StewardSpacing.Medium), Child = detailText });
            TextBlock status = new() { Style = StewardVisuals.Style("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            content.Children.Add(status);
            Button copy = StewardVisuals.ActionButton(StartupNoticeText.CopyDetailsLabel, "to the clipboard");
            copy.Style = StewardVisuals.Style("StewardButtonStyle");
            copy.Click += (_, _) =>
            {
                try
                {
                    DataPackage package = new();
                    package.SetText(details);
                    Clipboard.SetContent(package);
                    // Keeps the copied details on the clipboard after PC Steward closes (the next action is Close).
                    Clipboard.Flush();
                    StewardVisuals.SetLiveText(status, StartupNoticeText.DetailsCopied, announce: true);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException) { StewardVisuals.SetLiveText(status, StartupNoticeText.DetailsNotCopied, announce: true); }
            };
            actions.Children.Add(copy);
        }
        Button close = StewardVisuals.PrimaryButton(StartupNoticeText.CloseLabel, "PC Steward");
        close.Click += (_, _) => window.Close();
        actions.Children.Add(close);
        content.Children.Add(actions);
        Border card = new() { Style = StewardVisuals.Style("StewardCardStyle"), MaxWidth = 640, Margin = new Thickness(StewardSpacing.XLarge), Child = content };
        return new Grid { Background = StewardVisuals.Brush("StewardCanvasBrush"), Children = { new ScrollViewer { Content = card, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
    }
}
