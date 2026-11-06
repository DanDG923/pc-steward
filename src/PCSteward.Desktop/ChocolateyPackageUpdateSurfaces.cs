using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; Chocolatey package Confirm addendum §4: thin rendering of
/// <see cref="ChocolateyPackageUpdateViewModel"/> — the admitted entry point on a Chocolatey Community row and the package review
/// dialog (opened as "Checking", then re-rendered with the review session's state). It renders every line the view model exposes,
/// binds the primary button's label and enabled state to <see cref="ChocolateyPackageUpdateViewModel.PrimaryText"/> and
/// <see cref="ChocolateyPackageUpdateViewModel.IsPrimaryActionEnabled"/> only, and renders the progress and result views of a
/// confirmed run. It holds no logic, starts nothing and has no review, confirm or dispatch path.
/// </summary>
internal static class ChocolateyPackageUpdateSurfaces
{
    /// <summary>The admitted entry point; its accessible name starts with the visible label.</summary>
    internal static Button EntryButton(string packageId)
    {
        Button entry = new() { Content = ChocolateyPackageUpdateViewModel.EntryText };
        AutomationProperties.SetName(entry, $"{ChocolateyPackageUpdateViewModel.EntryText} {packageId}. {ChocolateyPackageUpdateViewModel.EntryAutomationDetail}");
        return entry;
    }

    /// <summary>The review dialog. Close is the default button, so Enter and Esc never confirm.</summary>
    internal static ContentDialog ReviewDialog(XamlRoot root, ChocolateyPackageUpdateViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ContentDialog dialog = new()
        {
            XamlRoot = root,
            CloseButtonText = ChocolateyPackageUpdateViewModel.CloseText,
            DefaultButton = ContentDialogButton.Close,
        };
        Render(dialog, model);
        return dialog;
    }

    /// <summary>
    /// Renders <paramref name="model"/>: title, the live summary, every disclosure line, then every unavailability reason, and the
    /// primary button from the view model only. A re-render keeps the ScrollViewer, the summary and the scroll position.
    /// </summary>
    internal static void Render(ContentDialog dialog, ChocolateyPackageUpdateViewModel model)
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
        dialog.PrimaryButtonText = model.PrimaryText;
        dialog.IsPrimaryButtonEnabled = model.IsPrimaryActionEnabled;
        dialog.CloseButtonText = ChocolateyPackageUpdateViewModel.CloseText;
        AutomationProperties.SetName(dialog, model.Title);
        AutomationProperties.SetHelpText(dialog, model.ConfirmAutomationName);
    }

    /// <summary>
    /// While a confirmed run is in flight the dialog is the progress view: the live summary carries the current phase, below it the
    /// frozen change line and the close guard. Both buttons are hidden; Esc and the main window's close are also refused.
    /// </summary>
    internal static void RenderProgress(ContentDialog dialog, string title, string change, string status)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        RenderStatus(dialog, title, status, [change, ChocolateyPackageUpdateRunPresentation.RunningCloseGuardText]);
        dialog.PrimaryButtonText = string.Empty;
        dialog.IsPrimaryButtonEnabled = false;
        dialog.CloseButtonText = string.Empty;
        AutomationProperties.SetHelpText(dialog, ChocolateyPackageUpdateRunPresentation.RunningCloseGuardText);
    }

    /// <summary>The result replaces the progress line; "Check again" starts a new review.</summary>
    internal static void RenderResult(ContentDialog dialog, ChocolateyClientUpdateRunView view)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(view);
        RenderStatus(dialog, view.Heading, view.Lines.Count == 0 ? view.Heading : view.Lines[0], view.Lines.Skip(1));
        dialog.PrimaryButtonText = ChocolateyPackageUpdateViewModel.CheckAgainText;
        dialog.IsPrimaryButtonEnabled = true;
        dialog.CloseButtonText = ChocolateyPackageUpdateViewModel.CloseText;
        AutomationProperties.SetHelpText(dialog, view.Heading);
    }

    private static void RenderStatus(ContentDialog dialog, string title, string status, IEnumerable<string> lines)
    {
        if (dialog.Content is not ScrollViewer { Content: StackPanel { Children.Count: > 0 } rendered } || rendered.Children[0] is not TextBlock liveSummary)
            return;
        while (rendered.Children.Count > 1) rendered.Children.RemoveAt(rendered.Children.Count - 1);
        foreach (string text in lines)
            rendered.Children.Add(new TextBlock { Text = text, Style = StewardVisuals.Style("StewardBodyStyle"), TextWrapping = TextWrapping.Wrap });
        StewardVisuals.SetLiveText(liveSummary, status, announce: true);
        dialog.Title = title;
        AutomationProperties.SetName(dialog, title);
    }

    private static void AddLines(StackPanel content, ChocolateyPackageUpdateViewModel model)
    {
        foreach (ReviewedDisplayField field in model.Fields) content.Children.Add(Line(field));
        if (model.UnavailableFields.Count != 0)
        {
            TextBlock heading = StewardVisuals.SectionHeading(ChocolateyPackageUpdateViewModel.UnavailableHeading, AutomationHeadingLevel.Level3);
            heading.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(heading);
            foreach (ReviewedDisplayField field in model.UnavailableFields) content.Children.Add(Line(field));
        }
    }

    private static Grid Line(ReviewedDisplayField field)
    {
        Grid line = StewardVisuals.EvidenceRow(field.Label, field.Text);
        AutomationProperties.SetName(line, field.AutomationName);
        return line;
    }
}
