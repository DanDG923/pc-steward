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
    private static readonly string[] PerRowActionNames =
    [
        "$\"Pin {row.Name} at installed version {row.Version}\"",
        "$\"Choose how long to ignore {row.Name}\"",
        "$\"Review {row.Name} decision evidence and options\"",
        "$\"Review {row.Name} evidence",
        "$\"Open {row.Name} evidence\"",
        "$\"Open unavailable source evidence for {row.Name}\"",
        "$\"Open repository evidence for {row.Name}\"",
        "$\"More options for {presentation.Name}\"",
        "$\"Choose {ready.Name} to review",
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
        StringAssert.Contains(page, "UpdatesPagePresenter.Build(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, activeDecisions, selectedReadyKey, DateTimeOffset.UtcNow)");
        string ready = Between(page, "foreach (UpdateRowPresentation ready in page.Ready)", "if (page.Ready.Count > 0)");
        string needsReview = Between(page, "foreach (UpdateRowPresentation needsReview in page.NeedsReview)", "// Ignored and skipped");
        string ignored = Between(page, "foreach (UpdateRowPresentation ignored in page.IgnoredAndSkipped)", "Expander ignoredGroup");
        StringAssert.Contains(ready, "RadioButton choose = new() { GroupName = \"ready-updates\", IsChecked = ready.StableProductKey == page.SelectedKey");
        Assert.AreEqual(1, Count(source, "new RadioButton") + Count(source, "RadioButton choose = new()"), "the only selection control is on Ready rows");
        foreach (string group in new[] { needsReview, ignored })
            foreach (string control in new[] { "RadioButton", "CheckBox", "ReviewAndExecuteAsync", "PrimaryButton", "IsEnabled = false" })
                Assert.IsFalse(group.Contains(control, StringComparison.Ordinal), $"no update control (not even a disabled one): {control}");
        StringAssert.Contains(needsReview, "needsReview.ReasonText");
        StringAssert.Contains(page, "IsExpanded = ignoredGroupExpanded");
        Assert.IsFalse(source.Contains("private bool ignoredGroupExpanded = true", StringComparison.Ordinal), "Ignored and skipped starts collapsed");
        Assert.AreEqual(1, Count(page, "ReviewAndExecuteAsync("), "only the review button opens a review");
        StringAssert.Contains(page, "review.IsEnabled = page.CanReview && !HasActiveOperation && reviewDecision is null");
        Assert.IsFalse(source.Contains("private string? selectedReadyKey =", StringComparison.Ordinal), "nothing is preselected");
        string presenter = ApplicationSource("UpdatesPagePresentation.cs");
        StringAssert.Contains(presenter, "case nameof(UpdateIntelligenceStatus.VerifiedUpdate):");
        StringAssert.Contains(presenter, "public bool HasUpdateControl => Group == UpdateGroup.Ready;");
        Assert.IsFalse(presenter.Contains("\"Skip", StringComparison.Ordinal) || presenter.Contains("SkipVersion", StringComparison.Ordinal), "no invented skip-version decision");

        string overflow = Between(source, "private Button UpdateRowOverflow(", "/// FR-UPD-01/04–09, FR-UI-03/04, FR-DAT-09, UJ-04");
        foreach (string operation in new[] { "PromptAndIgnoreProductAsync(row)", "PromptAndPinCurrentVersionAsync(row)", "ShowDetailsAsync(row)", "workflow.ReverseDecisionAsync(decisionId, token)" })
            StringAssert.Contains(overflow, operation);
        Assert.IsFalse(overflow.Contains("ReviewAndExecuteAsync", StringComparison.Ordinal));
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
            "if (HasActiveOperation || windowClosed || reviewDecision is not null) return;",
            "updates.BeginReviewAsync(row.StableProductKey, token)",
            "AdministratorRequirementPresenter.ForSelection(selection)",
            "UpdateReviewSheet.Create(selection,",
            "NavigateTo(ShellPage.UpdateJob);",
            "bool confirmed = await decision.Task;",
            "if (!confirmed || windowClosed)",
            "isExecuting = true;",
            "updates.ConfirmAndRunAsync(preparation.Review, preparation.RunningApplication.AppearsToBeRunning, token, preparation.Progress)"
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
        StringAssert.Contains(reviewStep, "Content = UpdateReviewSheet.CancelButtonLabel");
        StringAssert.Contains(reviewStep, "decision.TrySetResult(true);");
        Assert.AreEqual(1, Count(reviewStep, "decision.TrySetResult(true);"), "only the specific primary button confirms");
        Assert.IsFalse(reviewStep.Contains("KeyboardAccelerator", StringComparison.Ordinal), "no Enter accelerator on the update button");
        StringAssert.Contains(reviewStep, "foreach (ReviewFact fact in sheet.Facts)");
        StringAssert.Contains(reviewStep, "sheet.ConfidenceSummary");
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
        StringAssert.Contains(source, "|| downloads.IsCleaning || preparingReview;");
        int guard = review.IndexOf("if (HasActiveOperation || windowClosed || reviewDecision is not null) return;", StringComparison.Ordinal);
        int set = review.IndexOf("preparingReview = true;", StringComparison.Ordinal);
        int begin = review.IndexOf("updates.BeginReviewAsync(row.StableProductKey, token)", StringComparison.Ordinal);
        int reset = review.IndexOf("finally { preparingReview = false; }", StringComparison.Ordinal);
        int sheet = review.IndexOf("UpdateReviewSheet.Create(selection,", StringComparison.Ordinal);
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
        StringAssert.Contains(load, "selectedReadyKey = null;");
        string completion = review[review.IndexOf("finally { StopUpdateProgressPolling();", StringComparison.Ordinal)..];
        StringAssert.Contains(completion, "LoadSavedInventory();");
        StringAssert.Contains(Between(source, "private async Task ConfirmAndClearScanDataAsync()", "private async Task ConfirmAndClearDownloadedFilesAsync("), "LoadSavedInventory();");

        // Item 7: completion never navigates; elsewhere the band shows the result with "View result".
        string tail = review[review.IndexOf("job.Completion = UpdateOutcomePresenter.ForResult(result, selection, row.Name);", StringComparison.Ordinal)..];
        Assert.IsFalse(tail.Contains("NavigateTo(", StringComparison.Ordinal), "completion does not move the user");
        StringAssert.Contains(tail, "jobResultPending = navigation.Current != ShellPage.UpdateJob;");
        string visuals = Between(source, "private void RefreshOperationVisuals()", "private StackPanel BuildOnboardingPage()");
        StringAssert.Contains(visuals, "HasActiveOperation || ShowsJobResultInBand ? Visibility.Visible : Visibility.Collapsed");
        StringAssert.Contains(visuals, "string viewLabel = showResult ? \"View result\" : \"View update progress\";");
        StringAssert.Contains(source, "if (navigation.Current != ShellPage.UpdateJob) CancelPendingReview(); else jobResultPending = false;");
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
        string verify = Between(source, "private void AddVerifyStep(", "private StackPanel BuildSettingsPage()");
        StringAssert.Contains(verify, "foreach (UpdateJobItemOutcome item in completion.Items)");
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
