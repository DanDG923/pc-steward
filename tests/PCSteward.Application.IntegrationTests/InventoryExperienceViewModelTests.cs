using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class InventoryExperienceViewModelTests
{
    [TestMethod]
    public void EssentialConnectorCannotBeDisabledButNonessentialConnectorCan()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.SetConnectorEnabled("Windows uninstall registry", false);
        viewModel.SetConnectorEnabled("Current-user AppX/MSIX catalog", false);
        Assert.IsTrue(viewModel.Connectors.Single(connector => connector.Name == "Windows uninstall registry").IsEnabled);
        Assert.IsFalse(viewModel.Connectors.Single(connector => connector.Name == "Current-user AppX/MSIX catalog").IsEnabled);
    }

    [TestMethod]
    public void SearchAndSourceFilterKeepDistinctInstallationInstancesInspectable()
    {
        InventoryExperienceViewModel viewModel = new();
        Guid productId = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(productId, "alpha", "Alpha Tool"), new CanonicalProduct(Guid.NewGuid(), "beta", "Beta Tool")], [new InstallationInstance(Guid.NewGuid(), productId, "registry", "alpha-key", "1.2", "Machine", "x64")]);
        viewModel.SetFilters("alpha", "All statuses", "registry", "Machine", "Name");
        Assert.HasCount(1, viewModel.VisibleRows);
        Assert.AreEqual("Alpha Tool", viewModel.VisibleRows[0].Name);
        Assert.HasCount(1, viewModel.VisibleRows[0].Instances);
    }

    [TestMethod]
    public void CompletionUsesLocalOnlyTerminologyAndRecordsFreshness()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.ReportProgress(new ScanProgress(Guid.NewGuid(), "Collecting", TimeSpan.FromSeconds(2), "registry"));
        viewModel.CompleteScan(new ScanResult(Guid.NewGuid(), true, false, new Dictionary<string, TimeSpan> { ["registry"] = TimeSpan.FromSeconds(3) }, []), DateTimeOffset.UnixEpoch);
        Assert.IsFalse(viewModel.IsScanning);
        StringAssert.Contains(viewModel.ScanPhase, "Local-only");
        StringAssert.Contains(viewModel.FreshnessLabel, "Local-only");
    }
}
