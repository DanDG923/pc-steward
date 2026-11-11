using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Dispatching;
using System.Globalization;
using PCSteward.Application;
using PCSteward.Storage;
using PCSteward.Scheduling.Windows;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PCSteward.Desktop;

public sealed partial class MainWindow
{
    /// <summary>
    /// FR-UPD-01, FR-POL-01–05, FR-UI-04 (Task 73 Phase 2, ADR-030 decision 2): Updates grouped into Ready, Needs review and
    /// Ignored and skipped by the portable <see cref="UpdatesPagePresenter"/>. Only Ready rows get a selection control and
    /// nothing is preselected; Needs review rows show the reason in words and have no update control at all (SPEC §7.3).
    /// Choosing a row only enables the review button, and the review always opens the structured review sheet first.
    /// </summary>
    private StackPanel BuildUpdatesPage()
    {
        StackPanel panel = CreatePagePanel(960);
        UpdatesPagePresentation page = UpdatesPagePresenter.Build(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, activeDecisions, selectedReadyKeys, DateTimeOffset.UtcNow);
        selectedReadyKeys.RemoveWhere(key => !page.IsSelected(key));
        Button refresh = StewardVisuals.ActionButton(viewModel.IsCheckingUpdates ? "Checking package sources…" : "Refresh package sources");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !HasActiveOperation;
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        Button reviewDecisions = StewardVisuals.ActionButton("Review decisions");
        reviewDecisions.Click += (_, _) => NavigateTo(ShellPage.Review);
        StewardWrapPanel headerActions = StewardVisuals.ActionRow();
        headerActions.Children.Add(reviewDecisions);
        headerActions.Children.Add(refresh);
        panel.Children.Add(StewardVisuals.PageHeader("Updates", "Choose the updates that are ready, review their details, then decide. Nothing updates until you confirm it.", "Your approval is required", headerActions));
        panel.Children.Add(StewardVisuals.InformationBanner(viewModel.IsUpdateSourceEnabled ? "Package sources" : "Package sources are off", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));
        if (updateJob is { Step: UpdateJobStep.Update or UpdateJobStep.Verify } job)
        {
            Button view = StewardVisuals.ActionButton(job.Step == UpdateJobStep.Update ? "View update progress" : "View update result");
            view.Click += (_, _) => NavigateTo(ShellPage.UpdateJob);
            panel.Children.Add(StewardVisuals.InformationBanner(job.Step == UpdateJobStep.Update ? $"Updating {job.Sheet.AppName}" : $"Update finished: {job.Sheet.AppName}",
                job.Step == UpdateJobStep.Update ? UpdateJobProgressText.LeaveNote : job.Completion?.Headline ?? "No outcome recorded"));
            panel.Children.Add(view);
        }
        if (!page.HasAnyRow)
        {
            Button applications = StewardVisuals.ActionButton("Open Installed apps");
            applications.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            panel.Children.Add(StewardVisuals.EmptyState("", "Nothing needs update review", "Updates appear here only when a package source you turned on gives a fresh, exact and compatible match.", applications));
            return panel;
        }

        // Ready: the only group that can be selected. Checkboxes start empty; "Select all ready" and "Review all ready" choose exactly
        // the Ready group (Task 73 Phase 6, SPEC §9.13 G4). Choosing never starts anything: the review sheet comes first.
        panel.Children.Add(SectionHeading($"Ready ({page.Ready.Count})"));
        panel.Children.Add(PageCopy(page.Ready.Count == 0
            ? "No updates are ready. Updates appear here only when the evidence is fresh, exact and compatible."
            : "Choose the apps to review, or review all ready updates. Choosing doesn't start anything: you check every app on the next step and confirm there."));
        if (page.CanReviewAll)
        {
            Button selectAll = StewardVisuals.ActionButton(page.AllReadySelected ? "Clear selection" : "Select all ready");
            selectAll.IsEnabled = !HasActiveOperation && reviewDecision is null;
            selectAll.Click += (_, _) => { if (page.AllReadySelected) selectedReadyKeys.Clear(); else selectedReadyKeys.UnionWith(page.ReadyKeys); Render(); };
            panel.Children.Add(selectAll);
        }
        foreach (UpdateRowPresentation ready in page.Ready)
        {
            Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            CheckBox choose = new() { IsChecked = page.IsSelected(ready.StableProductKey), Content = ready.Name };
            StewardVisuals.Named(choose, ready.Name, $"{ready.VersionText}: select to review");
            string key = ready.StableProductKey;
            choose.Checked += (_, _) => { if (selectedReadyKeys.Add(key)) Render(); };
            choose.Unchecked += (_, _) => { if (selectedReadyKeys.Remove(key)) Render(); };
            line.Children.Add(choose);
            Border chip = StewardVisuals.StatusBadge(ready.Status, $"{ready.Name}: {StewardStatus.From(ready.Status).Label}");
            chip.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chip, 1); line.Children.Add(chip);
            Button more = UpdateRowOverflow(ready); Grid.SetColumn(more, 2); line.Children.Add(more);
            StackPanel body = new() { Spacing = StewardSpacing.XSmall, Margin = new Thickness(StewardSpacing.XLarge, 0, 0, 0) };
            StewardWrapPanel versions = StewardVisuals.ActionRow();
            versions.Children.Add(new TextBlock { Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true });
            if (ready.Badge is { } badge)
            {
                Border jump = StewardVisuals.Badge(badge.Label, badge.Glyph, StewardStatus.ToneName(badge.Tone), badge.AccessibleName);
                if (badge.IsHighlighted) jump.BorderThickness = new Thickness(2);
                versions.Children.Add(jump);
            }
            body.Children.Add(versions);
            body.Children.Add(PageCopy(ready.Publisher));
            if (!string.IsNullOrWhiteSpace(ready.Freshness)) body.Children.Add(new TextBlock { Text = ready.Freshness, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
            StackPanel card = new() { Spacing = StewardSpacing.XSmall };
            card.Children.Add(line);
            card.Children.Add(body);
            panel.Children.Add(Card(card));
        }
        if (page.Ready.Count > 0)
        {
            StewardWrapPanel reviewActions = StewardVisuals.ActionRow();
            // Both buttons only open the review sheet for a frozen set; the update needs its own confirmation there (FR-UPD-01, AC-UPD-01).
            Button reviewAll = StewardVisuals.PrimaryButton(preparingReview ? PreparingReviewLabel : page.ReviewAllLabel);
            reviewAll.IsEnabled = page.CanReviewAll && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
            AutomationProperties.SetHelpText(reviewAll, "Selects every ready update and opens one review. Nothing is updated until you confirm on the review page.");
            reviewAll.Click += async (_, _) => { selectedReadyKeys.Clear(); selectedReadyKeys.UnionWith(page.ReadyKeys); await ReviewAndExecuteAsync(RowsFor(page.ReadyKeys)); };
            string reviewLabel = preparingReview ? PreparingReviewLabel : page.ReviewButtonLabel;
            Button review = StewardVisuals.ActionButton(reviewLabel);
            review.AccessKey = "R";
            review.IsEnabled = page.CanReview && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
            AutomationProperties.SetHelpText(review, "Opens the review of the apps you selected. Nothing is updated until you confirm on the review page.");
            review.Click += async (_, _) => await ReviewAndExecuteAsync(RowsFor(page.SelectedKeys));
            reviewActions.Children.Add(reviewAll);
            reviewActions.Children.Add(review);
            panel.Children.Add(reviewActions);
        }

        // Needs review: the reason in words and no update control at all — not a disabled one.
        panel.Children.Add(SectionHeading($"Needs review ({page.NeedsReview.Count})"));
        panel.Children.Add(PageCopy(page.NeedsReview.Count == 0
            ? "Nothing needs review."
            : "These can't be updated from here. Low-confidence, conflicting, stale, unavailable or incomparable evidence is shown so you can see why."));
        foreach (UpdateRowPresentation needsReview in page.NeedsReview)
        {
            Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new() { Spacing = StewardSpacing.XSmall };
            identity.Children.Add(SectionHeading(needsReview.Name, AutomationHeadingLevel.Level3));
            identity.Children.Add(StewardVisuals.SelectableText($"{needsReview.Publisher} · {needsReview.VersionText}"));
            line.Children.Add(identity);
            Border chip = StewardVisuals.StatusBadge(needsReview.Status, $"{needsReview.Name}: {StewardStatus.From(needsReview.Status).Label}");
            chip.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(chip, 1); line.Children.Add(chip);
            Button more = UpdateRowOverflow(needsReview); Grid.SetColumn(more, 2); line.Children.Add(more);
            StackPanel card = new() { Spacing = StewardSpacing.XSmall };
            card.Children.Add(line);
            TextBlock reason = new() { Text = $"Why: {needsReview.ReasonText}", TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            AutomationProperties.SetName(reason, $"Why {needsReview.Name} needs review: {needsReview.ReasonText}");
            card.Children.Add(reason);
            if (!string.IsNullOrWhiteSpace(needsReview.Freshness)) card.Children.Add(new TextBlock { Text = needsReview.Freshness, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(Card(card));
        }

        // Ignored and skipped: collapsed by default and reversible from here or from Decisions.
        StackPanel ignoredRows = new() { Spacing = StewardSpacing.Small };
        ignoredRows.Children.Add(PageCopy(page.IgnoredAndSkipped.Count == 0
            ? "You haven't ignored, pinned or skipped a version of any app."
            : "You chose to ignore, pin or skip a version of these apps. Each choice can be reversed here or on the Decisions page."));
        foreach (UpdateRowPresentation ignored in page.IgnoredAndSkipped)
        {
            Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel identity = new() { Spacing = StewardSpacing.XSmall };
            identity.Children.Add(new TextBlock { Text = ignored.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            identity.Children.Add(PageCopy(ignored.ReasonText));
            line.Children.Add(identity);
            Border chip = StewardVisuals.StatusBadge(ignored.Status, $"{ignored.Name}: {StewardStatus.From(ignored.Status).Label}");
            chip.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(chip, 1); line.Children.Add(chip);
            Button more = UpdateRowOverflow(ignored); Grid.SetColumn(more, 2); line.Children.Add(more);
            ignoredRows.Children.Add(line);
        }
        Expander ignoredGroup = new() { Header = $"Ignored and skipped ({page.IgnoredAndSkipped.Count})", Content = ignoredRows, IsExpanded = ignoredGroupExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        ignoredGroup.Expanding += (_, _) => ignoredGroupExpanded = true;
        ignoredGroup.Collapsed += (_, _) => ignoredGroupExpanded = false;
        panel.Children.Add(ignoredGroup);
        return panel;
    }

    /// <summary>
    /// FR-POL-01–05 (ADR-030 decision 2): a row's overflow menu. Every entry uses a decision operation and its confirmation: ignore
    /// the app, pin the installed version, skip this exact version (owner decision 2026-11-02), open details, or reverse a recorded
    /// pin, ignore or skip. None of them can start an update.
    /// </summary>
    private Button UpdateRowOverflow(UpdateRowPresentation presentation)
    {
        Button more = new() { Content = new FontIcon { Glyph = "", FontSize = 16 }, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(more, $"More options for {presentation.Name}");
        ToolTipService.SetToolTip(more, "More options");
        MenuFlyout menu = new();
        foreach (UpdateRowActionPresentation action in presentation.Actions)
        {
            MenuFlyoutItem item = new() { Text = action.Label, Icon = new FontIcon { Glyph = action.Glyph } };
            AutomationProperties.SetName(item, $"{action.Label}: {presentation.Name}");
            item.IsEnabled = !HasActiveOperation || action.Action == UpdateRowAction.Details;
            item.Click += async (_, _) =>
            {
                if (viewModel.AllRows.SingleOrDefault(row => row.StableProductKey == presentation.StableProductKey) is not { } row) return;
                switch (action.Action)
                {
                    case UpdateRowAction.IgnoreApp: await PromptAndIgnoreProductAsync(row); break;
                    case UpdateRowAction.PinCurrentVersion: await PromptAndPinCurrentVersionAsync(row); break;
                    case UpdateRowAction.SkipVersion when action.Version is { } version: await PromptAndSkipVersionAsync(row, version); break;
                    case UpdateRowAction.Details: await ShowDetailsAsync(row); break;
                    case UpdateRowAction.Reverse when presentation.DecisionId is { } decisionId:
                        try { await DataOperationLease.RunAsync(token => workflow.ReverseDecisionAsync(decisionId, token), CancellationToken.None); LoadSavedInventory(); Render(); }
                        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
                        break;
                }
            };
            menu.Items.Add(item);
        }
        more.Flyout = menu;
        return more;
    }
}
