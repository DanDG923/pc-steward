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
        CancellationTokenSource cancellation = new();
        scanCancellation = cancellation;
        Render();
        Progress<ScanProgress> progress = new(value => { viewModel.ReportProgress(value); RefreshOperationVisuals(); });
        // Providers and SQLite complete asynchronously.  Keep their continuations off the
        // WinUI dispatcher; Progress posts the small UI updates back to it explicitly.
        ScanResult? completed = null;
        try { completed = await DataOperationLease.RunAsync(token => workflow.ScanAsync(progress, token), cancellation.Token); viewModel.CompleteScan(completed, DateTimeOffset.UtcNow); LoadSavedInventory(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
        finally { if (ReferenceEquals(scanCancellation, cancellation)) scanCancellation = null; cancellation.Dispose(); if (completed?.Succeeded == true && navigation.Current == ShellPage.Scan) NavigateTo(ShellPage.InstalledApps); else Render(); }
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
}
