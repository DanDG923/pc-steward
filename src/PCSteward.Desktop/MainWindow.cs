using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using PCSteward.Application;
using PCSteward.Storage;
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
    private CancellationTokenSource? scanCancellation;
    private ScanDataSummary scanData = new(0, null, 0);
    private IReadOnlyCollection<PCSteward.Domain.StableDecision> activeDecisions = [];
    private IReadOnlyCollection<PCSteward.Domain.ManualMapping> manualMappings = [];
    private bool isClearing;
    private bool isExecuting;
    private DownloadCacheSummary downloadCache = new(0, 0, 0, 0, null);

    public MainWindow(InventoryWorkflow workflow, InventoryExperienceViewModel viewModel, DownloadedArtifactCache downloads, ReviewedUpdateExecutionWorkflow updates)
    {
        this.workflow = workflow; this.viewModel = viewModel; this.downloads = downloads; this.updates = updates; Title = AppIdentity.DisplayName; LoadSavedInventory(); Render();
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
    }

    private void Render()
    {
        StackPanel panel = new() { Spacing = 12, Padding = new Thickness(24), MaxWidth = 1040 };
        panel.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = "First scan reads installed-app information stored locally by Windows. It does not request elevation, install updates, refresh package sources, or send data to an account. Local-only Windows inventory is the safe default.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Connectors — trust category", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
        foreach (ConnectorOption connector in viewModel.Connectors)
        {
            CheckBox checkBox = new() { Content = connector.RequiresSourceDisclosure ? $"{connector.Name} (requires source-disclosure approval)" : $"{connector.Name} ({connector.TrustCategory})", IsChecked = connector.IsEnabled, IsEnabled = !connector.IsEssential };
            AutomationProperties.SetName(checkBox, connector.RequiresSourceDisclosure ? $"Enable {connector.Name} after source-disclosure approval" : $"Enable {connector.Name}");
            checkBox.Checked += async (_, _) => await ChangeConnectorAsync(connector.Name, true);
            checkBox.Unchecked += async (_, _) => await ChangeConnectorAsync(connector.Name, false);
            panel.Children.Add(checkBox);
        }
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button scan = new() { Content = viewModel.IsWinGetEnabled ? "Start approved WinGet inventory scan" : "Start local-only scan", IsEnabled = !viewModel.IsScanning && !isExecuting };
        AutomationProperties.SetName(scan, scan.Content.ToString()); scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        Button checkUpdates = new() { Content = "Check approved update sources", IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !isExecuting };
        AutomationProperties.SetName(checkUpdates, "Check approved update sources"); checkUpdates.Click += async (_, _) => await CheckUpdateSourcesAsync(); actions.Children.Add(checkUpdates);
        if (viewModel.IsScanning) { Button cancel = new() { Content = "Cancel scan", AccessKey = "C" }; AutomationProperties.SetName(cancel, "Cancel scan"); cancel.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() => cancel.Focus(FocusState.Programmatic)); cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = $"Status: {viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss} · {viewModel.FreshnessLabel}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Update intelligence: {viewModel.UpdateFreshnessLabel} Verified {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate]}, review {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired]}, current {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.Current]}, unmanaged {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged]}, pinned {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.Pinned]}, ignored {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.Ignored]}, unavailable {viewModel.UpdateStatusCounts[PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable]}. Attention needed: {viewModel.AttentionNeeded.Count}.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = $"Source cache retention: {workflow.SourceCacheRetention.TotalDays:0} days. {(viewModel.IsUpdateSourceEnabled ? "Use Check approved update sources to refresh without installing anything." : "Enable approved update sources to check metadata.")}", TextWrapping = TextWrapping.Wrap });

        // A finite list viewport preserves ListView virtualization inside the outer page
        // scroller; an unconstrained ListView measures every installed product repeatedly.
        ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 520 };
        TextBox search = new() { PlaceholderText = "Search installed applications", Text = viewModel.SearchText }; AutomationProperties.SetName(search, "Search installed applications");
        ComboBox status = FilterBox(viewModel.StatusFilter, ["All statuses", "Installed", ..Enum.GetNames<PCSteward.Domain.UpdateIntelligenceStatus>()]); ComboBox publisher = FilterBox(viewModel.PublisherFilter, ["All publishers", ..viewModel.AvailablePublishers]); ComboBox source = FilterBox(viewModel.SourceFilter, "All sources", "registry", "msix", "winget"); ComboBox scope = FilterBox(viewModel.ScopeFilter, "All scopes", "Machine", "User", "Unknown"); ComboBox sort = FilterBox(viewModel.SortField, "Name", "Source", "Publisher");
        AutomationProperties.SetName(status, "Filter by status"); AutomationProperties.SetName(publisher, "Filter by publisher"); AutomationProperties.SetName(source, "Filter by source"); AutomationProperties.SetName(scope, "Filter by scope"); AutomationProperties.SetName(sort, "Sort inventory");
        void ApplyFilters() { viewModel.SetFilters(search.Text, status.SelectedItem?.ToString(), publisher.SelectedItem?.ToString(), source.SelectedItem?.ToString(), scope.SelectedItem?.ToString(), sort.SelectedItem?.ToString()); RenderInventory(list); }
        search.TextChanged += (_, _) => ApplyFilters(); status.SelectionChanged += (_, _) => ApplyFilters(); publisher.SelectionChanged += (_, _) => ApplyFilters(); source.SelectionChanged += (_, _) => ApplyFilters(); scope.SelectionChanged += (_, _) => ApplyFilters(); sort.SelectionChanged += (_, _) => ApplyFilters();
        panel.Children.Add(search); panel.Children.Add(status); panel.Children.Add(publisher); panel.Children.Add(source); panel.Children.Add(scope); panel.Children.Add(sort);
        list.ItemClick += async (_, args) => await ShowDetailsAsync((InventoryRow)args.ClickedItem); panel.Children.Add(list); RenderInventory(list);
        panel.Children.Add(new TextBlock { Text = isExecuting ? "An approved update is running. Scans and cleanup are unavailable until its controlled verification completes." : viewModel.IsUpdateSourceEnabled ? "Verified updates can be opened from product details for a separate review and confirmation. Cached or unavailable source evidence cannot be executed." : "Inventory is local-only until you explicitly enable approved update sources.", TextWrapping = TextWrapping.Wrap });
        AddDecisionsSection(panel);
        AddDataAndResetSection(panel); Content = new ScrollViewer { Content = panel };
    }

    private static ComboBox FilterBox(string selected, params string[] values) => FilterBox(selected, (IReadOnlyCollection<string>)values);
    private static ComboBox FilterBox(string selected, IReadOnlyCollection<string> values) { ComboBox box = new(); foreach (string value in values.Distinct(StringComparer.OrdinalIgnoreCase)) box.Items.Add(value); box.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values.First(); return box; }
    private void RenderInventory(ListView list) { list.Items.Clear(); foreach (InventoryRow row in viewModel.VisibleRows) list.Items.Add(row); list.DisplayMemberPath = nameof(InventoryRow.Name); }

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
        panel.Children.Add(new TextBlock { Text = "Data & reset", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
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
        panel.Children.Add(new TextBlock { Text = "Future controls", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] }); panel.Children.Add(new TextBlock { Text = "Factory reset will have its own two-step confirmation and will remove settings and stored decisions; routine cleanup above will not.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(new Button { Content = "Factory reset PC Steward (available in a later phase)", IsEnabled = false });
    }

    private void AddDecisionsSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Update decisions", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
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

    private async Task ShowDetailsAsync(InventoryRow row)
    {
        string instances = string.Join("\n", row.Instances.Select(instance => $"{instance.Provider}: {instance.ProviderIdentity}; {instance.Version ?? "unknown version"}; {instance.Scope}; {instance.Architecture}; channel {instance.Channel ?? "unknown"}; publisher {instance.Publisher ?? "unknown"}; {instance.Classification}"));
        IReadOnlyCollection<PCSteward.Domain.RawProviderRecord> raw = await workflow.GetRawDiagnosticsAsync(row.ProductId, CancellationToken.None); PCSteward.Application.UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey); IReadOnlyCollection<PCSteward.Domain.ProductTimelineEntry> timeline = await workflow.GetTimelineAsync(row.StableProductKey, CancellationToken.None); string intelligence = detail is null ? "No source candidate has been loaded. A source check is required before mapping or update review." : $"Installed {detail.InstalledVersion}; candidate {detail.CandidateVersion ?? "none"}; source {detail.SourceId ?? "none"}; stable package ID {detail.PackageId ?? "none"}; confidence {detail.Confidence}; architecture {detail.Architecture}; channel {detail.Channel ?? "unknown"}.\n{detail.Explanation}\nFreshness: {detail.Freshness}\nSource candidates:\n{string.Join("\n", detail.SourceCandidates)}\n{detail.ActionState}";
        TextBox reason = new() { Header = "Optional decision reason", PlaceholderText = "Why are you pinning, ignoring or mapping this product?", MaxLength = 512 };
        CheckBox ignoreUntilEnabled = new() { Content = "Ignore until a selected date" }; DatePicker ignoreUntil = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) }; ignoreUntilEnabled.Checked += (_, _) => ignoreUntil.IsEnabled = true; ignoreUntilEnabled.Unchecked += (_, _) => ignoreUntil.IsEnabled = false;
        TextBlock mappingOutcome = new() { TextWrapping = TextWrapping.Wrap };
        ComboBox mappingSource = FilterBox(detail?.SourceId ?? "winget", "winget", "msstore"); TextBox mappingPackage = new() { Header = "Exact approved source package ID", PlaceholderText = detail?.PackageId ?? "For example: Contoso.Widget" };
        StackPanel content = new() { Spacing = 8 }; content.Children.Add(new TextBlock { Text = $"Installed instances and provider provenance:\n{instances}\n\nUpdate intelligence:\n{intelligence}\n\nTimeline:\n{string.Join("\n", timeline.Select(entry => $"{entry.OccurredAt:O} — {entry.Summary}"))}\n\nBounded raw diagnostic records: {raw.Count}.", TextWrapping = TextWrapping.Wrap }); content.Children.Add(reason); content.Children.Add(ignoreUntilEnabled); content.Children.Add(ignoreUntil); content.Children.Add(mappingSource); content.Children.Add(mappingPackage);
        ScrollViewer dialogContent = new() { Content = content, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Button map = new() { Content = "Map an exact approved package", IsEnabled = viewModel.IsUpdateSourceEnabled }; AutomationProperties.SetName(map, "Create manual mapping for an exact approved source package"); map.Click += async (_, _) => { PCSteward.Domain.ManualMappingValidation validation = await workflow.CreateManualMappingAsync(row.StableProductKey, mappingSource.SelectedItem?.ToString() ?? "winget", string.IsNullOrWhiteSpace(mappingPackage.Text) ? detail?.PackageId ?? string.Empty : mappingPackage.Text, viewModel.IsUpdateSourceEnabled, reason.Text, CancellationToken.None); mappingOutcome.Text = validation.Explanation; }; content.Children.Add(map);
        if (detail is { SourceId: not null, PackageId: not null })
        {
            Button pinSource = new() { Content = "Pin this source package" }; AutomationProperties.SetName(pinSource, "Pin this source package"); pinSource.Click += async (_, _) => { await workflow.PinSourcePackageAsync(row.StableProductKey, detail.SourceId, detail.PackageId, reason.Text, CancellationToken.None); mappingOutcome.Text = "The source package is pinned. Remove the decision to resume normal review."; }; content.Children.Add(pinSource);
        }
        if (row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString() && viewModel.IsUpdateSourceEnabled && !isExecuting)
        {
            Button review = new() { Content = "Review and update" };
            AutomationProperties.SetName(review, "Review and update this verified package");
            content.Children.Add(review);
            ContentDialog? reviewDialog = null;
            bool reviewStarted = false;
            review.Click += async (_, _) => { reviewStarted = true; reviewDialog?.Hide(); await ReviewAndExecuteAsync(row); };
            content.Children.Add(mappingOutcome);
            reviewDialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = row.Name, Content = dialogContent, PrimaryButtonText = "Pin current version", SecondaryButtonText = "Ignore product", CloseButtonText = "Close" };
            ContentDialogResult reviewResult = await reviewDialog.ShowAsync(); if (reviewStarted) return; if (reviewResult == ContentDialogResult.Primary) await workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, reason.Text, CancellationToken.None); else if (reviewResult == ContentDialogResult.Secondary) await workflow.IgnoreAsync(row.StableProductKey, ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null, reason.Text, CancellationToken.None); LoadSavedInventory(); Render();
            return;
        }
        content.Children.Add(mappingOutcome);
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = row.Name, Content = dialogContent, PrimaryButtonText = "Pin current version", SecondaryButtonText = "Ignore product", CloseButtonText = "Close" }; ContentDialogResult result = await dialog.ShowAsync(); if (result == ContentDialogResult.Primary) await workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, reason.Text, CancellationToken.None); else if (result == ContentDialogResult.Secondary) await workflow.IgnoreAsync(row.StableProductKey, ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null, reason.Text, CancellationToken.None); LoadSavedInventory(); Render();
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
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Review approved update",
            Content = $"Package: {selection.PackageId}\nInstalled version: {selection.InstalledVersion}\nTarget version: {selection.CandidateVersion}\nSource: {selection.Source}\nScope: {selection.Scope}\nArchitecture: {selection.Architecture}\n\nClose the application first if it is running. PC Steward will not close applications. {elevation}\n\nProvider completion is not marked successful until the controlled post-update inventory verifies the target version.",
            PrimaryButtonText = "Install approved update",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;

        isExecuting = true;
        Render();
        ReviewedUpdateExecutionResult result;
        try { result = await updates.ConfirmAndRunAsync(preparation.Review, CancellationToken.None); }
        catch (Exception) { result = new(false, "PC Steward could not start the reviewed update. No success was recorded.", null); }
        finally { isExecuting = false; LoadSavedInventory(); Render(); }
        await ShowUpdateOutcomeAsync(result.Started ? "Update completed" : "Update not started", result.Message);
    }

    private Task<ContentDialogResult> ShowUpdateOutcomeAsync(string title, string message) => new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync().AsTask();
}
