using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    /// the update steps (otherwise on screen only during a real update), and a title beside a status badge. Static sample
    /// controls only; nothing here reads, writes or starts anything.
    /// </summary>
    private static Border BuildLayoutSamples()
    {
        StackPanel samples = new() { Spacing = StewardSpacing.Medium };
        AutomationProperties.SetAutomationId(samples, "LayoutSamples");
        AutomationProperties.SetName(samples, "Layout samples");
        samples.Children.Add(SectionHeading("Layout samples (build check only)"));

        // The same label with and without an icon, in both button styles, on one action row as on Home.
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        actions.Children.Add(SampleButton("SampleButtonText", "Check for updates", primary: false));
        actions.Children.Add(SampleButton("SampleButtonIcon", StewardVisuals.IconLabel("\uE72C", "Check for updates", 16), primary: false));
        actions.Children.Add(SampleButton("SampleButtonPrimaryText", "Check for updates", primary: true));
        actions.Children.Add(SampleButton("SampleButtonPrimaryIcon", StewardVisuals.IconLabel("\uE72C", "Check for updates", 16), primary: true));
        samples.Children.Add(actions);

        samples.Children.Add(BuildJobStepIndicator(UpdateJobStep.Review));
        samples.Children.Add(BuildJobStepIndicator(UpdateJobStep.Verify));

        // A title beside a status badge, as on the update result card.
        StewardWrapPanel title = StewardVisuals.TitleRow();
        title.Children.Add(StewardVisuals.CapCentred(new TextBlock { Text = "Sample App 1.2.3", Style = AppStyle("StewardBodyStrongStyle") }));
        Border badge = StewardVisuals.Badge("Verified", "\uE73E", "green");
        badge.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center;
        title.Children.Add(badge);
        AutomationProperties.SetAutomationId(title, "SampleTitleRow");
        AutomationProperties.SetName(title, "Sample title row");
        samples.Children.Add(title);
        return Card(samples);
    }

    private static Button SampleButton(string id, object content, bool primary)
    {
        Button button = new() { Content = content };
        if (primary) button.Style = AppStyle("StewardPrimaryButtonStyle");
        AutomationProperties.SetAutomationId(button, id);
        return StewardVisuals.Named(button, "Check for updates");
    }
}
