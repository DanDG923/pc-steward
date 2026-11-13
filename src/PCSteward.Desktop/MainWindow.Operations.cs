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

public sealed partial class MainWindow
{
    private async Task ChangeWinGetTrustedSourcesAsync(bool enabled)
    {
        bool approved = false;
        if (enabled)
        {
            ContentDialog consent = new()
            {
                XamlRoot = Content.XamlRoot,
                Title = SettingsPresenter.SourceSettingLabel + "?",
                Content = "To look for updates, PC Steward asks WinGet and Chocolatey Community about the packages installed on this PC, so they may receive installed-package identifiers. Chocolatey packages can run package-maintainer scripts during an update; Chocolatey updates are machine-wide and cannot be started in this version. " + ElevationStory.SourceConsent,
                PrimaryButtonText = "I approve",
                CloseButtonText = "Keep off",
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
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "The setting was not changed", Content = "PC Steward couldn't save this setting. Your previous setting is back in place.", CloseButtonText = "Close" }.ShowAsync();
        }
        Render();
    }

    private async Task ChangeConnectorAsync(string connectorName, bool enabled)
    {
        bool approved = false;
        ConnectorOption connector = viewModel.Connectors.Single(option => option.Name == connectorName);
        if (connector.RequiresSourceDisclosure && enabled)
        {
            ContentDialog consent = new() { XamlRoot = Content.XamlRoot, Title = $"Enable {connectorName}?", Content = "This source may receive installed-software identifiers. Turning it on doesn't install or update anything.", PrimaryButtonText = "I approve", CloseButtonText = "Keep off", DefaultButton = ContentDialogButton.Close };
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
        // UI review 2026-11-13 (item 21): a scan of a PC that has no app list yet is the first scan; Home asks about updates after it.
        bool nothingScannedYet = viewModel.LastScanAt is null && viewModel.AllRows.Count == 0;
        CancellationTokenSource cancellation = new();
        scanCancellation = cancellation;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        scanStartedAt = startedAt;
        Render();
        StartWorkProgressTimer();
        Progress<ScanProgress> progress = new(value => { viewModel.ReportProgress(value); RefreshOperationVisuals(); });
        // Providers and SQLite complete asynchronously.  Keep their continuations off the
        // WinUI dispatcher; Progress posts the small UI updates back to it explicitly.
        ScanResult? completed = null;
        try
        {
            completed = await DataOperationLease.RunAsync(token => workflow.ScanAsync(progress, token), cancellation.Token);
            viewModel.CompleteScan(completed, DateTimeOffset.UtcNow);
            // What the next scan's time left is estimated from.
            if (completed.Succeeded) lastScanDuration = DateTimeOffset.UtcNow - startedAt;
            LoadSavedInventory();
            if (completed.Succeeded && nothingScannedYet) firstScanThisSession = true;
        }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
        finally { scanStartedAt = null; if (ReferenceEquals(scanCancellation, cancellation)) scanCancellation = null; cancellation.Dispose(); if (completed?.Succeeded == true && navigation.Current == ShellPage.Scan) NavigateTo(FirstRunPresenter.AfterScan(AsksAboutUpdates)); else Render(); }
        if (completed is not null && !windowClosed) AnnounceCompletion(OperationAnnouncements.ScanFinished(completed.Succeeded, completed.Cancelled, viewModel.AllRows.Count), "scan-finished");
    }

    private async Task CheckUpdateSourcesAsync()
    {
        string? detailStableProductKey = navigation.Current == ShellPage.Detail || detailPaneOpen ? detailRow?.StableProductKey : null;
        // Independent review L2: this method takes no cancellation token and the band's Cancel only cancels a scan, so the source
        // refresh keeps its existing non-cancellable behaviour (bounded by the refresh service's own timeouts). Not changed here.
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        updateCheckStartedAt = startedAt;
        viewModel.BeginUpdateCheck(); Render();
        StartWorkProgressTimer();
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
        finally { viewModel.CompleteUpdateCheck(); updateCheckStartedAt = null; }
        // What the next check's time left is estimated from (kept for this session only).
        if (refreshed) lastUpdateCheckDuration = DateTimeOffset.UtcNow - startedAt;
        Render();
        if (refreshed && !windowClosed)
            AnnounceCompletion(OperationAnnouncements.UpdateCheckFinished(
                viewModel.AllRows.Count(row => row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate)),
                viewModel.AllRows.Count(row => row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired))), "update-check-finished");
    }

    // Owner request 2026-11-13 (FR-UI-01/05): a bar that fills while a scan or an update check runs, with about how long is left.
    private DateTimeOffset? scanStartedAt;
    private DateTimeOffset? updateCheckStartedAt;
    private TimeSpan? lastScanDuration;
    private TimeSpan? lastUpdateCheckDuration;
    private DispatcherQueueTimer? workProgressTimer;
    private StackPanel? pageWorkProgress;
    private ProgressBar? pageWorkProgressBar;
    private TextBlock? pageWorkProgressText;

    /// <summary>
    /// How far along the running scan or update check is; <see langword="null"/> when neither is running. The time left is an
    /// estimate from the last completed scan (this session's, else the most recent recorded one) or this session's last update check.
    /// </summary>
    private WorkProgressPresentation? CurrentWorkProgress()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (scanStartedAt is { } scan && (viewModel.IsScanning || scanCancellation is not null))
            return WorkProgressPresenter.Scan(viewModel.ScanStep, viewModel.ScanSteps, now - scan, lastScanDuration ?? WorkProgressPresenter.LastScanDuration(scanRuns));
        if (updateCheckStartedAt is { } check && viewModel.IsCheckingUpdates)
            return WorkProgressPresenter.UpdateCheck(now - check, lastUpdateCheckDuration);
        return null;
    }

    /// <summary>The bar and its one line for the page being shown (Home's status card or the Scan page); hidden when nothing is running.</summary>
    private StackPanel WorkProgressPanel()
    {
        ProgressBar bar = new() { Minimum = 0, Maximum = 100, Height = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(bar, "Progress");
        TextBlock text = new() { Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap };
        StackPanel panel = new() { Spacing = StewardSpacing.XSmall };
        panel.Children.Add(bar);
        panel.Children.Add(text);
        pageWorkProgress = panel;
        pageWorkProgressBar = bar;
        pageWorkProgressText = text;
        ShowWorkProgress();
        return panel;
    }

    /// <summary>Puts the current progress on the page's bar and on the bar in the band at the bottom of the window. It starts and saves nothing.</summary>
    private void ShowWorkProgress()
    {
        WorkProgressPresentation? work = CurrentWorkProgress();
        if (pageWorkProgress is not null) pageWorkProgress.Visibility = work is null ? Visibility.Collapsed : Visibility.Visible;
        if (pageWorkProgressBar is not null && work is not null) { pageWorkProgressBar.IsIndeterminate = work.IsIndeterminate; pageWorkProgressBar.Value = work.Percent; }
        if (pageWorkProgressText is not null && work is not null && !string.Equals(pageWorkProgressText.Text, work.Line, StringComparison.Ordinal)) pageWorkProgressText.Text = work.Line;
        // Any other work (an update, a clean-up, a save) has nothing to measure against, so the band's bar keeps moving without an end.
        if (operationProgress is not null) { operationProgress.IsIndeterminate = work is null || work.IsIndeterminate; operationProgress.Value = work?.Percent ?? 0; }
    }

    /// <summary>Refreshes the bars twice a second while a scan or an update check runs, and stops itself when neither is.</summary>
    private void StartWorkProgressTimer()
    {
        if (workProgressTimer is null)
        {
            workProgressTimer = DispatcherQueue.CreateTimer();
            workProgressTimer.Interval = TimeSpan.FromMilliseconds(500);
            workProgressTimer.Tick += (_, _) =>
            {
                if (!windowClosed) ShowWorkProgress();
                if (windowClosed || CurrentWorkProgress() is null) workProgressTimer?.Stop();
            };
        }
        workProgressTimer.Start();
    }
}
