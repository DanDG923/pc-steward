using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// Task 73 Phase 5 (ADR-030 decision 4; concept study §2 foundations 9–10; audit §4, §5, P-M2/P-M3/P-M9/P-M10): source contracts for
/// the accessibility and copy pass. AC-UX-01–09 are best effort under ADR-021; these tests pin what can be checked without a
/// Windows desktop. Narrator, UIA notifications, F6/access-key behaviour and text scaling still need an owner-host walkthrough.
/// </summary>
[TestClass]
public sealed partial class UiAccessibilityContractTests
{
    private static readonly string[] DesktopFiles = ["MainWindow.cs", "StewardPresentation.cs", "ReviewedRouteSurfaces.cs", "App.xaml.cs"];
    private static readonly string[] UserFacingApplicationFiles = ["StatusVocabulary.cs", "ElevationStory.cs", "ShellNavigation.cs", "PageFocusRestore.cs", "AccessibilityText.cs", "ChocolateyReviewDisclosure.cs", "ReviewedRouteViewModels.cs", "RunningApplicationDetection.cs", "InventoryExperienceViewModel.cs"];

    [TestMethod]
    [Description("AC-UX-01 (audit §5): page titles are level-1 headings and section headings go through the level-2/3 heading helper.")]
    public void PageTitlesAreLevelOneHeadingsAndSectionsUseTheHeadingHelper()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        string pageTitle = Between(presentation, "public static TextBlock PageTitle(string text)", "public static TextBlock SectionHeading(");
        StringAssert.Contains(pageTitle, "Style(\"StewardPageTitleStyle\")");
        StringAssert.Contains(pageTitle, "AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);");
        string sectionHeading = Between(presentation, "public static TextBlock SectionHeading(", "public static T Named<T>(");
        StringAssert.Contains(sectionHeading, "AutomationHeadingLevel level = AutomationHeadingLevel.Level2");
        StringAssert.Contains(sectionHeading, "AutomationProperties.SetHeadingLevel(heading, level);");
        StringAssert.Contains(Between(presentation, "public static Grid PageHeader(", "private const double NarrowHeaderWidth"), "copy.Children.Add(PageTitle(title));");
        Assert.IsFalse(Between(presentation, "public static Grid PageHeader(", "private const double NarrowHeaderWidth").Contains("ToUpperInvariant()", StringComparison.Ordinal), "eyebrows are sentence case, not forced capitals");

        // The title and section styles are applied only inside the two helpers, so no title or section heading lacks its level.
        foreach (string file in DesktopFiles)
        {
            string source = DesktopSource(file);
            int expectedPage = file == "StewardPresentation.cs" ? 1 : 0;
            Assert.AreEqual(expectedPage, Count(source, "\"StewardPageTitleStyle\""), $"{file}: page titles use StewardVisuals.PageTitle");
            Assert.AreEqual(expectedPage, Count(source, "\"StewardSectionTitleStyle\""), $"{file}: section headings use StewardVisuals.SectionHeading");
        }

        // Every page builder shows a level-1 title (through PageHeader or PageTitle), including its empty states.
        string window = DesktopSource("MainWindow.cs");
        foreach (Match builder in PageBuilder().Matches(window))
        {
            string name = builder.Groups["name"].Value;
            int start = builder.Index;
            int end = window.IndexOf("\n    private ", start + builder.Length, StringComparison.Ordinal);
            string body = window[start..(end < 0 ? window.Length : end)];
            Assert.IsTrue(body.Contains("PageHeader(", StringComparison.Ordinal) || body.Contains("PageTitle(", StringComparison.Ordinal), $"{name} shows a level-1 page title");
        }
        StringAssert.Contains(Between(window, "private StackPanel BuildUpdateJobPage()", "private static Grid BuildJobStepIndicator("), "StewardVisuals.PageTitle(\"Update\")");
        StringAssert.Contains(Between(window, "private StackPanel BuildProductDetailPage()", "private static TextBlock SectionHeading("), "StewardVisuals.PageTitle(\"App details\")");
        StringAssert.Contains(DesktopSource("App.xaml.cs"), "title.Children.Add(StewardVisuals.PageTitle(heading));");
        StringAssert.Contains(Between(presentation, "public static Border EmptyState(", "public static Border MetricCard("), "TextBlock heading = SectionHeading(title);");
    }

    [TestMethod]
    [Description("AC-UX-09 (research §2 item 10): completion paths raise a UIA notification; the band's live region speaks only on state changes.")]
    public void CompletionPathsRaiseNotificationsAndTheLiveRegionSpeaksOnlyOnStateChanges()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        string announce = Between(presentation, "public static void Announce(", "public static void SetLiveText(");
        StringAssert.Contains(announce, "RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, message, activityId)");
        string live = Between(presentation, "public static void SetLiveText(", "public static TextBlock SelectableText(");
        StringAssert.Contains(live, "if (string.Equals(target.Text, text, StringComparison.Ordinal)) return;");
        StringAssert.Contains(live, "if (!announce) return;");
        StringAssert.Contains(live, "RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)");

        string source = DesktopSource("MainWindow.cs");
        StringAssert.Contains(Between(source, "private void AnnounceCompletion(", "private StackPanel BuildOnboardingPage()"), "StewardVisuals.Announce(operationBand is { Visibility: Visibility.Visible } ? operationStatusText");
        (string Start, string End, string Call)[] completions =
        [
            ("private async Task StartScanAsync()", "private async Task CheckUpdateSourcesAsync()", "AnnounceCompletion(OperationAnnouncements.ScanFinished("),
            ("private async Task CheckUpdateSourcesAsync()", "private void AddDataAndResetSection", "AnnounceCompletion(OperationAnnouncements.UpdateCheckFinished("),
            ("private async Task ReviewAndExecuteAsync(InventoryRow row)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync", "AnnounceCompletion(OperationAnnouncements.ReviewReady(row.Name), \"review-ready\");"),
            ("private async Task ReviewAndExecuteAsync(InventoryRow row)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync", "AnnounceCompletion(OperationAnnouncements.JobFinished(row.Name, job.Completion.Headline), \"update-job-finished\");"),
            ("private async Task ApplyScheduleAsync(", "private void CopyDiagnostic(", "AnnounceCompletion("),
            ("private async Task ConfirmAndClearScanDataAsync()", "private async Task ConfirmAndClearDownloadedFilesAsync(", "AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), \"data-cleared\");"),
            ("private async Task ConfirmAndClearDownloadedFilesAsync(", "private StackPanel BuildProductDetailPage()", "AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), \"data-cleared\");"),
            ("private void WireAdvancedClear(", "private async Task ConfirmAndArmFactoryResetAsync(", "AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), \"data-cleared\");")
        ];
        foreach ((string start, string end, string call) in completions) StringAssert.Contains(Between(source, start, end), call, start);

        // The job outcome is announced only after the recorded result exists (never before execution finishes).
        string execute = Between(source, "private async Task ReviewAndExecuteAsync(InventoryRow row)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");
        Assert.IsTrue(execute.IndexOf("job.Completion = UpdateOutcomePresenter.ForResult(", StringComparison.Ordinal) < execute.IndexOf("OperationAnnouncements.JobFinished(", StringComparison.Ordinal));
        Assert.IsTrue(execute.IndexOf("updates.ConfirmAndRunAsync(", StringComparison.Ordinal) < execute.IndexOf("OperationAnnouncements.JobFinished(", StringComparison.Ordinal));

        string visuals = Between(source, "private void RefreshOperationVisuals()", "private void AnnounceCompletion(");
        StringAssert.Contains(visuals, "string state = OperationAnnouncements.StateKey(label);");
        StringAssert.Contains(visuals, "StewardVisuals.SetLiveText(operationStatusText, label, announce: stateChanged && operationBand?.Visibility == Visibility.Visible);");
        Assert.AreEqual(0, Count(source, "operationStatusText.Text ="), "the band text changes only through the live-region helper");
        StringAssert.Contains(source, "AutomationProperties.SetLiveSetting(operationStatusText, AutomationLiveSetting.Polite);");
        StringAssert.Contains(source, "StewardVisuals.SetLiveText(confirmation, SettingsPresenter.DiagnosticCopied(updateHistory.Count), announce: true);");
        Assert.IsFalse(source.Contains("AutomationProperties.SetName(jobProgressStateText", StringComparison.Ordinal), "the job state live region is read by its text");
    }

    [TestMethod]
    [Description("AC-UX-01; WCAG 2.5.3: every statically known button name starts with its visible label, through the LabelInName helper.")]
    public void ButtonAndLinkNamesStartWithTheirVisibleLabel()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        StringAssert.Contains(presentation, "AutomationProperties.SetName(element, AccessibilityText.LabelInName(visibleLabel, detail));");
        StringAssert.Contains(presentation, "public static Button PrimaryButton(string label, string? detail = null) =>");
        StringAssert.Contains(presentation, "public static Button ActionButton(string label, string? detail = null) => Named(new Button { Content = label }, label, detail);");
        Assert.IsFalse(presentation.Contains("string automationName)", StringComparison.Ordinal), "no helper accepts a free-form name that can drop the label");

        List<string> violations = [];
        foreach (string file in DesktopFiles)
        {
            string source = DesktopSource(file);
            Dictionary<string, string> labels = new(StringComparer.Ordinal);
            foreach (Match declaration in LiteralContentDeclaration().Matches(source)) labels[declaration.Groups["id"].Value] = declaration.Groups["label"].Value;
            foreach (Match declaration in HelperButtonDeclaration().Matches(source)) labels[declaration.Groups["id"].Value] = declaration.Groups["label"].Value;
            foreach (Match name in LiteralSetName().Matches(source))
            {
                if (!labels.TryGetValue(name.Groups["id"].Value, out string? label)) continue;
                string literal = name.Groups["name"].Value;
                string prefix = name.Groups["interpolated"].Success ? literal.Split('{')[0] : literal;
                string spoken = label.TrimEnd('…').TrimEnd();
                if (!prefix.StartsWith(spoken, StringComparison.Ordinal))
                    violations.Add($"{file}: {name.Groups["id"].Value} shows \"{label}\" but is named \"{literal}\"");
            }
            // A literal detail passed to the helpers must not repeat or replace the label (it is appended after it).
            foreach (Match helper in HelperButtonDeclaration().Matches(source))
                if (helper.Groups["detail"].Success && helper.Groups["detail"].Value.StartsWith(helper.Groups["label"].Value, StringComparison.Ordinal))
                    violations.Add($"{file}: {helper.Groups["id"].Value} repeats its label in the detail");
        }
        Assert.AreEqual(0, violations.Count, string.Join(Environment.NewLine, violations));

        // The audit's named mismatches are gone (audit §5): "Scan this PC", "Review verified update evidence", "Show … scan results".
        string window = DesktopSource("MainWindow.cs");
        foreach (string legacy in new[] { "\"Scan this PC\"", "Review verified update evidence", "$\"Show {label} scan results\"", "Start another local inventory scan", "Cancel the active local inventory scan" })
            Assert.IsFalse(window.Contains(legacy, StringComparison.Ordinal), legacy);
    }

    [TestMethod]
    [Description("AC-UX-01/09; FR-UPD-01: F6 regions, Ctrl+F, a guarded F5 and access keys; no shortcut can confirm, approve or skip a confirmation.")]
    public void KeyboardShortcutsAreGuardedAndNeverConfirm()
    {
        string source = DesktopSource("MainWindow.cs");
        string shortcuts = Between(source, "private void AddWindowShortcuts(UIElement root)", "private void OnShellPointerPressed(");
        StringAssert.Contains(shortcuts, "Key = Windows.System.VirtualKey.F6 }");
        StringAssert.Contains(shortcuts, "Key = Windows.System.VirtualKey.F6, Modifiers = Windows.System.VirtualKeyModifiers.Shift");
        StringAssert.Contains(shortcuts, "Key = Windows.System.VirtualKey.F, Modifiers = Windows.System.VirtualKeyModifiers.Control");
        StringAssert.Contains(shortcuts, "Key = Windows.System.VirtualKey.F5 }");
        StringAssert.Contains(shortcuts, "scan.Invoked += async (_, args) => { args.Handled = true; await ScanShortcutAsync(); };");
        StringAssert.Contains(shortcuts, "FocusRegionCycle.Next(current, available, backwards)");
        string scanShortcut = Between(shortcuts, "private async Task ScanShortcutAsync()", "private bool IsPopupOpen()");
        StringAssert.Contains(scanShortcut, "if (!ShortcutGuard.CanStartScan(shellShown: navigationView is not null, HasActiveOperation, reviewOpen: reviewDecision is not null, dialogOpen: IsPopupOpen())) return;");
        StringAssert.Contains(scanShortcut, "await CheckForUpdatesAsync();");
        foreach (string forbidden in new[] { "ConfirmAndRunAsync", "ReviewAndExecuteAsync", "TrySetResult", "ConfirmAndArmFactoryResetAsync", "ConfirmAndClear", "WireAdvancedClear", "ChangeWinGetTrustedSourcesAsync", "SaveScheduleAsync" })
            Assert.IsFalse(shortcuts.Contains(forbidden, StringComparison.Ordinal), $"no shortcut calls {forbidden}");
        StringAssert.Contains(source, "BuildWindowChrome()");
        StringAssert.Contains(Between(source, "private void BuildWindowChrome()", "private void AddWindowShortcuts("), "AddWindowShortcuts(windowRoot);");
        // CheckForUpdatesAsync and StartScanAsync keep their own active-work guard, so F5 cannot start a second operation either way.
        StringAssert.Contains(Between(source, "private async Task CheckForUpdatesAsync()", "private async Task StartScanAsync()"), "if (HasActiveOperation) { Render(); return; }");
        StringAssert.Contains(Between(source, "private async Task StartScanAsync()", "private async Task CheckUpdateSourcesAsync()"), "if (HasActiveOperation) { Render(); return; }");

        // Ctrl+F targets: the Installed apps search box and the Activity filters; the target is reset for every page.
        StringAssert.Contains(Between(source, "private StackPanel BuildInstalledAppsPage()", "private static (string Raw, string Label)[] LabelledFilterOptions"), "pageSearchTarget = search;");
        StringAssert.Contains(Between(source, "private StackPanel BuildActivityPage()", "private static Border ActivityEntryRow("), "pageSearchTarget = filter;");
        StringAssert.Contains(Between(source, "private void ShowCurrentPage(bool preserveViewState)", "private void RestoreScrollOffset("), "pageSearchTarget = null;");

        // Access keys on the primary commands; the review's confirm button has none and no accelerator.
        StringAssert.Contains(Between(source, "private void BuildWindowChrome()", "private void AddWindowShortcuts("), "titleBarBack.AccessKey = \"B\";");
        string home = Between(source, "private StackPanel BuildHomePage()", "private Button HomeTileButton(");
        StringAssert.Contains(home, "review.AccessKey = \"R\";");
        StringAssert.Contains(home, "AccessKey = AccessKeyFor(home.SecondaryLabel)");
        StringAssert.Contains(Between(source, "private StackPanel BuildInstalledAppsPage()", "private static (string Raw, string Label)[] LabelledFilterOptions"), "scan.AccessKey = \"S\";");
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "review.AccessKey = \"R\";");
        StringAssert.Contains(Between(source, "private StackPanel BuildSettingsPage()", "private async Task ApplyScheduleAsync("), "saveSchedule.AccessKey = \"S\";");
        string reviewStep = Between(source, "private void AddReviewStep(", "private void AddUpdateStep(");
        Assert.IsFalse(reviewStep.Contains("AccessKey", StringComparison.Ordinal), "the update confirmation has no access key");
        Assert.IsFalse(reviewStep.Contains("KeyboardAccelerator", StringComparison.Ordinal), "the update confirmation has no accelerator");
        // The Updates page review button only opens the review sheet; its confirmation happens on that sheet.
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "await ReviewAndExecuteAsync(row)");
        Assert.AreEqual(1, Count(source, "TrySetResult(true)"), "still exactly one confirmation path");
    }

    [TestMethod]
    [Description("AC-UX-01 (audit §5): package IDs, versions, evidence and technical details can be selected and copied.")]
    public void EvidenceAndTechnicalDetailAreSelectable()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        StringAssert.Contains(Between(presentation, "public static Grid EvidenceRow(", "public static Border SettingsCard("), "TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };");
        StringAssert.Contains(Between(presentation, "public static TextBlock SelectableText(", "public static FrameworkElement MessageWithDetails("), "IsTextSelectionEnabled = true");
        StringAssert.Contains(Between(presentation, "public static FrameworkElement MessageWithDetails(", "public static Border StatusBadge("), "Header = \"Technical details\", Content = SelectableText(technicalDetail)");

        string source = DesktopSource("MainWindow.cs");
        StringAssert.Contains(Between(source, "private void AddVerifyStep(", "private StackPanel BuildSettingsPage()"), "Header = \"Technical details\", Content = new TextBlock { Text = outcome.TechnicalDetail, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }");
        StringAssert.Contains(Between(source, "private StackPanel BuildRepositoryAppsPage()", "private StackPanel BuildResultsPage()"), "StewardVisuals.SelectableText($\"Source: {detail.SourceId} · Package ID: {detail.PackageId}\")");
        StringAssert.Contains(Between(source, "private StackPanel BuildReviewPage()", "private void AddReviewDecisionActions("), "StewardVisuals.SelectableText($\"{detail?.SourceId ?? StewardPresentation.Provider(row.Provider)} · {detail?.PackageId ?? \"package ID unknown\"}\")");
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true");
        StringAssert.Contains(Between(source, "private void AddReviewStep(", "private void AddUpdateStep("), "IsTextSelectionEnabled = true");
        // Blame-free errors keep the technical text (exception, residuals, reset marker) behind the expander.
        StringAssert.Contains(source, "StewardVisuals.MessageWithDetails(\"PC Steward couldn't start the factory reset. Nothing was deleted. Close PC Steward and try again.\", exception.Message)");
        StringAssert.Contains(source, "StewardVisuals.MessageWithDetails(result.Message, residuals)");
        Assert.IsFalse(source.Contains("Content = exception.Message", StringComparison.Ordinal), "no raw exception text as a dialog's whole message");
        StringAssert.Contains(DesktopSource("App.xaml.cs"), "IsTextSelectionEnabled = true");
    }

    [TestMethod]
    [Description("AC-UX-03/04 (audit §5, P-M9; WCAG 1.4.3/1.4.11): caption text ≥ 4.5:1 and strong borders ≥ 3:1 on their backgrounds in light and dark.")]
    public void CaptionTextAndBordersMeetContrastInLightAndDark()
    {
        Dictionary<string, Dictionary<string, string>> themes = ThemeColors();
        string[] backgrounds = ["StewardCanvasBrush", "StewardCanvasAltBrush", "StewardSurfaceBrush", "StewardSurfaceSubtleBrush", "StewardBrandSubtleBrush"];
        foreach (string theme in new[] { "Default", "Light", "Dark" })
        {
            Dictionary<string, string> colors = themes[theme];
            foreach (string background in backgrounds)
            {
                foreach (string text in new[] { "StewardTertiaryTextBrush", "StewardSecondaryTextBrush", "StewardTextBrush", "StewardBrandTextBrush" })
                {
                    double ratio = ContrastRatio(colors[text], colors[background]);
                    Assert.IsTrue(ratio >= 4.5, $"{theme}: {text} {colors[text]} on {background} {colors[background]} is {ratio:0.00}:1");
                }
            }
            foreach (string background in new[] { "StewardCanvasBrush", "StewardSurfaceBrush", "StewardSurfaceSubtleBrush" })
            {
                double ratio = ContrastRatio(colors["StewardBorderStrongBrush"], colors[background]);
                Assert.IsTrue(ratio >= 3.0, $"{theme}: StewardBorderStrongBrush on {background} is {ratio:0.00}:1");
            }
            foreach ((string foreground, string surface) in new[] { ("StewardSuccessBrush", "StewardSuccessSurfaceBrush"), ("StewardWarningBrush", "StewardWarningSurfaceBrush"), ("StewardDangerBrush", "StewardDangerSurfaceBrush") })
            {
                double ratio = ContrastRatio(colors[foreground], colors[surface]);
                Assert.IsTrue(ratio >= 4.5, $"{theme}: {foreground} on {surface} is {ratio:0.00}:1");
            }
        }
        // The audit's failing light tertiary value is gone, and the values documented in ColorTokens.xaml are the ones in use.
        Assert.AreEqual("#5B677B", themes["Light"]["StewardTertiaryTextBrush"]);
        Assert.AreEqual("#5B677B", themes["Default"]["StewardTertiaryTextBrush"]);
        Assert.AreEqual(5.28, Math.Round(ContrastRatio("#5B677B", "#F3F6FA"), 2));
        Assert.AreEqual(5.72, Math.Round(ContrastRatio("#5B677B", "#FFFFFF"), 2));
        Assert.AreEqual(4.13, Math.Round(ContrastRatio("#6B788D", "#F3F6FA"), 2), "the audit's computed ratio for the old value");
        // High contrast stays mapped to system colours.
        string tokens = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "Resources", "ColorTokens.xaml"));
        StringAssert.Contains(Between(tokens, "<ResourceDictionary x:Key=\"HighContrast\">", "</ResourceDictionary>"), "x:Key=\"StewardTertiaryTextBrush\" Color=\"{ThemeResource SystemColorWindowTextColor}\"");
    }

    [TestMethod]
    [Description("AC-UX-04 (audit P-M9): action and filter rows wrap, headers and settings rows reflow, and the window has a minimum size.")]
    public void ActionRowsWrapAndTheWindowHasAMinimumSize()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        StringAssert.Contains(presentation, "public sealed class StewardWrapPanel : Panel");
        StringAssert.Contains(presentation, "PlaceBesideOrBelow(grid, action, NarrowHeaderWidth, StewardSpacing.XLarge);");
        StringAssert.Contains(presentation, "PlaceBesideOrBelow(row, control, NarrowRowWidth, StewardSpacing.Large);");
        string source = DesktopSource("MainWindow.cs");
        StringAssert.Contains(source, "presenter.PreferredMinimumWidth = MinimumWindowWidth;");
        StringAssert.Contains(source, "presenter.PreferredMinimumHeight = MinimumWindowHeight;");
        StringAssert.Contains(source, "private const int MinimumWindowWidth = 500;");
        // The only horizontal StackPanels left are icon+text pairs (tile heading, title-bar brand, the shield button content).
        Assert.AreEqual(3, Count(source, "Orientation = Orientation.Horizontal"), "action rows use StewardVisuals.ActionRow()");
        Assert.IsFalse(source.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal), "filter rows wrap instead of scrolling sideways");
        Assert.IsTrue(Count(source, "StewardVisuals.ActionRow()") >= 12);
    }

    [TestMethod]
    [Description("P-M10, §17.5 (concept study §2 foundation 9): no developer or design jargon in user-facing Desktop or presenter strings, except listed pinned disclosures.")]
    public void UserFacingStringsHaveNoDeveloperJargon()
    {
        string[] jargon = ["quiet authority", "contextual view", "decision checkpoint", "canonical", "coordinator", "bounded raw", "raw diagnostic", "split token", "lib-bad", "app-container", "heuristic", "saturation", "indeterminate", "snapshot", "evidence before action"];
        // Pinned verbatim texts deliberately left unchanged (their exact wording is asserted by Task 50 and coverage tests).
        string[] pinned =
        [
            "(a split token)", // ChocolateyReviewDisclosure: Task 50 runtime-limit disclosure.
            "low-integrity or app-container token", // ChocolateyReviewDisclosure: Task 50 runtime-limit disclosure.
            "install of this package (lib-bad)", // ChocolateyReviewDisclosure: Task 50 lingering-installer disclosure.
            "exact-name heuristic", // RunningApplicationDetection.CoverageDisclosure: the running-app coverage disclosure.
            "Snapshot coverage counts are unavailable" // RunningApplicationDetection.CoverageDisclosure.
        ];
        string root = FindRepositoryRoot();
        IEnumerable<string> files = DesktopFiles.Select(file => Path.Combine(root, "src", "PCSteward.Desktop", file))
            .Concat(Directory.GetFiles(Path.Combine(root, "src", "PCSteward.Application"), "*Presentation.cs"))
            .Concat(UserFacingApplicationFiles.Select(file => Path.Combine(root, "src", "PCSteward.Application", file)));
        List<string> found = [];
        foreach (string file in files)
        {
            foreach (string line in File.ReadAllLines(file))
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (Match literal in StringLiteral().Matches(line))
                {
                    string text = literal.Value;
                    if (pinned.Any(allowed => text.Contains(allowed, StringComparison.Ordinal))) continue;
                    foreach (string term in jargon)
                        if (text.Contains(term, StringComparison.OrdinalIgnoreCase)) found.Add($"{Path.GetFileName(file)}: \"{term}\" in {text}");
                }
            }
        }
        Assert.AreEqual(0, found.Count, string.Join(Environment.NewLine, found));
        // Audit P-M3: nothing is called "approved" before approval on the review path, and one verb per action.
        string window = DesktopSource("MainWindow.cs");
        foreach (string legacy in new[] { "Approved updates", "Install approved update", "Verify all", "Review available actions", "Open evidence", "Review evidence", "Review details", "External release is blocked", "Do not treat an unsigned archive" })
            Assert.IsFalse(window.Contains(legacy, StringComparison.Ordinal), legacy);
        StringAssert.Contains(window, "Confirm all current package identities");
    }

    private static Dictionary<string, Dictionary<string, string>> ThemeColors()
    {
        XDocument tokens = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "Resources", "ColorTokens.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return tokens.Descendants(presentation + "ResourceDictionary")
            .Where(dictionary => dictionary.Attribute(xaml + "Key") is not null)
            .ToDictionary(
                dictionary => dictionary.Attribute(xaml + "Key")!.Value,
                dictionary => dictionary.Elements(presentation + "SolidColorBrush")
                    .Where(brush => brush.Attribute("Color")?.Value.StartsWith('#') == true)
                    .ToDictionary(brush => brush.Attribute(xaml + "Key")!.Value, brush => brush.Attribute("Color")!.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    /// <summary>WCAG 2.x contrast ratio from sRGB hex values (relative luminance, 0.03928 threshold).</summary>
    private static double ContrastRatio(string first, string second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        static double Channel(string value)
        {
            double c = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        string digits = hex.TrimStart('#');
        return 0.2126 * Channel(digits[0..2]) + 0.7152 * Channel(digits[2..4]) + 0.0722 * Channel(digits[4..6]);
    }

    [GeneratedRegex(@"private StackPanel (?<name>Build\w+Page)\(\)\s*(?:=>|\{)")]
    private static partial Regex PageBuilder();

    [GeneratedRegex(@"(?<id>\w+)\s*=\s*new(?:\s+\w+)?\(\)\s*\{\s*Content\s*=\s*""(?<label>[^""]+)""")]
    private static partial Regex LiteralContentDeclaration();

    [GeneratedRegex(@"(?<id>\w+)\s*=\s*StewardVisuals\.(?:PrimaryButton|ActionButton)\(""(?<label>[^""]+)""(?:,\s*\$?""(?<detail>[^""]*)"")?")]
    private static partial Regex HelperButtonDeclaration();

    [GeneratedRegex(@"AutomationProperties\.SetName\((?<id>\w+),\s*(?<interpolated>\$)?""(?<name>[^""]*)""")]
    private static partial Regex LiteralSetName();

    [GeneratedRegex(@"\$?@?""(?:[^""\\]|\\.)*""")]
    private static partial Regex StringLiteral();

    private static string DesktopSource(string relativePath) => File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", relativePath));

    private static string Between(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf(endMarker, start, StringComparison.Ordinal);
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
