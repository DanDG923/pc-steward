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
    private static readonly string[] PerRowActionNames =
    [
        "$\"Pin {row.Name} at installed version {row.Version}\"",
        "$\"Choose how long to ignore {row.Name}\"",
        "$\"Review {row.Name} decision evidence and options\"",
        "$\"Review {row.Name} evidence",
        "$\"Open {row.Name} evidence\"",
        "$\"Open unavailable source evidence for {row.Name}\"",
        "$\"Open repository evidence for {row.Name}\""
    ];
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
        string show = Between(source, "private void ShowCurrentPage(bool preserveViewState)", "private void RestoreScrollOffset(");
        StringAssert.Contains(show, "pageHost.VerticalOffset");
        StringAssert.Contains(show, "RestoreScrollOffset(host, page, offset)");
        StringAssert.Contains(show, "Focus(FocusState.Programmatic)");
        StringAssert.Contains(show, "pageHost.Content = page;");
        Assert.IsFalse(show.Contains("operationStatusText =", StringComparison.Ordinal), "the operation band is not rebuilt by a page refresh");

        // Independent review L8: the saved offset is applied only after layout, with one retry after the next layout pass.
        string scroll = Between(source, "private void RestoreScrollOffset(", "private bool IsFocusInside");
        int layout = scroll.IndexOf("host.UpdateLayout();", StringComparison.Ordinal);
        int change = scroll.IndexOf("host.ChangeView(null, offset, null, true);", StringComparison.Ordinal);
        Assert.IsTrue(layout >= 0 && change > layout, "layout completes before the offset is restored");
        StringAssert.Contains(scroll, "host.LayoutUpdated += Retry;");
        StringAssert.Contains(scroll, "host.LayoutUpdated -= Retry;");
    }

    [TestMethod]
    [Description("AC-UX-02; FR-UI-02 (independent review M3): focus is restored only to an unambiguous control, and repeated per-row actions name their product.")]
    public void FocusRestoreRequiresAUniqueMatchAndRowActionsNameTheirProduct()
    {
        string source = DesktopSource("MainWindow.cs");
        string show = Between(source, "private void ShowCurrentPage(bool preserveViewState)", "private void RestoreScrollOffset(");
        StringAssert.Contains(show, "PageFocusRestore.UniqueMatch(FindFocusTargets(page, focusKey))");
        StringAssert.Contains(show, "?? FocusManager.FindFirstFocusableElement(page) as Control;");
        Assert.IsFalse(source.Contains("FindFocusTarget(page, focusKey)", StringComparison.Ordinal), "the first-match focus search is gone");
        StringAssert.Contains(Between(source, "private static IEnumerable<Control> FindFocusTargets(", "private StackPanel BuildScanPage()"), "yield return control;");

        string decisions = Between(source, "private void AddDecisionsSection(StackPanel panel)", "private async Task SaveExportAsync(");
        StringAssert.Contains(decisions, "PageFocusRestore.RemoveDecisionName(PageFocusRestore.ProductName(viewModel.AllRows, decision.StableProductKey), decision.Kind)");
        StringAssert.Contains(decisions, "PageFocusRestore.RemoveManualMappingName(PageFocusRestore.ProductName(viewModel.AllRows, mapping.StableProductKey), mapping.SourceId, mapping.PackageId)");

        // Every per-row action button created inside a loop over rows, decisions or mappings carries the row's product name.
        foreach (string perRowName in PerRowActionNames) StringAssert.Contains(source, perRowName);
        Assert.IsFalse(source.Contains("$\"Remove {decision.Kind} decision\"", StringComparison.Ordinal), "decision names include the product");
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
    [Description("FR-UI-02; AC-UX-01/02 (independent review L6): every Start inventory entry point is gated on an active operation, and StartScanAsync never starts a second scan or replaces the active cancellation source.")]
    public void ScanEntryPointsAreGatedAndStartScanIsNotReentrant()
    {
        string source = DesktopSource("MainWindow.cs");
        string scan = Between(source, "private async Task StartScanAsync()", "private async Task CheckUpdateSourcesAsync()");
        int guard = scan.IndexOf("if (HasActiveOperation) { Render(); return; }", StringComparison.Ordinal);
        int assign = scan.IndexOf("scanCancellation = cancellation;", StringComparison.Ordinal);
        Assert.IsTrue(guard >= 0 && assign > guard, "the re-entrancy guard runs before the cancellation source is assigned");
        Assert.AreEqual(1, Count(scan, "scanCancellation = cancellation;"));
        Assert.IsFalse(scan.Contains("scanCancellation = new", StringComparison.Ordinal), "the active scan's source is never overwritten");
        StringAssert.Contains(scan, "if (ReferenceEquals(scanCancellation, cancellation)) scanCancellation = null;");

        string activity = Between(source, "private StackPanel BuildActivityPage()", "private void BuildWindowChrome()");
        int start = activity.IndexOf("Button start = StewardVisuals.PrimaryButton(\"Start local inventory\"", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0);
        StringAssert.Contains(activity[start..], "start.IsEnabled = !HasActiveOperation;");
        // Every button that starts a scan is disabled while any operation is active.
        Assert.AreEqual(Count(source, "PrimaryButton(\"Start local inventory\""), Count(source, "start.IsEnabled = !HasActiveOperation;"));
    }

    [TestMethod]
    [Description("FR-UI-01/02; AC-UX-02/04 (independent review L7/L9): theme and high-contrast changes refresh the page, caption buttons follow the theme, Back has a plain tooltip and the mouse Back button navigates back.")]
    public void ThemeChangesRefreshThePageAndTheTitleBarFollowsTheTheme()
    {
        string source = DesktopSource("MainWindow.cs");
        string chrome = Between(source, "private void BuildWindowChrome()", "private void OnShellPointerPressed(");
        StringAssert.Contains(chrome, "HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!windowClosed) RefreshForThemeChange(); });");
        StringAssert.Contains(chrome, "windowRoot.ActualThemeChanged += (_, _) => { if (!windowClosed) RefreshForThemeChange(); };");
        StringAssert.Contains(chrome, "ToolTipService.SetToolTip(titleBarBack, \"Back\");");
        Assert.IsFalse(source.Contains("\"Back (Alt+Left)\"", StringComparison.Ordinal), "WinUI shows the accelerator itself");
        StringAssert.Contains(chrome, "windowRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnShellPointerPressed), true);");
        StringAssert.Contains(chrome, "ApplyCaptionButtonColors();");

        string pointer = Between(source, "private void OnShellPointerPressed(", "private void RefreshForThemeChange()");
        StringAssert.Contains(pointer, "IsXButton1Pressed");
        StringAssert.Contains(pointer, "GoBack();");

        string refresh = Between(source, "private void RefreshForThemeChange()", "private void ApplyCaptionButtonColors()");
        foreach (string step in new[] { "ApplyBackdrop();", "ApplyCaptionButtonColors();", "Render();" }) StringAssert.Contains(refresh, step);

        string captions = Between(source, "private void ApplyCaptionButtonColors()", "private void ApplyBackdrop()");
        foreach (string property in new[] { "ButtonForegroundColor", "ButtonInactiveForegroundColor", "ButtonHoverForegroundColor", "ButtonHoverBackgroundColor", "ButtonPressedForegroundColor", "ButtonPressedBackgroundColor" })
            StringAssert.Contains(captions, $"bar.{property} =");
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
