using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// UI polish Phase 1 (ADR-030 implementation notes, 2026-11-11; approved dark Calm Native targets): source contracts for the
/// layout and colour foundations. The pages sit in one left-aligned column beside the navigation, colours come from Fluent theme
/// brushes over Mica, NavigationView keeps its standard selection, type follows the Fluent ramp, and the shared row card, info bar,
/// status chip and icon well exist for every page. What these contracts can't show (the look itself) is checked against the
/// targets on the owner's PC.
/// </summary>
[TestClass]
public sealed class UiPolishFoundationContractTests
{
    [TestMethod]
    [Description("UI polish Phase 1 (plan root cause 2): every page is one left-aligned column, at most 1000 epx of content, fitted to its host.")]
    public void PagesAreOneLeftAlignedColumnBesideTheNavigation()
    {
        string window = DesktopWindowSource.Read(Root());
        string create = Between(window, "private static StackPanel CreatePagePanel()", "private static TextBlock PageCopy(");
        StringAssert.Contains(create, "HorizontalAlignment = HorizontalAlignment.Left");
        StringAssert.Contains(create, "MaxWidth = StewardSpacing.PageMaxWidth + padding.Left + padding.Right");
        StringAssert.Contains(create, "Spacing = StewardSpacing.Section");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(window, @"CreatePagePanel\(\s*\d"), "no page sets its own width");
        Assert.IsFalse(create.Contains("MaxWidth = maxWidth", StringComparison.Ordinal), "the column is not a centred, per-page width");

        // Both hosts (the shell's page host and the bare host) fit the column to their width; the shell re-fits after each page.
        StringAssert.Contains(window, "StewardVisuals.KeepPageColumnFitted(pageHost);");
        StringAssert.Contains(window, "StewardVisuals.KeepPageColumnFitted(host);");
        StringAssert.Contains(window, "pageHost.Content = page;\n        StewardVisuals.FitPageColumn(pageHost);");
        string presentation = Desktop("StewardPresentation.cs");
        StringAssert.Contains(Between(presentation, "public static void FitPageColumn(", "public static void KeepPageColumnFitted("), "page.Width = Math.Min(host.ActualWidth, page.MaxWidth);");
        StringAssert.Contains(presentation, "public const double PageMaxWidth = 1000;");
        StringAssert.Contains(presentation, "public static readonly Thickness PagePadding = new(28, 12, 40, 40);");
        StringAssert.Contains(Desktop("Resources/ThemeResources.xaml"), "<Thickness x:Key=\"StewardPagePadding\">28,12,40,40</Thickness>");
        StringAssert.Contains(Desktop("Resources/ThemeResources.xaml"), "<x:Double x:Key=\"StewardPageMaxWidth\">1000</x:Double>");
    }

    [TestMethod]
    [Description("UI polish Phase 1: the navigation pane and content are transparent over Mica and NavigationView keeps its standard selected-item style.")]
    public void NavigationUsesTheStandardSelectionOverMica()
    {
        string tokens = Desktop("Resources/ColorTokens.xaml");
        Assert.IsFalse(tokens.Contains("NavigationViewItemBackgroundSelected", StringComparison.Ordinal), "no custom selected-item fill");
        Assert.IsFalse(tokens.Contains("NavigationViewItemForegroundSelected", StringComparison.Ordinal), "no custom selected-item text");
        foreach (string theme in new[] { "Default", "Light", "Dark" })
        {
            string block = Between(tokens, $"<ResourceDictionary x:Key=\"{theme}\">", "</ResourceDictionary>");
            foreach (string transparent in new[] { "NavigationViewDefaultPaneBackground", "NavigationViewContentBackground", "NavigationViewContentGridBorderBrush" })
                StringAssert.Contains(block, $"<SolidColorBrush x:Key=\"{transparent}\" Color=\"Transparent\" />");
        }
        Assert.IsFalse(Between(tokens, "<ResourceDictionary x:Key=\"HighContrast\">", "</ResourceDictionary>").Contains("NavigationView", StringComparison.Ordinal), "high contrast keeps the system navigation colours");
        // Mica stays the base layer (Desktop Acrylic, then the Fluent solid base, as fallbacks; opaque in high contrast).
        string window = DesktopWindowSource.Read(Root());
        StringAssert.Contains(window, ": MicaController.IsSupported() ? new MicaBackdrop()");
        StringAssert.Contains(window, "windowRoot.Background = SystemBackdrop is null ? StewardVisuals.Brush(\"StewardCanvasBrush\")");
    }

    [TestMethod]
    [Description("UI polish Phase 1: the Fluent type ramp — Body page descriptions under the title, Body Strong section headings, caption eyebrows.")]
    public void TypeFollowsTheFluentRamp()
    {
        string typography = Desktop("Resources/Typography.xaml");
        StringAssert.Contains(typography, "<Style x:Key=\"StewardSectionTitleStyle\" TargetType=\"TextBlock\" BasedOn=\"{StaticResource StewardBodyStrongStyle}\" />");
        string eyebrow = Between(typography, "<Style x:Key=\"StewardEyebrowStyle\"", "</Style>");
        StringAssert.Contains(eyebrow, "{StaticResource StewardCaptionFontSize}");
        Assert.IsFalse(eyebrow.Contains("CharacterSpacing", StringComparison.Ordinal), "no tracked-out eyebrow");
        Assert.IsFalse(eyebrow.Contains("StewardBrandTextBrush", StringComparison.Ordinal), "the eyebrow is not accent coloured");
        foreach ((string size, string value) in new[] { ("StewardCaptionFontSize", "12"), ("StewardBodyFontSize", "14"), ("StewardBodyLargeFontSize", "18"), ("StewardSubtitleFontSize", "20"), ("StewardTitleFontSize", "28"), ("StewardTitleLargeFontSize", "40") })
            StringAssert.Contains(typography, $"<x:Double x:Key=\"{size}\">{value}</x:Double>");

        string header = Between(Desktop("StewardPresentation.cs"), "public static Grid PageHeader(", "private const double NarrowHeaderWidth");
        StringAssert.Contains(header, "copy.Children.Add(new TextBlock { Text = description, Style = Style(\"StewardBodyStyle\") });");
        Assert.IsFalse(header.Contains("StewardBodyLargeStyle", StringComparison.Ordinal), "page descriptions are not Body Large");
        Assert.IsFalse(header.Contains("MaxWidth = 760", StringComparison.Ordinal), "the description shares the title's left edge and column");
    }

    [TestMethod]
    [Description("UI polish Phase 1: the shared row card, navigation row, icon well, info bar and status chip exist and keep their accessibility rules.")]
    public void SharedRowCardInfoBarAndStatusPiecesExist()
    {
        string presentation = Desktop("StewardPresentation.cs");
        StringAssert.Contains(presentation, "public static Border RowCard(string glyph, string title, string? description, FrameworkElement? control = null, string tone = \"neutral\")");
        StringAssert.Contains(presentation, "return new Border { Style = Style(\"StewardRowCardStyle\"), Child = row };");
        // The navigation row is one button whose name starts with its visible title (WCAG 2.5.3).
        string navigationRow = Between(presentation, "public static Button NavigationRow(", "public static Border EmptyState(");
        StringAssert.Contains(navigationRow, "return Named(new Button { Style = Style(\"StewardRowCardButtonStyle\"), Content = row }, title, description);");
        StringAssert.Contains(navigationRow, "Glyph = \"\\uE76C\"");
        string well = Between(presentation, "public static Border IconWell(", "private static StackPanel RowCopy(");
        StringAssert.Contains(well, "AutomationProperties.SetAccessibilityView(well, AccessibilityView.Raw);");

        // Page messages are the standard InfoBar: open, not closable, severity from the status tone.
        string infoBar = Between(presentation, "public static InfoBar InformationBanner(", "public static Border IconWell(");
        foreach (string expected in new[] { "IsOpen = true,", "IsClosable = false,", "\"attention\" => InfoBarSeverity.Warning", "\"blocked\" => InfoBarSeverity.Error", "\"success\" => InfoBarSeverity.Success", "_ => InfoBarSeverity.Informational", "AutomationProperties.SetName(bar, $\"{title}. {message}\");" })
            StringAssert.Contains(infoBar, expected);

        // Status chips are pills whose glyph and label carry the meaning; the information tone has its own checked colours.
        string controls = Desktop("Resources/ControlStyles.xaml");
        string chip = Between(controls, "<Style x:Key=\"StewardStatusChipStyle\"", "</Style>");
        StringAssert.Contains(chip, "{StaticResource StewardChipCornerRadius}");
        StringAssert.Contains(chip, "{ThemeResource StewardChipBorderBrush}");
        StringAssert.Contains(presentation, "\"information\" => (Brush(\"StewardInfoBrush\"), Brush(\"StewardInfoSurfaceBrush\")),");
        foreach (string style in new[] { "StewardRowCardStyle", "StewardRowCardButtonStyle", "StewardIconWellStyle" })
            StringAssert.Contains(controls, $"x:Key=\"{style}\"");
    }

    private static string Root() => CompiledIlScanner.FindRepositoryRoot();

    private static string Desktop(string relativePath) => File.ReadAllText(Path.Combine(Root(), "src", "PCSteward.Desktop", relativePath));

    private static string Between(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = start < 0 ? -1 : source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0 && end > start, $"Could not find section from {startMarker} to {endMarker}.");
        return source[start..end];
    }
}
