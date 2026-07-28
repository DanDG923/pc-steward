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

    [TestMethod]
    public void ClearScanDataReturnsDashboardToNoScanState()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.LoadInventory([new CanonicalProduct(Guid.NewGuid(), "alpha", "Alpha Tool")], []);
        viewModel.CompleteScan(new ScanResult(Guid.NewGuid(), true, false, new Dictionary<string, TimeSpan>(), []), DateTimeOffset.UtcNow);
        viewModel.ClearScanData();
        Assert.HasCount(0, viewModel.VisibleRows);
        StringAssert.Contains(viewModel.FreshnessLabel, "No scan yet");
        StringAssert.Contains(viewModel.ScanPhase, "No scan data");
    }
    [TestMethod]
    public void RestoredInventoryIsLabelledAsCachedLocalOnly()
    {
        InventoryExperienceViewModel viewModel = new(); viewModel.LoadInventory([new CanonicalProduct(Guid.NewGuid(), "alpha", "Alpha")], [], DateTimeOffset.UnixEpoch);
        StringAssert.Contains(viewModel.ScanPhase, "Cached"); StringAssert.Contains(viewModel.FreshnessLabel, "Local-only");
    }
    [TestMethod]
    public void PublisherStatusSourceScopeAndSortFiltersRemainActiveTogether()
    {
        InventoryExperienceViewModel viewModel = new(); Guid alpha = Guid.NewGuid(); Guid beta = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(alpha, "alpha", "Alpha"), new CanonicalProduct(beta, "beta", "Beta")], [new InstallationInstance(Guid.NewGuid(), alpha, "registry", "alpha", "1", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), beta, "msix", "beta", "1", "User", "x64", "Fabrikam")]);
        viewModel.SetFilters("", "Installed", "Contoso", "registry", "Machine", "Publisher");
        Assert.HasCount(1, viewModel.VisibleRows); Assert.AreEqual("Alpha", viewModel.VisibleRows.Single().Name); Assert.AreEqual("Contoso", viewModel.PublisherFilter);
        viewModel.SetFilters("Alpha", viewModel.StatusFilter, viewModel.PublisherFilter, viewModel.SourceFilter, viewModel.ScopeFilter, viewModel.SortField);
        Assert.HasCount(1, viewModel.VisibleRows); Assert.AreEqual("registry", viewModel.SourceFilter);
    }
    [TestMethod]
    public void ApprovedWingetModeNeverUsesLocalOnlyLabels()
    {
        InventoryExperienceViewModel viewModel = new(); viewModel.SetConnectorEnabled("WinGet installed inventory", true);
        StringAssert.Contains(viewModel.ScanModeLabel, "approved WinGet"); Assert.IsFalse(viewModel.ScanPhase.Contains("local-only", StringComparison.OrdinalIgnoreCase)); Assert.IsFalse(viewModel.FreshnessLabel.Contains("local-only", StringComparison.OrdinalIgnoreCase));
    }
    [TestMethod]
    public void UpdateIntelligenceShowsCountsAttentionAndCachedFreshnessSeparatelyFromInventory()
    {
        InventoryExperienceViewModel viewModel = new(); UpdateIntelligenceSummary summary = new([new UpdateIntelligenceItem("one", UpdateIntelligenceStatus.ReviewRequired, null, "Review")], new Dictionary<UpdateIntelligenceStatus, int> { [UpdateIntelligenceStatus.ReviewRequired] = 1 }, [new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Cached, [], ["source:stale"])]);
        viewModel.LoadUpdateIntelligence(summary);
        Assert.AreEqual(1, viewModel.UpdateStatusCounts[UpdateIntelligenceStatus.ReviewRequired]); Assert.HasCount(1, viewModel.AttentionNeeded); StringAssert.Contains(viewModel.UpdateFreshnessLabel, "cached", StringComparison.OrdinalIgnoreCase); StringAssert.Contains(viewModel.UpdateFreshnessLabel, "stale");
    }

    [TestMethod]
    public void UpdateDetailsExposeSafetyEvidenceAndNeverOfferAnExecutableAction()
    {
        InventoryExperienceViewModel viewModel = new(); Guid id = Guid.NewGuid(); viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "registry", "widget", "1.0", "Machine", "x64", "Contoso", Channel: "Stable")]);
        PackageMapping mapping = new("winget", "Contoso.Widget", "1.1", MappingConfidence.High, MappingOrigin.Automatic, "Exact identity.", true, [], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
        SourceCandidateObservation observation = new("winget", "Contoso.Widget", "1.1", "Contoso", "Machine", "x64", "Stable", SourceSnapshotState.Live, DateTimeOffset.UnixEpoch, MappingConfidence.High, []);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:widget", UpdateIntelligenceStatus.VerifiedUpdate, mapping, "Exact identity.", "winget", DateTimeOffset.UnixEpoch, [observation])], Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, _ => 0), [new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [], [])]));
        UpdateDetail detail = viewModel.UpdateDetails["product:widget"]; StringAssert.Contains(detail.ActionState, "not available"); Assert.AreEqual("Contoso.Widget", detail.PackageId); Assert.AreEqual("High", detail.Confidence); Assert.HasCount(1, detail.SourceCandidates); StringAssert.Contains(detail.SourceCandidates.Single(), "Contoso.Widget");
    }

    [TestMethod]
    public void MixedSourceFreshnessIsShownPerCandidateInsteadOfAsTheAlphabeticallyFirstSource()
    {
        InventoryExperienceViewModel viewModel = new();
        Guid id = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "winget", "Contoso.Widget", "1.0", "Machine", "x64", "Contoso")]);
        PackageMapping mapping = new("winget", "Contoso.Widget", "1.1", MappingConfidence.High, MappingOrigin.Automatic, "Exact identity.", true, [], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
        SourceSnapshot winget = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Cached, [], ["source:stale"]);
        SourceSnapshot store = new("msstore", "v2", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [], []);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:widget", UpdateIntelligenceStatus.VerifiedUpdate, mapping, "Exact identity.", "winget", DateTimeOffset.UnixEpoch)], new Dictionary<UpdateIntelligenceStatus, int> { [UpdateIntelligenceStatus.VerifiedUpdate] = 1 }, [store, winget]));

        StringAssert.Contains(viewModel.UpdateDetails["product:widget"].Freshness, "winget: cached");
        Assert.IsFalse(viewModel.UpdateDetails["product:widget"].Freshness.Contains("live", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(viewModel.SourceFreshnessLabels.Any(label => label.StartsWith("msstore: live", StringComparison.Ordinal)));
        Assert.IsTrue(viewModel.SourceFreshnessLabels.Any(label => label.StartsWith("winget: cached", StringComparison.Ordinal) && label.Contains("stale", StringComparison.Ordinal)));
    }
}
