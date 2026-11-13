using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;

namespace PCSteward.Desktop;

public sealed partial class MainWindow
{
    /// <summary>The environment variable the Windows launch check sets so the About page also shows <see cref="BuildLayoutSamples"/>.</summary>
    internal const string LayoutSamplesVariable = "PCSTEWARD_LAYOUT_SAMPLES";

    /// <summary>True only when the launch check asked for the layout samples; never in a normal start.</summary>
    private static bool LayoutSamplesRequested => Environment.GetEnvironmentVariable(LayoutSamplesVariable) == "1";

    /// <summary>
    /// Owner reports 2026-11-13 (releases 0.1.14 and 0.1.15): alignment faults that were fixed by reasoning about layout boxes
    /// came back on a real display. This card holds the controls those reports were about, built by the same code the pages
    /// use, so the launch check can measure their drawn pixels on a real Windows build: a text button beside an icon button,
    /// and the update steps, which are otherwise only on screen during a real update. Static sample controls only; nothing
    /// here reads, writes or starts anything.
    /// </summary>
    private static Border BuildLayoutSamples()
    {
        StackPanel samples = new() { Spacing = StewardSpacing.Medium };
        AutomationProperties.SetAutomationId(samples, "LayoutSamples");
        AutomationProperties.SetName(samples, "Layout samples");
        samples.Children.Add(SectionHeading("Layout samples (build check only)"));

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        actions.Children.Add(SampleButton("SampleButtonPrimaryText", "Update Sample Desktop Runtime", "Update Sample Desktop Runtime", primary: true));
        actions.Children.Add(SampleButton("SampleButtonIcon", StewardVisuals.IconLabel("", "Check for updates", 16), "Check for updates", primary: false));
        actions.Children.Add(SampleButton("SampleButtonPlainText", "Check for updates", "Check for updates", primary: false));
        actions.Children.Add(SampleButton("SampleButtonIconPrimary", StewardVisuals.IconLabel("", "Check for updates", 16), "Check for updates", primary: true));
        samples.Children.Add(actions);

        // Diagnostic variants: each isolates one difference between a text button and an icon button.
        StewardWrapPanel variants = StewardVisuals.ActionRow();
        StackPanel v1 = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        v1.Children.Add(new FontIcon { Glyph = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        v1.Children.Add(new TextBlock { Text = "Check for updates", VerticalAlignment = VerticalAlignment.Center });
        variants.Children.Add(SampleButton("SampleButtonV1NoMargin", v1, "Check for updates", primary: false));
        StackPanel v2 = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small, MinHeight = 20 };
        v2.Children.Add(new FontIcon { Glyph = "", FontSize = 16, Margin = StewardVisuals.IconLabelIconMargin, VerticalAlignment = VerticalAlignment.Center });
        v2.Children.Add(new TextBlock { Text = "Check for updates", TextLineBounds = TextLineBounds.Tight, LineHeight = 0, VerticalAlignment = VerticalAlignment.Center });
        variants.Children.Add(SampleButton("SampleButtonV2TightLabel", v2, "Check for updates", primary: false));
        StackPanel v3 = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        v3.Children.Add(new FontIcon { Glyph = "", FontSize = 16, Margin = StewardVisuals.IconLabelIconMargin, VerticalAlignment = VerticalAlignment.Center });
        v3.Children.Add(new ContentPresenter { Content = "Check for updates", VerticalAlignment = VerticalAlignment.Center });
        variants.Children.Add(SampleButton("SampleButtonV3PresenterLabel", v3, "Check for updates", primary: false));
        StackPanel v4 = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        v4.Children.Add(new TextBlock { Text = "Check for updates", VerticalAlignment = VerticalAlignment.Center });
        variants.Children.Add(SampleButton("SampleButtonV4LabelOnly", v4, "Check for updates", primary: false));
        StackPanel v5 = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        v5.Children.Add(new FontIcon { Glyph = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        variants.Children.Add(SampleButton("SampleButtonV5IconOnly", v5, "Icon only", primary: false));
        StackPanel v6 = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        v6.Children.Add(new FontIcon { Glyph = "", FontSize = 16, Margin = StewardVisuals.IconLabelIconMargin, VerticalAlignment = VerticalAlignment.Center });
        variants.Children.Add(SampleButton("SampleButtonV6IconOnlyMargin", v6, "Icon only, margin", primary: false));
        variants.Children.Add(SampleButton("SampleButtonV7TextBlock", new TextBlock { Text = "Check for updates" }, "Check for updates", primary: false));
        samples.Children.Add(variants);

        samples.Children.Add(BuildJobStepIndicator(UpdateJobStep.Review));
        samples.Children.Add(BuildJobStepIndicator(UpdateJobStep.Verify));

        StackPanel dots = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Medium };
        dots.Children.Add(SampleDot("SampleDotD1Current", 2, new TextBlock { Text = "1", TextLineBounds = TextLineBounds.Tight, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }));
        dots.Children.Add(SampleDot("SampleDotD2Plain", 1, new TextBlock { Text = "2", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }));
        Grid d3 = new();
        d3.Children.Add(new TextBlock { Text = "3", Style = AppStyle("StewardCaptionStyle"), TextLineBounds = TextLineBounds.Tight, LineHeight = 0, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        dots.Children.Add(SampleDot("SampleDotD3BadgeLike", 1, d3));
        dots.Children.Add(SampleDot("SampleDotD4FamilyOnly", 1, new TextBlock { Text = "1", TextLineBounds = TextLineBounds.Tight, LineHeight = 0, FontFamily = new FontFamily("Segoe UI Variable Text"), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }));
        Grid d5 = new();
        d5.Children.Add(new TextBlock { Text = "2", TextLineBounds = TextLineBounds.Tight, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        dots.Children.Add(SampleDot("SampleDotD5GridOnly", 1, d5));
        dots.Children.Add(SampleDot("SampleDotD6WrapOnly", 1, new TextBlock { Text = "3", TextLineBounds = TextLineBounds.Tight, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }));
        dots.Children.Add(SampleDot("SampleDotD7Tick", 1, new FontIcon { Glyph = "", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }));
        samples.Children.Add(dots);
        return Card(samples);
    }

    private static Button SampleButton(string id, object content, string label, bool primary)
    {
        Button button = new() { Content = content };
        if (primary) button.Style = AppStyle("StewardPrimaryButtonStyle");
        AutomationProperties.SetAutomationId(button, id);
        return StewardVisuals.Named(button, label);
    }

    private static Border SampleDot(string id, double border, UIElement child)
    {
        Border dot = new()
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(border),
            BorderBrush = StewardVisuals.Brush("StewardBorderStrongBrush"),
            Background = StewardVisuals.Brush("StewardSurfaceBrush"),
            Child = child
        };
        AutomationProperties.SetAutomationId(dot, id);
        AutomationProperties.SetName(dot, id);
        return dot;
    }
}
