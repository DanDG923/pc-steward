using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System.Globalization;
using PCSteward.Application;
using PCSteward.Persistence.Sqlite;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PCSteward.Desktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WinUI owns the window lifetime; scan resources are released after completion.")]
public sealed class MainWindow : Window
{
    private readonly SqliteDatabase database;
    private readonly InventoryExperienceViewModel viewModel = new();
    private readonly InventoryScanOrchestrator orchestrator;
    private readonly InventoryExportService exports;
    private CancellationTokenSource? scanCancellation;
    private ScanDataSummary scanData = new(0, null, 0);
    private bool isClearing;

    public MainWindow()
    {
        Title = AppIdentity.DisplayName;
        string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward");
        Directory.CreateDirectory(stateDirectory);
        database = new SqliteDatabase(Path.Combine(stateDirectory, "pcsteward.db"));
        database.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        orchestrator = new InventoryScanOrchestrator(database, [new RegistryLocalInventoryProvider()]);
        exports = new InventoryExportService(database);
        LoadSavedInventory(); Render();
    }

    private void LoadSavedInventory()
    {
        viewModel.LoadInventory(database.GetLastKnownGoodProductsAsync(CancellationToken.None).GetAwaiter().GetResult(), database.GetLastKnownGoodInstancesAsync(CancellationToken.None).GetAwaiter().GetResult());
        scanData = database.GetScanDataSummaryAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    private void Render()
    {
        StackPanel panel = new() { Spacing = 12, Padding = new Thickness(24), MaxWidth = 1040 };
        panel.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["TitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = "First scan: PC Steward reads installed-app information stored locally by Windows. It does not request elevation, install updates, refresh package sources, or send data to an account. A local-only scan is the safe default.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Connectors — trust category", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
        foreach (ConnectorOption connector in viewModel.Connectors)
        {
            CheckBox checkBox = new() { Content = $"{connector.Name} ({connector.TrustCategory})", IsChecked = connector.IsEnabled, IsEnabled = !connector.IsEssential };
            checkBox.Checked += (_, _) => viewModel.SetConnectorEnabled(connector.Name, true);
            checkBox.Unchecked += (_, _) => viewModel.SetConnectorEnabled(connector.Name, false);
            panel.Children.Add(checkBox);
        }
        StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button scan = new() { Content = "Start local-only scan", IsEnabled = !viewModel.IsScanning };
        scan.Click += async (_, _) => await StartScanAsync(); actions.Children.Add(scan);
        if (viewModel.IsScanning) { Button cancel = new() { Content = "Cancel scan" }; cancel.Click += (_, _) => scanCancellation?.Cancel(); actions.Children.Add(cancel); }
        panel.Children.Add(actions);
        panel.Children.Add(new TextBlock { Text = $"Status: {viewModel.ScanPhase} · elapsed {viewModel.Elapsed:mm\\:ss} · {viewModel.FreshnessLabel}", TextWrapping = TextWrapping.Wrap });

        ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
        TextBox search = new() { PlaceholderText = "Search installed applications", Text = viewModel.SearchText };
        AutomationProperties.SetName(search, "Search installed applications");
        search.TextChanged += (_, _) => { viewModel.SetFilters(search.Text, "All statuses", "All sources", "All scopes", "Name"); RenderInventory(list); };
        panel.Children.Add(search);
        list.ItemClick += (_, args) => ShowDetails((InventoryRow)args.ClickedItem);
        panel.Children.Add(list); RenderInventory(list);
        panel.Children.Add(new TextBlock { Text = "Inventory records are local-only. “Live” and “cached” source checks are not available in this phase.", TextWrapping = TextWrapping.Wrap });
        AddDataAndResetSection(panel);
        Content = new ScrollViewer { Content = panel };
    }

    private void RenderInventory(ListView list)
    {
        list.Items.Clear();
        foreach (InventoryRow row in viewModel.VisibleRows) list.Items.Add(row);
        list.DisplayMemberPath = nameof(InventoryRow.Name);
    }

    private async Task StartScanAsync()
    {
        scanCancellation = new CancellationTokenSource();
        Progress<ScanProgress> progress = new(value => { viewModel.ReportProgress(value); Render(); });
        ScanResult result = await orchestrator.ScanAsync(progress, scanCancellation.Token);
        viewModel.CompleteScan(result, DateTimeOffset.UtcNow); LoadSavedInventory(); scanCancellation.Dispose(); scanCancellation = null; Render();
    }

    private void AddDataAndResetSection(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Data & reset", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = $"Scan data: {scanData.ItemCount} items, {scanData.SizeBytes:N0} bytes. Last cleared: {(scanData.LastClearedAt is null ? "never" : scanData.LastClearedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))}. Clear scan data removes inventory, raw provider records, candidates and scan-run records; it preserves settings, connectors, schedules, update history, downloads, mappings, pins and ignores.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.", TextWrapping = TextWrapping.Wrap });
        StackPanel exportActions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button exportCsv = new() { Content = "Export inventory CSV" }; exportCsv.Click += async (_, _) => await SaveExportAsync(false); exportActions.Children.Add(exportCsv);
        Button exportJson = new() { Content = "Export inventory JSON" }; exportJson.Click += async (_, _) => await SaveExportAsync(true); exportActions.Children.Add(exportJson); panel.Children.Add(exportActions);
        bool destructiveActionsAvailable = !viewModel.IsScanning && !isClearing;
        Button clear = new() { Content = "Clear scan data", IsEnabled = destructiveActionsAvailable };
        AutomationProperties.SetName(clear, "Clear scan data"); clear.Click += async (_, _) => await ConfirmAndClearScanDataAsync(); panel.Children.Add(clear);
        panel.Children.Add(new TextBlock { Text = destructiveActionsAvailable ? "Routine cleanup is available without administrator elevation." : "Cleanup is unavailable while a scan or cleanup is active. Cancel or wait for it to finish.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Future controls", Style = (Style)global::Microsoft.UI.Xaml.Application.Current.Resources["SubtitleTextBlockStyle"] });
        panel.Children.Add(new TextBlock { Text = "Clear downloaded files and Factory reset are not available yet. Factory reset will have its own two-step confirmation and will remove settings and stored decisions; routine cleanup above will not.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new Button { Content = "Clear downloaded files (available in a later phase)", IsEnabled = false });
        panel.Children.Add(new Button { Content = "Factory reset PC Steward (available in a later phase)", IsEnabled = false });
    }

    private async Task SaveExportAsync(bool json)
    {
        InventoryExport export = json ? await exports.CreateJsonAsync(includeUpdateHistory: true, CancellationToken.None) : await exports.CreateCsvAsync(CancellationToken.None);
        FileSavePicker picker = new(); picker.SuggestedFileName = $"pc-steward-inventory-{export.ExportedAt:yyyyMMdd-HHmmss}"; picker.FileTypeChoices.Add(json ? "JSON inventory" : "CSV inventory", [export.FileExtension]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null) await FileIO.WriteTextAsync(file, export.Content);
    }

    private async Task ConfirmAndClearScanDataAsync()
    {
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = "Clear scan data?", Content = "This removes current inventory, raw provider records, update candidates and scan-run records. It keeps settings, enabled connectors, schedules, update history, downloaded files, mappings, pins and ignores.", PrimaryButtonText = "Clear scan data", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        isClearing = true; Render();
        ClearScanDataResult result = await database.ClearScanDataAsync(CancellationToken.None);
        isClearing = false;
        if (result.Succeeded) { viewModel.ClearScanData(); LoadSavedInventory(); }
        ContentDialog outcome = new() { XamlRoot = Content.XamlRoot, Title = result.Succeeded ? "Scan data cleared" : "Scan data was not cleared", Content = result.Message, CloseButtonText = "Close" };
        Render(); await outcome.ShowAsync();
    }

    private async void ShowDetails(InventoryRow row)
    {
        string instances = string.Join("\n", row.Instances.Select(instance => $"{instance.Provider}: {instance.ProviderIdentity}; {instance.Version ?? "unknown version"}; {instance.Scope}; {instance.Architecture}"));
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = row.Name, Content = $"Installed instances and raw-provider provenance:\n{instances}\n\nUpdate candidate data is not available in Phase 1.", CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }
}
