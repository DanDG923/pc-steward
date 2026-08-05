using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;

namespace PCSteward.Desktop;

public sealed record StewardStatus(string Label, string Glyph, string Tone)
{
    public static StewardStatus From(string status) => status switch
    {
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.Current) => new("Up to date", "\uE73E", "success"),
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate) => new("Update ready to review", "\uE896", "information"),
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) => new("Needs your decision", "\uE7BA", "attention"),
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged) => new("Inventoried only", "\uE946", "neutral"),
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.Pinned) => new("Version pinned", "\uE718", "neutral"),
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.Ignored) => new("Ignored by you", "\uE74D", "neutral"),
        nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable) => new("Update evidence unavailable", "\uEA39", "blocked"),
        "Installed" => new("Installed · source not checked", "\uE946", "neutral"),
        _ => new("Status unavailable", "\uE783", "neutral")
    };
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

    public static Grid PageHeader(string title, string description, string? eyebrow = null, FrameworkElement? action = null)
    {
        Grid grid = new() { ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel copy = new() { Spacing = 5 };
        if (!string.IsNullOrWhiteSpace(eyebrow)) copy.Children.Add(new TextBlock { Text = eyebrow.ToUpperInvariant(), Style = Style("StewardEyebrowStyle") });
        copy.Children.Add(new TextBlock { Text = title, Style = Style("StewardPageTitleStyle") });
        copy.Children.Add(new TextBlock { Text = description, Style = Style("StewardBodyLargeStyle"), MaxWidth = 760 });
        grid.Children.Add(copy);
        if (action is not null)
        {
            action.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
        }
        return grid;
    }

    public static Button PrimaryButton(string label, string automationName)
    {
        Button button = new() { Content = label, Style = Style("StewardPrimaryButtonStyle") };
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    public static Border StatusBadge(string status, string? automationName = null)
    {
        StewardStatus presentation = StewardStatus.From(status);
        return Badge(presentation.Label, presentation.Glyph, presentation.Tone, automationName);
    }

    public static Border Badge(string label, string glyph, string tone = "neutral", string? automationName = null)
    {
        string foreground = tone switch
        {
            "success" => "StewardSuccessBrush",
            "attention" => "StewardWarningBrush",
            "blocked" => "StewardDangerBrush",
            "information" => "StewardBrandTextBrush",
            _ => "StewardSecondaryTextBrush"
        };
        string background = tone switch
        {
            "success" => "StewardSuccessSurfaceBrush",
            "attention" => "StewardWarningSurfaceBrush",
            "blocked" => "StewardDangerSurfaceBrush",
            "information" => "StewardBrandSubtleBrush",
            _ => "StewardSurfaceSubtleBrush"
        };
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 12, Foreground = Brush(foreground) });
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(foreground), TextWrapping = TextWrapping.Wrap });
        Border badge = new() { Style = Style("StewardStatusChipStyle"), Background = Brush(background), BorderBrush = Brush(foreground), Child = content };
        AutomationProperties.SetName(badge, automationName ?? label);
        return badge;
    }

    public static Border InformationBanner(string title, string message, string tone = "information")
    {
        string glyph = tone switch { "attention" => "\uE7BA", "blocked" => "\uEA39", "success" => "\uE73E", _ => "\uE946" };
        string foreground = tone switch { "attention" => "StewardWarningBrush", "blocked" => "StewardDangerBrush", "success" => "StewardSuccessBrush", _ => "StewardBrandTextBrush" };
        string background = tone switch { "attention" => "StewardWarningSurfaceBrush", "blocked" => "StewardDangerSurfaceBrush", "success" => "StewardSuccessSurfaceBrush", _ => "StewardBrandSubtleBrush" };
        Grid grid = new() { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18, Foreground = Brush(foreground), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) });
        StackPanel copy = new() { Spacing = 3 };
        copy.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(foreground), TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock { Text = message, Style = Style("StewardBodyStyle") });
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        Border banner = new() { Background = Brush(background), BorderBrush = Brush(foreground), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Child = grid };
        AutomationProperties.SetName(banner, $"{title}. {message}");
        return banner;
    }

    public static Border EmptyState(string glyph, string title, string message, Button? action = null)
    {
        StackPanel content = new() { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 560 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 30, Foreground = Brush("StewardBrandBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = title, Style = Style("StewardSectionTitleStyle"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock { Text = message, Style = Style("StewardBodyStyle"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        if (action is not null) { action.HorizontalAlignment = HorizontalAlignment.Center; content.Children.Add(action); }
        Border empty = new() { Style = Style("StewardCardStyle"), Padding = new Thickness(32), Child = content };
        AutomationProperties.SetName(empty, $"{title}. {message}");
        return empty;
    }

    public static Border MetricCard(int value, string label, string detail, string status)
    {
        StackPanel content = new() { Spacing = 5 };
        Grid heading = new() { ColumnSpacing = 8 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = value.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Style = Style("StewardDisplayStyle") });
        Border badge = StatusBadge(status);
        Grid.SetColumn(badge, 1);
        heading.Children.Add(badge);
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = label, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("StewardTextBrush") });
        content.Children.Add(new TextBlock { Text = detail, Style = Style("StewardCaptionStyle") });
        return new Border { Style = Style("StewardCardStyle"), Child = content };
    }

    public static Grid EvidenceRow(string label, string value)
    {
        Grid row = new() { ColumnSpacing = 16, Padding = new Thickness(0, 5, 0, 5) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, Style = Style("StewardCaptionStyle") });
        TextBlock valueText = new() { Text = value, Foreground = Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(valueText);
        AutomationProperties.SetName(row, $"{label}: {value}");
        return row;
    }

    public static Border ActivityRow(DateTimeOffset occurredAt, string glyph, string title, string detail)
    {
        Grid row = new() { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, Foreground = Brush("StewardBrandBrush"), Margin = new Thickness(0, 2, 0, 0) });
        StackPanel copy = new() { Spacing = 2 };
        copy.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap });
        copy.Children.Add(new TextBlock { Text = detail, Style = Style("StewardCaptionStyle") });
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        TextBlock time = new() { Text = occurredAt.ToLocalTime().ToString("d MMM yyyy · HH:mm", System.Globalization.CultureInfo.CurrentCulture), Style = Style("StewardCaptionStyle"), Margin = new Thickness(12, 1, 0, 0) };
        Grid.SetColumn(time, 2);
        row.Children.Add(time);
        Border result = new() { BorderBrush = Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 10, 0, 10), Child = row };
        AutomationProperties.SetName(result, $"{title}. {detail}. {time.Text}");
        return result;
    }
}
