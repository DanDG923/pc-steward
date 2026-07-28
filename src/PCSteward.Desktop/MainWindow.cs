using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Desktop;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "WinUI owns the window lifetime; scan resources are released after completion.")]
public sealed class MainWindow : Window
{
    private readonly SqliteDatabase database;
    private readonly InventoryExperienceViewModel viewModel = new();
    private readonly InventoryScanOrchestrator orchestrator;
    private CancellationTokenSource? scanCancellation;

    public MainWindow()
    {
        Title = AppIdentity.DisplayName;
        string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward");
        Directory.CreateDirectory(stateDirectory);
        database = new SqliteDatabase(Path.Combine(stateDirectory, "pcsteward.db"));
        database.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        orchestrator = new InventoryScanOrchestrator(database, [new RegistryLocalInventoryProvider()]);
        LoadSavedInventory(); Render();
    }

    private void LoadSavedInventory()
    {
        viewModel.LoadInventory(database.GetLastKnownGoodProductsAsync(CancellationToken.None).GetAwaiter().GetResult(), database.GetLastKnownGoodInstancesAsync(CancellationToken.None).GetAwaiter().GetResult());
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

    private async void ShowDetails(InventoryRow row)
    {
        string instances = string.Join("\n", row.Instances.Select(instance => $"{instance.Provider}: {instance.ProviderIdentity}; {instance.Version ?? "unknown version"}; {instance.Scope}; {instance.Architecture}"));
        ContentDialog dialog = new() { XamlRoot = Content.XamlRoot, Title = row.Name, Content = $"Installed instances and raw-provider provenance:\n{instances}\n\nUpdate candidate data is not available in Phase 1.", CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }
}
