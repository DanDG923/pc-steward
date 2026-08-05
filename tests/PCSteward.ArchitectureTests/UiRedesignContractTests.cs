using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class UiRedesignContractTests
{
    private static readonly string[] SupportedThemes = ["Default", "Light", "Dark", "HighContrast"];

    [TestMethod]
    public void ProductionShellHasFiveStableDestinationsAndKeepsContextualRoutes()
    {
        string source = DesktopSource("MainWindow.cs");
        string shell = Between(source, "private NavigationView BuildShell", "private static void AddNavigationItem");

        foreach (string destination in new[] { "Overview", "Applications", "Updates", "Activity", "Settings" })
            StringAssert.Contains(shell, $"AddNavigationItem(navigation, \"{destination}\"");
        StringAssert.Contains(shell, "AddNavigationItem(navigation, \"About & support\"");

        foreach (string contextual in new[] { "scan", "results", "review", "current", "storage", "privacy", "detail" })
            StringAssert.Contains(source, $"destination == \"{contextual}\"");

        Assert.AreEqual(6, Count(shell, "AddNavigationItem(navigation,"));
        StringAssert.Contains(shell, "IsSettingsVisible = false");
        StringAssert.Contains(shell, "PaneDisplayMode = NavigationViewPaneDisplayMode.Auto");
        StringAssert.Contains(shell, "CompactModeThresholdWidth = 900");
    }

    [TestMethod]
    public void ApplicationInventoryUsesAReusableVirtualizedStructuredRow()
    {
        string root = FindRepositoryRoot();
        string source = DesktopSource("MainWindow.cs");
        string presentation = DesktopSource("StewardPresentation.cs");
        string templatePath = Path.Combine(root, "src", "PCSteward.Desktop", "Resources", "DataTemplates.xaml");
        XDocument template = XDocument.Load(templatePath);
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        Assert.IsNotNull(template.Descendants().SingleOrDefault(element =>
            element.Name.LocalName == "DataTemplate" &&
            string.Equals(element.Attribute(xaml + "Key")?.Value, "StewardApplicationRowTemplate", StringComparison.Ordinal)));
        StringAssert.Contains(source, "list.ItemsSource = viewModel.VisibleRows.Select(ApplicationListItem.From).ToArray()");
        StringAssert.Contains(source, "ItemTemplate = (DataTemplate)");
        Assert.IsFalse(source.Contains("DisplayMemberPath = nameof(InventoryRow.DisplayLabel)", StringComparison.Ordinal));
        StringAssert.Contains(presentation, "AccessibleSummary");
        StringAssert.Contains(presentation, "Needs your decision");
        StringAssert.Contains(presentation, "Inventoried only");
    }

    [TestMethod]
    public void LongRunningScanUpdatesTheGlobalBandWithoutRebuildingTheShellPerProgressEvent()
    {
        string source = DesktopSource("MainWindow.cs");
        string scan = Between(source, "private async Task StartScanAsync()", "private async Task CheckUpdateSourcesAsync()");

        StringAssert.Contains(scan, "viewModel.ReportProgress(value); RefreshOperationVisuals();");
        Assert.IsFalse(scan.Contains("viewModel.ReportProgress(value); Render();", StringComparison.Ordinal));
        StringAssert.Contains(source, "IsIndeterminate = true");
        StringAssert.Contains(source, "cancel.Click += (_, _) => scanCancellation?.Cancel()");
        StringAssert.Contains(source, "Previous completed data is retained where available");
    }

    [TestMethod]
    public void ThemeAndAccessibilityResourcesCoverRowsStatusAndFocus()
    {
        string root = FindRepositoryRoot();
        string app = DesktopSource("App.xaml");
        string controls = DesktopSource(Path.Combine("Resources", "ControlStyles.xaml"));
        string templates = DesktopSource(Path.Combine("Resources", "DataTemplates.xaml"));
        XDocument colors = XDocument.Load(Path.Combine(root, "src", "PCSteward.Desktop", "Resources", "ColorTokens.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        StringAssert.Contains(app, "Resources/DataTemplates.xaml");
        StringAssert.Contains(controls, "StewardListItemStyle");
        StringAssert.Contains(controls, "FocusVisualPrimaryBrush");
        StringAssert.Contains(templates, "AutomationProperties.Name=\"{Binding AccessibleSummary}\"");
        StringAssert.Contains(templates, "StatusGlyph");
        string[] themes = colors.Descendants(presentation + "ResourceDictionary").Attributes(xaml + "Key").Select(attribute => attribute.Value).ToArray();
        CollectionAssert.AreEquivalent(SupportedThemes, themes);
    }

    [TestMethod]
    public void RedesignEvidenceDocumentsArePresentAndSelectedDirectionIsExplicit()
    {
        string root = FindRepositoryRoot();
        foreach (string file in new[]
        {
            "UI_CURRENT_STATE_AUDIT.md", "COMPETITOR_UI_RESEARCH.md", "UI_INSPIRATION_REFERENCE_MAP.md",
            "UI_DESIGN_DIRECTIONS.md", "SELECTED_UI_DIRECTION.md", "PROTECTED_UI_BEHAVIOURS.md",
            "UI_DESIGN_SYSTEM.md", "UI_REDESIGN_CHANGELOG.md", "UI_VALIDATION_REPORT.md"
        })
            Assert.IsTrue(File.Exists(Path.Combine(root, "docs", file)), $"Missing docs/{file}");

        string selected = File.ReadAllText(Path.Combine(root, "docs", "SELECTED_UI_DIRECTION.md"));
        StringAssert.Contains(selected, "Steward Ledger");
        StringAssert.Contains(selected, "five stable principal destinations");
        StringAssert.Contains(selected, "Preserve the separate source-consent, review, final approval, UAC, execution, and verification stages");
    }

    [TestMethod]
    public void EveryReviewRequiredEntryPointUsesSafeDecisionActionsWithoutUpdateAuthority()
    {
        string source = DesktopSource("MainWindow.cs");
        string results = Between(source, "private void RenderResultsPage()", "private void RenderReviewPage()");
        string review = Between(source, "private void RenderReviewPage()", "private void AddReviewDecisionActions");
        string sharedActions = Between(source, "private void AddReviewDecisionActions", "private async Task PromptAndConfirmPackageIdentityAsync");
        string detail = Between(source, "private void RenderProductDetailPage()", "private async Task ShowDetailsAsync");

        StringAssert.Contains(results, "AddReviewDecisionActions(finding, row, detail)");
        StringAssert.Contains(review, "AddReviewDecisionActions(decision, row, detail)");
        foreach (string action in new[] { "Refresh source evidence", "Confirm package identity", "Choose package identity", "Pin current version", "Ignore…", "Review details" })
            StringAssert.Contains(sharedActions, action);
        StringAssert.Contains(sharedActions, "RequiresFreshSourceEvidence: true");
        StringAssert.Contains(sharedActions, "CreateSourceEvidenceRefreshButton(row)");
        Assert.IsFalse((results + review + sharedActions).Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
        StringAssert.Contains(detail, "RequiresFreshSourceEvidence: true");
        StringAssert.Contains(detail, "CreateSourceEvidenceRefreshButton(row)");
        StringAssert.Contains(detail, "Approved package candidates");
        StringAssert.Contains(detail, "Confirm selected identity");
        StringAssert.Contains(detail, "CreateManualMappingAsync");
        StringAssert.Contains(results, "ResultsFilter.Unavailable");
        StringAssert.Contains(results, "FailedUnavailable");
        StringAssert.Contains(results, "there is no decision or update to approve");
        StringAssert.Contains(detail, "sourceEvidenceUnavailable ? \"Available next step\" : \"Decisions\"");
        StringAssert.Contains(detail, "if (!sourceEvidenceUnavailable)");
        StringAssert.Contains(DesktopSource("StewardPresentation.cs"), "Update evidence unavailable");

        string refresh = Between(source, "private async Task CheckUpdateSourcesAsync()", "private void AddDataAndResetSection");
        StringAssert.Contains(refresh, "workflow.RefreshUpdateIntelligenceAsync");
        StringAssert.Contains(refresh, "detailStableProductKey");
        StringAssert.Contains(refresh, "detailRow = viewModel.AllRows.SingleOrDefault");
        Assert.IsFalse(refresh.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
    }

    private static string DesktopSource(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", relativePath));

    private static string Between(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, $"Could not find section from {startMarker} to {endMarker}.");
        return source[start..end];
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0) { count++; index += value.Length; }
        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("PCSteward.slnx was not found above the test output directory.");
    }
}
