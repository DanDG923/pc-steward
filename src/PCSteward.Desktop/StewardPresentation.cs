using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UI-01/05, AC-UX-03 (Task 73 Phase 1): the Desktop status chip. Label, glyph and tone come only from the portable
/// <see cref="StatusVocabulary"/>, so every list, badge and filter uses the same words; colour is never the only signal.
/// </summary>
public sealed record StewardStatus(string Label, string Glyph, string Tone)
{
    public static StewardStatus From(string status)
    {
        StatusChipPresentation chip = StatusVocabulary.For(status);
        return new(chip.Label, chip.Glyph, ToneName(chip.Tone));
    }

    public static string ToneName(StatusTone tone) => tone switch
    {
        StatusTone.Success => "success",
        StatusTone.Information => "information",
        StatusTone.Attention => "attention",
        StatusTone.Critical => "blocked",
        _ => "neutral"
    };
}

/// <summary>Spacing tokens on the 4 px grid, mirroring <c>Resources/ThemeResources.xaml</c>.</summary>
internal static class StewardSpacing
{
    public const double XSmall = 4;
    public const double Small = 8;
    public const double Medium = 12;
    public const double Large = 16;
    /// <summary>The gap between a page's sections (header, cards, groups), as in the approved targets.</summary>
    public const double Section = 20;
    public const double XLarge = 24;
    public const double XXLarge = 32;
    public const double TitleBarHeight = 32;
    /// <summary>UI polish Phase 1: the page column's content width; the column sits beside the navigation, left aligned.</summary>
    public const double PageMaxWidth = 1000;
    /// <summary>The page column's padding (left 28, top 12, right 40, bottom 40), mirroring <c>StewardPagePadding</c>.</summary>
    public static readonly Thickness PagePadding = new(28, 12, 40, 40);
}

public sealed class ApplicationListItem
{
    private ApplicationListItem(InventoryRow row)
    {
        Row = row;
        Name = row.Name;
        Publisher = string.Equals(row.Publisher, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown publisher" : row.Publisher;
        Version = string.Equals(row.Version, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown version" : row.Version;
        ScopeAndProvider = $"{StewardPresentation.Scope(row.Scope)} · {StewardPresentation.Provider(row.Provider)}";
        StewardStatus status = StewardStatus.From(row.Status);
        StatusLabel = status.Label;
        StatusGlyph = status.Glyph;
        (StatusForeground, StatusBackground) = StewardVisuals.ToneBrushes(status.Tone);
        Initials = StewardPresentation.Initials(row.Name);
        AccessibleSummary = $"{Name}, {Publisher}, version {Version}, {StatusLabel}, {ScopeAndProvider}";
    }

    public InventoryRow Row { get; }
    public string Name { get; }
    public string Publisher { get; }
    public string Version { get; }
    public string ScopeAndProvider { get; }
    public string StatusLabel { get; }
    public string StatusGlyph { get; }
    public Brush StatusForeground { get; }
    public Brush StatusBackground { get; }
    public string Initials { get; }
    public string AccessibleSummary { get; }

    public static ApplicationListItem From(InventoryRow row) => new(row);
}

public static class StewardPresentation
{
    public static string Provider(string value) => value.ToLowerInvariant() switch
    {
        "winget" => "WinGet",
        "msstore" => "Microsoft Store",
        "msix" => "MSIX",
        "appx" => "AppX",
        "registry" => "Windows registry",
        "chocolatey" => "Chocolatey",
        _ => string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown source" : value
    };

    public static string Scope(string value) => value.ToLowerInvariant() switch
    {
        "machine" => "All users",
        "user" => "Current user",
        _ => string.Equals(value, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Scope unknown" : value
    };

    public static string Initials(string name)
    {
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length >= 2) return string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
        if (words.Length == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
        return "AP";
    }
}

internal static class StewardVisuals
{
    public static Brush Brush(string key) => (Brush)global::Microsoft.UI.Xaml.Application.Current.Resources[key];
    public static Style Style(string key) => (Style)global::Microsoft.UI.Xaml.Application.Current.Resources[key];

    /// <summary>
    /// A page header: optional eyebrow, the page title (a level-1 heading, AC-UX-01) and description, with an optional action that
    /// sits beside the copy on wide windows and moves under it on narrow windows or at large text sizes (audit P-M9).
    /// </summary>
    public static Grid PageHeader(string title, string description, string? eyebrow = null, FrameworkElement? action = null)
    {
        Grid grid = new() { ColumnSpacing = StewardSpacing.XLarge };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        StackPanel copy = new() { Spacing = StewardSpacing.XSmall };
        // Sentence case (concept study §2 foundation 9): the eyebrow is shown as written, never forced to capitals.
        if (!string.IsNullOrWhiteSpace(eyebrow)) copy.Children.Add(new TextBlock { Text = eyebrow, Style = Style("StewardEyebrowStyle") });
        copy.Children.Add(PageTitle(title));
        // UI polish Phase 1 (approved targets): the description is Body text in the secondary colour, directly under the title
        // and on the same left edge, as in Windows Settings.
        copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardBodyStyle") });
        grid.Children.Add(copy);
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(action);
            PlaceBesideOrBelow(grid, action, NarrowHeaderWidth, StewardSpacing.XLarge);
        }
        return grid;
    }

    /// <summary>Below these widths a header or settings-row action moves under its copy instead of clipping beside it (audit P-M9).</summary>
    private const double NarrowHeaderWidth = 640;
    private const double NarrowRowWidth = 520;

    /// <summary>Puts <paramref name="action"/> in column 1 on wide layouts and in row 1, under the copy, on narrow ones.</summary>
    private static void PlaceBesideOrBelow(Grid grid, FrameworkElement action, double narrowWidth, double wideSpacing)
    {
        HorizontalAlignment wideAlignment = action.HorizontalAlignment;
        void Place(double width)
        {
            bool narrow = width > 0 && width < narrowWidth;
            Grid.SetColumn(action, narrow ? 0 : 1);
            Grid.SetRow(action, narrow ? 1 : 0);
            grid.ColumnSpacing = narrow ? 0 : wideSpacing;
            grid.RowSpacing = narrow ? StewardSpacing.Small : 0;
            action.HorizontalAlignment = narrow ? HorizontalAlignment.Left : wideAlignment;
        }
        Place(0);
        grid.SizeChanged += (_, args) => Place(args.NewSize.Width);
    }

    /// <summary>
    /// UI polish Phase 1 (plan root cause 2): keeps the hosted page column left aligned beside the navigation. A page is a
    /// left-aligned column with a maximum width; this gives it the host's full width up to that maximum, so the column fills a
    /// narrow window and stops at its maximum on a wide one instead of floating in the middle. Call it after setting the
    /// host's content; the host re-fits the column whenever it is resized.
    /// </summary>
    public static void FitPageColumn(ScrollViewer host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Content is FrameworkElement page && host.ActualWidth > 0) page.Width = Math.Min(host.ActualWidth, page.MaxWidth);
    }

    /// <summary>Re-fits the hosted page column (<see cref="FitPageColumn"/>) whenever <paramref name="host"/> is resized.</summary>
    public static void KeepPageColumnFitted(ScrollViewer host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.SizeChanged += (_, _) => FitPageColumn(host);
    }

    /// <summary>AC-UX-01: a page title, exposed to assistive technology as a level-1 heading.</summary>
    public static TextBlock PageTitle(string text)
    {
        TextBlock title = new() { Text = text, Style = Style("StewardPageTitleStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        return title;
    }

    /// <summary>AC-UX-01: a section heading (level 2 by default; level 3 for an item inside a section).</summary>
    public static TextBlock SectionHeading(string text, AutomationHeadingLevel level = AutomationHeadingLevel.Level2)
    {
        TextBlock heading = new() { Text = text, Style = Style("StewardSectionTitleStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(heading, level);
        return heading;
    }

    /// <summary>
    /// WCAG 2.5.3 label in name (AC-UX-01): sets the accessible name to the visible label, optionally followed by detail, through
    /// <see cref="AccessibilityText.LabelInName"/>, so the name always starts with what is on screen.
    /// </summary>
    public static T Named<T>(T element, string visibleLabel, string? detail = null) where T : DependencyObject
    {
        AutomationProperties.SetName(element, AccessibilityText.LabelInName(visibleLabel, detail));
        return element;
    }

    /// <summary>A standard button whose accessible name starts with its visible label.</summary>
    public static Button ActionButton(string label, string? detail = null) => Named(new Button { Content = label }, label, detail);

    /// <summary>A primary (accent) button whose accessible name starts with its visible label.</summary>
    public static Button PrimaryButton(string label, string? detail = null) =>
        Named(new Button { Content = label, Style = Style("StewardPrimaryButtonStyle") }, label, detail);

    /// <summary>Audit P-M9: a row of actions that wraps onto more lines instead of clipping at narrow widths or large text.</summary>
    public static StewardWrapPanel ActionRow(double spacing = StewardSpacing.Small) => new() { HorizontalSpacing = spacing, VerticalSpacing = spacing };

    /// <summary>
    /// AC-UX-09 (research §2 item 10): raises a UIA notification with <paramref name="message"/> from <paramref name="source"/>, so
    /// Narrator announces a finished scan, update job, review or save/clear without moving focus.
    /// </summary>
    public static void Announce(UIElement? source, string message, string activityId)
    {
        if (source is null || string.IsNullOrWhiteSpace(message)) return;
        try
        {
            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(source) ?? FrameworkElementAutomationPeer.CreatePeerForElement(source);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, message, activityId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    /// <summary>
    /// AC-UX-09: updates a polite live region. LiveRegionChanged is raised only when <paramref name="announce"/> is true (the
    /// state changed), so elapsed-time ticks update the text without repeating it to a screen reader.
    /// </summary>
    public static void SetLiveText(TextBlock target, string text, bool announce)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (string.Equals(target.Text, text, StringComparison.Ordinal)) return;
        target.Text = text;
        if (!announce) return;
        try { (FrameworkElementAutomationPeer.FromElement(target) ?? FrameworkElementAutomationPeer.CreatePeerForElement(target))?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
    }

    /// <summary>Selectable evidence text (package IDs, versions, technical detail) that can be copied.</summary>
    public static TextBlock SelectableText(string text, string? styleKey = null)
    {
        TextBlock block = new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        if (styleKey is not null) block.Style = Style(styleKey);
        return block;
    }

    /// <summary>
    /// Concept study §2 foundation 9: a plain, blame-free message, with any technical code or diagnostic kept behind a collapsed
    /// "Technical details" expander and selectable so it can be copied into a report.
    /// </summary>
    public static FrameworkElement MessageWithDetails(string message, string? technicalDetail)
    {
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(technicalDetail))
            content.Children.Add(new Expander { Header = "Technical details", Content = SelectableText(technicalDetail), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        return content;
    }

    public static Border StatusBadge(string status, string? automationName = null)
    {
        StewardStatus presentation = StewardStatus.From(status);
        return Badge(presentation.Label, presentation.Glyph, presentation.Tone, automationName);
    }

    /// <summary>The one tone-to-brush mapping shared by code-built chips and the inventory row template.</summary>
    public static (Brush Foreground, Brush Background) ToneBrushes(string tone) => tone switch
    {
        "success" => (Brush("StewardSuccessBrush"), Brush("StewardSuccessSurfaceBrush")),
        "attention" => (Brush("StewardWarningBrush"), Brush("StewardWarningSurfaceBrush")),
        "blocked" => (Brush("StewardDangerBrush"), Brush("StewardDangerSurfaceBrush")),
        "information" => (Brush("StewardInfoBrush"), Brush("StewardInfoSurfaceBrush")),
        _ => (Brush("StewardSecondaryTextBrush"), Brush("StewardIconWellBrush"))
    };

    public static Border Badge(string label, string glyph, string tone = "neutral", string? automationName = null)
    {
        (Brush foreground, Brush background) = ToneBrushes(tone);
        // UI polish Phase 1: a tinted pill (StewardStatusChipStyle); the glyph and label carry the meaning, never colour alone.
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, Style = Style("StewardCaptionStyle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = foreground, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        Border badge = new() { Style = Style("StewardStatusChipStyle"), Background = background, Child = content };
        AutomationProperties.SetName(badge, automationName ?? label);
        return badge;
    }

    /// <summary>
    /// UI polish Phase 1 (approved targets): a page-level message as the standard Windows <see cref="InfoBar"/>, which brings the
    /// Fluent severity colours, icon and high-contrast behaviour. It stays open (no close button), because each message on a page
    /// explains a state the owner can't dismiss. <paramref name="tone"/> uses the status tone names (information, attention,
    /// blocked, success); an optional action sits after the message.
    /// </summary>
    public static InfoBar InformationBanner(string title, string message, string tone = "information", ButtonBase? action = null)
    {
        InfoBar bar = new()
        {
            Title = title,
            Message = message,
            Severity = tone switch { "attention" => InfoBarSeverity.Warning, "blocked" => InfoBarSeverity.Error, "success" => InfoBarSeverity.Success, _ => InfoBarSeverity.Informational },
            IsOpen = true,
            IsClosable = false,
            IsIconVisible = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (action is not null) bar.ActionButton = action;
        AutomationProperties.SetName(bar, $"{title}. {message}");
        return bar;
    }

    /// <summary>
    /// UI polish Phase 1: a 36 epx icon well (6 px corners). Neutral wells use the subtle fill and the primary text colour; a
    /// status tone tints the well with that tone's surface and glyph colour.
    /// </summary>
    public static Border IconWell(string glyph, string tone = "neutral")
    {
        (Brush foreground, Brush background) = tone == "neutral" ? (Brush("StewardTextBrush"), Brush("StewardIconWellBrush")) : ToneBrushes(tone);
        Border well = new() { Style = Style("StewardIconWellStyle"), Background = background, Child = new FontIcon { Glyph = glyph, FontSize = 20, Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        AutomationProperties.SetAccessibilityView(well, AccessibilityView.Raw);
        return well;
    }

    /// <summary>The title and description of a row card: Body in the primary colour, then Caption in the secondary colour.</summary>
    private static StackPanel RowCopy(string title, string? description)
    {
        StackPanel copy = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, Style = Style("StewardRowTitleStyle") });
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardCaptionStyle") });
        return copy;
    }

    /// <summary>
    /// UI polish Phase 1 (approved targets): a Windows Settings-style row card. An icon well, a title and one-sentence description,
    /// and an optional control on the right (under the copy on narrow windows or at large text sizes, audit P-M9).
    /// </summary>
    public static Border RowCard(string glyph, string title, string? description, FrameworkElement? control = null, string tone = "neutral")
    {
        Grid row = new() { ColumnSpacing = StewardSpacing.Large };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.Children.Add(IconWell(glyph, tone));
        StackPanel copy = RowCopy(title, description);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(control);
            void Place(double width)
            {
                bool narrow = width > 0 && width < NarrowRowWidth;
                Grid.SetColumn(control, narrow ? 1 : 2);
                Grid.SetRow(control, narrow ? 1 : 0);
                row.RowSpacing = narrow ? StewardSpacing.Small : 0;
                control.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }
            Place(0);
            row.SizeChanged += (_, args) => Place(args.NewSize.Width);
        }
        return new Border { Style = Style("StewardRowCardStyle"), Child = row };
    }

    /// <summary>
    /// UI polish Phase 1 (approved targets): a row card that opens something — the whole row is one button with an icon well,
    /// title, description and a chevron. Its accessible name starts with the title (WCAG 2.5.3) and adds the description.
    /// </summary>
    public static Button NavigationRow(string glyph, string title, string? description, string tone = "neutral")
    {
        Grid row = new() { ColumnSpacing = StewardSpacing.Large };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(IconWell(glyph, tone));
        StackPanel copy = RowCopy(title, description);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        FontIcon chevron = new() { Glyph = "\uE76C", FontSize = 12, Foreground = Brush("StewardSecondaryTextBrush"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);
        return Named(new Button { Style = Style("StewardRowCardButtonStyle"), Content = row }, title, description);
    }

    public static Border EmptyState(string glyph, string title, string message, Button? action = null)
    {
        StackPanel content = new() { Spacing = StewardSpacing.Small, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 30, Foreground = Brush("StewardBrandBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        TextBlock heading = SectionHeading(title);
        heading.TextAlignment = TextAlignment.Center;
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = message, Style = Style("StewardBodyStyle"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        if (action is not null) { action.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(action); }
        Border empty = new() { Style = Style("StewardCardStyle"), Padding = new Thickness(32), Child = content };
        AutomationProperties.SetName(empty, $"{title}. {message}");
        return empty;
    }

    /// <summary>A count with its status chip; the label is the chip's vocabulary label, so cards and rows use the same words.</summary>
    public static Border MetricCard(int value, string detail, string status)
    {
        string label = StewardStatus.From(status).Label;
        StackPanel content = new() { Spacing = StewardSpacing.XSmall };
        content.Children.Add(new TextBlock { Text = value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Style = Style("StewardDisplayStyle") });
        Border badge = StatusBadge(status);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        content.Children.Add(badge);
        content.Children.Add(new TextBlock { Text = detail, Style = Style("StewardCaptionStyle") });
        Border card = new() { Style = Style("StewardCardStyle"), Child = content };
        AutomationProperties.SetName(card, $"{value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)} {label}. {detail}");
        return card;
    }

    public static Grid EvidenceRow(string label, string value)
    {
        Grid row = new() { ColumnSpacing = StewardSpacing.Large, Padding = new Thickness(0, StewardSpacing.XSmall, 0, StewardSpacing.XSmall) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Style = Style("StewardCaptionStyle") });
        // Evidence values (package IDs, versions, sources, technical detail) can be selected and copied (audit §5).
        TextBlock valueText = new() { Text = value, Foreground = Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        AutomationProperties.SetName(row, $"{label}: {value}");
        return row;
    }

    /// <summary>
    /// Task 73 Phase 4 (ADR-030 concept A): a Windows 11 SettingsCard-style group — icon, a level-2 heading, an optional description,
    /// then its rows. <paramref name="danger"/> draws the separated destructive-action card (border and heading in the danger colour;
    /// the meaning is also carried by the "Danger zone" text and the warning icon, never by colour alone).
    /// </summary>
    public static Border SettingsCard(string glyph, string title, string? description, FrameworkElement body, bool danger = false)
    {
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        Grid header = new() { ColumnSpacing = StewardSpacing.Medium };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // UI polish Phase 1: the card's icon sits in an icon well, as in the approved targets (the danger card's well is danger toned).
        Border well = IconWell(glyph, danger ? "blocked" : "neutral");
        well.VerticalAlignment = VerticalAlignment.Top;
        header.Children.Add(well);
        StackPanel copy = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        TextBlock heading = SectionHeading(title);
        if (danger) heading.Foreground = Brush("StewardDangerBrush");
        copy.Children.Add(heading);
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        content.Children.Add(header);
        content.Children.Add(body);
        Border card = new() { Style = Style("StewardCardStyle"), Child = content };
        if (danger) { card.BorderBrush = Brush("StewardDangerBrush"); card.BorderThickness = new Thickness(2); }
        return card;
    }

    /// <summary>One SettingsCard row: a label and description on the left, its control on the right (under them when narrow).</summary>
    public static Grid SettingsRow(string title, string? description, FrameworkElement control)
    {
        Grid row = new() { ColumnSpacing = StewardSpacing.Large, Padding = new Thickness(0, StewardSpacing.XSmall, 0, StewardSpacing.XSmall) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        StackPanel copy = new() { Spacing = StewardSpacing.XSmall, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(new TextBlock { Text = title, Foreground = Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        row.Children.Add(copy);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(control);
        PlaceBesideOrBelow(row, control, NarrowRowWidth, StewardSpacing.Large);
        return row;
    }

    public static Border ActivityRow(string when, string glyph, string title, string detail)
    {
        Grid row = new() { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = Brush("StewardBrandBrush"), Margin = new Thickness(0, 2, 0, 0) });
        StackPanel copy = new() { Spacing = StewardSpacing.XSmall };
        copy.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(detail)) copy.Children.Add(new TextBlock { Text = detail, Style = Style("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        TextBlock time = new() { Text = when, Style = Style("StewardCaptionStyle"), Margin = new Thickness(12, 1, 0, 0) };
        Grid.SetColumn(time, 2);
        row.Children.Add(time);
        Border result = new() { BorderBrush = Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = row };
        AutomationProperties.SetName(result, $"{title}. {detail}. {time.Text}");
        return result;
    }
}

/// <summary>
/// Audit P-M9 (AC-UX best effort, research §2 item 7): a minimal wrapping panel for action and filter rows. Children keep their
/// natural size and move to the next line when the row runs out of width, so nothing is clipped at narrow widths or at 150–225%
/// text scaling. Standard WinUI only (no Toolkit dependency); it lays out and does nothing else.
/// </summary>
public sealed class StewardWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = StewardSpacing.Small;
    public double VerticalSpacing { get; set; } = StewardSpacing.Small;

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize)
    {
        double lineWidth = 0, lineHeight = 0, width = 0, height = 0;
        foreach (UIElement child in Children)
        {
            child.Measure(new global::Windows.Foundation.Size(availableSize.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            global::Windows.Foundation.Size desired = child.DesiredSize;
            if (lineWidth > 0 && lineWidth + HorizontalSpacing + desired.Width > availableSize.Width)
            {
                width = Math.Max(width, lineWidth);
                height += lineHeight + VerticalSpacing;
                lineWidth = desired.Width;
                lineHeight = desired.Height;
            }
            else
            {
                lineWidth = lineWidth > 0 ? lineWidth + HorizontalSpacing + desired.Width : desired.Width;
                lineHeight = Math.Max(lineHeight, desired.Height);
            }
        }
        width = Math.Max(width, lineWidth);
        height += lineHeight;
        return new global::Windows.Foundation.Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
    {
        double x = 0, y = 0, lineHeight = 0;
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            global::Windows.Foundation.Size desired = child.DesiredSize;
            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }
            double childWidth = Math.Min(desired.Width, finalSize.Width);
            child.Arrange(new global::Windows.Foundation.Rect(x, y, childWidth, desired.Height));
            x += childWidth + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }
        return finalSize;
    }
}
