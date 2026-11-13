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
    /// <summary>The page column's padding (left 28, top 12, right 40, bottom 40), mirroring <c>StewardPagePadding</c>.</summary>
    public static readonly Thickness PagePadding = new(28, 12, 40, 40);
}

public sealed class ApplicationListItem
{
    private ApplicationListItem(InventoryRow row, bool showSource)
    {
        Row = row;
        Name = row.Name;
        Publisher = string.IsNullOrWhiteSpace(row.Publisher) || string.Equals(row.Publisher, "Unknown", StringComparison.OrdinalIgnoreCase) ? "Unknown publisher" : row.Publisher;
        bool versionKnown = !string.IsNullOrWhiteSpace(row.Version) && !string.Equals(row.Version, "Unknown", StringComparison.OrdinalIgnoreCase);
        Version = versionKnown ? row.Version : "Unknown version";
        // UI polish phase 4 (approved target T-Installed): the Version column shows a dash for an unknown version; the accessible
        // summary still says "Unknown version" in words.
        VersionText = versionKnown ? row.Version : "—";
        ScopeLabel = StewardPresentation.Scope(row.Scope);
        ScopeAndProvider = $"{ScopeLabel} · {StewardPresentation.Provider(row.Provider)}";
        PublisherAndProvider = InstalledAppsFilters.PublisherAndSource(row.Publisher, StewardPresentation.Provider(row.Provider), showSource);
        StewardStatus status = StewardStatus.From(row.Status);
        StatusLabel = status.Label;
        StatusGlyph = status.Glyph;
        (StatusForeground, StatusBackground) = StewardVisuals.ToneBrushes(status.Tone);
        // A neutral status ("No update source", "Pinned", "Source not checked" and so on) is a fact, not a warning, so it is plain
        // secondary text; only statuses that ask for a look (an update, a review, a source problem, current) get a tinted chip.
        bool calm = status.Tone == "neutral";
        ChipVisibility = calm ? Visibility.Collapsed : Visibility.Visible;
        PlainStatusVisibility = calm ? Visibility.Visible : Visibility.Collapsed;
        Initials = StewardPresentation.Initials(row.Name);
        AccessibleSummary = $"{Name}, {Publisher}, version {Version}, {StatusLabel}, {ScopeAndProvider}";
    }

    public InventoryRow Row { get; }
    public string Name { get; }
    public string Publisher { get; }
    public string Version { get; }
    public string ScopeAndProvider { get; }
    /// <summary>The version shown in the Version column ("—" when unknown).</summary>
    public string VersionText { get; }
    /// <summary>Who the app is installed for ("All users" or "Current user").</summary>
    public string ScopeLabel { get; }
    /// <summary>The row's second line: the publisher, plus where the record came from when another row shares the name.</summary>
    public string PublisherAndProvider { get; }
    /// <summary>Visible for statuses that ask for a look; neutral statuses show <see cref="PlainStatusVisibility"/> text instead.</summary>
    public Visibility ChipVisibility { get; }
    public Visibility PlainStatusVisibility { get; }
    public string StatusLabel { get; }
    public string StatusGlyph { get; }
    public Brush StatusForeground { get; }
    public Brush StatusBackground { get; }
    public string Initials { get; }
    public string AccessibleSummary { get; }

    public static ApplicationListItem From(InventoryRow row, bool showSource = true) => new(row, showSource);
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
        // and on the same left edge, as in Windows Settings. UI polish phase 4: a page may have no description (About).
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardBodyStyle") });
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
    /// UI polish Phase 1 (plan root cause 2): keeps the hosted page column left aligned beside the navigation. Phase 5 (owner
    /// feedback on a maximised window): the column has no maximum width, so it always fills the host from the navigation to the
    /// window's right edge. Call it after setting the host's content; the host re-fits the column whenever it is resized.
    /// </summary>
    public static void FitPageColumn(ScrollViewer host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Content is FrameworkElement page && host.ActualWidth > 0) page.Width = host.ActualWidth;
    }

    /// <summary>Re-fits the hosted page column (<see cref="FitPageColumn"/>) whenever <paramref name="host"/> is resized.</summary>
    public static void KeepPageColumnFitted(ScrollViewer host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.SizeChanged += (_, _) => FitPageColumn(host);
    }

    /// <summary>
    /// UI polish phase 5 (owner feedback on a maximised window): grows the bounded, virtualized <paramref name="list"/> inside
    /// <paramref name="card"/> so the card reaches the bottom of the page host's viewport, keeping whatever follows the card on
    /// the page (such as a footnote) in view. The list never drops below <paramref name="minimumHeight"/>, so on a short window
    /// the page scrolls instead. It re-fits when the window or the content above the card changes size.
    /// UI review 2026-11-13 (fault 1): the page is top aligned so its height is the height of its content. Left to stretch, a
    /// page shorter than the window reports the window's height, the blank space counts as content below the card, and the list
    /// never grows past its minimum (five rows on a maximised 4K window).
    /// </summary>
    public static void FillViewportHeight(StackPanel page, FrameworkElement card, ListViewBase list, double minimumHeight)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(list);
        page.VerticalAlignment = VerticalAlignment.Top;
        list.MaxHeight = minimumHeight;
        ScrollViewer? host = null;
        void Fit()
        {
            if (host is null || host.ViewportHeight <= 0 || card.Visibility != Visibility.Visible || card.ActualHeight <= 0) return;
            double top = card.TransformToVisual(page).TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y;
            double below = page.ActualHeight - top - card.ActualHeight;
            double chrome = card.ActualHeight - list.ActualHeight;
            double wanted = ViewportFill.ListHeight(host.ViewportHeight, top, below, chrome, minimumHeight);
            if (Math.Abs(wanted - list.MaxHeight) >= 1) list.MaxHeight = wanted;
        }
        void Resized(object sender, SizeChangedEventArgs args) => Fit();
        page.Loaded += (_, _) =>
        {
            host = null;
            for (DependencyObject? node = VisualTreeHelper.GetParent(page); node is not null && host is null; node = VisualTreeHelper.GetParent(node)) host = node as ScrollViewer;
            if (host is not null) host.SizeChanged += Resized;
            Fit();
        };
        page.Unloaded += (_, _) =>
        {
            if (host is not null) host.SizeChanged -= Resized;
            host = null;
        };
        page.SizeChanged += Resized;
    }

    /// <summary>
    /// Owner feedback on 0.1.1 (a maximised window still left most of the height empty under an empty state): grows
    /// <paramref name="card"/> itself so it reaches the bottom of the page host's viewport, keeping whatever follows it on the page
    /// (such as a footnote) in view. Only <see cref="FrameworkElement.MinHeight"/> changes, so on a short window the card keeps
    /// its natural height and the page scrolls as before. It re-fits when the window or the content above the card changes size.
    /// The page is top aligned for the same reason as the list overload: a stretched page reports the window's height.
    /// </summary>
    public static void FillViewportHeight(StackPanel page, FrameworkElement card)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(card);
        page.VerticalAlignment = VerticalAlignment.Top;
        ScrollViewer? host = null;
        void Fit()
        {
            if (host is null || host.ViewportHeight <= 0 || card.Visibility != Visibility.Visible || card.ActualHeight <= 0) return;
            double top = card.TransformToVisual(page).TransformPoint(new global::Windows.Foundation.Point(0, 0)).Y;
            double below = page.ActualHeight - top - card.ActualHeight;
            double wanted = ViewportFill.CardHeight(host.ViewportHeight, top, below);
            if (Math.Abs(wanted - card.MinHeight) >= 1) card.MinHeight = wanted;
        }
        void Resized(object sender, SizeChangedEventArgs args) => Fit();
        page.Loaded += (_, _) =>
        {
            host = null;
            for (DependencyObject? node = VisualTreeHelper.GetParent(page); node is not null && host is null; node = VisualTreeHelper.GetParent(node)) host = node as ScrollViewer;
            if (host is not null) host.SizeChanged += Resized;
            Fit();
        };
        page.Unloaded += (_, _) =>
        {
            if (host is not null) host.SizeChanged -= Resized;
            host = null;
        };
        page.SizeChanged += Resized;
    }

    /// <summary>True for a card made by <see cref="EmptyState"/>; the page host grows the first one on a page to the window's bottom.</summary>
    public static bool IsEmptyState(UIElement element) => element is Border border && EmptyStates.TryGetValue(border, out _);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Border, object> EmptyStates = new();

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

    /// <summary>
    /// Audit P-M9: a row of actions that wraps onto more lines instead of clipping at narrow widths or large text. Owner report
    /// 2026-11-13: the controls on one line are the same height, so a button with an icon does not sit lower than its neighbour.
    /// </summary>
    public static StewardWrapPanel ActionRow(double spacing = StewardSpacing.Small) => new() { HorizontalSpacing = spacing, VerticalSpacing = spacing, LevelLines = true };

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
        // Colour option 2 (owner choice 2026-11-11): decorative hues for icon wells that carry no status (Settings-style).
        "blue" => (Brush("StewardHueBlueBrush"), Brush("StewardHueBlueSurfaceBrush")),
        "teal" => (Brush("StewardHueTealBrush"), Brush("StewardHueTealSurfaceBrush")),
        "amber" => (Brush("StewardHueAmberBrush"), Brush("StewardHueAmberSurfaceBrush")),
        "purple" => (Brush("StewardHuePurpleBrush"), Brush("StewardHuePurpleSurfaceBrush")),
        "pink" => (Brush("StewardHuePinkBrush"), Brush("StewardHuePinkSurfaceBrush")),
        "green" => (Brush("StewardHueGreenBrush"), Brush("StewardHueGreenSurfaceBrush")),
        _ => (Brush("StewardSecondaryTextBrush"), Brush("StewardIconWellBrush"))
    };

    /// <summary>
    /// Colour option 2: a card background tinted with a tone's surface colour at 60% opacity, so the window's Mica Alt tint shows
    /// through and the full-strength icon well stays distinct. Neutral keeps the standard card fill; in high contrast every
    /// surface is the system window colour, so the card stays opaque and system-coloured.
    /// </summary>
    public static Brush ToneTint(string tone)
    {
        if (tone == "neutral") return Brush("StewardSurfaceBrush");
        Windows.UI.Color surface = ((SolidColorBrush)ToneBrushes(tone).Background).Color;
        return new SolidColorBrush(surface) { Opacity = 0.6 };
    }

    public static Border Badge(string label, string glyph, string tone = "neutral", string? automationName = null)
    {
        (Brush foreground, Brush background) = ToneBrushes(tone);
        // UI polish Phase 1: a tinted pill (StewardStatusChipStyle); the glyph and label carry the meaning, never colour alone.
        // UI review 2026-11-13 (item 18): the label is in a column that takes the width left over, so a long label wraps onto a
        // second line when space is tight instead of being clipped (a side-by-side stack never wraps its text).
        Grid content = new() { ColumnSpacing = 6 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        TextBlock text = new() { Text = label, Style = Style("StewardCaptionStyle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = foreground, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
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
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardDescriptionStyle") });
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

    /// <summary>
    /// UI polish phase 2 (approved Home targets): the status card's 56 epx round icon well, tinted with a status tone's surface and
    /// glyph colour. Decorative: the sentence beside it carries the meaning.
    /// </summary>
    public static Border StatusWell(string glyph, string tone = "information")
    {
        (Brush foreground, Brush background) = tone == "neutral" ? (Brush("StewardTextBrush"), Brush("StewardIconWellBrush")) : ToneBrushes(tone);
        Border well = new()
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            Background = background,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new FontIcon { Glyph = glyph, FontSize = 28, Foreground = foreground, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        AutomationProperties.SetAccessibilityView(well, AccessibilityView.Raw);
        return well;
    }

    /// <summary>
    /// UI polish phase 2 (approved Home targets): lays <paramref name="items"/> out in as many equal columns as fit at
    /// <paramref name="minColumnWidth"/>, from one up to <paramref name="maxColumns"/>, so rows never clip at narrow widths or
    /// large text sizes and a wide window uses its width (phase 5). Items keep their order, reading left to right then down.
    /// </summary>
    public static Grid ResponsiveColumns(FrameworkElement[] items, double minColumnWidth, double spacing, int maxColumns = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxColumns, 1);
        Grid grid = new() { RowSpacing = spacing };
        foreach (FrameworkElement item in items) grid.Children.Add(item);
        int columns = 0;
        void Place(double width)
        {
            int wanted = width > 0 ? (int)Math.Clamp(Math.Floor((width + spacing) / (minColumnWidth + spacing)), 1, maxColumns) : maxColumns;
            if (wanted == columns) return;
            columns = wanted;
            // Only the columns in use exist, so no empty column adds a stray gap.
            grid.ColumnDefinitions.Clear();
            for (int column = 0; column < wanted; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnSpacing = wanted > 1 ? spacing : 0;
            grid.RowDefinitions.Clear();
            for (int row = 0; row < (items.Length + wanted - 1) / wanted; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int index = 0; index < items.Length; index++)
            {
                Grid.SetColumn(items[index], index % wanted);
                Grid.SetRow(items[index], index / wanted);
            }
        }
        Place(0);
        grid.SizeChanged += (_, args) => Place(args.NewSize.Width);
        return grid;
    }

    /// <summary>A small icon and a short line in the secondary text colour, such as the status card's facts and Home's footer.</summary>
    public static Grid IconLine(string glyph, string text, string tone = "neutral", string? styleKey = null)
    {
        Brush foreground = tone == "neutral" ? Brush("StewardSecondaryTextBrush") : ToneBrushes(tone).Foreground;
        // A Grid, not a horizontal StackPanel, so a long line wraps beside its icon instead of clipping.
        Grid line = new() { ColumnSpacing = 6 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = foreground, VerticalAlignment = VerticalAlignment.Center });
        TextBlock copy = new() { Text = text, Style = Style(styleKey ?? "StewardBodyStyle"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(copy, 1);
        line.Children.Add(copy);
        return line;
    }

    /// <summary>
    /// UI polish phase 3 (approved targets T-Updates-Ready and T-Verify): one card holding a list of rows, separated by thin dividers,
    /// as in Windows Settings lists. Each row keeps its own controls and names; the card only lays them out.
    /// </summary>
    public static Border ListCard(IReadOnlyList<FrameworkElement> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        StackPanel list = new();
        for (int index = 0; index < rows.Count; index++)
        {
            list.Children.Add(new Border
            {
                MinHeight = 56,
                Padding = new Thickness(StewardSpacing.Large, StewardSpacing.Small, StewardSpacing.Large, StewardSpacing.Small),
                BorderBrush = Brush("StewardBorderBrush"),
                BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0),
                Child = rows[index]
            });
        }
        return new Border { Style = Style("StewardCardStyle"), Padding = new Thickness(0), Child = list };
    }

    /// <summary>
    /// UI polish phase 3 (approved target T-Updates-Ready): a 32 epx rounded tile with an app's initials. Decorative: the app's name is
    /// always shown beside it.
    /// </summary>
    public static Border Avatar(string initials)
    {
        Border avatar = new()
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = Brush("StewardIconWellBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = initials, Style = Style("StewardCaptionStyle"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("StewardTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        AutomationProperties.SetAccessibilityView(avatar, AccessibilityView.Raw);
        return avatar;
    }

    /// <summary>
    /// UI review 2026-11-13 (item 20; ADR-035): an app's own icon, the size of the tile it replaces. Decorative, like the tile:
    /// it is kept out of what a screen reader reads, because the app's name is beside it.
    /// </summary>
    public static Image AppIconImage(ImageSource icon)
    {
        Image image = new() { Source = icon, Width = AppIconRules.TileSize, Height = AppIconRules.TileSize, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(image, AccessibilityView.Raw);
        AutomationProperties.SetAutomationId(image, "AppIcon");
        return image;
    }

    /// <summary>
    /// UI polish phase 3 (approved target T-Review): a Settings-style expander row — an icon well, a title and one-line description
    /// in the header, and <paramref name="content"/> when expanded. Collapsed by default; the expander's name is the title and its
    /// help text the description, so the header reads the same to a screen reader as it looks.
    /// </summary>
    public static Expander ExpanderRow(string glyph, string title, string? description, FrameworkElement content, string tone = "neutral")
    {
        Grid header = new() { ColumnSpacing = StewardSpacing.Large, Padding = new Thickness(0, StewardSpacing.Small, 0, StewardSpacing.Small) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(IconWell(glyph, tone));
        StackPanel copy = RowCopy(title, description);
        Grid.SetColumn(copy, 1);
        header.Children.Add(copy);
        Expander expander = new()
        {
            Header = header,
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(expander, title);
        if (!string.IsNullOrWhiteSpace(description)) AutomationProperties.SetHelpText(expander, description);
        return expander;
    }

    /// <summary>
    /// UI polish phase 4 (approved target T-Decisions): a calm empty state — a 56 epx round neutral icon well, a Subtitle heading,
    /// one sentence and an optional action, centred in a card.
    /// </summary>
    public static Border EmptyState(string glyph, string title, string message, Button? action = null, Button? secondary = null)
    {
        // Centred both ways, so the message stays in the middle when the page host grows the card to the window's bottom.
        StackPanel content = new() { Spacing = StewardSpacing.Small, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 560 };
        Border well = StatusWell(glyph, "neutral");
        well.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(well);
        TextBlock heading = SectionHeading(title);
        heading.Style = Style("StewardSubtitleStyle");
        heading.Margin = new Thickness(0, StewardSpacing.XSmall, 0, 0);
        heading.TextAlignment = TextAlignment.Center;
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = message, Style = Style("StewardBodyStyle"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        if (action is not null && secondary is not null)
        {
            // Two choices side by side (the guided first run's question); they wrap onto two lines when the card is narrow.
            StewardWrapPanel choices = ActionRow();
            choices.HorizontalAlignment = HorizontalAlignment.Center;
            choices.Children.Add(action);
            choices.Children.Add(secondary);
            content.Children.Add(choices);
        }
        else if (action is not null) { action.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(action); }
        Border empty = new() { Style = Style("StewardCardStyle"), Padding = new Thickness(32), Child = content };
        AutomationProperties.SetName(empty, $"{title}. {message}");
        EmptyStates.AddOrUpdate(empty, title);
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
        content.Children.Add(new TextBlock { Text = detail, Style = Style("StewardDescriptionStyle") });
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
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
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
        if (!string.IsNullOrWhiteSpace(description)) copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
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
        if (!string.IsNullOrWhiteSpace(detail)) copy.Children.Add(new TextBlock { Text = detail, Style = Style("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        TextBlock time = new() { Text = when, Style = Style("StewardCaptionStyle"), Margin = new Thickness(12, 1, 0, 0) };
        Grid.SetColumn(time, 2);
        row.Children.Add(time);
        Border result = new() { BorderBrush = Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = row };
        AutomationProperties.SetName(result, $"{title}. {detail}. {time.Text}");
        return result;
    }

    /// <summary>
    /// UI polish phase 4 (approved targets T-Installed and T-Activity): one saved-filter tab — the label, its count in a quieter
    /// colour and, when selected, Body Strong text over a short accent bar, as in the Windows SelectorBar. The bar is not the only
    /// signal: the selected tab's text is bold and the page sets its "Selected" item status. The caller names the button.
    /// </summary>
    public static Button FilterTab(string label, int count, bool selected)
    {
        StackPanel text = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.XSmall };
        text.Children.Add(new TextBlock { Text = label, Style = Style(selected ? "StewardBodyStrongStyle" : "StewardBodyStyle"), TextWrapping = TextWrapping.NoWrap });
        text.Children.Add(new TextBlock { Text = count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Style = Style("StewardBodyStyle"), Foreground = Brush("StewardTertiaryTextBrush"), TextWrapping = TextWrapping.NoWrap });
        Grid content = new() { RowSpacing = 6 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        content.Children.Add(text);
        Border indicator = new()
        {
            Width = 16,
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = Brush("StewardBrandBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = selected ? Visibility.Visible : Visibility.Collapsed
        };
        Grid.SetRow(indicator, 1);
        content.Children.Add(indicator);
        return new Button
        {
            Content = content,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(StewardSpacing.XSmall, StewardSpacing.Small, StewardSpacing.XSmall, 0)
        };
    }

    /// <summary>
    /// A button's content: a small icon and its label, side by side (Check for updates, Scan again, Copy diagnostic, Filters).
    /// Owner report 2026-11-13 (release 0.1.14): an icon is laid out in a box taller than the icon itself, taller than a line
    /// of text at 16, so "Check for updates" was two units taller than a button with text only and its label sat above the
    /// middle. The icon gives that spare room back (<see cref="IconLabelIconMargin"/>), so the label alone sets the height, as
    /// in any other button, and the icon and the label are centred on each other.
    /// </summary>
    public static StackPanel IconLabel(string glyph, string label, double iconSize = 14)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = iconSize, Margin = IconLabelIconMargin, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    /// <summary>The same amount off the top and the bottom, so the icon stays where it is and only stops adding height.</summary>
    public static readonly Thickness IconLabelIconMargin = new(0, -4, 0, -4);

    /// <summary>
    /// A row of <see cref="FilterTab"/> buttons over a thin divider; it wraps instead of clipping at narrow widths.
    /// UI review 2026-11-13 (item 18): each one is announced as a tab with its place in the row ("tab, 2 of 5"), as the Windows
    /// selector control would. They stay the app's own buttons because that control does not wrap at narrow widths or large text.
    /// </summary>
    public static Border FilterBar(IEnumerable<Button> tabs, string accessibleName)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        StewardWrapPanel row = new() { HorizontalSpacing = StewardSpacing.Large, VerticalSpacing = StewardSpacing.XSmall };
        Button[] all = [.. tabs];
        for (int index = 0; index < all.Length; index++)
        {
            AutomationProperties.SetLocalizedControlType(all[index], "tab");
            AutomationProperties.SetPositionInSet(all[index], index + 1);
            AutomationProperties.SetSizeOfSet(all[index], all.Length);
            row.Children.Add(all[index]);
        }
        AutomationProperties.SetName(row, accessibleName);
        return new Border { BorderBrush = Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    /// <summary>
    /// UI polish phase 4 (approved targets): a page's quiet footnote — a small icon and one Caption line in the secondary colour,
    /// such as "No update source isn't a safety warning".
    /// </summary>
    public static Grid FootNote(string glyph, string text)
    {
        Grid line = IconLine(glyph, text, styleKey: "StewardDescriptionStyle");
        foreach (TextBlock copy in line.Children.OfType<TextBlock>()) copy.Foreground = Brush("StewardSecondaryTextBrush");
        return line;
    }

    /// <summary>
    /// UI polish phase 4 (approved target T-Activity): a list-card row — a tinted icon well, a title and one-line description, and
    /// optional trailing content (a time, a button). The caller names it; a Grid has no automation peer of its own.
    /// </summary>
    public static Grid ListRow(FrameworkElement leading, string title, string? description, FrameworkElement? trailing = null)
    {
        Grid row = new() { ColumnSpacing = StewardSpacing.Large, MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        leading.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(leading);
        StackPanel copy = RowCopy(title, description);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
        }
        return row;
    }
}

/// <summary>
/// Audit P-M9 (AC-UX best effort, research §2 item 7): a minimal wrapping panel for action and filter rows. Children keep their
/// natural size and move to the next line when the row runs out of width, so nothing is clipped at narrow widths or at 150–225%
/// text scaling. Standard WinUI only (no Toolkit dependency); it lays out and does nothing else.
/// UI review 2026-11-13 (fault 2): the line breaks are decided once, when the row is measured (<see cref="WrapLayout"/>), and
/// arranging reuses them. Deciding again while arranging stacked the last button under the first on a 150% display.
/// </summary>
public sealed class StewardWrapPanel : Panel
{
    public double HorizontalSpacing { get; set; } = StewardSpacing.Small;
    public double VerticalSpacing { get; set; } = StewardSpacing.Small;
    /// <summary>
    /// Owner report 2026-11-13: true for a row of buttons and other controls, so everything on one line is as tall as the
    /// tallest and the edges are level. A button with an icon is a little taller than one without.
    /// </summary>
    public bool LevelLines { get; set; }

    private IReadOnlyList<int> measuredLines = [];

    protected override global::Windows.Foundation.Size MeasureOverride(global::Windows.Foundation.Size availableSize)
    {
        List<WrapLayout.Item> items = [];
        foreach (UIElement child in Children)
        {
            child.Measure(new global::Windows.Foundation.Size(availableSize.Width, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            items.Add(new WrapLayout.Item(child.DesiredSize.Width, child.DesiredSize.Height));
        }
        WrapLayout.Measured measured = WrapLayout.Measure(items, availableSize.Width, HorizontalSpacing, VerticalSpacing);
        measuredLines = measured.Lines;
        return new global::Windows.Foundation.Size(measured.Width, measured.Height);
    }

    protected override global::Windows.Foundation.Size ArrangeOverride(global::Windows.Foundation.Size finalSize)
    {
        List<UIElement> visible = [];
        List<WrapLayout.Item> items = [];
        foreach (UIElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            visible.Add(child);
            items.Add(new WrapLayout.Item(child.DesiredSize.Width, child.DesiredSize.Height));
        }
        // Arrange always follows a measure of the same children; if it ever doesn't, measure the lines here rather than guess.
        IReadOnlyList<int> lines = measuredLines.Count == items.Count
            ? measuredLines
            : WrapLayout.Measure(items, finalSize.Width, HorizontalSpacing, VerticalSpacing).Lines;
        IReadOnlyList<WrapLayout.Slot> slots = WrapLayout.Arrange(items, lines, finalSize.Width, HorizontalSpacing, VerticalSpacing, LevelLines);
        for (int index = 0; index < visible.Count; index++)
        {
            WrapLayout.Slot slot = slots[index];
            visible[index].Arrange(new global::Windows.Foundation.Rect(slot.X, slot.Y, slot.Width, slot.Height));
        }
        return finalSize;
    }
}
