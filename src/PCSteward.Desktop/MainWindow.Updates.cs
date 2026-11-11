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
    /// FR-UPD-01, FR-POL-01–05, FR-UI-04/05 (Task 73 Phase 2, ADR-030 decision 2; UI polish phase 3, approved targets T-Updates and
    /// T-Updates-Ready): Updates grouped into Ready, Needs review and Ignored and skipped by the portable
    /// <see cref="UpdatesPagePresenter"/>. The header counts the rows and states freshness in words; with update checks off an info
    /// bar offers the existing source consent. Only Ready rows get a selection control and nothing is preselected; Needs review rows
    /// say why in words and have no update control at all. Records of the same app are one row. Choosing rows only enables the
    /// review button, and the review always opens the structured review sheet first.
    /// </summary>
    private StackPanel BuildUpdatesPage()
    {
        StackPanel panel = CreatePagePanel();
        UpdatesPagePresentation page = UpdatesPagePresenter.Build(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, activeDecisions, selectedReadyKeys, DateTimeOffset.UtcNow);
        selectedReadyKeys.RemoveWhere(key => !page.IsSelected(key));
        UpdatesPageHeader header = UpdatesPagePresenter.Header(page, viewModel.IsUpdateSourceEnabled, viewModel.Sources, DateTimeOffset.UtcNow);
        Button? check = null;
        if (header.ShowsCheckForUpdates)
        {
            // The same read-only check as Home's "Check for updates": a scan, then the package-source check. It never elevates.
            string checkLabel = viewModel.IsScanning ? "Scanning…" : viewModel.IsCheckingUpdates ? "Checking…" : "Check for updates";
            check = new() { Content = IconAndLabel("\uE72C", checkLabel), AccessKey = "C" };
            StewardVisuals.Named(check, checkLabel, "without administrator permission");
            AutomationProperties.SetHelpText(check, HomePresenter.CheckForUpdatesHelp);
            check.IsEnabled = viewModel.IsUpdateSourceEnabled && !HasActiveOperation;
            check.Click += async (_, _) => await CheckForUpdatesAsync();
        }
        panel.Children.Add(StewardVisuals.PageHeader("Updates", header.Description, action: check));
        if (header.Notice is { } notice)
        {
            Button? turnOn = null;
            if (notice.OffersTurnOnUpdateChecks)
            {
                // The existing source consent: it shows what is shared, defaults to keeping local-only and starts nothing else.
                turnOn = StewardVisuals.PrimaryButton(HomePresenter.TurnOnUpdateChecksLabel, "shows what is shared and asks you first");
                turnOn.AccessKey = "T";
                AutomationProperties.SetHelpText(turnOn, HomePresenter.TurnOnUpdateChecksHelp);
                turnOn.IsEnabled = !HasActiveOperation;
                turnOn.Click += async (_, _) => await ChangeWinGetTrustedSourcesAsync(true);
            }
            panel.Children.Add(StewardVisuals.InformationBanner(notice.Title, notice.Message, StewardStatus.ToneName(notice.Tone), turnOn));
        }
        if (updateJob is { Step: UpdateJobStep.Update or UpdateJobStep.Verify } job)
        {
            Button view = StewardVisuals.ActionButton(job.Step == UpdateJobStep.Update ? "View update progress" : "View update result");
            view.Click += (_, _) => NavigateTo(ShellPage.UpdateJob);
            panel.Children.Add(StewardVisuals.InformationBanner(job.Step == UpdateJobStep.Update ? $"Updating {job.Sheet.AppName}" : $"Update finished: {job.Sheet.AppName}",
                job.Step == UpdateJobStep.Update ? UpdateJobProgressText.LeaveNote : job.Completion?.Headline ?? "No outcome recorded", "information", view));
        }
        if (!page.HasAnyRow)
        {
            Button applications = StewardVisuals.ActionButton("Open Installed apps");
            applications.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            panel.Children.Add(StewardVisuals.EmptyState("\uE896", header.EmptyTitle, header.EmptyMessage, applications));
            panel.Children.Add(StewardVisuals.IconLine("\uE946", header.Footer, styleKey: "StewardCaptionStyle"));
            return panel;
        }

        // Ready: the only group that can be selected. Checkboxes start empty; "Select all ready" and "Review all" choose exactly the
        // Ready group (Task 73 Phase 6, SPEC §9.13 G4). Choosing never starts anything: the review sheet comes first.
        if (page.Ready.Count > 0)
        {
            Button selectAll = StewardVisuals.ActionButton(page.AllReadySelected ? "Clear selection" : "Select all ready");
            selectAll.IsEnabled = !HasActiveOperation && reviewDecision is null;
            selectAll.Click += (_, _) => { if (page.AllReadySelected) selectedReadyKeys.Clear(); else selectedReadyKeys.UnionWith(page.ReadyKeys); Render(); };
            // Both buttons only open the review sheet for a frozen set; the update needs its own confirmation there (FR-UPD-01, AC-UPD-01).
            Button reviewAll = StewardVisuals.PrimaryButton(preparingReview ? PreparingReviewLabel : page.ReviewAllLabel);
            reviewAll.AccessKey = "R";
            reviewAll.IsEnabled = page.CanReviewAll && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
            AutomationProperties.SetHelpText(reviewAll, "Selects every ready update and opens one review. Nothing is updated until you confirm on the review page.");
            reviewAll.Click += async (_, _) => { selectedReadyKeys.Clear(); selectedReadyKeys.UnionWith(page.ReadyKeys); await ReviewAndExecuteAsync(RowsFor(page.ReadyKeys)); };
            string reviewLabel = preparingReview ? PreparingReviewLabel : page.ReviewButtonLabel;
            Button review = StewardVisuals.PrimaryButton(reviewLabel);
            review.AccessKey = "R";
            review.IsEnabled = page.CanReview && !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
            AutomationProperties.SetHelpText(review, "Opens the review of the apps you selected. Nothing is updated until you confirm on the review page.");
            review.Click += async (_, _) => await ReviewAndExecuteAsync(RowsFor(page.SelectedKeys));
            // The selection bar (approved target): one primary button at a time — the chosen apps once something is chosen,
            // otherwise every ready app.
            StewardWrapPanel reviewActions = StewardVisuals.ActionRow();
            reviewActions.Children.Add(selectAll);
            reviewActions.Children.Add(page.CanReview ? review : reviewAll);
            panel.Children.Add(new Border { Style = AppStyle("StewardCardStyle"), Padding = new Thickness(StewardSpacing.Large, StewardSpacing.Medium, StewardSpacing.Large, StewardSpacing.Medium), Child = StewardVisuals.SettingsRow(page.SelectionText, null, reviewActions) });

            List<FrameworkElement> readyRows = [];
            foreach (UpdateRowPresentation ready in page.Ready)
            {
                Grid line = UpdateRowGrid();
                CheckBox choose = new() { IsChecked = page.IsSelected(ready.StableProductKey), MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
                StewardVisuals.Named(choose, ready.Name, $"{ready.VersionText}: select to review");
                string key = ready.StableProductKey;
                choose.Checked += (_, _) => { if (selectedReadyKeys.Add(key)) Render(); };
                choose.Unchecked += (_, _) => { if (selectedReadyKeys.Remove(key)) Render(); };
                line.Children.Add(choose);
                Border avatar = StewardVisuals.Avatar(StewardPresentation.Initials(ready.Name));
                Grid.SetColumn(avatar, 1); line.Children.Add(avatar);
                StewardWrapPanel title = new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.XSmall };
                title.Children.Add(new TextBlock { Text = ready.Name, Style = AppStyle("StewardBodyStrongStyle"), VerticalAlignment = VerticalAlignment.Center });
                title.Children.Add(new TextBlock { Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true });
                if (ready.Badge is { } badge)
                {
                    Border jump = StewardVisuals.Badge(badge.Label, badge.Glyph, StewardStatus.ToneName(badge.Tone), badge.AccessibleName);
                    jump.VerticalAlignment = VerticalAlignment.Center;
                    title.Children.Add(jump);
                }
                UpdateRowCopy(line, title, ready.Summary);
                Button more = UpdateRowOverflow(ready); Grid.SetColumn(more, 3); line.Children.Add(more);
                readyRows.Add(line);
            }
            panel.Children.Add(UpdateGroupSection($"Ready ({page.Ready.Count})", null, StewardVisuals.ListCard(readyRows)));
        }

        // Needs review: the reason in words and no update control at all — not a disabled one. "Review match" only opens the app's
        // details, where a package identity can be confirmed through its own confirmation; nothing there starts an update.
        if (page.NeedsReview.Count > 0)
        {
            List<FrameworkElement> reviewRows = [];
            foreach (UpdateRowPresentation needsReview in page.NeedsReview)
            {
                Grid line = UpdateRowGrid();
                Border well = StewardVisuals.IconWell(needsReview.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable) ? "\uE7BA" : "\uE721");
                Grid.SetColumnSpan(well, 2); line.Children.Add(well);
                TextBlock name = new() { Text = needsReview.Name, Style = AppStyle("StewardBodyStrongStyle"), TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetHeadingLevel(name, AutomationHeadingLevel.Level3);
                TextBlock reason = UpdateRowCopy(line, name, needsReview.Summary);
                reason.IsTextSelectionEnabled = true;
                AutomationProperties.SetName(reason, $"Why {needsReview.Name} needs review: {needsReview.ReasonText}");
                StewardWrapPanel actions = StewardVisuals.ActionRow();
                actions.VerticalAlignment = VerticalAlignment.Center;
                bool unavailable = needsReview.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable);
                Button open = StewardVisuals.ActionButton(unavailable ? "View details" : "Review match", $"for {needsReview.Name}");
                string reviewKey = needsReview.StableProductKey;
                open.Click += async (_, _) => { if (RowsFor([reviewKey]) is [var row]) await ShowDetailsAsync(row); };
                actions.Children.Add(open);
                actions.Children.Add(UpdateRowOverflow(needsReview));
                Grid.SetColumn(actions, 3); line.Children.Add(actions);
                reviewRows.Add(line);
            }
            HyperlinkButton allDecisions = new() { Content = "See every match that needs a decision", Padding = new Thickness(0) };
            StewardVisuals.Named(allDecisions, "See every match that needs a decision", "on Review decisions");
            allDecisions.Click += (_, _) => NavigateTo(ShellPage.Review);
            panel.Children.Add(UpdateGroupSection($"Needs review ({page.NeedsReview.Count})",
                "PC Steward can't safely update these yet. Each one says why.", StewardVisuals.ListCard(reviewRows), allDecisions));
        }

        // Ignored and skipped: collapsed by default and reversible from here or from Decisions.
        if (page.IgnoredAndSkipped.Count > 0)
        {
            List<FrameworkElement> ignoredRows = [];
            foreach (UpdateRowPresentation ignored in page.IgnoredAndSkipped)
            {
                Grid line = UpdateRowGrid();
                Border avatar = StewardVisuals.Avatar(StewardPresentation.Initials(ignored.Name));
                Grid.SetColumnSpan(avatar, 2); line.Children.Add(avatar);
                TextBlock name = new() { Text = ignored.Name, Style = AppStyle("StewardBodyStrongStyle"), TextWrapping = TextWrapping.Wrap };
                UpdateRowCopy(line, name, ignored.Summary);
                Border chip = StewardVisuals.StatusBadge(ignored.Status, $"{ignored.Name}: {StewardStatus.From(ignored.Status).Label}");
                Button more = UpdateRowOverflow(ignored);
                StewardWrapPanel actions = StewardVisuals.ActionRow();
                actions.VerticalAlignment = VerticalAlignment.Center;
                chip.VerticalAlignment = VerticalAlignment.Center;
                actions.Children.Add(chip);
                actions.Children.Add(more);
                Grid.SetColumn(actions, 3); line.Children.Add(actions);
                ignoredRows.Add(line);
            }
            Grid ignoredHeader = new() { ColumnSpacing = StewardSpacing.Large };
            ignoredHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ignoredHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ignoredHeader.Children.Add(StewardVisuals.IconWell("\uED1A"));
            StackPanel ignoredCopy = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            ignoredCopy.Children.Add(new TextBlock { Text = $"Ignored and skipped ({page.IgnoredAndSkipped.Count})", Style = AppStyle("StewardRowTitleStyle") });
            ignoredCopy.Children.Add(new TextBlock { Text = "You can undo these at any time, here or on the Decisions page.", Style = AppStyle("StewardCaptionStyle") });
            Grid.SetColumn(ignoredCopy, 1); ignoredHeader.Children.Add(ignoredCopy);
            Expander ignoredGroup = new() { Header = ignoredHeader, Content = StewardVisuals.ListCard(ignoredRows), IsExpanded = ignoredGroupExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(ignoredGroup, $"Ignored and skipped ({page.IgnoredAndSkipped.Count}). You can undo these at any time.");
            ignoredGroup.Expanding += (_, _) => ignoredGroupExpanded = true;
            ignoredGroup.Collapsed += (_, _) => ignoredGroupExpanded = false;
            panel.Children.Add(ignoredGroup);
        }
        panel.Children.Add(StewardVisuals.IconLine("\uEA18", header.Footer, styleKey: "StewardCaptionStyle"));
        return panel;
    }

    /// <summary>
    /// One Updates list row's columns: the selection box (Ready only), the app's avatar or icon well, the copy, and the actions. A row
    /// without a selection box spans its icon over the first two columns, so every row's copy starts on the same edge.
    /// </summary>
    private static Grid UpdateRowGrid()
    {
        Grid line = new() { ColumnSpacing = StewardSpacing.Large };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return line;
    }

    /// <summary>Adds a row's copy (its title element and one wrapping caption line) to <paramref name="line"/> and returns the caption.</summary>
    private static TextBlock UpdateRowCopy(Grid line, FrameworkElement title, string summary)
    {
        StackPanel copy = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(title);
        TextBlock caption = new() { Text = summary, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap };
        copy.Children.Add(caption);
        Grid.SetColumn(copy, 2);
        line.Children.Add(copy);
        return caption;
    }

    /// <summary>An Updates group: its heading, an optional one-line description and link, then its list card.</summary>
    private static StackPanel UpdateGroupSection(string heading, string? description, FrameworkElement list, FrameworkElement? link = null)
    {
        StackPanel section = new() { Spacing = StewardSpacing.Small };
        section.Children.Add(SectionHeading(heading));
        if (description is not null) section.Children.Add(new TextBlock { Text = description, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        if (link is not null) section.Children.Add(link);
        section.Children.Add(list);
        return section;
    }

    /// <summary>A button's content: a small icon then its label.</summary>
    private static StackPanel IconAndLabel(string glyph, string label)
    {
        StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16 });
        content.Children.Add(new TextBlock { Text = label });
        return content;
    }

    /// <summary>
    /// FR-POL-01–05 (ADR-030 decision 2): a row's overflow menu. Every entry uses a decision operation and its confirmation: ignore
    /// the app, pin the installed version, skip this exact version (owner decision 2026-11-02), open details, or reverse a recorded
    /// pin, ignore or skip. None of them can start an update.
    /// </summary>
    private Button UpdateRowOverflow(UpdateRowPresentation presentation)
    {
        Button more = new() { Content = new FontIcon { Glyph = "", FontSize = 16 }, VerticalAlignment = VerticalAlignment.Center, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) };
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
                // UI polish phase 3: a row that stands for several records of the same app records the same decision for each of them,
                // in the same confirmation, so the app never reappears from its other record. None of this can start an update.
                IReadOnlyList<InventoryRow> sameApp = RowsFor(presentation.AlsoFound.Select(other => other.StableProductKey));
                switch (action.Action)
                {
                    case UpdateRowAction.IgnoreApp: await PromptAndIgnoreProductAsync(row, sameApp); break;
                    case UpdateRowAction.PinCurrentVersion: await PromptAndPinCurrentVersionAsync(row, sameApp); break;
                    case UpdateRowAction.SkipVersion when action.Version is { } version: await PromptAndSkipVersionAsync(row, version, sameApp); break;
                    case UpdateRowAction.Details: await ShowDetailsAsync(row); break;
                    case UpdateRowAction.Reverse when presentation.DecisionId is { } decisionId:
                        Guid[] alsoReverse = [.. presentation.AlsoFound.Select(other => other.DecisionId).OfType<Guid>()];
                        try
                        {
                            await DataOperationLease.RunAsync(async token =>
                            {
                                await workflow.ReverseDecisionAsync(decisionId, token);
                                foreach (Guid other in alsoReverse) await workflow.ReverseDecisionAsync(other, token);
                            }, CancellationToken.None);
                            LoadSavedInventory(); Render();
                        }
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
