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
    /// <summary>The Data &amp; reset subpage under Settings (Task 73 Phase 4; SPEC §17.3).</summary>
    private StackPanel BuildPrivacyPage()
    {
        StackPanel panel = CreatePagePanel();
        // UI polish phase 4: no eyebrow, so the title and description share one left edge like every other page.
        panel.Children.Add(StewardVisuals.PageHeader("Data & reset", "What PC Steward keeps on this PC, how much space it uses, and separate controls to remove it. Exports are in Settings › Privacy & data."));
        AddDataAndResetSection(panel);
        return panel;
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
            if (reason is not null) body.Children.Add(new TextBlock { Text = reason, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
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
        if (reset.UnavailableReason is { } resetReason) danger.Children.Add(new TextBlock { Text = resetReason, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
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
            // Blame-free message; the sanitized exception text (profile folder, paths and secrets removed) stays under "Technical details".
            await new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Factory reset was not started", Content = StewardVisuals.MessageWithDetails("PC Steward couldn't start the factory reset. Nothing was deleted. Close PC Steward and try again.", StartupNoticeText.SanitizedDetails(exception.Message)), CloseButtonText = "Close" }.ShowAsync();
        }
    }

    private async Task SaveExportAsync(bool json) { try { InventoryExport export = await DataOperationLease.RunReadAsync(token => workflow.CreateExportAsync(json, token), CancellationToken.None); FileSavePicker picker = new(); picker.SuggestedFileName = $"pc-steward-inventory-{export.ExportedAt:yyyyMMdd-HHmmss}"; picker.FileTypeChoices.Add(json ? "JSON inventory" : "CSV inventory", [export.FileExtension]); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); StorageFile? file = await picker.PickSaveFileAsync(); if (file is not null) await FileIO.WriteTextAsync(file, export.Content); } catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); } }

    private async Task ConfirmAndClearScanDataAsync()
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = "Clear scan data?", Content = "This removes current inventory, raw provider records, update candidates, scan-run records and saved diagnostic timelines. It keeps settings, enabled connectors, schedules, update history, downloaded files, mappings, pins and ignores.", PrimaryButtonText = "Clear scan data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); ClearScanDataResult result;
        try { result = await DataOperationLease.RunAsync(token => workflow.ClearScanDataAsync(token), CancellationToken.None); if (result.Succeeded) { viewModel.ClearScanData(); LoadSavedInventory(); ForgetDiagnosticTimelines(); } }
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
}
