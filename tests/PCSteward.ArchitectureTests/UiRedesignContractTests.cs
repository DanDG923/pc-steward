using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class UiRedesignContractTests
{
    private static readonly string[] SupportedThemes = ["Default", "Light", "Dark", "HighContrast"];
    private static readonly string[] MainNavigationLabels = ["Home", "Updates", "Installed apps", "Activity", "Decisions"];
    private static readonly string[] FooterNavigationLabels = ["Settings", "About"];
    private static readonly string[] EveryShellPage = ["Home", "Updates", "InstalledApps", "Activity", "Decisions", "Settings", "About", "Scan", "Results", "Review", "RepositoryApps", "Detail", "Storage", "Privacy", "Onboarding"];
    private static readonly string[] ContextualEntryPoints = ["Scan", "Results", "Review", "RepositoryApps", "Detail", "Storage", "Privacy", "Decisions", "InstalledApps", "Settings"];

    /// <summary>
    /// Task 73 Phase 1 (ADR-030, FR-UI-01/02, AC-UX-01/02). Deliberately replaces the Task 35 pin of six literal
    /// <c>AddNavigationItem(navigation, "…")</c> calls and string <c>destination == "…"</c> routes: the seven ADR-030 items now come
    /// from the portable <c>ShellNavigation.Items</c> (labels pinned here and in DesktopFoundationsPresentationTests), the
    /// NavigationView is created once, navigation reacts to <c>ItemInvoked</c>, every page has a builder and an entry point,
    /// and Back is a real back stack.
    /// </summary>
    [TestMethod]
    public void ProductionShellHasSevenAdr030DestinationsAPersistentShellAndReachableContextualPages()
    {
        string source = DesktopSource("MainWindow.cs");
        string navigationModel = ApplicationSource("ShellNavigation.cs");
        string items = Between(navigationModel, "public static IReadOnlyList<ShellNavigationItem> Items", "public ShellPage Current");
        foreach (string label in MainNavigationLabels) StringAssert.Contains(items, $"\"{label}\", \"");
        foreach (string label in FooterNavigationLabels) StringAssert.Contains(items, $"\"{label}\", \"");
        Assert.AreEqual(7, Count(items, "new(ShellPage."));
        Assert.AreEqual(2, Count(items, "\", true)"), "Settings and About are the only footer items");

        string shell = Between(source, "private void EnsureShell()", "private static void AddNavigationItem");
        StringAssert.Contains(shell, "if (navigationView is not null || windowRoot is null) return;");
        StringAssert.Contains(shell, "foreach (ShellNavigationItem item in ShellNavigation.Items) AddNavigationItem(navigationView, item);");
        Assert.AreEqual(1, Count(source, "new NavigationView"), "the NavigationView is created once");
        Assert.AreEqual(1, Count(source, "AddNavigationItem(navigationView,"));
        StringAssert.Contains(shell, "navigationView.ItemInvoked +=");
        Assert.IsFalse(source.Contains("SelectionChanged += (_, args)", StringComparison.Ordinal), "navigation reacts to ItemInvoked, not SelectionChanged");
        StringAssert.Contains(shell, "IsSettingsVisible = false");
        StringAssert.Contains(shell, "PaneDisplayMode = NavigationViewPaneDisplayMode.Auto");
        StringAssert.Contains(shell, "CompactModeThresholdWidth = 900");

        string pages = Between(source, "private StackPanel BuildPage(ShellPage page)", "private void RenderStartupStateLoadFailure()");
        foreach (string page in EveryShellPage) StringAssert.Contains(pages, $"ShellPage.{page} => Build");
        foreach (string page in ContextualEntryPoints) StringAssert.Contains(source, $"NavigateTo(ShellPage.{page})");

        string render = Between(source, "private void Render()", "private void NavigateTo(ShellPage page)");
        StringAssert.Contains(render, "ShowCurrentPage(preserveViewState: true);");
        Assert.IsFalse(render.Contains("new NavigationView", StringComparison.Ordinal) || render.Contains("Content =", StringComparison.Ordinal), "Render refreshes only the hosted page");
        Assert.IsFalse(source.Contains("Content = BuildShell(", StringComparison.Ordinal));
        Assert.IsFalse(source.Contains("destination", StringComparison.Ordinal), "no string-destination router remains");

        string chrome = Between(source, "private void BuildWindowChrome()", "private void ApplyBackdrop()");
        StringAssert.Contains(chrome, "ExtendsContentIntoTitleBar = true;");
        StringAssert.Contains(chrome, "SetTitleBar(dragRegion);");
        StringAssert.Contains(chrome, "titleBarBack.Click += (_, _) => GoBack();");
        StringAssert.Contains(chrome, "Windows.System.VirtualKey.Left, Modifiers = Windows.System.VirtualKeyModifiers.Menu");
        StringAssert.Contains(source, "titleBarBack.IsEnabled = navigationView is not null && navigation.CanGoBack;");
        StringAssert.Contains(Between(source, "private void ApplyBackdrop()", "private void ShowBarePage"), "new MicaBackdrop()");
        StringAssert.Contains(Between(source, "private void ApplyBackdrop()", "private void ShowBarePage"), "highContrast ? null");
    }

    [TestMethod]
    [Description("FR-UI-02; AC-UX-02 (audit P-H7): a refresh rebuilds only the hosted page and restores its scroll offset and focused control.")]
    public void PageRefreshPreservesScrollAndFocus()
    {
        string source = DesktopSource("MainWindow.cs");
        string show = Between(source, "private void ShowCurrentPage(bool preserveViewState)", "private bool IsFocusInside");
        StringAssert.Contains(show, "pageHost.VerticalOffset");
        StringAssert.Contains(show, "pageHost.ChangeView(null, offset, null, true)");
        StringAssert.Contains(show, "FindFocusTarget(page, focusKey)");
        StringAssert.Contains(show, "Focus(FocusState.Programmatic)");
        StringAssert.Contains(show, "pageHost.Content = page;");
        Assert.IsFalse(show.Contains("operationStatusText =", StringComparison.Ordinal), "the operation band is not rebuilt by a page refresh");
    }

    [TestMethod]
    [Description("FR-UI-01/05; AC-UX-03 (audit P-M2): filters and chips use the one status vocabulary, never raw enum or provider values.")]
    public void FiltersAndChipsUseTheSingleStatusVocabulary()
    {
        string source = DesktopSource("MainWindow.cs");
        string presentation = DesktopSource("StewardPresentation.cs");
        string apps = Between(source, "private StackPanel BuildInstalledAppsPage()", "private static (string Raw, string Label)[] LabelledFilterOptions");
        Assert.IsFalse(source.Contains("Enum.GetNames<PCSteward.Domain.UpdateIntelligenceStatus>()", StringComparison.Ordinal), "status filter must not list raw enum names");
        StringAssert.Contains(apps, "StatusVocabulary.FilterLabels");
        StringAssert.Contains(apps, "StatusVocabulary.FilterKeyForLabel(status.SelectedItem?.ToString())");
        StringAssert.Contains(apps, "LabelledFilterOptions(viewModel.AllRows.Select(row => row.Provider), StewardPresentation.Provider)");
        StringAssert.Contains(apps, "LabelledFilterOptions(viewModel.AllRows.Select(row => row.Scope), StewardPresentation.Scope)");
        string results = Between(source, "private StackPanel BuildResultsPage()", "private StackPanel BuildReviewPage()");
        StringAssert.Contains(results, "StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate).Label");
        Assert.IsFalse(results.Contains("$\"Show {option} scan results\"", StringComparison.Ordinal), "automation names use the visible label, not the enum");
        StringAssert.Contains(presentation, "StatusChipPresentation chip = StatusVocabulary.For(status);");
        StringAssert.Contains(presentation, "public static (Brush Foreground, Brush Background) ToneBrushes(string tone)");
    }

    [TestMethod]
    [Description("FR-UPD-04; FR-UI-03 (audit P-H5): About, source consent and the helper-missing outcome tell the one elevation story.")]
    public void DesktopCopyTellsOneElevationStory()
    {
        string source = DesktopSource("MainWindow.cs");
        string about = Between(source, "private static StackPanel BuildAboutPage()", "private StackPanel BuildHomePage()");
        StringAssert.Contains(about, "PageCopy(ElevationStory.Summary)");
        StringAssert.Contains(about, "PageCopy(ElevationStory.HelperRequirement)");
        string consent = Between(source, "private async Task ChangeWinGetTrustedSourcesAsync(bool enabled)", "private async Task ChangeConnectorAsync");
        StringAssert.Contains(consent, "+ ElevationStory.SourceConsent");
        StringAssert.Contains(consent, "DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(Between(source, "private async Task ChangeConnectorAsync", "private async Task StartScanAsync()"), "DefaultButton = ContentDialogButton.Close");
        foreach (string contradiction in new[] { "does not use elevation or an elevated helper", "machine-scope only and requires separate review, confirmation, and elevation", "Machine-scope updates are not actionable here" })
            Assert.IsFalse(source.Contains(contradiction, StringComparison.Ordinal), contradiction);
        StringAssert.Contains(ApplicationSource("ElevationStory.cs"), "Checking for updates never asks for administrator permission");
        Assert.IsFalse(source.Contains("Cancel update", StringComparison.Ordinal), "there is no Cancel update mid-run");
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
        // Task 73 Phase 1: the literal chip labels moved from StewardPresentation.cs to the portable StatusVocabulary
        // (exact labels pinned in DesktopFoundationsPresentationTests); unmanaged still never reads as unsafe.
        StringAssert.Contains(presentation, "StatusVocabulary.For(status)");
        string vocabulary = ApplicationSource("StatusVocabulary.cs");
        StringAssert.Contains(vocabulary, "\"Update available — review\"");
        StringAssert.Contains(vocabulary, "This is not a safety warning.");
        string templateSource = DesktopSource(Path.Combine("Resources", "DataTemplates.xaml"));
        StringAssert.Contains(templateSource, "Foreground=\"{Binding StatusForeground}\"");
        StringAssert.Contains(templateSource, "Background=\"{Binding StatusBackground}\"");
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
        string results = Between(source, "private StackPanel BuildResultsPage()", "private StackPanel BuildReviewPage()");
        string review = Between(source, "private StackPanel BuildReviewPage()", "private void AddReviewDecisionActions");
        string sharedActions = Between(source, "private void AddReviewDecisionActions", "private async Task PromptAndConfirmPackageIdentityAsync");
        string detail = Between(source, "private StackPanel BuildProductDetailPage()", "private async Task ShowDetailsAsync");

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
        // Task 73 Phase 1: the FailedUnavailable chip label is the SPEC §7.2 "Source unavailable" from the portable vocabulary.
        StringAssert.Contains(ApplicationSource("StatusVocabulary.cs"), "new(nameof(UpdateIntelligenceStatus.FailedUnavailable), \"Source unavailable\"");
        StringAssert.Contains(ApplicationSource("StatusVocabulary.cs"), "nothing can be updated");

        string refresh = Between(source, "private async Task CheckUpdateSourcesAsync()", "private void AddDataAndResetSection");
        StringAssert.Contains(refresh, "workflow.RefreshUpdateIntelligenceAsync");
        StringAssert.Contains(refresh, "detailStableProductKey");
        StringAssert.Contains(refresh, "detailRow = viewModel.AllRows.SingleOrDefault");
        Assert.IsFalse(refresh.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
    }

    private static string DesktopSource(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", relativePath));

    private static string ApplicationSource(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Application", relativePath));

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
