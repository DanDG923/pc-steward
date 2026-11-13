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
public sealed partial class MainWindow : Window
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
    private readonly ChocolateyClientReleaseCatalogFacts chocolateyClientReleases;
    private readonly Func<CancellationToken, Task<ChocolateyInstalledClientVerification>> checkInstalledChocolateyClient;
    private readonly Func<ChocolateyClientUpdateReviewSession> createChocolateyClientUpdateSession;
    private ChocolateyClientUpdateReviewDialog? activeChocolateyClientUpdate;
    private readonly Func<ChocolateyPackageUpdateReviewSession> createChocolateyPackageUpdateSession;
    private ChocolateyPackageUpdateReviewDialog? activeChocolateyPackageUpdate;
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
    // UI review 2026-11-13 (item 11): whether "Match it yourself" is open on app details; closed each time details are opened.
    private bool detailMatchFormOpen;
    // UI review 2026-11-13 (item 12): the line shown after a pin, ignore, skip or confirmed match, with Undo, on the page it was made on.
    private DecisionNotice? decisionNotice;
    private ShellPage decisionNoticePage;
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
    // Task 73 Phase 6: the Ready apps the user ticked. It starts empty and only Ready keys survive each render (nothing preselected).
    private readonly HashSet<string> selectedReadyKeys = new(StringComparer.Ordinal);
    private bool ignoredGroupExpanded;
    private bool cantUpdateGroupExpanded;
    private UpdateJobSession? updateJob;
    private TaskCompletionSource<bool>? reviewDecision;
    // Independent review: true while BeginReviewAsync prepares a review, so a second review cannot start meanwhile.
    private bool preparingReview;
    // Task 73 Phase 6 review M2: "Preparing review… n of N" and the band's "Cancel review preparation" while a review is prepared.
    private UpdateBatchReviewProgress? reviewPreparationProgress;
    private CancellationTokenSource? reviewPreparationCancellation;
    private Button? operationCancelReview;
    // Task 73 Phase 6 review (Info): the band names the app updating now in a batch ("Updating Firefox (2 of 4)"), so a UAC prompt has context.
    private string? updateProgressSubject;
    // Independent review: a job finished while the user was elsewhere; the band offers "View result" instead of moving them.
    private bool jobResultPending;
    private ProgressBar? operationProgress;
    private TextBlock? jobProgressStateText;
    // Task 73 Phase 6 review L1: the Update step line that carries the live state (the app updating now, or the next app while waiting).
    private UpdateJobItemLine? jobProgressLine;
    private Button? operationViewJob;
    private Control? pageInitialFocus;
    // Task 73 Phase 5 (AC-UX-01/09): the control Ctrl+F focuses on the current page, and the last operation state the live region spoke.
    private Control? pageSearchTarget;
    private string? lastOperationState;

    private enum ResultsFilter { All, Actionable, NeedsDecision, Unavailable, Unmanaged }

    private const int MinimumWindowWidth = WindowMinimumSize.EffectiveWidth;
    private const int MinimumWindowHeight = WindowMinimumSize.EffectiveHeight;
    private double appliedMinimumSizeScale;

    /// <summary>
    /// Task 73 Phase 2 (ADR-030 decision 3): what the update job page shows. It holds presentation only — the reviewed sheet, the
    /// current step and, once recorded, the completion. The immutable reviews and their approvals stay inside ReviewAndExecuteAsync.
    /// Task 73 Phase 6 (SPEC §9.13 G4): one session covers a frozen set of apps; it also tracks which app is updating now, each
    /// finished app's outcome label and the "stop after this app" request (read only between apps).
    /// </summary>
    private sealed class UpdateJobSession(IReadOnlyList<InventoryRow> rows, UpdateReviewSheet sheet)
    {
        public IReadOnlyList<InventoryRow> Rows { get; } = rows;
        public UpdateReviewSheet Sheet { get; } = sheet;
        public UpdateJobStep Step { get; set; } = UpdateJobStep.Review;
        public UpdateJobCompletion? Completion { get; set; }
        public int CurrentIndex { get; set; } = -1;
        public string?[] FinishedLabels { get; } = new string?[sheet.ItemCount];
        public UpdateBatchStop Stop { get; } = new();
    }

    public MainWindow(InventoryWorkflow workflow, InventoryExperienceViewModel viewModel, DownloadedArtifactCache downloads, ReviewedUpdateExecutionWorkflow updates, IScheduledCheckSettingsStore scheduleSettingsStore, ScheduledCheckRegistrationService scheduleRegistration, IReviewedUpdateHistoryReader reviewedHistory, IActivityHistoryReader activityHistory, Func<ReviewedRouteSplitTokenState> probeReviewedRouteSplitToken, ChocolateyClientReleaseCatalogFacts chocolateyClientReleases, Func<CancellationToken, Task<ChocolateyInstalledClientVerification>> checkInstalledChocolateyClient, Func<ChocolateyClientUpdateReviewSession> createChocolateyClientUpdateSession, Func<ChocolateyPackageUpdateReviewSession> createChocolateyPackageUpdateSession, bool showPostResetOnboarding = false, Func<FactoryResetResult>? acknowledgeResetOnboarding = null)
    {
        this.workflow = workflow; this.viewModel = viewModel; this.downloads = downloads; this.updates = updates; this.scheduleSettingsStore = scheduleSettingsStore; this.scheduleRegistration = scheduleRegistration; this.acknowledgeResetOnboarding = acknowledgeResetOnboarding;
        this.reviewedHistory = reviewedHistory ?? throw new ArgumentNullException(nameof(reviewedHistory)); this.activityHistory = activityHistory ?? throw new ArgumentNullException(nameof(activityHistory)); this.probeReviewedRouteSplitToken = probeReviewedRouteSplitToken ?? throw new ArgumentNullException(nameof(probeReviewedRouteSplitToken)); this.chocolateyClientReleases = chocolateyClientReleases ?? throw new ArgumentNullException(nameof(chocolateyClientReleases)); this.checkInstalledChocolateyClient = checkInstalledChocolateyClient ?? throw new ArgumentNullException(nameof(checkInstalledChocolateyClient)); this.createChocolateyClientUpdateSession = createChocolateyClientUpdateSession ?? throw new ArgumentNullException(nameof(createChocolateyClientUpdateSession)); this.createChocolateyPackageUpdateSession = createChocolateyPackageUpdateSession ?? throw new ArgumentNullException(nameof(createChocolateyPackageUpdateSession));
        updateProgressTimer = DispatcherQueue.CreateTimer();
        updateProgressTimer.Interval = TimeSpan.FromMilliseconds(250);
        Closed += (_, _) => { windowClosed = true; reviewDecision?.TrySetResult(false); reviewPreparationCancellation?.Cancel(); StopUpdateProgressPolling(); updateProgressMailbox = null; updateProgressPresentation = null; };
        // Chocolatey Confirm design §4.3: closing PC Steward cannot stop an elevated Chocolatey update that has started, so the window
        // stays open until its result is recorded and shown.
        AppWindow.Closing += (_, args) => { if (activeChocolateyClientUpdate?.IsRunning == true || activeChocolateyPackageUpdate?.IsRunning == true) args.Cancel = true; };
        Title = AppIdentity.DisplayName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "pc-steward.ico"));
        // Sharp text fix: the app is DPI aware, so the 1280x820 first size is in effective pixels, scaled to the display.
        WindowDisplay.ResizeEffective(this, 1280, 820);
        // Audit P-M9: a minimum window size, so pages reflow (wrapping action rows, stacked headers) instead of clipping. It is set
        // at the window's display scale now and rescaled once the content has a XamlRoot (see ApplyMinimumWindowSize).
        ApplyMinimumWindowSize(WindowDisplay.Scale(this));
        navigation = new ShellNavigation(showPostResetOnboarding ? ShellPage.Onboarding : ShellPage.Home);
        // Leaving the update job's Review step cancels that review: navigation never approves anything (FR-UPD-01).
        navigation.PropertyChanged += (_, _) => { SyncNavigationChrome(); if (navigation.Current != ShellPage.UpdateJob) CancelPendingReview(); else jobResultPending = false; };
        BuildWindowChrome();
        if (!showPostResetOnboarding) startupStateLoadFailed = !LoadSavedInventory();
        Render();
        // ADR-032 revision 2 §8: a recorded PC Steward update is decided once per launch.
        if (!showPostResetOnboarding) selfUpdateStatusLoad = LoadSelfUpdateStatusAsync();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyMinimumWindowSize(sender.RasterizationScale);

    /// <summary>AC-UX-04 (final review Info-1): applies the 500x500 epx minimum in physical pixels at <paramref name="scale"/>.</summary>
    private void ApplyMinimumWindowSize(double scale)
    {
        if (windowClosed || scale.Equals(appliedMinimumSizeScale)) return;
        if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter) return;
        presenter.PreferredMinimumWidth = WindowMinimumSize.PhysicalPixels(MinimumWindowWidth, scale);
        presenter.PreferredMinimumHeight = WindowMinimumSize.PhysicalPixels(MinimumWindowHeight, scale);
        appliedMinimumSizeScale = scale;
    }

    private bool LoadSavedInventory()
    {
        // FR-UPD-01 (independent review): a reload, rescan, Clear scan data or a finished update job ends any earlier choice on
        // Updates; nothing is preselected again.
        selectedReadyKeys.Clear();
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
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageTitle("PC Steward couldn't open its data"));
        panel.Children.Add(PageCopy("Another PC Steward task is still using its data, or a reset is still being finished. Nothing was scanned or changed. Close PC Steward and try again when the other task has finished."));
        ShowBarePage(panel);
    }

    /// <summary>
    /// UI polish Phase 1 (approved targets; plan root cause 2): every page is one left-aligned column beside the navigation, like
    /// Windows Settings, and never centred in the window. Phase 5 (owner feedback on a maximised window): the column has no
    /// maximum width; the host gives it the window's full width (<see cref="StewardVisuals.FitPageColumn"/>).
    /// </summary>
    private static StackPanel CreatePagePanel()
    {
        Thickness padding = StewardSpacing.PagePadding;
        StackPanel panel = new()
        {
            Spacing = StewardSpacing.Section,
            Padding = padding,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        panel.Resources[typeof(TextBlock)] = AppStyle("StewardBodyStyle");
        panel.Resources[typeof(Button)] = AppStyle("StewardButtonStyle");
        panel.Resources[typeof(TextBox)] = AppStyle("StewardInputStyle");
        panel.Resources[typeof(ComboBox)] = AppStyle("StewardSelectStyle");
        return panel;
    }

    private static TextBlock PageCopy(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    private static Border Card(FrameworkElement child) => new() { Style = AppStyle("StewardCardStyle"), Child = child };

    // The self-update counts from the owner's Install until PC Steward exits, because a ready session closes the app.
    private bool HasActiveOperation => (selfUpdateStage is SelfUpdateStage.Installing or SelfUpdateStage.Closing) ||
        scanCancellation is not null || viewModel.IsScanning || viewModel.IsCheckingUpdates || isExecuting || executionPending || isClearing || downloads.IsCleaning || preparingReview || isSavingSchedule;

    /// <summary>Leaving or cancelling the review clears it at once, so the next page is not built while it still counts as open.</summary>
    private void CancelPendingReview()
    {
        if (reviewDecision is not { } pending) return;
        reviewDecision = null;
        pending.TrySetResult(false);
    }

    private bool ShowsJobResultInBand => jobResultPending && !isExecuting && updateJob is { Completion: not null } && navigation.Current != ShellPage.UpdateJob;

    private const string PreparingReviewLabel = "Preparing review…";

    /// <summary>
    /// AC-UX-09 (final review L5): one announcement per change. While an update runs on the update job page, the job's own state text
    /// is the live region, so the band stays quiet; a job result shown in the band is spoken once, by the completion notification
    /// (<see cref="AnnounceCompletion"/>), not also by the band's live region.
    /// </summary>
    private bool BandAnnouncesState => !ShowsJobResultInBand && !(navigation.Current == ShellPage.UpdateJob && (isExecuting || executionPending));

    private string CurrentOperationLabel => viewModel.IsScanning
        ? $"Scanning this PC · {viewModel.ScanPhase} · {viewModel.Elapsed:mm\\:ss}"
        : scanCancellation is not null
            ? "Preparing the scan"
            : viewModel.IsCheckingUpdates
                ? "Looking for updates"
                : isExecuting
                    ? UpdateJobProgressText.BandLabel(updateProgressSubject, updateProgressLabel)
                    : isClearing || downloads.IsCleaning
                        ? "Clearing PC Steward data"
                        : isSavingSchedule
                            ? "Saving the schedule"
                        : preparingReview
                            ? UpdateJobProgressText.PreparingReview(PreparingReviewLabel, reviewPreparationProgress)
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
            StewardVisuals.SetLiveText(operationStatusText, label, announce: stateChanged && BandAnnouncesState && operationBand?.Visibility == Visibility.Visible);
        }
        if (operationCancel is not null) operationCancel.Visibility = viewModel.IsScanning || scanCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        if (operationCancelReview is not null) operationCancelReview.Visibility = preparingReview && reviewPreparationCancellation is not null ? Visibility.Visible : Visibility.Collapsed;
        if (scanPageStatusText is not null) scanPageStatusText.Text = $"{viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss}";
        // Task 73 Phase 2: the job page's per-item state follows the same polled progress presentation; no page rebuild per frame.
        if (jobProgressStateText is not null && isExecuting) StewardVisuals.SetLiveText(jobProgressStateText, UpdateJobProgressText.LiveState(jobProgressLine ?? new(true, null), updateProgressPresentation), announce: true);
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

    private static Style AppStyle(string key) => (Style)global::Microsoft.UI.Xaml.Application.Current.Resources[key];

    private static ComboBox FilterBox(string selected, params string[] values) => FilterBox(selected, (IReadOnlyCollection<string>)values);
    private static ComboBox FilterBox(string selected, IReadOnlyCollection<string> values) { ComboBox box = new(); foreach (string value in values.Distinct(StringComparer.OrdinalIgnoreCase)) box.Items.Add(value); box.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values.First(); return box; }
    private void RenderInventory(ListView list)
    {
        // UI review 2026-11-13 (item 8): a row says where its record came from only when another listed row has the same name.
        IReadOnlySet<string> sharedNames = InstalledAppsFilters.SharedNames(viewModel.ListedRows);
        list.ItemsSource = viewModel.VisibleRows.Select(row => ApplicationListItem.From(row, sharedNames.Contains(row.Name))).ToArray();
    }

    private static TextBlock SectionHeading(string text, AutomationHeadingLevel level = AutomationHeadingLevel.Level2) => StewardVisuals.SectionHeading(text, level);

    /// <summary>An access key from a label's first letter ("Scan again" → S, "Check for updates" → C).</summary>
    private static string AccessKeyFor(string label) => label.Length == 0 ? string.Empty : label[..1].ToUpperInvariant();
}
