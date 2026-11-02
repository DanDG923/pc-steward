using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 (owner decisions 1–5), ADR-030: thin rendering of
/// <see cref="ChocolateyClientUpdateViewModel"/> — the Settings card row and Installed apps detail entry point, and the
/// Chocolatey client update review dialog ("may be available" until admission). It renders every line the view model exposes and binds Confirm's enabled state to
/// <see cref="ChocolateyClientUpdateViewModel.IsConfirmEnabled"/> (always false while the route is not admitted). It holds no
/// logic, starts nothing and has no review, confirm or dispatch path.
/// </summary>
internal static class ChocolateyClientUpdateSurfaces
{
    /// <summary>The entry point; its accessible name starts with the visible label and carries the summary.</summary>
    internal static Button EntryButton(ChocolateyClientUpdateViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Button entry = new() { Content = ChocolateyClientUpdateViewModel.EntryText };
        AutomationProperties.SetName(entry, model.EntryAutomationName);
        return entry;
    }

    /// <summary>The review dialog: facts, then Chocolatey's setup side effects, then every unavailability reason, before Confirm.</summary>
    internal static ContentDialog ReviewDialog(XamlRoot root, ChocolateyClientUpdateViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        StackPanel content = new() { Spacing = 4 };
        TextBlock summary = new() { Text = model.Summary, Style = StewardVisuals.Style("StewardBodyStyle"), TextWrapping = TextWrapping.Wrap };
        content.Children.Add(summary);
        content.Children.Add(Heading(ChocolateyClientUpdateViewModel.FactsHeading));
        foreach (ReviewedDisplayField field in model.Fields) content.Children.Add(Line(field));
        content.Children.Add(Heading(ChocolateyClientUpdateViewModel.DisclosuresHeading));
        foreach (ReviewedDisplayField field in model.DisclosureFields) content.Children.Add(Line(field));
        if (model.UnavailableFields.Count != 0)
        {
            content.Children.Add(Heading(ChocolateyClientUpdateViewModel.UnavailableHeading));
            foreach (ReviewedDisplayField field in model.UnavailableFields) content.Children.Add(Line(field));
        }
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = model.Title,
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
            PrimaryButtonText = ChocolateyClientUpdateViewModel.ConfirmText,
            IsPrimaryButtonEnabled = model.IsConfirmEnabled,
            CloseButtonText = ChocolateyClientUpdateViewModel.CloseText,
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetName(dialog, model.Title);
        AutomationProperties.SetHelpText(dialog, model.ConfirmAutomationName);
        return dialog;
    }

    private static TextBlock Heading(string text)
    {
        TextBlock heading = StewardVisuals.SectionHeading(text, AutomationHeadingLevel.Level3);
        heading.Margin = new Thickness(0, 8, 0, 0);
        return heading;
    }

    private static Grid Line(ReviewedDisplayField field)
    {
        Grid line = StewardVisuals.EvidenceRow(field.Label, field.Text);
        AutomationProperties.SetName(line, field.AutomationName);
        return line;
    }
}
