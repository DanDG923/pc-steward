using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class UiRedesignContractTests
{
    private static readonly string[] SupportedThemes = ["Default", "Light", "Dark", "HighContrast"];
    private static readonly string[] MainNavigationLabels = ["Home", "Updates", "Installed apps", "Activity", "Decisions"];
    private static readonly string[] FooterNavigationLabels = ["Settings", "About"];
    private static readonly string[] EveryShellPage = ["Home", "Updates", "InstalledApps", "Activity", "Decisions", "Settings", "About", "Scan", "Results", "Review", "RepositoryApps", "Detail", "Storage", "Privacy", "Onboarding", "UpdateJob"];
    // Task 73 Phase 5 (WCAG 2.5.3, AC-UX-01): deliberately updated so every per-row name starts with the visible label
    // (StewardVisuals.ActionButton / Named → AccessibilityText.LabelInName) and still names the product; "View details" is the one verb
    // for opening an app's details (audit P-M3).
    private static readonly string[] PerRowActionNames =
    [
        "StewardVisuals.ActionButton(\"Pin current version\", $\"of {row.Name} ({row.Version})\")",
        "StewardVisuals.ActionButton(\"Ignore…\", $\"{row.Name}: choose how long\")",
        "StewardVisuals.ActionButton(\"View details\", $\"for {row.Name}\")",
        "StewardVisuals.ActionButton(\"View details\", $\"for {row.Name} (update evidence unavailable)\")",
        "$\"More options for {presentation.Name}\"",
        "StewardVisuals.Named(choose, ready.Name, $\"{ready.VersionText}: select to review\")",
        "$\"Why {needsReview.Name} needs review: {needsReview.ReasonText}\""
    ];
    private static readonly string[] ContextualEntryPoints = ["Scan", "Results", "Review", "RepositoryApps", "Detail", "Storage", "Privacy", "Decisions", "InstalledApps", "Settings", "UpdateJob"];

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
        StringAssert.Contains(Between(source, "private static IEnumerable<Control> FindFocusTargets(", DesktopWindowSource.EndOf("MainWindow.Shell.cs")), "yield return control;");

        // Task 73 Phase 3 deliberately moves the product-named Remove names into the portable DecisionsPresenter (unit-tested in
        // HomeActivityDecisionsPresentationTests); the Desktop binds them and every name still comes from PageFocusRestore with the product.
        string decisions = Between(source, "private void AddDecisionsSection(StackPanel panel)", DesktopWindowSource.EndOf("MainWindow.Decisions.cs"));
        StringAssert.Contains(decisions, "DecisionsPresenter.Build(activeDecisions, manualMappings, viewModel.AllRows)");
        StringAssert.Contains(decisions, "AutomationProperties.SetName(remove, decision.RemoveAccessibleName);");
        string decisionsPresenter = ApplicationSource("DecisionsPresentation.cs");
        StringAssert.Contains(decisionsPresenter, "string product = PageFocusRestore.ProductName(rows, decision.StableProductKey);");
        StringAssert.Contains(decisionsPresenter, "string product = PageFocusRestore.ProductName(rows, mapping.StableProductKey);");
        StringAssert.Contains(decisionsPresenter, "PageFocusRestore.RemoveDecisionName(product, \"pin current version\")");
        StringAssert.Contains(decisionsPresenter, "PageFocusRestore.RemoveDecisionName(product, \"pin source package\")");
        StringAssert.Contains(decisionsPresenter, "\"ignore indefinitely\"");
        StringAssert.Contains(decisionsPresenter, "PageFocusRestore.RemoveManualMappingName(product, mapping.SourceId, mapping.PackageId)");

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
        string results = Between(source, "private StackPanel BuildResultsPage()", DesktopWindowSource.EndOf("MainWindow.Results.cs"));
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
        string about = Between(source, "private static StackPanel BuildAboutPage()", DesktopWindowSource.EndOf("MainWindow.About.cs"));
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
        StringAssert.Contains(source, "Data from your last completed scan is kept where available");
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

        string activity = Between(source, "private StackPanel BuildActivityPage()", DesktopWindowSource.EndOf("MainWindow.Activity.cs"));
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
        string results = Between(source, "private StackPanel BuildResultsPage()", DesktopWindowSource.EndOf("MainWindow.Results.cs"));
        string review = Between(source, "private StackPanel BuildReviewPage()", "private void AddReviewDecisionActions");
        string sharedActions = Between(source, "private void AddReviewDecisionActions", "private async Task PromptAndConfirmPackageIdentityAsync");
        string detail = Between(source, "private StackPanel BuildProductDetailPage()", "private async Task ShowDetailsAsync");

        StringAssert.Contains(results, "AddReviewDecisionActions(finding, row, detail)");
        StringAssert.Contains(review, "AddReviewDecisionActions(decision, row, detail)");
        foreach (string action in new[] { "Refresh source evidence", "Confirm package identity", "Choose package identity", "Pin current version", "Ignore…", "View details" })
            StringAssert.Contains(sharedActions, action);
        StringAssert.Contains(sharedActions, "RequiresFreshSourceEvidence: true");
        StringAssert.Contains(sharedActions, "CreateSourceEvidenceRefreshButton(row)");
        Assert.IsFalse((results + review + sharedActions).Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
        StringAssert.Contains(detail, "RequiresFreshSourceEvidence: true");
        StringAssert.Contains(detail, "CreateSourceEvidenceRefreshButton(row)");
        StringAssert.Contains(detail, "Matching packages from your sources");
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

        string refresh = Between(source, "private async Task CheckUpdateSourcesAsync()", DesktopWindowSource.EndOf("MainWindow.Operations.cs"));
        StringAssert.Contains(refresh, "workflow.RefreshUpdateIntelligenceAsync");
        StringAssert.Contains(refresh, "detailStableProductKey");
        StringAssert.Contains(refresh, "detailRow = viewModel.AllRows.SingleOrDefault");
        Assert.IsFalse(refresh.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
    }

    /// <summary>
    /// Task 73 Phase 2 (ADR-030 decision 2; SPEC §7.3; audit §6.3–6.4): the Updates page is grouped by the portable presenter; only
    /// Ready rows get a selection control, nothing is preselected, Needs review and Ignored rows have no update control at all, and
    /// a selection only opens the review. Replaces the Phase 1 flat card list deliberately.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-01; FR-UI-04; FR-POL-01–05: grouped Updates, nothing preselected, no update control for needs-review rows.")]
    public void UpdatesPageGroupsEvidenceAndNeedsReviewRowsHaveNoUpdateControl()
    {
        string source = DesktopSource("MainWindow.cs");
        string page = Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow(");
        StringAssert.Contains(page, "UpdatesPagePresenter.Build(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, activeDecisions, selectedReadyKeys, DateTimeOffset.UtcNow)");
        StringAssert.Contains(page, "selectedReadyKeys.RemoveWhere(key => !page.IsSelected(key));");
        string ready = Between(page, "foreach (UpdateRowPresentation ready in page.Ready)", "if (page.Ready.Count > 0)");
        string needsReview = Between(page, "foreach (UpdateRowPresentation needsReview in page.NeedsReview)", "// Ignored and skipped");
        string ignored = Between(page, "foreach (UpdateRowPresentation ignored in page.IgnoredAndSkipped)", "Expander ignoredGroup");
        // Task 73 Phase 6 (SPEC §9.13 G4) deliberately replaces the single radio with a checkbox per Ready row; it still starts unticked.
        StringAssert.Contains(ready, "CheckBox choose = new() { IsChecked = page.IsSelected(ready.StableProductKey), Content = ready.Name };");
        Assert.IsFalse(source.Contains("RadioButton choose", StringComparison.Ordinal), "the single-choice radio is gone");
        Assert.AreEqual(1, Count(page, "CheckBox choose = new()"), "the only selection control on Updates is on Ready rows");
        // "Select all ready" and "Review all ready" choose exactly the Ready group, and only on an explicit press.
        Assert.AreEqual(2, Count(page, "selectedReadyKeys.UnionWith(page.ReadyKeys)"), "only Select all ready and Review all ready select the Ready group");
        StringAssert.Contains(page, "reviewAll.Click += async (_, _) => { selectedReadyKeys.Clear(); selectedReadyKeys.UnionWith(page.ReadyKeys); await ReviewAndExecuteAsync(RowsFor(page.ReadyKeys)); };");
        foreach (string group in new[] { needsReview, ignored })
            foreach (string control in new[] { "RadioButton", "CheckBox", "ReviewAndExecuteAsync", "PrimaryButton", "IsEnabled = false" })
                Assert.IsFalse(group.Contains(control, StringComparison.Ordinal), $"no update control (not even a disabled one): {control}");
        StringAssert.Contains(needsReview, "needsReview.ReasonText");
        StringAssert.Contains(page, "IsExpanded = ignoredGroupExpanded");
        Assert.IsFalse(source.Contains("private bool ignoredGroupExpanded = true", StringComparison.Ordinal), "Ignored and skipped starts collapsed");
        Assert.AreEqual(2, Count(page, "ReviewAndExecuteAsync("), "only the two review buttons open a review (selected apps, all ready apps)");
        StringAssert.Contains(page, "review.IsEnabled = page.CanReview && !HasActiveOperation && reviewDecision is null");
        StringAssert.Contains(page, "reviewAll.IsEnabled = page.CanReviewAll && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;");
        StringAssert.Contains(source, "private readonly HashSet<string> selectedReadyKeys = new(StringComparer.Ordinal);");
        Assert.AreEqual(1, Count(source, "selectedReadyKeys = "), "nothing is preselected: the set is only ever created empty");
        string presentationSource = ApplicationSource("UpdatesPagePresentation.cs");
        StringAssert.Contains(presentationSource, "string[] selected = selectedKeys is null ? [] : [.. ready.Select(row => row.StableProductKey).Where(key => selectedKeys.Contains(key, StringComparer.Ordinal))];");
        string presenter = ApplicationSource("UpdatesPagePresentation.cs");
        StringAssert.Contains(presenter, "case nameof(UpdateIntelligenceStatus.VerifiedUpdate):");
        StringAssert.Contains(presenter, "public bool HasUpdateControl => Group == UpdateGroup.Ready;");
        // Task 73 Phase 6 (owner decision 2026-11-02) deliberately replaces "no invented skip-version decision": Skip this version is
        // now a real, reversible decision. It is offered only with an exact known version, and a skipped row is never Ready.
        StringAssert.Contains(presenter, "case nameof(UpdateIntelligenceStatus.Skipped):");
        StringAssert.Contains(presenter, "detail?.CandidateVersion is { } version && !string.IsNullOrWhiteSpace(version)");
        StringAssert.Contains(presenter, "$\"Skip this version ({version.Trim()})\"");

        string overflow = Between(source, "private Button UpdateRowOverflow(", DesktopWindowSource.EndOf("MainWindow.Updates.cs"));
        foreach (string operation in new[] { "PromptAndIgnoreProductAsync(row)", "PromptAndPinCurrentVersionAsync(row)", "PromptAndSkipVersionAsync(row, version)", "ShowDetailsAsync(row)", "workflow.ReverseDecisionAsync(decisionId, token)" })
            StringAssert.Contains(overflow, operation);
        Assert.IsFalse(overflow.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
        // The skip prompt uses the existing decision confirmation pattern: Cancel default, optional bounded reason, and no update authority.
        string skip = Between(source, "private async Task PromptAndSkipVersionAsync(InventoryRow row, string version)", "private static TextBox DecisionReasonBox(");
        StringAssert.Contains(skip, "DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(skip, "DecisionReasonBox(");
        StringAssert.Contains(skip, "workflow.SkipVersionAsync(row.StableProductKey, version, decisionReason, token)");
        foreach (string authority in new[] { "ReviewAndExecuteAsync", "ConfirmAndRun", "BeginReviewAsync", "BeginBatchReviewAsync", "TrySetResult" })
            Assert.IsFalse(skip.Contains(authority, StringComparison.Ordinal), $"skipping a version has no update authority: {authority}");
    }

    /// <summary>
    /// Task 73 Phase 2 (ADR-030 decision 3; FR-UPD-01/04; audit P-H6, P-M3): the job stepper keeps the existing review → explicit
    /// confirmation → execution order exactly, with Cancel as the default, the UAC shield only when administrator approval is
    /// required, and leaving the page (or closing the window) cancelling the review.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-01/04/05/06; FR-UI-03/04: review sheet replaces the wall-of-text dialog; ordering and Cancel default are kept.")]
    public void UpdateJobReviewKeepsTheOrderingCancelDefaultAndNeverApprovesByNavigation()
    {
        string source = DesktopSource("MainWindow.cs");
        string review = source[source.IndexOf("private async Task ReviewAndExecuteAsync", StringComparison.Ordinal)..];
        string[] ordered =
        [
            "if (HasActiveOperation || windowClosed || reviewDecision is not null || rows.Count == 0) return;",
            "updates.BeginBatchReviewAsync(keys, preparationProgress, token)",
            "item.Preparation.Review!.Selection,",
            "UpdateReviewSheet.CreateBatch(inputs,",
            "NavigateTo(ShellPage.UpdateJob);",
            "bool confirmed = await decision.Task;",
            "if (!confirmed || windowClosed)",
            "isExecuting = true;",
            "updates.ConfirmAndRunBatchAsync(preparation, job.Stop,"
        ];
        int previous = -1;
        foreach (string step in ordered)
        {
            int index = review.IndexOf(step, StringComparison.Ordinal);
            Assert.IsTrue(index > previous, $"out of order or missing: {step}");
            previous = index;
        }
        Assert.IsFalse(source.Contains("Title = \"Review approved update\"", StringComparison.Ordinal), "the wall-of-text dialog is gone");
        Assert.IsFalse(source.Contains("Install approved update", StringComparison.Ordinal), "never 'approved' before approval");
        // Leaving the page cancels the review and clears it before the next page is built (independent review).
        StringAssert.Contains(source, "if (navigation.Current != ShellPage.UpdateJob) CancelPendingReview();");
        string cancelPending = Between(source, "private void CancelPendingReview()", "private bool ShowsJobResultInBand");
        int clear = cancelPending.IndexOf("reviewDecision = null;", StringComparison.Ordinal);
        int settle = cancelPending.IndexOf("pending.TrySetResult(false);", StringComparison.Ordinal);
        Assert.IsTrue(clear >= 0 && settle > clear, "the review is cleared, then cancelled");
        StringAssert.Contains(source, "Closed += (_, _) => { windowClosed = true; reviewDecision?.TrySetResult(false);");
        Assert.AreEqual(1, Count(source, "TrySetResult(true)"), "exactly one confirmation path in the whole window");

        string reviewStep = Between(source, "private void AddReviewStep(", "private void AddUpdateStep(");
        int cancel = reviewStep.IndexOf("actions.Children.Add(cancel);", StringComparison.Ordinal);
        int confirm = reviewStep.IndexOf("actions.Children.Add(confirm);", StringComparison.Ordinal);
        Assert.IsTrue(cancel >= 0 && confirm > cancel, "Cancel comes first");
        StringAssert.Contains(reviewStep, "pageInitialFocus = cancel;");
        StringAssert.Contains(reviewStep, "StewardVisuals.ActionButton(UpdateReviewSheet.CancelButtonLabel,");
        StringAssert.Contains(reviewStep, "decision.TrySetResult(true);");
        Assert.AreEqual(1, Count(reviewStep, "decision.TrySetResult(true);"), "only the specific primary button confirms");
        Assert.IsFalse(reviewStep.Contains("KeyboardAccelerator", StringComparison.Ordinal), "no Enter accelerator on the update button");
        // Task 73 Phase 6: every app in the frozen set shows its own facts and confidence; excluded apps are explained.
        StringAssert.Contains(reviewStep, "foreach (UpdateReviewSheetItem item in sheet.Items)");
        StringAssert.Contains(reviewStep, "foreach (ReviewFact fact in item.Facts)");
        StringAssert.Contains(reviewStep, "item.ConfidenceSummary");
        StringAssert.Contains(reviewStep, "sheet.Excluded");
        StringAssert.Contains(reviewStep, "sheet.ElevationNote");
        StringAssert.Contains(reviewStep, "foreach (ReviewRiskCallout risk in sheet.Risks)");
        StringAssert.Contains(reviewStep, "sheet.Attestation");
        StringAssert.Contains(source, "?? initialFocus");
    }

    /// <summary>
    /// Task 73 Phase 2 independent-review fixes (FR-UPD-01/04, FR-UI-03/04, UJ-04): Cancel or leaving the review re-enables the
    /// Review button; a review being prepared blocks a second one and shows a busy state; a decided sheet cannot be confirmed or
    /// cancelled again; a new review gets a fresh page; the confirmation fine print starts expanded; a stale choice is cleared;
    /// and a job that finishes elsewhere is announced in the band instead of moving the user.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-01/04; FR-UI-03/04; UJ-04 (independent review): review state is released, guarded and never moves the user.")]
    public void UpdateJobReviewStateIsReleasedGuardedAndNeverMovesTheUser()
    {
        string source = DesktopSource("MainWindow.cs");
        string review = Between(source, "private async Task ReviewAndExecuteAsync", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");

        // Item 1: the not-confirmed path always re-renders the page now shown, and Cancel clears the review before navigating.
        string notConfirmed = Between(review, "if (!confirmed || windowClosed)", "isExecuting = true;");
        StringAssert.Contains(notConfirmed, "if (!windowClosed) Render();");
        Assert.IsFalse(notConfirmed.Contains("navigation.Current == ShellPage.UpdateJob", StringComparison.Ordinal), "re-render whichever page is shown");
        StringAssert.Contains(review, "if (ReferenceEquals(reviewDecision, decision)) reviewDecision = null;");
        string reviewStep = Between(source, "private void AddReviewStep(", "private void AddUpdateStep(");
        string cancelHandler = Between(reviewStep, "cancel.Click += (_, _) =>", "confirm.Click +=");
        int settled = cancelHandler.IndexOf("if (!decision.TrySetResult(false)) return;", StringComparison.Ordinal);
        int cleared = cancelHandler.IndexOf("reviewDecision = null;", StringComparison.Ordinal);
        int leave = cancelHandler.IndexOf("GoBack();", StringComparison.Ordinal);
        Assert.IsTrue(settled >= 0 && cleared > settled && leave > cleared, "Cancel settles, clears, then navigates (and never navigates once decided)");

        // Item 2: preparation is guarded and visible; it covers preparation only.
        StringAssert.Contains(source, "|| downloads.IsCleaning || preparingReview || isSavingSchedule;");
        int guard = review.IndexOf("if (HasActiveOperation || windowClosed || reviewDecision is not null || rows.Count == 0) return;", StringComparison.Ordinal);
        int set = review.IndexOf("preparingReview = true;", StringComparison.Ordinal);
        int begin = review.IndexOf("updates.BeginBatchReviewAsync(keys, preparationProgress, token)", StringComparison.Ordinal);
        int reset = review.IndexOf("finally { preparingReview = false; }", StringComparison.Ordinal);
        int sheet = review.IndexOf("UpdateReviewSheet.CreateBatch(inputs,", StringComparison.Ordinal);
        Assert.IsTrue(guard >= 0 && set > guard && begin > set && reset > begin && sheet > reset, "preparingReview wraps BeginReviewAsync only");
        StringAssert.Contains(source, "private const string PreparingReviewLabel = \"Preparing review…\";");
        StringAssert.Contains(source, "? PreparingReviewLabel");
        string detail = Between(source, "Button review = StewardVisuals.PrimaryButton(\"Review and update\"", "decisionActions.Children.Add(review);");
        StringAssert.Contains(detail, "review.IsEnabled = !HasActiveOperation && reviewDecision is null;");

        // Items 2 and 10: a decided sheet renders both buttons disabled; Confirm also waits for any active operation.
        StringAssert.Contains(reviewStep, "bool decided = decision.Task.IsCompleted;");
        StringAssert.Contains(reviewStep, "cancel.IsEnabled = !decided;");
        StringAssert.Contains(reviewStep, "confirm.IsEnabled = !HasActiveOperation && !decided;");

        // Item 5: a new review from the job page gets a fresh render (focus on Cancel, scroll reset).
        StringAssert.Contains(review, "if (navigation.Current == ShellPage.UpdateJob) ShowCurrentPage(preserveViewState: false); else NavigateTo(ShellPage.UpdateJob);");

        // Item 8: the attestation and coverage disclosure start expanded.
        StringAssert.Contains(reviewStep, "Header = \"What you're confirming\", Content = fineprint, IsExpanded = true");

        // Item 6: any reload, rescan, Clear scan data or finished job ends the earlier choice.
        string load = Between(source, "private bool LoadSavedInventory()", "try { DataOperationLease.RunReadSynchronous(");
        StringAssert.Contains(load, "selectedReadyKeys.Clear();");
        string completion = review[review.IndexOf("finally { StopUpdateProgressPolling();", StringComparison.Ordinal)..];
        StringAssert.Contains(completion, "LoadSavedInventory();");
        StringAssert.Contains(Between(source, "private async Task ConfirmAndClearScanDataAsync()", "private async Task ConfirmAndClearDownloadedFilesAsync("), "LoadSavedInventory();");

        // Item 7: completion never navigates; elsewhere the band shows the result with "View result".
        string tail = review[review.IndexOf("job.Completion = run is not null", StringComparison.Ordinal)..];
        Assert.IsFalse(tail.Contains("NavigateTo(", StringComparison.Ordinal), "completion does not move the user");
        StringAssert.Contains(tail, "jobResultPending = navigation.Current != ShellPage.UpdateJob;");
        string visuals = Between(source, "private void RefreshOperationVisuals()", "private static Style AppStyle(");
        StringAssert.Contains(visuals, "HasActiveOperation || ShowsJobResultInBand ? Visibility.Visible : Visibility.Collapsed");
        StringAssert.Contains(visuals, "string viewLabel = showResult ? \"View result\" : \"View update progress\";");
        StringAssert.Contains(source, "if (navigation.Current != ShellPage.UpdateJob) CancelPendingReview(); else jobResultPending = false;");
    }

    /// <summary>
    /// Task 73 final review L2 (FR-UPD-01/04/08/09, FR-HIS-03): clicking Confirm marks execution pending synchronously, before the
    /// single confirmation completes the decision, so HasActiveOperation is true at once and F5 or a scan cannot start in between;
    /// ConfirmAndRunAsync still runs only after the awaited decision. A data-lease refusal before the workflow is entered reads as
    /// "Not started", never as an unrecorded outcome.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-01/04/08/09; FR-HIS-03 (final review L2): Confirm blocks other work at once; a lease refusal reads as Not started.")]
    public void ConfirmMarksExecutionPendingAtOnceAndALeaseRefusalReadsAsNotStarted()
    {
        string source = DesktopSource("MainWindow.cs");
        StringAssert.Contains(source, "|| isExecuting || executionPending || isClearing ||");
        string reviewStep = Between(source, "private void AddReviewStep(", "private void AddUpdateStep(");
        string confirmHandler = Between(reviewStep, "confirm.Click += (_, _) =>", "actions.Children.Add(cancel);");
        int completedGuard = confirmHandler.IndexOf("if (decision.Task.IsCompleted) return;", StringComparison.Ordinal);
        int pending = confirmHandler.IndexOf("executionPending = true;", StringComparison.Ordinal);
        int settle = confirmHandler.IndexOf("decision.TrySetResult(true);", StringComparison.Ordinal);
        Assert.IsTrue(completedGuard >= 0 && pending > completedGuard && settle > pending, "pending is set before the decision completes");
        StringAssert.Contains(confirmHandler, "if (!accepted) executionPending = false;");
        Assert.AreEqual(1, Count(source, "TrySetResult(true)"), "still exactly one confirmation path");

        string review = Between(source, "private async Task ReviewAndExecuteAsync", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");
        StringAssert.Contains(Between(review, "if (!confirmed || windowClosed)", "isExecuting = true;"), "executionPending = false;");
        int awaited = review.IndexOf("bool confirmed = await decision.Task;", StringComparison.Ordinal);
        int executing = review.IndexOf("isExecuting = true;", StringComparison.Ordinal);
        int handOver = review.IndexOf("executionPending = false;", executing, StringComparison.Ordinal);
        int run = review.IndexOf("updates.ConfirmAndRunBatchAsync(", StringComparison.Ordinal);
        Assert.IsTrue(awaited >= 0 && executing > awaited && handOver > executing && run > handOver, "execution starts only after the awaited decision");

        int entered = review.IndexOf("Volatile.Write(ref operationEntered, true);", StringComparison.Ordinal);
        Assert.IsTrue(entered >= 0 && run > entered, "the flag is set inside the lease, before the workflow runs");
        string leaseCatch = "catch (InvalidOperationException) when (!Volatile.Read(ref operationEntered)) { result = new(false, UpdateOutcomePresenter.DataUnavailableNotStartedMessage, null); }";
        int refusal = review.IndexOf(leaseCatch, StringComparison.Ordinal);
        int general = review.IndexOf("catch (Exception) { result = new(true, \"The reviewed update did not produce a recorded outcome.", StringComparison.Ordinal);
        Assert.IsTrue(refusal >= 0 && general > refusal, "a lease refusal is caught separately, before the unrecorded-outcome fallback");
    }

    /// <summary>
    /// Task 73 Phase 6 (SPEC §9.13 G4; FR-UPD-10/11; AC-UPD-09/10): Update all runs the frozen set one app at a time through the
    /// existing per-app confirm-and-run (fresh revalidation, own approval, own elevation), with no change to the elevated helper. The
    /// only stop control asks to stop before the next app; nothing cancels a running installer, there is no "Cancel update", and the
    /// batch never reaches the Chocolatey reviewed route. Verify groups the per-app outcomes.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-10/11; AC-UPD-09/10; SPEC §9.13 G4: sequential batch through the existing per-app path; stop only between apps; no Cancel update.")]
    public void UpdateAllRunsSequentiallyThroughThePerAppPathAndStopsOnlyBetweenApps()
    {
        string workflow = ApplicationSource("ReviewedUpdateExecutionWorkflow.cs");
        string batch = Between(workflow, "public async Task<UpdateBatchRunResult> ConfirmAndRunBatchAsync(", "private static string NotStartedMessage(");
        int stopCheck = batch.IndexOf("stop.IsRequested", StringComparison.Ordinal);
        int run = batch.IndexOf("await ConfirmAndRunAsync(jobReview,", StringComparison.Ordinal);
        Assert.IsTrue(stopCheck >= 0 && run > stopCheck, "stop is read before an app starts");
        Assert.AreEqual(1, Count(batch, "await ConfirmAndRunAsync("), "every app goes through the one existing confirm-and-run path");
        foreach (string forbidden in new[] { "coordinator.RunAsync", "approvals.ConfirmAsync", "CancellationTokenSource", ".Cancel(", "Elevat", "Chocolatey", "UpdateDispatch" })
            Assert.IsFalse(batch.Contains(forbidden, StringComparison.Ordinal), $"the batch adds no execution, approval, cancellation or elevation path of its own: {forbidden}");
        StringAssert.Contains(workflow, "public static readonly TimeSpan MaximumBatchDispatchAge = TimeSpan.FromMinutes(30);");

        string source = DesktopSource("MainWindow.cs");
        string step = Between(source, "private void AddUpdateStep(", "private void AddVerifyStep(");
        StringAssert.Contains(step, "Button stop = StewardVisuals.ActionButton(UpdateJobProgressText.StopAfterCurrentLabel);");
        StringAssert.Contains(step, "stop.Click += (_, _) => { job.Stop.Request(); Render(); };");
        StringAssert.Contains(step, "if (job.Sheet.ItemCount > 1 && job.CurrentIndex < job.Sheet.ItemCount - 1)");
        Assert.IsFalse(source.Contains("Cancel update", StringComparison.Ordinal), "there is no Cancel update");
        string review = Between(source, "private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");
        foreach (string forbidden in new[] { "Chocolatey", "ReviewedUpdateDispatch", "LiveApprovedUpdate", "updates.ConfirmAndRunAsync(" })
            Assert.IsFalse(review.Contains(forbidden, StringComparison.Ordinal), $"the batch never reaches another route: {forbidden}");
        string notice = Between(source, "private void OnBatchItemNotice(", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");
        foreach (string authority in new[] { "TrySetResult", "ConfirmAndRun", "Stop.Request" })
            Assert.IsFalse(notice.Contains(authority, StringComparison.Ordinal), $"a progress notice only presents: {authority}");

        string verify = Between(source, "private void AddVerifyStep(", "private async Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?> ReadReviewedHistoryAsync(");
        StringAssert.Contains(verify, "completion.Groups");
        StringAssert.Contains(verify, "RowsFor([key])");
        StringAssert.Contains(ApplicationSource("UpdateJobPresentation.cs"), "public const string PerAppElevationNote =");
    }

    /// <summary>
    /// Task 73 Phase 6 independent review (M2, L2, L6; SPEC §9.13 G4; FR-UPD-10, AC-UPD-01): batch review preparation shares one
    /// fresh observation and starts the ceiling after preparation; preparation is cancellable from the band and reports progress;
    /// dispatch re-checks the ceiling after its own re-preparation and immediately before approval; a frozen review runs once.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-10; AC-UPD-01; SPEC §9.13 G4: shared fresh observation, ceiling after preparation and before approval, single-use batch, cancellable preparation.")]
    public void UpdateAllPreparationIsSharedCancellableAndTheCeilingIsCheckedBeforeApproval()
    {
        string app = DesktopSource("App.xaml.cs");
        StringAssert.Contains(app, "prepareSelections: workflow.PrepareApprovedUpdateSelectionsAsync");
        StringAssert.Contains(app, "new(workflow.PrepareApprovedUpdateSelectionAsync, new UpdateJobApprovalService(database), coordinator, runningApplications,");

        string workflow = ApplicationSource("ReviewedUpdateExecutionWorkflow.cs");
        string begin = Between(workflow, "public async Task<UpdateBatchReviewPreparation> BeginBatchReviewAsync(", "private async Task<IReadOnlyList<UpdateSelectionPreparation?>> PrepareSelectionsAsync(");
        int prepare = begin.IndexOf("await PrepareSelectionsAsync(keys, Report, cancellationToken)", StringComparison.Ordinal);
        int reviewedAt = begin.IndexOf("return new(items, excluded, utcNow());", StringComparison.Ordinal);
        Assert.IsTrue(prepare >= 0 && reviewedAt > prepare, "ReviewedAt is taken after every app was prepared");
        Assert.AreEqual(1, Count(begin, "utcNow()"), "the ceiling has one start");

        string confirm = Between(workflow, "public async Task<ReviewedUpdateExecutionResult> ConfirmAndRunAsync(", "public Task<UpdateBatchReviewPreparation> BeginBatchReviewAsync(");
        int evidence = confirm.IndexOf("review.Selection.EqualsEvidence(preparation.Selection)", StringComparison.Ordinal);
        int ceiling = confirm.IndexOf("IsPastDispatchCeiling(reviewed, utcNow())", StringComparison.Ordinal);
        int approve = confirm.IndexOf("await approvals.ConfirmAsync(review,", StringComparison.Ordinal);
        Assert.IsTrue(evidence >= 0 && ceiling > evidence && approve > ceiling, "the ceiling is re-read after re-preparation and immediately before approval");

        string batch = Between(workflow, "public async Task<UpdateBatchRunResult> ConfirmAndRunBatchAsync(", "private static string NotStartedMessage(");
        int claim = batch.IndexOf("if (!review.TryClaimForRun()) throw new InvalidOperationException(", StringComparison.Ordinal);
        int firstRun = batch.IndexOf("await ConfirmAndRunAsync(jobReview,", StringComparison.Ordinal);
        Assert.IsTrue(claim >= 0 && firstRun > claim, "a frozen review is claimed once, before any app is re-prepared or approved");
        StringAssert.Contains(batch, "cancellationToken, item.Preparation.Progress, review.ReviewedAt)");

        string inventory = ApplicationSource("InventoryWorkflow.cs");
        string shared = Between(inventory, "public async Task<IReadOnlyList<UpdateSelectionPreparation>> PrepareApprovedUpdateSelectionsAsync(", "private sealed record FreshSelectionEvidence(");
        Assert.AreEqual(1, Count(shared, "await ObserveFreshSelectionEvidenceAsync(cancellationToken)"), "one fresh observation for the whole batch review");
        StringAssert.Contains(shared, "await PrepareAgainstFreshEvidenceAsync(key, evidence, cancellationToken)");
        string single = Between(inventory, "public async Task<UpdateSelectionPreparation> PrepareApprovedUpdateSelectionAsync(", "public async Task<IReadOnlyList<UpdateSelectionPreparation>> PrepareApprovedUpdateSelectionsAsync(");
        StringAssert.Contains(single, "await PrepareAgainstFreshEvidenceAsync(stableProductKey, evidence, cancellationToken)", "the single and batch paths share the per-app validation");

        string source = DesktopSource("MainWindow.cs");
        string review = Between(source, "private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");
        StringAssert.Contains(review, "DataOperationLease.RunAsync(token => updates.BeginBatchReviewAsync(keys, preparationProgress, token), preparationCancellation.Token)");
        StringAssert.Contains(review, "preparation.ExpiresAt.ToLocalTime()");
        StringAssert.Contains(source, "cancelReview.Click += (_, _) => reviewPreparationCancellation?.Cancel();");
        StringAssert.Contains(source, "UpdateJobProgressText.PreparingReview(PreparingReviewLabel, reviewPreparationProgress)");
        StringAssert.Contains(source, "UpdateJobProgressText.BandLabel(updateProgressSubject, updateProgressLabel)");
        string reviewStep = Between(source, "private void AddReviewStep(", "private void AddUpdateStep(");
        StringAssert.Contains(reviewStep, "sheet.ExpiryNote is { } expiryNote");
    }

    /// <summary>
    /// Task 73 Phase 2 (ADR-030 decision 3; FR-UPD-08/09; FR-DAT-09; audit P-H3/P-H4): Verify shows the mapped per-item outcome,
    /// its evidence, restart guidance and Delete downloaded files through the existing cleanup; a refused UAC prompt returns to
    /// Review instead of reading as a failure.
    /// </summary>
    [TestMethod]
    [Description("FR-UPD-07–09; FR-DAT-09; UJ-04: four honest outcomes, refusal returns to Review, completion cleanup.")]
    public void VerifyStepShowsHonestOutcomesRestartGuidanceAndCleanup()
    {
        string source = DesktopSource("MainWindow.cs");
        string verify = Between(source, "private void AddVerifyStep(", "private async Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?> ReadReviewedHistoryAsync(");
        // AC-UPD-09: cards follow the outcome groups, so an interleaved batch never shows an item under another group's heading.
        StringAssert.Contains(verify, "foreach (UpdateJobItemOutcome item in completion.DisplayItems)");
        Assert.IsFalse(verify.Contains("in completion.Items)", StringComparison.Ordinal), "never the raw batch order");
        StringAssert.Contains(verify, "outcome.Label");
        StringAssert.Contains(verify, "outcome.Summary");
        StringAssert.Contains(verify, "outcome.TechnicalDetail");
        StringAssert.Contains(verify, "completion.RestartGuidance");
        StringAssert.Contains(verify, "ConfirmAndClearDownloadedFilesAsync(\"Delete downloaded files after update completion?\")");
        StringAssert.Contains(verify, "UpdateJobCleanupText.Recoverable(downloadCache.RecoverableBytes)");
        StringAssert.Contains(verify, "IsEnabled = downloadCache.RecoverableBytes > 0 && !HasActiveOperation");
        Assert.IsFalse(verify.Contains("Update failed", StringComparison.Ordinal), "no flattened failure title in the Desktop");
        StringAssert.Contains(source, "job.Step = job.Completion.ReturnsToReview ? UpdateJobStep.Review : UpdateJobStep.Verify;");
        string presenter = ApplicationSource("UpdateJobPresentation.cs");
        foreach (string label in new[] { "VerifiedLabel = \"Verified\"", "RestartRequiredLabel = \"Restart required\"", "InstalledNotVerifiedLabel = \"Installed, not verified\"", "FailedLabel = \"Failed\"" })
            StringAssert.Contains(presenter, label);
        StringAssert.Contains(presenter, "NeverRestartsNote = \"PC Steward never restarts your PC.\"");
    }

    /// <summary>
    /// Task 73 Phase 3 (FR-UI-01, FR-POL-01–05, FR-HIS-02; audit P-L7, P-M1, P-L6, P-M5): Installed apps has saved filters with counts
    /// over every app and keeps the virtualized, bounded list; app details use the same confirmation dialogs as the lists for pin,
    /// ignore and every mapping action (no immediate decision writes), and show structured sections and the timeline as a list.
    /// </summary>
    [TestMethod]
    [Description("FR-UI-01; FR-POL-01–05; FR-HIS-02; audit P-L7/P-M1/P-L6: saved filters, shared decision dialogs, structured details.")]
    public void InstalledAppsUsesSavedFiltersAndDetailsUseTheSharedDecisionDialogs()
    {
        string source = DesktopSource("MainWindow.cs");
        string apps = Between(source, "private StackPanel BuildInstalledAppsPage()", "private static (string Raw, string Label)[] LabelledFilterOptions");
        StringAssert.Contains(apps, "foreach (InstalledAppsFilterOption option in InstalledAppsFilters.Options(viewModel.AllRows))");
        StringAssert.Contains(apps, "viewModel.SetSavedFilter(value)");
        StringAssert.Contains(apps, "AutomationProperties.SetName(filter, option.AccessibleName);");
        StringAssert.Contains(apps, "AutomationProperties.SetItemStatus(filter, \"Selected\");");
        StringAssert.Contains(apps, "ItemTemplate = (DataTemplate)");
        StringAssert.Contains(apps, "MaxHeight = 620", "the virtualized list keeps a bounded height inside the page host");
        StringAssert.Contains(apps, "StatusVocabulary.For(nameof(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged)).Meaning");
        string filters = ApplicationSource("InstalledAppsPresentation.cs");
        StringAssert.Contains(filters, "rows.Count(row => Matches(filter, row.Status))");
        StringAssert.Contains(filters, "!Statuses.TryGetValue(filter, out string[]? keys) ||", "All matches every status");

        string detail = Between(source, "private StackPanel BuildProductDetailPage()", "private async Task ShowDetailsAsync");
        foreach (string shared in new[] { "PromptAndPinCurrentVersionAsync(row)", "PromptAndIgnoreProductAsync(row)", "PromptAndConfirmPackageIdentityAsync(row, detail)",
            "PromptAndCreateManualMappingAsync(row, candidate.SourceId, candidate.PackageId)", "PromptAndCreateManualMappingAsync(row, sourceId, packageId)" })
            StringAssert.Contains(detail, shared);
        foreach (string immediate in new[] { "workflow.PinCurrentVersionAsync", "workflow.IgnoreAsync", "workflow.CreateManualMappingAsync", "string.Join(\"\\n\"" })
            Assert.IsFalse(detail.Contains(immediate, StringComparison.Ordinal), $"no immediate decision write or newline dump on details: {immediate}");
        foreach (string section in new[] { "AppDetailPresenter.Timeline(detailTimeline)", "AppDetailPresenter.Instances(row.Instances)", "AppDetailPresenter.Candidates(" })
            StringAssert.Contains(detail, section);

        string mapping = Between(source, "private async Task PromptAndCreateManualMappingAsync(", "private async Task PromptAndPinCurrentVersionAsync(");
        StringAssert.Contains(mapping, "DefaultButton = ContentDialogButton.Close");
        int read = mapping.IndexOf("string decisionReason = reason.Text;", StringComparison.Ordinal);
        int lease = mapping.IndexOf("DataOperationLease.RunAsync(", StringComparison.Ordinal);
        Assert.IsTrue(read >= 0 && lease > read, "the reason is read on the UI thread before the lease");
        StringAssert.Contains(mapping, "It does not approve or start an update.");
        Assert.IsFalse(mapping.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
    }

    /// <summary>
    /// Task 73 Phase 3 (FR-HIS-01–03): Activity is a read-only, filterable, day-grouped history built by the portable presenter, with
    /// the Phase 2 outcome states and sanitized details, and keeps the Task 50 reviewed-outcomes section. The history reader only reads.
    /// </summary>
    [TestMethod]
    [Description("FR-HIS-01–03: filterable day-grouped Activity, honest outcomes, sanitized details, read-only reader, Task 50 section kept.")]
    public void ActivityIsAReadOnlyFilterableHistoryThatKeepsReviewedOutcomes()
    {
        string source = DesktopSource("MainWindow.cs");
        string activity = Between(source, "private StackPanel BuildActivityPage()", "private void AddReviewedUpdateOutcomesSection(");
        StringAssert.Contains(activity, "ActivityPresenter.Entries(UpdateJobHistory.Group(updateHistory), scanRuns, activeDecisions, manualMappings, viewModel.AllRows)");
        StringAssert.Contains(activity, "ActivityPresenter.Build(entries, activityFilter, DateTimeOffset.UtcNow)");
        StringAssert.Contains(activity, "foreach (ActivityDay day in page.Days)");
        StringAssert.Contains(activity, "Header = \"Details\"");
        StringAssert.Contains(activity, "AutomationProperties.SetItemStatus(filter, \"Selected\");");
        int days = activity.IndexOf("foreach (ActivityDay day in page.Days)", StringComparison.Ordinal);
        int reviewed = activity.IndexOf("AddReviewedUpdateOutcomesSection(panel);", StringComparison.Ordinal);
        Assert.IsTrue(days >= 0 && reviewed > days, "the Task 50 reviewed-outcomes section is kept");
        foreach (string action in new[] { "workflow.", "DataOperationLease.Run", "ReviewAndExecuteAsync", "FileSavePicker", "Export", "Clear" })
            Assert.IsFalse(activity.Contains(action, StringComparison.Ordinal), $"Activity is read-only: {action}");

        string presenter = ApplicationSource("ActivityPresentation.cs");
        StringAssert.Contains(presenter, "UpdateOutcomePresenter.ForStatus(final.Status, final.Diagnostic, final.ExitCode, final.TargetVersion)");
        StringAssert.Contains(presenter, "SanitizedDiagnosticBundle.Sanitize(job.Final.Diagnostic)");
        string reader = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Persistence.Sqlite", "SqliteActivityHistory.cs"));
        foreach (string write in new[] { "INSERT", "UPDATE ", "DELETE", "CREATE ", "DROP", "ExecuteNonQuery" })
            Assert.IsFalse(reader.Contains(write, StringComparison.Ordinal), $"the activity reader only reads: {write}");
        StringAssert.Contains(reader, "LIMIT $limit");

        // Audit P-M6: one date format and human-readable sizes through the portable DisplayFormat.
        foreach (string raw in new[] { "ToString(\"g\"", ":N0} bytes", "} characters", "d MMMM yyyy", "dddd, d MMMM" })
            Assert.IsFalse(source.Contains(raw, StringComparison.Ordinal) || DesktopSource("StewardPresentation.cs").Contains(raw, StringComparison.Ordinal), $"no ad-hoc date or size format: {raw}");
    }

    /// <summary>
    /// Task 73 Phase 3 (FR-POL-01–05; ADR-030 foundation 7): Decisions is one view of pins, ignores and mappings, each reversed only
    /// through its existing operation; Home keeps unmanaged as "not a safety warning", its tiles are real buttons, and "Check for
    /// updates" composes only the existing read-only scan and source refresh (never review or execution).
    /// </summary>
    [TestMethod]
    [Description("FR-POL-01–05; FR-UI-01/05/06: one reversible Decisions view; Home tiles are buttons; Check for updates never reviews, approves or elevates.")]
    public void DecisionsAreReversibleAndHomeNeverStartsAnUpdate()
    {
        string source = DesktopSource("MainWindow.cs");
        string decisions = Between(source, "private void AddDecisionsSection(StackPanel panel)", DesktopWindowSource.EndOf("MainWindow.Decisions.cs"));
        StringAssert.Contains(decisions, "workflow.DeleteManualMappingAsync(decision.Id, token)");
        StringAssert.Contains(decisions, "workflow.ReverseDecisionAsync(decision.Id, token)");
        StringAssert.Contains(decisions, "decision.Reason is { } reason");
        Assert.IsFalse(decisions.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
        StringAssert.Contains(Between(source, "private StackPanel BuildDecisionsPage()", "private void AddDecisionsSection("), "AddDecisionsSection(panel);");

        string home = Between(source, "private StackPanel BuildHomePage()", DesktopWindowSource.EndOf("MainWindow.Home.cs"));
        StringAssert.Contains(home, "HomePresenter.Build(new HomeInputs(");
        // UI polish phase 2: the "At a glance" rows are real buttons named by the presenter.
        StringAssert.Contains(home, "Button button = StewardVisuals.NavigationRow(");
        StringAssert.Contains(home, "AutomationProperties.SetName(button, row.AccessibleName);");
        // Each action does only its own thing: Review opens Updates, Turn on opens the existing consent, Check and Scan run the read-only scan.
        string actions = Between(home, "private Button HomeActionButton(", "private InfoBar HomeNoticeBar(");
        StringAssert.Contains(actions, "button.Click += (_, _) => NavigateTo(ShellPage.Updates);");
        StringAssert.Contains(actions, "button.Click += async (_, _) => await ChangeWinGetTrustedSourcesAsync(true);");
        StringAssert.Contains(actions, "button.Click += async (_, _) => await CheckForUpdatesAsync();");
        foreach (string authority in new[] { "ReviewAndExecuteAsync", "ConfirmAndRunAsync", "ConfirmAndRunBatchAsync", "BeginReviewAsync", "BeginBatchReviewAsync", "selectedReadyKeys.", "SaveConnectorStatesAsync", "SaveConnectorStateAsync", "ChangeWinGetTrustedSourcesAsync(false)" })
            Assert.IsFalse(home.Contains(authority, StringComparison.Ordinal), $"Home never selects, reviews or runs an update, and never changes a source without its consent: {authority}");
        // Turning sources on from Home goes through the one consent path: the disclosure dialog, Keep local-only as the default.
        string consent = Between(source, "private async Task ChangeWinGetTrustedSourcesAsync(bool enabled)", "private async Task ChangeConnectorAsync(");
        StringAssert.Contains(consent, "PrimaryButtonText = \"I approve\"");
        StringAssert.Contains(consent, "DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(consent, "if (!approved) { Render(); return; }");
        foreach (string started in new[] { "StartScanAsync", "CheckUpdateSourcesAsync", "CheckForUpdatesAsync" })
            Assert.IsFalse(consent.Contains(started, StringComparison.Ordinal), $"turning sources on starts nothing: {started}");
        string check = Between(source, "private async Task CheckForUpdatesAsync()", "private async Task StartScanAsync()");
        StringAssert.Contains(check, "if (HasActiveOperation) { Render(); return; }");
        StringAssert.Contains(check, "await StartScanAsync();");
        StringAssert.Contains(check, "await CheckUpdateSourcesAsync();");
        foreach (string authority in new[] { "ReviewAndExecuteAsync", "ConfirmAndRunAsync", "ConfirmAndRunBatchAsync", "Elevat", "runas" })
            Assert.IsFalse(check.Contains(authority, StringComparison.OrdinalIgnoreCase), authority);
        string homePresenter = ApplicationSource("HomePresentation.cs");
        StringAssert.Contains(ApplicationSource("StatusVocabulary.cs"), "This is not a safety warning.");
        Assert.IsFalse(homePresenter.Contains("\"Unmanaged\"", StringComparison.Ordinal), "Home says \"No update source\", never \"Unmanaged\"");
        StringAssert.Contains(homePresenter, "UpdatesPagePresenter.Build(rows, inputs.Details, inputs.SourcesEnabled, inputs.Decisions, null, inputs.Now).Ready.Count", "Home's ready count is the Updates Ready group with nothing selected");
        Assert.IsFalse(homePresenter.Contains("unsafe", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Task 73 Phase 3 independent review (FR-UI-05/06, FR-HIS-01; L1, L3, L6): Home binds the presenter's secondary label, caption and
    /// attention-list filter rather than hard-coding them, and row names sit on elements that have an automation peer (best effort per
    /// ADR-021), not on a Grid or Border.
    /// </summary>
    [TestMethod]
    [Description("FR-UI-05/06; FR-HIS-01 (independent review L1/L3/L6): Home binds the scan-only label, caption and attention filter; row names are on TextBlocks.")]
    public void HomeBindsItsReviewFixesAndRowNamesHaveAnAutomationPeer()
    {
        string source = DesktopSource("MainWindow.cs");
        string home = Between(source, "private StackPanel BuildHomePage()", DesktopWindowSource.EndOf("MainWindow.Home.cs"));
        // UI polish phase 2: the actions come from the presenter (scan-only "Scan again" when sources are off) and their names and
        // help text are bound, never hard-coded; info bars take their action name and Activity filter from the presenter.
        StringAssert.Contains(home, "HomeActionButton(home.Primary, isPrimary: true)");
        StringAssert.Contains(home, "if (home.Secondary is { } secondary) actions.Children.Add(HomeActionButton(secondary, isPrimary: false));");
        StringAssert.Contains(home, "AutomationProperties.SetName(button, action.AccessibleName);");
        StringAssert.Contains(home, "AutomationProperties.SetHelpText(button, action.HelpText);");
        StringAssert.Contains(home, "AutomationProperties.SetName(open, notice.ActionAccessibleName);");
        StringAssert.Contains(home, "ActivityFilter filter = notice.ActivityFilter;");
        Assert.IsFalse(home.Contains("activityFilter = ActivityFilter.", StringComparison.Ordinal), "every Activity filter Home opens comes from the presenter");
        // Names sit on elements with an automation peer (a Button or HyperlinkButton), never on a Grid or Border.
        Assert.IsFalse(home.Contains("AutomationProperties.SetName(card,", StringComparison.Ordinal) || home.Contains("AutomationProperties.SetName(copy,", StringComparison.Ordinal), "a Grid has no automation peer");
        string row = Between(source, "private static Border ActivityEntryRow(ActivityEntry entry)", "private void AddReviewedUpdateOutcomesSection(");
        StringAssert.Contains(row, "AutomationProperties.SetName(title, entry.AccessibleName);");
        Assert.IsFalse(row.Contains("AutomationProperties.SetName(row,", StringComparison.Ordinal), "a Border has no automation peer");

        string homePresenter = ApplicationSource("HomePresentation.cs");
        StringAssert.Contains(homePresenter, "secondary = new(HomeActionKind.ScanAgain, \"Scan again\"");
        StringAssert.Contains(homePresenter, "public static readonly TimeSpan LiveWindow = TimeSpan.FromMinutes(15);");
        string activity = ApplicationSource("ActivityPresentation.cs");
        StringAssert.Contains(activity, "VersionComparison.Compare(new VersionComparisonRequest(installedVersion, job.TargetVersion, AllowPrereleaseCandidate: true))", "resolution uses the domain comparer");
        Assert.IsFalse(activity.Contains("string.Equals(installedVersion, job.TargetVersion, StringComparison.Ordinal)", StringComparison.Ordinal), "no string-equality version check");
    }

    [TestMethod]
    [Description("FR-DAT-06/08/12/13/14; AC-UX-07/08; SPEC §17.3 (Task 73 Phase 4): the three exact labels stay, each Data & reset button runs its existing operation and confirmation, and every control is disabled during active work.")]
    public void DataAndResetKeepsTheExactLabelsAndExistingOperations()
    {
        string source = DesktopSource("MainWindow.cs");
        string presenter = ApplicationSource("DataResetPresentation.cs");
        StringAssert.Contains(presenter, "public const string ClearScanDataLabel = \"Clear scan data\";");
        StringAssert.Contains(presenter, "public const string ClearDownloadedFilesLabel = \"Clear downloaded files\";");
        StringAssert.Contains(presenter, "public const string FactoryResetLabel = \"Factory reset PC Steward\";");
        StringAssert.Contains(presenter, "public const string FactoryResetConfirmationPhrase = \"RESET\";");
        StringAssert.Contains(presenter, "ProviderCacheBoundary");

        string section = Between(source, "private void AddDataAndResetSection(StackPanel panel)", "private async Task ConfirmAndArmFactoryResetAsync(Button invoker)");
        StringAssert.Contains(section, "DataResetPresenter.Build(DataResetInputsNow())");
        StringAssert.Contains(section, "page.ProviderCacheBoundary");
        StringAssert.Contains(section, "StewardVisuals.EvidenceRow(\"Deletes\", card.Deletes)");
        StringAssert.Contains(section, "StewardVisuals.EvidenceRow(\"Keeps\", card.Keeps)");
        StringAssert.Contains(section, "Button button = new() { Content = action.Label, IsEnabled = action.IsEnabled };");
        StringAssert.Contains(section, "case DataAction.ClearScanData: button.Click += async (_, _) => await ConfirmAndClearScanDataAsync(); break;");
        StringAssert.Contains(section, "case DataAction.ClearDownloadedFiles: button.Click += async (_, _) => await ConfirmAndClearDownloadedFilesAsync(\"Clear downloaded files?\"); break;");
        foreach (string clear in new[] { "workflow.ClearSourceMetadataAsync(CancellationToken.None)", "FactoryReset.ClearLogsAsync(CancellationToken.None)", "workflow.ClearUpdateHistoryAsync(CancellationToken.None)", "workflow.ClearMappingsAndPoliciesAsync(CancellationToken.None)" })
            StringAssert.Contains(section, clear);
        // Each advanced clear keeps its own confirmation (Cancel is the default) and runs inside the data-operation lease.
        StringAssert.Contains(section, "PrimaryButtonText = label, CloseButtonText = \"Cancel\", DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(section, "await DataOperationLease.RunAsync(_ => clear(), CancellationToken.None)");
        // Factory reset sits in its own danger-zone card after the routine cards, not beside routine cleanup.
        int routine = section.IndexOf("foreach (DataCardPresentation card in page.Cards)", StringComparison.Ordinal);
        int danger = section.IndexOf("reset.Heading, \"Separate from routine cleanup. This cannot be undone.\", danger, danger: true)", StringComparison.Ordinal);
        Assert.IsTrue(routine >= 0 && danger > routine, "the danger zone is a separate card after routine cleanup");
        StringAssert.Contains(section, "danger: true");
        StringAssert.Contains(section, "Button factoryReset = new() { Content = reset.Label, IsEnabled = reset.IsEnabled");
        StringAssert.Contains(section, "factoryReset.Click += async (_, _) => await ConfirmAndArmFactoryResetAsync(factoryReset);");
        Assert.AreEqual(1, Count(source, "await ConfirmAndArmFactoryResetAsync("), "one factory reset entry point");
        // Disabled during any active work (FR-DAT-14).
        StringAssert.Contains(source, "activeDecisions.Count + manualMappings.Count, HasActiveWork: HasActiveOperation);");

        // The existing confirmation dialogs keep their exact labels and Cancel default.
        string clearScan = Between(source, "private async Task ConfirmAndClearScanDataAsync()", "private async Task ConfirmAndClearDownloadedFilesAsync(");
        StringAssert.Contains(clearScan, "Title = \"Clear scan data?\"");
        StringAssert.Contains(clearScan, "PrimaryButtonText = \"Clear scan data\", CloseButtonText = \"Cancel\", DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(clearScan, "DataOperationLease.RunAsync(token => workflow.ClearScanDataAsync(token), CancellationToken.None)");
        string clearDownloads = Between(source, "private async Task ConfirmAndClearDownloadedFilesAsync(", DesktopWindowSource.EndOf("MainWindow.DataAndReset.cs"));
        StringAssert.Contains(clearDownloads, "PrimaryButtonText = \"Delete downloaded files\", CloseButtonText = \"Cancel\", DefaultButton = ContentDialogButton.Close");
        StringAssert.Contains(clearDownloads, "DataOperationLease.RunAsync(token => downloads.ClearInactiveAsync(token), CancellationToken.None)");
        StringAssert.Contains(clearDownloads, "Windows, WinGet, Microsoft Store, or other provider caches");
    }

    [TestMethod]
    [Description("FR-DAT-12/13; AC-UX-08 (Task 73 Phase 4): factory reset keeps two confirmations with Cancel as the default, the exact case-sensitive RESET, no Enter activation, the active-work lockout and arm-then-close for next-launch verification.")]
    public void FactoryResetKeepsTheTwoStepRESETFlow()
    {
        string source = DesktopSource("MainWindow.cs");
        string reset = Between(source, "private async Task ConfirmAndArmFactoryResetAsync(Button invoker)", "private async Task SaveExportAsync(");
        int scope = reset.IndexOf("Title = \"Review factory reset scope\"", StringComparison.Ordinal);
        int final = reset.IndexOf("Title = \"This cannot be undone\"", StringComparison.Ordinal);
        int lockout = reset.IndexOf("if (viewModel.IsScanning || isExecuting || isClearing || downloads.IsCleaning)", StringComparison.Ordinal);
        int arm = reset.IndexOf("DataOperationLease.RunSynchronous(() => FactoryReset.Arm()); Close();", StringComparison.Ordinal);
        Assert.IsTrue(scope >= 0 && final > scope && lockout > final && arm > lockout, "scope, then RESET, then lockout, then arm and close");
        Assert.AreEqual(2, Count(reset, "DefaultButton = ContentDialogButton.Close"), "Cancel is the default on both confirmations");
        StringAssert.Contains(reset, "PrimaryButtonText = \"Factory reset PC Steward\", CloseButtonText = \"Cancel\", DefaultButton = ContentDialogButton.Close, IsPrimaryButtonEnabled = false");
        StringAssert.Contains(reset, "final.IsPrimaryButtonEnabled = string.Equals(phrase.Text, \"RESET\", StringComparison.Ordinal);");
        StringAssert.Contains(reset, "TextBox phrase = new() { Header = \"Type RESET to continue\", PlaceholderText = \"RESET\" };");
        foreach (string enter in new[] { "KeyboardAccelerator", "VirtualKey.Enter", "KeyDown", "AcceptsReturn", "DefaultButton = ContentDialogButton.Primary" })
            Assert.IsFalse(reset.Contains(enter, StringComparison.Ordinal), $"no Enter activation: {enter}");
        Assert.AreEqual(1, Count(source, "FactoryReset.Arm()"), "factory reset is armed in one place");
        StringAssert.Contains(ApplicationSource("DataResetPresentation.cs"), "finishes the cleanup the next time it starts and checks it before first-run setup");
    }

    [TestMethod]
    [Description("Audit P-M7 (owner decision); FR-DAT-01/03; FR-HIS-05 (Task 73 Phase 4 independent review): Settings apply immediately except the Schedule card, which has exactly one Save schedule button; saving fails closed, distinguishes a Task Scheduler refusal from a busy gate and counts as active work; source consent stays separate; Copy diagnostic copies the existing sanitized bundle and flushes the clipboard.")]
    public void SettingsUseOneSaveModelAndCopyTheSanitizedDiagnostic()
    {
        string source = DesktopSource("MainWindow.cs");
        string settings = Between(source, "private StackPanel BuildSettingsPage()", DesktopWindowSource.EndOf("MainWindow.Settings.cs"));
        string scheduleCard = Between(settings, "ScheduleCardPresentation plan = SettingsPresenter.Schedule(", "StackPanel decisions = new()");
        // Exactly one Save button in Settings, on the Schedule card; every other setting saves immediately.
        Assert.IsFalse(settings.Contains("Content = \"Save", StringComparison.Ordinal), "no other Save button");
        Assert.AreEqual(1, Count(settings, "SettingsPresenter.SaveScheduleLabel"), "exactly one Save button in Settings");
        StringAssert.Contains(scheduleCard, "Button saveSchedule = StewardVisuals.PrimaryButton(SettingsPresenter.SaveScheduleLabel);");
        StringAssert.Contains(scheduleCard, "saveSchedule.Click += async (_, _) => await SaveScheduleAsync();");
        StringAssert.Contains(scheduleCard, "saveSchedule.IsEnabled = plan.IsSaveEnabled;");
        StringAssert.Contains(scheduleCard, "SettingsPresenter.ScheduleDescription");
        StringAssert.Contains(ApplicationSource("SettingsPresentation.cs"), "Changes to the schedule apply when you select Save schedule.");
        StringAssert.Contains(ApplicationSource("SettingsPresentation.cs"), "public const SettingsSaveModel SaveModel = SettingsSaveModel.AppliesImmediatelyExceptSchedule;");
        foreach (string immediate in new[] { "cadence.SelectionChanged += async", "startTime.TimeChanged +=", "weeklyDay.SelectionChanged +=", "scheduleEnabled.Toggled +=", "notifications.Toggled +=" })
            Assert.IsFalse(scheduleCard.Contains(immediate, StringComparison.Ordinal), $"schedule controls do not save on change: {immediate}");
        // The cadence handler only changes which controls are available; it never saves.
        string cadenceHandler = Between(scheduleCard, "cadence.SelectionChanged += (_, _) =>", "saveSchedule.Click +=");
        StringAssert.Contains(cadenceHandler, "SettingsPresenter.ScheduleControlAvailability(cadence.SelectedIndex, busy: false)");
        foreach (string save in new[] { "SaveScheduleAsync", "ApplyScheduleAsync", "Render()" })
            Assert.IsFalse(cadenceHandler.Contains(save, StringComparison.Ordinal), $"the cadence handler never saves or re-renders: {save}");
        StringAssert.Contains(settings, "CreatePagePanel()"); // UI polish Phase 1: one left-aligned page column for every page.
        // Fail closed (fix 3): no cadence or weekday selection shows a message and saves nothing; the weekday is set by index.
        StringAssert.Contains(scheduleCard, "SettingsPresenter.RequestedSchedule(cadence.SelectedIndex, scheduleEnabled.IsOn, notifications.IsOn, weeklyDay.SelectedIndex, startTime.Time)");
        StringAssert.Contains(scheduleCard, "if (requested is null) { scheduleSummary.Text = SettingsPresenter.ScheduleNotChosen; return; }");
        StringAssert.Contains(scheduleCard, "weeklyDay.SelectedIndex = (int)plan.WeeklyDay;");
        Assert.IsFalse(scheduleCard.Contains("SelectedItem as DayOfWeek", StringComparison.Ordinal));
        StringAssert.Contains(settings, "IsEnabled = plan.IsTimeEnabled");
        StringAssert.Contains(settings, "IsEnabled = plan.IsWeekdayEnabled");
        StringAssert.Contains(settings, "IsEnabled = plan.IsEnableToggleEnabled");
        // Handlers are attached after the saved values are set, so rendering the page never saves anything.
        Assert.IsTrue(settings.IndexOf("ToggleSwitch notifications = new()", StringComparison.Ordinal) < settings.IndexOf("saveSchedule.Click += async", StringComparison.Ordinal));
        // Fix 5: registration and save failures are handled inside the lease with distinct outcomes; only the gate reaches "Action unavailable".
        string apply = Between(settings, "private async Task ApplyScheduleAsync(ScheduledCheckSettings requested)", "private void CopyDiagnostic(");
        StringAssert.Contains(apply, "ScheduleSaveOperation.SaveAsync(previous, requested, scheduleRegistration.ApplyAsync, scheduleSettingsStore.SaveScheduledCheckSettingsAsync, token)");
        StringAssert.Contains(apply, "if (outcome == ScheduleSaveOutcome.Saved) scheduledSettings = requested;");
        StringAssert.Contains(apply, "SettingsPresenter.ScheduleSaveResult(outcome)");
        StringAssert.Contains(apply, "catch (InvalidOperationException) { isSavingSchedule = false; Render(); await ShowDataOperationUnavailableAsync(); return; }");
        StringAssert.Contains(apply, "Title = \"Schedule was not changed\"");
        // Fix 6: saving the schedule is active work, so clear/reset and other actions are unavailable meanwhile.
        StringAssert.Contains(source, "|| preparingReview || isSavingSchedule;");
        StringAssert.Contains(settings, "SettingsPresenter.Schedule(scheduledSettings, scheduledStatus, isSavingSchedule, otherWorkActive: HasActiveOperation)");
        // Source consent keeps its own disclosure; turning it on starts nothing.
        StringAssert.Contains(settings, "winget.Toggled += async (_, _) => await ChangeWinGetTrustedSourcesAsync(winget.IsOn);");
        StringAssert.Contains(settings, "includeAppx.Toggled += async (_, _) => await ChangeConnectorAsync(appx.Name, includeAppx.IsOn);");
        foreach (string start in new[] { "StartScanAsync()", "CheckUpdateSourcesAsync()", "CheckForUpdatesAsync()" })
            Assert.IsFalse(settings.Contains(start, StringComparison.Ordinal), $"Settings starts nothing: {start}");
        // Exports keep the identifying-data warning (FR-DAT-01/03).
        StringAssert.Contains(settings, "SettingsPresenter.ExportWarning");
        StringAssert.Contains(settings, "await SaveExportAsync(false)");
        StringAssert.Contains(settings, "await SaveExportAsync(true)");
        StringAssert.Contains(ApplicationSource("SettingsPresentation.cs"), "Exports reveal installed software and potentially identifying machine metadata. Keep exported files private.");
        // Copy diagnostic uses the existing sanitized bundle (FR-HIS-05).
        string copy = Between(settings, "private void CopyDiagnostic(", "private DataResetInputs DataResetInputsNow()");
        StringAssert.Contains(copy, "package.SetText(SanitizedDiagnosticBundle.Create(updateHistory));");
        StringAssert.Contains(copy, "StewardVisuals.SetLiveText(confirmation, SettingsPresenter.DiagnosticCopied(updateHistory.Count), announce: true);");
        AssertFlushedAfterSetContent(copy);
        StringAssert.Contains(settings, "NavigateTo(ShellPage.Privacy)");
    }

    [TestMethod]
    [Description("Audit P-L5 (Task 73 Phase 4): the startup busy, reset-recovery and failure windows share one style, make their diagnostic selectable and offer Copy details and Close, without changing start-up decisions.")]
    public void StartupNoticesHaveSelectableDetailsCopyAndClose()
    {
        string app = DesktopSource("App.xaml.cs");
        string notice = app[app.IndexOf("internal static class StartupNotice", StringComparison.Ordinal)..];
        StringAssert.Contains(notice, "IsTextSelectionEnabled = true");
        StringAssert.Contains(notice, "StewardVisuals.ActionButton(StartupNoticeText.CopyDetailsLabel, \"to the clipboard\")");
        StringAssert.Contains(app, "StewardVisuals.PrimaryButton(StartupNoticeText.CloseLabel, \"PC Steward\")");
        StringAssert.Contains(notice, "close.Click += (_, _) => window.Close();");
        Assert.AreEqual(3, Count(app, "Content = StartupNotice.Build(this,"), "all three windows use the shared style");
        StringAssert.Contains(app, "StartupNoticeText.ExceptionDetails(exception.GetType().Name, exception.Message)");
        StringAssert.Contains(app, "StartupNoticeText.ResidualDetails(result.Residuals.Select(item => (item.Category, item.Reason, item.Detail)))");
        // Independent review fix 2: both diagnostics are sanitized before they are shown or copied.
        string text = ApplicationSource("StartupNoticePresentation.cs");
        StringAssert.Contains(text, "return SanitizedDiagnosticBundle.Sanitize(text);");
        StringAssert.Contains(text, "$\"{Safe(typeName, userProfile)}: {Safe(message, userProfile)}\"");
        StringAssert.Contains(text, "string detail = Safe(item.Detail, userProfile);");
        // Independent review fix 1: the copied details survive closing the window and the app.
        AssertFlushedAfterSetContent(notice);
        Assert.IsFalse(notice.Contains("FactoryReset.", StringComparison.Ordinal), "the notice never resets or retries");
    }

    private static void AssertFlushedAfterSetContent(string section)
    {
        int set = section.IndexOf("Clipboard.SetContent(package);", StringComparison.Ordinal);
        int flush = section.IndexOf("Clipboard.Flush();", StringComparison.Ordinal);
        int fallback = section.IndexOf("catch (Exception exception) when (exception is not OutOfMemoryException)", StringComparison.Ordinal);
        Assert.IsTrue(set >= 0 && flush > set && fallback > flush, "Clipboard.Flush() follows SetContent inside the same try");
    }

    private static string DesktopSource(string relativePath) => DesktopWindowSource.ReadDesktopFile(FindRepositoryRoot(), relativePath);

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
