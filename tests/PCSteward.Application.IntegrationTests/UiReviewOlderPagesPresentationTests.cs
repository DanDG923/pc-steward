using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// UI review 2026-11-13, item 14 (FR-UI-01/02/05, FR-POL-03/05): the Scan results, Review decisions and Repository apps pages are
/// gone. Updates is the one place for anything to do with an update, and Installed apps gains a "Has an update source" tab.
/// Presentation only: a tab never changes data, and confirming matches together still goes through its own dialog.
/// </summary>
[TestClass]
public sealed class UiReviewOlderPagesPresentationTests
{
    private static readonly string[] RetiredPages = ["Results", "Review", "RepositoryApps"];
    private static readonly string[] TabLabels = ["All", "Update ready", "Check the match", "Has an update source", "No update source", "Pinned, ignored & skipped"];
    private static readonly string[] TabLabelsWithoutSources = ["All", "Update ready", "Check the match", "No update source", "Pinned, ignored & skipped"];
    private static readonly string[] InternalWords = ["inventory", "package source", "trusted source", "source evidence", "package identity", "connector", "mapping", "repository"];

    [TestMethod]
    [Description("FR-UI-02 (item 14): the three older pages are no longer pages, and every page that is left has a title and an owner in the navigation.")]
    public void TheThreeOlderPagesAreGone()
    {
        string[] pages = Enum.GetNames<ShellPage>();
        foreach (string retired in RetiredPages) CollectionAssert.DoesNotContain(pages, retired);
        foreach (ShellPage page in Enum.GetValues<ShellPage>())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ShellNavigation.TitleOf(page)), page.ToString());
            Assert.IsTrue(Enum.IsDefined(ShellNavigation.ParentOf(page)), page.ToString());
        }
        Assert.AreEqual(ShellPage.Home, ShellNavigation.ParentOf(ShellPage.Scan), "the scan's progress page stays, under Home");
        Assert.AreEqual(ShellPage.Updates, ShellNavigation.ParentOf(ShellPage.UpdateJob));
        Assert.AreEqual(ShellPage.InstalledApps, ShellNavigation.ParentOf(ShellPage.Detail));
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 14): Has an update source is a tab on Installed apps, beside No update source, and lists the apps matched to an update source.")]
    public void HasAnUpdateSourceIsATabOnInstalledApps()
    {
        InventoryRow[] rows = [Row("a", "Alpha", "VerifiedUpdate"), Row("b", "Beta", "Current"), Row("c", "Gamma", "Unmanaged"), Row("d", "Delta", "Pinned")];
        HashSet<string> matched = new(StringComparer.Ordinal) { "a", "b" };
        IReadOnlyList<InstalledAppsFilterOption> tabs = InstalledAppsFilters.Options(rows, matched);
        CollectionAssert.AreEqual(TabLabels, tabs.Select(tab => tab.Label).ToArray());
        InstalledAppsFilterOption has = tabs[3];
        Assert.AreEqual(InstalledAppsFilter.HasUpdateSource, has.Filter);
        Assert.AreEqual(2, has.Count);
        Assert.AreEqual("Has an update source (2): apps PC Steward can check for updates", has.AccessibleName);
        Assert.IsTrue(InstalledAppsFilters.Matches(InstalledAppsFilter.HasUpdateSource, rows[0], matched));
        Assert.IsFalse(InstalledAppsFilters.Matches(InstalledAppsFilter.HasUpdateSource, rows[2], matched));
        Assert.IsFalse(InstalledAppsFilters.Matches(InstalledAppsFilter.HasUpdateSource, rows[0], null), "it lists nothing when no matches are known");
        Assert.IsFalse(InstalledAppsFilters.Matches(InstalledAppsFilter.HasUpdateSource, "Current"), "it is not a status");
        Assert.IsTrue(InstalledAppsFilters.Matches(InstalledAppsFilter.All, rows[2], matched));
        Assert.IsTrue(InstalledAppsFilters.Matches(InstalledAppsFilter.Unmanaged, rows[2], matched));
        CollectionAssert.AreEqual(TabLabelsWithoutSources, InstalledAppsFilters.Options(rows).Select(tab => tab.Label).ToArray());
    }

    [TestMethod]
    [Description("FR-UI-01/05 (item 14): choosing the tab lists exactly the matched apps, and it lists none while update checks are off.")]
    public void TheTabListsTheMatchedAppsAndFollowsTheUpdateChecksSwitch()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.SetConnectorEnabled("WinGet package sources", true);
        Guid currentId = Guid.NewGuid(); Guid updateId = Guid.NewGuid(); Guid installedOnlyId = Guid.NewGuid();
        viewModel.LoadInventory(
            [new CanonicalProduct(currentId, "product:current", "Current Tool"), new CanonicalProduct(updateId, "product:update", "Update Tool"), new CanonicalProduct(installedOnlyId, "product:installed", "Installed Only")],
            [new InstallationInstance(Guid.NewGuid(), currentId, "registry", "current-tool", "2.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), updateId, "registry", "update-tool", "1.0", "Machine", "x64", "Contoso"), new InstallationInstance(Guid.NewGuid(), installedOnlyId, "registry", "installed-only", "1.0", "Machine", "x64", "Contoso")]);
        PackageMapping currentMapping = new("winget", "Contoso.CurrentTool", "2.0", MappingConfidence.High, MappingOrigin.Automatic, "Exact version match.", false, [], new VersionComparisonResult(VersionComparisonOutcome.Equal, VersionComparisonExplanation.NumericDotted));
        PackageMapping updateMapping = new("winget", "Contoso.UpdateTool", "1.1", MappingConfidence.High, MappingOrigin.Automatic, "Verified update.", true, [], new VersionComparisonResult(VersionComparisonOutcome.Newer, VersionComparisonExplanation.NumericDotted));
        viewModel.LoadUpdateIntelligence(new UpdateIntelligenceSummary(
            [new UpdateIntelligenceItem("product:current", UpdateIntelligenceStatus.Current, currentMapping, "Exact version match.", "winget", DateTimeOffset.UnixEpoch), new UpdateIntelligenceItem("product:update", UpdateIntelligenceStatus.VerifiedUpdate, updateMapping, "Verified update.", "winget", DateTimeOffset.UnixEpoch)],
            Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, status => status is UpdateIntelligenceStatus.Current or UpdateIntelligenceStatus.VerifiedUpdate ? 1 : 0),
            [new SourceSnapshot("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [], [])]));

        Assert.HasCount(3, viewModel.VisibleRows);
        viewModel.SetSavedFilter(InstalledAppsFilter.HasUpdateSource);
        CollectionAssert.AreEquivalent(new List<string> { "Current Tool", "Update Tool" }, viewModel.VisibleRows.Select(row => row.Name).ToList());
        Assert.AreEqual(2, InstalledAppsFilters.Options(viewModel.ListedRows, viewModel.UpdateSourceKeys).Single(tab => tab.Filter == InstalledAppsFilter.HasUpdateSource).Count);

        viewModel.SetConnectorEnabled("WinGet package sources", false);
        Assert.HasCount(0, viewModel.VisibleRows, "nothing is listed as having an update source while update checks are off");
        Assert.AreEqual(0, InstalledAppsFilters.Options(viewModel.ListedRows, viewModel.UpdateSourceKeys).Single(tab => tab.Filter == InstalledAppsFilter.HasUpdateSource).Count);
        viewModel.SetSavedFilter(InstalledAppsFilter.All);
        Assert.HasCount(3, viewModel.VisibleRows);
    }

    [TestMethod]
    [Description("FR-POL-03/05, FR-UI-01 (item 14): matches that only need a yes are confirmed together from Installed apps, in plain words.")]
    public void MatchesThatOnlyNeedAYesAreConfirmedTogether()
    {
        Assert.AreEqual("1 of these apps is already on the version its match lists. Confirm the match and PC Steward will offer its updates when they appear.", InstalledAppsFilters.ConfirmCurrentMatchesText(1));
        Assert.AreEqual("3 of these apps are already on the version their match lists. Confirm the matches and PC Steward will offer their updates when they appear.", InstalledAppsFilters.ConfirmCurrentMatchesText(3));
        Assert.AreEqual("Confirm these matches…", InstalledAppsFilters.ConfirmCurrentMatchesLabel);
        Assert.AreEqual("Confirm match", InstalledAppsFilters.ConfirmCurrentMatchesButton(1));
        Assert.AreEqual("Confirm 3 matches", InstalledAppsFilters.ConfirmCurrentMatchesButton(3));
        // The dialog names the apps, so what is confirmed is what was shown (review finding on part 3c).
        Assert.AreEqual("This confirms the match for Alpha.", InstalledAppsFilters.ConfirmCurrentMatchesNames(["Alpha"]));
        Assert.AreEqual("This confirms the matches for Alpha, Beta and Gamma.", InstalledAppsFilters.ConfirmCurrentMatchesNames(["Alpha", "Beta", "Gamma"]));
        string many = InstalledAppsFilters.ConfirmCurrentMatchesNames([.. Enumerable.Range(1, 15).Select(index => $"App {index}")]);
        StringAssert.EndsWith(many, "App 12 and 3 more.");
        Assert.AreEqual("Update checks are off", InstalledAppsFilters.ChecksOffTitle);
        Assert.AreEqual("PC Steward can't tell which apps have an update source until update checks are on. You can turn them on in Settings.", InstalledAppsFilters.ChecksOffMessage);
        StringAssert.Contains(InstalledAppsFilters.ConfirmCurrentMatchesExplanation, "You can undo each one on the Decisions page.");
        StringAssert.Contains(InstalledAppsFilters.ConfirmCurrentMatchesExplanation, "It does not approve or start an update.");
        foreach (string text in new[] { InstalledAppsFilters.ConfirmCurrentMatchesText(3), InstalledAppsFilters.ConfirmCurrentMatchesLabel, InstalledAppsFilters.ConfirmCurrentMatchesButton(3), InstalledAppsFilters.ConfirmCurrentMatchesTitle, InstalledAppsFilters.ConfirmCurrentMatchesExplanation, InstalledAppsFilters.HasUpdateSourceLabel })
            foreach (string word in InternalWords)
                Assert.IsFalse(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"'{word}' in: {text}");
    }

    private static InventoryRow Row(string key, string name, string status) =>
        new(Guid.NewGuid(), name, "Contoso", "winget", "User", "1.0", status, "Application", [], key);
}
