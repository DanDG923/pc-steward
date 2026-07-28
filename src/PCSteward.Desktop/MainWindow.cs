using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using PCSteward.Application;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PCSteward.Desktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WinUI owns the window lifetime and scan cancellation is disposed after every completed scan.")]
public sealed class MainWindow : Window
{
    private const string WingetConnector = "WinGet installed inventory";
    private readonly InventoryWorkflow workflow;
    private readonly InventoryExperienceViewModel viewModel;
    private CancellationTokenSource? scanCancellation;
    private ScanDataSummary scanData = new(0, null, 0);
    private bool isClearing;

    public MainWindow(InventoryWorkflow workflow, InventoryExperienceViewModel viewModel)
    {
        this.workflow = workflow; this.viewModel = viewModel; Title = AppIdentity.DisplayName; LoadSavedInventory(); Render();
    }

    private void LoadSavedInventory()
    {
        InventoryDashboard dashboard = workflow.LoadAsync(viewModel.Connectors.ToArray(), CancellationToken.None).GetAwaiter().GetResult();
        foreach (ConnectorOption connector in viewModel.Connectors.ToArray()) viewModel.SetConnectorEnabled(connector.Name, dashboard.Connectors[connector.Name].Enabled);
        viewModel.LoadInventory(dashboard.Products, dashboard.Instances, dashboard.LastCompletedAt); scanData = dashboard.ScanData;
    }

    private void Render()
    {
        StackPanel panel = new() { Spacing = 12, Padding = new Thickness(24), MaxWidth = 1040 };
        panel.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = "First scan reads installed-app information stored locally by Windows. It does not request elevation, install updates, refresh package sources, or send data to an account. Local-only Windows inventory is the safe default.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Connectors — trust category", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
        foreach (ConnectorOption connector in viewModel.Connectors)
        {
            bool isWinget = connector.Name == WingetConnector;
            CheckBox checkBox = new() { Content = isWinget ? "WinGet installed inventory (requires source-disclosure approval)" : $"{connector.Name} ({connector.TrustCategory})", IsChecked = connector.IsEnabled, IsEnabled = !connector.IsEssential };
            AutomationProperties.SetName(checkBox, isWinget ? "Enable WinGet installed inventory after source-disclosure approval" : $"Enable {connector.Name}");
            checkBox.Checked += async (_, _) => await ChangeConnectorAsync(connector.Name, true);
            checkBox.Unchecked += async (_, _) => await ChangeConnectorAsync(connector.Name, false);
            panel.Children.Add(checkBox);
        }
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button scan = new() { Content = viewModel.IsWinGetEnabled ? "Start approved WinGet inventory scan" : "Start local-only scan", IsEnabled = !viewModel.IsScanning };
        AutomationProperties.SetName(scan, scan.Content.ToString()); scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning) { Button cancel = new() { Content = "Cancel scan" }; AutomationProperties.SetName(cancel, "Cancel scan"); cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = $"Status: {viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss} · {viewModel.FreshnessLabel}", TextWrapping = TextWrapping.Wrap });

        // A finite list viewport preserves ListView virtualization inside the outer page
        // scroller; an unconstrained ListView measures every installed product repeatedly.
        ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 520 };
        TextBox search = new() { PlaceholderText = "Search installed applications", Text = viewModel.SearchText }; AutomationProperties.SetName(search, "Search installed applications");
        ComboBox status = FilterBox(viewModel.StatusFilter, "All statuses", "Installed"); ComboBox publisher = FilterBox(viewModel.PublisherFilter, ["All publishers", ..viewModel.AvailablePublishers]); ComboBox source = FilterBox(viewModel.SourceFilter, "All sources", "registry", "msix", "winget"); ComboBox scope = FilterBox(viewModel.ScopeFilter, "All scopes", "Machine", "User", "Unknown"); ComboBox sort = FilterBox(viewModel.SortField, "Name", "Source", "Publisher");
        AutomationProperties.SetName(status, "Filter by status"); AutomationProperties.SetName(publisher, "Filter by publisher"); AutomationProperties.SetName(source, "Filter by source"); AutomationProperties.SetName(scope, "Filter by scope"); AutomationProperties.SetName(sort, "Sort inventory");
        void ApplyFilters() { viewModel.SetFilters(search.Text, status.SelectedItem?.ToString(), publisher.SelectedItem?.ToString(), source.SelectedItem?.ToString(), scope.SelectedItem?.ToString(), sort.SelectedItem?.ToString()); RenderInventory(list); }
        search.TextChanged += (_, _) => ApplyFilters(); status.SelectionChanged += (_, _) => ApplyFilters(); publisher.SelectionChanged += (_, _) => ApplyFilters(); source.SelectionChanged += (_, _) => ApplyFilters(); scope.SelectionChanged += (_, _) => ApplyFilters(); sort.SelectionChanged += (_, _) => ApplyFilters();
        panel.Children.Add(search); panel.Children.Add(status); panel.Children.Add(publisher); panel.Children.Add(source); panel.Children.Add(scope); panel.Children.Add(sort);
        list.ItemClick += async (_, args) => await ShowDetailsAsync((InventoryRow)args.ClickedItem); panel.Children.Add(list); RenderInventory(list);
        panel.Children.Add(new TextBlock { Text = viewModel.IsWinGetEnabled ? "WinGet installed-inventory collection is enabled after explicit approval. No update execution is available in this phase." : "Inventory records are local-only. Live/cached source checks are not available in this phase.", TextWrapping = TextWrapping.Wrap });
        AddDataAndResetSection(panel); Content = new ScrollViewer { Content = panel };
    }

    private static ComboBox FilterBox(string selected, params string[] values) => FilterBox(selected, (IReadOnlyCollection<string>)values);
    private static ComboBox FilterBox(string selected, IReadOnlyCollection<string> values) { ComboBox box = new(); foreach (string value in values.Distinct(StringComparer.OrdinalIgnoreCase)) box.Items.Add(value); box.SelectedItem = values.FirstOrDefault(value => string.Equals(value, selected, StringComparison.OrdinalIgnoreCase)) ?? values.First(); return box; }
    private void RenderInventory(ListView list) { list.Items.Clear(); foreach (InventoryRow row in viewModel.VisibleRows) list.Items.Add(row); list.DisplayMemberPath = nameof(InventoryRow.Name); }

    private async Task ChangeConnectorAsync(string connectorName, bool enabled)
    {
        bool approved = false;
        if (connectorName == WingetConnector && enabled)
        {
            ContentDialog consent = new() { XamlRoot = Content.XamlRoot, Title = "Enable WinGet installed inventory?", Content = "WinGet may disclose installed-software identifiers to configured package sources. It remains read-only and does not install or update software.", PrimaryButtonText = "I approve", CloseButtonText = "Keep local-only" };
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

    private void AddDataAndResetSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Data & reset", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = $"Scan data: {scanData.ItemCount} items, estimated payload {scanData.SizeBytes:N0} characters. Last cleared: {(scanData.LastClearedAt is null ? "never" : scanData.LastClearedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}. Clear scan data removes inventory, raw provider records, candidates and scan-run records; it preserves settings, connectors, schedules, update history, downloads, mappings, pins and ignores.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.", TextWrapping = TextWrapping.Wrap });
        StackPanel exportActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 }; Button exportCsv = new() { Content = "Export inventory CSV" }; exportCsv.Click += async (_, _) => await SaveExportAsync(false); exportActions.Children.Add(exportCsv); Button exportJson = new() { Content = "Export inventory JSON" }; exportJson.Click += async (_, _) => await SaveExportAsync(true); exportActions.Children.Add(exportJson); panel.Children.Add(exportActions);
        bool available = !viewModel.IsScanning && !isClearing; Button clear = new() { Content = "Clear scan data", IsEnabled = available }; AutomationProperties.SetName(clear, "Clear scan data"); clear.Click += async (_, _) => await ConfirmAndClearScanDataAsync(); panel.Children.Add(clear);
        panel.Children.Add(new TextBlock { Text = available ? "Routine cleanup is available without administrator elevation." : "Cleanup is unavailable while a scan or cleanup is active. Cancel or wait for it to finish.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Future controls", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] }); panel.Children.Add(new TextBlock { Text = "Clear downloaded files and Factory reset are not available yet. Factory reset will have its own two-step confirmation and will remove settings and stored decisions; routine cleanup above will not.", TextWrapping = TextWrapping.Wrap }); panel.Children.Add(new Button { Content = "Clear downloaded files (available in a later phase)", IsEnabled = false }); panel.Children.Add(new Button { Content = "Factory reset PC Steward (available in a later phase)", IsEnabled = false });
    }

    private async Task SaveExportAsync(bool json) { InventoryExport export = await workflow.CreateExportAsync(json, CancellationToken.None); FileSavePicker picker = new(); picker.SuggestedFileName = $"pc-steward-inventory-{export.ExportedAt:yyyyMMdd-HHmmss}"; picker.FileTypeChoices.Add(json ? "JSON inventory" : "CSV inventory", [export.FileExtension]); InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this)); StorageFile? file = await picker.PickSaveFileAsync(); if (file is not null) await FileIO.WriteTextAsync(file, export.Content); }

    private async Task ConfirmAndClearScanDataAsync()
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = "Clear scan data?", Content = "This removes current inventory, raw provider records, update candidates and scan-run records. It keeps settings, enabled connectors, schedules, update history, downloaded files, mappings, pins and ignores.", PrimaryButtonText = "Clear scan data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }; if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render(); ClearScanDataResult result = await workflow.ClearScanDataAsync(CancellationToken.None); isClearing = false; if (result.Succeeded) { viewModel.ClearScanData(); LoadSavedInventory(); } ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? "Scan data cleared" : "Scan data was not cleared", Content = result.Message, CloseButtonText = "Close" }; Render(); await outcome.ShowAsync();
    }

    private async Task ShowDetailsAsync(InventoryRow row)
    {
        string instances = string.Join("\n", row.Instances.Select(instance => $"{instance.Provider}: {instance.ProviderIdentity}; {instance.Version ?? "unknown version"}; {instance.Scope}; {instance.Architecture}; publisher {instance.Publisher ?? "unknown"}; {instance.Classification}"));
        IReadOnlyCollection<PCSteward.Domain.RawProviderRecord> raw = await workflow.GetRawDiagnosticsAsync(row.ProductId, CancellationToken.None); ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = row.Name, Content = $"Installed instances and provider provenance:\n{instances}\n\nBounded raw diagnostic records: {raw.Count}. Update candidate data is not available in Phase 1.", CloseButtonText = "Close" }; await dialog.ShowAsync();
    }
}
