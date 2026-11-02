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
    private readonly IActivityHistoryReader activityHistory;
    private readonly Func<ReviewedRouteSplitTokenState> probeReviewedRouteSplitToken;
    private CancellationTokenSource? scanCancellation;
    private ScanDataSummary scanData = new(0, null, 0);
    private IReadOnlyCollection<PCSteward.Domain.StableDecision> activeDecisions = [];
    private IReadOnlyCollection<PCSteward.Domain.ManualMapping> manualMappings = [];
    // Task 73 Phase 3 (FR-HIS-01): bounded, read-only recorded history for Home and Activity; presentation only.
    private IReadOnlyList<UpdateHistoryEntry> updateHistory = [];
    private IReadOnlyList<PCSteward.Domain.ScanRun> scanRuns = [];
    private bool activityHistoryUnavailable;
    private ActivityFilter activityFilter = ActivityFilter.All;
    private const int ActivityHistoryLimit = 500;
    private bool moreAppFiltersExpanded;
    private bool isClearing;
    private bool isSavingSchedule;
    private bool isExecuting;
    // Independent review (L2): set synchronously when Confirm is clicked, before the decision completes, so HasActiveOperation is
    // true at once and F5 or a scan cannot start between the click and the awaited decision. Cleared when isExecuting takes over.
    private bool executionPending;
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
    private string? selectedReadyKey;
    private bool ignoredGroupExpanded;
    private UpdateJobSession? updateJob;
    private TaskCompletionSource<bool>? reviewDecision;
    // Independent review: true while BeginReviewAsync prepares a review, so a second review cannot start meanwhile.
    private bool preparingReview;
    // Independent review: a job finished while the user was elsewhere; the band offers "View result" instead of moving them.
    private bool jobResultPending;
    private ProgressBar? operationProgress;
    private TextBlock? jobProgressStateText;
    private Button? operationViewJob;
    private Control? pageInitialFocus;
    // Task 73 Phase 5 (AC-UX-01/09): the control Ctrl+F focuses on the current page, and the last operation state the live region spoke.
    private Control? pageSearchTarget;
    private string? lastOperationState;

    private enum ResultsFilter { All, Actionable, NeedsDecision, Unavailable, Unmanaged }

    private const int MinimumWindowWidth = 500;
    private const int MinimumWindowHeight = 500;

    /// <summary>
    /// Task 73 Phase 2 (ADR-030 decision 3): what the update job page shows. It holds presentation only — the reviewed sheet, the
    /// current step and, once recorded, the completion. The immutable review and its approval stay inside ReviewAndExecuteAsync.
    /// </summary>
    private sealed class UpdateJobSession(InventoryRow row, UpdateReviewSheet sheet)
    {
        public InventoryRow Row { get; } = row;
        public UpdateReviewSheet Sheet { get; } = sheet;
        public UpdateJobStep Step { get; set; } = UpdateJobStep.Review;
        public UpdateJobCompletion? Completion { get; set; }
    }

    public MainWindow(InventoryWorkflow workflow, InventoryExperienceViewModel viewModel, DownloadedArtifactCache downloads, ReviewedUpdateExecutionWorkflow updates, IScheduledCheckSettingsStore scheduleSettingsStore, ScheduledCheckRegistrationService scheduleRegistration, IReviewedUpdateHistoryReader reviewedHistory, IActivityHistoryReader activityHistory, Func<ReviewedRouteSplitTokenState> probeReviewedRouteSplitToken, bool showPostResetOnboarding = false, Func<FactoryResetResult>? acknowledgeResetOnboarding = null)
    {
        this.workflow = workflow; this.viewModel = viewModel; this.downloads = downloads; this.updates = updates; this.scheduleSettingsStore = scheduleSettingsStore; this.scheduleRegistration = scheduleRegistration; this.acknowledgeResetOnboarding = acknowledgeResetOnboarding;
        this.reviewedHistory = reviewedHistory ?? throw new ArgumentNullException(nameof(reviewedHistory)); this.activityHistory = activityHistory ?? throw new ArgumentNullException(nameof(activityHistory)); this.probeReviewedRouteSplitToken = probeReviewedRouteSplitToken ?? throw new ArgumentNullException(nameof(probeReviewedRouteSplitToken));
        updateProgressTimer = DispatcherQueue.CreateTimer();
        updateProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        Closed += (_, _) => { windowClosed = true; reviewDecision?.TrySetResult(false); StopUpdateProgressPolling(); updateProgressMailbox = null; updateProgressPresentation = null; };
        Title = AppIdentity.DisplayName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "pc-steward.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));
        // Audit P-M9: a minimum window size, so pages reflow (wrapping action rows, stacked headers) instead of clipping.
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinimumWindowWidth;
            presenter.PreferredMinimumHeight = MinimumWindowHeight;
        }
        navigation = new ShellNavigation(showPostResetOnboarding ? ShellPage.Onboarding : ShellPage.Home);
        // Leaving the update job's Review step cancels that review: navigation never approves anything (FR-UPD-01).
        navigation.PropertyChanged += (_, _) => { SyncNavigationChrome(); if (navigation.Current != ShellPage.UpdateJob) CancelPendingReview(); else jobResultPending = false; };
        BuildWindowChrome();
        if (!showPostResetOnboarding) startupStateLoadFailed = !LoadSavedInventory();
        Render();
    }

    private bool LoadSavedInventory()
    {
        // FR-UPD-01 (independent review): a reload, rescan, Clear scan data or a finished update job ends any earlier choice on
        // Updates; nothing is preselected again.
        selectedReadyKey = null;
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
            LoadActivityHistory();
            // An open app detail page follows a decision made on it (the row's status and its timeline change).
            if (detailRow is { } open)
            {
                detailRow = viewModel.AllRows.SingleOrDefault(row => row.StableProductKey == open.StableProductKey) ?? open;
                detailTimeline = workflow.GetTimelineAsync(open.StableProductKey, CancellationToken.None).GetAwaiter().GetResult();
            }
        }); return true; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// FR-HIS-01 (Task 73 Phase 3): reads the bounded recorded history inside the caller's read lease. History is display only, so a
    /// failed read shows Activity as unavailable instead of blocking the rest of the window.
    /// </summary>
    private void LoadActivityHistory()
    {
        try
        {
            updateHistory = activityHistory.GetRecentUpdateHistoryAsync(ActivityHistoryLimit, CancellationToken.None).GetAwaiter().GetResult();
            scanRuns = activityHistory.GetRecentScanRunsAsync(ActivityHistoryLimit, CancellationToken.None).GetAwaiter().GetResult();
            activityHistoryUnavailable = false;
        }
        catch (Exception exception) when (exception is not InvalidOperationException and not OutOfMemoryException)
        {
            updateHistory = []; scanRuns = []; activityHistoryUnavailable = true;
        }
    }

    private async Task ShowDataOperationUnavailableAsync()
    {
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Action unavailable", Content = "PC Steward is finishing another task with its data, such as a reset. Wait for it to finish, then try again.", CloseButtonText = "Close" }.ShowAsync();
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
        ShellPage.UpdateJob => BuildUpdateJobPage(),
        _ => BuildHomePage()
    };

    private void RenderStartupStateLoadFailure()
    {
        StackPanel panel = CreatePagePanel(760);
        panel.Children.Add(StewardVisuals.PageTitle("PC Steward couldn't open its data"));
        panel.Children.Add(PageCopy("Another PC Steward task is still using its data, or a reset is still being finished. Nothing was scanned or changed. Close PC Steward and try again when the other task has finished."));
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

    private bool HasActiveOperation => scanCancellation is not null || viewModel.IsScanning || viewModel.IsCheckingUpdates || isExecuting || executionPending || isClearing || downloads.IsCleaning || preparingReview || isSavingSchedule;

    /// <summary>Leaving or cancelling the review clears it at once, so the next page is not built while it still counts as open.</summary>
    private void CancelPendingReview()
    {
        if (reviewDecision is not { } pending) return;
        reviewDecision = null;
        pending.TrySetResult(false);
    }

    private bool ShowsJobResultInBand => jobResultPending && !isExecuting && updateJob is { Completion: not null } && navigation.Current != ShellPage.UpdateJob;

    private const string PreparingReviewLabel = "Preparing review…";

    private string CurrentOperationLabel => viewModel.IsScanning
        ? $"Scanning this PC · {viewModel.ScanPhase} · {viewModel.Elapsed:mm\\:ss}"
        : scanCancellation is not null
            ? "Preparing the scan"
            : viewModel.IsCheckingUpdates
                ? "Checking package sources for updates"
                : isExecuting
                    ? updateProgressLabel
                    : isClearing || downloads.IsCleaning
                        ? "Clearing PC Steward data"
                        : isSavingSchedule
                            ? "Saving the schedule"
                        : preparingReview
                            ? PreparingReviewLabel
                            : ShowsJobResultInBand && updateJob is { Completion: { } completion } finished
                                ? $"{finished.Sheet.AppName}: {completion.Headline}"
                                : "Ready";

    private void RefreshOperationVisuals()
    {
        if (operationBand is not null) operationBand.Visibility = HasActiveOperation || ShowsJobResultInBand ? Visibility.Visible : Visibility.Collapsed;
        if (operationProgress is not null) operationProgress.Visibility = HasActiveOperation ? Visibility.Visible : Visibility.Collapsed;
        if (operationStatusText is not null)
        {
            // AC-UX-09: the band's text is a polite live region. It is raised only when the state changes (not on elapsed-time
            // ticks), so a screen reader hears "Scanning this PC · Windows registry" once, not every second.
            string label = CurrentOperationLabel;
            string state = OperationAnnouncements.StateKey(label);
            bool stateChanged = !string.Equals(state, lastOperationState, StringComparison.Ordinal);
            lastOperationState = state;
            StewardVisuals.SetLiveText(operationStatusText, label, announce: stateChanged && operationBand?.Visibility == Visibility.Visible);
        }
        if (operationCancel is not null) operationCancel.Visibility = viewModel.IsScanning || scanCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        if (scanPageStatusText is not null) scanPageStatusText.Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}";
        // Task 73 Phase 2: the job page's per-item state follows the same polled progress presentation; no page rebuild per frame.
        if (jobProgressStateText is not null && isExecuting) StewardVisuals.SetLiveText(jobProgressStateText, UpdateJobProgressText.ItemState(updateProgressPresentation), announce: true);
        if (operationViewJob is not null)
        {
            // Independent review: a job that finished elsewhere shows its result here with "View result"; it never moves the user.
            bool showResult = ShowsJobResultInBand;
            operationViewJob.Visibility = (isExecuting && updateJob is not null && navigation.Current != ShellPage.UpdateJob) || showResult ? Visibility.Visible : Visibility.Collapsed;
            string viewLabel = showResult ? "View result" : "View update progress";
            if (!string.Equals(operationViewJob.Content as string, viewLabel, StringComparison.Ordinal))
            {
                operationViewJob.Content = viewLabel;
                StewardVisuals.Named(operationViewJob, viewLabel);
            }
        }
    }

    /// <summary>
    /// AC-UX-09 (Task 73 Phase 5; research §2 item 10): announces a finished scan, update check, review, update job or save/clear as a
    /// UIA notification from the operation band's status text when the band is shown, otherwise from the page host. Focus never moves.
    /// </summary>
    private void AnnounceCompletion(string message, string activityId) =>
        StewardVisuals.Announce(operationBand is { Visibility: Visibility.Visible } ? operationStatusText : pageHost is not null ? pageHost : windowRoot, message, activityId);

    private StackPanel BuildOnboardingPage()
    {
        StackPanel panel = CreatePagePanel(760);
        panel.Children.Add(StewardVisuals.PageHeader("Start fresh with PC Steward", "Factory reset is complete. No scan or source connection was started automatically.", "Fresh local state"));
        panel.Children.Add(StewardVisuals.InformationBanner("PC Steward data was removed", "Inventory, source evidence, settings, decisions, history, app-owned downloads, logs, and the scheduled check were included in the reset.", "success"));
        panel.Children.Add(StewardVisuals.EmptyState("\uE721", "Build a new local inventory", "When you continue, PC Steward first verifies the fresh state and then opens the read-only inventory screen. It does not start the scan for you."));
        Button continueButton = StewardVisuals.PrimaryButton("Continue to local inventory", "after factory reset (local only)");
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
                // Blame-free message; the residual reasons and marker state stay available under "Technical details".
                await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset still needs attention", Content = StewardVisuals.MessageWithDetails("PC Steward couldn't confirm that the reset finished. Nothing was scanned. Close apps that might be using PC Steward's files, then try again.", details), CloseButtonText = "Close" }.ShowAsync();
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
        panel.Children.Add(StewardVisuals.PageHeader("About", "PC Steward lists the apps on this Windows 11 PC and helps you review and apply their updates. Everything stays on this PC."));
        panel.Children.Add(StewardVisuals.InformationBanner("Private, and you stay in control", "No account, cloud sync, usage tracking, always-running background service or automatic updates."));
        StackPanel scope = new() { Spacing = 8 };
        scope.Children.Add(SectionHeading("Updates and administrator permission"));
        scope.Children.Add(PageCopy(ElevationStory.Summary));
        scope.Children.Add(PageCopy(ElevationStory.HelperRequirement));
        panel.Children.Add(Card(scope));
        StackPanel data = new() { Spacing = 8 };
        data.Children.Add(SectionHeading("Data and removal"));
        data.Children.Add(PageCopy("Inventory, settings, decisions, history, source evidence, downloads and logs are stored locally under your PC Steward data root. Removing the extracted package removes its binaries but retains that data by default. Use Factory reset PC Steward to remove app-owned local data and the current-user scheduled check before removal."));
        panel.Children.Add(Card(data));
        panel.Children.Add(StewardVisuals.InformationBanner("Preview build", "This build is for private testing and is not a public release. Install PC Steward only from the private release channel you were given, and check its published checksum.", "attention"));
        StackPanel support = new() { Spacing = 8 };
        support.Children.Add(SectionHeading("Support and rollback"));
        support.Children.Add(PageCopy("If replacing PC Steward with a newer build fails, stop, copy the diagnostic from Settings › Privacy & exports, and use the rollback package from the same private release channel. Don't download PC Steward from mirrors or other download sites."));
        panel.Children.Add(Card(support));
        return panel;
    }

    /// <summary>
    /// FR-UI-01/05/06 (Task 73 Phase 3, ADR-030 concept A; audit P-H4, P-M4): one status sentence with freshness in words, a primary
    /// "Review updates" and a secondary "Check for updates" (which never elevates), the compact attention view, and tiles — real
    /// buttons — that open filtered pages. Every FR-UI-01 count appears once (hero or tile); nothing restates it.
    /// </summary>
    private StackPanel BuildHomePage()
    {
        IReadOnlyList<UpdateJobRecord> jobs = UpdateJobHistory.Group(updateHistory);
        IReadOnlyList<ActivityEntry> activity = ActivityPresenter.Entries(jobs, scanRuns, activeDecisions, manualMappings, viewModel.AllRows);
        HomePresentation home = HomePresenter.Build(new HomeInputs(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, viewModel.Sources,
            activeDecisions, [.. jobs], activity, viewModel.LastScanAt, DateTimeOffset.UtcNow));
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageTitle("Home"));

        if (!home.HasInventory)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "(read-only scan of this PC)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("", "See what is installed", "The first scan reads Windows application records on this PC. It does not request administrator access, connect to package sources, or change software.", start));
            panel.Children.Add(StewardVisuals.InformationBanner("Private by default", "Inventory and decisions stay on this PC. Package sources remain off until you approve their disclosure."));
            return panel;
        }

        // The status hero: one sentence, freshness in words (never colour alone), one primary and one secondary action.
        StackPanel hero = new() { Spacing = StewardSpacing.Medium };
        TextBlock sentence = new() { Text = home.Hero, Style = AppStyle("StewardTitleStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(sentence, AutomationHeadingLevel.Level2);
        hero.Children.Add(sentence);
        StewardWrapPanel freshness = StewardVisuals.ActionRow();
        Border freshnessChip = StewardVisuals.Badge(home.Freshness.Word, home.Freshness.Freshness == EvidenceFreshness.Live ? "" : "", StewardStatus.ToneName(home.Freshness.Tone), $"Update evidence: {home.Freshness.Word}");
        freshnessChip.VerticalAlignment = VerticalAlignment.Center;
        freshness.Children.Add(freshnessChip);
        freshness.Children.Add(new TextBlock { Text = home.Freshness.Sentence, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 720 });
        hero.Children.Add(freshness);
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        Button review = StewardVisuals.PrimaryButton(home.PrimaryLabel);
        // The presenter's name already starts with the visible label (HomeActivityDecisionsPresentationTests: label-in-name).
        AutomationProperties.SetName(review, home.PrimaryAccessibleName);
        review.AccessKey = "R";
        review.Click += (_, _) => NavigateTo(ShellPage.Updates);
        // Independent review L1: with sources off this is "Scan again" (CheckForUpdatesAsync then scans only).
        string checkLabel = viewModel.IsScanning ? "Scanning…" : viewModel.IsCheckingUpdates ? "Checking…" : home.SecondaryLabel;
        Button check = new() { Content = checkLabel, IsEnabled = !HasActiveOperation, AccessKey = AccessKeyFor(home.SecondaryLabel) };
        if (checkLabel == home.SecondaryLabel) AutomationProperties.SetName(check, home.SecondaryAccessibleName); else StewardVisuals.Named(check, checkLabel);
        AutomationProperties.SetHelpText(check, home.SecondaryHelpText);
        ToolTipService.SetToolTip(check, home.SecondaryHelpText);
        check.Click += async (_, _) => await CheckForUpdatesAsync();
        actions.Children.Add(review);
        actions.Children.Add(check);
        // The last scan's results page stays reachable from Home (audit P-H2), as a quiet link rather than a third button.
        HyperlinkButton results = new() { Content = "Open scan results", VerticalAlignment = VerticalAlignment.Center };
        StewardVisuals.Named(results, "Open scan results");
        results.Click += (_, _) => NavigateTo(ShellPage.Results);
        actions.Children.Add(results);
        hero.Children.Add(actions);
        if (home.SecondaryCaption.Length != 0)
            hero.Children.Add(new TextBlock { Text = home.SecondaryCaption, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Card(hero));

        // FR-UI-06: the compact attention view — failed or unverified updates and pending restarts, newest first.
        if (home.Attention.Count != 0)
        {
            StackPanel attention = new() { Spacing = StewardSpacing.Small };
            attention.Children.Add(SectionHeading("Needs your attention"));
            foreach (HomeAttentionItem item in home.Attention)
            {
                Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel copy = new() { Spacing = StewardSpacing.XSmall };
                // Independent review L6 (ADR-021 best effort): a Grid has no automation peer, so the line's name goes on its first TextBlock.
                TextBlock appName = new() { Text = item.AppName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetName(appName, item.AccessibleName);
                copy.Children.Add(appName);
                copy.Children.Add(new TextBlock { Text = $"{item.Summary} · {item.When}", Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
                line.Children.Add(copy);
                Border chip = StewardVisuals.Badge(item.OutcomeLabel, item.Glyph, StewardStatus.ToneName(item.Tone), $"{item.AppName}: {item.OutcomeLabel}");
                chip.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(chip, 1); line.Children.Add(chip);
                attention.Children.Add(line);
            }
            // Independent review L3: the filter opened includes every kind of item listed (Updates when a restart is pending).
            Button openActivity = new() { Content = "View in Activity" };
            AutomationProperties.SetName(openActivity, home.AttentionLinkAccessibleName);
            ActivityFilter attentionFilter = home.AttentionActivityFilter;
            openActivity.Click += (_, _) => { activityFilter = attentionFilter; NavigateTo(ShellPage.Activity); };
            attention.Children.Add(openActivity);
            panel.Children.Add(Card(attention));
        }

        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Update history unavailable", "PC Steward couldn't read its recorded update history just now, so restart and attention counts may be incomplete. Nothing was changed.", "neutral"));

        // Tiles: real buttons whose accessible names start with the visible title and say where they lead.
        Grid tiles = new() { ColumnSpacing = StewardSpacing.Medium, RowSpacing = StewardSpacing.Medium };
        const int TileColumns = 3;
        for (int column = 0; column < TileColumns; column++) tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int index = 0; index < home.Tiles.Count; index++)
        {
            if (index % TileColumns == 0) tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Button tile = HomeTileButton(home.Tiles[index]);
            Grid.SetColumn(tile, index % TileColumns); Grid.SetRow(tile, index / TileColumns);
            tiles.Children.Add(tile);
        }
        panel.Children.Add(tiles);
        return panel;
    }

    /// <summary>A Home tile: a real Button (keyboard, Narrator and focus visuals for free) that opens its filtered page.</summary>
    private Button HomeTileButton(HomeTile tile)
    {
        (Brush foreground, _) = StewardVisuals.ToneBrushes(StewardStatus.ToneName(tile.Tone));
        StackPanel content = new() { Spacing = StewardSpacing.XSmall };
        StackPanel heading = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        heading.Children.Add(new FontIcon { Glyph = tile.Glyph, FontSize = 16, Foreground = foreground });
        heading.Children.Add(new TextBlock { Text = tile.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = tile.Value, Style = AppStyle("StewardSubtitleStyle"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = tile.Description, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        Button button = new()
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(StewardSpacing.Large)
        };
        AutomationProperties.SetName(button, tile.AccessibleName);
        HomeTileTarget target = tile.Target;
        button.Click += (_, _) =>
        {
            // Opening a tile only navigates with a filter; it never scans, checks, approves or updates anything.
            if (target.AppsFilter is { } appsFilter) viewModel.SetSavedFilter(appsFilter);
            if (target.ActivityFilter is { } filter) activityFilter = filter;
            NavigateTo(target.Page);
        };
        return button;
    }

    /// <summary>
    /// FR-UI-01, AC-UX-03 (Task 73 Phase 3; ADR-030 decision 5; audit P-L7): saved filters (All, Updates, Needs review, Unmanaged,
    /// Pinned &amp; ignored) with counts over every saved app, search, and the detailed filters behind "More filters". The list stays
    /// virtualized with a bounded height inside the page host. Repository apps stays reachable from the header.
    /// </summary>
    private StackPanel BuildInstalledAppsPage()
    {
        StackPanel panel = CreatePagePanel();
        Button scan = StewardVisuals.ActionButton("Scan again", "(read-only scan of this PC)");
        scan.IsEnabled = !HasActiveOperation;
        scan.AccessKey = "S";
        scan.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
        Button repositoryApps = StewardVisuals.ActionButton("Repository apps");
        repositoryApps.Click += (_, _) => NavigateTo(ShellPage.RepositoryApps);
        StewardWrapPanel headerActions = StewardVisuals.ActionRow();
        headerActions.Children.Add(repositoryApps);
        headerActions.Children.Add(scan);
        panel.Children.Add(StewardVisuals.PageHeader("Installed apps", "Search and inspect the local inventory. Filtering never changes saved data.", null, headerActions));
        panel.Children.Add(StewardVisuals.InformationBanner("Last scan", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "information"));

        // Saved filters: the same words as the status chips, and counts over every saved app ("All" really is all).
        // Audit P-M9: filter rows wrap instead of scrolling sideways or clipping.
        StewardWrapPanel savedFilters = StewardVisuals.ActionRow();
        AutomationProperties.SetName(savedFilters, "Saved filters");
        foreach (InstalledAppsFilterOption option in InstalledAppsFilters.Options(viewModel.AllRows))
        {
            bool selected = viewModel.SavedFilter == option.Filter;
            // The selected filter stays focusable (accent style plus "Selected" item status) so a refresh can restore focus to it.
            Button filter = selected ? StewardVisuals.PrimaryButton(option.DisplayLabel) : new Button { Content = option.DisplayLabel };
            AutomationProperties.SetName(filter, option.AccessibleName);
            if (selected) AutomationProperties.SetItemStatus(filter, "Selected");
            InstalledAppsFilter value = option.Filter;
            filter.Click += (_, _) => { viewModel.SetSavedFilter(value); Render(); };
            savedFilters.Children.Add(filter);
        }
        panel.Children.Add(savedFilters);
        if (viewModel.SavedFilter == InstalledAppsFilter.Unmanaged)
            panel.Children.Add(StewardVisuals.InformationBanner("Unmanaged isn't unsafe", StatusVocabulary.For(nameof(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged)).Meaning, "neutral"));

        TextBox search = new() { Header = "Search", PlaceholderText = "Name or publisher", Text = viewModel.SearchText };
        AutomationProperties.SetName(search, "Search applications");
        // Ctrl+F focuses this box (AC-UX-01; research §2 item 3).
        pageSearchTarget = search;
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
        bool detailedFiltersInUse = viewModel.StatusFilter != StatusVocabulary.AllStatusesLabel || viewModel.PublisherFilter != "All publishers" || viewModel.SourceFilter != "All sources" || viewModel.ScopeFilter != "All scopes" || viewModel.SortField != "Name";
        Expander moreFilters = new() { Header = "More filters", Content = filters, IsExpanded = moreAppFiltersExpanded || detailedFiltersInUse, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        moreFilters.Expanding += (_, _) => moreAppFiltersExpanded = true;
        moreFilters.Collapsed += (_, _) => moreAppFiltersExpanded = false;
        filterContent.Children.Add(moreFilters);
        panel.Children.Add(Card(filterContent));
        // Bounded height keeps the list virtualized inside the page's own scroll host (wheel scrolling chains to the page at the ends).
        ListView list = new() { Style = AppStyle("StewardListViewStyle"), ItemContainerStyle = AppStyle("StewardListItemStyle"), ItemTemplate = (DataTemplate)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardApplicationRowTemplate"], MaxHeight = 620 };
        TextBlock matching = PageCopy(string.Empty);
        AutomationProperties.SetLiveSetting(matching, AutomationLiveSetting.Polite);
        Border empty = StewardVisuals.EmptyState("", "No applications match", "Adjust the search or filters. The saved inventory has not changed.");
        void ApplyFilters()
        {
            viewModel.SetFilters(search.Text, StatusVocabulary.FilterKeyForLabel(status.SelectedItem?.ToString()), publisher.SelectedItem?.ToString(), FilterRawFor(source.SelectedItem?.ToString(), sourceOptions, "All sources"), FilterRawFor(scope.SelectedItem?.ToString(), scopeOptions, "All scopes"), sort.SelectedItem?.ToString());
            RenderInventory(list);
            matching.Text = $"{DisplayFormat.Plural(viewModel.VisibleRows.Count, "application")} {(viewModel.VisibleRows.Count == 1 ? "matches" : "match")} {InstalledAppsFilters.Label(viewModel.SavedFilter)} and the current search.";
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
        TextBlock applicationHeader = new() { Text = "Application", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(applicationHeader, 1); columns.Children.Add(applicationHeader);
        TextBlock versionHeader = new() { Text = "Version and source", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(versionHeader, 2); columns.Children.Add(versionHeader);
        TextBlock statusHeader = new() { Text = "Status", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(statusHeader, 3); columns.Children.Add(statusHeader);
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
        panel.Children.Add(StewardVisuals.PageHeader("Repository apps", "Installed apps that a package source you turned on can identify exactly."));
        panel.Children.Add(StewardVisuals.InformationBanner("Package sources", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        Button refresh = StewardVisuals.ActionButton(viewModel.IsCheckingUpdates ? "Checking package sources…" : "Refresh package sources");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting;
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        actions.Children.Add(refresh);
        if (!viewModel.IsUpdateSourceEnabled)
        {
            Button settings = StewardVisuals.ActionButton("Open source settings");
            settings.Click += (_, _) => NavigateTo(ShellPage.Settings);
            actions.Children.Add(settings);
        }
        panel.Children.Add(actions);

        if (managedRows.Count == 0)
        {
            string empty = viewModel.IsUpdateSourceEnabled
                ? "No installed app is identified exactly by your package sources yet. Refresh package sources to update this view; nothing is downloaded or updated."
                : "Package sources are off, so PC Steward can't tell which apps they manage or whether they're up to date. Turn on trusted sources in Settings, then refresh when you choose.";
            panel.Children.Add(StewardVisuals.EmptyState("\uE73E", "No repository apps yet", empty));
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
                heading.Children.Add(SectionHeading(row.Name, AutomationHeadingLevel.Level3));
                Border badge = StewardVisuals.StatusBadge(row.Status); Grid.SetColumn(badge, 1); heading.Children.Add(badge);
                current.Children.Add(heading);
                current.Children.Add(PageCopy(row.Publisher));
                current.Children.Add(StewardVisuals.SelectableText($"Installed version: {row.Version}{(detail.CandidateVersion is null || detail.CandidateVersion == row.Version ? string.Empty : $" · source version: {detail.CandidateVersion}")}"));
                current.Children.Add(StewardVisuals.SelectableText($"Source: {detail.SourceId} · Package ID: {detail.PackageId}"));
                current.Children.Add(PageCopy(detail.Freshness));
                Button evidence = StewardVisuals.ActionButton("View details", $"for {row.Name}");
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
        panel.Children.Add(StewardVisuals.PageHeader("Scan results", "What the last scan found, grouped by what you can do next. Nothing updates from this page.", "Last scan"));
        panel.Children.Add(StewardVisuals.InformationBanner("Last scan", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "success"));
        StewardWrapPanel filters = StewardVisuals.ActionRow();
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
            Button filter = resultsFilter == option ? StewardVisuals.PrimaryButton(label, "scan results") : StewardVisuals.ActionButton(label, "scan results");
            if (resultsFilter == option) AutomationProperties.SetItemStatus(filter, "Selected");
            filter.Click += (_, _) => { resultsFilter = option; Render(); };
            filters.Children.Add(filter);
        }
        panel.Children.Add(filters);
        InventoryRow[] currentIdentityCandidates = viewModel.AllRows
            .Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString())
            .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)
            .ToArray();
        // Audit P-M3: one verb for one action — this and the Review page both "Confirm" current package identities.
        Button confirmAllCurrent = StewardVisuals.ActionButton(
            currentIdentityCandidates.Length == 0 ? "Confirm all current package identities" : $"Confirm all {currentIdentityCandidates.Length} current package identities",
            "that the source reports as already installed");
        confirmAllCurrent.IsEnabled = currentIdentityCandidates.Length > 0;
        confirmAllCurrent.Click += async (_, _) => await ConfirmCurrentPackageIdentitiesAsync(currentIdentityCandidates);
        panel.Children.Add(confirmAllCurrent);
        if (currentIdentityCandidates.Length == 0) panel.Children.Add(PageCopy("No package identities are waiting to be confirmed as current."));
        InventoryRow[] resultRows = viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "FailedUnavailable" or "Unmanaged")
            .Where(row => resultsFilter switch
            {
                ResultsFilter.Actionable => row.Status == "VerifiedUpdate",
                ResultsFilter.NeedsDecision => row.Status == "ReviewRequired",
                ResultsFilter.Unavailable => row.Status == "FailedUnavailable",
                ResultsFilter.Unmanaged => row.Status == "Unmanaged",
                _ => true
            }).ToArray();
        if (resultRows.Length == 0) panel.Children.Add(StewardVisuals.EmptyState("\uE711", "No results match this view", "Choose another filter, or open Installed apps to see everything."));
        foreach (InventoryRow row in resultRows)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel finding = new() { Spacing = StewardSpacing.XSmall };
            finding.Children.Add(SectionHeading(row.Name, AutomationHeadingLevel.Level3));
            finding.Children.Add(StewardVisuals.StatusBadge(row.Status, $"{row.Name}: {StewardStatus.From(row.Status).Label}"));
            finding.Children.Add(PageCopy($"{StewardPresentation.Provider(row.Provider)} · {StewardPresentation.Scope(row.Scope)}"));
            finding.Children.Add(PageCopy(row.Status switch
            {
                "VerifiedUpdate" => "View the details, then review the update on the Updates page. Nothing updates until you confirm it.",
                "ReviewRequired" => "Check the details and choose what to do. It can't be updated until the match is resolved.",
                "FailedUnavailable" => "The package source couldn't confirm whether an update is available. Refresh source evidence to try again; there is no decision or update to approve.",
                _ => "Found on this PC. No package source manages it, which isn't a safety problem."
            }));
            if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired))
            {
                AddReviewDecisionActions(finding, row, detail);
            }
            else if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable))
            {
                finding.Children.Add(PageCopy(detail?.ActionState ?? "Refresh source evidence to retry. No update starts."));
                StewardWrapPanel actions = StewardVisuals.ActionRow();
                if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled) actions.Children.Add(CreateSourceEvidenceRefreshButton(row));
                Button open = StewardVisuals.ActionButton("View details", $"for {row.Name} (update evidence unavailable)"); open.Click += async (_, _) => await ShowDetailsAsync(row); actions.Children.Add(open);
                finding.Children.Add(actions);
            }
            else
            {
                Button open = StewardVisuals.ActionButton("View details", $"for {row.Name}"); open.Click += async (_, _) => await ShowDetailsAsync(row); finding.Children.Add(open);
            }
            panel.Children.Add(Card(finding));
        }
        if (downloadCache.RecoverableBytes > 0)
        {
            Button storage = StewardVisuals.ActionButton($"Open storage: {DisplayFormat.Size(downloadCache.RecoverableBytes)} of downloaded files can be cleared"); storage.Click += (_, _) => NavigateTo(ShellPage.Storage); panel.Children.Add(storage);
        }
        Button review = StewardVisuals.ActionButton("Review decisions"); review.Click += (_, _) => NavigateTo(ShellPage.Review); panel.Children.Add(review);
        return panel;
    }

    private StackPanel BuildReviewPage()
    {
        StackPanel panel = CreatePagePanel();
        InventoryRow[] ready = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString()).ToArray();
        InventoryRow[] decisions = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString()).ToArray();
        panel.Children.Add(StewardVisuals.PageHeader("Review decisions", "Resolve package matches first. Updates that are ready stay separate, and each still needs your confirmation on its review page."));
        panel.Children.Add(StewardVisuals.InformationBanner("Reviewing doesn't update anything", "Viewing details or confirming a package identity never starts an update."));
        panel.Children.Add(SectionHeading("Needs a decision"));
        panel.Children.Add(PageCopy(decisions.Length == 0 ? "No package matches need a decision." : $"{DisplayFormat.Plural(decisions.Length, "item")} {(decisions.Length == 1 ? "needs" : "need")} a decision. None of them can be updated until the decision is made."));
        foreach (InventoryRow row in decisions)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel decision = new() { Spacing = StewardSpacing.XSmall };
            decision.Children.Add(SectionHeading(row.Name, AutomationHeadingLevel.Level3));
            decision.Children.Add(StewardVisuals.StatusBadge(row.Status));
            decision.Children.Add(StewardVisuals.SelectableText($"{detail?.SourceId ?? StewardPresentation.Provider(row.Provider)} · {detail?.PackageId ?? "package ID unknown"}"));
            decision.Children.Add(PageCopy(detail?.Explanation ?? "The available evidence needs review before any update can be considered."));
            AddReviewDecisionActions(decision, row, detail);
            panel.Children.Add(Card(decision));
        }
        InventoryRow[] currentIdentityCandidates = decisions
            .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)
            .ToArray();
        if (currentIdentityCandidates.Length > 0)
        {
            panel.Children.Add(PageCopy($"{DisplayFormat.Plural(currentIdentityCandidates.Length, "item")} can be confirmed together, because the package source reports the installed version is the same as the available version."));
            Button confirmAll = StewardVisuals.ActionButton("Confirm all current package identities", "that the source reports as already installed");
            confirmAll.Click += async (_, _) => await ConfirmCurrentPackageIdentitiesAsync(currentIdentityCandidates);
            panel.Children.Add(confirmAll);
        }
        // Audit P-M3: nothing is called "approved" before the user approves it on the review page.
        panel.Children.Add(SectionHeading("Ready to review"));
        panel.Children.Add(PageCopy(ready.Length == 0 ? "No updates are ready to review. Cached, unavailable, low-confidence or conflicting matches can't be updated." : $"{DisplayFormat.Plural(ready.Length, "update")} can be reviewed. Each one still needs your confirmation on its review page."));
        foreach (InventoryRow row in ready)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel action = new() { Spacing = StewardSpacing.XSmall };
            action.Children.Add(SectionHeading($"{row.Name} {row.Version} → {detail?.CandidateVersion ?? "new version unknown"}", AutomationHeadingLevel.Level3));
            action.Children.Add(StewardVisuals.StatusBadge(row.Status));
            action.Children.Add(StewardVisuals.SelectableText($"{detail?.SourceId ?? "Unknown source"} · {StewardPresentation.Scope(row.Scope)} · {detail?.Architecture ?? "Unknown architecture"}"));
            Button open = StewardVisuals.ActionButton("View details", $"for {row.Name}"); open.Click += async (_, _) => await ShowDetailsAsync(row); action.Children.Add(open);
            panel.Children.Add(Card(action));
        }
        panel.Children.Add(PageCopy("PC Steward updates one app at a time and checks the installed version before it records an update as successful."));
        return panel;
    }

    private void AddReviewDecisionActions(StackPanel container, InventoryRow row, UpdateDetail? detail)
    {
        container.Children.Add(PageCopy(detail?.ActionState ?? "You can pin the installed version, ignore the app, or view its details."));
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled)
        {
            actions.Children.Add(CreateSourceEvidenceRefreshButton(row));
        }
        if (detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true } && viewModel.IsUpdateSourceEnabled)
        {
            Button confirm = StewardVisuals.PrimaryButton("Confirm package identity", $"({detail.PackageId} is {row.Name})");
            confirm.Click += async (_, _) => await PromptAndConfirmPackageIdentityAsync(row, detail);
            actions.Children.Add(confirm);
        }
        else if (detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0 && viewModel.IsUpdateSourceEnabled)
        {
            Button choose = StewardVisuals.PrimaryButton("Choose package identity", $"for {row.Name}");
            choose.Click += async (_, _) => await ShowDetailsAsync(row);
            actions.Children.Add(choose);
        }
        Button pin = StewardVisuals.ActionButton("Pin current version", $"of {row.Name} ({row.Version})");
        pin.Click += async (_, _) => await PromptAndPinCurrentVersionAsync(row);
        actions.Children.Add(pin);
        Button ignore = StewardVisuals.ActionButton("Ignore…", $"{row.Name}: choose how long");
        ignore.Click += async (_, _) => await PromptAndIgnoreProductAsync(row);
        actions.Children.Add(ignore);
        Button open = StewardVisuals.ActionButton("View details", $"for {row.Name}");
        open.Click += async (_, _) => await ShowDetailsAsync(row);
        actions.Children.Add(open);
        container.Children.Add(actions);
    }

    private Button CreateSourceEvidenceRefreshButton(InventoryRow row)
    {
        Button refresh = StewardVisuals.PrimaryButton(
            viewModel.IsCheckingUpdates ? "Checking source evidence…" : "Refresh source evidence",
            $"for {row.Name}");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting;
        AutomationProperties.SetHelpText(refresh, "Checks your package sources only. It doesn't download or install an update.");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        return refresh;
    }

    private async Task PromptAndConfirmPackageIdentityAsync(InventoryRow row, UpdateDetail detail) =>
        await PromptAndCreateManualMappingAsync(row, detail.SourceId!, detail.PackageId!);

    /// <summary>
    /// FR-POL-03/05 (Task 73 Phase 3; audit P-M1): the one confirmation for recording a package identity, used by the lists, the
    /// review page and every mapping action on app details. It records a reversible mapping after validation; it never approves or
    /// starts an update. The reason is read on the UI thread before the data-operation lease starts.
    /// </summary>
    private async Task PromptAndCreateManualMappingAsync(InventoryRow row, string sourceId, string packageId)
    {
        TextBox reason = DecisionReasonBox("Optional reason for confirming this package identity");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Confirm only if you recognise {packageId} from {sourceId} as {row.Name}. This records a reversible mapping. It does not approve or start an update."));
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
        try { validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, sourceId, packageId, viewModel.IsUpdateSourceEnabled, decisionReason, token), CancellationToken.None); }
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
        CheckBox ignoreUntilEnabled = new() { Content = "Ignore until a selected date" };
        DatePicker ignoreUntil = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) };
        ignoreUntilEnabled.Checked += (_, _) => ignoreUntil.IsEnabled = true;
        ignoreUntilEnabled.Unchecked += (_, _) => ignoreUntil.IsEnabled = false;
        TextBox reason = DecisionReasonBox("Optional reason for ignoring this product");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Ignore {row.Name} indefinitely, or select an end date below. You can reverse this decision on the Decisions page. No application is changed."));
        content.Children.Add(ignoreUntilEnabled);
        content.Children.Add(ignoreUntil);
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
        DateTimeOffset? ignoredUntil = ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null;
        string decisionReason = reason.Text;
        try { await DataOperationLease.RunAsync(token => workflow.IgnoreAsync(row.StableProductKey, ignoredUntil, decisionReason, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Product ignored", ignoredUntil is null ? $"{row.Name} is ignored until you reverse the decision. No application was changed." : $"{row.Name} is ignored until {DisplayFormat.Date(ignoredUntil.Value)}. No application was changed.");
    }

    private static TextBox DecisionReasonBox(string placeholder) => new() { Header = "Optional reason", PlaceholderText = placeholder, MaxLength = 512 };

    private async Task ConfirmCurrentPackageIdentitiesAsync(IReadOnlyCollection<InventoryRow> candidates)
    {
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirm current package identities?",
            Content = "This confirms the package identity only for the listed apps whose package source reports the installed version is the same as the available version. Newer, unknown, unavailable or conflicting matches are skipped. You can undo each one on the Decisions page. No update will start.",
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
        StackPanel panel = CreatePagePanel(1000);
        panel.Children.Add(StewardVisuals.PageHeader("Storage", "Space used by PC Steward's own records and downloaded update files.", "PC Steward data"));
        panel.Children.Add(StewardVisuals.InformationBanner("Only PC Steward's own files", "This page never covers Windows temporary files, other apps' caches, your documents, drivers, firmware or installed apps."));
        StackPanel summary = new() { Spacing = StewardSpacing.Small };
        foreach (DetailFact fact in DataResetPresenter.StorageSummary(DataResetInputsNow())) summary.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        summary.Children.Add(PageCopy("Source metadata, logs and update history have their own cards in Data & reset."));
        Button privacy = StewardVisuals.ActionButton("Open Data & reset");
        privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        summary.Children.Add(privacy);
        panel.Children.Add(StewardVisuals.SettingsCard("\uEDA2", "PC Steward storage", null, summary));
        return panel;
    }

    /// <summary>
    /// FR-HIS-01–03 (Task 73 Phase 3, ADR-030): a filterable history (All, Updates, Scans, Problems) grouped by day. Each update shows
    /// its Phase 2 outcome, start and end time, provider, package ID and target version; Details shows the exit code, the failure
    /// class and the sanitized recorded output. The Task 50 reviewed-outcomes section (route, verification, running installers,
    /// helper report) follows unchanged. Read-only: nothing here deletes, exports or starts anything.
    /// </summary>
    private StackPanel BuildActivityPage()
    {
        StackPanel panel = CreatePagePanel();
        Button privacy = StewardVisuals.ActionButton("Data & reset", "(view and clear local data)");
        privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        panel.Children.Add(StewardVisuals.PageHeader("Activity", "Scans, updates and decisions recorded on this PC. PC Steward does not upload activity telemetry.", null, privacy));
        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Recorded history unavailable", "PC Steward couldn't read its recorded update and scan history just now. Nothing was changed; try again later.", "neutral"));

        IReadOnlyList<ActivityEntry> entries = ActivityPresenter.Entries(UpdateJobHistory.Group(updateHistory), scanRuns, activeDecisions, manualMappings, viewModel.AllRows);
        if (entries.Count == 0)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "(read-only scan of this PC)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("", "No local activity yet", "Complete an inventory or save a decision and its timestamped record will appear here.", start));
        }
        else
        {
            ActivityPagePresentation page = ActivityPresenter.Build(entries, activityFilter, DateTimeOffset.UtcNow);
            StewardWrapPanel filters = StewardVisuals.ActionRow();
            AutomationProperties.SetName(filters, "Activity filters");
            foreach (ActivityFilterOption option in page.Filters)
            {
                bool selected = page.Filter == option.Filter;
                // The selected filter stays focusable (accent style plus "Selected" item status) so a refresh can restore focus to it.
                Button filter = selected ? StewardVisuals.PrimaryButton(option.DisplayLabel) : new Button { Content = option.DisplayLabel };
                AutomationProperties.SetName(filter, option.AccessibleName);
                if (selected) { AutomationProperties.SetItemStatus(filter, "Selected"); pageSearchTarget = filter; }
                ActivityFilter value = option.Filter;
                filter.Click += (_, _) => { activityFilter = value; Render(); };
                filters.Children.Add(filter);
            }
            // Activity has filters rather than a search box; Ctrl+F focuses the selected filter.
            panel.Children.Add(filters);
            TextBlock summary = PageCopy(page.Summary);
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
            panel.Children.Add(summary);
            if (page.MatchingCount == 0)
                panel.Children.Add(StewardVisuals.EmptyState("", "Nothing here", page.Filter == ActivityFilter.Problems ? "No failed or unverified updates and no incomplete scans are recorded." : "Choose another filter to see more of the recorded activity."));
            foreach (ActivityDay day in page.Days)
            {
                panel.Children.Add(SectionHeading(day.Heading));
                StackPanel dayEntries = new() { Spacing = 0 };
                foreach (ActivityEntry entry in day.Entries) dayEntries.Children.Add(ActivityEntryRow(entry));
                panel.Children.Add(Card(dayEntries));
            }
        }
        AddReviewedUpdateOutcomesSection(panel);
        panel.Children.Add(StewardVisuals.InformationBanner("Package sources", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "neutral"));
        return panel;
    }

    /// <summary>One Activity entry: title and outcome chip (icon and text), time, summary, facts, and Details behind an expander.</summary>
    private static Border ActivityEntryRow(ActivityEntry entry)
    {
        StackPanel content = new() { Spacing = StewardSpacing.XSmall };
        Grid heading = new() { ColumnSpacing = StewardSpacing.Medium };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel titles = new() { Spacing = StewardSpacing.XSmall };
        // Independent review L6 (ADR-021 best effort): a Border has no automation peer, so the entry's name goes on its title TextBlock.
        TextBlock title = new() { Text = entry.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(title, entry.AccessibleName);
        titles.Children.Add(title);
        titles.Children.Add(new TextBlock { Text = entry.TimeText, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(titles);
        Border chip = StewardVisuals.Badge(entry.OutcomeLabel, entry.Glyph, StewardStatus.ToneName(entry.Tone), $"{entry.Title}: {entry.OutcomeLabel}");
        chip.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(chip, 1); heading.Children.Add(chip);
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = entry.Summary, TextWrapping = TextWrapping.Wrap });
        foreach (DetailFact fact in entry.Facts) content.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        if (entry.Details.Count != 0)
        {
            StackPanel details = new() { Spacing = 0 };
            foreach (DetailFact fact in entry.Details)
            {
                Grid line = StewardVisuals.EvidenceRow(fact.Label, fact.Value);
                // Evidence can be selected and copied (for example the recorded output).
                foreach (TextBlock text in line.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = true;
                details.Children.Add(line);
            }
            content.Children.Add(new Expander { Header = "Details", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        return new Border { BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, StewardSpacing.Medium, 0, StewardSpacing.Medium), Child = content };
    }

    /// <summary>Admission obligation 5: the read-only reviewed-update outcomes (route, verification, lingering processes, helper report).</summary>
    private void AddReviewedUpdateOutcomesSection(StackPanel panel)
    {
        panel.Children.Add(SectionHeading(ReviewedUpdateHistoryListViewModel.Heading));
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

    /// <summary>The Data &amp; reset subpage under Settings (Task 73 Phase 4; SPEC §17.3).</summary>
    private StackPanel BuildPrivacyPage()
    {
        StackPanel panel = CreatePagePanel(1000);
        panel.Children.Add(StewardVisuals.PageHeader("Data & reset", "What PC Steward stores on this PC, how much space it uses, and separate controls to remove it. Exports are in Settings › Privacy & exports.", "Settings"));
        AddDataAndResetSection(panel);
        return panel;
    }

    /// <summary>FR-POL-01–05 (ADR-030 foundation 7): pins, ignores and confirmed package identities in one reversible view.</summary>
    private StackPanel BuildDecisionsPage()
    {
        StackPanel panel = CreatePagePanel(900);
        panel.Children.Add(StewardVisuals.PageHeader("Decisions", "Pins, ignores and package identities you confirmed. Each one is reversible, and none of them starts an update."));
        AddDecisionsSection(panel);
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
        titleBarBack.AccessKey = "B";
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
        AddWindowShortcuts(windowRoot);

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

    /// <summary>
    /// AC-UX-01/09, FR-UPD-01 (Task 73 Phase 5; research §2 items 2–3): window shortcuts. F6 and Shift+F6 move focus between the
    /// navigation pane, the page and the operation band; Ctrl+F focuses the page's search or filters; F5 starts the same read-only
    /// scan (and, when sources are on, source check) as the visible "Scan again"/"Check for updates" button, and only when
    /// <see cref="ShortcutGuard.CanStartScan"/> allows it — never during active work, an open review or a dialog. No shortcut can
    /// confirm a review, approve or start an update, or skip a confirmation.
    /// </summary>
    private void AddWindowShortcuts(UIElement root)
    {
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        KeyboardAccelerator nextRegion = new() { Key = Windows.System.VirtualKey.F6 };
        nextRegion.Invoked += (_, args) => args.Handled = CycleFocusRegion(backwards: false);
        KeyboardAccelerator previousRegion = new() { Key = Windows.System.VirtualKey.F6, Modifiers = Windows.System.VirtualKeyModifiers.Shift };
        previousRegion.Invoked += (_, args) => args.Handled = CycleFocusRegion(backwards: true);
        KeyboardAccelerator search = new() { Key = Windows.System.VirtualKey.F, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        search.Invoked += (_, args) => args.Handled = FocusPageSearch();
        KeyboardAccelerator scan = new() { Key = Windows.System.VirtualKey.F5 };
        scan.Invoked += async (_, args) => { args.Handled = true; await ScanShortcutAsync(); };
        root.KeyboardAccelerators.Add(nextRegion);
        root.KeyboardAccelerators.Add(previousRegion);
        root.KeyboardAccelerators.Add(search);
        root.KeyboardAccelerators.Add(scan);
    }

    /// <summary>F5: the guarded read-only scan. It calls only <see cref="CheckForUpdatesAsync"/>, which re-checks the guard itself.</summary>
    private async Task ScanShortcutAsync()
    {
        if (!ShortcutGuard.CanStartScan(shellShown: navigationView is not null, HasActiveOperation, reviewOpen: reviewDecision is not null, dialogOpen: IsPopupOpen())) return;
        await CheckForUpdatesAsync();
    }

    private bool IsPopupOpen() => Content?.XamlRoot is { } root && VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count > 0;

    private bool FocusPageSearch()
    {
        if (IsPopupOpen() || pageSearchTarget is not { } target || !target.Focus(FocusState.Keyboard)) return false;
        if (target is TextBox box) box.SelectAll();
        return true;
    }

    /// <summary>F6 / Shift+F6: the next available region (<see cref="FocusRegionCycle"/>); a region that cannot take focus is skipped.</summary>
    private bool CycleFocusRegion(bool backwards)
    {
        if (navigationView is null || pageHost is null || Content?.XamlRoot is null || IsPopupOpen()) return false;
        List<FocusRegion> available = [FocusRegion.Navigation, FocusRegion.Page];
        if (operationBand is { Visibility: Visibility.Visible }) available.Add(FocusRegion.OperationBand);
        FocusRegion? current = CurrentFocusRegion();
        for (int attempt = 0; attempt < available.Count; attempt++)
        {
            if (FocusRegionCycle.Next(current, available, backwards) is not { } next) return false;
            if (FocusRegionElement(next)) return true;
            current = next;
        }
        return false;
    }

    private FocusRegion? CurrentFocusRegion()
    {
        if (Content?.XamlRoot is null || FocusManager.GetFocusedElement(Content.XamlRoot) is not DependencyObject focused) return null;
        for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (ReferenceEquals(node, operationBand)) return FocusRegion.OperationBand;
            if (ReferenceEquals(node, pageHost)) return FocusRegion.Page;
            if (ReferenceEquals(node, navigationView)) return FocusRegion.Navigation;
        }
        return null;
    }

    private bool FocusRegionElement(FocusRegion region)
    {
        if (navigationView is null || pageHost is null) return false;
        Control? target = region switch
        {
            FocusRegion.Navigation => navigationView.SelectedItem as Control ?? navigationView.MenuItems.OfType<Control>().FirstOrDefault(),
            FocusRegion.Page => pageHost.Content is DependencyObject page ? FocusManager.FindFirstFocusableElement(page) as Control : null,
            _ => operationBand?.Child is DependencyObject band ? FocusManager.FindFirstFocusableElement(band) as Control : null
        };
        return target?.Focus(FocusState.Keyboard) == true;
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
        operationProgress = progress;
        Button cancel = StewardVisuals.ActionButton("Cancel scan", "in progress");
        cancel.Visibility = viewModel.IsScanning || scanCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        cancel.Click += (_, _) => scanCancellation?.Cancel();
        Grid.SetColumn(cancel, 2);
        operation.Children.Add(cancel);
        operationCancel = cancel;
        // Task 73 Phase 2: the update job is non-modal; from any page the band leads back to its progress. It never cancels it.
        operation.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button viewJob = StewardVisuals.ActionButton("View update progress");
        viewJob.Visibility = Visibility.Collapsed;
        viewJob.Click += (_, _) => NavigateTo(ShellPage.UpdateJob);
        Grid.SetColumn(viewJob, 3);
        operation.Children.Add(viewJob);
        operationViewJob = viewJob;
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
        pageInitialFocus = null;
        pageSearchTarget = null;
        StackPanel page = BuildPage(navigation.Current);
        // A page may name the control that takes focus when it is opened, for example Cancel on the update review (Cancel is the default).
        Control? initialFocus = sameView ? null : pageInitialFocus;
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
                if (!focusWasInPage && initialFocus is null) return;
                // Independent review M3: restore only an unambiguous match; otherwise start at the top of the page.
                Control? target = (sameView && focusKey is not null ? PageFocusRestore.UniqueMatch(FindFocusTargets(page, focusKey)) : null)
                    ?? initialFocus
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
        panel.Children.Add(StewardVisuals.PageHeader("Inventory scan", viewModel.IsScanning || scanCancellation is not null ? "PC Steward is reading the apps installed on this PC. Your last completed scan stays available until this one finishes." : viewModel.LastScanAt is null ? "Start a private, read-only scan. Nothing is installed, removed or changed, and no administrator permission is requested." : "Check what the scan covers before replacing your last completed scan.", "Read only"));
        // Status text, not a heading: the operation band's live region announces state changes.
        scanPageStatusText = new TextBlock { Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}", Style = AppStyle("StewardSubtitleStyle") };
        StackPanel state = new() { Spacing = 8 };
        state.Children.Add(scanPageStatusText);
        state.Children.Add(StewardVisuals.EvidenceRow("Saved inventory", viewModel.LastScanAt is null ? "No completed scan" : viewModel.LastScanCompletedLabel));
        state.Children.Add(StewardVisuals.EvidenceRow("Selected scope", viewModel.ScanModeLabel));
        state.Children.Add(StewardVisuals.EvidenceRow("Administrator access", "Not requested"));
        panel.Children.Add(Card(state));
        if (viewModel.LastScanWarnings.Count != 0) panel.Children.Add(StewardVisuals.InformationBanner("Some app sources couldn't be read fully", string.Join(" ", viewModel.LastScanWarnings) + " Data from your last completed scan is kept where available.", "attention"));
        if (!viewModel.IsScanning && scanCancellation is null)
        {
            StackPanel scope = new() { Spacing = 8 };
            scope.Children.Add(SectionHeading("What the scan covers"));
            scope.Children.Add(new CheckBox { Content = "Windows application inventory (required · read-only)", IsChecked = true, IsEnabled = false });
            ToggleSwitch winget = new() { Header = "WinGet and Chocolatey trusted sources", IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Local only", OnContent = "Enabled after disclosure" };
            AutomationProperties.SetName(winget, "WinGet and Chocolatey trusted sources"); winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn); scope.Children.Add(winget);
            scope.Children.Add(PageCopy("Enabling these sources may disclose installed package identifiers to WinGet and Chocolatey Community. Scanning remains read-only and never asks for administrator permission; an update still needs its own review and confirmation."));
            panel.Children.Add(Card(scope));
        }
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        // Label in name (audit §5): the accessible name starts with the visible "Start inventory" / "Replace saved inventory".
        Button scan = StewardVisuals.PrimaryButton(viewModel.LastScanAt is null ? "Start inventory" : "Replace saved inventory", "(read-only scan of this PC)");
        scan.IsEnabled = !HasActiveOperation;
        scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning || scanCancellation is not null) { Button cancel = StewardVisuals.ActionButton("Cancel scan"); cancel.AccessKey = "C"; cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(StewardVisuals.InformationBanner("A scan changes nothing", "A scan never approves, downloads or installs an update. Package sources stay off until you turn them on."));
        return panel;
    }

    /// <summary>
    /// FR-UPD-01, FR-POL-01–05, FR-UI-04 (Task 73 Phase 2, ADR-030 decision 2): Updates grouped into Ready, Needs review and
    /// Ignored and skipped by the portable <see cref="UpdatesPagePresenter"/>. Only Ready rows get a selection control and
    /// nothing is preselected; Needs review rows show the reason in words and have no update control at all (SPEC §7.3).
    /// Choosing a row only enables the review button, and the review always opens the structured review sheet first.
    /// </summary>
    private StackPanel BuildUpdatesPage()
    {
        StackPanel panel = CreatePagePanel(960);
        UpdatesPagePresentation page = UpdatesPagePresenter.Build(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, activeDecisions, selectedReadyKey, DateTimeOffset.UtcNow);
        selectedReadyKey = page.SelectedKey;
        Button refresh = StewardVisuals.ActionButton(viewModel.IsCheckingUpdates ? "Checking package sources…" : "Refresh package sources");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !HasActiveOperation;
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        Button reviewDecisions = StewardVisuals.ActionButton("Review decisions");
        reviewDecisions.Click += (_, _) => NavigateTo(ShellPage.Review);
        StewardWrapPanel headerActions = StewardVisuals.ActionRow();
        headerActions.Children.Add(reviewDecisions);
        headerActions.Children.Add(refresh);
        panel.Children.Add(StewardVisuals.PageHeader("Updates", "Choose an update that's ready, review its details, then decide. Nothing updates until you confirm it.", "Your approval is required", headerActions));
        panel.Children.Add(StewardVisuals.InformationBanner(viewModel.IsUpdateSourceEnabled ? "Package sources" : "Package sources are off", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));
        if (updateJob is { Step: UpdateJobStep.Update or UpdateJobStep.Verify } job)
        {
            Button view = StewardVisuals.ActionButton(job.Step == UpdateJobStep.Update ? "View update progress" : "View update result");
            view.Click += (_, _) => NavigateTo(ShellPage.UpdateJob);
            panel.Children.Add(StewardVisuals.InformationBanner(job.Step == UpdateJobStep.Update ? $"Updating {job.Sheet.AppName}" : $"Update finished: {job.Sheet.AppName}",
                job.Step == UpdateJobStep.Update ? UpdateJobProgressText.LeaveNote : job.Completion?.Headline ?? "No outcome recorded"));
            panel.Children.Add(view);
        }
        if (!page.HasAnyRow)
        {
            Button applications = StewardVisuals.ActionButton("Open Installed apps");
            applications.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            panel.Children.Add(StewardVisuals.EmptyState("", "Nothing needs update review", "Updates appear here only when a package source you turned on gives a fresh, exact and compatible match.", applications));
            return panel;
        }

        // Ready: the only group that can be selected. One app per reviewed job, so the choice is single (radio) and starts empty.
        panel.Children.Add(SectionHeading($"Ready ({page.Ready.Count})"));
        panel.Children.Add(PageCopy(page.Ready.Count == 0
            ? "No updates are ready. Updates appear here only when the evidence is fresh, exact and compatible."
            : "Choose one app to review. Choosing doesn't start anything: PC Steward reviews and updates one app per job, and you confirm it on the next step."));
        foreach (UpdateRowPresentation ready in page.Ready)
        {
            Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            RadioButton choose = new() { GroupName = "ready-updates", IsChecked = ready.StableProductKey == page.SelectedKey, Content = ready.Name };
            StewardVisuals.Named(choose, ready.Name, $"{ready.VersionText}: choose to review");
            string key = ready.StableProductKey;
            choose.Checked += (_, _) => { if (selectedReadyKey == key) return; selectedReadyKey = key; Render(); };
            line.Children.Add(choose);
            Border chip = StewardVisuals.StatusBadge(ready.Status, $"{ready.Name}: {StewardStatus.From(ready.Status).Label}");
            chip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chip, 1); line.Children.Add(chip);
            Button more = UpdateRowOverflow(ready); Grid.SetColumn(more, 2); line.Children.Add(more);
            StackPanel body = new() { Spacing = StewardSpacing.XSmall, Margin = new Thickness(StewardSpacing.XLarge, 0, 0, 0) };
            StewardWrapPanel versions = StewardVisuals.ActionRow();
            versions.Children.Add(new TextBlock { Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true });
            if (ready.Badge is { } badge)
            {
                Border jump = StewardVisuals.Badge(badge.Label, badge.Glyph, StewardStatus.ToneName(badge.Tone), badge.AccessibleName);
                if (badge.IsHighlighted) jump.BorderThickness = new Thickness(2);
                versions.Children.Add(jump);
            }
            body.Children.Add(versions);
            body.Children.Add(PageCopy(ready.Publisher));
            if (!string.IsNullOrWhiteSpace(ready.Freshness)) body.Children.Add(new TextBlock { Text = ready.Freshness, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
            StackPanel card = new() { Spacing = StewardSpacing.XSmall };
            card.Children.Add(line);
            card.Children.Add(body);
            panel.Children.Add(Card(card));
        }
        if (page.Ready.Count > 0)
        {
            string reviewLabel = preparingReview ? PreparingReviewLabel : page.ReviewButtonLabel;
            Button review = StewardVisuals.PrimaryButton(reviewLabel);
            // Opens the review sheet only; the update needs its own confirmation there (FR-UPD-01).
            review.AccessKey = "R";
            review.IsEnabled = page.CanReview && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
            AutomationProperties.SetHelpText(review, "Opens the review. Nothing is updated until you confirm on the review page.");
            review.Click += async (_, _) => { if (page.Selected is { } chosen && viewModel.AllRows.SingleOrDefault(row => row.StableProductKey == chosen.StableProductKey) is { } row) await ReviewAndExecuteAsync(row); };
            panel.Children.Add(review);
        }

        // Needs review: the reason in words and no update control at all — not a disabled one.
        panel.Children.Add(SectionHeading($"Needs review ({page.NeedsReview.Count})"));
        panel.Children.Add(PageCopy(page.NeedsReview.Count == 0
            ? "Nothing needs review."
            : "These can't be updated from here. Low-confidence, conflicting, stale, unavailable or incomparable evidence is shown so you can see why."));
        foreach (UpdateRowPresentation needsReview in page.NeedsReview)
        {
            Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new() { Spacing = StewardSpacing.XSmall };
            identity.Children.Add(SectionHeading(needsReview.Name, AutomationHeadingLevel.Level3));
            identity.Children.Add(StewardVisuals.SelectableText($"{needsReview.Publisher} · {needsReview.VersionText}"));
            line.Children.Add(identity);
            Border chip = StewardVisuals.StatusBadge(needsReview.Status, $"{needsReview.Name}: {StewardStatus.From(needsReview.Status).Label}");
            chip.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(chip, 1); line.Children.Add(chip);
            Button more = UpdateRowOverflow(needsReview); Grid.SetColumn(more, 2); line.Children.Add(more);
            StackPanel card = new() { Spacing = StewardSpacing.XSmall };
            card.Children.Add(line);
            TextBlock reason = new() { Text = $"Why: {needsReview.ReasonText}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            AutomationProperties.SetName(reason, $"Why {needsReview.Name} needs review: {needsReview.ReasonText}");
            card.Children.Add(reason);
            if (!string.IsNullOrWhiteSpace(needsReview.Freshness)) card.Children.Add(new TextBlock { Text = needsReview.Freshness, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(Card(card));
        }

        // Ignored and skipped: collapsed by default and reversible from here or from Decisions.
        StackPanel ignoredRows = new() { Spacing = StewardSpacing.Small };
        ignoredRows.Children.Add(PageCopy(page.IgnoredAndSkipped.Count == 0
            ? "You haven't ignored or pinned any apps."
            : "You chose to ignore or pin these apps. Each choice can be reversed here or on the Decisions page."));
        foreach (UpdateRowPresentation ignored in page.IgnoredAndSkipped)
        {
            Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new() { Spacing = StewardSpacing.XSmall };
            identity.Children.Add(new TextBlock { Text = ignored.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            identity.Children.Add(PageCopy(ignored.ReasonText));
            line.Children.Add(identity);
            Border chip = StewardVisuals.StatusBadge(ignored.Status, $"{ignored.Name}: {StewardStatus.From(ignored.Status).Label}");
            chip.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(chip, 1); line.Children.Add(chip);
            Button more = UpdateRowOverflow(ignored); Grid.SetColumn(more, 2); line.Children.Add(more);
            ignoredRows.Children.Add(line);
        }
        Expander ignoredGroup = new() { Header = $"Ignored and skipped ({page.IgnoredAndSkipped.Count})", Content = ignoredRows, IsExpanded = ignoredGroupExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ignoredGroup.Expanding += (_, _) => ignoredGroupExpanded = true;
        ignoredGroup.Collapsed += (_, _) => ignoredGroupExpanded = false;
        panel.Children.Add(ignoredGroup);
        return panel;
    }

    /// <summary>
    /// FR-POL-01–05 (ADR-030 decision 2): a row's overflow menu. Every entry uses an existing decision operation and its existing
    /// confirmation: ignore the app, pin the installed version, open details, or reverse a recorded pin or ignore. There is no
    /// "Skip this version": PC Steward has no such decision, so none is invented.
    /// </summary>
    private Button UpdateRowOverflow(UpdateRowPresentation presentation)
    {
        Button more = new() { Content = new FontIcon { Glyph = "", FontSize = 16 }, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(more, $"More options for {presentation.Name}");
        ToolTipService.SetToolTip(more, "More options");
        MenuFlyout menu = new();
        foreach (UpdateRowActionPresentation action in presentation.Actions)
        {
            MenuFlyoutItem item = new() { Text = action.Label, Icon = new FontIcon { Glyph = action.Glyph } };
            AutomationProperties.SetName(item, $"{action.Label}: {presentation.Name}");
            item.IsEnabled = !HasActiveOperation || action.Action == UpdateRowAction.Details;
            item.Click += async (_, _) =>
            {
                if (viewModel.AllRows.SingleOrDefault(row => row.StableProductKey == presentation.StableProductKey) is not { } row) return;
                switch (action.Action)
                {
                    case UpdateRowAction.IgnoreApp: await PromptAndIgnoreProductAsync(row); break;
                    case UpdateRowAction.PinCurrentVersion: await PromptAndPinCurrentVersionAsync(row); break;
                    case UpdateRowAction.Details: await ShowDetailsAsync(row); break;
                    case UpdateRowAction.Reverse when presentation.DecisionId is { } decisionId:
                        try { await DataOperationLease.RunAsync(token => workflow.ReverseDecisionAsync(decisionId, token), CancellationToken.None); LoadSavedInventory(); Render(); }
                        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
                        break;
                }
            };
            menu.Items.Add(item);
        }
        more.Flyout = menu;
        return more;
    }

    /// <summary>
    /// FR-UPD-01/04–09, FR-UI-03/04, FR-DAT-09, UJ-04 (Task 73 Phase 2, ADR-030 decision 3): the update job stepper, used only for
    /// the job — Review → Update → Verify. Review shows the structured sheet for the reviewed immutable selection with Cancel
    /// focused first; Update is non-modal progress with no cancel; Verify shows the honest outcomes, evidence, restart guidance
    /// and Delete downloaded files. The page only presents; ReviewAndExecuteAsync owns the review, the approval and the run.
    /// </summary>
    private StackPanel BuildUpdateJobPage()
    {
        StackPanel panel = CreatePagePanel(820);
        jobProgressStateText = null;
        if (updateJob is not { } job)
        {
            panel.Children.Add(StewardVisuals.PageTitle("Update"));
            Button open = StewardVisuals.ActionButton("Open Updates");
            open.Click += (_, _) => NavigateTo(ShellPage.Updates);
            panel.Children.Add(StewardVisuals.EmptyState("", "No update is open", "Choose an app that's ready on the Updates page to review its update.", open));
            return panel;
        }
        panel.Children.Add(StewardVisuals.PageHeader(job.Step switch
        {
            UpdateJobStep.Review => job.Sheet.Title,
            UpdateJobStep.Update => $"Updating {job.Sheet.AppName}",
            _ => job.Completion?.Headline ?? "No outcome recorded"
        }, job.Sheet.AppName));
        panel.Children.Add(BuildJobStepIndicator(job.Step));
        switch (job.Step)
        {
            case UpdateJobStep.Review: AddReviewStep(panel, job); break;
            case UpdateJobStep.Update: AddUpdateStep(panel, job); break;
            default: AddVerifyStep(panel, job); break;
        }
        return panel;
    }

    private static Grid BuildJobStepIndicator(UpdateJobStep current)
    {
        Grid steps = new() { ColumnSpacing = StewardSpacing.Medium };
        AutomationProperties.SetName(steps, "Update steps");
        int column = 0;
        foreach (UpdateJobStepPresentation step in UpdateJobSteps.For(current))
        {
            steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string tone = step.State switch { UpdateJobStepState.Completed => "success", UpdateJobStepState.Current => "information", _ => "neutral" };
            string glyph = step.State == UpdateJobStepState.Completed ? "" : step.State == UpdateJobStepState.Current ? "" : "";
            Border entry = StewardVisuals.Badge($"{step.Number} {step.Label}", glyph, tone, step.AccessibleName);
            entry.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (step.State == UpdateJobStepState.Current) entry.BorderThickness = new Thickness(2);
            Grid.SetColumn(entry, column++);
            steps.Children.Add(entry);
        }
        return steps;
    }

    private void AddReviewStep(StackPanel panel, UpdateJobSession job)
    {
        UpdateReviewSheet sheet = job.Sheet;
        bool refused = job.Completion is { ReturnsToReview: true };
        if (refused)
        {
            // A refused UAC prompt returns quietly here. The approved job was used up by that attempt, so trying again starts a
            // new review of fresh evidence; nothing was changed and it is not a failure.
            panel.Children.Add(StewardVisuals.InformationBanner("Nothing was changed", job.Completion!.Items[0].Outcome.Summary + " " + job.Completion.Items[0].Outcome.NextStep, "neutral"));
        }
        else panel.Children.Add(PageCopy(UpdateReviewSheet.Intro));

        StackPanel facts = new() { Spacing = 0 };
        TextBlock factsHeading = SectionHeading("What will be updated");
        factsHeading.Margin = new Thickness(0, 0, 0, StewardSpacing.Small);
        facts.Children.Add(factsHeading);
        foreach (ReviewFact fact in sheet.Facts) facts.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        panel.Children.Add(Card(facts));

        StackPanel confidence = new() { Spacing = StewardSpacing.Small };
        confidence.Children.Add(SectionHeading("How sure PC Steward is"));
        confidence.Children.Add(new TextBlock { Text = sheet.ConfidenceSummary, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        StackPanel evidence = new() { Spacing = StewardSpacing.XSmall };
        foreach (string line in sheet.ConfidenceEvidence) evidence.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        confidence.Children.Add(new Expander { Header = "Evidence", Content = evidence, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        panel.Children.Add(Card(confidence));

        foreach (ReviewRiskCallout risk in sheet.Risks)
            panel.Children.Add(StewardVisuals.InformationBanner(risk.Title, risk.Message, StewardStatus.ToneName(risk.Tone)));

        StackPanel fineprint = new() { Spacing = StewardSpacing.Small };
        fineprint.Children.Add(new TextBlock { Text = sheet.Attestation, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        fineprint.Children.Add(new TextBlock { Text = sheet.CoverageDisclosure, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        // Independent review: the attestation and coverage disclosure are what the user confirms, so they start expanded.
        panel.Children.Add(new Expander { Header = "What you're confirming", Content = fineprint, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        if (refused || reviewDecision is not { } decision)
        {
            Button again = StewardVisuals.PrimaryButton(preparingReview ? PreparingReviewLabel : "Start a new review", $"of the update for {sheet.AppName}");
            again.IsEnabled = !HasActiveOperation;
            again.Click += async (_, _) => await ReviewAndExecuteAsync(job.Row);
            Button leave = StewardVisuals.ActionButton("Back to Updates");
            leave.Click += (_, _) => { updateJob = null; jobResultPending = false; NavigateTo(ShellPage.Updates); };
            actions.Children.Add(leave);
            actions.Children.Add(again);
            panel.Children.Add(actions);
            return;
        }
        // Cancel is the default: it comes first and takes focus, and the update button has no Enter accelerator.
        Button cancel = StewardVisuals.ActionButton(UpdateReviewSheet.CancelButtonLabel, $"the review of {sheet.AppName}. Nothing is changed.");
        StackPanel primaryContent = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        if (sheet.ShowsAdministratorShield) primaryContent.Children.Add(new FontIcon { Glyph = "", FontSize = 16 });
        primaryContent.Children.Add(new TextBlock { Text = sheet.PrimaryButtonLabel });
        Button confirm = new() { Content = primaryContent, Style = AppStyle("StewardPrimaryButtonStyle") };
        AutomationProperties.SetName(confirm, sheet.PrimaryAutomationName);
        AutomationProperties.SetHelpText(confirm, sheet.AdministratorRequirement.Explanation);
        // Independent review: once the decision is made (for example the update was confirmed and is about to start), a rebuilt
        // sheet shows both buttons disabled, and Cancel can no longer navigate away. Confirm also waits for any active operation.
        bool decided = decision.Task.IsCompleted;
        cancel.IsEnabled = !decided;
        confirm.IsEnabled = !HasActiveOperation && !decided;
        cancel.Click += (_, _) =>
        {
            cancel.IsEnabled = false; confirm.IsEnabled = false;
            if (!decision.TrySetResult(false)) return;
            // Clear the review before navigating so the page shown next has its Review button enabled again.
            if (ReferenceEquals(reviewDecision, decision)) reviewDecision = null;
            if (navigation.CanGoBack) GoBack(); else NavigateTo(ShellPage.Updates);
        };
        confirm.Click += (_, _) =>
        {
            cancel.IsEnabled = false; confirm.IsEnabled = false;
            if (decision.Task.IsCompleted) return;
            // Mark execution pending before completing the decision, so no other operation can start in between.
            executionPending = true;
            bool accepted = decision.TrySetResult(true);
            if (!accepted) executionPending = false;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        pageInitialFocus = cancel;
    }

    private void AddUpdateStep(StackPanel panel, UpdateJobSession job)
    {
        panel.Children.Add(PageCopy(UpdateJobProgressText.LeaveNote));
        StackPanel item = new() { Spacing = StewardSpacing.Small };
        item.Children.Add(SectionHeading(UpdateJobProgressText.Heading(job.Sheet.ItemCount)));
        item.Children.Add(StewardVisuals.EvidenceRow(job.Sheet.AppName, job.Sheet.Facts.Single(fact => fact.Label == "Version").Value));
        jobProgressStateText = new TextBlock { Text = UpdateJobProgressText.ItemState(updateProgressPresentation), TextWrapping = TextWrapping.Wrap };
        // A polite live region: its text (not a fixed name) is what a screen reader hears when the state changes.
        AutomationProperties.SetLiveSetting(jobProgressStateText, AutomationLiveSetting.Polite);
        item.Children.Add(jobProgressStateText);
        ProgressBar progress = new() { IsIndeterminate = true, Height = 4 };
        AutomationProperties.SetName(progress, $"{job.Sheet.AppName} update progress");
        item.Children.Add(progress);
        panel.Children.Add(Card(item));
        if (job.Sheet.ShowsAdministratorShield)
            panel.Children.Add(StewardVisuals.InformationBanner(job.Sheet.AdministratorRequirement.Label, job.Sheet.AdministratorRequirement.Explanation, "information"));
    }

    private void AddVerifyStep(StackPanel panel, UpdateJobSession job)
    {
        UpdateJobCompletion completion = job.Completion ?? new UpdateJobCompletion([]);
        foreach (UpdateJobItemOutcome item in completion.Items)
        {
            UpdateOutcomePresentation outcome = item.Outcome;
            StackPanel card = new() { Spacing = StewardSpacing.Small };
            Grid heading = new() { ColumnSpacing = StewardSpacing.Medium };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(SectionHeading(item.AppName));
            Border badge = StewardVisuals.Badge(outcome.Label, outcome.Glyph, StewardStatus.ToneName(outcome.Tone), outcome.AccessibleName(item.AppName));
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 1); heading.Children.Add(badge);
            card.Children.Add(heading);
            card.Children.Add(StewardVisuals.EvidenceRow("Version", $"{item.InstalledVersion} → {item.TargetVersion}"));
            card.Children.Add(StewardVisuals.EvidenceRow("Package ID", item.PackageId));
            card.Children.Add(new TextBlock { Text = outcome.Summary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            card.Children.Add(PageCopy(outcome.NextStep));
            card.Children.Add(new Expander { Header = "Technical details", Content = new TextBlock { Text = outcome.TechnicalDetail, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            if (outcome.Kind is UpdateOutcomeKind.Failed or UpdateOutcomeKind.NotStarted)
            {
                Button again = StewardVisuals.ActionButton("Review again", $"the update for {item.AppName}");
                again.IsEnabled = !HasActiveOperation;
                again.Click += async (_, _) => await ReviewAndExecuteAsync(job.Row);
                card.Children.Add(again);
            }
            panel.Children.Add(Card(card));
        }
        panel.Children.Add(StewardVisuals.InformationBanner(completion.RestartRequired ? "Restart to finish" : "Restart", completion.RestartGuidance, completion.RestartRequired ? "attention" : "neutral"));

        // FR-DAT-09 / UJ-04 step 9: Delete downloaded files on the completion view, through the existing confirmation and cleanup.
        StackPanel cleanup = new() { Spacing = StewardSpacing.Small };
        cleanup.Children.Add(SectionHeading(UpdateJobCleanupText.Heading));
        cleanup.Children.Add(PageCopy(UpdateJobCleanupText.Recoverable(downloadCache.RecoverableBytes)));
        Button delete = StewardVisuals.ActionButton("Delete downloaded files", "after update completion");
        delete.IsEnabled = downloadCache.RecoverableBytes > 0 && !HasActiveOperation;
        delete.Click += async (_, _) => await ConfirmAndClearDownloadedFilesAsync("Delete downloaded files after update completion?");
        cleanup.Children.Add(delete);
        panel.Children.Add(Card(cleanup));

        Button done = StewardVisuals.PrimaryButton("Done", "(close the update result and return to Updates)");
        done.Click += (_, _) => { updateJob = null; jobResultPending = false; NavigateTo(ShellPage.Updates); };
        panel.Children.Add(done);
    }

    /// <summary>
    /// Task 73 Phase 4 (ADR-030 concept A; audit P-M7, §6 items 9–10; FR-DAT-01/03, FR-HIS-05): Settings as SettingsCard groups in one
    /// column (Update sources, Scan scope, Schedule, Decisions, Privacy &amp; exports, Data &amp; reset, About) with one save model —
    /// every control saves as soon as it changes, except the Schedule card, which changes Windows Task Scheduler and so applies only
    /// through its one "Save schedule" button (<see cref="SettingsPresenter.SaveModel"/>). The source consent toggle keeps its own
    /// disclosure and turning it on starts nothing; data controls live on the Data &amp; reset subpage.
    /// </summary>
    private StackPanel BuildSettingsPage()
    {
        StackPanel panel = CreatePagePanel(1000);
        panel.Children.Add(StewardVisuals.PageHeader("Settings", "Choose what PC Steward looks at, when it checks, and what it keeps on this PC.", "Preferences"));
        panel.Children.Add(PageCopy(SettingsPresenter.SaveModelText));

        // Update sources: the consent toggle stays separate, with its existing disclosure dialog before first use.
        StackPanel sources = new() { Spacing = StewardSpacing.Small };
        ToggleSwitch winget = new() { IsOn = viewModel.IsWinGetTrustedSourcesEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(winget, "WinGet and Chocolatey trusted sources");
        winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn);
        sources.Children.Add(StewardVisuals.SettingsRow("WinGet and Chocolatey trusted sources", SettingsPresenter.SourceConsentNote, winget));
        sources.Children.Add(StewardVisuals.EvidenceRow("Current evidence", viewModel.UpdateFreshnessLabel));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Update sources", "Optional package sources that tell PC Steward which updates exist. They are off until you approve them.", sources));

        // Scan scope / AppX.
        StackPanel scope = new() { Spacing = StewardSpacing.Small };
        CheckBox local = new() { Content = "Always on", IsChecked = true, IsEnabled = false };
        StewardVisuals.Named(local, "Always on", "(Windows local inventory, required, local only)");
        scope.Children.Add(StewardVisuals.SettingsRow("Windows local inventory", "Installed apps that Windows reports for this PC. Always on, read-only and never shared.", local));
        ConnectorOption appx = viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog");
        ToggleSwitch includeAppx = new() { IsOn = appx.IsEnabled, OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(includeAppx, "Include current-user AppX/MSIX catalog");
        includeAppx.Toggled += async (_, _) => await ChangeConnectorAsync(appx.Name, includeAppx.IsOn);
        scope.Children.Add(StewardVisuals.SettingsRow("Include current-user AppX/MSIX catalog", "Store and packaged apps installed for your account. Read locally; nothing is shared.", includeAppx));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Scan scope", "What a scan reads. Scanning never asks for administrator permission and never changes anything.", scope));

        // Schedule: nothing applies until "Save schedule", which saves the whole record through the register-then-save operation.
        ScheduleCardPresentation plan = SettingsPresenter.Schedule(scheduledSettings, scheduledStatus, isSavingSchedule, otherWorkActive: HasActiveOperation);
        StackPanel schedule = new() { Spacing = StewardSpacing.Small };
        RadioButtons cadence = new() { SelectedIndex = plan.CadenceIndex, IsEnabled = plan.IsCadenceEnabled };
        foreach (string option in SettingsPresenter.CadenceOptions) cadence.Items.Add(option);
        AutomationProperties.SetName(cadence, "Scheduled check cadence");
        TimePicker startTime = new() { Time = plan.LocalStartTime.ToTimeSpan(), IsEnabled = plan.IsTimeEnabled };
        AutomationProperties.SetName(startTime, "Local start time");
        ComboBox weeklyDay = new() { IsEnabled = plan.IsWeekdayEnabled, MinWidth = 160 };
        // Audit P-M6: localized day names; the selection stays index-based (DayOfWeek order), so what is saved is unchanged.
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>()) weeklyDay.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(day));
        weeklyDay.SelectedIndex = (int)plan.WeeklyDay;
        AutomationProperties.SetName(weeklyDay, "Weekly day");
        ToggleSwitch scheduleEnabled = new() { IsOn = plan.IsEnabled, IsEnabled = plan.IsEnableToggleEnabled, OffContent = "Not scheduled", OnContent = "On · runs for your account only, no background service" };
        AutomationProperties.SetName(scheduleEnabled, "Enable scheduled checks");
        ToggleSwitch notifications = new() { IsOn = plan.NotificationsEnabled, IsEnabled = plan.IsNotificationsToggleEnabled, OffContent = "Off", OnContent = "Attention and repeated scan failures only" };
        AutomationProperties.SetName(notifications, "Windows notifications");
        TextBlock scheduleSummary = new() { Text = isSavingSchedule ? "Saving…" : plan.Summary, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(scheduleSummary, AutomationLiveSetting.Polite);
        Button saveSchedule = StewardVisuals.PrimaryButton(SettingsPresenter.SaveScheduleLabel);
        saveSchedule.AccessKey = "S";
        saveSchedule.IsEnabled = plan.IsSaveEnabled;
        Control[] scheduleControls = [cadence, startTime, weeklyDay, scheduleEnabled, notifications, saveSchedule];
        async Task SaveScheduleAsync()
        {
            if (isSavingSchedule) return;
            if (HasActiveOperation) { scheduleSummary.Text = DataResetPresenter.ActiveWorkReason; return; }
            // Fail closed: no cadence or weekday selection means nothing is saved.
            ScheduledCheckSettings? requested = SettingsPresenter.RequestedSchedule(cadence.SelectedIndex, scheduleEnabled.IsOn, notifications.IsOn, weeklyDay.SelectedIndex, startTime.Time);
            if (requested is null) { scheduleSummary.Text = SettingsPresenter.ScheduleNotChosen; return; }
            foreach (Control control in scheduleControls) control.IsEnabled = false;
            scheduleSummary.Text = "Saving…";
            await ApplyScheduleAsync(requested);
        }
        // Handlers are attached after the saved values are set. Only "Save schedule" saves; the cadence handler changes which
        // controls are available and never saves, registers or re-renders anything.
        cadence.SelectionChanged += (_, _) =>
        {
            if (isSavingSchedule) return;
            (bool time, bool weekday, bool enable) = SettingsPresenter.ScheduleControlAvailability(cadence.SelectedIndex, busy: false);
            startTime.IsEnabled = time;
            weeklyDay.IsEnabled = weekday;
            scheduleEnabled.IsEnabled = enable;
        };
        saveSchedule.Click += async (_, _) => await SaveScheduleAsync();
        schedule.Children.Add(scheduleSummary);
        schedule.Children.Add(StewardVisuals.SettingsRow("Check cadence", "Manual only means PC Steward checks only when you start a scan.", cadence));
        schedule.Children.Add(StewardVisuals.SettingsRow("Local start time", "Used for daily and weekly checks.", startTime));
        schedule.Children.Add(StewardVisuals.SettingsRow("Weekly day", "Used for weekly checks.", weeklyDay));
        schedule.Children.Add(StewardVisuals.SettingsRow("Enable scheduled checks", "Creates a current-user scheduled check. Unavailable with Manual only.", scheduleEnabled));
        schedule.Children.Add(StewardVisuals.SettingsRow("Windows notifications", "Only when something needs your attention or scans keep failing.", notifications));
        schedule.Children.Add(saveSchedule);
        foreach (DetailFact fact in plan.Status) schedule.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Schedule", SettingsPresenter.ScheduleDescription, schedule));

        StackPanel decisions = new() { Spacing = StewardSpacing.Small };
        Button openDecisions = StewardVisuals.ActionButton("Open Decisions");
        openDecisions.Click += (_, _) => NavigateTo(ShellPage.Decisions);
        decisions.Children.Add(StewardVisuals.SettingsRow(activeDecisions.Count == 0 && manualMappings.Count == 0 ? "No active pins, ignores or confirmed package identities." : $"{activeDecisions.Count + manualMappings.Count:N0} active decision{(activeDecisions.Count + manualMappings.Count == 1 ? string.Empty : "s")}", "Review or reverse them on the Decisions page.", openDecisions));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Decisions", "Pins, ignores and package identities you confirmed. Each one is reversible.", decisions));

        // Privacy & exports (FR-DAT-01/03) and Copy diagnostic (FR-HIS-05).
        StackPanel privacy = new() { Spacing = StewardSpacing.Small };
        privacy.Children.Add(StewardVisuals.InformationBanner("Exports can identify this PC", SettingsPresenter.ExportWarning, "attention"));
        StewardWrapPanel exportActions = StewardVisuals.ActionRow();
        Button exportCsv = StewardVisuals.ActionButton("Export inventory CSV"); exportCsv.Click += async (_, _) => await SaveExportAsync(false); exportActions.Children.Add(exportCsv);
        Button exportJson = StewardVisuals.ActionButton("Export inventory JSON"); exportJson.Click += async (_, _) => await SaveExportAsync(true); exportActions.Children.Add(exportJson);
        privacy.Children.Add(StewardVisuals.SettingsRow("Export inventory", "Saves a file only where you choose. Nothing is uploaded.", exportActions));
        DiagnosticCopyPresentation diagnostic = SettingsPresenter.DiagnosticCopy(activityHistoryUnavailable);
        Button copyDiagnostic = new() { Content = diagnostic.Label, IsEnabled = diagnostic.IsEnabled };
        AutomationProperties.SetName(copyDiagnostic, diagnostic.AccessibleName);
        TextBlock copied = new() { Text = diagnostic.UnavailableReason ?? string.Empty, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetLiveSetting(copied, AutomationLiveSetting.Polite);
        copyDiagnostic.Click += (_, _) => CopyDiagnostic(copied);
        privacy.Children.Add(StewardVisuals.SettingsRow(diagnostic.Label, diagnostic.Description, copyDiagnostic));
        privacy.Children.Add(copied);
        panel.Children.Add(StewardVisuals.SettingsCard("", "Privacy & exports", "PC Steward keeps everything on this PC. You decide what leaves it.", privacy));

        // Data & reset subpage (SPEC §17.3).
        StackPanel data = new() { Spacing = StewardSpacing.Small };
        foreach (DetailFact fact in DataResetPresenter.StorageSummary(DataResetInputsNow())) data.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        Button openData = StewardVisuals.ActionButton("Open Data & reset");
        openData.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        data.Children.Add(StewardVisuals.SettingsRow("Clear data or reset PC Steward", "Separate controls for scan data, downloaded files, source metadata, logs and history, and factory reset.", openData));
        panel.Children.Add(StewardVisuals.SettingsCard("", "Data & reset", "What PC Steward stores, how much space it uses, and how to remove it.", data));

        StackPanel about = new() { Spacing = StewardSpacing.Small };
        Button openAbout = StewardVisuals.ActionButton("About PC Steward");
        openAbout.Click += (_, _) => NavigateTo(ShellPage.About);
        about.Children.Add(StewardVisuals.SettingsRow(AppIdentity.DisplayName, "Version, privacy and how updates and administrator permission work.", openAbout));
        panel.Children.Add(StewardVisuals.SettingsCard("", "About", null, about));
        return panel;
    }

    /// <summary>
    /// Audit P-M7 and independent review fixes 5–6: "Save schedule" registers (or removes) the current-user scan-only task, then
    /// saves the settings, inside the data-operation lease (<see cref="ScheduleSaveOperation"/>). Registration and save failures are
    /// handled inside the lease, so only a busy gate reaches the "Action unavailable" dialog; a Task Scheduler refusal, a restored
    /// task or a task that could not be put back each say what happened. While saving, <see cref="HasActiveOperation"/> is true, so
    /// clear, reset, scan and other actions are unavailable.
    /// </summary>
    private async Task ApplyScheduleAsync(ScheduledCheckSettings requested)
    {
        ScheduledCheckSettings previous = scheduledSettings.Normalize();
        isSavingSchedule = true;
        RefreshOperationVisuals();
        ScheduleSaveOutcome outcome;
        try
        {
            (outcome, ScheduledCheckStatus? status) = await DataOperationLease.RunAsync(async token =>
            {
                ScheduleSaveOutcome saved = await ScheduleSaveOperation.SaveAsync(previous, requested, scheduleRegistration.ApplyAsync, scheduleSettingsStore.SaveScheduledCheckSettingsAsync, token);
                ScheduledCheckStatus? current = null;
                try { current = await scheduleSettingsStore.GetScheduledCheckStatusAsync(token); }
                catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException)) { }
                return (saved, current);
            }, CancellationToken.None);
            if (outcome == ScheduleSaveOutcome.Saved) scheduledSettings = requested;
            if (status is not null) scheduledStatus = status;
        }
        catch (InvalidOperationException) { isSavingSchedule = false; Render(); await ShowDataOperationUnavailableAsync(); return; }
        catch (Exception) { isSavingSchedule = false; Render(); await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Schedule was not changed", Content = "PC Steward couldn't start saving the schedule. Your saved settings were left unchanged.", CloseButtonText = "Close" }.ShowAsync(); return; }
        isSavingSchedule = false;
        Render();
        AnnounceCompletion(SettingsPresenter.ScheduleSaveResult(outcome) is { } result ? OperationAnnouncements.Completed(result.Title) : OperationAnnouncements.Completed("Schedule saved"), "schedule-saved");
        if (SettingsPresenter.ScheduleSaveResult(outcome) is { } message)
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = message.Title, Content = message.Body, CloseButtonText = "Close" }.ShowAsync();
    }

    /// <summary>FR-HIS-05: copies the existing sanitized diagnostic bundle of recent update outcomes and confirms in text.</summary>
    private void CopyDiagnostic(TextBlock confirmation)
    {
        try
        {
            Windows.ApplicationModel.DataTransfer.DataPackage package = new();
            package.SetText(SanitizedDiagnosticBundle.Create(updateHistory));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            // Keeps the copied diagnostic on the clipboard after PC Steward closes.
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            StewardVisuals.SetLiveText(confirmation, SettingsPresenter.DiagnosticCopied(updateHistory.Count), announce: true);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { StewardVisuals.SetLiveText(confirmation, SettingsPresenter.DiagnosticCopyFailed, announce: true); }
    }

    /// <summary>The Data &amp; reset inputs from the state loaded for this window; controls are unavailable during any active work.</summary>
    private DataResetInputs DataResetInputsNow() => new(
        scanData, downloadCache.TotalBytes, downloadCache.RecoverableBytes, downloadCache.ArtifactCount, downloadCache.ActiveCount, downloadCache.LastCleanedAt,
        viewModel.UpdateFreshnessLabel, updateHistory.Count, ActivityHistoryLimit, activityHistoryUnavailable,
        activeDecisions.Count + manualMappings.Count, HasActiveWork: HasActiveOperation);

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
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Trusted sources were not changed", Content = "PC Steward couldn't save this setting. Your previous setting is back in place.", CloseButtonText = "Close" }.ShowAsync();
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

    /// <summary>
    /// Task 73 Phase 3 (Home "Check for updates"): a read-only scan of this PC and, only when package sources are on and the scan
    /// completed, a refresh of their evidence — the same two user-started operations as before, in order. It never asks for
    /// administrator permission, never selects or approves anything and never installs.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        if (HasActiveOperation) { Render(); return; }
        DateTimeOffset? before = viewModel.LastScanAt;
        await StartScanAsync();
        if (windowClosed || HasActiveOperation || !viewModel.IsUpdateSourceEnabled || viewModel.LastScanAt is null || viewModel.LastScanAt == before) return;
        await CheckUpdateSourcesAsync();
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
        if (completed is not null && !windowClosed) AnnounceCompletion(OperationAnnouncements.ScanFinished(completed.Succeeded, completed.Cancelled, viewModel.AllRows.Count), "scan-finished");
    }

    private async Task CheckUpdateSourcesAsync()
    {
        string? detailStableProductKey = navigation.Current == ShellPage.Detail ? detailRow?.StableProductKey : null;
        // Independent review L2: this method takes no cancellation token and the band's Cancel only cancels a scan, so the source
        // refresh keeps its existing non-cancellable behaviour (bounded by the refresh service's own timeouts). Not changed here.
        viewModel.BeginUpdateCheck(); Render();
        bool refreshed = true;
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
        catch (InvalidOperationException) { refreshed = false; await ShowDataOperationUnavailableAsync(); }
        finally { viewModel.CompleteUpdateCheck(); }
        Render();
        if (refreshed && !windowClosed)
            AnnounceCompletion(OperationAnnouncements.UpdateCheckFinished(
                viewModel.AllRows.Count(row => row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate)),
                viewModel.AllRows.Count(row => row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired))), "update-check-finished");
    }

    /// <summary>
    /// FR-DAT-06–15, AC-UX-07/08, SPEC §12.5/§17.3 (Task 73 Phase 4, ADR-030 foundation 6; audit P-H4): separate cards — scan data,
    /// downloaded files, source metadata, logs and history, and decisions — each with its size and last-cleaned time where recorded and
    /// explicit Deletes/Keeps text, then Factory reset in a separated danger-zone card. Every button runs its existing operation through
    /// its existing confirmation dialog; every control is unavailable during active work.
    /// </summary>
    private void AddDataAndResetSection(StackPanel panel)
    {
        DataResetPagePresentation page = DataResetPresenter.Build(DataResetInputsNow());
        panel.Children.Add(StewardVisuals.InformationBanner("Installed apps and provider caches are never touched", page.ProviderCacheBoundary));
        TextBlock availability = PageCopy(page.Availability);
        AutomationProperties.SetLiveSetting(availability, AutomationLiveSetting.Polite);
        panel.Children.Add(availability);
        foreach (DataCardPresentation card in page.Cards)
        {
            StackPanel body = new() { Spacing = StewardSpacing.Small };
            foreach (DetailFact fact in card.Facts) body.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
            body.Children.Add(StewardVisuals.EvidenceRow("Deletes", card.Deletes));
            body.Children.Add(StewardVisuals.EvidenceRow("Keeps", card.Keeps));
            StewardWrapPanel actions = StewardVisuals.ActionRow();
            foreach (DataActionPresentation action in card.Actions) actions.Children.Add(DataActionButton(action));
            body.Children.Add(actions);
            string? reason = card.Actions.Select(action => action.UnavailableReason).FirstOrDefault(text => text is not null);
            if (reason is not null) body.Children.Add(new TextBlock { Text = reason, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(StewardVisuals.SettingsCard(card.Glyph, card.Title, null, body));
        }

        // Danger zone: visually separated from routine cleanup, with the existing two confirmations (exact RESET, Cancel default).
        FactoryResetCardPresentation reset = page.FactoryReset;
        StackPanel danger = new() { Spacing = StewardSpacing.Small };
        danger.Children.Add(new TextBlock { Text = reset.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        danger.Children.Add(StewardVisuals.EvidenceRow("Deletes", reset.Deletes));
        danger.Children.Add(StewardVisuals.EvidenceRow("Keeps", reset.Keeps));
        danger.Children.Add(PageCopy(reset.Afterwards));
        Button factoryReset = new() { Content = reset.Label, IsEnabled = reset.IsEnabled, Margin = new Thickness(0, StewardSpacing.Small, 0, 0) };
        AutomationProperties.SetName(factoryReset, reset.AccessibleName);
        factoryReset.Click += async (_, _) => await ConfirmAndArmFactoryResetAsync(factoryReset);
        danger.Children.Add(factoryReset);
        if (reset.UnavailableReason is { } resetReason) danger.Children.Add(new TextBlock { Text = resetReason, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        Border dangerZone = StewardVisuals.SettingsCard("", reset.Heading, "Separate from routine cleanup. This cannot be undone.", danger, danger: true);
        dangerZone.Margin = new Thickness(0, StewardSpacing.XLarge, 0, 0);
        panel.Children.Add(dangerZone);
    }

    /// <summary>One Data &amp; reset button, wired to its existing operation and confirmation (labels from the view model).</summary>
    private Button DataActionButton(DataActionPresentation action)
    {
        Button button = new() { Content = action.Label, IsEnabled = action.IsEnabled };
        AutomationProperties.SetName(button, action.AccessibleName);
        switch (action.Action)
        {
            case DataAction.ClearScanData: button.Click += async (_, _) => await ConfirmAndClearScanDataAsync(); break;
            case DataAction.ClearDownloadedFiles: button.Click += async (_, _) => await ConfirmAndClearDownloadedFilesAsync("Clear downloaded files?"); break;
            case DataAction.ClearSourceMetadata: WireAdvancedClear(button, "Clear source metadata", "Deletes cached package-source metadata and freshness records. Preserves inventory, connector consent, schedule, history, decisions and downloads.", () => workflow.ClearSourceMetadataAsync(CancellationToken.None)); break;
            case DataAction.ClearLogs: WireAdvancedClear(button, "Clear logs", "Deletes only PC Steward file logs. It preserves inventory, source metadata, settings, decisions, history, downloads and provider caches.", async () => { FactoryResetResult result = await FactoryReset.ClearLogsAsync(CancellationToken.None); return new AdvancedDataClearResult("Logs", result.Succeeded, 0, result.Succeeded ? "PC Steward logs were cleared." : "Logs were only partly cleared: " + string.Join(", ", result.Residuals.Select(item => item.Reason))); }); break;
            case DataAction.ClearUpdateHistory: WireAdvancedClear(button, "Clear update history", "Deletes recorded update outcomes only. Preserves inventory, settings, decisions, source metadata and downloads.", () => workflow.ClearUpdateHistoryAsync(CancellationToken.None)); break;
            case DataAction.ClearMappingsAndPolicies: WireAdvancedClear(button, "Clear mappings and policies", "Deletes manual mappings, pins and ignores. Preserves inventory, settings, consent, source metadata, history and downloads.", () => workflow.ClearMappingsAndPoliciesAsync(CancellationToken.None)); break;
            default: button.IsEnabled = false; break;
        }
        return button;
    }

    private void WireAdvancedClear(Button button, string label, string scope, Func<Task<AdvancedDataClearResult>> clear)
    {
        button.Click += async (_, _) =>
        {
            ContentDialog confirmation = new() { XamlRoot = Content.XamlRoot, Title = label + "?", Content = scope, PrimaryButtonText = label, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            isClearing = true; Render(); AdvancedDataClearResult result;
            try { result = await DataOperationLease.RunAsync(_ => clear(), CancellationToken.None); LoadSavedInventory(); }
            catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
            finally { isClearing = false; Render(); }
            string outcomeTitle = result.Succeeded ? label + " complete" : label + " was not completed";
            AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), "data-cleared");
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = outcomeTitle, Content = result.Message, CloseButtonText = "Close" }.ShowAsync();
        };
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
            // Blame-free message; the exception text stays available under "Technical details".
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset was not started", Content = StewardVisuals.MessageWithDetails("PC Steward couldn't start the factory reset. Nothing was deleted. Close PC Steward and try again.", exception.Message), CloseButtonText = "Close" }.ShowAsync();
        }
    }

    /// <summary>
    /// FR-POL-01–05 (Task 73 Phase 3, ADR-030 foundation 7): pins, ignores (indefinitely or until a date) and confirmed package
    /// identities in one view, grouped by kind, each with its reason and its existing reversal. Each Remove button's accessible name
    /// starts with its label and names the product (Phase 1 focus rule). Removing a decision only reapplies the normal review rules;
    /// it never starts an update.
    /// </summary>
    private void AddDecisionsSection(StackPanel panel)
    {
        IReadOnlyList<DecisionRow> rows = DecisionsPresenter.Build(activeDecisions, manualMappings, viewModel.AllRows);
        panel.Children.Add(PageCopy(rows.Count == 0
            ? "No active pins, ignores or confirmed package identities. Decisions are optional, reversible, and never enable or start updates."
            : "Remove a decision to reapply the normal review rules on the next source check. Nothing is updated when you do."));
        foreach (DecisionKind kind in new[] { DecisionKind.Pin, DecisionKind.Ignore, DecisionKind.Mapping })
        {
            DecisionRow[] group = rows.Where(row => row.Kind == kind).ToArray();
            if (group.Length == 0) continue;
            StackPanel section = new() { Spacing = 0 };
            section.Children.Add(SectionHeading(DecisionsPresenter.GroupHeading(kind, group.Length)));
            foreach (DecisionRow decision in group) section.Children.Add(DecisionRowView(decision));
            panel.Children.Add(Card(section));
        }
    }

    private Border DecisionRowView(DecisionRow decision)
    {
        Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.Children.Add(new FontIcon { Glyph = decision.Glyph, FontSize = 16, Foreground = StewardVisuals.Brush("StewardBrandBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        StackPanel copy = new() { Spacing = StewardSpacing.XSmall };
        copy.Children.Add(new TextBlock { Text = decision.ProductName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock { Text = decision.Title, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        copy.Children.Add(new TextBlock { Text = decision.Detail, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        if (decision.Reason is { } reason) copy.Children.Add(new TextBlock { Text = $"Reason: {reason}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        Grid.SetColumn(copy, 1); line.Children.Add(copy);
        Button remove = new() { Content = decision.RemoveLabel, VerticalAlignment = VerticalAlignment.Top, IsEnabled = !HasActiveOperation };
        AutomationProperties.SetName(remove, decision.RemoveAccessibleName);
        remove.Click += async (_, _) =>
        {
            remove.IsEnabled = false;
            try
            {
                // The existing reversal operations: a pin or ignore is reversed, a confirmed mapping is deleted.
                if (decision.Kind == DecisionKind.Mapping) await DataOperationLease.RunAsync(token => workflow.DeleteManualMappingAsync(decision.Id, token), CancellationToken.None);
                else await DataOperationLease.RunAsync(token => workflow.ReverseDecisionAsync(decision.Id, token), CancellationToken.None);
                LoadSavedInventory(); Render();
            }
            catch (InvalidOperationException) { remove.IsEnabled = true; await ShowDataOperationUnavailableAsync(); }
        };
        Grid.SetColumn(remove, 2); line.Children.Add(remove);
        Border row = new() { BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, StewardSpacing.Medium, 0, StewardSpacing.Medium), Child = line };
        AutomationProperties.SetName(row, $"{decision.ProductName}: {decision.Title}");
        return row;
    }

    private async Task SaveExportAsync(bool json) { try { InventoryExport export = await DataOperationLease.RunReadAsync(token => workflow.CreateExportAsync(json, token), CancellationToken.None); FileSavePicker picker = new(); picker.SuggestedFileName = $"pc-steward-inventory-{export.ExportedAt:yyyyMMdd-HHmmss}"; picker.FileTypeChoices.Add(json ? "JSON inventory" : "CSV inventory", [export.FileExtension]); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); StorageFile? file = await picker.PickSaveFileAsync(); if (file is not null) await FileIO.WriteTextAsync(file, export.Content); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }

    private async Task ConfirmAndClearScanDataAsync()
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = "Clear scan data?", Content = "This removes current inventory, raw provider records, update candidates and scan-run records. It keeps settings, enabled connectors, schedules, update history, downloaded files, mappings, pins and ignores.", PrimaryButtonText = "Clear scan data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); ClearScanDataResult result;
        try { result = await DataOperationLease.RunAsync(token => workflow.ClearScanDataAsync(token), CancellationToken.None); if (result.Succeeded) { viewModel.ClearScanData(); LoadSavedInventory(); } }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        finally { isClearing = false; Render(); }
        string outcomeTitle = result.Succeeded ? "Scan data cleared" : "Scan data was not cleared";
        AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), "data-cleared");
        ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = outcomeTitle, Content = result.Message, CloseButtonText = "Close" }; await outcome.ShowAsync();
    }

    private async Task ConfirmAndClearDownloadedFilesAsync(string title)
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = title, Content = "This removes inactive PC Steward-owned update payloads and temporary extraction files only. It does not delete active update files, installed applications, scan data, source metadata, update history, or Windows, WinGet, Microsoft Store, or other provider caches.", PrimaryButtonText = "Delete downloaded files", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); DownloadCleanupResult result;
        try { result = await DataOperationLease.RunAsync(token => downloads.ClearInactiveAsync(token), CancellationToken.None); LoadSavedInventory(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        finally { isClearing = false; Render(); }
        string outcomeTitle = result.Succeeded ? "Downloaded files cleared" : "Downloaded files were only partly cleared";
        AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), "data-cleared");
        // Blame-free message first; the remaining items (paths and reasons) sit behind "Technical details".
        string? residuals = result.Residuals.Count == 0 ? null : "Remaining items: " + string.Join(", ", result.Residuals.Select(residual => $"{residual.RelativePath} ({residual.Reason})"));
        ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = outcomeTitle, Content = StewardVisuals.MessageWithDetails(result.Message, residuals), CloseButtonText = "Close" }; await outcome.ShowAsync();
    }

    /// <summary>
    /// FR-POL-01–05, FR-HIS-02, FR-UI-04 (Task 73 Phase 3; audit P-M1, P-L6): an app's details. Pin, ignore and every mapping action
    /// use the same confirmation dialogs as the lists and the Updates page; technical details are structured sections (instances,
    /// candidates) and the app's own timeline is a list in words, not a newline dump.
    /// </summary>
    private StackPanel BuildProductDetailPage()
    {
        if (detailRow is null)
        {
            StackPanel missing = CreatePagePanel(760);
            missing.Children.Add(StewardVisuals.PageTitle("App details"));
            Button leave = StewardVisuals.ActionButton("Open Installed apps");
            leave.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            missing.Children.Add(StewardVisuals.EmptyState("", "App details are no longer open", "Open an app from Installed apps, Updates or the scan results to see its details.", leave));
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
        title.Children.Add(new TextBlock { Text = "App details", Style = AppStyle("StewardCaptionStyle") });
        title.Children.Add(StewardVisuals.PageTitle(row.Name));
        StewardStatus statusPresentation = StewardStatus.From(row.Status);
        title.Children.Add(StewardVisuals.Badge(status, statusPresentation.Glyph, statusPresentation.Tone, $"{row.Name}: {status}"));
        heading.Children.Add(title);
        // The title bar's Back owns the "B" access key, so this page-level Back has none (no duplicate keys in one scope).
        Button back = StewardVisuals.ActionButton("Back", "to the previous page"); back.Click += (_, _) => { detailRow = null; if (navigation.CanGoBack) GoBack(); else NavigateTo(ShellPage.InstalledApps); }; Grid.SetColumn(back, 1); heading.Children.Add(back);
        panel.Children.Add(heading);

        Grid comparison = new() { ColumnSpacing = 12, RowSpacing = 12 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel installed = new() { Spacing = StewardSpacing.Small };
        installed.Children.Add(SectionHeading("Installed"));
        installed.Children.Add(StewardVisuals.EvidenceRow("Version", row.Version));
        installed.Children.Add(StewardVisuals.EvidenceRow("Publisher", row.Publisher));
        installed.Children.Add(StewardVisuals.EvidenceRow("Location", $"{StewardPresentation.Scope(row.Scope)} · {StewardPresentation.Provider(row.Provider)}"));
        comparison.Children.Add(Card(installed));
        StackPanel evidence = new() { Spacing = StewardSpacing.Small };
        evidence.Children.Add(SectionHeading("Package source"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Package ID", detail?.PackageId ?? "No managed package identity"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Source", detail?.SourceId ?? "No match from a source you turned on"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Available", detail?.CandidateVersion ?? "Not established"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Confidence", ReviewReasonText.Confidence(detail?.Confidence)));
        evidence.Children.Add(PageCopy(detail?.Freshness ?? "Local-only — no source check has been run."));
        Grid.SetColumn(evidence, 1); comparison.Children.Add(Card(evidence));
        panel.Children.Add(comparison);

        StackPanel why = new() { Spacing = StewardSpacing.Small };
        why.Children.Add(SectionHeading("Why this appears"));
        why.Children.Add(PageCopy(detail?.Explanation ?? "This app was found on this PC. No package source identifies it exactly yet."));
        IReadOnlyList<string> reasons = ReviewReasonText.DescribeAll(detail?.ReviewReasons ?? []);
        foreach (string reason in reasons) why.Children.Add(PageCopy($"• {reason}"));
        panel.Children.Add(Card(why));

        StackPanel capability = new() { Spacing = StewardSpacing.Small };
        capability.Children.Add(SectionHeading("What PC Steward can do"));
        capability.Children.Add(PageCopy(detail?.ActionState ?? "Nothing happens automatically. You can record a decision or check the package source evidence."));
        capability.Children.Add(PageCopy("This page never installs, removes or changes the app."));
        panel.Children.Add(Card(capability));

        PCSteward.Application.AdministratorRequirementPresentation administratorRequirement =
            PCSteward.Application.AdministratorRequirementPresenter.BeforeReview();
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate))
        {
            panel.Children.Add(StewardVisuals.InformationBanner(administratorRequirement.Label, administratorRequirement.Explanation, "attention"));
        }

        bool sourceEvidenceUnavailable = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable);
        StackPanel decisions = new() { Spacing = 8 };
        decisions.Children.Add(SectionHeading(sourceEvidenceUnavailable ? "Available next step" : "Decisions"));
        if (sourceEvidenceUnavailable)
        {
            decisions.Children.Add(PageCopy("Refresh the package source evidence to try again. While it's unknown whether an update exists, there's nothing to approve, map, pin or ignore."));
        }
        else
        {
            // Audit P-M1: the same decisions, with the same confirmation dialogs (and optional reason), as everywhere else.
            decisions.Children.Add(PageCopy("Each decision opens a confirmation where you can add an optional reason. Decisions are reversible on the Decisions page and never start an update."));
        }
        ComboBox? candidatePicker = null;
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0 && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy("Choose only a package you recognise as this installed app. PC Steward checks it again before saving your choice, which you can undo. No update starts."));
            candidatePicker = new ComboBox
            {
                Header = "Matching packages from your sources",
                ItemsSource = detail.ReviewCandidates.ToArray(),
                DisplayMemberPath = nameof(ReviewCandidateChoice.DisplayLabel),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(candidatePicker, $"Matching packages from your sources for {row.Name}");
            decisions.Children.Add(candidatePicker);
        }
        StewardWrapPanel decisionActions = StewardVisuals.ActionRow();
        if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisionActions.Children.Add(CreateSourceEvidenceRefreshButton(row));
        }
        if (candidatePicker is not null)
        {
            Button confirmSelected = StewardVisuals.PrimaryButton("Confirm selected identity", $"for {row.Name}");
            confirmSelected.Click += async (_, _) =>
            {
                if (candidatePicker.SelectedItem is not ReviewCandidateChoice candidate) return;
                await PromptAndCreateManualMappingAsync(row, candidate.SourceId, candidate.PackageId);
            };
            decisionActions.Children.Add(confirmSelected);
        }
        if (!sourceEvidenceUnavailable)
        {
            Button pin = StewardVisuals.ActionButton("Pin current version", $"of {row.Name} ({row.Version})");
            pin.IsEnabled = !HasActiveOperation;
            pin.Click += async (_, _) => await PromptAndPinCurrentVersionAsync(row);
            decisionActions.Children.Add(pin);
            Button ignore = StewardVisuals.ActionButton("Ignore…", $"{row.Name}: choose how long");
            ignore.IsEnabled = !HasActiveOperation;
            ignore.Click += async (_, _) => await PromptAndIgnoreProductAsync(row);
            decisionActions.Children.Add(ignore);
        }
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy($"Confirm this only if you recognise {detail.PackageId} from {detail.SourceId} as this installed app. You can undo it, and it doesn't start an update."));
            Button confirmPackage = StewardVisuals.ActionButton("Confirm package identity", $"({detail.PackageId} is this installed app)");
            confirmPackage.Click += async (_, _) => await PromptAndConfirmPackageIdentityAsync(row, detail);
            decisionActions.Children.Add(confirmPackage);
        }
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate) && viewModel.IsUpdateSourceEnabled && !isExecuting)
        {
            Button review = StewardVisuals.PrimaryButton("Review and update", $"this verified package. {administratorRequirement.Label}");
            AutomationProperties.SetHelpText(review, administratorRequirement.Explanation);
            review.IsEnabled = !HasActiveOperation && reviewDecision is null;
            review.Click += async (_, _) => await ReviewAndExecuteAsync(row);
            decisionActions.Children.Add(review);
        }
        decisions.Children.Add(decisionActions);
        panel.Children.Add(Card(decisions));

        if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId))
        {
            StackPanel reviewedRoute = new() { Spacing = StewardSpacing.Small };
            reviewedRoute.Children.Add(SectionHeading("Chocolatey reviewed update"));
            reviewedRoute.Children.Add(PageCopy("PC Steward does not allow this Chocolatey update route yet. You can read what it would disclose before any confirmation; it cannot start an update."));
            Button openReviewedRoute = ReviewedRouteSurfaces.ChocolateyReviewEntryButton();
            openReviewedRoute.Click += async (_, _) => await ShowChocolateyReviewedRouteAsync();
            reviewedRoute.Children.Add(openReviewedRoute);
            panel.Children.Add(Card(reviewedRoute));
        }

        // FR-HIS-02: the app's own timeline as a list, newest first.
        StackPanel history = new() { Spacing = 0 };
        history.Children.Add(SectionHeading("History for this app"));
        IReadOnlyList<ProductTimelineItem> timeline = AppDetailPresenter.Timeline(detailTimeline);
        if (timeline.Count == 0) history.Children.Add(PageCopy("No events are recorded for this app yet."));
        foreach (ProductTimelineItem item in timeline)
        {
            Border entry = StewardVisuals.ActivityRow(item.When, item.Glyph, item.Title, item.Detail ?? string.Empty);
            AutomationProperties.SetName(entry, item.AccessibleName);
            history.Children.Add(entry);
        }
        panel.Children.Add(Card(history));

        // Technical details: structured sections instead of newline-joined text (audit P-L6).
        StackPanel technical = new() { Spacing = StewardSpacing.Medium };
        technical.Children.Add(SectionHeading("Installed instances", AutomationHeadingLevel.Level3));
        IReadOnlyList<DetailItem> instances = AppDetailPresenter.Instances(row.Instances);
        if (instances.Count == 0) technical.Children.Add(PageCopy("No installed instance details are recorded."));
        foreach (DetailItem instance in instances) technical.Children.Add(DetailItemCard(instance));
        technical.Children.Add(SectionHeading("Packages found in your sources", AutomationHeadingLevel.Level3));
        IReadOnlyList<DetailItem> candidates = AppDetailPresenter.Candidates(detail?.ReviewCandidates ?? []);
        if (candidates.Count == 0) technical.Children.Add(PageCopy("No packages were found in your sources."));
        foreach (DetailItem candidate in candidates) technical.Children.Add(DetailItemCard(candidate));
        technical.Children.Add(SectionHeading("Match a package by ID", AutomationHeadingLevel.Level3));
        // Audit §4: the source list shows the names rows show; the chosen index maps back to the same raw source ID as before.
        string[] mappingSources = ["winget", "msstore"];
        ComboBox mappingSource = FilterBox(StewardPresentation.Provider(detail?.SourceId ?? "winget"), mappingSources.Select(StewardPresentation.Provider).ToArray());
        mappingSource.Header = "Source";
        AutomationProperties.SetName(mappingSource, $"Source for matching {row.Name}");
        TextBox mappingPackage = new() { Header = "Exact package ID", PlaceholderText = detail?.PackageId ?? "For example: Contoso.Widget" };
        technical.Children.Add(mappingSource);
        technical.Children.Add(mappingPackage);
        Button map = StewardVisuals.ActionButton("Match package…", $"for {row.Name}");
        map.IsEnabled = viewModel.IsUpdateSourceEnabled && !sourceEvidenceUnavailable && !HasActiveOperation;
        map.Click += async (_, _) =>
        {
            string sourceId = mappingSource.SelectedIndex >= 0 && mappingSource.SelectedIndex < mappingSources.Length ? mappingSources[mappingSource.SelectedIndex] : "winget";
            string packageId = string.IsNullOrWhiteSpace(mappingPackage.Text) ? detail?.PackageId ?? string.Empty : mappingPackage.Text.Trim();
            if (packageId.Length == 0) { await ShowUpdateOutcomeAsync("Package ID needed", "Enter the exact package ID first. Nothing was saved."); return; }
            await PromptAndCreateManualMappingAsync(row, sourceId, packageId);
        };
        technical.Children.Add(map);
        technical.Children.Add(PageCopy($"Technical records kept for troubleshooting: {detailRaw.Count.ToString("N0", CultureInfo.CurrentCulture)}."));
        Expander disclosure = new() { Header = "Technical details", Content = technical, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(disclosure);
        return panel;
    }

    private static TextBlock SectionHeading(string text, AutomationHeadingLevel level = AutomationHeadingLevel.Level2) => StewardVisuals.SectionHeading(text, level);

    /// <summary>An access key from a label's first letter ("Scan again" → S, "Check for updates" → C).</summary>
    private static string AccessKeyFor(string label) => label.Length == 0 ? string.Empty : label[..1].ToUpperInvariant();

    /// <summary>One structured item (an instance or a candidate): a title and its facts, with selectable values.</summary>
    private static Border DetailItemCard(DetailItem item)
    {
        StackPanel content = new() { Spacing = 0 };
        content.Children.Add(new TextBlock { Text = item.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(0, 0, 0, StewardSpacing.XSmall) });
        foreach (DetailFact fact in item.Facts) content.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        Border card = new() { BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(StewardSpacing.XSmall), Padding = new Thickness(StewardSpacing.Medium), Child = content };
        AutomationProperties.SetName(card, item.Title);
        return card;
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
        if (HasActiveOperation || windowClosed || reviewDecision is not null) return;
        // Independent review: while the review is prepared every review entry point is disabled (HasActiveOperation) and the band
        // says "Preparing review…", so a second review cannot start. The flag covers preparation only, never the sheet.
        preparingReview = true;
        jobResultPending = false;
        Render();
        UpdateReviewPreparation? prepared = null;
        bool leaseUnavailable = false;
        try { prepared = await DataOperationLease.RunAsync(token => updates.BeginReviewAsync(row.StableProductKey, token), CancellationToken.None); }
        catch (InvalidOperationException) { leaseUnavailable = true; }
        catch (Exception) { prepared = null; }
        finally { preparingReview = false; }
        if (windowClosed) return;
        if (prepared is null)
        {
            Render();
            if (leaseUnavailable) await ShowDataOperationUnavailableAsync();
            else await ShowUpdateOutcomeAsync("Update not available", "PC Steward could not prepare this update. No update was started.");
            return;
        }
        UpdateReviewPreparation preparation = prepared;
        if (preparation.Review is null)
        {
            Render();
            await ShowUpdateOutcomeAsync("Update not available", preparation.Message);
            return;
        }
        PCSteward.Domain.UpdateJobSelection selection = preparation.Review.Selection;
        PCSteward.Application.AdministratorRequirementPresentation administratorRequirement =
            PCSteward.Application.AdministratorRequirementPresenter.ForSelection(selection);
        // FR-UI-03/04, SPEC §17.2 (audit P-H6): the structured review sheet replaces the wall-of-text confirmation. It shows only the
        // reviewed immutable selection, the computed confidence, the running-app warning with its CoverageDisclosure, and the
        // administrator requirement. Cancel is the default, and leaving the page cancels the review.
        UpdateReviewSheet sheet = UpdateReviewSheet.Create(selection, row.Name, row.Publisher, viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey),
            preparation.RunningApplication, administratorRequirement);
        UpdateJobSession job = new(row, sheet);
        updateJob = job;
        TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        reviewDecision = decision;
        // A new review (for example "Start a new review" from the job page) always gets a fresh page: focus on Cancel, scroll at top.
        if (navigation.Current == ShellPage.UpdateJob) ShowCurrentPage(preserveViewState: false); else NavigateTo(ShellPage.UpdateJob);
        AnnounceCompletion(OperationAnnouncements.ReviewReady(row.Name), "review-ready");
        bool confirmed = await decision.Task;
        if (ReferenceEquals(reviewDecision, decision)) reviewDecision = null;
        if (!confirmed || windowClosed)
        {
            executionPending = false;
            if (ReferenceEquals(updateJob, job)) updateJob = null;
            // Independent review: always refresh whichever page is shown, so its Review button is enabled again after Cancel.
            if (!windowClosed) Render();
            return;
        }

        isExecuting = true;
        executionPending = false;
        job.Step = UpdateJobStep.Update;
        updateProgressMailbox = preparation.Progress;
        updateProgressPresentation = new(preparation.Review.JobId, preparation.Review.CorrelationId, selection.Scope);
        updateProgressLabel = preparation.Progress is null ? "Progress unavailable" : "Revalidating reviewed update";
        ReviewedUpdateExecutionResult result;
        // Set only once the data lease has run the operation: a lease refusal (busy, reset pending or an unresolved earlier
        // update) happens before the workflow is entered, so nothing was started and nothing was changed.
        bool operationEntered = false;
        try
        {
            Render();
            StartUpdateProgressPolling();
            result = await DataOperationLease.RunAsync(token =>
            {
                Volatile.Write(ref operationEntered, true);
                return updates.ConfirmAndRunAsync(preparation.Review, preparation.RunningApplication.AppearsToBeRunning, token, preparation.Progress);
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) when (!Volatile.Read(ref operationEntered)) { result = new(false, UpdateOutcomePresenter.DataUnavailableNotStartedMessage, null); }
        catch (Exception) { result = new(true, "The reviewed update did not produce a recorded outcome. Execution or verification may have been interrupted. No verified success was recorded; refresh inventory before deciding what to do next.", null); }
        finally { StopUpdateProgressPolling(); isExecuting = false; updateProgressMailbox = null; updateProgressPresentation = null; if (!windowClosed) LoadSavedInventory(); }
        if (windowClosed) return;
        // FR-UPD-08/09, UJ-04 step 8 (audit P-H3): the recorded batch is authoritative and maps to Verified, Restart required,
        // Installed, not verified, Failed or Not started — never a flattened "Update failed". A refused UAC prompt returns quietly
        // to Review. The retained terminal may still be rate-limited; do not fabricate a progress frame or sequence.
        job.Completion = UpdateOutcomePresenter.ForResult(result, selection, row.Name);
        job.Step = job.Completion.ReturnsToReview ? UpdateJobStep.Review : UpdateJobStep.Verify;
        // Independent review: never move the user. Elsewhere, the band shows the result with "View result".
        jobResultPending = navigation.Current != ShellPage.UpdateJob;
        Render();
        AnnounceCompletion(OperationAnnouncements.JobFinished(row.Name, job.Completion.Headline), "update-job-finished");
    }

    private Task<ContentDialogResult> ShowUpdateOutcomeAsync(string title, string message) => new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync().AsTask();
}
