using Microsoft.UI.Xaml;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Persistence.Sqlite;

namespace PCSteward.Desktop;

public partial class App : global::Microsoft.UI.Xaml.Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InventoryExperienceViewModel viewModel = new();
        string stateDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSteward");
        Directory.CreateDirectory(stateDirectory);
        SqliteDatabase database = new(Path.Combine(stateDirectory, "pcsteward.db"));
        database.MigrateAsync(CancellationToken.None).GetAwaiter().GetResult();
        InventoryWorkflow workflow = new(database, database, database, [new RegistryLocalInventoryProvider(), new MsixLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog").IsEnabled), new WingetLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "WinGet installed inventory").IsEnabled)], new BoundedScanEventSink());
        MainWindow window = new(workflow, viewModel);
        window.Activate();
    }
}
