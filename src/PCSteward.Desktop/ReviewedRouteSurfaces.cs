using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, FR-UPD-01, FR-HIS-01, FR-UI-03/04, TM-03/04/07/13; Task 50 admission obligation 5 (N6 review M1): thin rendering
/// of the portable view models (<see cref="ChocolateyReviewedRouteReviewViewModel"/>, <see cref="ReviewedUpdateHistoryListViewModel"/>).
/// It renders every display line the view models expose and binds the Confirm button's enabled state to
/// <see cref="ChocolateyReviewedRouteReviewViewModel.IsConfirmEnabled"/> (false today). It holds no logic, starts nothing and
/// has no dispatch path.
/// </summary>
internal static class ReviewedRouteSurfaces
{
    /// <summary>The entry point shown only on Chocolatey Community rows.</summary>
    internal static Button ChocolateyReviewEntryButton()
    {
        Button entry = new() { Content = ChocolateyReviewedRouteReviewViewModel.EntryText };
        AutomationProperties.SetName(entry, ChocolateyReviewedRouteReviewViewModel.EntryAutomationName);
        return entry;
    }

    /// <summary>The review dialog: every disclosure line, then every unavailability reason, before a Confirm button bound to the view model.</summary>
    internal static ContentDialog ChocolateyReviewDialog(XamlRoot root, ChocolateyReviewedRouteReviewViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        StackPanel content = new() { Spacing = 4 };
        foreach (ReviewedDisplayField field in model.Fields) content.Children.Add(Line(field));
        if (model.UnavailableFields.Count != 0)
        {
            TextBlock unavailableHeading = StewardVisuals.SectionHeading(ChocolateyReviewedRouteReviewViewModel.UnavailableHeading);
            unavailableHeading.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(unavailableHeading);
            foreach (ReviewedDisplayField field in model.UnavailableFields) content.Children.Add(Line(field));
        }
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            Title = model.Title,
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
            PrimaryButtonText = ChocolateyReviewedRouteReviewViewModel.ConfirmText,
            IsPrimaryButtonEnabled = model.IsConfirmEnabled,
            CloseButtonText = ChocolateyReviewedRouteReviewViewModel.CloseText,
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetName(dialog, model.Title);
        AutomationProperties.SetHelpText(dialog, model.ConfirmAutomationName);
        return dialog;
    }

    /// <summary>Replaces <paramref name="container"/>'s children with the history summary and one card per reviewed outcome.</summary>
    internal static void RenderHistory(StackPanel container, ReviewedUpdateHistoryListViewModel model)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(model);
        container.Children.Clear();
        container.Children.Add(new TextBlock { Text = model.Summary, Style = StewardVisuals.Style("StewardBodyStyle") });
        foreach (ReviewedUpdateHistoryItemViewModel item in model.Items)
        {
            StackPanel entry = new() { Spacing = 2 };
            entry.Children.Add(StewardVisuals.SectionHeading(item.Title, AutomationHeadingLevel.Level3));
            foreach (ReviewedDisplayField field in item.Fields) entry.Children.Add(Line(field));
            Border card = new() { Style = StewardVisuals.Style("StewardCardStyle"), Child = entry };
            AutomationProperties.SetName(card, item.AutomationName);
            container.Children.Add(card);
        }
    }

    private static Grid Line(ReviewedDisplayField field)
    {
        Grid line = StewardVisuals.EvidenceRow(field.Label, field.Text);
        AutomationProperties.SetName(line, field.AutomationName);
        return line;
    }
}
