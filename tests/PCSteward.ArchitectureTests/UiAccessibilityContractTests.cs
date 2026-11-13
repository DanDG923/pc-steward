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
    private static readonly string[] DecorativeHues = ["Blue", "Teal", "Amber", "Purple", "Pink", "Green"];
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
        StringAssert.Contains(Between(window, "private StackPanel BuildProductDetailPage()", "private static Border DetailItemCard("), "StewardVisuals.PageTitle(\"App details\")");
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
        StringAssert.Contains(Between(source, "private void AnnounceCompletion(", "private static Style AppStyle("), "StewardVisuals.Announce(operationBand is { Visibility: Visibility.Visible } ? operationStatusText");
        (string Start, string End, string Call)[] completions =
        [
            ("private async Task StartScanAsync()", "private async Task CheckUpdateSourcesAsync()", "AnnounceCompletion(OperationAnnouncements.ScanFinished("),
            ("private async Task CheckUpdateSourcesAsync()", DesktopWindowSource.EndOf("MainWindow.Operations.cs"), "AnnounceCompletion(OperationAnnouncements.UpdateCheckFinished("),
            ("private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync", "AnnounceCompletion(OperationAnnouncements.ReviewReady(sheet.AppName), \"review-ready\");"),
            ("private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync", "AnnounceCompletion(OperationAnnouncements.JobFinished(sheet.AppName, job.Completion.Headline), \"update-job-finished\");"),
            ("private async Task ApplyScheduleAsync(", "private void CopyDiagnostic(", "AnnounceCompletion("),
            ("private async Task ConfirmAndClearScanDataAsync()", "private async Task ConfirmAndClearDownloadedFilesAsync(", "AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), \"data-cleared\");"),
            ("private async Task ConfirmAndClearDownloadedFilesAsync(", DesktopWindowSource.EndOf("MainWindow.DataAndReset.cs"), "AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), \"data-cleared\");"),
            ("private void WireAdvancedClear(", "private async Task ConfirmAndArmFactoryResetAsync(", "AnnounceCompletion(OperationAnnouncements.Completed(outcomeTitle), \"data-cleared\");")
        ];
        foreach ((string start, string end, string call) in completions) StringAssert.Contains(Between(source, start, end), call, start);

        // The job outcome is announced only after the recorded result exists (never before execution finishes).
        string execute = Between(source, "private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)", "private Task<ContentDialogResult> ShowUpdateOutcomeAsync");
        Assert.IsTrue(execute.IndexOf("job.Completion = run is not null", StringComparison.Ordinal) < execute.IndexOf("OperationAnnouncements.JobFinished(", StringComparison.Ordinal));
        Assert.IsTrue(execute.IndexOf("updates.ConfirmAndRunBatchAsync(", StringComparison.Ordinal) < execute.IndexOf("OperationAnnouncements.JobFinished(", StringComparison.Ordinal));

        string visuals = Between(source, "private void RefreshOperationVisuals()", "private void AnnounceCompletion(");
        StringAssert.Contains(visuals, "string state = OperationAnnouncements.StateKey(label);");
        StringAssert.Contains(visuals, "StewardVisuals.SetLiveText(operationStatusText, label, announce: stateChanged && BandAnnouncesState && operationBand?.Visibility == Visibility.Visible);");
        // Final review L5: no duplicate speech. The band is quiet while the job page narrates a running update, and a job result
        // shown in the band is spoken once, by the completion notification.
        StringAssert.Contains(source, "private bool BandAnnouncesState => !ShowsJobResultInBand && !(navigation.Current == ShellPage.UpdateJob && (isExecuting || executionPending));");
        StringAssert.Contains(visuals, "if (jobProgressStateText is not null && isExecuting) StewardVisuals.SetLiveText(jobProgressStateText,");
        StringAssert.Contains(execute, "AnnounceCompletion(OperationAnnouncements.JobFinished(sheet.AppName, job.Completion.Headline), \"update-job-finished\");");
        Assert.AreEqual(1, Count(source, "OperationAnnouncements.JobFinished("), "the job result is raised by one notification only");
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

        // The audit's named mismatches are gone (audit §5): "Review verified update evidence", "Show … scan results". "Scan this PC"
        // is a label again (UI review 2026-11-13, item 8), now with a name that starts with it, which the checks above enforce.
        string window = DesktopSource("MainWindow.cs");
        foreach (string legacy in new[] { "Review verified update evidence", "$\"Show {label} scan results\"", "Start another local inventory scan", "Cancel the active local inventory scan" })
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
        StringAssert.Contains(Between(source, "private StackPanel BuildActivityPage()", "private static FrameworkElement ActivityEntryRow("), "pageSearchTarget = filter;");
        StringAssert.Contains(Between(source, "private void ShowCurrentPage(bool preserveViewState)", "private void RestoreScrollOffset("), "pageSearchTarget = null;");

        // Access keys on the primary commands; the review's confirm button has none and no accelerator.
        StringAssert.Contains(Between(source, "private void BuildWindowChrome()", "private void AddWindowShortcuts("), "titleBarBack.AccessKey = \"B\";");
        // UI polish phase 2: every Home action has an access key from its label ("See updates" → S, "Turn on update checks…" → T).
        string home = Between(source, "private StackPanel BuildHomePage()", DesktopWindowSource.EndOf("MainWindow.Home.cs"));
        StringAssert.Contains(home, "AccessKey = AccessKeyFor(action.Label)");
        StringAssert.Contains(Between(source, "private StackPanel BuildInstalledAppsPage()", "private static (string Raw, string Label)[] LabelledFilterOptions"), "scan.AccessKey = \"S\";");
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "review.AccessKey = \"R\";");
        StringAssert.Contains(Between(source, "private StackPanel BuildSettingsPage()", "private async Task ApplyScheduleAsync("), "saveSchedule.AccessKey = \"S\";");
        string reviewStep = Between(source, "private void AddReviewStep(", "private void AddUpdateStep(");
        Assert.IsFalse(reviewStep.Contains("AccessKey", StringComparison.Ordinal), "the update confirmation has no access key");
        Assert.IsFalse(reviewStep.Contains("KeyboardAccelerator", StringComparison.Ordinal), "the update confirmation has no accelerator");
        // The Updates page review button only opens the review sheet; its confirmation happens on that sheet.
        // Task 73 Phase 6: both Updates buttons (selected apps, all ready apps) only open the review sheet for a frozen set.
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "await ReviewAndExecuteAsync(RowsFor(page.SelectedKeys))");
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "await ReviewAndExecuteAsync(RowsFor(page.ReadyKeys))");
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
        StringAssert.Contains(Between(source, "private void AddVerifyStep(", "private async Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?> ReadReviewedHistoryAsync("), "Header = \"Technical details\", Content = new TextBlock { Text = outcome.TechnicalDetail, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }");
        // UI review 2026-11-13 (item 14): the Repository apps and Review decisions pages are gone; package IDs are selectable on app details.
        StringAssert.Contains(Between(source, "private StackPanel BuildUpdatesPage()", "private Button UpdateRowOverflow("), "Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true");
        StringAssert.Contains(Between(source, "private void AddReviewStep(", "private void AddUpdateStep("), "IsTextSelectionEnabled = true");
        // Blame-free errors keep the technical text (exception, residuals, reset marker) behind the expander.
        // Final review L4: the exception text is sanitized (profile folder, paths, secrets) before it is shown.
        StringAssert.Contains(source, "StewardVisuals.MessageWithDetails(\"PC Steward couldn't start the factory reset. Nothing was deleted. Close PC Steward and try again.\", StartupNoticeText.SanitizedDetails(exception.Message))");
        StringAssert.Contains(source, "then try again.\", StartupNoticeText.SanitizedDetails(details))");
        Assert.AreEqual(0, System.Text.RegularExpressions.Regex.Count(source, @"(?<!SanitizedDetails\()\bexception\.Message\b"), "no raw exception text in any dialog or technical details");
        StringAssert.Contains(source, "StewardVisuals.MessageWithDetails(result.Message, residuals)");
        Assert.IsFalse(source.Contains("Content = exception.Message", StringComparison.Ordinal), "no raw exception text as a dialog's whole message");
        StringAssert.Contains(DesktopSource("App.xaml.cs"), "IsTextSelectionEnabled = true");
    }

    [TestMethod]
    [Description("AC-UX-03/04 (audit §5, P-M9; WCAG 1.4.3/1.4.11; UI polish Phase 1): neutral colours are Fluent theme brushes, captions use the secondary text brush, and status tones meet 4.5:1 in light and dark.")]
    public void CaptionTextAndBordersMeetContrastInLightAndDark()
    {
        // UI polish Phase 1 deliberately replaces the July 2026 navy palette (and its hex contrast checks) with Windows App SDK
        // theme brushes. Fluent's own text and stroke brushes are designed for these layers, with one exception the contract pins:
        // TextFillColorTertiary is below 4.5:1 on light cards, so caption and metadata text uses TextFillColorSecondary.
        Dictionary<string, Dictionary<string, string>> aliases = ThemeAliases();
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            ["StewardCanvasBrush"] = "SolidBackgroundFillColorBaseBrush",
            ["StewardSurfaceBrush"] = "CardBackgroundFillColorDefaultBrush",
            ["StewardSurfaceSubtleBrush"] = "CardBackgroundFillColorSecondaryBrush",
            ["StewardBorderBrush"] = "CardStrokeColorDefaultBrush",
            ["StewardBorderStrongBrush"] = "ControlStrongStrokeColorDefaultBrush",
            ["StewardTextBrush"] = "TextFillColorPrimaryBrush",
            ["StewardSecondaryTextBrush"] = "TextFillColorSecondaryBrush",
            ["StewardTertiaryTextBrush"] = "TextFillColorSecondaryBrush",
            ["StewardBrandBrush"] = "AccentTextFillColorPrimaryBrush",
            ["StewardBrandTextBrush"] = "AccentTextFillColorPrimaryBrush",
            ["StewardFocusBrush"] = "FocusStrokeColorOuterBrush"
        };
        Dictionary<string, Dictionary<string, string>> themes = ThemeColors();
        foreach (string theme in new[] { "Default", "Light", "Dark" })
        {
            foreach ((string token, string fluent) in expected)
            {
                Assert.AreEqual(fluent, aliases[theme].GetValueOrDefault(token), $"{theme}: {token} is the Fluent {fluent}");
                Assert.IsFalse(themes[theme].ContainsKey(token), $"{theme}: {token} has no custom colour");
            }
            Assert.AreNotEqual("TextFillColorTertiaryBrush", aliases[theme]["StewardTertiaryTextBrush"], "tertiary Fluent text is below 4.5:1 on light cards");
        }

        // Status tones keep custom colours (the Windows 11 system fill colours, darkened in light where needed). Each tone is at
        // least 4.5:1 on its own tinted surface and on the Windows 11 base and card colours of that theme (opaque equivalents).
        string[] lightLayers = ["#FFFFFF", "#FBFBFB", "#F3F3F3"];
        string[] darkLayers = ["#2B2B2B", "#202020", "#1C1C1C"];
        (string Foreground, string Surface)[] tones = [("StewardSuccessBrush", "StewardSuccessSurfaceBrush"), ("StewardWarningBrush", "StewardWarningSurfaceBrush"), ("StewardDangerBrush", "StewardDangerSurfaceBrush"), ("StewardInfoBrush", "StewardInfoSurfaceBrush"),
            // Colour option 2 (owner choice 2026-11-11): the decorative icon-well hues meet the same bar.
            .. DecorativeHues.Select(hue => ($"StewardHue{hue}Brush", $"StewardHue{hue}SurfaceBrush"))];
        foreach (string theme in new[] { "Default", "Light", "Dark" })
        {
            Dictionary<string, string> colors = themes[theme];
            foreach ((string foreground, string surface) in tones)
            {
                double own = ContrastRatio(colors[foreground], colors[surface]);
                Assert.IsTrue(own >= 4.5, $"{theme}: {foreground} {colors[foreground]} on {surface} {colors[surface]} is {own:0.00}:1");
                foreach (string layer in theme == "Dark" ? darkLayers : lightLayers)
                {
                    double ratio = ContrastRatio(colors[foreground], layer);
                    Assert.IsTrue(ratio >= 4.5, $"{theme}: {foreground} {colors[foreground]} on {layer} is {ratio:0.00}:1");
                }
            }
        }
        Assert.AreEqual(4.76, Math.Round(ContrastRatio("#0F7B0F", "#DFF6DD"), 2), "the lowest status pair, light success");
        // High contrast stays mapped to system colours.
        string tokens = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "Resources", "ColorTokens.xaml"));
        string highContrast = Between(tokens, "<ResourceDictionary x:Key=\"HighContrast\">", "</ResourceDictionary>");
        StringAssert.Contains(highContrast, "x:Key=\"StewardTertiaryTextBrush\" Color=\"{ThemeResource SystemColorWindowTextColor}\"");
        StringAssert.Contains(highContrast, "x:Key=\"StewardChipBorderBrush\" Color=\"{ThemeResource SystemColorWindowTextColor}\"");
        Assert.IsFalse(highContrast.Contains("Color=\"#", StringComparison.Ordinal), "no custom colour in high contrast");
    }

    /// <summary>Per theme dictionary: each StaticResource alias key and the Fluent resource it points to.</summary>
    private static Dictionary<string, Dictionary<string, string>> ThemeAliases()
    {
        XDocument tokens = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Desktop", "Resources", "ColorTokens.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return tokens.Descendants(presentation + "ResourceDictionary")
            .Where(dictionary => dictionary.Attribute(xaml + "Key") is not null)
            .ToDictionary(
                dictionary => dictionary.Attribute(xaml + "Key")!.Value,
                dictionary => dictionary.Elements(presentation + "StaticResource")
                    .ToDictionary(alias => alias.Attribute(xaml + "Key")!.Value, alias => alias.Attribute("ResourceKey")!.Value, StringComparer.Ordinal),
                StringComparer.Ordinal);
    }

    [TestMethod]
    [Description("AC-UX-04 (audit P-M9): action and filter rows wrap, headers and settings rows reflow, and the window has a minimum size.")]
    public void ActionRowsWrapAndTheWindowHasAMinimumSize()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        StringAssert.Contains(presentation, "public sealed class StewardWrapPanel : Panel");
        // UI review 2026-11-13 (fault 2): the lines are decided once, when the row is measured, and arranging reuses them.
        string wrap = presentation[presentation.IndexOf("public sealed class StewardWrapPanel : Panel", StringComparison.Ordinal)..];
        StringAssert.Contains(wrap, "measuredLines = measured.Lines;");
        StringAssert.Contains(wrap, "WrapLayout.Arrange(items, lines, finalSize.Width, HorizontalSpacing, VerticalSpacing, LevelLines)");
        Assert.IsFalse(wrap.Contains("> finalSize.Width", StringComparison.Ordinal), "arranging never decides a line break by comparing widths again");
        // Owner report 2026-11-13 (release 0.1.14): a row of controls gives everything on one line the line's height, so Home's
        // "Check for updates" (with an icon) is level with "Update all N…" beside it. Rows of text and chips do not.
        StringAssert.Contains(presentation, "ActionRow(double spacing = StewardSpacing.Small) => new() { HorizontalSpacing = spacing, VerticalSpacing = spacing, LevelLines = true };");
        Assert.AreEqual(2, Count(presentation, "LevelLines = true"), "action rows and title rows that hold a badge");
        // The same report: the update steps' joining lines are at the circles' middle, and a step's number is centred by its shape.
        string steps = Between(DesktopSource("MainWindow.cs"), "private static Grid BuildJobStepIndicator(", "private static string RiskGlyph(");
        StringAssert.Contains(steps, "StewardWrapPanel row = new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.Small, LevelLines = true };");
        StringAssert.Contains(steps, "new Border { Width = 40, Height = 1, Background = StewardVisuals.Brush(\"StewardBorderStrongBrush\"), VerticalAlignment = VerticalAlignment.Center }");
        // Owner report 2026-11-13 (release 0.1.15): that needs a font named by a style (measured on a Windows build: in the default
        // font the number was drawn 5.5 units above the circle's middle), so the number and the label go through CapHeightBox.
        StringAssert.Contains(steps, ": StewardVisuals.CapHeightBox(new TextBlock { Text = step.Number.ToString(CultureInfo.CurrentCulture), Style = AppStyle(\"StewardCaptionStyle\"),");
        StringAssert.Contains(steps, "entry.Children.Add(StewardVisuals.CapHeightBox(new TextBlock");
        StringAssert.Contains(steps, "Style = AppStyle(\"StewardBodyStyle\"),");
        Assert.IsFalse(steps.Contains("TextLineBounds", StringComparison.Ordinal), "no step text sets its own bounds");
        StringAssert.Contains(steps, "Style = AppStyle(\"StewardBodyStyle\"),");
        Assert.IsFalse(steps.Contains("TextLineBounds", StringComparison.Ordinal), "no step text sets its own bounds");
        StringAssert.Contains(presentation, "PlaceBesideOrBelow(grid, action, NarrowHeaderWidth, StewardSpacing.XLarge);");
        StringAssert.Contains(presentation, "PlaceBesideOrBelow(row, control, NarrowRowWidth, StewardSpacing.Large);");
        string source = DesktopSource("MainWindow.cs");
        // Final review Info-1: the 500x500 epx minimum is applied in physical pixels at the display scale, and again on a scale change.
        StringAssert.Contains(source, "presenter.PreferredMinimumWidth = WindowMinimumSize.PhysicalPixels(MinimumWindowWidth, scale);");
        StringAssert.Contains(source, "presenter.PreferredMinimumHeight = WindowMinimumSize.PhysicalPixels(MinimumWindowHeight, scale);");
        StringAssert.Contains(source, "private const int MinimumWindowWidth = WindowMinimumSize.EffectiveWidth;");
        StringAssert.Contains(source, "ApplyMinimumWindowSize(root.RasterizationScale);");
        StringAssert.Contains(source, "root.Changed += OnXamlRootChanged;");
        StringAssert.Contains(source, "private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyMinimumWindowSize(sender.RasterizationScale);");
        Assert.AreEqual(2, Count(source, "presenter.PreferredMinimum"), "the minimum size is set in one place");
        // The only horizontal StackPanels left in the window are icon+text pairs (the title-bar brand and the job stepper's
        // step dot and label). A button's icon and label are the shared StewardVisuals.IconLabel.
        Assert.AreEqual(2, Count(source, "Orientation = Orientation.Horizontal"), "action rows use StewardVisuals.ActionRow()");
        // Owner report 2026-11-13 (release 0.1.14): "Check for updates" was taller than a button with text only, and its label
        // sat above the middle, on Home and on Updates. Both now use the one shared content, where the icon adds no height.
        Assert.AreEqual(4, Count(source, "StewardVisuals.IconLabel(\"\\uE72C\", "), "Home, Updates, and the two layout samples the launch check measures");
        StringAssert.Contains(source, "if (scanAction) content = StewardVisuals.IconLabel(\"\\uE72C\", label, 16);");
        StringAssert.Contains(source, "check = new() { Content = StewardVisuals.IconLabel(\"\\uE72C\", checkLabel, 16), AccessKey = \"C\" };");
        StringAssert.Contains(presentation, "new FontIcon { Glyph = glyph, FontSize = iconSize, Margin = IconLabelIconMargin, VerticalAlignment = VerticalAlignment.Center }");
        StringAssert.Contains(presentation, "public static readonly Thickness IconLabelIconMargin = new(0, -4, 0, -4);");
        Assert.IsFalse(source.Contains("new FontIcon { Glyph = \"\\uE72C\"", StringComparison.Ordinal), "no page builds its own icon button content");
        // Owner report 2026-11-13 (release 0.1.15), measured from the drawn pixels of a Windows build: the icon was never the cause.
        // A text block placed in a button is one unit taller than the label a button draws for plain text and its letters sit
        // two to three units higher, so the label beside an icon is the element a button makes for its own label.
        string iconLabel = Between(presentation, "public static StackPanel IconLabel(string glyph, string label, double iconSize = 14)", "public static readonly Thickness IconLabelIconMargin");
        StringAssert.Contains(iconLabel, "content.Children.Add(ButtonLabel(label));");
        Assert.IsFalse(iconLabel.Contains("new TextBlock", StringComparison.Ordinal), "a text block in a button is taller and sits higher than a button's own label");
        StringAssert.Contains(presentation, "public static ContentPresenter ButtonLabel(string label) => new() { Content = label, VerticalAlignment = VerticalAlignment.Center };");
        // The review step's update button: the same shared content with the shield, and a plain label without it.
        StringAssert.Contains(source, "object primaryContent = sheet.ShowsAdministratorShield ? StewardVisuals.IconLabel(\"\\uEA18\", sheet.PrimaryButtonLabel, 16) : sheet.PrimaryButtonLabel;");
        Assert.IsFalse(source.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto", StringComparison.Ordinal), "filter rows wrap instead of scrolling sideways");
        Assert.IsTrue(Count(source, "StewardVisuals.ActionRow()") >= 12);
    }

    [TestMethod]
    [Description("FR-UI-01, AC-UX-01 best effort (UI review follow-up 2026-11-13, owner report): a status badge is level with the title beside it, and its label sits at the middle of its pill.")]
    public void ABadgeIsLevelWithTheTitleBesideIt()
    {
        string presentation = DesktopSource("StewardPresentation.cs");
        string source = DesktopSource("MainWindow.cs");
        // A title line that holds a badge gives everything on a line the line's height, so each thing centres itself in it.
        StringAssert.Contains(presentation, "public static StewardWrapPanel TitleRow() => new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.XSmall, LevelLines = true };");
        // Text beside the badge is measured by its capital letters and centred in a holder that is still one text line tall.
        string capHeight = Between(presentation, "public static TextBlock CapHeightBox(TextBlock text)", "public static StewardWrapPanel TitleRow()");
        StringAssert.Contains(capHeight, "text.TextLineBounds = TextLineBounds.Tight;");
        StringAssert.Contains(capHeight, "text.LineHeight = 0;", "a style's fixed line height would keep the box a full line high");
        StringAssert.Contains(capHeight, "text.VerticalAlignment = VerticalAlignment.Center;");
        // Owner report 2026-11-13 (release 0.1.15): measured on a Windows build, the default font ignores those bounds and
        // draws at the top of the box; a named font does not. Text with no style and no font of its own gets the body font.
        StringAssert.Contains(capHeight, "if (text.Style is null && text.ReadLocalValue(TextBlock.FontFamilyProperty) == DependencyProperty.UnsetValue) text.FontFamily = new FontFamily(NamedTextFont);");
        StringAssert.Contains(presentation, "public const string NamedTextFont = \"Segoe UI Variable Text\";");
        string capCentred = Between(presentation, "public static Grid CapCentred(TextBlock text)", "/// <summary>");
        StringAssert.Contains(capCentred, "CapHeightBox(text);");
        StringAssert.Contains(capCentred, "Grid holder = new() { MinHeight = TitleLineHeight, VerticalAlignment = VerticalAlignment.Center };");
        StringAssert.Contains(presentation, "public const double TitleLineHeight = 20;");

        // Every badge: the label is measured the same way inside its pill, in code and in the Installed apps row template.
        string badge = Between(presentation, "public static Border Badge(string label, string glyph, string tone = \"neutral\", string? automationName = null)", "AutomationProperties.SetName(badge, automationName ?? label);");
        StringAssert.Contains(badge, "Style = Style(\"StewardCaptionStyle\"), TextLineBounds = TextLineBounds.Tight, LineHeight = 0,");
        StringAssert.Contains(badge, "AutomationProperties.SetAutomationId(text, BadgeLabelAutomationId);");
        StringAssert.Contains(presentation, "public const string BadgeLabelAutomationId = \"StatusBadgeLabel\";");
        string templates = DesktopSource("Resources/DataTemplates.xaml");
        StringAssert.Contains(templates, "AutomationProperties.AutomationId=\"StatusBadgeLabel\"");
        StringAssert.Contains(templates, "TextLineBounds=\"Tight\"");
        StringAssert.Contains(templates, "LineHeight=\"0\"");
        StringAssert.Contains(Between(templates, "AutomationProperties.AutomationId=\"StatusBadgeLabel\"", "TextLineBounds=\"Tight\""), "Style=\"{StaticResource StewardCaptionStyle}\"", "the default font draws at the top of the box");

        // The two places a badge sits beside a title: an update's result card, and a ready row on the Updates page.
        StringAssert.Contains(source, "StewardWrapPanel title = StewardVisuals.TitleRow();");
        StringAssert.Contains(source, "title.Children.Add(StewardVisuals.CapCentred(name));");
        StringAssert.Contains(source, "StewardWrapPanel title = badged ? StewardVisuals.TitleRow() : new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.XSmall };");
        StringAssert.Contains(source, "title.Children.Add(UpdateRowName(ready.Name, ready.StableProductKey, badged));");
        StringAssert.Contains(source, "title.Children.Add(badged ? StewardVisuals.CapCentred(versions) : versions);");
        StringAssert.Contains(source, "Content = besideBadge ? StewardVisuals.CapCentred(label) : label,");
        Assert.AreEqual(2, Count(source, "badge.VerticalAlignment = VerticalAlignment.Center;") + Count(source, "jump.VerticalAlignment = VerticalAlignment.Center;"), "both badges centre themselves in the line");
        // The title bar's name is measured the same way, so it is level with the mark beside it and the window's own buttons.
        StringAssert.Contains(source, "brand.Children.Add(StewardVisuals.CapHeightBox(new TextBlock { Text = AppIdentity.DisplayName, Style = AppStyle(\"StewardCaptionStyle\") }));");

        // The Windows launch check measures a badge label's box on a real build.
        string smoke = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Test-DesktopLaunchSmokeCI.ps1"));
        StringAssert.Contains(smoke, "'LAYOUT-BADGE-LABEL where='");
        StringAssert.Contains(smoke, "Write-BadgeLabel $pane 'details'");
        StringAssert.Contains(smoke, "' offCentre='");
    }

    [TestMethod]
    [Description("FR-UI-01, AC-UX-01 best effort (owner reports 2026-11-13, releases 0.1.14 and 0.1.15): alignment is proven from the drawn pixels of a Windows build, on sample controls that only the launch check can turn on.")]
    public void AlignmentIsMeasuredFromDrawnPixelsOnSamplesOnlyTheLaunchCheckShows()
    {
        string source = DesktopSource("MainWindow.cs");
        // The samples are on the About page only when the launch check asks, and nowhere else reads that switch.
        StringAssert.Contains(source, "internal const string LayoutSamplesVariable = \"PCSTEWARD_LAYOUT_SAMPLES\";");
        StringAssert.Contains(source, "private static bool LayoutSamplesRequested => Environment.GetEnvironmentVariable(LayoutSamplesVariable) == \"1\";");
        StringAssert.Contains(source, "if (LayoutSamplesRequested) panel.Children.Add(BuildLayoutSamples());");
        Assert.AreEqual(2, Count(source, "LayoutSamplesRequested"), "one definition and one use, on the About page");
        Assert.AreEqual(2, Count(source, "BuildLayoutSamples()"), "one definition and one use");
        // They are the pages' own building blocks, and they do nothing: no handler, no view model, no command.
        string samples = Between(source, "private static Border BuildLayoutSamples()", "private static Button SampleButton(");
        StringAssert.Contains(samples, "StewardWrapPanel actions = StewardVisuals.ActionRow();");
        Assert.AreEqual(2, Count(samples, "StewardVisuals.IconLabel("));
        Assert.AreEqual(2, Count(samples, "samples.Children.Add(BuildJobStepIndicator("));
        StringAssert.Contains(samples, "StewardWrapPanel title = StewardVisuals.TitleRow();");
        StringAssert.Contains(samples, "StewardVisuals.CapCentred(");
        StringAssert.Contains(samples, "StewardVisuals.Badge(");
        foreach (string forbidden in new[] { "+=", "viewModel", "Async", "Click" })
        {
            Assert.IsFalse(samples.Contains(forbidden, StringComparison.Ordinal), $"a sample does nothing: {forbidden}");
        }

        // The launch check turns them on, reads pixels, and states each owner report as a verdict line.
        string smoke = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "Test-DesktopLaunchSmokeCI.ps1"));
        StringAssert.Contains(smoke, "$env:PCSTEWARD_LAYOUT_SAMPLES = '1'");
        StringAssert.Contains(smoke, "public static class SmokeInk");
        StringAssert.Contains(smoke, "$g.CopyFromScreen(");
        StringAssert.Contains(smoke, "' tallerThanTextButton='");
        StringAssert.Contains(smoke, "' labelHigherThanTextButton='");
        StringAssert.Contains(smoke, "'LAYOUT-INK-VERDICT steps worstMarkMid='");
        StringAssert.Contains(smoke, "'LAYOUT-INK-VERDICT title-badge labelMid='");
        StringAssert.Contains(smoke, "'LAYOUT-INK-VERDICT titlebar nameBelowMark='");
        // The check stays informational: a measurement never adds a failure.
        Assert.IsFalse(Between(smoke, "function Write-ButtonInk(", "function Open-Page(").Contains("$failures", StringComparison.Ordinal));
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
        IEnumerable<string> files = DesktopFiles.Where(file => file != "MainWindow.cs").Concat(DesktopWindowSource.PartialFiles).Select(file => Path.Combine(root, "src", "PCSteward.Desktop", file))
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
        StringAssert.Contains(window, "InstalledAppsFilters.ConfirmCurrentMatchesLabel");
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

    private static string DesktopSource(string relativePath) => DesktopWindowSource.ReadDesktopFile(FindRepositoryRoot(), relativePath);

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
