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
    public void ARowTheNewSummaryNoLongerAssessesLosesItsEarlierUpdateStatus()
    {
        InventoryExperienceViewModel viewModel = new(); Guid entry = Guid.NewGuid(); Guid record = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(entry, "registry:chat", "Chat"), new CanonicalProduct(record, "winget:winget:contoso.chat", "Chat")],
            [new InstallationInstance(Guid.NewGuid(), entry, "registry", "chat", "1.0", "User", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), record, "winget", "Contoso.Chat", "1.0", "Unknown", "Unknown", null, SourceId: "winget")]);
        Dictionary<UpdateIntelligenceStatus, int> counts = Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, _ => 0);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("registry:chat", UpdateIntelligenceStatus.ReviewRequired, null, "Review."), new UpdateIntelligenceItem("winget:winget:contoso.chat", UpdateIntelligenceStatus.ReviewRequired, null, "Review.")], counts, []));

        // WinGet's own record is no longer assessed once the entry's row carries its package.
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("registry:chat", UpdateIntelligenceStatus.Current, null, "Current.")], counts, []));

        Assert.AreEqual("Current", viewModel.AllRows.Single(row => row.StableProductKey == "registry:chat").Status);
        Assert.AreEqual("Installed", viewModel.AllRows.Single(row => row.StableProductKey == "winget:winget:contoso.chat").Status);
        Assert.IsEmpty(UpdatesPagePresenter.Build(viewModel.AllRows, viewModel.UpdateDetails, true, [], null, DateTimeOffset.UnixEpoch).NeedsReview);
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
    public void AdministratorRequirementUsesValidatedSelectionAndRemainsUnknownBeforeReview()
    {
        AdministratorRequirementPresentation unknown = AdministratorRequirementPresenter.BeforeReview();
        UpdateJobSelection machineSelection = new("product:machine", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Machine", "1.0", "1.1", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1");
        UpdateJobSelection userSelection = new("product:user", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.User", "1.0", "1.1", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1");
        AdministratorRequirementPresentation machine = AdministratorRequirementPresenter.ForSelection(machineSelection);
        AdministratorRequirementPresentation user = AdministratorRequirementPresenter.ForSelection(userSelection);

        Assert.AreEqual(AdministratorRequirementStatus.Required, machine.Status);
        StringAssert.Contains(machine.Label, "required");
        StringAssert.Contains(machine.Explanation, "UAC");
        Assert.AreEqual(AdministratorRequirementStatus.NotExpected, user.Status);
        StringAssert.Contains(user.Label, "not expected");
        Assert.AreEqual(AdministratorRequirementStatus.Unknown, unknown.Status);
        StringAssert.Contains(unknown.Explanation, "confirm");
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
        UpdateDetail liveDetail = CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [exact], [])).UpdateDetails["product:widget"];
        Assert.IsTrue(liveDetail.CanConfirmPackageIdentity);
        Assert.IsFalse(liveDetail.RequiresFreshSourceEvidence);
        StringAssert.Contains(liveDetail.ActionState, "confirm this exact package identity");
        StringAssert.Contains(liveDetail.ActionState, "None starts an update");
        UpdateDetail cachedDetail = CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Cached, [exact], ["source:stale"])).UpdateDetails["product:widget"];
        Assert.IsFalse(cachedDetail.CanConfirmPackageIdentity);
        Assert.IsTrue(cachedDetail.RequiresFreshSourceEvidence);
        StringAssert.Contains(cachedDetail.ActionState, "refresh source evidence");
        StringAssert.Contains(cachedDetail.ActionState, "successful live refresh is required");
        UpdateDetail unavailableDetail = CreateViewModel(new SourceSnapshot("winget", null, null, SourceSnapshotState.Unavailable, [], ["source:offline"])).UpdateDetails["product:widget"];
        Assert.IsFalse(unavailableDetail.CanConfirmPackageIdentity);
        Assert.IsTrue(unavailableDetail.RequiresFreshSourceEvidence);
        Assert.IsFalse(CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [exact, exact], [])).UpdateDetails["product:widget"].CanConfirmPackageIdentity);
        Assert.IsFalse(CreateViewModel(new SourceSnapshot("winget", "v1", DateTimeOffset.UtcNow, SourceSnapshotState.Live, [exact with { InstalledVersion = "0.9" }], [])).UpdateDetails["product:widget"].CanConfirmPackageIdentity);
    }

    [TestMethod]
    public void AmbiguousCandidatesExposeStructuredChoosePinAndIgnoreActions()
    {
        InventoryExperienceViewModel viewModel = new();
        Guid id = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "registry", "widget", "1.0", "User", "x64", "Contoso")]);
        SourceCandidateObservation first = new("winget", "Contoso.Widget", "1.1", "Contoso", "User", "x64", "Stable", SourceSnapshotState.Live, DateTimeOffset.UnixEpoch, MappingConfidence.Medium, [MappingReviewReason.AmbiguousPublisher]);
        SourceCandidateObservation second = new("winget", "Contoso.Widget.Legacy", "1.0", "Contoso", "User", "x64", "Stable", SourceSnapshotState.Live, DateTimeOffset.UnixEpoch, MappingConfidence.Medium, [MappingReviewReason.AmbiguousPublisher]);
        UpdateIntelligenceItem item = new("product:widget", UpdateIntelligenceStatus.ReviewRequired, null, "More than one package has the same strongest evidence.", SourceCandidates: [first, second]);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([item], new Dictionary<UpdateIntelligenceStatus, int> { [UpdateIntelligenceStatus.ReviewRequired] = 1 }, []));

        UpdateDetail detail = viewModel.UpdateDetails["product:widget"];
        Assert.HasCount(2, detail.ReviewCandidates);
        string[] expectedPackageIds = ["Contoso.Widget", "Contoso.Widget.Legacy"];
        CollectionAssert.AreEquivalent(expectedPackageIds, detail.ReviewCandidates.Select(candidate => candidate.PackageId).ToArray());
        Assert.IsTrue(detail.ReviewCandidates.All(candidate => candidate.SourceId == "winget"));
        StringAssert.Contains(detail.ActionState, "choose an exact package identity");
        StringAssert.Contains(detail.ActionState, "pin the installed version");
        StringAssert.Contains(detail.ActionState, "ignore the product");
        StringAssert.Contains(detail.ActionState, "None starts an update");
    }

    [TestMethod]
    public void IncomparableVersionRefreshesStaleEvidenceButRemainsNonExecutableWhenLive()
    {
        InventoryExperienceViewModel viewModel = new();
        Guid id = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(id, "product:widget", "Widget")], [new InstallationInstance(Guid.NewGuid(), id, "msstore", "Contoso.Widget", "2026-R1", "User", "x64", "Contoso")]);
        PackageMapping mapping = new("msstore", "Contoso.Widget", "2026-R2", MappingConfidence.High, MappingOrigin.Automatic, "Exact package identity; versions cannot be safely ordered.", false, [MappingReviewReason.IncomparableVersion], new VersionComparisonResult(VersionComparisonOutcome.Incomparable, VersionComparisonExplanation.VendorSuffixUnsupported));
        SourcePackageRecord package = new("msstore", "Contoso.Widget", "Widget", "Contoso", "2026-R1", "2026-R2", "User", "x64", null);
        SourceSnapshot source = new("msstore", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Cached, [package], ["source:stale"]);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:widget", UpdateIntelligenceStatus.ReviewRequired, mapping, mapping.EvidenceExplanation, "msstore", source.LastSuccessAt)], new Dictionary<UpdateIntelligenceStatus, int> { [UpdateIntelligenceStatus.ReviewRequired] = 1 }, [source]));

        UpdateDetail detail = viewModel.UpdateDetails["product:widget"];
        Assert.IsFalse(detail.CanConfirmPackageIdentity);
        Assert.IsTrue(detail.RequiresFreshSourceEvidence);
        StringAssert.Contains(detail.ActionState, "refresh source evidence");
        StringAssert.Contains(detail.ActionState, "pin the installed version");
        StringAssert.Contains(detail.ActionState, "will not guess the version order");
        StringAssert.Contains(detail.ActionState, "No update starts");

        SourceSnapshot liveSource = source with { State = SourceSnapshotState.Live, Warnings = [] };
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:widget", UpdateIntelligenceStatus.ReviewRequired, mapping, mapping.EvidenceExplanation, "msstore", liveSource.LastSuccessAt)], new Dictionary<UpdateIntelligenceStatus, int> { [UpdateIntelligenceStatus.ReviewRequired] = 1 }, [liveSource]));
        UpdateDetail liveDetail = viewModel.UpdateDetails["product:widget"];
        Assert.IsFalse(liveDetail.CanConfirmPackageIdentity);
        Assert.IsFalse(liveDetail.RequiresFreshSourceEvidence);
        Assert.IsFalse(liveDetail.ActionState.Contains("refresh source evidence", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(liveDetail.ActionState, "will not guess the version order");
    }

    [TestMethod]
    public void LivePackageIdentityWithoutUpdateAvailabilityIsUnavailableAndRetryableNotADecision()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.SetConnectorEnabled("WinGet package sources", true);
        Guid id = Guid.NewGuid();
        viewModel.LoadInventory([new CanonicalProduct(id, "product:store-app", "Store App")], [new InstallationInstance(Guid.NewGuid(), id, "msstore", "Contoso.StoreApp", "1.0", "User", "x64", "Contoso")]);
        PackageMapping mapping = new("msstore", "Contoso.StoreApp", null, MappingConfidence.High, MappingOrigin.ProviderSupplied, "The package ID exactly matches the installed provider package ID. Versions cannot be safely ordered.", false, [MappingReviewReason.IncomparableVersion], new VersionComparisonResult(VersionComparisonOutcome.Incomparable, VersionComparisonExplanation.MissingCandidateVersion));
        SourcePackageRecord package = new("msstore", "Contoso.StoreApp", "Store App", "Contoso", "1.0", null, "User", "x64", null);
        SourceSnapshot source = new("msstore", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [package], ["winget:update-check-unavailable"]);
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary([new UpdateIntelligenceItem("product:store-app", UpdateIntelligenceStatus.FailedUnavailable, mapping, "The repository identified the exact package, but it did not establish update availability.", "msstore", source.LastSuccessAt)], new Dictionary<UpdateIntelligenceStatus, int> { [UpdateIntelligenceStatus.FailedUnavailable] = 1 }, [source]));

        UpdateDetail detail = viewModel.UpdateDetails["product:store-app"];
        Assert.IsFalse(detail.CanConfirmPackageIdentity);
        Assert.IsTrue(detail.RequiresFreshSourceEvidence);
        StringAssert.Contains(detail.ActionState, "refresh source evidence");
        StringAssert.Contains(detail.ActionState, "not a decision for you");
        StringAssert.Contains(detail.Freshness, "Update availability was not established");
        Assert.HasCount(1, viewModel.RepositoryManagedRows);
        Assert.AreEqual(nameof(UpdateIntelligenceStatus.FailedUnavailable), viewModel.RepositoryManagedRows.Single().Status);
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
