using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using System.Globalization;
using PCSteward.Application;
using PCSteward.Storage;
using PCSteward.Scheduling.Windows;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PCSteward.Desktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WinUI owns the window lifetime and scan cancellation is disposed after every completed scan.")]
public sealed class MainWindow : Window
{
    private readonly InventoryWorkflow workflow;
    private readonly InventoryExperienceViewModel viewModel;
    private readonly DownloadedArtifactCache downloads;
    private readonly ReviewedUpdateExecutionWorkflow updates;
    private readonly IScheduledCheckSettingsStore scheduleSettingsStore;
    private readonly ScheduledCheckRegistrationService scheduleRegistration;
    private readonly Func<FactoryResetResult>? acknowledgeResetOnboarding;
    private readonly IReviewedUpdateHistoryReader reviewedHistory;
    private readonly Func<ReviewedRouteSplitTokenState> probeReviewedRouteSplitToken;
    private CancellationTokenSource? scanCancellation;
    private ScanDataSummary scanData = new(0, null, 0);
    private IReadOnlyCollection<PCSteward.Domain.StableDecision> activeDecisions = [];
    private IReadOnlyCollection<PCSteward.Domain.ManualMapping> manualMappings = [];
    private bool isClearing;
    private bool isExecuting;
    private DownloadCacheSummary downloadCache = new(0, 0, 0, 0, null);
    private ScheduledCheckSettings scheduledSettings = ScheduledCheckSettings.Manual;
    private ScheduledCheckStatus scheduledStatus = ScheduledCheckStatus.NotScheduled;
    private string destination = "overview";
    private string detailReturnDestination = "applications";
    private InventoryRow? detailRow;
    private IReadOnlyCollection<PCSteward.Domain.RawProviderRecord> detailRaw = [];
    private IReadOnlyCollection<PCSteward.Domain.ProductTimelineEntry> detailTimeline = [];
    private bool onboardingAcknowledging;
    private bool startupStateLoadFailed;
    private ResultsFilter resultsFilter = ResultsFilter.All;
    private Border? operationBand;
    private TextBlock? operationStatusText;
    private TextBlock? scanPageStatusText;
    private readonly DispatcherQueueTimer updateProgressTimer;
    private Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object>? updateProgressTick;
    private UpdateProgressMailbox? updateProgressMailbox;
    private UpdateProgressPresentation? updateProgressPresentation;
    private TextBlock? updateProgressTarget;
    private long updateProgressEpoch;
    private bool windowClosed;
    private string updateProgressLabel = "Preparing reviewed update";

    private enum ResultsFilter { All, Actionable, NeedsDecision, Unavailable, Unmanaged }

    public MainWindow(InventoryWorkflow workflow, InventoryExperienceViewModel viewModel, DownloadedArtifactCache downloads, ReviewedUpdateExecutionWorkflow updates, IScheduledCheckSettingsStore scheduleSettingsStore, ScheduledCheckRegistrationService scheduleRegistration, IReviewedUpdateHistoryReader reviewedHistory, Func<ReviewedRouteSplitTokenState> probeReviewedRouteSplitToken, bool showPostResetOnboarding = false, Func<FactoryResetResult>? acknowledgeResetOnboarding = null)
    {
        this.workflow = workflow; this.viewModel = viewModel; this.downloads = downloads; this.updates = updates; this.scheduleSettingsStore = scheduleSettingsStore; this.scheduleRegistration = scheduleRegistration; this.acknowledgeResetOnboarding = acknowledgeResetOnboarding;
        this.reviewedHistory = reviewedHistory ?? throw new ArgumentNullException(nameof(reviewedHistory)); this.probeReviewedRouteSplitToken = probeReviewedRouteSplitToken ?? throw new ArgumentNullException(nameof(probeReviewedRouteSplitToken));
        updateProgressTimer = DispatcherQueue.CreateTimer();
        updateProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        Closed += (_, _) => { windowClosed = true; StopUpdateProgressPolling(); updateProgressMailbox = null; updateProgressPresentation = null; };
        Title = AppIdentity.DisplayName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "pc-steward.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        destination = showPostResetOnboarding ? "onboarding" : "overview";
        if (!showPostResetOnboarding) startupStateLoadFailed = !LoadSavedInventory();
        Render();
    }

    private bool LoadSavedInventory()
    {
        try { DataOperationLease.RunReadSynchronous(() =>
        {
            InventoryDashboard dashboard = workflow.LoadAsync(viewModel.Connectors.ToArray(), CancellationToken.None).GetAwaiter().GetResult();
            foreach (ConnectorOption connector in viewModel.Connectors.ToArray()) viewModel.SetConnectorEnabled(connector.Name, dashboard.Connectors[connector.Name].Enabled);
            viewModel.LoadInventory(dashboard.Products, dashboard.Instances, dashboard.LastCompletedAt);
            viewModel.LoadUpdateIntelligence(workflow.LoadUpdateIntelligenceAsync(viewModel.IsUpdateSourceEnabled, CancellationToken.None).GetAwaiter().GetResult()); scanData = dashboard.ScanData;
            downloadCache = downloads.GetSummaryAsync(CancellationToken.None).GetAwaiter().GetResult();
            activeDecisions = workflow.GetActiveDecisionsAsync(CancellationToken.None).GetAwaiter().GetResult();
            manualMappings = workflow.GetManualMappingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            scheduledSettings = scheduleSettingsStore.GetScheduledCheckSettingsAsync(CancellationToken.None).GetAwaiter().GetResult();
            scheduledStatus = scheduleSettingsStore.GetScheduledCheckStatusAsync(CancellationToken.None).GetAwaiter().GetResult();
        }); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private async Task ShowDataOperationUnavailableAsync()
    {
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Action unavailable", Content = "Factory reset recovery or another PC Steward data operation is active. Wait for it to finish, then try again.", CloseButtonText = "Close" }.ShowAsync();
    }

    private void Render()
    {
        if (startupStateLoadFailed) { RenderStartupStateLoadFailure(); return; }
        if (destination == "settings") { RenderSettings(); return; }
        if (destination == "scan") { RenderScanPage(); return; }
        if (destination == "updates") { RenderUpdatesPage(); return; }
        if (destination == "detail") { RenderProductDetailPage(); return; }
        if (destination == "current") { RenderCurrentPage(); return; }
        if (destination == "results") { RenderResultsPage(); return; }
        if (destination == "review") { RenderReviewPage(); return; }
        if (destination == "overview") { RenderOverviewPage(); return; }
        if (destination == "applications") { RenderApplicationsPage(); return; }
        if (destination == "activity") { RenderActivityPage(); return; }
        if (destination == "privacy") { RenderPrivacyPage(); return; }
        if (destination == "storage") { RenderStoragePage(); return; }
        if (destination == "onboarding") { RenderOnboardingPage(); return; }
        if (destination == "about") { RenderAboutPage(); return; }
        RenderPlaceholder();
    }

    private void RenderStartupStateLoadFailure()
    {
        StackPanel panel = CreatePagePanel(760);
        panel.Children.Add(new TextBlock { Text = "PC Steward could not open local state", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("A local data operation is still active or reset recovery is in progress. No scan or state-changing action was started. Close PC Steward and try again after the other operation finishes."));
        Content = panel;
    }

    private static StackPanel CreatePagePanel(double maxWidth = 1040)
    {
        StackPanel panel = new() { Spacing = 16, Padding = new Thickness(24), MaxWidth = maxWidth };
        panel.Resources[typeof(TextBlock)] = AppStyle("StewardBodyStyle");
        panel.Resources[typeof(Button)] = AppStyle("StewardButtonStyle");
        panel.Resources[typeof(TextBox)] = AppStyle("StewardInputStyle");
        panel.Resources[typeof(ComboBox)] = AppStyle("StewardSelectStyle");
        return panel;
    }

    private static TextBlock PageCopy(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static Border Card(FrameworkElement child) => new() { Style = AppStyle("StewardCardStyle"), Child = child };

    private bool HasActiveOperation => scanCancellation is not null || viewModel.IsScanning || viewModel.IsCheckingUpdates || isExecuting || isClearing || downloads.IsCleaning;

    private string CurrentOperationLabel => viewModel.IsScanning
        ? $"Reading installed application records · {viewModel.ScanPhase} · {viewModel.Elapsed:mm\\:ss}"
        : scanCancellation is not null
            ? "Preparing the local inventory scan"
            : viewModel.IsCheckingUpdates
                ? "Refreshing approved package-source evidence"
                : isExecuting
                    ? updateProgressLabel
                    : isClearing || downloads.IsCleaning
                        ? "Completing the requested PC Steward data cleanup"
                        : "Ready · no background coordinator is running";

    private void RefreshOperationVisuals()
    {
        if (operationBand is not null) operationBand.Visibility = HasActiveOperation ? Visibility.Visible : Visibility.Collapsed;
        if (operationStatusText is not null) operationStatusText.Text = CurrentOperationLabel;
        if (scanPageStatusText is not null) scanPageStatusText.Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}";
    }

    private void RenderOnboardingPage()
    {
        StackPanel panel = CreatePagePanel(760);
        panel.Children.Add(StewardVisuals.PageHeader("Start fresh with PC Steward", "Factory reset is complete. No scan or source connection was started automatically.", "Fresh local state"));
        panel.Children.Add(StewardVisuals.InformationBanner("PC Steward data was removed", "Inventory, source evidence, settings, decisions, history, app-owned downloads, logs, and the scheduled check were included in the reset.", "success"));
        panel.Children.Add(StewardVisuals.EmptyState("\uE721", "Build a new local inventory", "When you continue, PC Steward first verifies the fresh state and then opens the read-only inventory screen. It does not start the scan for you."));
        Button continueButton = StewardVisuals.PrimaryButton("Continue to local inventory", "Continue to local-only inventory after factory reset");
        AutomationProperties.SetName(continueButton, "Continue to local-only inventory after factory reset");
        continueButton.Click += async (_, _) =>
        {
            if (onboardingAcknowledging) return;
            onboardingAcknowledging = true;
            continueButton.IsEnabled = false;
            PostResetOnboardingOutcome outcome = PostResetOnboardingFlow.Evaluate(
                () => acknowledgeResetOnboarding?.Invoke() ?? new FactoryResetResult(true, []),
                () => FactoryReset.GetMarkerState(),
                LoadSavedInventory);
            if (!outcome.AcknowledgementSucceeded)
            {
                string details = string.Join(", ", outcome.AcknowledgementResult.Residuals.Select(item => item.Reason));
                if (!string.IsNullOrWhiteSpace(details))
                {
                    details = $"{details} | marker state: {outcome.MarkerState}";
                }
                else
                {
                    details = $"Reset marker state: {outcome.MarkerState}";
                }
                await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset still needs attention", Content = details, CloseButtonText = "Close" }.ShowAsync();
                onboardingAcknowledging = false;
                continueButton.IsEnabled = true;
                return;
            }
            if (!outcome.ScanNavigationRequested)
            {
                await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Fresh state could not be loaded", Content = "PC Steward could not confirm and reload the fresh local state. No scan was started.", CloseButtonText = "Close" }.ShowAsync();
                onboardingAcknowledging = false;
                continueButton.IsEnabled = true;
                return;
            }
            destination = "scan"; Render();
        };
        panel.Children.Add(continueButton);
        Content = panel;
    }

    private void RenderAboutPage()
    {
        StackPanel panel = CreatePagePanel(820);
        panel.Children.Add(StewardVisuals.PageHeader("About & support", "PC Steward is a local-first Windows 11 application inventory and evidence-review tool.", "Quiet authority"));
        panel.Children.Add(StewardVisuals.InformationBanner("Private and user-directed", "No account, cloud sync, telemetry, resident background service, or automatic updates."));
        StackPanel scope = new() { Spacing = 8 };
        scope.Children.Add(new TextBlock { Text = "Update scope", Style = AppStyle("StewardSectionTitleStyle") });
        scope.Children.Add(PageCopy("This standalone package can review and run only explicitly approved current-user WinGet updates. Machine-scope updates are not actionable here. PC Steward does not use elevation or an elevated helper in this package."));
        panel.Children.Add(Card(scope));
        StackPanel data = new() { Spacing = 8 };
        data.Children.Add(new TextBlock { Text = "Data and removal", Style = AppStyle("StewardSectionTitleStyle") });
        data.Children.Add(PageCopy("Inventory, settings, decisions, history, source evidence, downloads and logs are stored locally under your PC Steward data root. Removing the extracted package removes its binaries but retains that data by default. Use Factory reset PC Steward to remove app-owned local data and the current-user scheduled check before removal."));
        panel.Children.Add(Card(data));
        panel.Children.Add(StewardVisuals.InformationBanner("External release is blocked", "A trusted release signature, approved private channel, supported-host lifecycle checks, and agreed-baseline performance evidence are still required. Do not treat an unsigned archive as a release candidate.", "attention"));
        StackPanel support = new() { Spacing = 8 };
        support.Children.Add(new TextBlock { Text = "Support and rollback", Style = AppStyle("StewardSectionTitleStyle") });
        support.Children.Add(PageCopy("Use only the approved private release channel and its published checksum. If a supported update or package replacement fails, stop, preserve the diagnostic export, and use the product-owner-approved rollback package. Do not download installers from mirrors or third-party portals."));
        panel.Children.Add(Card(support));
        Content = BuildShell(panel);
    }

    private void RenderOverviewPage()
    {
        int ready = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate);
        int review = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired);
        int current = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.Current);
        int unmanaged = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged);
        StackPanel panel = CreatePagePanel();
        string description = viewModel.LastScanAt is null
            ? "Build a private, read-only inventory of installed applications when you are ready."
            : ready > 0
                ? $"{ready} verified update{(ready == 1 ? " is" : "s are")} ready for your review. Nothing starts automatically."
                : review > 0
                    ? $"{review} application{(review == 1 ? " needs" : "s need")} your decision; no action is preselected."
                    : "Your latest local inventory has no verified update waiting for approval.";
        panel.Children.Add(StewardVisuals.PageHeader("Overview", description, "Evidence before action"));

        if (viewModel.LastScanAt is null && viewModel.AllRows.Count == 0)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "Start a read-only local application inventory");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { destination = "scan"; await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("\uE721", "See what is installed", "The first scan reads Windows application records on this PC. It does not request administrator access, connect to package sources, or change software.", start));
            panel.Children.Add(StewardVisuals.InformationBanner("Private by default", "Inventory and decisions stay on this PC. Package sources remain off until you approve their disclosure."));
            Content = BuildShell(panel);
            return;
        }

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button primary = StewardVisuals.PrimaryButton(ready > 0 ? $"Review {ready} update{(ready == 1 ? string.Empty : "s")}" : review > 0 ? $"Review {review} decision{(review == 1 ? string.Empty : "s")}" : "Open applications", ready > 0 ? "Review verified update evidence" : review > 0 ? "Review application decisions" : "Open installed applications");
        primary.IsEnabled = !isExecuting;
        primary.Click += (_, _) => { destination = ready > 0 ? "updates" : review > 0 ? "review" : "applications"; Render(); };
        Button scan = new() { Content = viewModel.IsScanning ? "Scan in progress" : "Scan again", IsEnabled = !HasActiveOperation };
        AutomationProperties.SetName(scan, viewModel.IsScanning ? "Inventory scan in progress" : "Start another local inventory scan");
        scan.Click += async (_, _) => { destination = "scan"; await StartScanAsync(); };
        actions.Children.Add(primary);
        actions.Children.Add(scan);

        Grid hero = new() { ColumnSpacing = 28 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        StackPanel heroCopy = new() { Spacing = 10 };
        heroCopy.Children.Add(new TextBlock { Text = ready > 0 ? "Updates wait for you" : review > 0 ? "Decisions wait for you" : "No action is waiting", Style = AppStyle("StewardDisplayStyle") });
        heroCopy.Children.Add(PageCopy($"PC Steward found {viewModel.AllRows.Count:N0} installed application{(viewModel.AllRows.Count == 1 ? string.Empty : "s")}. It keeps observation, source evidence, your decisions, approval, and verification as separate stages."));
        heroCopy.Children.Add(actions);
        hero.Children.Add(heroCopy);
        StackPanel evidence = new() { Spacing = 2 };
        evidence.Children.Add(StewardVisuals.EvidenceRow("Inventory", viewModel.LastScanCompletedLabel));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Scope", viewModel.ScanModeLabel));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Automatic changes", "None"));
        Grid.SetColumn(evidence, 1);
        hero.Children.Add(evidence);
        panel.Children.Add(Card(hero));

        panel.Children.Add(StewardVisuals.InformationBanner(
            viewModel.IsUpdateSourceEnabled ? "Source evidence is enabled" : "Local inventory only",
            viewModel.UpdateFreshnessLabel,
            viewModel.IsUpdateSourceEnabled ? "information" : "neutral"));

        Grid metrics = new() { ColumnSpacing = 12, RowSpacing = 12 };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        (int count, string label, string detail, string status)[] values =
        [
            (ready, "Ready to review", "Fresh, exact package evidence", "VerifiedUpdate"),
            (review, "Need a decision", "Review-only; no automatic action", "ReviewRequired"),
            (current, "Up to date", "Based on the latest source evidence", "Current"),
            (unmanaged, "Inventoried only", "Visible without being labelled unsafe", "Unmanaged")
        ];
        for (int index = 0; index < values.Length; index++)
        {
            Border metric = StewardVisuals.MetricCard(values[index].count, values[index].label, values[index].detail, values[index].status);
            Grid.SetColumn(metric, index % 2); Grid.SetRow(metric, index / 2); metrics.Children.Add(metric);
        }
        panel.Children.Add(metrics);
        Content = BuildShell(panel);
    }

    private void RenderApplicationsPage()
    {
        StackPanel panel = CreatePagePanel();
        Button scan = new() { Content = "Scan again", IsEnabled = !HasActiveOperation };
        AutomationProperties.SetName(scan, "Start another local inventory scan");
        scan.Click += async (_, _) => { destination = "scan"; await StartScanAsync(); };
        panel.Children.Add(StewardVisuals.PageHeader("Applications", "Search and inspect the canonical local inventory. Filtering never changes saved data.", "Installed software", scan));
        panel.Children.Add(StewardVisuals.InformationBanner("Inventory freshness", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "information"));
        TextBox search = new() { Header = "Search", PlaceholderText = "Name or publisher", Text = viewModel.SearchText };
        AutomationProperties.SetName(search, "Search applications");
        ComboBox status = FilterBox(viewModel.StatusFilter, ["All statuses", "Installed", .. Enum.GetNames<PCSteward.Domain.UpdateIntelligenceStatus>()]);
        ComboBox publisher = FilterBox(viewModel.PublisherFilter, ["All publishers", .. viewModel.AvailablePublishers]);
        ComboBox source = FilterBox(viewModel.SourceFilter, ["All sources", .. viewModel.AllRows.Select(row => row.Provider).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)]);
        ComboBox scope = FilterBox(viewModel.ScopeFilter, ["All scopes", .. viewModel.AllRows.Select(row => row.Scope).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase)]);
        ComboBox sort = FilterBox(viewModel.SortField, "Name", "Source", "Publisher");
        status.Header = "Status"; publisher.Header = "Publisher"; source.Header = "Source"; scope.Header = "Scope"; sort.Header = "Sort by";
        AutomationProperties.SetName(status, "Filter applications by status"); AutomationProperties.SetName(publisher, "Filter applications by publisher"); AutomationProperties.SetName(source, "Filter applications by source"); AutomationProperties.SetName(scope, "Filter applications by scope"); AutomationProperties.SetName(sort, "Sort applications");
        StackPanel filterContent = new() { Spacing = 12 };
        filterContent.Children.Add(search);
        Grid filters = new() { ColumnSpacing = 10, RowSpacing = 10 };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FrameworkElement[] filterControls = [status, publisher, source, scope, sort];
        for (int index = 0; index < filterControls.Length; index++)
        {
            Grid.SetColumn(filterControls[index], index % 3);
            Grid.SetRow(filterControls[index], index / 3);
            filters.Children.Add(filterControls[index]);
        }
        filterContent.Children.Add(filters);
        panel.Children.Add(Card(filterContent));
        ListView list = new() { Style = AppStyle("StewardListViewStyle"), ItemContainerStyle = AppStyle("StewardListItemStyle"), ItemTemplate = (DataTemplate)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardApplicationRowTemplate"], MaxHeight = 620 };
        TextBlock matching = PageCopy(string.Empty);
        Border empty = StewardVisuals.EmptyState("\uE711", "No applications match", "Adjust the search or filters. The saved inventory has not changed.");
        void ApplyFilters()
        {
            viewModel.SetFilters(search.Text, status.SelectedItem?.ToString(), publisher.SelectedItem?.ToString(), source.SelectedItem?.ToString(), scope.SelectedItem?.ToString(), sort.SelectedItem?.ToString());
            RenderInventory(list);
            matching.Text = $"{viewModel.VisibleRows.Count:N0} application{(viewModel.VisibleRows.Count == 1 ? string.Empty : "s")} match the current filters.";
            list.Visibility = viewModel.VisibleRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = viewModel.VisibleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => ApplyFilters(); status.SelectionChanged += (_, _) => ApplyFilters(); publisher.SelectionChanged += (_, _) => ApplyFilters(); source.SelectionChanged += (_, _) => ApplyFilters(); scope.SelectionChanged += (_, _) => ApplyFilters(); sort.SelectionChanged += (_, _) => ApplyFilters();
        list.ItemClick += async (_, args) => await ShowDetailsAsync(((ApplicationListItem)args.ClickedItem).Row);
        ApplyFilters();
        panel.Children.Add(matching);
        Grid columns = new() { Padding = new Thickness(12, 0, 12, 0), ColumnSpacing = 12 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        TextBlock applicationHeader = new() { Text = "APPLICATION", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(applicationHeader, 1); columns.Children.Add(applicationHeader);
        TextBlock versionHeader = new() { Text = "VERSION & SOURCE", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(versionHeader, 2); columns.Children.Add(versionHeader);
        TextBlock statusHeader = new() { Text = "STATUS", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(statusHeader, 3); columns.Children.Add(statusHeader);
        panel.Children.Add(columns);
        panel.Children.Add(list);
        panel.Children.Add(empty);
        Content = BuildShell(panel);
    }

    private void RenderCurrentPage()
    {
        StackPanel panel = CreatePagePanel(960);
        IReadOnlyList<InventoryRow> managedRows = viewModel.RepositoryManagedRows;
        int upToDate = managedRows.Count(row => row.Status == "Current");
        int updatesAvailable = managedRows.Count(row => row.Status == "VerifiedUpdate");
        int reviewRequired = managedRows.Count(row => row.Status == "ReviewRequired");
        int unavailable = managedRows.Count(row => row.Status == "FailedUnavailable");
        panel.Children.Add(StewardVisuals.PageHeader("Repository apps", "Installed applications with a concrete identity from an enabled, approved package source.", "Contextual view"));
        panel.Children.Add(StewardVisuals.InformationBanner("Repository evidence", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button refresh = new()
        {
            Content = viewModel.IsCheckingUpdates ? "Checking connected repositories" : "Refresh connected repositories",
            IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting
        };
        AutomationProperties.SetName(refresh, "Refresh connected repositories");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        actions.Children.Add(refresh);
        if (!viewModel.IsUpdateSourceEnabled)
        {
            Button settings = new() { Content = "Open source settings" };
            AutomationProperties.SetName(settings, "Open source settings");
            settings.Click += (_, _) => { destination = "settings"; Render(); };
            actions.Children.Add(settings);
        }
        panel.Children.Add(actions);

        if (managedRows.Count == 0)
        {
            string empty = viewModel.IsUpdateSourceEnabled
                ? "No installed application currently has a concrete managed package identity from the connected repositories. Refresh the connected repositories to update this view; no update or download will start."
                : "No repository is enabled, so installed applications cannot be assessed as repository-managed or up to date. Enable WinGet trusted sources after its disclosure, then refresh when you choose.";
            panel.Children.Add(StewardVisuals.EmptyState("\uE73E", "No repository-managed applications", empty));
        }
        else
        {
            panel.Children.Add(PageCopy($"{managedRows.Count:N0} repository-managed app{(managedRows.Count == 1 ? string.Empty : "s")}: {upToDate} up to date, {updatesAvailable} update{(updatesAvailable == 1 ? string.Empty : "s")} available, {reviewRequired} requiring review, {unavailable} with update evidence unavailable."));
            foreach (InventoryRow row in managedRows)
            {
                UpdateDetail detail = viewModel.UpdateDetails[row.StableProductKey];
                StackPanel current = new() { Spacing = 5 };
                Grid heading = new() { ColumnSpacing = 12 };
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                heading.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
                Border badge = StewardVisuals.StatusBadge(row.Status); Grid.SetColumn(badge, 1); heading.Children.Add(badge);
                current.Children.Add(heading);
                current.Children.Add(PageCopy(row.Publisher));
                current.Children.Add(PageCopy($"Installed version: {row.Version}{(detail.CandidateVersion is null || detail.CandidateVersion == row.Version ? string.Empty : $" · repository version: {detail.CandidateVersion}")}"));
                current.Children.Add(PageCopy($"Repository: {detail.SourceId} · Package ID: {detail.PackageId}"));
                current.Children.Add(PageCopy(detail.Freshness));
                Button evidence = new() { Content = "Open evidence" };
                AutomationProperties.SetName(evidence, $"Open repository evidence for {row.Name}");
                evidence.Click += async (_, _) => await ShowDetailsAsync(row);
                current.Children.Add(evidence);
                panel.Children.Add(Card(current));
            }
        }
        Content = BuildShell(panel);
    }

    private static string RepositoryManagementStatus(string status) => status switch
    {
        "Current" => "Managed · up to date",
        "VerifiedUpdate" => "Managed · update available",
        "ReviewRequired" => "Repository match · review required",
        "FailedUnavailable" => "Repository match · update evidence unavailable",
        _ => "Repository status unavailable"
    };

    private void RenderResultsPage()
    {
        StackPanel panel = CreatePagePanel();
        int actionable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString());
        int decisions = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString());
        int unavailable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable.ToString());
        int unmanaged = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged.ToString());
        panel.Children.Add(StewardVisuals.PageHeader("Inventory results", "Verified opportunities, decisions, unavailable update evidence, and inventoried-only software stay visibly separate. No update starts from this summary.", "Completed inventory"));
        panel.Children.Add(StewardVisuals.InformationBanner("Saved snapshot", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "success"));
        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach ((ResultsFilter option, string label) in new[]
        {
            (ResultsFilter.All, $"All {actionable + decisions + unavailable + unmanaged}"),
            (ResultsFilter.Actionable, $"Actionable {actionable}"),
            (ResultsFilter.NeedsDecision, $"Needs decision {decisions}"),
            (ResultsFilter.Unavailable, $"Unavailable {unavailable}"),
            (ResultsFilter.Unmanaged, $"Unmanaged {unmanaged}")
        })
        {
            Button filter = new() { Content = label, IsEnabled = resultsFilter != option };
            AutomationProperties.SetName(filter, $"Show {option} scan results");
            filter.Click += (_, _) => { resultsFilter = option; Render(); };
            filters.Children.Add(filter);
        }
        panel.Children.Add(filters);
        InventoryRow[] currentIdentityCandidates = viewModel.AllRows
            .Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString())
            .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)
            .ToArray();
        Button verifyAll = new()
        {
            Content = currentIdentityCandidates.Length == 0 ? "Verify all current package identities" : $"Verify all {currentIdentityCandidates.Length} current package identities",
            IsEnabled = currentIdentityCandidates.Length > 0
        };
        AutomationProperties.SetName(verifyAll, "Verify all source-reported current package identities");
        verifyAll.Click += async (_, _) => await ConfirmCurrentPackageIdentitiesAsync(currentIdentityCandidates);
        panel.Children.Add(verifyAll);
        if (currentIdentityCandidates.Length == 0) panel.Children.Add(PageCopy("No source-reported current package identities are available to verify."));
        InventoryRow[] resultRows = viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "FailedUnavailable" or "Unmanaged")
            .Where(row => resultsFilter switch
            {
                ResultsFilter.Actionable => row.Status == "VerifiedUpdate",
                ResultsFilter.NeedsDecision => row.Status == "ReviewRequired",
                ResultsFilter.Unavailable => row.Status == "FailedUnavailable",
                ResultsFilter.Unmanaged => row.Status == "Unmanaged",
                _ => true
            }).ToArray();
        if (resultRows.Length == 0) panel.Children.Add(StewardVisuals.EmptyState("\uE711", "No results match this view", "Choose another result filter or open the full Applications inventory."));
        foreach (InventoryRow row in resultRows)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel finding = new() { Spacing = 5 };
            finding.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
            finding.Children.Add(StewardVisuals.StatusBadge(row.Status, $"{row.Name}: {StewardStatus.From(row.Status).Label}"));
            finding.Children.Add(PageCopy($"{StewardPresentation.Provider(row.Provider)} · {StewardPresentation.Scope(row.Scope)}"));
            finding.Children.Add(PageCopy(row.Status switch
            {
                "VerifiedUpdate" => "Open the exact source evidence before a separate update confirmation.",
                "ReviewRequired" => "Inspect the evidence and choose the applicable safe decision.",
                "FailedUnavailable" => "The repository did not establish update availability. Retry the source evidence; there is no decision or update to approve.",
                _ => "Inventoried locally; unmanaged is visible, not labelled unsafe."
            }));
            if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired))
            {
                AddReviewDecisionActions(finding, row, detail);
            }
            else if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable))
            {
                finding.Children.Add(PageCopy(detail?.ActionState ?? "Refresh source evidence to retry. No update starts."));
                StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
                if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled) actions.Children.Add(CreateSourceEvidenceRefreshButton(row));
                Button open = new() { Content = "Open source evidence" }; AutomationProperties.SetName(open, $"Open unavailable source evidence for {row.Name}"); open.Click += async (_, _) => await ShowDetailsAsync(row); actions.Children.Add(open);
                finding.Children.Add(actions);
            }
            else
            {
                Button open = new() { Content = "Open evidence" }; AutomationProperties.SetName(open, $"Open {row.Name} evidence"); open.Click += async (_, _) => await ShowDetailsAsync(row); finding.Children.Add(open);
            }
            panel.Children.Add(Card(finding));
        }
        if (downloadCache.RecoverableBytes > 0)
        {
            Button storage = new() { Content = $"Review {downloadCache.RecoverableBytes:N0} bytes of app-owned downloads" }; storage.Click += (_, _) => { destination = "storage"; Render(); }; panel.Children.Add(storage);
        }
        Button review = new() { Content = "Review available actions" }; review.Click += (_, _) => { destination = "review"; Render(); }; panel.Children.Add(review);
        Content = BuildShell(panel);
    }

    private void RenderReviewPage()
    {
        StackPanel panel = CreatePagePanel();
        InventoryRow[] ready = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString()).ToArray();
        InventoryRow[] decisions = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString()).ToArray();
        panel.Children.Add(StewardVisuals.PageHeader("Review actions", "Resolve package-identity decisions first. Verified updates remain separate and still require an exact final confirmation.", "Decision checkpoint"));
        panel.Children.Add(StewardVisuals.InformationBanner("Review is not approval", "Opening evidence or recording a reversible package mapping does not start an update."));
        panel.Children.Add(new TextBlock { Text = "Needs a decision", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(PageCopy(decisions.Length == 0 ? "No source-matched items currently need a decision." : $"{decisions.Length} item{(decisions.Length == 1 ? string.Empty : "s")} need a decision. These are not approved updates."));
        foreach (InventoryRow row in decisions)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel decision = new() { Spacing = 5 };
            decision.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
            decision.Children.Add(StewardVisuals.StatusBadge(row.Status));
            decision.Children.Add(PageCopy($"{detail?.SourceId ?? row.Provider} · {detail?.PackageId ?? "package identity unavailable"}"));
            decision.Children.Add(PageCopy(detail?.Explanation ?? "The available evidence needs review before any update can be considered."));
            AddReviewDecisionActions(decision, row, detail);
            panel.Children.Add(Card(decision));
        }
        InventoryRow[] currentIdentityCandidates = decisions
            .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)
            .ToArray();
        if (currentIdentityCandidates.Length > 0)
        {
            panel.Children.Add(PageCopy($"{currentIdentityCandidates.Length} item{(currentIdentityCandidates.Length == 1 ? string.Empty : "s")} can be confirmed together because the repository reports its installed version is already the available version."));
            Button confirmAll = new() { Content = "Confirm all current package identities" };
            AutomationProperties.SetName(confirmAll, "Confirm all source-reported current package identities");
            confirmAll.Click += async (_, _) => await ConfirmCurrentPackageIdentitiesAsync(currentIdentityCandidates);
            panel.Children.Add(confirmAll);
        }
        panel.Children.Add(new TextBlock { Text = "Approved updates", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(PageCopy(ready.Length == 0 ? "No approved actions are ready. Cached, unavailable, low-confidence, or conflicting evidence remains review-only." : $"{ready.Length} approved action{(ready.Length == 1 ? string.Empty : "s")} can be opened for separate confirmation."));
        foreach (InventoryRow row in ready)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel action = new() { Spacing = 5 };
            action.Children.Add(new TextBlock { Text = $"{row.Name} {row.Version} → {detail?.CandidateVersion ?? "candidate unavailable"}", Style = AppStyle("StewardSectionTitleStyle") });
            action.Children.Add(StewardVisuals.StatusBadge(row.Status));
            action.Children.Add(PageCopy($"{detail?.SourceId ?? "Unknown source"} · exact identity required · {row.Scope} scope · {detail?.Architecture ?? "Unknown architecture"}"));
            Button open = new() { Content = "Review evidence" }; AutomationProperties.SetName(open, $"Review {row.Name} evidence"); open.Click += async (_, _) => await ShowDetailsAsync(row); action.Children.Add(open);
            panel.Children.Add(Card(action));
        }
        panel.Children.Add(PageCopy("Sequential and verified: PC Steward rechecks the installed version before recording an update as successful."));
        Content = BuildShell(panel);
    }

    private void AddReviewDecisionActions(StackPanel container, InventoryRow row, UpdateDetail? detail)
    {
        container.Children.Add(PageCopy(detail?.ActionState ?? "Available actions: pin the installed version, ignore the product, or inspect the evidence."));
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled)
        {
            actions.Children.Add(CreateSourceEvidenceRefreshButton(row));
        }
        if (detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true } && viewModel.IsUpdateSourceEnabled)
        {
            Button confirm = StewardVisuals.PrimaryButton("Confirm package identity", $"Confirm {detail.PackageId} is {row.Name}");
            confirm.Click += async (_, _) => await PromptAndConfirmPackageIdentityAsync(row, detail);
            actions.Children.Add(confirm);
        }
        else if (detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0 && viewModel.IsUpdateSourceEnabled)
        {
            Button choose = StewardVisuals.PrimaryButton("Choose package identity", $"Choose the exact approved package identity for {row.Name}");
            choose.Click += async (_, _) => await ShowDetailsAsync(row);
            actions.Children.Add(choose);
        }
        Button pin = new() { Content = "Pin current version" };
        AutomationProperties.SetName(pin, $"Pin {row.Name} at installed version {row.Version}");
        pin.Click += async (_, _) => await PromptAndPinCurrentVersionAsync(row);
        actions.Children.Add(pin);
        Button ignore = new() { Content = "Ignore…" };
        AutomationProperties.SetName(ignore, $"Choose how long to ignore {row.Name}");
        ignore.Click += async (_, _) => await PromptAndIgnoreProductAsync(row);
        actions.Children.Add(ignore);
        Button open = new() { Content = "Review details" };
        AutomationProperties.SetName(open, $"Review {row.Name} decision evidence and options");
        open.Click += async (_, _) => await ShowDetailsAsync(row);
        actions.Children.Add(open);
        container.Children.Add(actions);
    }

    private Button CreateSourceEvidenceRefreshButton(InventoryRow row)
    {
        Button refresh = StewardVisuals.PrimaryButton(
            viewModel.IsCheckingUpdates ? "Checking source evidence…" : "Refresh source evidence",
            $"Refresh approved repository evidence for {row.Name}");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting;
        AutomationProperties.SetHelpText(refresh, "Checks approved repository metadata only. It does not download or install an update.");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        return refresh;
    }

    private async Task PromptAndConfirmPackageIdentityAsync(InventoryRow row, UpdateDetail detail)
    {
        TextBox reason = DecisionReasonBox("Optional reason for confirming this package identity");
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(PageCopy($"Confirm only if you recognise {detail.PackageId} from {detail.SourceId} as {row.Name}. This records a reversible mapping. It does not approve or start an update."));
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirm package identity?",
            Content = content,
            PrimaryButtonText = "Confirm identity",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        string decisionReason = reason.Text;
        PCSteward.Domain.ManualMappingValidation validation;
        try { validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, detail.SourceId!, detail.PackageId!, viewModel.IsUpdateSourceEnabled, decisionReason, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        if (!validation.IsValid) { await ShowUpdateOutcomeAsync("Identity not confirmed", validation.Explanation); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Package identity confirmed", validation.Explanation + " No update was started.");
    }

    private async Task PromptAndPinCurrentVersionAsync(InventoryRow row)
    {
        TextBox reason = DecisionReasonBox("Optional reason for pinning this version");
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(PageCopy($"Pin {row.Name} at the installed version {row.Version}? PC Steward will stop offering a different version until you reverse this decision in Settings. No application is changed."));
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Pin current version?",
            Content = content,
            PrimaryButtonText = "Pin version",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        string decisionReason = reason.Text;
        try { await DataOperationLease.RunAsync(token => workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, decisionReason, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Current version pinned", $"{row.Name} is pinned at {row.Version}. No application was changed.");
    }

    private async Task PromptAndIgnoreProductAsync(InventoryRow row)
    {
        CheckBox untilEnabled = new() { Content = "Ignore until a selected date" };
        DatePicker until = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) };
        untilEnabled.Checked += (_, _) => until.IsEnabled = true;
        untilEnabled.Unchecked += (_, _) => until.IsEnabled = false;
        TextBox reason = DecisionReasonBox("Optional reason for ignoring this product");
        StackPanel content = new() { Spacing = 10 };
        content.Children.Add(PageCopy($"Ignore {row.Name} indefinitely, or select an end date below. You can reverse this decision in Settings. No application is changed."));
        content.Children.Add(untilEnabled);
        content.Children.Add(until);
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Ignore this product?",
            Content = content,
            PrimaryButtonText = "Ignore product",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        DateTimeOffset? ignoredUntil = untilEnabled.IsChecked == true ? until.Date : null;
        string decisionReason = reason.Text;
        try { await DataOperationLease.RunAsync(token => workflow.IgnoreAsync(row.StableProductKey, ignoredUntil, decisionReason, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Product ignored", ignoredUntil is null ? $"{row.Name} is ignored until you reverse the decision. No application was changed." : $"{row.Name} is ignored until {ignoredUntil.Value.ToLocalTime():d MMMM yyyy}. No application was changed.");
    }

    private static TextBox DecisionReasonBox(string placeholder) => new() { Header = "Optional reason", PlaceholderText = placeholder, MaxLength = 512 };

    private async Task ConfirmCurrentPackageIdentitiesAsync(IReadOnlyCollection<InventoryRow> candidates)
    {
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirm current package identities?",
            Content = "This records reversible mappings only for the listed items where the approved source reports its installed version equals the available version. It skips newer, unknown, unavailable or conflicting candidates. No update will start.",
            PrimaryButtonText = "Confirm mappings",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        RepositoryIdentityConfirmation[] confirmations = candidates
            .Select(row => viewModel.UpdateDetails[row.StableProductKey])
            .Select(detail => new RepositoryIdentityConfirmation(detail.StableProductKey, detail.SourceId!, detail.PackageId!))
            .ToArray();
        try { RepositoryIdentityConfirmationResult result = await DataOperationLease.RunAsync(token => workflow.ConfirmCurrentRepositoryIdentitiesAsync(confirmations, viewModel.IsUpdateSourceEnabled, "Bulk confirmation of source-reported current package identity.", token), CancellationToken.None); LoadSavedInventory(); await ShowUpdateOutcomeAsync(result.Confirmed == 0 ? "No mappings confirmed" : "Package identities confirmed", result.Message); Render(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
    }

    private void RenderStoragePage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Storage", "Review space used by PC Steward-owned records and downloaded update files.", "App-owned data"));
        panel.Children.Add(StewardVisuals.InformationBanner("Bounded scope", "Windows temporary files, provider caches, documents, drivers, firmware, and installed applications are outside this screen's scope."));
        StackPanel summary = new() { Spacing = 8 };
        summary.Children.Add(new TextBlock { Text = "App-owned storage", Style = AppStyle("StewardSectionTitleStyle") });
        summary.Children.Add(PageCopy($"Scan data: {scanData.ItemCount:N0} items; estimated payload {scanData.SizeBytes:N0} characters."));
        summary.Children.Add(PageCopy($"Downloaded update files: {downloadCache.TotalBytes:N0} bytes total; {downloadCache.RecoverableBytes:N0} bytes recoverable."));
        summary.Children.Add(PageCopy("Source metadata, logs, and update history are controlled separately in Privacy & data."));
        panel.Children.Add(Card(summary));
        Button privacy = new() { Content = "Manage privacy and data" }; privacy.Click += (_, _) => { destination = "privacy"; Render(); }; panel.Children.Add(privacy);
        Content = BuildShell(panel);
    }

    private void RenderActivityPage()
    {
        StackPanel panel = CreatePagePanel();
        Button privacy = new() { Content = "Privacy & data" };
        AutomationProperties.SetName(privacy, "View privacy and local data controls");
        privacy.Click += (_, _) => { destination = "privacy"; Render(); };
        panel.Children.Add(StewardVisuals.PageHeader("Activity", "A local ledger of completed inventory and reversible decisions. PC Steward does not upload activity telemetry.", "Local record", privacy));

        List<(DateTimeOffset OccurredAt, string Glyph, string Title, string Detail)> activity = [];
        if (viewModel.LastScanAt is { } lastScan)
            activity.Add((lastScan, "\uE721", "Inventory completed", $"{viewModel.AllRows.Count:N0} canonical applications · {viewModel.ScanModeLabel}."));
        activity.AddRange(activeDecisions.Select(decision =>
            (decision.CreatedAt, "\uE8C8", $"{decision.Kind} decision saved", $"{decision.Value}{(string.IsNullOrWhiteSpace(decision.Reason) ? string.Empty : $" · {decision.Reason}")}")));
        activity.AddRange(manualMappings.Select(mapping =>
            (mapping.CreatedAt, "\uE8D7", "Package identity confirmed", $"{mapping.SourceId}:{mapping.PackageId}{(string.IsNullOrWhiteSpace(mapping.Reason) ? string.Empty : $" · {mapping.Reason}")}")));

        if (activity.Count == 0)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "Start a read-only local application inventory");
            start.Click += async (_, _) => { destination = "scan"; await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("\uE823", "No local activity yet", "Complete an inventory or save a decision and its timestamped record will appear here.", start));
        }
        else
        {
            StackPanel timeline = new() { Spacing = 0 };
            foreach ((DateTimeOffset occurredAt, string glyph, string title, string detail) in activity.OrderByDescending(item => item.OccurredAt).Take(50))
                timeline.Children.Add(StewardVisuals.ActivityRow(occurredAt, glyph, title, detail));
            panel.Children.Add(Card(timeline));
            panel.Children.Add(PageCopy($"Showing {Math.Min(activity.Count, 50):N0} of {activity.Count:N0} locally available record{(activity.Count == 1 ? string.Empty : "s")}. Update execution outcomes remain attached to each application's technical timeline."));
        }
        AddReviewedUpdateOutcomesSection(panel);
        panel.Children.Add(StewardVisuals.InformationBanner("Current source evidence", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "neutral"));
        Content = BuildShell(panel);
    }

    /// <summary>Admission obligation 5: the read-only reviewed-update outcomes (route, verification, lingering processes, helper report).</summary>
    private void AddReviewedUpdateOutcomesSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = ReviewedUpdateHistoryListViewModel.Heading, Style = AppStyle("StewardSectionTitleStyle") });
        StackPanel outcomes = new() { Spacing = 8 };
        AutomationProperties.SetName(outcomes, ReviewedUpdateHistoryListViewModel.Heading);
        outcomes.Children.Add(PageCopy(ReviewedUpdateHistoryListViewModel.LoadingText));
        panel.Children.Add(outcomes);
        bool loaded = false;
        outcomes.Loaded += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            ReviewedUpdateHistoryListViewModel history;
            try { history = await DataOperationLease.RunReadAsync(token => ReviewedUpdateHistoryListViewModel.LoadAsync(reviewedHistory, token), CancellationToken.None); }
            catch (Exception) { history = ReviewedUpdateHistoryListViewModel.Unavailable; }
            if (!windowClosed) ReviewedRouteSurfaces.RenderHistory(outcomes, history);
        };
    }

    private void RenderPrivacyPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Privacy & data", "Export or remove PC Steward-owned information with explicit, category-specific controls.", "Stored locally"));
        panel.Children.Add(StewardVisuals.InformationBanner("Installed applications are preserved", "Routine cleanup affects only the named PC Steward category. Factory reset is separate and also preserves installed applications and provider caches."));
        AddDataAndResetSection(panel);
        Content = BuildShell(panel);
    }

    private static Style AppStyle(string key) => (Style)global::Microsoft.UI.Xaml.Application.Current.Resources[key];

    private NavigationView BuildShell(StackPanel panel)
    {
        Grid shell = new() { Background = (Microsoft.UI.Xaml.Media.Brush)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardCanvasBrush"] };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border header = new()
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardSurfaceBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardBorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 12, 24, 12)
        };
        Grid headerContents = new() { ColumnSpacing = 20 };
        headerContents.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerContents.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel brand = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        Image mark = new() { Width = 32, Height = 32, Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png")) };
        AutomationProperties.SetName(mark, AppIdentity.DisplayName);
        brand.Children.Add(mark);
        StackPanel copy = new() { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, Style = AppStyle("StewardSectionTitleStyle") });
        copy.Children.Add(new TextBlock { Text = "Evidence before action", Style = AppStyle("StewardCaptionStyle") });
        brand.Children.Add(copy);
        headerContents.Children.Add(brand);
        StackPanel trust = new() { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        trust.Children.Add(StewardVisuals.Badge("Stored locally", "\uE72E", "success", "PC Steward data is stored locally"));
        trust.Children.Add(new TextBlock { Text = HasActiveOperation ? "Work in progress" : "Ready", Style = AppStyle("StewardCaptionStyle"), VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(trust, 1);
        headerContents.Children.Add(trust);
        header.Child = headerContents;
        shell.Children.Add(header);

        Grid operation = new() { ColumnSpacing = 16 };
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        operationStatusText = new TextBlock { Text = CurrentOperationLabel, Foreground = StewardVisuals.Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        TextBlock progressLabel = operationStatusText;
        progressLabel.Loaded += (_, _) => { if (ReferenceEquals(operationStatusText, progressLabel) && isExecuting) StartUpdateProgressPolling(); };
        progressLabel.Unloaded += (_, _) => { if (ReferenceEquals(updateProgressTarget, progressLabel)) StopUpdateProgressPolling(); };
        operation.Children.Add(operationStatusText);
        ProgressBar progress = new() { IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center, Height = 4 };
        AutomationProperties.SetName(progress, "Current PC Steward operation progress");
        Grid.SetColumn(progress, 1);
        operation.Children.Add(progress);
        Button cancel = new() { Content = "Cancel scan", Visibility = viewModel.IsScanning || scanCancellation is not null ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(cancel, "Cancel the active local inventory scan");
        cancel.Click += (_, _) => scanCancellation?.Cancel();
        Grid.SetColumn(cancel, 2);
        operation.Children.Add(cancel);
        operationBand = new Border
        {
            Background = StewardVisuals.Brush("StewardBrandSubtleBrush"),
            BorderBrush = StewardVisuals.Brush("StewardBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 10, 24, 10),
            Visibility = HasActiveOperation ? Visibility.Visible : Visibility.Collapsed,
            Child = operation
        };
        Grid.SetRow(operationBand, 1);
        shell.Children.Add(operationBand);

        ScrollViewer content = new()
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(content, 2);
        shell.Children.Add(content);
        NavigationView navigation = new()
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            OpenPaneLength = 232,
            CompactPaneLength = 56,
            CompactModeThresholdWidth = 900,
            ExpandedModeThresholdWidth = 1100,
            AlwaysShowHeader = false,
            SelectionFollowsFocus = NavigationViewSelectionFollowsFocus.Disabled,
            Content = shell
        };
        AddNavigationItem(navigation, "Overview", "overview", "\uE80F");
        AddNavigationItem(navigation, "Applications", "applications", "\uE71D");
        AddNavigationItem(navigation, "Updates", "updates", "\uE896");
        AddNavigationItem(navigation, "Activity", "activity", "\uE823");
        AddNavigationItem(navigation, "About & support", "about", "\uE897", true);
        AddNavigationItem(navigation, "Settings", "settings", "\uE713", true);
        string principalDestination = destination switch
        {
            "scan" or "results" or "onboarding" => "overview",
            "current" or "detail" => "applications",
            "review" => "updates",
            _ => destination
        };
        navigation.SelectedItem = navigation.MenuItems.Concat(navigation.FooterMenuItems).OfType<NavigationViewItem>().FirstOrDefault(item => string.Equals(item.Tag as string, principalDestination, StringComparison.Ordinal));
        navigation.SelectionChanged += (_, args) =>
        {
            string? selected = (args.SelectedItem as NavigationViewItem)?.Tag as string;
            if (selected is not null && !string.Equals(selected, destination, StringComparison.Ordinal)) { destination = selected; Render(); }
        };
        return navigation;
    }

    private static void AddNavigationItem(NavigationView navigation, string label, string tag, string glyph, bool footer = false)
    {
        NavigationViewItem item = new() { Content = label, Tag = tag, Icon = new FontIcon { Glyph = glyph } };
        AutomationProperties.SetName(item, label);
        if (footer) navigation.FooterMenuItems.Add(item); else navigation.MenuItems.Add(item);
    }

    private void RenderPlaceholder()
    {
        string pageTitle = destination switch { "about" => "About & support", _ => AppIdentity.DisplayName };
        StackPanel panel = new() { Spacing = 12, Padding = new Thickness(24), MaxWidth = 1040 };
        panel.Children.Add(new TextBlock { Text = pageTitle, Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(new TextBlock { Text = "This destination preserves the approved navigation structure.", TextWrapping = TextWrapping.Wrap });
        Content = BuildShell(panel);
    }

    private void RenderScanPage()
    {
        StackPanel panel = CreatePagePanel(900);
        panel.Children.Add(StewardVisuals.PageHeader("Inventory scan", viewModel.IsScanning || scanCancellation is not null ? "PC Steward is reading installed-application records. Your prior completed snapshot remains available until a replacement completes." : viewModel.LastScanAt is null ? "Start a private, read-only inventory. Nothing is installed, removed, or elevated." : "Review the current inventory scope before replacing the saved snapshot.", "Read only"));
        scanPageStatusText = new TextBlock { Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}", Style = AppStyle("StewardSectionTitleStyle") };
        StackPanel state = new() { Spacing = 8 };
        state.Children.Add(scanPageStatusText);
        state.Children.Add(StewardVisuals.EvidenceRow("Saved inventory", viewModel.LastScanAt is null ? "No completed scan" : viewModel.LastScanCompletedLabel));
        state.Children.Add(StewardVisuals.EvidenceRow("Selected scope", viewModel.ScanModeLabel));
        state.Children.Add(StewardVisuals.EvidenceRow("Administrator access", "Not requested"));
        panel.Children.Add(Card(state));
        if (viewModel.LastScanWarnings.Count != 0) panel.Children.Add(StewardVisuals.InformationBanner("Some providers reported a limitation", string.Join(" ", viewModel.LastScanWarnings) + " Previous completed data is retained where available.", "attention"));
        if (!viewModel.IsScanning && scanCancellation is null)
        {
            StackPanel scope = new() { Spacing = 8 };
            scope.Children.Add(new TextBlock { Text = "Inventory scope", Style = AppStyle("StewardSectionTitleStyle") });
            scope.Children.Add(new CheckBox { Content = "Windows application inventory (required · read-only)", IsChecked = true, IsEnabled = false });
            ToggleSwitch winget = new() { Header = "WinGet and Chocolatey trusted sources", IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Local only", OnContent = "Enabled after disclosure" };
            AutomationProperties.SetName(winget, "Enable WinGet and Chocolatey trusted sources"); winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn); scope.Children.Add(winget);
            scope.Children.Add(PageCopy("Enabling these sources may disclose installed package identifiers to WinGet and Chocolatey Community. Scanning remains read-only; an update still needs its own review and confirmation."));
            panel.Children.Add(Card(scope));
        }
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button scan = StewardVisuals.PrimaryButton(viewModel.LastScanAt is null ? "Start inventory" : "Replace saved inventory", viewModel.LastScanAt is null ? "Start local inventory" : "Start a replacement local inventory");
        scan.IsEnabled = !HasActiveOperation;
        AutomationProperties.SetName(scan, "Scan this PC"); scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning || scanCancellation is not null) { Button cancel = new() { Content = "Cancel scan", AccessKey = "C" }; AutomationProperties.SetName(cancel, "Cancel scan"); cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(StewardVisuals.InformationBanner("No action is implied", "A scan never approves, downloads, or installs an update. Connected package sources remain separately consented."));
        Content = BuildShell(panel);
    }

    private void RenderUpdatesPage()
    {
        StackPanel panel = CreatePagePanel(960);
        int ready = viewModel.AllRows.Count(row => row.Status == "VerifiedUpdate");
        int decisions = viewModel.AllRows.Count(row => row.Status == "ReviewRequired");
        int unavailable = viewModel.AllRows.Count(row => row.Status == "FailedUnavailable");
        Button refresh = new() { Content = viewModel.IsCheckingUpdates ? "Checking sources" : "Refresh evidence", IsEnabled = viewModel.IsUpdateSourceEnabled && !HasActiveOperation };
        AutomationProperties.SetName(refresh, "Refresh approved package source evidence");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        panel.Children.Add(StewardVisuals.PageHeader("Updates", "Fresh, compatible, exact package evidence can be reviewed here. Approval and execution remain separate.", "Your approval is required", refresh));
        panel.Children.Add(StewardVisuals.InformationBanner(viewModel.IsUpdateSourceEnabled ? "Package-source freshness" : "Package sources are off", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));

        Grid summary = new() { ColumnSpacing = 12 };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border readyCard = StewardVisuals.MetricCard(ready, "Ready to review", "A final confirmation is still required", "VerifiedUpdate"); summary.Children.Add(readyCard);
        Border decisionCard = StewardVisuals.MetricCard(decisions, "Need a decision", "Not eligible for execution", "ReviewRequired"); Grid.SetColumn(decisionCard, 1); summary.Children.Add(decisionCard);
        Border unavailableCard = StewardVisuals.MetricCard(unavailable, "Update evidence unavailable", "The repository did not provide usable availability evidence", "FailedUnavailable"); Grid.SetColumn(unavailableCard, 2); summary.Children.Add(unavailableCard);
        panel.Children.Add(summary);

        bool hasEvidence = false;
        foreach (InventoryRow row in viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "FailedUnavailable"))
        {
            hasEvidence = true;
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            string action = row.Status switch
            {
                "VerifiedUpdate" when detail is not null => "Open evidence review from Applications before any update can start.",
                "FailedUnavailable" => "Refresh source evidence to retry. This is not a decision or an approved update.",
                _ => "No update action is available: evidence is review-only, stale, or not an exact match."
            };
            StackPanel update = new() { Spacing = 5 };
            Grid heading = new() { ColumnSpacing = 12 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new() { Spacing = 2 };
            identity.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
            identity.Children.Add(PageCopy(row.Publisher));
            heading.Children.Add(identity);
            Border badge = StewardVisuals.StatusBadge(row.Status, $"{row.Name}: {StewardStatus.From(row.Status).Label}"); Grid.SetColumn(badge, 1); heading.Children.Add(badge);
            update.Children.Add(heading);
            update.Children.Add(StewardVisuals.EvidenceRow("Versions", $"{row.Version} → {detail?.CandidateVersion ?? "Not established"}"));
            update.Children.Add(StewardVisuals.EvidenceRow("Target", $"{StewardPresentation.Scope(row.Scope)} · {detail?.Architecture ?? "Unknown architecture"}"));
            update.Children.Add(StewardVisuals.EvidenceRow("Package", $"{detail?.SourceId ?? StewardPresentation.Provider(row.Provider)} · {detail?.PackageId ?? "Identity not established"}"));
            update.Children.Add(PageCopy(detail?.Freshness ?? action));
            Button review = new() { Content = row.Status switch { "VerifiedUpdate" => "Review update evidence", "FailedUnavailable" => "Review source evidence", _ => "Review decision evidence" } }; AutomationProperties.SetName(review, $"Review {row.Name} evidence. {StewardStatus.From(row.Status).Label}. {action}"); review.Click += async (_, _) => await ShowDetailsAsync(row); update.Children.Add(review);
            panel.Children.Add(Card(update));
        }
        if (!hasEvidence)
        {
            Button applications = new() { Content = "View applications" };
            applications.Click += (_, _) => { destination = "applications"; Render(); };
            panel.Children.Add(StewardVisuals.EmptyState("\uE73E", "Nothing needs update review", "Local-only, stale, unavailable, and low-confidence evidence never creates an install action.", applications));
        }
        Content = BuildShell(panel);
    }

    private void RenderSettings()
    {
        StackPanel panel = CreatePagePanel(900);
        panel.Children.Add(StewardVisuals.PageHeader("Settings", "Manage inventory scope, package-source consent, scheduled checks, reversible decisions, and local data.", "Preferences"));
        panel.Children.Add(StewardVisuals.InformationBanner("Source consent stays separate", "Enabling a source never starts a scan, source refresh, download, or update."));

        StackPanel sources = new() { Spacing = 12 };
        sources.Children.Add(new TextBlock { Text = "Inventory and update sources", Style = AppStyle("StewardSectionTitleStyle") });
        sources.Children.Add(PageCopy("Local Windows inventory is required. Network-backed package sources remain optional and show a disclosure before first use."));
        sources.Children.Add(new CheckBox { Content = "Windows local inventory (required · local only)", IsChecked = true, IsEnabled = false });
        ConnectorOption appx = viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog");
        CheckBox includeAppx = new() { Content = "Include current-user AppX/MSIX catalog", IsChecked = appx.IsEnabled };
        AutomationProperties.SetName(includeAppx, "Include current-user AppX/MSIX catalog");
        includeAppx.Checked += async (_, _) => await ChangeConnectorAsync(appx.Name, true);
        includeAppx.Unchecked += async (_, _) => await ChangeConnectorAsync(appx.Name, false);
        sources.Children.Add(includeAppx);
        ToggleSwitch winget = new() { Header = "WinGet and Chocolatey trusted sources", IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Disabled", OnContent = "Enabled after disclosure" };
        AutomationProperties.SetName(winget, "Enable WinGet and Chocolatey trusted sources");
        winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn);
        sources.Children.Add(winget);
        panel.Children.Add(Card(sources));

        StackPanel schedule = new() { Spacing = 12 };
        schedule.Children.Add(new TextBlock { Text = "Schedule & notifications", Style = AppStyle("StewardSectionTitleStyle") });
        schedule.Children.Add(PageCopy("Scheduled work checks inventory only. It never approves or installs updates and does not leave a resident service or tray process."));
        RadioButtons cadence = new() { Header = "Check cadence", SelectedIndex = scheduledSettings.Cadence switch { ScheduleCadence.Daily => 1, ScheduleCadence.Weekly => 2, _ => 0 } };
        cadence.Items.Add("Manual only"); cadence.Items.Add("Daily"); cadence.Items.Add("Weekly");
        AutomationProperties.SetName(cadence, "Scheduled check cadence");
        TimePicker startTime = new() { Header = "Local start time", Time = scheduledSettings.LocalStartTime.ToTimeSpan() };
        ComboBox weeklyDay = new() { Header = "Weekly day", IsEnabled = scheduledSettings.Cadence == ScheduleCadence.Weekly };
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) weeklyDay.Items.Add(day);
        weeklyDay.SelectedItem = scheduledSettings.WeeklyDay;
        cadence.SelectionChanged += (_, _) => weeklyDay.IsEnabled = cadence.SelectedIndex == 2;
        ToggleSwitch scheduleEnabled = new() { Header = "Enable scheduled checks", IsOn = scheduledSettings.IsEnabled, OffContent = "Not scheduled", OnContent = "Current-user task · no resident service" };
        ToggleSwitch notifications = new() { Header = "Windows notifications", IsOn = scheduledSettings.NotificationsEnabled, OffContent = "Off", OnContent = "Attention and repeated scan failures only" };
        Button save = new() { Content = "Save schedule" };
        AutomationProperties.SetName(save, "Save scheduled check settings");
        save.Click += async (_, _) =>
        {
            ScheduleCadence selectedCadence = cadence.SelectedIndex switch { 1 => ScheduleCadence.Daily, 2 => ScheduleCadence.Weekly, _ => ScheduleCadence.Manual };
            ScheduledCheckSettings requested = new ScheduledCheckSettings(selectedCadence, scheduleEnabled.IsOn, notifications.IsOn, weeklyDay.SelectedItem is DayOfWeek day ? day : DayOfWeek.Monday, TimeOnly.FromTimeSpan(startTime.Time)).Normalize();
            try { scheduledStatus = await DataOperationLease.RunAsync(async token => { await scheduleRegistration.ApplyAsync(requested, token); await scheduleSettingsStore.SaveScheduledCheckSettingsAsync(requested, token); return await scheduleSettingsStore.GetScheduledCheckStatusAsync(token); }, CancellationToken.None); scheduledSettings = requested; Render(); }
            catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
            catch (Exception) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Schedule was not changed", Content = "Windows could not create the current-user scan-only task. Your saved settings were left unchanged.", CloseButtonText = "Close" }.ShowAsync(); }
        };
        schedule.Children.Add(cadence); schedule.Children.Add(startTime); schedule.Children.Add(weeklyDay); schedule.Children.Add(scheduleEnabled); schedule.Children.Add(notifications); schedule.Children.Add(save);
        schedule.Children.Add(StewardVisuals.EvidenceRow("Task status", scheduledStatus.LastResult));
        schedule.Children.Add(StewardVisuals.EvidenceRow("Last completed", scheduledStatus.LastCompletedAt is null ? "Not yet" : scheduledStatus.LastCompletedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)));
        panel.Children.Add(Card(schedule));

        StackPanel decisions = new() { Spacing = 12 };
        AddDecisionsSection(decisions);
        panel.Children.Add(Card(decisions));

        StackPanel data = new() { Spacing = 10 };
        data.Children.Add(new TextBlock { Text = "Storage, privacy & reset", Style = AppStyle("StewardSectionTitleStyle") });
        data.Children.Add(PageCopy("Review app-owned storage, export inventory, clear individual data categories, or perform the separately protected factory-reset flow."));
        data.Children.Add(StewardVisuals.EvidenceRow("Scan records", $"{scanData.ItemCount:N0} items"));
        data.Children.Add(StewardVisuals.EvidenceRow("Downloaded files", $"{downloadCache.TotalBytes:N0} bytes"));
        StackPanel dataActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button storage = new() { Content = "View storage" }; storage.Click += (_, _) => { destination = "storage"; Render(); }; dataActions.Children.Add(storage);
        Button privacy = new() { Content = "Privacy & data controls" }; privacy.Click += (_, _) => { destination = "privacy"; Render(); }; dataActions.Children.Add(privacy);
        data.Children.Add(dataActions);
        panel.Children.Add(Card(data));
        Content = BuildShell(panel);
    }

    private static ComboBox FilterBox(string selected, params string[] values) => FilterBox(selected, (IReadOnlyCollection<string>)values);
    private static ComboBox FilterBox(string selected, IReadOnlyCollection<string> values) { ComboBox box = new(); foreach (string value in values.Distinct(StringComparer.OrdinalIgnoreCase)) box.Items.Add(value); box.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values.First(); return box; }
    private void RenderInventory(ListView list) => list.ItemsSource = viewModel.VisibleRows.Select(ApplicationListItem.From).ToArray();

    private async Task ChangeWinGetTrustedSourcesAsync(bool enabled)
    {
        bool approved = false;
        if (enabled)
        {
            ContentDialog consent = new()
            {
                XamlRoot = Content.XamlRoot,
                Title = "Enable WinGet and Chocolatey trusted sources?",
                Content = "WinGet and Chocolatey Community may receive installed-package identifiers. Chocolatey packages can run package-maintainer scripts during an update. Scanning remains read-only; an update is machine-scope only and requires separate review, confirmation, and elevation.",
                PrimaryButtonText = "I approve",
                CloseButtonText = "Keep local-only",
                DefaultButton = ContentDialogButton.Close
            };
            approved = await consent.ShowAsync() == ContentDialogResult.Primary;
            if (!approved) { Render(); return; }
        }
        try
        {
            await DataOperationLease.RunAsync(token => workflow.SaveConnectorStatesAsync(
                new Dictionary<string, ConnectorState>
                {
                    ["WinGet installed inventory"] = new(enabled, enabled),
                    ["WinGet package sources"] = new(enabled, enabled)
                }, token), CancellationToken.None);
            viewModel.SetConnectorEnabled("WinGet installed inventory", enabled);
            viewModel.SetConnectorEnabled("WinGet package sources", enabled);
        }
        catch (Exception)
        {
            LoadSavedInventory();
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "WinGet sources were not changed", Content = "PC Steward could not save the combined WinGet setting. The current saved connector state has been restored.", CloseButtonText = "Close" }.ShowAsync();
        }
        Render();
    }

    private async Task ChangeConnectorAsync(string connectorName, bool enabled)
    {
        bool approved = false;
        ConnectorOption connector = viewModel.Connectors.Single(option => option.Name == connectorName);
        if (connector.RequiresSourceDisclosure && enabled)
        {
            ContentDialog consent = new() { XamlRoot = Content.XamlRoot, Title = $"Enable {connectorName}?", Content = "Package sources may disclose installed-software identifiers. Scanning remains read-only and does not install or update software.", PrimaryButtonText = "I approve", CloseButtonText = "Keep local-only" };
            approved = await consent.ShowAsync() == ContentDialogResult.Primary;
            if (!approved) enabled = false;
        }
        try { await DataOperationLease.RunAsync(token => workflow.SaveConnectorStateAsync(connectorName, enabled, approved, token), CancellationToken.None); viewModel.SetConnectorEnabled(connectorName, enabled); Render(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); Render(); }
    }

    private async Task StartScanAsync()
    {
        scanCancellation = new CancellationTokenSource();
        Render();
        Progress<ScanProgress> progress = new(value => { viewModel.ReportProgress(value); RefreshOperationVisuals(); });
        // Providers and SQLite complete asynchronously.  Keep their continuations off the
        // WinUI dispatcher; Progress posts the small UI updates back to it explicitly.
        ScanResult? completed = null;
        try { completed = await DataOperationLease.RunAsync(token => workflow.ScanAsync(progress, token), scanCancellation.Token); viewModel.CompleteScan(completed, DateTimeOffset.UtcNow); LoadSavedInventory(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
        finally { scanCancellation?.Dispose(); scanCancellation = null; if (completed?.Succeeded == true) destination = "results"; Render(); }
    }

    private async Task CheckUpdateSourcesAsync()
    {
        string? detailStableProductKey = destination == "detail" ? detailRow?.StableProductKey : null;
        viewModel.BeginUpdateCheck(); Render();
        try
        {
            viewModel.LoadUpdateIntelligence(await DataOperationLease.RunAsync(token => workflow.RefreshUpdateIntelligenceAsync(viewModel.IsUpdateSourceEnabled, token), CancellationToken.None));
            activeDecisions = await DataOperationLease.RunReadAsync(token => workflow.GetActiveDecisionsAsync(token), CancellationToken.None);
            manualMappings = await DataOperationLease.RunReadAsync(token => workflow.GetManualMappingsAsync(token), CancellationToken.None);
            if (detailStableProductKey is not null)
            {
                detailRow = viewModel.AllRows.SingleOrDefault(row => row.StableProductKey == detailStableProductKey) ?? detailRow;
            }
        }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
        finally { viewModel.CompleteUpdateCheck(); }
        Render();
    }

    private void AddDataAndResetSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Data & reset", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = $"Scan data: {scanData.ItemCount} items, estimated payload {scanData.SizeBytes:N0} characters. Last cleared: {(scanData.LastClearedAt is null ? "never" : scanData.LastClearedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}. Clear scan data removes inventory, raw provider records, candidates and scan-run records; it preserves settings, connectors, schedules, update history, downloads, mappings, pins and ignores.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.", TextWrapping = TextWrapping.Wrap });
        StackPanel exportActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 }; Button exportCsv = new() { Content = "Export inventory CSV" }; exportCsv.Click += async (_, _) => await SaveExportAsync(false); exportActions.Children.Add(exportCsv); Button exportJson = new() { Content = "Export inventory JSON" }; exportJson.Click += async (_, _) => await SaveExportAsync(true); exportActions.Children.Add(exportJson); panel.Children.Add(exportActions);
        bool available = !viewModel.IsScanning && !isExecuting && !isClearing && !downloads.IsCleaning; Button clear = new() { Content = "Clear scan data", IsEnabled = available }; AutomationProperties.SetName(clear, "Clear scan data"); clear.Click += async (_, _) => await ConfirmAndClearScanDataAsync(); panel.Children.Add(clear);
        panel.Children.Add(new TextBlock { Text = $"Downloaded update files: {downloadCache.ArtifactCount} tracked item(s), {downloadCache.TotalBytes:N0} bytes total, {downloadCache.RecoverableBytes:N0} bytes recoverable. Last cleaned: {(downloadCache.LastCleanedAt is null ? "never" : downloadCache.LastCleanedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}. Clear downloaded files removes only inactive files under PC Steward's app-owned download cache; it preserves active update files, installed applications, scan data, source metadata, update history and all Windows, WinGet, Microsoft Store and other provider caches.", TextWrapping = TextWrapping.Wrap });
        Button clearDownloads = new() { Content = "Clear downloaded files", IsEnabled = available && downloadCache.RecoverableBytes > 0 }; AutomationProperties.SetName(clearDownloads, "Clear downloaded files"); clearDownloads.Click += async (_, _) => await ConfirmAndClearDownloadedFilesAsync("Clear downloaded files?"); panel.Children.Add(clearDownloads);
        if (downloadCache.RecoverableBytes > 0)
        {
            panel.Children.Add(new TextBlock { Text = $"Update completion cleanup: {downloadCache.RecoverableBytes:N0} bytes of app-owned downloaded files remain. You can recover this space without affecting the installed update.", TextWrapping = TextWrapping.Wrap });
            Button completionDelete = new() { Content = "Delete downloaded files", IsEnabled = available }; AutomationProperties.SetName(completionDelete, "Delete downloaded files after update completion"); completionDelete.Click += async (_, _) => await ConfirmAndClearDownloadedFilesAsync("Delete downloaded files after update completion?"); panel.Children.Add(completionDelete);
        }
        panel.Children.Add(new TextBlock { Text = available ? "Routine cleanup is available without administrator elevation." : "Cleanup is unavailable while a scan, update, or cleanup is active. Cancel or wait for it to finish.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Advanced data controls", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = "Each control removes only its stated PC Steward category. Provider caches, exports, installed applications and settings not named below are preserved.", TextWrapping = TextWrapping.Wrap });
        AddAdvancedClearButton(panel, "Clear source metadata", "Deletes cached package-source metadata and freshness records. Preserves inventory, connector consent, schedule, history, decisions and downloads.", () => workflow.ClearSourceMetadataAsync(CancellationToken.None), available);
        AddAdvancedClearButton(panel, "Clear logs", "Deletes only PC Steward file logs. It preserves inventory, source metadata, settings, decisions, history, downloads and provider caches.", async () => { FactoryResetResult result = await FactoryReset.ClearLogsAsync(CancellationToken.None); return new AdvancedDataClearResult("Logs", result.Succeeded, 0, result.Succeeded ? "PC Steward logs were cleared." : "Logs were only partly cleared: " + string.Join(", ", result.Residuals.Select(item => item.Reason))); }, available);
        AddAdvancedClearButton(panel, "Clear update history", "Deletes recorded update outcomes only. Preserves inventory, settings, decisions, source metadata and downloads.", () => workflow.ClearUpdateHistoryAsync(CancellationToken.None), available);
        AddAdvancedClearButton(panel, "Clear mappings and policies", "Deletes manual mappings, pins and ignores. Preserves inventory, settings, consent, source metadata, history and downloads.", () => workflow.ClearMappingsAndPoliciesAsync(CancellationToken.None), available);
        panel.Children.Add(new TextBlock { Text = "Factory reset PC Steward", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = "Danger zone — deletes all local PC Steward data, settings, consent, decisions, logs, downloads and the current-user scheduled check. It preserves PC Steward binaries, installed applications, provider caches and exports. PC Steward closes and verifies cleanup at the next launch before onboarding.", TextWrapping = TextWrapping.Wrap });
        Button factoryReset = new() { Content = "Factory reset PC Steward", IsEnabled = available }; AutomationProperties.SetName(factoryReset, "Factory reset PC Steward"); factoryReset.Click += async (_, _) => await ConfirmAndArmFactoryResetAsync(factoryReset); panel.Children.Add(factoryReset);
    }

    private void AddAdvancedClearButton(StackPanel panel, string label, string scope, Func<Task<AdvancedDataClearResult>> clear, bool available)
    {
        Button button = new() { Content = label, IsEnabled = available }; AutomationProperties.SetName(button, label);
        button.Click += async (_, _) =>
        {
            ContentDialog confirmation = new() { XamlRoot = Content.XamlRoot, Title = label + "?", Content = scope, PrimaryButtonText = label, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            isClearing = true; Render(); AdvancedDataClearResult result;
            try { result = await DataOperationLease.RunAsync(_ => clear(), CancellationToken.None); LoadSavedInventory(); }
            catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
            finally { isClearing = false; Render(); }
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? label + " complete" : label + " was not completed", Content = result.Message, CloseButtonText = "Close" }.ShowAsync();
        };
        panel.Children.Add(new TextBlock { Text = scope, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(button);
    }

    private async Task ConfirmAndArmFactoryResetAsync(Button invoker)
    {
        ContentDialog scope = new() { XamlRoot = Content.XamlRoot, Title = "Review factory reset scope", Content = "Deletes: all PC Steward scan data, source metadata, logs, update history, downloads, settings, consent, decisions and the current-user scheduled check.\n\nPreserves: PC Steward binaries, installed applications, provider caches and your exports.\n\nNo active work is allowed.", PrimaryButtonText = "Continue to final confirmation", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await scope.ShowAsync() != ContentDialogResult.Primary) { invoker.Focus(FocusState.Programmatic); return; }
        TextBox phrase = new() { Header = "Type RESET to continue", PlaceholderText = "RESET" };
        ContentDialog final = new() { XamlRoot = Content.XamlRoot, Title = "This cannot be undone", Content = phrase, PrimaryButtonText = "Factory reset PC Steward", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false };
        phrase.TextChanged += (_, _) => final.IsPrimaryButtonEnabled = string.Equals(phrase.Text, "RESET", StringComparison.Ordinal);
        if (await final.ShowAsync() != ContentDialogResult.Primary) { invoker.Focus(FocusState.Programmatic); return; }
        if (viewModel.IsScanning || isExecuting || isClearing || downloads.IsCleaning) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset unavailable", Content = "Cancel or wait for active work to finish, then try again.", CloseButtonText = "Close" }.ShowAsync(); return; }
        try { DataOperationLease.RunSynchronous(() => FactoryReset.Arm()); Close(); }
        catch (Exception exception)
        {
            if (FactoryReset.IsPending()) { Close(); return; }
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset was not started", Content = exception.Message, CloseButtonText = "Close" }.ShowAsync();
        }
    }

    private void AddDecisionsSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Update decisions", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = activeDecisions.Count == 0 && manualMappings.Count == 0 ? "No active pins, ignores or manual mappings. Decisions are optional, reversible, and do not enable updates." : "Active decisions and manual mappings are listed below. Remove one to reapply normal review rules on the next source check.", TextWrapping = TextWrapping.Wrap });
        foreach (PCSteward.Domain.StableDecision decision in activeDecisions)
        {
            Grid row = new() { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{decision.Kind}: {decision.Value}{(string.IsNullOrWhiteSpace(decision.Reason) ? string.Empty : $" — {decision.Reason}")}", TextWrapping = TextWrapping.Wrap });
            Button remove = new() { Content = "Remove decision" }; AutomationProperties.SetName(remove, $"Remove {decision.Kind} decision"); remove.Click += async (_, _) => { try { await DataOperationLease.RunAsync(token => workflow.ReverseDecisionAsync(decision.Id, token), CancellationToken.None); LoadSavedInventory(); Render(); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }; Grid.SetColumn(remove, 1); row.Children.Add(remove); panel.Children.Add(row);
        }
        foreach (PCSteward.Domain.ManualMapping mapping in manualMappings)
        {
            Grid row = new() { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"Manual mapping: {mapping.SourceId}:{mapping.PackageId}{(string.IsNullOrWhiteSpace(mapping.Reason) ? string.Empty : $" — {mapping.Reason}")}", TextWrapping = TextWrapping.Wrap });
            Button remove = new() { Content = "Remove mapping" }; AutomationProperties.SetName(remove, $"Remove manual mapping {mapping.PackageId}"); remove.Click += async (_, _) => { try { await DataOperationLease.RunAsync(token => workflow.DeleteManualMappingAsync(mapping.Id, token), CancellationToken.None); LoadSavedInventory(); Render(); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }; Grid.SetColumn(remove, 1); row.Children.Add(remove); panel.Children.Add(row);
        }
    }

    private async Task SaveExportAsync(bool json) { try { InventoryExport export = await DataOperationLease.RunReadAsync(token => workflow.CreateExportAsync(json, token), CancellationToken.None); FileSavePicker picker = new(); picker.SuggestedFileName = $"pc-steward-inventory-{export.ExportedAt:yyyyMMdd-HHmmss}"; picker.FileTypeChoices.Add(json ? "JSON inventory" : "CSV inventory", [export.FileExtension]); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); StorageFile? file = await picker.PickSaveFileAsync(); if (file is not null) await FileIO.WriteTextAsync(file, export.Content); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }

    private async Task ConfirmAndClearScanDataAsync()
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = "Clear scan data?", Content = "This removes current inventory, raw provider records, update candidates and scan-run records. It keeps settings, enabled connectors, schedules, update history, downloaded files, mappings, pins and ignores.", PrimaryButtonText = "Clear scan data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); ClearScanDataResult result;
        try { result = await DataOperationLease.RunAsync(token => workflow.ClearScanDataAsync(token), CancellationToken.None); if (result.Succeeded) { viewModel.ClearScanData(); LoadSavedInventory(); } }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        finally { isClearing = false; Render(); }
        ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? "Scan data cleared" : "Scan data was not cleared", Content = result.Message, CloseButtonText = "Close" }; await outcome.ShowAsync();
    }

    private async Task ConfirmAndClearDownloadedFilesAsync(string title)
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = title, Content = "This removes inactive PC Steward-owned update payloads and temporary extraction files only. It does not delete active update files, installed applications, scan data, source metadata, update history, or Windows, WinGet, Microsoft Store, or other provider caches.", PrimaryButtonText = "Delete downloaded files", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); DownloadCleanupResult result;
        try { result = await DataOperationLease.RunAsync(token => downloads.ClearInactiveAsync(token), CancellationToken.None); LoadSavedInventory(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        finally { isClearing = false; Render(); }
        ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? "Downloaded files cleared" : "Downloaded files were only partly cleared", Content = result.Message + (result.Residuals.Count == 0 ? string.Empty : " Remaining items: " + string.Join(", ", result.Residuals.Select(residual => $"{residual.RelativePath} ({residual.Reason})"))), CloseButtonText = "Close" }; await outcome.ShowAsync();
    }

    private void RenderProductDetailPage()
    {
        if (detailRow is null) { destination = detailReturnDestination; Render(); return; }

        InventoryRow row = detailRow;
        UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
        bool cachedCurrent = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.Current) && detail?.Freshness.Contains("cached", StringComparison.OrdinalIgnoreCase) == true;
        string status = row.Status switch
        {
            nameof(PCSteward.Domain.UpdateIntelligenceStatus.Current) when cachedCurrent => "Managed · current in cached evidence",
            nameof(PCSteward.Domain.UpdateIntelligenceStatus.Current) => "Managed · up to date",
            nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate) => "Managed · update available",
            nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) => "Repository match · review required",
            nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable) => "Repository match · update evidence unavailable",
            _ => "Unmanaged · local inventory only"
        };

        StackPanel panel = CreatePagePanel(1000);
        Grid heading = new() { ColumnSpacing = 16 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel title = new() { Spacing = 4 };
        title.Children.Add(new TextBlock { Text = "Application detail", Style = AppStyle("StewardCaptionStyle") });
        title.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardPageTitleStyle") });
        StewardStatus statusPresentation = StewardStatus.From(row.Status);
        title.Children.Add(StewardVisuals.Badge(status, statusPresentation.Glyph, statusPresentation.Tone, $"{row.Name}: {status}"));
        heading.Children.Add(title);
        Button back = new() { Content = "Back" }; AutomationProperties.SetName(back, "Back to previous application view"); back.Click += (_, _) => { destination = detailReturnDestination; detailRow = null; Render(); }; Grid.SetColumn(back, 1); heading.Children.Add(back);
        panel.Children.Add(heading);

        Grid comparison = new() { ColumnSpacing = 12, RowSpacing = 12 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel installed = new() { Spacing = 6 };
        installed.Children.Add(new TextBlock { Text = "Installed", Style = AppStyle("StewardSectionTitleStyle") });
        installed.Children.Add(StewardVisuals.EvidenceRow("Version", row.Version));
        installed.Children.Add(StewardVisuals.EvidenceRow("Publisher", row.Publisher));
        installed.Children.Add(StewardVisuals.EvidenceRow("Location", $"{StewardPresentation.Scope(row.Scope)} · {StewardPresentation.Provider(row.Provider)}"));
        comparison.Children.Add(Card(installed));
        StackPanel evidence = new() { Spacing = 6 };
        evidence.Children.Add(new TextBlock { Text = "Repository evidence", Style = AppStyle("StewardSectionTitleStyle") });
        evidence.Children.Add(StewardVisuals.EvidenceRow("Package ID", detail?.PackageId ?? "No managed package identity"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Repository", detail?.SourceId ?? "No enabled source match"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Available", detail?.CandidateVersion ?? "Not established"));
        evidence.Children.Add(PageCopy(detail?.Freshness ?? "Local-only — no source check has been run."));
        Grid.SetColumn(evidence, 1); comparison.Children.Add(Card(evidence));
        panel.Children.Add(comparison);

        StackPanel why = new() { Spacing = 6 };
        why.Children.Add(new TextBlock { Text = "Why this appears", Style = AppStyle("StewardSectionTitleStyle") });
        why.Children.Add(PageCopy(detail?.Explanation ?? "This application is present in the local inventory. No concrete repository package identity is currently available."));
        panel.Children.Add(Card(why));

        StackPanel capability = new() { Spacing = 6 };
        capability.Children.Add(new TextBlock { Text = "What PC Steward can do", Style = AppStyle("StewardSectionTitleStyle") });
        capability.Children.Add(PageCopy(detail?.ActionState ?? "No automatic action is available. You can record a decision or review available source evidence."));
        capability.Children.Add(PageCopy("What is preserved: this page does not install, remove, or change the application."));
        panel.Children.Add(Card(capability));

        PCSteward.Application.AdministratorRequirementPresentation administratorRequirement =
            PCSteward.Application.AdministratorRequirementPresenter.BeforeReview();
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate))
        {
            panel.Children.Add(StewardVisuals.InformationBanner(administratorRequirement.Label, administratorRequirement.Explanation, "attention"));
        }

        TextBox reason = new() { Header = "Optional decision reason", PlaceholderText = "Why are you pinning, ignoring or mapping this product?", MaxLength = 512 };
        CheckBox ignoreUntilEnabled = new() { Content = "Ignore until a selected date" };
        DatePicker ignoreUntil = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) };
        ignoreUntilEnabled.Checked += (_, _) => ignoreUntil.IsEnabled = true;
        ignoreUntilEnabled.Unchecked += (_, _) => ignoreUntil.IsEnabled = false;
        TextBlock outcome = new() { TextWrapping = TextWrapping.Wrap };
        bool sourceEvidenceUnavailable = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable);
        StackPanel decisions = new() { Spacing = 8 };
        decisions.Children.Add(new TextBlock { Text = sourceEvidenceUnavailable ? "Available next step" : "Decisions", Style = AppStyle("StewardSectionTitleStyle") });
        if (sourceEvidenceUnavailable)
        {
            decisions.Children.Add(PageCopy("Retry the approved repository evidence. PC Steward is not asking you to approve, map, pin, or ignore this item while update availability is missing."));
        }
        else
        {
            decisions.Children.Add(reason);
            decisions.Children.Add(ignoreUntilEnabled);
            decisions.Children.Add(ignoreUntil);
        }
        ComboBox? candidatePicker = null;
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0 && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy("Choose only the exact approved package identity you recognise as this installed application. The source package is revalidated before the reversible mapping is saved; no update starts."));
            candidatePicker = new ComboBox
            {
                Header = "Approved package candidates",
                ItemsSource = detail.ReviewCandidates.ToArray(),
                DisplayMemberPath = nameof(ReviewCandidateChoice.DisplayLabel),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(candidatePicker, $"Approved package candidates for {row.Name}");
            decisions.Children.Add(candidatePicker);
        }
        StackPanel decisionActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisionActions.Children.Add(CreateSourceEvidenceRefreshButton(row));
        }
        if (candidatePicker is not null)
        {
            Button confirmSelected = StewardVisuals.PrimaryButton("Confirm selected identity", $"Confirm the selected approved package identity for {row.Name}");
            confirmSelected.Click += async (_, _) =>
            {
                if (candidatePicker.SelectedItem is not ReviewCandidateChoice candidate) { outcome.Text = "Select an approved package candidate first."; return; }
                string decisionReason = reason.Text;
                PCSteward.Domain.ManualMappingValidation validation;
                try { validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, candidate.SourceId, candidate.PackageId, viewModel.IsUpdateSourceEnabled, decisionReason, token), CancellationToken.None); }
                catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
                outcome.Text = validation.Explanation;
                if (!validation.IsValid) return;
                LoadSavedInventory();
                detailRow = viewModel.AllRows.SingleOrDefault(item => item.StableProductKey == row.StableProductKey) ?? row;
                Render();
            };
            decisionActions.Children.Add(confirmSelected);
        }
        if (!sourceEvidenceUnavailable)
        {
            Button pin = new() { Content = "Pin current version" }; pin.Click += async (_, _) => { string decisionReason = reason.Text; try { await DataOperationLease.RunAsync(token => workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, decisionReason, token), CancellationToken.None); LoadSavedInventory(); Render(); } catch (InvalidOperationException) { await ShowUpdateOutcomeAsync("Action unavailable", "Factory reset recovery or another data operation is active."); } }; decisionActions.Children.Add(pin);
            Button ignore = new() { Content = "Ignore product" }; ignore.Click += async (_, _) => { string decisionReason = reason.Text; DateTimeOffset? ignoredUntil = ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null; try { await DataOperationLease.RunAsync(token => workflow.IgnoreAsync(row.StableProductKey, ignoredUntil, decisionReason, token), CancellationToken.None); LoadSavedInventory(); Render(); } catch (InvalidOperationException) { await ShowUpdateOutcomeAsync("Action unavailable", "Factory reset recovery or another data operation is active."); } }; decisionActions.Children.Add(ignore);
        }
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy($"Confirm this only if you recognise {detail.PackageId} from {detail.SourceId} as this installed application. The reversible mapping does not start an update."));
            Button confirmPackage = new() { Content = "Confirm package identity" };
            AutomationProperties.SetName(confirmPackage, $"Confirm {detail.PackageId} is this installed application");
            confirmPackage.Click += async (_, _) =>
            {
                string decisionReason = reason.Text;
                PCSteward.Domain.ManualMappingValidation validation;
                try { validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, detail.SourceId, detail.PackageId, true, decisionReason, token), CancellationToken.None); }
                catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
                outcome.Text = validation.Explanation;
                if (!validation.IsValid) return;
                LoadSavedInventory();
                detailRow = viewModel.AllRows.SingleOrDefault(candidate => candidate.StableProductKey == row.StableProductKey) ?? row;
                Render();
            };
            decisionActions.Children.Add(confirmPackage);
        }
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate) && viewModel.IsUpdateSourceEnabled && !isExecuting)
        {
            Button review = StewardVisuals.PrimaryButton("Review and update", $"Review and update this verified package. {administratorRequirement.Label}");
            AutomationProperties.SetName(review, $"Review and update this verified package. {administratorRequirement.Label}");
            AutomationProperties.SetHelpText(review, administratorRequirement.Explanation);
            review.Click += async (_, _) => await ReviewAndExecuteAsync(row);
            decisionActions.Children.Add(review);
        }
        decisions.Children.Add(decisionActions);
        decisions.Children.Add(outcome);
        panel.Children.Add(Card(decisions));

        if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId))
        {
            StackPanel reviewedRoute = new() { Spacing = 6 };
            reviewedRoute.Children.Add(new TextBlock { Text = "Chocolatey reviewed update", Style = AppStyle("StewardSectionTitleStyle") });
            reviewedRoute.Children.Add(PageCopy("PC Steward does not allow this Chocolatey update route yet. You can read what it would disclose before any confirmation; it cannot start an update."));
            Button openReviewedRoute = ReviewedRouteSurfaces.ChocolateyReviewEntryButton();
            openReviewedRoute.Click += async (_, _) => await ShowChocolateyReviewedRouteAsync();
            reviewedRoute.Children.Add(openReviewedRoute);
            panel.Children.Add(Card(reviewedRoute));
        }

        ComboBox mappingSource = FilterBox(detail?.SourceId ?? "winget", "winget", "msstore");
        TextBox mappingPackage = new() { Header = "Exact approved source package ID", PlaceholderText = detail?.PackageId ?? "For example: Contoso.Widget" };
        StackPanel technical = new() { Spacing = 8 };
        technical.Children.Add(PageCopy($"Installed instances:\n{string.Join("\n", row.Instances.Select(instance => $"{instance.Provider}: {instance.ProviderIdentity}; {instance.Version ?? "unknown version"}; {instance.Scope}; {instance.Architecture}"))}"));
        technical.Children.Add(PageCopy(detailTimeline.Count == 0 ? "Timeline: no product-specific events are recorded." : $"Timeline:\n{string.Join("\n", detailTimeline.Select(entry => $"{entry.OccurredAt.ToLocalTime():g} — {entry.Summary}"))}"));
        technical.Children.Add(PageCopy(detail?.SourceCandidates.Count > 0 ? $"Source candidates:\n{string.Join("\n", detail.SourceCandidates)}" : "Source candidates: none available."));
        technical.Children.Add(mappingSource);
        technical.Children.Add(mappingPackage);
        Button map = new() { Content = "Map exact approved package", IsEnabled = viewModel.IsUpdateSourceEnabled && !sourceEvidenceUnavailable }; AutomationProperties.SetName(map, "Create manual mapping for an exact approved source package"); map.Click += async (_, _) => { string sourceId = mappingSource.SelectedItem?.ToString() ?? "winget"; string packageId = string.IsNullOrWhiteSpace(mappingPackage.Text) ? detail?.PackageId ?? string.Empty : mappingPackage.Text; string decisionReason = reason.Text; try { PCSteward.Domain.ManualMappingValidation validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, sourceId, packageId, viewModel.IsUpdateSourceEnabled, decisionReason, token), CancellationToken.None); outcome.Text = validation.Explanation; if (validation.IsValid) { LoadSavedInventory(); detailRow = viewModel.AllRows.SingleOrDefault(candidate => candidate.StableProductKey == row.StableProductKey) ?? row; Render(); } } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }; technical.Children.Add(map);
        technical.Children.Add(PageCopy($"Bounded raw diagnostic records: {detailRaw.Count}."));
        Expander disclosure = new() { Header = "Technical details", Content = technical };
        panel.Children.Add(disclosure);
        Content = BuildShell(panel);
    }

    /// <summary>
    /// Admission obligation 5: the runtime-limit Chocolatey review. The split-token state is probed afresh for this review. No
    /// reviewed operation can exist while the route is not admitted, so Confirm stays disabled (<c>IsConfirmEnabled</c>); any
    /// confirmation goes only through the view model's <c>ReviewedUpdateReview.Confirm</c> recheck and nothing is dispatched.
    /// </summary>
    private async Task ShowChocolateyReviewedRouteAsync()
    {
        if (windowClosed) return;
        ChocolateyReviewedRouteReviewViewModel review = ChocolateyReviewedRouteReviewViewModel.Open(null, null, probeReviewedRouteSplitToken);
        ContentDialog dialog = ReviewedRouteSurfaces.ChocolateyReviewDialog(Content.XamlRoot, review);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || windowClosed) return;
        // No dispatch path is composed for this route, so nothing can start either way.
        _ = review.TryConfirm();
        await ShowUpdateOutcomeAsync("Update not started", ChocolateyReviewedRouteReviewViewModel.NotStartedText);
    }

    private async Task ShowDetailsAsync(InventoryRow row)
    {
        detailRow = row;
        detailReturnDestination = destination;
        try { detailRaw = await DataOperationLease.RunReadAsync(token => workflow.GetRawDiagnosticsAsync(row.ProductId, token), CancellationToken.None); detailTimeline = await DataOperationLease.RunReadAsync(token => workflow.GetTimelineAsync(row.StableProductKey, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        destination = "detail";
        Render();
    }

    private void StartUpdateProgressPolling()
    {
        StopUpdateProgressPolling();
        if (windowClosed || !isExecuting || updateProgressMailbox is null || updateProgressPresentation is null || updateProgressPresentation.IsFinal || updateProgressPresentation.IsUnavailable) return;
        updateProgressTarget = operationStatusText;
        long epoch = updateProgressEpoch;
        UpdateProgressMailbox mailbox = updateProgressMailbox;
        UpdateProgressPresentation presentation = updateProgressPresentation;
        updateProgressTick = (_, _) => PollUpdateProgress(epoch, mailbox, presentation);
        updateProgressTimer.Tick += updateProgressTick;
        updateProgressTimer.Start();
    }

    private void PollUpdateProgress(long epoch, UpdateProgressMailbox mailbox, UpdateProgressPresentation presentation)
    {
        if (windowClosed || !isExecuting || epoch != updateProgressEpoch || !ReferenceEquals(mailbox, updateProgressMailbox) || !ReferenceEquals(updateProgressTarget, operationStatusText)) return;
        if (mailbox.HasDegraded) presentation.MarkUnavailable();
        if (mailbox.TryTake(out UpdateProgressFrame? frame) && frame is not null) presentation.Consume(frame);
        updateProgressLabel = presentation.Label;
        RefreshOperationVisuals();
        if (presentation.IsFinal || presentation.IsUnavailable) StopUpdateProgressPolling();
    }

    private void StopUpdateProgressPolling()
    {
        ++updateProgressEpoch;
        updateProgressTimer.Stop();
        if (updateProgressTick is not null) updateProgressTimer.Tick -= updateProgressTick;
        updateProgressTick = null;
        updateProgressTarget = null;
    }

    private async Task ReviewAndExecuteAsync(InventoryRow row)
    {
        if (HasActiveOperation || windowClosed) return;
        UpdateReviewPreparation preparation;
        try { preparation = await DataOperationLease.RunAsync(token => updates.BeginReviewAsync(row.StableProductKey, token), CancellationToken.None); }
        catch (InvalidOperationException) { if (!windowClosed) await ShowDataOperationUnavailableAsync(); return; }
        catch (Exception)
        {
            if (!windowClosed) await ShowUpdateOutcomeAsync("Update not available", "PC Steward could not prepare this update. No update was started.");
            return;
        }
        if (windowClosed) return;
        if (preparation.Review is null)
        {
            await ShowUpdateOutcomeAsync("Update not available", preparation.Message);
            return;
        }
        PCSteward.Domain.UpdateJobSelection selection = preparation.Review.Selection;
        const string verification = "\n\nBy choosing Install approved update, you verify that the exact WinGet source, package ID, installed version and target version shown here are the application and update you intend.";
        PCSteward.Application.AdministratorRequirementPresentation administratorRequirement =
            PCSteward.Application.AdministratorRequirementPresenter.ForSelection(selection);
        string runningWarning = preparation.RunningApplication.AppearsToBeRunning
            ? $"\n\nTarget-specific warning: {selection.PackageId} appears to be running (detected process: {string.Join(", ", preparation.RunningApplication.ProcessNames)}). PC Steward will not close it. Close the application and cancel this review, or explicitly choose Install approved update anyway."
            : $"\n\nNo bounded package-derived process match was detected for {selection.PackageId}. Close the target application first if you know it is running; PC Steward will not close applications.";
        runningWarning += "\n\n" + preparation.RunningApplication.CoverageDisclosure;
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Review approved update",
            Content = $"Package: {selection.PackageId}\nInstalled version: {selection.InstalledVersion}\nTarget version: {selection.CandidateVersion}\nSource: {selection.Source}\nScope: {selection.Scope}\nArchitecture: {selection.Architecture}\n\n{administratorRequirement.Label}\n{administratorRequirement.Explanation}{runningWarning}{verification}\n\nProvider completion is not marked successful until the controlled post-update inventory verifies the target version.",
            PrimaryButtonText = preparation.RunningApplication.AppearsToBeRunning ? "Install approved update anyway" : "Install approved update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary || windowClosed) return;

        isExecuting = true;
        updateProgressMailbox = preparation.Progress;
        updateProgressPresentation = new(preparation.Review.JobId, preparation.Review.CorrelationId, selection.Scope);
        updateProgressLabel = preparation.Progress is null ? "Progress unavailable" : "Revalidating reviewed update";
        ReviewedUpdateExecutionResult result;
        try
        {
            Render();
            StartUpdateProgressPolling();
            result = await DataOperationLease.RunAsync(token => updates.ConfirmAndRunAsync(preparation.Review, preparation.RunningApplication.AppearsToBeRunning, token, preparation.Progress), CancellationToken.None);
        }
        catch (Exception) { result = new(true, "The reviewed update did not produce a recorded outcome. Execution or verification may have been interrupted. No verified success was recorded; refresh inventory before deciding what to do next.", null); }
        finally { StopUpdateProgressPolling(); isExecuting = false; updateProgressMailbox = null; updateProgressPresentation = null; if (!windowClosed) LoadSavedInventory(); }
        if (windowClosed) return;
        // The retained terminal may still be rate-limited. The recorded batch is authoritative;
        // do not fabricate a progress frame or sequence to bypass consumer validation.
        await ShowUpdateOutcomeAsync(GetUpdateOutcomeTitle(result), GetUpdateOutcomeMessage(result));
        if (!windowClosed) Render();
    }

    private static string GetUpdateOutcomeTitle(ReviewedUpdateExecutionResult result) =>
        result.Batch?.FinalEntries.SingleOrDefault()?.Status switch
        {
            PCSteward.Application.UpdateHistoryStatus.Success => "Update completed",
            PCSteward.Application.UpdateHistoryStatus.RestartRequired => "Restart required",
            null when !result.Started => "Update not started",
            null => "Outcome unavailable",
            _ => "Update failed"
        };

    private static string GetUpdateOutcomeMessage(ReviewedUpdateExecutionResult result) =>
        result.Batch?.FinalEntries.SingleOrDefault() is
        {
            Status: PCSteward.Application.UpdateHistoryStatus.ProductFailed,
            Diagnostic: "elevated-helper-fixed-installed-path-invalid"
        }
            ? "The update was blocked before it could run. This standalone PC Steward beta is not installed alongside its trusted elevated helper in the protected PC Steward location. No application was changed."
            : result.Message;

    private Task<ContentDialogResult> ShowUpdateOutcomeAsync(string title, string message) => new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync().AsTask();
}
