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
    public void FailedOrPartialScanKeepsPriorFreshnessAndExposesProviderWarnings()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.LoadInventory([new CanonicalProduct(Guid.NewGuid(), "prior", "Prior snapshot")], [], DateTimeOffset.UnixEpoch);
        viewModel.CompleteScan(new ScanResult(Guid.NewGuid(), false, false, new Dictionary<string, TimeSpan>(), ["registry:timeout"]), DateTimeOffset.UtcNow);
        StringAssert.Contains(viewModel.FreshnessLabel, "1970");
        CollectionAssert.Contains(viewModel.LastScanWarnings.ToArray(), "registry:timeout");
    }
    [TestMethod]
    public void FreshnessUsesAReadableLocalCompletionTime()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.LoadInventory([], [], DateTimeOffset.UnixEpoch);

        StringAssert.Contains(viewModel.LastScanCompletedLabel, "1970");
        Assert.IsFalse(viewModel.FreshnessLabel.Contains("T00:00:00", StringComparison.Ordinal));
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
    public void AllRowsRemainAvailableForCrossPageSummariesWhenAppsFiltersAreActive()
    {
        InventoryExperienceViewModel viewModel = new(); Guid alpha = Guid.NewGuid(); Guid beta = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(alpha, "alpha", "Alpha"), new CanonicalProduct(beta, "beta", "Beta")], [new InstallationInstance(Guid.NewGuid(), alpha, "registry", "alpha", "1", "Machine", "x64"), new InstallationInstance(Guid.NewGuid(), beta, "msix", "beta", "1", "User", "x64")]);
        viewModel.SetFilters("Alpha", "Installed", "All publishers", "registry", "Machine", "Name");
        Assert.HasCount(1, viewModel.VisibleRows);
        Assert.HasCount(2, viewModel.AllRows);
    }

    [TestMethod]
    public void SearchMatchesApplicationPublishersAndWingetStateRequiresBothTrustedConnectors()
    {
        InventoryExperienceViewModel viewModel = new(); Guid product = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(product, "alpha", "Alpha")], [new InstallationInstance(Guid.NewGuid(), product, "registry", "alpha", "1", "Machine", "x64", "Contoso")]);
        viewModel.SetFilters("contoso", "Installed", "All publishers", "All sources", "All scopes", "Name");
        Assert.HasCount(1, viewModel.VisibleRows);
        viewModel.SetConnectorEnabled("WinGet installed inventory", true);
        Assert.IsFalse(viewModel.IsWinGetTrustedSourcesEnabled);
        viewModel.SetConnectorEnabled("WinGet package sources", true);
        Assert.IsTrue(viewModel.IsWinGetTrustedSourcesEnabled);
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
    public void RepositoryManagedRowsExposeCurrentAndUpdateStatusesOnlyWhenAConnectedSourceHasPackageIdentity()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.SetConnectorEnabled("WinGet package sources", true);
        Guid currentId = Guid.NewGuid(); Guid updateId = Guid.NewGuid(); Guid reviewId = Guid.NewGuid(); Guid installedOnlyId = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(currentId, "product:current", "Current Tool"), new CanonicalProduct(updateId, "product:update", "Update Tool"), new CanonicalProduct(reviewId, "product:review", "Review Tool"), new CanonicalProduct(installedOnlyId, "product:installed", "Installed Only")],
            [new InstallationInstance(Guid.NewGuid(), currentId, "registry", "current-tool", "2.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), updateId, "registry", "update-tool", "1.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), reviewId, "registry", "review-tool", "1.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), installedOnlyId, "registry", "installed-only", "1.0", "Machine", "x64", "Contoso")]);
        PackageMapping currentMapping = new("winget", "Contoso.CurrentTool", "2.0", MappingConfidence.High, MappingOrigin.Automatic, "Exact version match.", false, [], new VersionComparisonResult(VersionComparisonOutcome.Equal, VersionComparisonExplanation.NumericDotted));
        PackageMapping updateMapping = new("msstore", "Contoso.UpdateTool", "1.1", MappingConfidence.High, MappingOrigin.Automatic, "Verified update.", true, [], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
        PackageMapping reviewMapping = new("winget", "Contoso.ReviewTool", "1.1", MappingConfidence.Low, MappingOrigin.Automatic, "Review required.", false, [], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary(
            [new UpdateIntelligenceItem("product:current", UpdateIntelligenceStatus.Current, currentMapping, "Exact version match.", "winget", DateTimeOffset.UnixEpoch), new UpdateIntelligenceItem("product:update", UpdateIntelligenceStatus.VerifiedUpdate, updateMapping, "Verified update.", "msstore", DateTimeOffset.UnixEpoch), new UpdateIntelligenceItem("product:review", UpdateIntelligenceStatus.ReviewRequired, reviewMapping, "Review required.", "winget", DateTimeOffset.UnixEpoch)],
            Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, status => status is UpdateIntelligenceStatus.Current or UpdateIntelligenceStatus.VerifiedUpdate or UpdateIntelligenceStatus.ReviewRequired ? 1 : 0),
            [new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Cached, [], ["source:stale"]), new SourceSnapshot("msstore", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [], [])]));

        Assert.HasCount(3, viewModel.RepositoryManagedRows);
        Assert.AreEqual(1, viewModel.RepositoryManagedRows.Count(row => row.Status == "Current"));
        Assert.AreEqual(1, viewModel.RepositoryManagedRows.Count(row => row.Status == "VerifiedUpdate"));
        Assert.AreEqual(1, viewModel.RepositoryManagedRows.Count(row => row.Status == "ReviewRequired"));
        Assert.AreEqual("winget", viewModel.UpdateDetails["product:current"].SourceId);
        StringAssert.Contains(viewModel.UpdateDetails["product:current"].Freshness, "cached", StringComparison.OrdinalIgnoreCase);
        viewModel.SetConnectorEnabled("WinGet package sources", false);
        Assert.HasCount(0, viewModel.RepositoryManagedRows);
    }

    [TestMethod]
    public void UpdateDetailsExposeSafetyEvidenceAndOnlyOfferReviewedExecution()
    {
        InventoryExperienceViewModel viewModel = new(); Guid id = Guid.NewGuid(); viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "registry", "widget", "1.0", "Machine", "x64", "Contoso", Channel: "Stable")]);
        PackageMapping mapping = new("winget", "Contoso.Widget", "1.1", MappingConfidence.High, MappingOrigin.Automatic, "Exact identity.", true, [], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
        SourceCandidateObservation observation = new("winget", "Contoso.Widget", "1.1", "Contoso", "Machine", "x64", "Stable", SourceSnapshotState.Live, DateTimeOffset.UnixEpoch, MappingConfidence.High, []);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:widget", UpdateIntelligenceStatus.VerifiedUpdate, mapping, "Exact identity.", "winget", DateTimeOffset.UnixEpoch, [observation])], Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, _ => 0), [new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [], [])]));
        UpdateDetail detail = viewModel.UpdateDetails["product:widget"]; StringAssert.Contains(detail.ActionState, "reviewed"); Assert.AreEqual("Contoso.Widget", detail.PackageId); Assert.AreEqual("High", detail.Confidence); Assert.HasCount(1, detail.SourceCandidates); StringAssert.Contains(detail.SourceCandidates.Single(), "Contoso.Widget");
    }

    [TestMethod]
    public void NewerPackageIdentityCanBeConfirmedOnlyFromOneLiveExactSourceRecordBoundToInstalledVersion()
    {
        static InventoryExperienceViewModel CreateViewModel(SourceSnapshot source)
        {
            InventoryExperienceViewModel viewModel = new();
            Guid id = Guid.NewGuid();
            viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "registry", "widget", "1.0", "User", "x64", "Contoso")]);
            PackageMapping mapping = new("winget", "Contoso.Widget", "1.1", MappingConfidence.Medium, MappingOrigin.Automatic, "Review exact identity.", false, [MappingReviewReason.AmbiguousPublisher], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
            viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:widget", UpdateIntelligenceStatus.ReviewRequired, mapping, "Review exact identity.", "winget", source.LastSuccessAt)], Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, _ => 0), [source]));
            return viewModel;
        }

        SourcePackageRecord exact = new("winget", "Contoso.Widget", "Widget", "Contoso", "1.0", "1.1", "Unknown", "Unknown", null);
        Assert.IsTrue(CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [exact], [])).UpdateDetails["product:widget"].CanConfirmPackageIdentity);
        Assert.IsFalse(CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Cached, [exact], ["source:stale"])).UpdateDetails["product:widget"].CanConfirmPackageIdentity);
        Assert.IsFalse(CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [exact, exact], [])).UpdateDetails["product:widget"].CanConfirmPackageIdentity);
        Assert.IsFalse(CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [exact with { InstalledVersion = "0.9" }], [])).UpdateDetails["product:widget"].CanConfirmPackageIdentity);
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
