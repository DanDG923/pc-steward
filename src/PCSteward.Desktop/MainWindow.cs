using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private readonly ShellNavigation navigation;
    private Windows.UI.ViewManagement.AccessibilitySettings? accessibilitySettings;
    private Grid? windowRoot;
    private Button? titleBarBack;
    private NavigationView? navigationView;
    private ScrollViewer? pageHost;
    private ShellPage? renderedPage;
    private Button? operationCancel;
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
        navigation = new ShellNavigation(showPostResetOnboarding ? ShellPage.Onboarding : ShellPage.Home);
        navigation.PropertyChanged += (_, _) => SyncNavigationChrome();
        BuildWindowChrome();
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

    /// <summary>
    /// Refreshes the current page only. The window chrome, navigation pane and operation band are created once and persist,
    /// so focus in them survives; the page keeps its scroll position and, where it still exists, the focused control.
    /// </summary>
    private void Render()
    {
        if (startupStateLoadFailed) { RenderStartupStateLoadFailure(); return; }
        if (navigation.Current == ShellPage.Onboarding) { ShowBarePage(BuildOnboardingPage()); return; }
        EnsureShell();
        ShowCurrentPage(preserveViewState: true);
    }

    /// <summary>FR-UI-02 (audit P-H1/P-H2): opens a page through the back stack. Re-opening the current page refreshes it.</summary>
    private void NavigateTo(ShellPage page)
    {
        if (!navigation.Navigate(page)) { Render(); return; }
        if (page == ShellPage.Onboarding) { Render(); return; }
        EnsureShell();
        ShowCurrentPage(preserveViewState: false);
    }

    private void GoBack()
    {
        if (!navigation.GoBack()) return;
        EnsureShell();
        ShowCurrentPage(preserveViewState: false);
    }

    private StackPanel BuildPage(ShellPage page) => page switch
    {
        ShellPage.Home => BuildHomePage(),
        ShellPage.Updates => BuildUpdatesPage(),
        ShellPage.InstalledApps => BuildInstalledAppsPage(),
        ShellPage.Activity => BuildActivityPage(),
        ShellPage.Decisions => BuildDecisionsPage(),
        ShellPage.Settings => BuildSettingsPage(),
        ShellPage.About => BuildAboutPage(),
        ShellPage.Scan => BuildScanPage(),
        ShellPage.Results => BuildResultsPage(),
        ShellPage.Review => BuildReviewPage(),
        ShellPage.RepositoryApps => BuildRepositoryAppsPage(),
        ShellPage.Detail => BuildProductDetailPage(),
        ShellPage.Storage => BuildStoragePage(),
        ShellPage.Privacy => BuildPrivacyPage(),
        ShellPage.Onboarding => BuildOnboardingPage(),
        _ => BuildHomePage()
    };

    private void RenderStartupStateLoadFailure()
    {
        StackPanel panel = CreatePagePanel(760);
        panel.Children.Add(new TextBlock { Text = "PC Steward could not open local state", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("A local data operation is still active or reset recovery is in progress. No scan or state-changing action was started. Close PC Steward and try again after the other operation finishes."));
        ShowBarePage(panel);
    }

    private static StackPanel CreatePagePanel(double maxWidth = 1040)
    {
        StackPanel panel = new() { Spacing = StewardSpacing.Large, Padding = new Thickness(StewardSpacing.XLarge), MaxWidth = maxWidth };
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
        if (operationCancel is not null) operationCancel.Visibility = viewModel.IsScanning || scanCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        if (scanPageStatusText is not null) scanPageStatusText.Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}";
    }

    private StackPanel BuildOnboardingPage()
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
            navigation.Reset(ShellPage.Scan); Render();
        };
        panel.Children.Add(continueButton);
        return panel;
    }

    private static StackPanel BuildAboutPage()
    {
        StackPanel panel = CreatePagePanel(820);
        panel.Children.Add(StewardVisuals.PageHeader("About", "PC Steward is a local-first Windows 11 application inventory and evidence-review tool."));
        panel.Children.Add(StewardVisuals.InformationBanner("Private and user-directed", "No account, cloud sync, telemetry, resident background service, or automatic updates."));
        StackPanel scope = new() { Spacing = 8 };
        scope.Children.Add(new TextBlock { Text = "Updates and administrator permission", Style = AppStyle("StewardSectionTitleStyle") });
        scope.Children.Add(PageCopy(ElevationStory.Summary));
        scope.Children.Add(PageCopy(ElevationStory.HelperRequirement));
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
        return panel;
    }

    private StackPanel BuildHomePage()
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
        panel.Children.Add(StewardVisuals.PageHeader("Home", description));

        if (viewModel.LastScanAt is null && viewModel.AllRows.Count == 0)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "Start a read-only local application inventory");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("\uE721", "See what is installed", "The first scan reads Windows application records on this PC. It does not request administrator access, connect to package sources, or change software.", start));
            panel.Children.Add(StewardVisuals.InformationBanner("Private by default", "Inventory and decisions stay on this PC. Package sources remain off until you approve their disclosure."));
            return panel;
        }

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        Button primary = StewardVisuals.PrimaryButton(ready > 0 ? $"Review {ready} update{(ready == 1 ? string.Empty : "s")}" : review > 0 ? $"Review {review} decision{(review == 1 ? string.Empty : "s")}" : "Open applications", ready > 0 ? "Review verified update evidence" : review > 0 ? "Review application decisions" : "Open installed applications");
        primary.IsEnabled = !isExecuting;
        primary.Click += (_, _) => NavigateTo(ready > 0 ? ShellPage.Updates : review > 0 ? ShellPage.Review : ShellPage.InstalledApps);
        Button scan = new() { Content = viewModel.IsScanning ? "Scan in progress" : "Scan again", IsEnabled = !HasActiveOperation };
        AutomationProperties.SetName(scan, viewModel.IsScanning ? "Inventory scan in progress" : "Start another local inventory scan");
        scan.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
        Button results = new() { Content = "View scan results" };
        AutomationProperties.SetName(results, "View scan results");
        results.Click += (_, _) => NavigateTo(ShellPage.Results);
        actions.Children.Add(primary);
        actions.Children.Add(scan);
        actions.Children.Add(results);

        Grid hero = new() { ColumnSpacing = StewardSpacing.XLarge };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.8, GridUnitType.Star) });
        StackPanel heroCopy = new() { Spacing = StewardSpacing.Medium };
        heroCopy.Children.Add(new TextBlock { Text = ready > 0 ? "Updates wait for you" : review > 0 ? "Decisions wait for you" : "No action is waiting", Style = AppStyle("StewardDisplayStyle") });
        heroCopy.Children.Add(PageCopy($"PC Steward found {viewModel.AllRows.Count:N0} installed application{(viewModel.AllRows.Count == 1 ? string.Empty : "s")}. It keeps observation, source evidence, your decisions, approval, and verification as separate stages."));
        heroCopy.Children.Add(actions);
        hero.Children.Add(heroCopy);
        StackPanel evidence = new() { Spacing = StewardSpacing.XSmall };
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
        (int count, string detail, string status)[] values =
        [
            (ready, "Fresh, exact package evidence", "VerifiedUpdate"),
            (review, "Review-only; no automatic action", "ReviewRequired"),
            (current, "Based on the latest source evidence", "Current"),
            (unmanaged, "Visible without being labelled unsafe", "Unmanaged")
        ];
        for (int index = 0; index < values.Length; index++)
        {
            Border metric = StewardVisuals.MetricCard(values[index].count, values[index].detail, values[index].status);
            Grid.SetColumn(metric, index % 2); Grid.SetRow(metric, index / 2); metrics.Children.Add(metric);
        }
        panel.Children.Add(metrics);
        return panel;
    }

    private StackPanel BuildInstalledAppsPage()
    {
        StackPanel panel = CreatePagePanel();
        Button scan = new() { Content = "Scan again", IsEnabled = !HasActiveOperation };
        AutomationProperties.SetName(scan, "Start another local inventory scan");
        scan.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
        Button repositoryApps = new() { Content = "Repository apps" };
        AutomationProperties.SetName(repositoryApps, "Repository apps");
        repositoryApps.Click += (_, _) => NavigateTo(ShellPage.RepositoryApps);
        StackPanel headerActions = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        headerActions.Children.Add(repositoryApps);
        headerActions.Children.Add(scan);
        panel.Children.Add(StewardVisuals.PageHeader("Installed apps", "Search and inspect the local inventory. Filtering never changes saved data.", null, headerActions));
        panel.Children.Add(StewardVisuals.InformationBanner("Inventory freshness", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "information"));
        TextBox search = new() { Header = "Search", PlaceholderText = "Name or publisher", Text = viewModel.SearchText };
        AutomationProperties.SetName(search, "Search applications");
        // Filters show the labels the rows show (StatusVocabulary, StewardPresentation), never raw enum or provider values.
        (string Raw, string Label)[] sourceOptions = LabelledFilterOptions(viewModel.AllRows.Select(row => row.Provider), StewardPresentation.Provider);
        (string Raw, string Label)[] scopeOptions = LabelledFilterOptions(viewModel.AllRows.Select(row => row.Scope), StewardPresentation.Scope);
        ComboBox status = FilterBox(StatusVocabulary.FilterLabelForKey(viewModel.StatusFilter), StatusVocabulary.FilterLabels);
        ComboBox publisher = FilterBox(viewModel.PublisherFilter, ["All publishers", .. viewModel.AvailablePublishers]);
        ComboBox source = FilterBox(FilterLabelFor(viewModel.SourceFilter, sourceOptions), ["All sources", .. sourceOptions.Select(option => option.Label)]);
        ComboBox scope = FilterBox(FilterLabelFor(viewModel.ScopeFilter, scopeOptions), ["All scopes", .. scopeOptions.Select(option => option.Label)]);
        ComboBox sort = FilterBox(viewModel.SortField, "Name", "Source", "Publisher");
        status.Header = "Status"; publisher.Header = "Publisher"; source.Header = "Source"; scope.Header = "Scope"; sort.Header = "Sort by";
        AutomationProperties.SetName(status, "Filter applications by status"); AutomationProperties.SetName(publisher, "Filter applications by publisher"); AutomationProperties.SetName(source, "Filter applications by source"); AutomationProperties.SetName(scope, "Filter applications by scope"); AutomationProperties.SetName(sort, "Sort applications");
        StackPanel filterContent = new() { Spacing = StewardSpacing.Medium };
        filterContent.Children.Add(search);
        Grid filters = new() { ColumnSpacing = StewardSpacing.Medium, RowSpacing = StewardSpacing.Medium };
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
            viewModel.SetFilters(search.Text, StatusVocabulary.FilterKeyForLabel(status.SelectedItem?.ToString()), publisher.SelectedItem?.ToString(), FilterRawFor(source.SelectedItem?.ToString(), sourceOptions, "All sources"), FilterRawFor(scope.SelectedItem?.ToString(), scopeOptions, "All scopes"), sort.SelectedItem?.ToString());
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
        return panel;
    }

    private static (string Raw, string Label)[] LabelledFilterOptions(IEnumerable<string> rawValues, Func<string, string> label) => rawValues
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(raw => (Raw: raw, Label: label(raw)))
        .GroupBy(option => option.Label, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static string FilterLabelFor(string raw, (string Raw, string Label)[] options) =>
        options.FirstOrDefault(option => string.Equals(option.Raw, raw, StringComparison.OrdinalIgnoreCase)).Label ?? raw;

    private static string FilterRawFor(string? label, (string Raw, string Label)[] options, string all) =>
        options.FirstOrDefault(option => string.Equals(option.Label, label, StringComparison.Ordinal)).Raw ?? all;

    private StackPanel BuildRepositoryAppsPage()
    {
        StackPanel panel = CreatePagePanel(960);
        IReadOnlyList<InventoryRow> managedRows = viewModel.RepositoryManagedRows;
        int upToDate = managedRows.Count(row => row.Status == "Current");
        int updatesAvailable = managedRows.Count(row => row.Status == "VerifiedUpdate");
        int reviewRequired = managedRows.Count(row => row.Status == "ReviewRequired");
        int unavailable = managedRows.Count(row => row.Status == "FailedUnavailable");
        panel.Children.Add(StewardVisuals.PageHeader("Repository apps", "Installed applications with a concrete identity from an enabled, approved package source."));
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
            settings.Click += (_, _) => NavigateTo(ShellPage.Settings);
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
                StackPanel current = new() { Spacing = StewardSpacing.XSmall };
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
        return panel;
    }

    private StackPanel BuildResultsPage()
    {
        StackPanel panel = CreatePagePanel();
        int actionable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString());
        int decisions = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString());
        int unavailable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable.ToString());
        int unmanaged = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged.ToString());
        panel.Children.Add(StewardVisuals.PageHeader("Inventory results", "Verified opportunities, decisions, unavailable update evidence, and inventoried-only software stay visibly separate. No update starts from this summary.", "Completed inventory"));
        panel.Children.Add(StewardVisuals.InformationBanner("Saved snapshot", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "success"));
        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        foreach ((ResultsFilter option, string label) in new[]
        {
            (ResultsFilter.All, $"All results {actionable + decisions + unavailable + unmanaged}"),
            (ResultsFilter.Actionable, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate).Label} {actionable}"),
            (ResultsFilter.NeedsDecision, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired).Label} {decisions}"),
            (ResultsFilter.Unavailable, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable).Label} {unavailable}"),
            (ResultsFilter.Unmanaged, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged).Label} {unmanaged}")
        })
        {
            // The selected filter stays focusable (accent style plus "Selected" item status) so a refresh can restore focus to it.
            Button filter = resultsFilter == option ? StewardVisuals.PrimaryButton(label, $"Show {label} scan results") : new Button { Content = label };
            AutomationProperties.SetName(filter, $"Show {label} scan results");
            if (resultsFilter == option) AutomationProperties.SetItemStatus(filter, "Selected");
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
            StackPanel finding = new() { Spacing = StewardSpacing.XSmall };
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
            Button storage = new() { Content = $"Review {downloadCache.RecoverableBytes:N0} bytes of app-owned downloads" }; storage.Click += (_, _) => NavigateTo(ShellPage.Storage); panel.Children.Add(storage);
        }
        Button review = new() { Content = "Review available actions" }; review.Click += (_, _) => NavigateTo(ShellPage.Review); panel.Children.Add(review);
        return panel;
    }

    private StackPanel BuildReviewPage()
    {
        StackPanel panel = CreatePagePanel();
        InventoryRow[] ready = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString()).ToArray();
        InventoryRow[] decisions = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString()).ToArray();
        panel.Children.Add(StewardVisuals.PageHeader("Review decisions", "Resolve package-identity decisions first. Verified updates remain separate and still require an exact final confirmation."));
        panel.Children.Add(StewardVisuals.InformationBanner("Review is not approval", "Opening evidence or recording a reversible package mapping does not start an update."));
        panel.Children.Add(new TextBlock { Text = "Needs a decision", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(PageCopy(decisions.Length == 0 ? "No source-matched items currently need a decision." : $"{decisions.Length} item{(decisions.Length == 1 ? string.Empty : "s")} need a decision. These are not approved updates."));
        foreach (InventoryRow row in decisions)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel decision = new() { Spacing = StewardSpacing.XSmall };
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
            StackPanel action = new() { Spacing = StewardSpacing.XSmall };
            action.Children.Add(new TextBlock { Text = $"{row.Name} {row.Version} → {detail?.CandidateVersion ?? "candidate unavailable"}", Style = AppStyle("StewardSectionTitleStyle") });
            action.Children.Add(StewardVisuals.StatusBadge(row.Status));
            action.Children.Add(PageCopy($"{detail?.SourceId ?? "Unknown source"} · exact identity required · {row.Scope} scope · {detail?.Architecture ?? "Unknown architecture"}"));
            Button open = new() { Content = "Review evidence" }; AutomationProperties.SetName(open, $"Review {row.Name} evidence"); open.Click += async (_, _) => await ShowDetailsAsync(row); action.Children.Add(open);
            panel.Children.Add(Card(action));
        }
        panel.Children.Add(PageCopy("Sequential and verified: PC Steward rechecks the installed version before recording an update as successful."));
        return panel;
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
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
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
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Pin {row.Name} at the installed version {row.Version}? PC Steward will stop offering a different version until you reverse this decision on the Decisions page. No application is changed."));
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
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Ignore {row.Name} indefinitely, or select an end date below. You can reverse this decision on the Decisions page. No application is changed."));
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

    private StackPanel BuildStoragePage()
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
        Button privacy = new() { Content = "Manage privacy and data" }; privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy); panel.Children.Add(privacy);
        return panel;
    }

    private StackPanel BuildActivityPage()
    {
        StackPanel panel = CreatePagePanel();
        Button privacy = new() { Content = "Privacy & data" };
        AutomationProperties.SetName(privacy, "View privacy and local data controls");
        privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        panel.Children.Add(StewardVisuals.PageHeader("Activity", "A local ledger of completed inventory and reversible decisions. PC Steward does not upload activity telemetry.", null, privacy));

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
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
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
        return panel;
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

    private StackPanel BuildPrivacyPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Privacy & data", "Export or remove PC Steward-owned information with explicit, category-specific controls.", "Stored locally"));
        panel.Children.Add(StewardVisuals.InformationBanner("Installed applications are preserved", "Routine cleanup affects only the named PC Steward category. Factory reset is separate and also preserves installed applications and provider caches."));
        AddDataAndResetSection(panel);
        return panel;
    }

    /// <summary>FR-POL-01–05 (ADR-030 foundation 7): pins, ignores and confirmed package identities in one reversible view.</summary>
    private StackPanel BuildDecisionsPage()
    {
        StackPanel panel = CreatePagePanel(900);
        panel.Children.Add(StewardVisuals.PageHeader("Decisions", "Pins, ignores and package identities you confirmed. Each one is reversible, and none of them starts an update."));
        StackPanel decisions = new() { Spacing = StewardSpacing.Medium };
        AddDecisionsSection(decisions);
        panel.Children.Add(Card(decisions));
        return panel;
    }

    private static Style AppStyle(string key) => (Style)global::Microsoft.UI.Xaml.Application.Current.Resources[key];

    /// <summary>
    /// FR-UI-01/02 (ADR-030 foundation 11; audit P-L1): the window chrome, built once. Content extends into a 32 px custom title
    /// bar with a Back button, the app mark and name; the drag region excludes Back so it stays clickable. Mica is the base
    /// layer where supported (Desktop Acrylic, then the opaque canvas brush, as fallbacks); high contrast always uses the
    /// opaque system-mapped canvas brush.
    /// </summary>
    private void BuildWindowChrome()
    {
        ExtendsContentIntoTitleBar = true;
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Standard;
        }

        windowRoot = new Grid();
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StewardSpacing.TitleBarHeight) });
        windowRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid titleBar = new() { Height = StewardSpacing.TitleBarHeight };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBarBack = new Button
        {
            Content = new FontIcon { Glyph = "\uE72B", FontSize = 12 },
            Width = 40,
            Height = StewardSpacing.TitleBarHeight,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(StewardSpacing.XSmall),
            VerticalAlignment = VerticalAlignment.Top,
            IsEnabled = false,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(titleBarBack, "Back");
        // Independent review L9: WinUI appends the keyboard accelerator to the tooltip itself, so the text stays plain.
        ToolTipService.SetToolTip(titleBarBack, "Back");
        titleBarBack.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = Windows.System.VirtualKey.Left, Modifiers = Windows.System.VirtualKeyModifiers.Menu });
        titleBarBack.KeyboardAccelerators.Add(new KeyboardAccelerator { Key = Windows.System.VirtualKey.GoBack });
        titleBarBack.Click += (_, _) => GoBack();
        titleBar.Children.Add(titleBarBack);

        Grid dragRegion = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        StackPanel brand = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(StewardSpacing.Medium, 0, 0, 0) };
        Image mark = new() { Width = 16, Height = 16, Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png")) };
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw);
        brand.Children.Add(mark);
        brand.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, Style = AppStyle("StewardCaptionStyle"), VerticalAlignment = VerticalAlignment.Center });
        dragRegion.Children.Add(brand);
        Grid.SetColumn(dragRegion, 1);
        titleBar.Children.Add(dragRegion);
        windowRoot.Children.Add(titleBar);
        Content = windowRoot;
        SetTitleBar(dragRegion);
        // Independent review L9: the mouse Back button (XButton1) goes back anywhere in the shell, even over handled controls.
        windowRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), true);

        try
        {
            accessibilitySettings = new Windows.UI.ViewManagement.AccessibilitySettings();
            accessibilitySettings.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!windowClosed) RefreshForThemeChange(); });
        }
        catch (Exception) { accessibilitySettings = null; }
        windowRoot.ActualThemeChanged += (_, _) => { if (!windowClosed) RefreshForThemeChange(); };
        ApplyBackdrop();
        ApplyCaptionButtonColors();
    }

    private void OnShellPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (windowRoot is null || navigationView is null || !navigation.CanGoBack) return;
        if (!args.GetCurrentPoint(windowRoot).Properties.IsXButton1Pressed) return;
        args.Handled = true;
        GoBack();
    }

    /// <summary>
    /// Independent review L7: theme brushes are read when controls are built, so a high-contrast or light/dark change refreshes
    /// the backdrop, the caption buttons, the persistent operation band and the current page (which rebuilds its list items).
    /// </summary>
    private void RefreshForThemeChange()
    {
        ApplyBackdrop();
        ApplyCaptionButtonColors();
        if (operationBand is not null)
        {
            operationBand.Background = StewardVisuals.Brush("StewardBrandSubtleBrush");
            operationBand.BorderBrush = StewardVisuals.Brush("StewardBorderBrush");
        }
        if (operationStatusText is not null) operationStatusText.Foreground = StewardVisuals.Brush("StewardTextBrush");
        Render();
    }

    /// <summary>
    /// Independent review L9: with content extended into the title bar, the caption buttons take the current theme's text colour,
    /// with a subtle hover and pressed fill; high contrast uses the system-mapped text and canvas colours.
    /// </summary>
    private void ApplyCaptionButtonColors()
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported()) return;
        Microsoft.UI.Windowing.AppWindowTitleBar bar = AppWindow.TitleBar;
        bool highContrast = accessibilitySettings?.HighContrast == true;
        Windows.UI.Color text = ((SolidColorBrush)StewardVisuals.Brush("StewardTextBrush")).Color;
        Windows.UI.Color canvas = ((SolidColorBrush)StewardVisuals.Brush("StewardCanvasBrush")).Color;
        Windows.UI.Color inactive = ((SolidColorBrush)StewardVisuals.Brush("StewardTertiaryTextBrush")).Color;
        Windows.UI.Color hover = highContrast ? text : Microsoft.UI.ColorHelper.FromArgb(0x19, text.R, text.G, text.B);
        Windows.UI.Color pressed = highContrast ? text : Microsoft.UI.ColorHelper.FromArgb(0x33, text.R, text.G, text.B);
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonForegroundColor = text;
        bar.ButtonInactiveForegroundColor = inactive;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = highContrast ? canvas : text;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = highContrast ? canvas : text;
    }

    private void ApplyBackdrop()
    {
        if (windowRoot is null) return;
        // MicaBackdrop also falls back to a solid colour in high contrast; the explicit check keeps the system-mapped canvas brush.
        bool highContrast = accessibilitySettings?.HighContrast == true;
        SystemBackdrop = highContrast ? null
            : MicaController.IsSupported() ? new MicaBackdrop()
            : DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop()
            : null;
        windowRoot.Background = SystemBackdrop is null ? StewardVisuals.Brush("StewardCanvasBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    /// <summary>Shows a page without the navigation shell (startup failure, post-reset onboarding).</summary>
    private void ShowBarePage(StackPanel panel)
    {
        if (windowRoot is null) return;
        if (navigationView is not null) { windowRoot.Children.Remove(navigationView); navigationView = null; pageHost = null; renderedPage = null; }
        foreach (UIElement bare in windowRoot.Children.Where(child => Grid.GetRow((FrameworkElement)child) == 1).ToArray()) windowRoot.Children.Remove(bare);
        ScrollViewer host = new() { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(host, 1);
        windowRoot.Children.Add(host);
        SyncNavigationChrome();
    }

    /// <summary>
    /// FR-UI-01/02, AC-UX-01/02 (ADR-030; audit P-H1/P-H7): the persistent shell, created once. The NavigationView, the operation
    /// band and the page host survive page changes and refreshes; only the hosted page is rebuilt. Navigation reacts to
    /// <c>ItemInvoked</c>, so invoking the highlighted parent of a contextual page navigates to it.
    /// </summary>
    private void EnsureShell()
    {
        if (navigationView is not null || windowRoot is null) return;
        foreach (UIElement bare in windowRoot.Children.Where(child => Grid.GetRow((FrameworkElement)child) == 1).ToArray()) windowRoot.Children.Remove(bare);

        Grid shell = new();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid operation = new() { ColumnSpacing = StewardSpacing.Large };
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        operationStatusText = new TextBlock { Text = CurrentOperationLabel, Foreground = StewardVisuals.Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(operationStatusText, AutomationLiveSetting.Polite);
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
        operationCancel = cancel;
        operationBand = new Border
        {
            Background = StewardVisuals.Brush("StewardBrandSubtleBrush"),
            BorderBrush = StewardVisuals.Brush("StewardBorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(StewardSpacing.XLarge, StewardSpacing.Small, StewardSpacing.XLarge, StewardSpacing.Small),
            Visibility = HasActiveOperation ? Visibility.Visible : Visibility.Collapsed,
            Child = operation
        };
        shell.Children.Add(operationBand);

        pageHost = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(pageHost, 1);
        shell.Children.Add(pageHost);

        navigationView = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            IsTitleBarAutoPaddingEnabled = false,
            OpenPaneLength = 232,
            CompactPaneLength = 56,
            CompactModeThresholdWidth = 900,
            ExpandedModeThresholdWidth = 1100,
            AlwaysShowHeader = false,
            SelectionFollowsFocus = NavigationViewSelectionFollowsFocus.Disabled,
            Content = shell
        };
        foreach (ShellNavigationItem item in ShellNavigation.Items) AddNavigationItem(navigationView, item);
        navigationView.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer?.Tag is string tag && Enum.TryParse(tag, out ShellPage page)) NavigateTo(page);
        };
        Grid.SetRow(navigationView, 1);
        windowRoot.Children.Add(navigationView);
        SyncNavigationChrome();
    }

    private static void AddNavigationItem(NavigationView navigation, ShellNavigationItem item)
    {
        NavigationViewItem entry = new() { Content = item.Label, Tag = item.Page.ToString(), Icon = new FontIcon { Glyph = item.Glyph } };
        AutomationProperties.SetName(entry, item.Label);
        if (item.IsFooter) navigation.FooterMenuItems.Add(entry); else navigation.MenuItems.Add(entry);
    }

    /// <summary>Highlights the navigation item that owns the current page and enables Back only when there is history.</summary>
    private void SyncNavigationChrome()
    {
        if (titleBarBack is not null)
        {
            titleBarBack.Visibility = navigationView is null ? Visibility.Collapsed : Visibility.Visible;
            titleBarBack.IsEnabled = navigationView is not null && navigation.CanGoBack;
        }
        if (navigationView is null) return;
        string selected = navigation.SelectedItem.ToString();
        navigationView.SelectedItem = navigationView.MenuItems.Concat(navigationView.FooterMenuItems).OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, selected, StringComparison.Ordinal));
    }

    /// <summary>
    /// Rebuilds only the hosted page. On a refresh of the same page the scroll offset and, when it still exists, the focused
    /// control are restored, so keyboard and screen-reader users are not thrown back to the top (AC-UX-02; audit P-H7).
    /// </summary>
    private void ShowCurrentPage(bool preserveViewState)
    {
        if (pageHost is null) return;
        bool sameView = preserveViewState && renderedPage == navigation.Current;
        double offset = sameView ? pageHost.VerticalOffset : 0;
        bool focusWasInPage = IsFocusInside(pageHost, out string? focusKey);
        StackPanel page = BuildPage(navigation.Current);
        renderedPage = navigation.Current;
        pageHost.Content = page;
        if (!sameView) pageHost.ChangeView(null, 0, null, true);
        void Restore(object sender, RoutedEventArgs args)
        {
            page.Loaded -= Restore;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (windowClosed || pageHost is not { } host || !ReferenceEquals(host.Content, page)) return;
                if (offset > 0) RestoreScrollOffset(host, page, offset);
                if (!focusWasInPage) return;
                // Independent review M3: restore only an unambiguous match; otherwise start at the top of the page.
                Control? target = (sameView && focusKey is not null ? PageFocusRestore.UniqueMatch(FindFocusTargets(page, focusKey)) : null)
                    ?? FocusManager.FindFirstFocusableElement(page) as Control;
                target?.Focus(FocusState.Programmatic);
            });
        }
        page.Loaded += Restore;
        RefreshOperationVisuals();
        SyncNavigationChrome();
    }

    /// <summary>
    /// Independent review L8: the rebuilt page must be measured before its saved offset is reachable, so layout is completed
    /// first; if the content is still shorter than the offset, the offset is applied once more after the next layout pass.
    /// </summary>
    private void RestoreScrollOffset(ScrollViewer host, StackPanel page, double offset)
    {
        host.UpdateLayout();
        host.ChangeView(null, offset, null, true);
        if (host.ScrollableHeight >= offset) return;
        void Retry(object? sender, object args)
        {
            host.LayoutUpdated -= Retry;
            if (!windowClosed && ReferenceEquals(host.Content, page)) host.ChangeView(null, offset, null, true);
        }
        host.LayoutUpdated += Retry;
    }

    private bool IsFocusInside(DependencyObject container, out string? key)
    {
        key = null;
        if (Content?.XamlRoot is null || FocusManager.GetFocusedElement(Content.XamlRoot) is not DependencyObject focused) return false;
        key = FocusKey(focused);
        for (DependencyObject? current = focused; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, container)) return true;
        return false;
    }

    private static string? FocusKey(DependencyObject element)
    {
        string name = AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return element switch
        {
            ContentControl { Content: string content } => content,
            TextBox { Header: string header } => header,
            ComboBox { Header: string header } => header,
            _ => null
        };
    }

    /// <summary>Every enabled control on the page whose accessible name (or content) equals <paramref name="key"/>.</summary>
    private static IEnumerable<Control> FindFocusTargets(DependencyObject root, string key)
    {
        if (root is Control { IsEnabled: true } control && string.Equals(FocusKey(control), key, StringComparison.Ordinal)) yield return control;
        IEnumerable<DependencyObject> children = root switch
        {
            Panel panel => panel.Children,
            Border { Child: { } child } => [child],
            Expander { Content: DependencyObject content } => [content],
            ContentControl { Content: DependencyObject content } => [content],
            _ => []
        };
        foreach (DependencyObject child in children)
            foreach (Control match in FindFocusTargets(child, key))
                yield return match;
    }

    private StackPanel BuildScanPage()
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
            scope.Children.Add(PageCopy("Enabling these sources may disclose installed package identifiers to WinGet and Chocolatey Community. Scanning remains read-only and never asks for administrator permission; an update still needs its own review and confirmation."));
            panel.Children.Add(Card(scope));
        }
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button scan = StewardVisuals.PrimaryButton(viewModel.LastScanAt is null ? "Start inventory" : "Replace saved inventory", viewModel.LastScanAt is null ? "Start local inventory" : "Start a replacement local inventory");
        scan.IsEnabled = !HasActiveOperation;
        AutomationProperties.SetName(scan, "Scan this PC"); scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning || scanCancellation is not null) { Button cancel = new() { Content = "Cancel scan", AccessKey = "C" }; AutomationProperties.SetName(cancel, "Cancel scan"); cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(StewardVisuals.InformationBanner("No action is implied", "A scan never approves, downloads, or installs an update. Connected package sources remain separately consented."));
        return panel;
    }

    private StackPanel BuildUpdatesPage()
    {
        StackPanel panel = CreatePagePanel(960);
        int ready = viewModel.AllRows.Count(row => row.Status == "VerifiedUpdate");
        int decisions = viewModel.AllRows.Count(row => row.Status == "ReviewRequired");
        int unavailable = viewModel.AllRows.Count(row => row.Status == "FailedUnavailable");
        Button refresh = new() { Content = viewModel.IsCheckingUpdates ? "Checking sources" : "Refresh evidence", IsEnabled = viewModel.IsUpdateSourceEnabled && !HasActiveOperation };
        AutomationProperties.SetName(refresh, "Refresh approved package source evidence");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        Button reviewDecisions = new() { Content = "Review decisions" };
        AutomationProperties.SetName(reviewDecisions, "Review decisions");
        reviewDecisions.Click += (_, _) => NavigateTo(ShellPage.Review);
        StackPanel headerActions = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        headerActions.Children.Add(reviewDecisions);
        headerActions.Children.Add(refresh);
        panel.Children.Add(StewardVisuals.PageHeader("Updates", "Fresh, compatible, exact package evidence can be reviewed here. Approval and execution remain separate.", "Your approval is required", headerActions));
        panel.Children.Add(StewardVisuals.InformationBanner(viewModel.IsUpdateSourceEnabled ? "Package-source freshness" : "Package sources are off", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));

        Grid summary = new() { ColumnSpacing = 12 };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border readyCard = StewardVisuals.MetricCard(ready, "A final confirmation is still required", "VerifiedUpdate"); summary.Children.Add(readyCard);
        Border decisionCard = StewardVisuals.MetricCard(decisions, "Not eligible for execution", "ReviewRequired"); Grid.SetColumn(decisionCard, 1); summary.Children.Add(decisionCard);
        Border unavailableCard = StewardVisuals.MetricCard(unavailable, "The repository did not provide usable availability evidence", "FailedUnavailable"); Grid.SetColumn(unavailableCard, 2); summary.Children.Add(unavailableCard);
        panel.Children.Add(summary);

        bool hasEvidence = false;
        foreach (InventoryRow row in viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "FailedUnavailable"))
        {
            hasEvidence = true;
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            string action = row.Status switch
            {
                "VerifiedUpdate" when detail is not null => "Open the update evidence to review it before any update can start.",
                "FailedUnavailable" => "Refresh source evidence to retry. This is not a decision or an approved update.",
                _ => "No update action is available: evidence is review-only, stale, or not an exact match."
            };
            StackPanel update = new() { Spacing = StewardSpacing.XSmall };
            Grid heading = new() { ColumnSpacing = 12 };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new() { Spacing = StewardSpacing.XSmall };
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
            Button applications = new() { Content = "View installed apps" };
            applications.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            panel.Children.Add(StewardVisuals.EmptyState("\uE73E", "Nothing needs update review", "Local-only, stale, unavailable, and low-confidence evidence never creates an install action.", applications));
        }
        return panel;
    }

    private StackPanel BuildSettingsPage()
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

        StackPanel decisions = new() { Spacing = StewardSpacing.Small };
        decisions.Children.Add(new TextBlock { Text = "Update decisions", Style = AppStyle("StewardSectionTitleStyle") });
        decisions.Children.Add(PageCopy(activeDecisions.Count == 0 && manualMappings.Count == 0 ? "No active pins, ignores or confirmed package identities." : $"{activeDecisions.Count + manualMappings.Count:N0} active decision{(activeDecisions.Count + manualMappings.Count == 1 ? string.Empty : "s")}. Review or reverse them on the Decisions page."));
        Button openDecisions = new() { Content = "Open Decisions" };
        AutomationProperties.SetName(openDecisions, "Open Decisions");
        openDecisions.Click += (_, _) => NavigateTo(ShellPage.Decisions);
        decisions.Children.Add(openDecisions);
        panel.Children.Add(Card(decisions));

        StackPanel data = new() { Spacing = StewardSpacing.Medium };
        data.Children.Add(new TextBlock { Text = "Storage, privacy & reset", Style = AppStyle("StewardSectionTitleStyle") });
        data.Children.Add(PageCopy("Review app-owned storage, export inventory, clear individual data categories, or perform the separately protected factory-reset flow."));
        data.Children.Add(StewardVisuals.EvidenceRow("Scan records", $"{scanData.ItemCount:N0} items"));
        data.Children.Add(StewardVisuals.EvidenceRow("Downloaded files", $"{downloadCache.TotalBytes:N0} bytes"));
        StackPanel dataActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button storage = new() { Content = "View storage" }; storage.Click += (_, _) => NavigateTo(ShellPage.Storage); dataActions.Children.Add(storage);
        Button privacy = new() { Content = "Privacy & data controls" }; privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy); dataActions.Children.Add(privacy);
        data.Children.Add(dataActions);
        panel.Children.Add(Card(data));
        return panel;
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
                Content = "WinGet and Chocolatey Community may receive installed-package identifiers. Chocolatey packages can run package-maintainer scripts during an update; Chocolatey updates are machine-wide and cannot be started in this version. Scanning remains read-only. " + ElevationStory.SourceConsent,
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
            ContentDialog consent = new() { XamlRoot = Content.XamlRoot, Title = $"Enable {connectorName}?", Content = "Package sources may disclose installed-software identifiers. Scanning remains read-only and does not install or update software.", PrimaryButtonText = "I approve", CloseButtonText = "Keep local-only", DefaultButton = ContentDialogButton.Close };
            approved = await consent.ShowAsync() == ContentDialogResult.Primary;
            if (!approved) enabled = false;
        }
        try { await DataOperationLease.RunAsync(token => workflow.SaveConnectorStateAsync(connectorName, enabled, approved, token), CancellationToken.None); viewModel.SetConnectorEnabled(connectorName, enabled); Render(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); Render(); }
    }

    private async Task StartScanAsync()
    {
        // Independent review L6: a request while a scan or another operation is active starts nothing and never replaces the
        // active scan's cancellation source.
        if (HasActiveOperation) { Render(); return; }
        CancellationTokenSource cancellation = new();
        scanCancellation = cancellation;
        Render();
        Progress<ScanProgress> progress = new(value => { viewModel.ReportProgress(value); RefreshOperationVisuals(); });
        // Providers and SQLite complete asynchronously.  Keep their continuations off the
        // WinUI dispatcher; Progress posts the small UI updates back to it explicitly.
        ScanResult? completed = null;
        try { completed = await DataOperationLease.RunAsync(token => workflow.ScanAsync(progress, token), cancellation.Token); viewModel.CompleteScan(completed, DateTimeOffset.UtcNow); LoadSavedInventory(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
        finally { if (ReferenceEquals(scanCancellation, cancellation)) scanCancellation = null; cancellation.Dispose(); if (completed?.Succeeded == true && navigation.Current == ShellPage.Scan) NavigateTo(ShellPage.Results); else Render(); }
    }

    private async Task CheckUpdateSourcesAsync()
    {
        string? detailStableProductKey = navigation.Current == ShellPage.Detail ? detailRow?.StableProductKey : null;
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
            Button remove = new() { Content = "Remove decision" }; AutomationProperties.SetName(remove, PageFocusRestore.RemoveDecisionName(PageFocusRestore.ProductName(viewModel.AllRows, decision.StableProductKey), decision.Kind)); remove.Click += async (_, _) => { try { await DataOperationLease.RunAsync(token => workflow.ReverseDecisionAsync(decision.Id, token), CancellationToken.None); LoadSavedInventory(); Render(); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }; Grid.SetColumn(remove, 1); row.Children.Add(remove); panel.Children.Add(row);
        }
        foreach (PCSteward.Domain.ManualMapping mapping in manualMappings)
        {
            Grid row = new() { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"Manual mapping: {mapping.SourceId}:{mapping.PackageId}{(string.IsNullOrWhiteSpace(mapping.Reason) ? string.Empty : $" — {mapping.Reason}")}", TextWrapping = TextWrapping.Wrap });
            Button remove = new() { Content = "Remove mapping" }; AutomationProperties.SetName(remove, PageFocusRestore.RemoveManualMappingName(PageFocusRestore.ProductName(viewModel.AllRows, mapping.StableProductKey), mapping.SourceId, mapping.PackageId)); remove.Click += async (_, _) => { try { await DataOperationLease.RunAsync(token => workflow.DeleteManualMappingAsync(mapping.Id, token), CancellationToken.None); LoadSavedInventory(); Render(); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }; Grid.SetColumn(remove, 1); row.Children.Add(remove); panel.Children.Add(row);
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

    private StackPanel BuildProductDetailPage()
    {
        if (detailRow is null)
        {
            StackPanel missing = CreatePagePanel(760);
            Button leave = new() { Content = "Open installed apps" };
            leave.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            missing.Children.Add(StewardVisuals.EmptyState("\uE711", "App details are no longer open", "Open an app from Installed apps, Updates or the scan results to see its details.", leave));
            return missing;
        }

        InventoryRow row = detailRow;
        UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
        bool cachedCurrent = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.Current) && detail?.Freshness.Contains("cached", StringComparison.OrdinalIgnoreCase) == true;
        // One status vocabulary (StatusVocabulary); cached evidence stays visibly distinct in text (FR-UI-05).
        string status = cachedCurrent ? $"{StatusVocabulary.For(row.Status).Label} · cached evidence" : StatusVocabulary.For(row.Status).Label;

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
        Button back = new() { Content = "Back" }; AutomationProperties.SetName(back, "Back to previous application view"); back.Click += (_, _) => { detailRow = null; if (navigation.CanGoBack) GoBack(); else NavigateTo(ShellPage.InstalledApps); }; Grid.SetColumn(back, 1); heading.Children.Add(back);
        panel.Children.Add(heading);

        Grid comparison = new() { ColumnSpacing = 12, RowSpacing = 12 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel installed = new() { Spacing = StewardSpacing.Small };
        installed.Children.Add(new TextBlock { Text = "Installed", Style = AppStyle("StewardSectionTitleStyle") });
        installed.Children.Add(StewardVisuals.EvidenceRow("Version", row.Version));
        installed.Children.Add(StewardVisuals.EvidenceRow("Publisher", row.Publisher));
        installed.Children.Add(StewardVisuals.EvidenceRow("Location", $"{StewardPresentation.Scope(row.Scope)} · {StewardPresentation.Provider(row.Provider)}"));
        comparison.Children.Add(Card(installed));
        StackPanel evidence = new() { Spacing = StewardSpacing.Small };
        evidence.Children.Add(new TextBlock { Text = "Repository evidence", Style = AppStyle("StewardSectionTitleStyle") });
        evidence.Children.Add(StewardVisuals.EvidenceRow("Package ID", detail?.PackageId ?? "No managed package identity"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Repository", detail?.SourceId ?? "No enabled source match"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Available", detail?.CandidateVersion ?? "Not established"));
        evidence.Children.Add(PageCopy(detail?.Freshness ?? "Local-only — no source check has been run."));
        Grid.SetColumn(evidence, 1); comparison.Children.Add(Card(evidence));
        panel.Children.Add(comparison);

        StackPanel why = new() { Spacing = StewardSpacing.Small };
        why.Children.Add(new TextBlock { Text = "Why this appears", Style = AppStyle("StewardSectionTitleStyle") });
        why.Children.Add(PageCopy(detail?.Explanation ?? "This application is present in the local inventory. No concrete repository package identity is currently available."));
        panel.Children.Add(Card(why));

        StackPanel capability = new() { Spacing = StewardSpacing.Small };
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
            StackPanel reviewedRoute = new() { Spacing = StewardSpacing.Small };
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
        return panel;
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
        try { detailRaw = await DataOperationLease.RunReadAsync(token => workflow.GetRawDiagnosticsAsync(row.ProductId, token), CancellationToken.None); detailTimeline = await DataOperationLease.RunReadAsync(token => workflow.GetTimelineAsync(row.StableProductKey, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        NavigateTo(ShellPage.Detail);
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
            ? ElevationStory.HelperMissingOutcome
            : result.Message;

    private Task<ContentDialogResult> ShowUpdateOutcomeAsync(string title, string message) => new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync().AsTask();
}
