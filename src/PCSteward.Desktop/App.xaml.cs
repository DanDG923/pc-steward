using Microsoft.UI.Xaml;
using PCSteward.Application;
using PCSteward.Diagnostics;
using PCSteward.Execution;
using PCSteward.Persistence.Sqlite;
using PCSteward.Provider.WinGet;
using PCSteward.Storage;

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
        List<ILocalInventoryProvider> providers = [];
        ILocalInventoryProvider? evidenceDelay = EvidenceDelayInventoryProvider.FromEnvironment();
        if (evidenceDelay is not null) providers.Add(evidenceDelay);
        providers.AddRange([new RegistryLocalInventoryProvider(), new MsixLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog").IsEnabled), new WingetLocalInventoryProvider(() => viewModel.Connectors.Single(connector => connector.Name == "WinGet installed inventory").IsEnabled)]);
        IReadOnlyCollection<IUpdateSourceConnector> updateSources = [
            new WingetUpdateSourceConnector("winget"),
            new WingetUpdateSourceConnector("msstore")
        ];
        InventoryWorkflow workflow = new(
            database,
            database,
            database,
            providers,
            new BoundedScanEventSink(),
            database,
            database,
            updateSources,
            new WingetPostUpdatePackageInventory(),
            new WingetStructuredUpdateCandidateAuthority());
        DownloadedArtifactCache downloads = new(database);
        ApprovedUpdateExecutor executor = new(new NonElevatedUpdateRunner(new WingetUpdateExecutor()), new ElevatedUpdateRunner());
        PackageDerivedRunningApplicationDetector runningApplications = new();
        SequentialUpdateBatchCoordinator coordinator = new(executor, workflow, database, runningApplications);
        ReviewedUpdateExecutionWorkflow updates = new(workflow.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database), coordinator, runningApplications);
        MainWindow window = new(workflow, viewModel, downloads, updates);
        window.Activate();
    }
}
