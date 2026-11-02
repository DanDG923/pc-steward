using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 (owner decisions 1–5), ADR-030: thin rendering of
/// <see cref="ChocolateyClientUpdateViewModel"/> — the Settings card row and Installed apps detail entry point, and the
/// Chocolatey client update review dialog (opened as "Checking", then re-rendered with the installed-client check's result; an offer is
/// "not yet enabled" until admission). It renders every line the view model exposes and binds Confirm's enabled state to
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
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            PrimaryButtonText = ChocolateyClientUpdateViewModel.ConfirmText,
            CloseButtonText = ChocolateyClientUpdateViewModel.CloseText,
            DefaultButton = ContentDialogButton.Close,
        };
        Render(dialog, model);
        return dialog;
    }

    /// <summary>
    /// Renders <paramref name="model"/> into the dialog (when it opens, and again when the installed-client check ends): title,
    /// every line, and Confirm's enabled state from <see cref="ChocolateyClientUpdateViewModel.IsConfirmEnabled"/> only. The
    /// re-render keeps the dialog's ScrollViewer, its summary and its scroll position and replaces only the lines below the
    /// summary; the summary is a polite live region whose change is announced, so the check's result reaches a screen reader
    /// without moving the reader to the top (independent review L4; AC-UX best effort, ADR-021).
    /// </summary>
    internal static void Render(ContentDialog dialog, ChocolateyClientUpdateViewModel model)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(model);
        if (dialog.Content is ScrollViewer { Content: StackPanel { Children.Count: > 0 } rendered } host && rendered.Children[0] is TextBlock liveSummary)
        {
            double offset = host.VerticalOffset;
            while (rendered.Children.Count > 1) rendered.Children.RemoveAt(rendered.Children.Count - 1);
            AddLines(rendered, model);
            StewardVisuals.SetLiveText(liveSummary, model.Summary, announce: true);
            host.UpdateLayout();
            host.ChangeView(null, offset, null, true);
        }
        else
        {
            StackPanel content = new() { Spacing = 4 };
            TextBlock summary = new() { Text = model.Summary, Style = StewardVisuals.Style("StewardBodyStyle"), TextWrapping = TextWrapping.Wrap };
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
            content.Children.Add(summary);
            AddLines(content, model);
            dialog.Content = new ScrollViewer { Content = content, MaxHeight = 520 };
        }
        dialog.Title = model.Title;
        dialog.IsPrimaryButtonEnabled = model.IsConfirmEnabled;
        AutomationProperties.SetName(dialog, model.Title);
        AutomationProperties.SetHelpText(dialog, model.ConfirmAutomationName);
    }

    // Every line below the summary: the facts, Chocolatey's setup side effects, then every unavailability reason.
    private static void AddLines(StackPanel content, ChocolateyClientUpdateViewModel model)
    {
        content.Children.Add(Heading(ChocolateyClientUpdateViewModel.FactsHeading));
        foreach (ReviewedDisplayField field in model.Fields) content.Children.Add(Line(field));
        content.Children.Add(Heading(ChocolateyClientUpdateViewModel.DisclosuresHeading));
        foreach (ReviewedDisplayField field in model.DisclosureFields) content.Children.Add(Line(field));
        if (model.UnavailableFields.Count != 0)
        {
            content.Children.Add(Heading(ChocolateyClientUpdateViewModel.UnavailableHeading));
            foreach (ReviewedDisplayField field in model.UnavailableFields) content.Children.Add(Line(field));
        }
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
