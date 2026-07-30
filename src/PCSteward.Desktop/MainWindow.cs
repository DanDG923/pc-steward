using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
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

    public MainWindow(InventoryWorkflow workflow, InventoryExperienceViewModel viewModel, DownloadedArtifactCache downloads, ReviewedUpdateExecutionWorkflow updates, IScheduledCheckSettingsStore scheduleSettingsStore, ScheduledCheckRegistrationService scheduleRegistration)
    {
        this.workflow = workflow; this.viewModel = viewModel; this.downloads = downloads; this.updates = updates; this.scheduleSettingsStore = scheduleSettingsStore; this.scheduleRegistration = scheduleRegistration;
        Title = AppIdentity.DisplayName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "pc-steward.ico"));
        LoadSavedInventory();
        Render();
    }

    private void LoadSavedInventory()
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
    }

    private void Render()
    {
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
        RenderPlaceholder();
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

    private void RenderOverviewPage()
    {
        int ready = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate);
        int review = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired);
        int current = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.Current);
        int unmanaged = viewModel.UpdateStatusCounts.GetValueOrDefault(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged);
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(new TextBlock { Text = "Overview", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy(ready > 0 ? $"{ready} update{(ready == 1 ? string.Empty : "s")} are ready to review" : "Your PC is accounted for"));
        panel.Children.Add(PageCopy("PC Steward keeps local inventory, source evidence, and any action separate. Nothing changes until you explicitly review and confirm it."));

        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button reviewUpdates = new() { Content = ready > 0 ? $"Review {ready} update{(ready == 1 ? string.Empty : "s")}" : "Open updates", IsEnabled = !isExecuting };
        AutomationProperties.SetName(reviewUpdates, "Review update evidence");
        reviewUpdates.Click += (_, _) => { destination = "updates"; Render(); };
        Button scan = new() { Content = viewModel.IsScanning ? "Scan in progress" : "Scan again", IsEnabled = !viewModel.IsScanning && !isExecuting };
        AutomationProperties.SetName(scan, scan.Content.ToString());
        scan.Click += async (_, _) => await StartScanAsync();
        actions.Children.Add(reviewUpdates); actions.Children.Add(scan);

        Grid hero = new() { ColumnSpacing = 24 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.75, GridUnitType.Star) });
        StackPanel heroCopy = new() { Spacing = 8 };
        heroCopy.Children.Add(new TextBlock { Text = "EVIDENCE BEFORE ACTION", Style = AppStyle("StewardCaptionStyle") });
        heroCopy.Children.Add(new TextBlock { Text = "Your PC is accounted for", Style = AppStyle("StewardSectionTitleStyle") });
        heroCopy.Children.Add(PageCopy($"{viewModel.AllRows.Count:N0} installed applications. {ready} verified update{(ready == 1 ? string.Empty : "s")} can be reviewed; {review} item{(review == 1 ? string.Empty : "s")} need a decision; unmanaged software remains visible."));
        heroCopy.Children.Add(actions);
        hero.Children.Add(heroCopy);
        StackPanel evidence = new() { Spacing = 8 };
        evidence.Children.Add(PageCopy($"Scan mode: {viewModel.ScanModeLabel}"));
        evidence.Children.Add(PageCopy($"Completed: {viewModel.LastScanCompletedLabel}"));
        evidence.Children.Add(PageCopy($"Source evidence: {viewModel.UpdateFreshnessLabel}"));
        evidence.Children.Add(PageCopy("Changes made: none automatically"));
        Grid.SetColumn(evidence, 1); hero.Children.Add(evidence);
        panel.Children.Add(Card(hero));

        Grid metrics = new() { ColumnSpacing = 12, RowSpacing = 12 };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        (int count, string label, string detail)[] values = [(ready, "Ready to review", "Verified package identity"), (review, "Need a decision", "No automatic action"), (current, "Current", "No action expected"), (unmanaged, "Unmanaged", "Visible, not labelled unsafe")];
        for (int index = 0; index < values.Length; index++)
        {
            StackPanel metric = new() { Spacing = 4 };
            metric.Children.Add(new TextBlock { Text = values[index].count.ToString(CultureInfo.CurrentCulture), Style = AppStyle("StewardPageTitleStyle") });
            metric.Children.Add(new TextBlock { Text = values[index].label, Style = AppStyle("StewardSectionTitleStyle") });
            metric.Children.Add(PageCopy(values[index].detail));
            Border card = Card(metric); Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); metrics.Children.Add(card);
        }
        panel.Children.Add(metrics);
        panel.Children.Add(new TextBlock { Text = "What is worth reviewing", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(PageCopy("Open Applications to inspect the installed inventory, Repository apps to see source-managed status, or Updates to inspect evidence that could lead to a separate confirmation."));
        StackPanel records = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button applications = new() { Content = "Open applications" }; AutomationProperties.SetName(applications, "Open applications inventory"); applications.Click += (_, _) => { destination = "applications"; Render(); }; records.Children.Add(applications);
        Button currentApps = new() { Content = "View repository apps" }; AutomationProperties.SetName(currentApps, "View repository apps"); currentApps.Click += (_, _) => { destination = "current"; Render(); }; records.Children.Add(currentApps);
        panel.Children.Add(records);
        Content = BuildShell(panel);
    }

    private void RenderApplicationsPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(new TextBlock { Text = "Applications", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("Installed applications appear once while their provider, scope, version, and update evidence remain inspectable. Filtering changes only this view."));
        panel.Children.Add(PageCopy($"Local inventory: {viewModel.FreshnessLabel}"));
        TextBox search = new() { PlaceholderText = "Search applications", Text = viewModel.SearchText };
        AutomationProperties.SetName(search, "Search applications");
        ComboBox status = FilterBox(viewModel.StatusFilter, ["All statuses", "Installed", .. Enum.GetNames<PCSteward.Domain.UpdateIntelligenceStatus>()]);
        ComboBox publisher = FilterBox(viewModel.PublisherFilter, ["All publishers", .. viewModel.AvailablePublishers]);
        ComboBox source = FilterBox(viewModel.SourceFilter, "All sources", "registry", "msix", "winget");
        ComboBox scope = FilterBox(viewModel.ScopeFilter, "All scopes", "Machine", "User", "Unknown");
        ComboBox sort = FilterBox(viewModel.SortField, "Name", "Source", "Publisher");
        AutomationProperties.SetName(status, "Filter applications by status"); AutomationProperties.SetName(publisher, "Filter applications by publisher"); AutomationProperties.SetName(source, "Filter applications by source"); AutomationProperties.SetName(scope, "Filter applications by scope"); AutomationProperties.SetName(sort, "Sort applications");
        ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 560 };
        TextBlock matching = PageCopy(string.Empty);
        void ApplyFilters()
        {
            viewModel.SetFilters(search.Text, status.SelectedItem?.ToString(), publisher.SelectedItem?.ToString(), source.SelectedItem?.ToString(), scope.SelectedItem?.ToString(), sort.SelectedItem?.ToString());
            RenderInventory(list);
            matching.Text = $"{viewModel.VisibleRows.Count:N0} application{(viewModel.VisibleRows.Count == 1 ? string.Empty : "s")} match the current filters.";
        }
        search.TextChanged += (_, _) => ApplyFilters(); status.SelectionChanged += (_, _) => ApplyFilters(); publisher.SelectionChanged += (_, _) => ApplyFilters(); source.SelectionChanged += (_, _) => ApplyFilters(); scope.SelectionChanged += (_, _) => ApplyFilters(); sort.SelectionChanged += (_, _) => ApplyFilters();
        panel.Children.Add(search);
        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        filters.Children.Add(status); filters.Children.Add(publisher); filters.Children.Add(source); filters.Children.Add(scope); filters.Children.Add(sort); panel.Children.Add(filters);
        list.ItemClick += async (_, args) => await ShowDetailsAsync((InventoryRow)args.ClickedItem);
        ApplyFilters();
        panel.Children.Add(matching);
        panel.Children.Add(new TextBlock { Text = "Application    Version    Status    Scope · source", Style = AppStyle("StewardCaptionStyle") });
        panel.Children.Add(list);
        Content = BuildShell(panel);
    }

    private void RenderCurrentPage()
    {
        StackPanel panel = CreatePagePanel(960);
        IReadOnlyList<InventoryRow> managedRows = viewModel.RepositoryManagedRows;
        int upToDate = managedRows.Count(row => row.Status == "Current");
        int updatesAvailable = managedRows.Count(row => row.Status == "VerifiedUpdate");
        int reviewRequired = managedRows.Count(row => row.Status == "ReviewRequired");
        panel.Children.Add(new TextBlock { Text = "Repository-managed apps", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("These installed applications have a concrete WinGet or Microsoft Store package identity from an enabled repository. Their status says whether they are up to date, have an update available, or require review. Cached evidence is shown as cached and may be stale; local-only inventory is excluded."));
        panel.Children.Add(PageCopy($"Repository evidence: {viewModel.UpdateFreshnessLabel}"));

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
            panel.Children.Add(PageCopy(empty));
        }
        else
        {
            panel.Children.Add(PageCopy($"{managedRows.Count:N0} repository-managed app{(managedRows.Count == 1 ? string.Empty : "s")}: {upToDate} up to date, {updatesAvailable} update{(updatesAvailable == 1 ? string.Empty : "s")} available, {reviewRequired} requiring review."));
            foreach (InventoryRow row in managedRows)
            {
                UpdateDetail detail = viewModel.UpdateDetails[row.StableProductKey];
                StackPanel current = new() { Spacing = 5 };
                current.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
                current.Children.Add(PageCopy(row.Publisher));
                current.Children.Add(PageCopy($"Status: {RepositoryManagementStatus(row.Status)}"));
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
        _ => "Repository status unavailable"
    };

    private void RenderResultsPage()
    {
        StackPanel panel = CreatePagePanel();
        int actionable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString());
        int decisions = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString());
        int unmanaged = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged.ToString());
        panel.Children.Add(new TextBlock { Text = "Scan results", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("Results distinguish verified opportunities, decisions, unmanaged software, and app-owned cleanup. No action starts until you open evidence and confirm it separately."));
        StackPanel filters = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (string label in new[] { $"All {actionable + decisions + unmanaged}", $"Actionable {actionable}", $"Needs decision {decisions}", $"Unmanaged {unmanaged}" })
        {
            Button filter = new() { Content = label, IsEnabled = false }; filters.Children.Add(filter);
        }
        panel.Children.Add(filters);
        foreach (InventoryRow row in viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "Unmanaged"))
        {
            StackPanel finding = new() { Spacing = 5 };
            finding.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
            finding.Children.Add(PageCopy($"{row.Status} · {row.Provider} · {row.Scope} scope"));
            finding.Children.Add(PageCopy(row.Status == "VerifiedUpdate" ? "Open the exact source evidence before a separate update confirmation." : row.Status == "ReviewRequired" ? "No source is selected automatically; inspect the evidence and choose a safe next step." : "Inventoried locally; unmanaged is visible, not labelled unsafe."));
            Button open = new() { Content = "Open evidence" }; AutomationProperties.SetName(open, $"Open {row.Name} evidence"); open.Click += async (_, _) => await ShowDetailsAsync(row); finding.Children.Add(open);
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
        panel.Children.Add(new TextBlock { Text = "Review actions", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("Review required items need a decision before they can become eligible for a separate update confirmation. Approved updates remain distinct and are never started from this page."));
        panel.Children.Add(new TextBlock { Text = "Needs a decision", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(PageCopy(decisions.Length == 0 ? "No source-matched items currently need a decision." : $"{decisions.Length} item{(decisions.Length == 1 ? string.Empty : "s")} need a decision. These are not approved updates."));
        foreach (InventoryRow row in decisions)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel decision = new() { Spacing = 5 };
            decision.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
            decision.Children.Add(PageCopy($"{detail?.SourceId ?? row.Provider} · {detail?.PackageId ?? "package identity unavailable"}"));
            decision.Children.Add(PageCopy(detail?.Explanation ?? "The available evidence needs review before any update can be considered."));
            Button open = new() { Content = "Review evidence" }; AutomationProperties.SetName(open, $"Review {row.Name} decision evidence"); open.Click += async (_, _) => await ShowDetailsAsync(row); decision.Children.Add(open);
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
            confirmAll.Click += async (_, _) =>
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
                RepositoryIdentityConfirmation[] confirmations = currentIdentityCandidates
                    .Select(row => viewModel.UpdateDetails[row.StableProductKey])
                    .Select(detail => new RepositoryIdentityConfirmation(detail.StableProductKey, detail.SourceId!, detail.PackageId!))
                    .ToArray();
                RepositoryIdentityConfirmationResult result = await workflow.ConfirmCurrentRepositoryIdentitiesAsync(confirmations, viewModel.IsUpdateSourceEnabled, "Bulk confirmation of source-reported current package identity.", CancellationToken.None);
                LoadSavedInventory();
                Render();
                await ShowUpdateOutcomeAsync(result.Confirmed == 0 ? "No mappings confirmed" : "Package identities confirmed", result.Message);
            };
            panel.Children.Add(confirmAll);
        }
        panel.Children.Add(new TextBlock { Text = "Approved updates", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(PageCopy(ready.Length == 0 ? "No approved actions are ready. Cached, unavailable, low-confidence, or conflicting evidence remains review-only." : $"{ready.Length} approved action{(ready.Length == 1 ? string.Empty : "s")} can be opened for separate confirmation."));
        foreach (InventoryRow row in ready)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel action = new() { Spacing = 5 };
            action.Children.Add(new TextBlock { Text = $"{row.Name} {row.Version} → {detail?.CandidateVersion ?? "candidate unavailable"}", Style = AppStyle("StewardSectionTitleStyle") });
            action.Children.Add(PageCopy($"{detail?.SourceId ?? "Unknown source"} · exact identity required · {row.Scope} scope · {detail?.Architecture ?? "Unknown architecture"}"));
            Button open = new() { Content = "Review evidence" }; AutomationProperties.SetName(open, $"Review {row.Name} evidence"); open.Click += async (_, _) => await ShowDetailsAsync(row); action.Children.Add(open);
            panel.Children.Add(Card(action));
        }
        panel.Children.Add(PageCopy("Sequential and verified: PC Steward rechecks the installed version before recording an update as successful."));
        Content = BuildShell(panel);
    }

    private void RenderStoragePage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(new TextBlock { Text = "Storage", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("Storage shown here is limited to PC Steward-owned data. Windows temporary files, provider caches, documents, drivers, and firmware are outside this scope."));
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
        panel.Children.Add(new TextBlock { Text = "Activity", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("A local record of scan state, saved decisions, and update evidence. PC Steward does not upload activity telemetry."));
        panel.Children.Add(Card(new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "Latest inventory", Style = AppStyle("StewardSectionTitleStyle") },
                PageCopy(viewModel.LastScanAt is null ? "No completed scan is recorded." : $"Completed {viewModel.LastScanCompletedLabel}. {viewModel.ScanPhase}."),
                PageCopy($"Saved decisions: {activeDecisions.Count}; manual mappings: {manualMappings.Count}."),
                PageCopy($"Update evidence: {viewModel.UpdateFreshnessLabel}")
            }
        }));
        Button privacy = new() { Content = "View privacy and data controls" }; AutomationProperties.SetName(privacy, "View privacy and data controls"); privacy.Click += (_, _) => { destination = "privacy"; Render(); }; panel.Children.Add(privacy);
        Content = BuildShell(panel);
    }

    private void RenderPrivacyPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(new TextBlock { Text = "Privacy & data", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("Your PC Steward data stays on this PC. Each category has a separate purpose, retention model, and confirmation before removal."));
        panel.Children.Add(PageCopy("Routine cleanup affects only the stated PC Steward data. Factory reset is separate and preserves installed applications and provider caches."));
        AddDataAndResetSection(panel);
        Content = BuildShell(panel);
    }

    private static Style AppStyle(string key) => (Style)global::Microsoft.UI.Xaml.Application.Current.Resources[key];

    private NavigationView BuildShell(StackPanel panel)
    {
        Grid shell = new() { Background = (Microsoft.UI.Xaml.Media.Brush)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardCanvasBrush"] };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Border header = new()
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardSurfaceBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardBorderBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(24, 12, 24, 12)
        };
        StackPanel brand = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        Image mark = new() { Width = 32, Height = 32, Source = new BitmapImage(new Uri("ms-appx:///Assets/logo-mark-64.png")) };
        AutomationProperties.SetName(mark, AppIdentity.DisplayName);
        brand.Children.Add(mark);
        StackPanel copy = new() { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, Style = AppStyle("StewardSectionTitleStyle") });
        copy.Children.Add(new TextBlock { Text = "Evidence before action", Style = AppStyle("StewardCaptionStyle") });
        brand.Children.Add(copy);
        StackPanel headerContents = new() { Spacing = 4 };
        headerContents.Children.Add(brand);
        headerContents.Children.Add(new TextBlock { Text = viewModel.IsScanning ? $"Scan in progress · {viewModel.ScanPhase}" : isExecuting ? "Approved update verification is in progress" : "No background coordinator is running.", Style = AppStyle("StewardCaptionStyle") });
        header.Child = headerContents;
        shell.Children.Add(header);

        Border contentCard = new() { Style = AppStyle("StewardCardStyle"), Margin = new Thickness(24), Child = panel };
        ScrollViewer content = new()
        {
            Content = contentCard,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(content, 1);
        shell.Children.Add(content);
        NavigationView navigation = new()
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = true,
            OpenPaneLength = 232,
            CompactPaneLength = 56,
            AlwaysShowHeader = false,
            SelectionFollowsFocus = NavigationViewSelectionFollowsFocus.Disabled,
            Content = shell
        };
        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Home" });
        AddNavigationItem(navigation, "Overview", "overview", "\uE80F");
        AddNavigationItem(navigation, "Scan", "scan", "\uE721");
        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Review" });
        AddNavigationItem(navigation, "Scan results", "results", "\uE9D2");
        AddNavigationItem(navigation, "Review actions", "review", "\uE8C8");
        AddNavigationItem(navigation, "Applications", "applications", "\uE71D");
        AddNavigationItem(navigation, "Updates", "updates", "\uE896");
        AddNavigationItem(navigation, "Repository apps", "current", "\uE73E");
        AddNavigationItem(navigation, "Storage", "storage", "\uE7F1");
        navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Records" });
        AddNavigationItem(navigation, "Activity", "activity", "\uE823");
        AddNavigationItem(navigation, "Privacy & data", "privacy", "\uEA18");
        AddNavigationItem(navigation, "About & support", "about", "\uE897", true);
        navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => string.Equals(item.Tag as string, destination, StringComparison.Ordinal));
        navigation.SelectionChanged += (_, args) =>
        {
            string? selected = args.IsSettingsSelected ? "settings" : (args.SelectedItem as NavigationViewItem)?.Tag as string;
            if (selected is not null && !string.Equals(selected, destination, StringComparison.Ordinal)) { destination = selected; Render(); }
        };
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.IsSettingsInvoked && !string.Equals(destination, "settings", StringComparison.Ordinal)) { destination = "settings"; Render(); }
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
        StackPanel panel = new() { Spacing = 16, Padding = new Thickness(24), MaxWidth = 900 };
        panel.Resources[typeof(TextBlock)] = AppStyle("StewardBodyStyle");
        panel.Resources[typeof(Button)] = AppStyle("StewardButtonStyle");
        panel.Children.Add(new TextBlock { Text = "Scan", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(new TextBlock { Text = viewModel.IsScanning ? "Scanning only reads installed-software information. Your prior completed inventory remains available until the replacement snapshot is published." : viewModel.LastScanAt is null ? "Start with a local-only inventory. It does not request elevation, install, uninstall, or delete anything." : "Review the current scan state and start a replacement inventory when you are ready.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Status: {viewModel.ScanPhase}. Elapsed: {viewModel.Elapsed:mm\\:ss}. {viewModel.FreshnessLabel}", TextWrapping = TextWrapping.Wrap });
        if (viewModel.LastScanWarnings.Count != 0) panel.Children.Add(new TextBlock { Text = "Provider result: " + string.Join(", ", viewModel.LastScanWarnings) + ". Previous completed data is retained where available.", TextWrapping = TextWrapping.Wrap });
        if (!viewModel.IsScanning)
        {
            StackPanel scope = new() { Spacing = 8 };
            scope.Children.Add(new TextBlock { Text = "Choose what PC Steward may check", Style = AppStyle("StewardSectionTitleStyle") });
            scope.Children.Add(new CheckBox { Content = "Windows application inventory (required · read-only)", IsChecked = true, IsEnabled = false });
            ToggleSwitch winget = new() { Header = "WinGet trusted sources", IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Local only", OnContent = "Enabled after disclosure" };
            AutomationProperties.SetName(winget, "Enable WinGet trusted sources"); winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn); scope.Children.Add(winget);
            scope.Children.Add(PageCopy("Enabling WinGet may disclose installed package identifiers to configured sources. Scanning remains read-only and never installs a package."));
            panel.Children.Add(Card(scope));
        }
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button scan = new() { Content = "Scan this PC", IsEnabled = !viewModel.IsScanning && !isExecuting };
        AutomationProperties.SetName(scan, "Scan this PC"); scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning) { Button cancel = new() { Content = "Cancel scan", AccessKey = "C" }; AutomationProperties.SetName(cancel, "Cancel scan"); cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = "Source checks remain opt-in: enable a trusted source in Settings only after its disclosure. A scan never begins an update.", TextWrapping = TextWrapping.Wrap });
        Content = BuildShell(panel);
    }

    private void RenderUpdatesPage()
    {
        StackPanel panel = CreatePagePanel(960);
        panel.Children.Add(new TextBlock { Text = "Updates", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(PageCopy("Only fresh, compatible, and explicitly reviewed package identities can move into an update confirmation."));
        panel.Children.Add(PageCopy(viewModel.UpdateFreshnessLabel));
        bool hasEvidence = false;
        foreach (InventoryRow row in viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "FailedUnavailable"))
        {
            hasEvidence = true;
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            string action = row.Status == "VerifiedUpdate" && detail is not null ? "Open evidence review from Applications before any update can start." : "No update action is available: evidence is review-only, stale, unavailable, or not an exact match.";
            StackPanel update = new() { Spacing = 5 };
            update.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardSectionTitleStyle") });
            update.Children.Add(PageCopy(row.Publisher));
            update.Children.Add(PageCopy($"{row.Version}  →  {detail?.CandidateVersion ?? "candidate unavailable"}"));
            update.Children.Add(PageCopy($"{row.Scope} scope · {detail?.Architecture ?? "Unknown architecture"} · {detail?.SourceId ?? row.Provider}"));
            update.Children.Add(PageCopy($"Package ID: {detail?.PackageId ?? "unknown"}. {detail?.Freshness ?? action}"));
            Button review = new() { Content = "Review evidence" }; AutomationProperties.SetName(review, $"Review {row.Name} evidence. {row.Status}. {action}"); review.Click += async (_, _) => await ShowDetailsAsync(row); update.Children.Add(review);
            panel.Children.Add(Card(update));
        }
        if (!hasEvidence) panel.Children.Add(new TextBlock { Text = "No update evidence needs review. Local-only, stale, unavailable, and low-confidence evidence never creates an install action.", TextWrapping = TextWrapping.Wrap });
        Content = BuildShell(panel);
    }

    private void RenderSettings()
    {
        StackPanel panel = new() { Spacing = 16, Padding = new Thickness(24), MaxWidth = 900 };
        panel.Resources[typeof(TextBlock)] = AppStyle("StewardBodyStyle");
        panel.Resources[typeof(Button)] = AppStyle("StewardButtonStyle");
        panel.Children.Add(new TextBlock { Text = "Settings", Style = AppStyle("StewardPageTitleStyle") });
        panel.Children.Add(new TextBlock { Text = "Preferences and trusted sources", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = "Source choices explain their disclosure and resource impact before they change. Enabling a source never starts a scan, check, download, or update.", TextWrapping = TextWrapping.Wrap });
        StackPanel sources = new() { Spacing = 12 };
        sources.Children.Add(new TextBlock { Text = "Inventory and update sources", Style = AppStyle("StewardSectionTitleStyle") });
        sources.Children.Add(new CheckBox { Content = "Windows local inventory (required · local only)", IsChecked = true, IsEnabled = false });
        ConnectorOption appx = viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog");
        CheckBox includeAppx = new() { Content = "Include current-user AppX/MSIX catalog", IsChecked = appx.IsEnabled };
        AutomationProperties.SetName(includeAppx, "Include current-user AppX/MSIX catalog");
        includeAppx.Checked += async (_, _) => await ChangeConnectorAsync(appx.Name, true);
        includeAppx.Unchecked += async (_, _) => await ChangeConnectorAsync(appx.Name, false);
        sources.Children.Add(includeAppx);
        ToggleSwitch winget = new() { Header = "WinGet trusted sources", IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Disabled", OnContent = "Enabled after disclosure" };
        AutomationProperties.SetName(winget, "Enable WinGet trusted sources");
        winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn);
        sources.Children.Add(winget);
        panel.Children.Add(Card(sources));
        panel.Children.Add(new TextBlock { Text = "Schedule & notifications", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = "Checks only. Scheduled work never installs updates.", TextWrapping = TextWrapping.Wrap });
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
            try { await scheduleRegistration.ApplyAsync(requested, CancellationToken.None); await scheduleSettingsStore.SaveScheduledCheckSettingsAsync(requested, CancellationToken.None); scheduledSettings = requested; scheduledStatus = await scheduleSettingsStore.GetScheduledCheckStatusAsync(CancellationToken.None); Render(); }
            catch (Exception) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Schedule was not changed", Content = "Windows could not create the current-user scan-only task. Your saved settings were left unchanged.", CloseButtonText = "Close" }.ShowAsync(); }
        };
        panel.Children.Add(cadence); panel.Children.Add(startTime); panel.Children.Add(weeklyDay); panel.Children.Add(scheduleEnabled); panel.Children.Add(notifications); panel.Children.Add(save);
        panel.Children.Add(new TextBlock { Text = $"Task status: {scheduledStatus.LastResult}. Last completed: {(scheduledStatus.LastCompletedAt is null ? "not yet" : scheduledStatus.LastCompletedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "A scheduled task runs at lower priority, avoids overlap, exits after its bounded work, and never starts a tray app, service, or update installer.", TextWrapping = TextWrapping.Wrap });
        Content = BuildShell(panel);
    }

    private static ComboBox FilterBox(string selected, params string[] values) => FilterBox(selected, (IReadOnlyCollection<string>)values);
    private static ComboBox FilterBox(string selected, IReadOnlyCollection<string> values) { ComboBox box = new(); foreach (string value in values.Distinct(StringComparer.OrdinalIgnoreCase)) box.Items.Add(value); box.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values.First(); return box; }
    private void RenderInventory(ListView list) { list.Items.Clear(); foreach (InventoryRow row in viewModel.VisibleRows) list.Items.Add(row); list.DisplayMemberPath = nameof(InventoryRow.DisplayLabel); }

    private async Task ChangeWinGetTrustedSourcesAsync(bool enabled)
    {
        bool approved = false;
        if (enabled)
        {
            ContentDialog consent = new()
            {
                XamlRoot = Content.XamlRoot,
                Title = "Enable WinGet trusted sources?",
                Content = "WinGet may disclose installed-software identifiers to configured package sources. It supplies installed-inventory and update-source evidence, remains read-only during scans, and never installs or updates software without a separate review and confirmation.",
                PrimaryButtonText = "I approve",
                CloseButtonText = "Keep local-only",
                DefaultButton = ContentDialogButton.Close
            };
            approved = await consent.ShowAsync() == ContentDialogResult.Primary;
            if (!approved) { Render(); return; }
        }
        try
        {
            await workflow.SaveConnectorStateAsync("WinGet installed inventory", enabled, enabled, CancellationToken.None);
            await workflow.SaveConnectorStateAsync("WinGet package sources", enabled, enabled, CancellationToken.None);
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
            ContentDialog consent = new() { XamlRoot = Content.XamlRoot, Title = $"Enable {connectorName}?", Content = "WinGet may disclose installed-software identifiers to configured package sources. It remains read-only and does not install or update software.", PrimaryButtonText = "I approve", CloseButtonText = "Keep local-only" };
            approved = await consent.ShowAsync() == ContentDialogResult.Primary;
            if (!approved) enabled = false;
        }
        await workflow.SaveConnectorStateAsync(connectorName, enabled, approved, CancellationToken.None); viewModel.SetConnectorEnabled(connectorName, enabled); Render();
    }

    private async Task StartScanAsync()
    {
        scanCancellation = new CancellationTokenSource(); Progress<ScanProgress> progress = new(value => { viewModel.ReportProgress(value); Render(); });
        // Providers and SQLite complete asynchronously.  Keep their continuations off the
        // WinUI dispatcher; Progress posts the small UI updates back to it explicitly.
        ScanResult result = await Task.Run(() => workflow.ScanAsync(progress, scanCancellation.Token));
        viewModel.CompleteScan(result, DateTimeOffset.UtcNow); LoadSavedInventory(); scanCancellation.Dispose(); scanCancellation = null; Render();
    }

    private async Task CheckUpdateSourcesAsync()
    {
        viewModel.BeginUpdateCheck(); Render();
        try { viewModel.LoadUpdateIntelligence(await workflow.RefreshUpdateIntelligenceAsync(viewModel.IsUpdateSourceEnabled, CancellationToken.None)); }
        finally { viewModel.CompleteUpdateCheck(); }
        activeDecisions = await workflow.GetActiveDecisionsAsync(CancellationToken.None); manualMappings = await workflow.GetManualMappingsAsync(CancellationToken.None); Render();
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
            isClearing = true; Render(); AdvancedDataClearResult result = await clear(); isClearing = false; LoadSavedInventory(); Render();
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
        try { FactoryReset.Arm(); Close(); }
        catch (Exception exception) { await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset was not started", Content = exception.Message, CloseButtonText = "Close" }.ShowAsync(); }
    }

    private void AddDecisionsSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Update decisions", Style = AppStyle("StewardSectionTitleStyle") });
        panel.Children.Add(new TextBlock { Text = activeDecisions.Count == 0 && manualMappings.Count == 0 ? "No active pins, ignores or manual mappings. Decisions are optional, reversible, and do not enable updates." : "Active decisions and manual mappings are listed below. Remove one to reapply normal review rules on the next source check.", TextWrapping = TextWrapping.Wrap });
        foreach (PCSteward.Domain.StableDecision decision in activeDecisions)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8 }; row.Children.Add(new TextBlock { Text = $"{decision.Kind}: {decision.Value}{(string.IsNullOrWhiteSpace(decision.Reason) ? string.Empty : $" — {decision.Reason}")}", TextWrapping = TextWrapping.Wrap, Width = 760 }); Button remove = new() { Content = "Remove decision" }; AutomationProperties.SetName(remove, $"Remove {decision.Kind} decision"); remove.Click += async (_, _) => { await workflow.ReverseDecisionAsync(decision.Id, CancellationToken.None); LoadSavedInventory(); Render(); }; row.Children.Add(remove); panel.Children.Add(row);
        }
        foreach (PCSteward.Domain.ManualMapping mapping in manualMappings)
        {
            StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8 }; row.Children.Add(new TextBlock { Text = $"Manual mapping: {mapping.SourceId}:{mapping.PackageId}{(string.IsNullOrWhiteSpace(mapping.Reason) ? string.Empty : $" — {mapping.Reason}")}", TextWrapping = TextWrapping.Wrap, Width = 760 }); Button remove = new() { Content = "Remove mapping" }; AutomationProperties.SetName(remove, $"Remove manual mapping {mapping.PackageId}"); remove.Click += async (_, _) => { await workflow.DeleteManualMappingAsync(mapping.Id, CancellationToken.None); LoadSavedInventory(); Render(); }; row.Children.Add(remove); panel.Children.Add(row);
        }
    }

    private async Task SaveExportAsync(bool json) { InventoryExport export = await workflow.CreateExportAsync(json, CancellationToken.None); FileSavePicker picker = new(); picker.SuggestedFileName = $"pc-steward-inventory-{export.ExportedAt:yyyyMMdd-HHmmss}"; picker.FileTypeChoices.Add(json ? "JSON inventory" : "CSV inventory", [export.FileExtension]); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); StorageFile? file = await picker.PickSaveFileAsync(); if (file is not null) await FileIO.WriteTextAsync(file, export.Content); }

    private async Task ConfirmAndClearScanDataAsync()
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = "Clear scan data?", Content = "This removes current inventory, raw provider records, update candidates and scan-run records. It keeps settings, enabled connectors, schedules, update history, downloaded files, mappings, pins and ignores.", PrimaryButtonText = "Clear scan data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); ClearScanDataResult result = await workflow.ClearScanDataAsync(CancellationToken.None); isClearing = false; if (result.Succeeded) { viewModel.ClearScanData(); LoadSavedInventory(); } ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? "Scan data cleared" : "Scan data was not cleared", Content = result.Message, CloseButtonText = "Close" }; Render(); await outcome.ShowAsync();
    }

    private async Task ConfirmAndClearDownloadedFilesAsync(string title)
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = title, Content = "This removes inactive PC Steward-owned update payloads and temporary extraction files only. It does not delete active update files, installed applications, scan data, source metadata, update history, or Windows, WinGet, Microsoft Store, or other provider caches.", PrimaryButtonText = "Delete downloaded files", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); DownloadCleanupResult result = await downloads.ClearInactiveAsync(CancellationToken.None); isClearing = false; LoadSavedInventory(); ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? "Downloaded files cleared" : "Downloaded files were only partly cleared", Content = result.Message + (result.Residuals.Count == 0 ? string.Empty : " Remaining items: " + string.Join(", ", result.Residuals.Select(residual => $"{residual.RelativePath} ({residual.Reason})"))), CloseButtonText = "Close" }; Render(); await outcome.ShowAsync();
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
            _ => "Unmanaged · local inventory only"
        };

        StackPanel panel = CreatePagePanel(1000);
        Grid heading = new() { ColumnSpacing = 16 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel title = new() { Spacing = 4 };
        title.Children.Add(new TextBlock { Text = "Application detail", Style = AppStyle("StewardCaptionStyle") });
        title.Children.Add(new TextBlock { Text = row.Name, Style = AppStyle("StewardPageTitleStyle") });
        title.Children.Add(PageCopy(status));
        heading.Children.Add(title);
        Button back = new() { Content = "Back" }; AutomationProperties.SetName(back, "Back to previous application view"); back.Click += (_, _) => { destination = detailReturnDestination; detailRow = null; Render(); }; Grid.SetColumn(back, 1); heading.Children.Add(back);
        panel.Children.Add(heading);

        Grid comparison = new() { ColumnSpacing = 12, RowSpacing = 12 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel installed = new() { Spacing = 6 };
        installed.Children.Add(new TextBlock { Text = "Installed", Style = AppStyle("StewardSectionTitleStyle") });
        installed.Children.Add(PageCopy($"Version: {row.Version}"));
        installed.Children.Add(PageCopy($"Publisher: {row.Publisher}"));
        installed.Children.Add(PageCopy($"Scope: {row.Scope} · {row.Provider}"));
        comparison.Children.Add(Card(installed));
        StackPanel evidence = new() { Spacing = 6 };
        evidence.Children.Add(new TextBlock { Text = "Repository evidence", Style = AppStyle("StewardSectionTitleStyle") });
        evidence.Children.Add(PageCopy($"Package ID: {detail?.PackageId ?? "No managed package identity"}"));
        evidence.Children.Add(PageCopy($"Repository: {detail?.SourceId ?? "No enabled source match"}"));
        evidence.Children.Add(PageCopy($"Available version: {detail?.CandidateVersion ?? "Not established"}"));
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

        TextBox reason = new() { Header = "Optional decision reason", PlaceholderText = "Why are you pinning, ignoring or mapping this product?", MaxLength = 512 };
        CheckBox ignoreUntilEnabled = new() { Content = "Ignore until a selected date" };
        DatePicker ignoreUntil = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) };
        ignoreUntilEnabled.Checked += (_, _) => ignoreUntil.IsEnabled = true;
        ignoreUntilEnabled.Unchecked += (_, _) => ignoreUntil.IsEnabled = false;
        TextBlock outcome = new() { TextWrapping = TextWrapping.Wrap };
        StackPanel decisions = new() { Spacing = 8 };
        decisions.Children.Add(new TextBlock { Text = "Decisions", Style = AppStyle("StewardSectionTitleStyle") });
        decisions.Children.Add(reason);
        decisions.Children.Add(ignoreUntilEnabled);
        decisions.Children.Add(ignoreUntil);
        StackPanel decisionActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button pin = new() { Content = "Pin current version" }; pin.Click += async (_, _) => { await workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, reason.Text, CancellationToken.None); LoadSavedInventory(); Render(); }; decisionActions.Children.Add(pin);
        Button ignore = new() { Content = "Ignore product" }; ignore.Click += async (_, _) => { await workflow.IgnoreAsync(row.StableProductKey, ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null, reason.Text, CancellationToken.None); LoadSavedInventory(); Render(); }; decisionActions.Children.Add(ignore);
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: not null, PackageId: not null, CanConfirmAsCurrent: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy($"Confirm this only if you recognise {detail.PackageId} from {detail.SourceId} as this installed application. The reversible mapping does not start an update."));
            Button confirmPackage = new() { Content = "Confirm package identity" };
            AutomationProperties.SetName(confirmPackage, $"Confirm {detail.PackageId} is this installed application");
            confirmPackage.Click += async (_, _) =>
            {
                PCSteward.Domain.ManualMappingValidation validation = await workflow.CreateManualMappingAsync(row.StableProductKey, detail.SourceId, detail.PackageId, true, reason.Text, CancellationToken.None);
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
            Button review = new() { Content = "Review and update" }; AutomationProperties.SetName(review, "Review and update this verified package"); review.Click += async (_, _) => await ReviewAndExecuteAsync(row); decisionActions.Children.Add(review);
        }
        decisions.Children.Add(decisionActions);
        decisions.Children.Add(outcome);
        panel.Children.Add(Card(decisions));

        ComboBox mappingSource = FilterBox(detail?.SourceId ?? "winget", "winget", "msstore");
        TextBox mappingPackage = new() { Header = "Exact approved source package ID", PlaceholderText = detail?.PackageId ?? "For example: Contoso.Widget" };
        StackPanel technical = new() { Spacing = 8 };
        technical.Children.Add(PageCopy($"Installed instances:\n{string.Join("\n", row.Instances.Select(instance => $"{instance.Provider}: {instance.ProviderIdentity}; {instance.Version ?? "unknown version"}; {instance.Scope}; {instance.Architecture}"))}"));
        technical.Children.Add(PageCopy($"Timeline:\n{string.Join("\n", detailTimeline.Select(entry => $"{entry.OccurredAt.ToLocalTime():g} — {entry.Summary}"))}"));
        technical.Children.Add(PageCopy($"Source candidates:\n{string.Join("\n", detail?.SourceCandidates ?? [])}"));
        technical.Children.Add(mappingSource);
        technical.Children.Add(mappingPackage);
        Button map = new() { Content = "Map exact approved package", IsEnabled = viewModel.IsUpdateSourceEnabled }; AutomationProperties.SetName(map, "Create manual mapping for an exact approved source package"); map.Click += async (_, _) => { PCSteward.Domain.ManualMappingValidation validation = await workflow.CreateManualMappingAsync(row.StableProductKey, mappingSource.SelectedItem?.ToString() ?? "winget", string.IsNullOrWhiteSpace(mappingPackage.Text) ? detail?.PackageId ?? string.Empty : mappingPackage.Text, viewModel.IsUpdateSourceEnabled, reason.Text, CancellationToken.None); outcome.Text = validation.Explanation; if (validation.IsValid) { LoadSavedInventory(); detailRow = viewModel.AllRows.SingleOrDefault(candidate => candidate.StableProductKey == row.StableProductKey) ?? row; Render(); } }; technical.Children.Add(map);
        technical.Children.Add(PageCopy($"Bounded raw diagnostic records: {detailRaw.Count}."));
        Expander disclosure = new() { Header = "Technical details", Content = technical };
        panel.Children.Add(disclosure);
        Content = BuildShell(panel);
    }

    private async Task ShowDetailsAsync(InventoryRow row)
    {
        detailRow = row;
        detailReturnDestination = destination;
        detailRaw = await workflow.GetRawDiagnosticsAsync(row.ProductId, CancellationToken.None);
        detailTimeline = await workflow.GetTimelineAsync(row.StableProductKey, CancellationToken.None);
        destination = "detail";
        Render();
    }

    private async Task ReviewAndExecuteAsync(InventoryRow row)
    {
        UpdateReviewPreparation preparation = await updates.BeginReviewAsync(row.StableProductKey, CancellationToken.None);
        if (preparation.Review is null)
        {
            await ShowUpdateOutcomeAsync("Update not available", preparation.Message);
            return;
        }

        PCSteward.Domain.UpdateJobSelection selection = preparation.Review.Selection;
        string elevation = selection.Scope == PCSteward.Domain.UpdateScope.Machine ? "Windows will request UAC only after you confirm this review." : "This is a user-scope update and does not request UAC.";
        string runningWarning = preparation.RunningApplication.AppearsToBeRunning
            ? $"\n\nTarget-specific warning: {selection.PackageId} appears to be running (detected process: {string.Join(", ", preparation.RunningApplication.ProcessNames)}). PC Steward will not close it. Close the application and cancel this review, or explicitly choose Install approved update anyway."
            : $"\n\nNo bounded package-derived process match was detected for {selection.PackageId}. Close the target application first if you know it is running; PC Steward will not close applications.";
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Review approved update",
            Content = $"Package: {selection.PackageId}\nInstalled version: {selection.InstalledVersion}\nTarget version: {selection.CandidateVersion}\nSource: {selection.Source}\nScope: {selection.Scope}\nArchitecture: {selection.Architecture}{runningWarning}\n\n{elevation}\n\nProvider completion is not marked successful until the controlled post-update inventory verifies the target version.",
            PrimaryButtonText = preparation.RunningApplication.AppearsToBeRunning ? "Install approved update anyway" : "Install approved update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        isExecuting = true;
        Render();
        ReviewedUpdateExecutionResult result;
        try { result = await updates.ConfirmAndRunAsync(preparation.Review, preparation.RunningApplication.AppearsToBeRunning, CancellationToken.None); }
        catch (Exception) { result = new(false, "PC Steward could not start the reviewed update. No success was recorded.", null); }
        finally { isExecuting = false; LoadSavedInventory(); Render(); }
        await ShowUpdateOutcomeAsync(result.Started ? "Update completed" : "Update not started", result.Message);
    }

    private Task<ContentDialogResult> ShowUpdateOutcomeAsync(string title, string message) => new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync().AsTask();
}
