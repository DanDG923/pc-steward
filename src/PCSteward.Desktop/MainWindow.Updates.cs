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
    /// T-Updates-Ready; UI review 2026-11-13, items 9 and 10): Updates grouped by the portable <see cref="UpdatesPagePresenter"/>
    /// into Update ready, Check the match, Can't update yet and Ignored and skipped. The header counts the rows and says when
    /// PC Steward last looked; with update checks off an info bar offers the existing source consent. Only ready rows get a
    /// selection control or an "Update…" button, and nothing is preselected. A "Check the match" row asks one question and a
    /// "Can't update yet" line gives one reason; neither has an update control at all. Records of the same app are one row.
    /// Every route to an update opens the structured review sheet first.
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
            StewardVisuals.Named(check, checkLabel, "(scans this PC, then looks for updates)");
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
            if (NothingOnOfferLink(page) is { } possibleMatches) panel.Children.Add(possibleMatches);
            panel.Children.Add(StewardVisuals.IconLine("\uE946", header.Footer, styleKey: "StewardDescriptionStyle"));
            return panel;
        }

        // Ready: the only group that can be selected. Checkboxes start empty; "Select all ready" and "Update all" choose exactly the
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
            AutomationProperties.SetHelpText(reviewAll, "Selects every ready update and opens one review. Only high-confidence matches are included. Nothing is updated until you confirm on the review page.");
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
                Border avatar = AppTile(ready.Name, ready.StableProductKey);
                Grid.SetColumn(avatar, 1); line.Children.Add(avatar);
                StewardWrapPanel title = new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.XSmall };
                title.Children.Add(UpdateRowName(ready.Name, ready.StableProductKey));
                title.Children.Add(new TextBlock { Text = ready.VersionText, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true });
                if (ready.Badge is { } badge)
                {
                    Border jump = StewardVisuals.Badge(badge.Label, badge.Glyph, StewardStatus.ToneName(badge.Tone), badge.AccessibleName);
                    jump.VerticalAlignment = VerticalAlignment.Center;
                    title.Children.Add(jump);
                }
                UpdateRowCopy(line, title, ready.Summary);
                // UI review 2026-11-13 (item 9): each ready row has its own "Update…" button, so updating one app is one click from
                // the list. It opens the same review as the selection bar, for this app alone; the review still needs its own
                // confirmation before anything is installed (FR-UPD-01, AC-UPD-01).
                Button update = StewardVisuals.ActionButton("Update…", $"{ready.Name} to {ready.NewVersion}");
                update.IsEnabled = !HasActiveOperation && reviewDecision is null && viewModel.IsUpdateSourceEnabled;
                AutomationProperties.SetHelpText(update, "Opens the review for this app. Nothing is installed until you confirm on the review page.");
                update.Click += async (_, _) => await ReviewAndExecuteAsync(RowsFor([key]));
                StewardWrapPanel readyActions = StewardVisuals.ActionRow();
                readyActions.VerticalAlignment = VerticalAlignment.Center;
                readyActions.Children.Add(update);
                readyActions.Children.Add(UpdateRowOverflow(ready));
                Grid.SetColumn(readyActions, 3); line.Children.Add(readyActions);
                readyRows.Add(line);
            }
            panel.Children.Add(UpdateGroupSection($"{StatusVocabulary.For(nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate)).Label} ({page.Ready.Count})", null, StewardVisuals.ListCard(readyRows)));
        }

        // Check the match (UI review 2026-11-13, item 10): one question the owner can answer. "Yes, same app" opens the existing
        // confirmation, which records a reversible match and never starts an update; "Choose the match" opens the app's details,
        // where tied matches are listed. The row has no update control at all.
        if (page.CheckTheMatch.Count > 0)
        {
            List<FrameworkElement> matchRows = [];
            foreach (UpdateRowPresentation match in page.CheckTheMatch)
            {
                Grid line = UpdateRowGrid();
                Border avatar = AppTile(match.Name, match.StableProductKey);
                Grid.SetColumnSpan(avatar, 2); line.Children.Add(avatar);
                StewardWrapPanel title = new() { HorizontalSpacing = StewardSpacing.Medium, VerticalSpacing = StewardSpacing.XSmall };
                title.Children.Add(UpdateRowName(match.Name, match.StableProductKey));
                title.Children.Add(new TextBlock { Text = match.InstalledVersion, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true });
                TextBlock question = UpdateRowCopy(line, title, match.Summary);
                question.IsTextSelectionEnabled = true;
                string matchKey = match.StableProductKey;
                Button same = StewardVisuals.ActionButton("Yes, same app", $"({match.PackageId} is {match.Name})");
                same.IsEnabled = !HasActiveOperation;
                AutomationProperties.SetHelpText(same, "Opens a confirmation. You can undo it later, and it doesn't start an update.");
                same.Click += async (_, _) =>
                {
                    if (RowsFor([matchKey]) is [var row] && viewModel.UpdateDetails.TryGetValue(matchKey, out UpdateDetail? detail) && detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true })
                        await PromptAndConfirmPackageIdentityAsync(row, detail);
                };
                // Tied matches (owner decision 2026-11-13: ask only when two packages tie): the choice is made on the app's details,
                // through the same reversible confirmation. Opening details never starts, approves or changes anything.
                Button pick = StewardVisuals.ActionButton("Choose the match", $"for {match.Name}");
                pick.Click += async (_, _) => { if (RowsFor([matchKey]) is [var row]) await ShowDetailsAsync(row); };
                StewardWrapPanel matchActions = StewardVisuals.ActionRow();
                matchActions.VerticalAlignment = VerticalAlignment.Center;
                matchActions.Children.Add(match.AsksToChoose ? pick : same);
                matchActions.Children.Add(UpdateRowOverflow(match));
                Grid.SetColumn(matchActions, 3); line.Children.Add(matchActions);
                matchRows.Add(line);
            }
            panel.Children.Add(UpdateGroupSection($"{StatusVocabulary.For(nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired)).Label} ({page.CheckTheMatch.Count})",
                UpdatesPagePresenter.CheckTheMatchDescription, StewardVisuals.ListCard(matchRows)));
        }

        // Can't update yet (item 10): one line per reason, naming its apps, collapsed by default. Nothing here can update anything:
        // an app's name opens its details, and "Check again" runs the same read-only check as the header button.
        if (page.CantUpdateYet.Count > 0)
        {
            List<FrameworkElement> reasonRows = [];
            foreach (CantUpdateGroup group in page.CantUpdateGroups)
            {
                Grid line = UpdateRowGrid();
                Border well = StewardVisuals.IconWell("\uE946");
                Grid.SetColumnSpan(well, 2); line.Children.Add(well);
                StackPanel copy = new() { Spacing = StewardSpacing.XSmall, VerticalAlignment = VerticalAlignment.Center };
                copy.Children.Add(new TextBlock { Text = group.Reason, Style = AppStyle("StewardBodyStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
                StewardWrapPanel names = new() { HorizontalSpacing = StewardSpacing.Large, VerticalSpacing = StewardSpacing.XSmall };
                foreach (UpdateRowPresentation app in group.Rows)
                {
                    HyperlinkButton open = new() { Content = app.Name, Padding = new Thickness(0) };
                    StewardVisuals.Named(open, app.Name, "(open details)");
                    string appKey = app.StableProductKey;
                    open.Click += async (_, _) => { if (RowsFor([appKey]) is [var row]) await ShowDetailsAsync(row); };
                    names.Children.Add(open);
                }
                copy.Children.Add(names);
                Grid.SetColumn(copy, 2); line.Children.Add(copy);
                if (group.CanCheckAgain)
                {
                    Button again = StewardVisuals.ActionButton("Check again", $"for {group.Names}");
                    again.VerticalAlignment = VerticalAlignment.Center;
                    again.IsEnabled = viewModel.IsUpdateSourceEnabled && !HasActiveOperation;
                    AutomationProperties.SetHelpText(again, HomePresenter.CheckForUpdatesHelp);
                    again.Click += async (_, _) => await CheckForUpdatesAsync();
                    Grid.SetColumn(again, 3); line.Children.Add(again);
                }
                reasonRows.Add(line);
            }
            string cantTitle = $"{UpdatesPagePresenter.CantUpdateYetTitle} ({page.CantUpdateYet.Count})";
            Grid cantHeader = new() { ColumnSpacing = StewardSpacing.Large };
            cantHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cantHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cantHeader.Children.Add(StewardVisuals.IconWell("\uE7BA"));
            StackPanel cantCopy = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            cantCopy.Children.Add(new TextBlock { Text = cantTitle, Style = AppStyle("StewardRowTitleStyle") });
            cantCopy.Children.Add(new TextBlock { Text = UpdatesPagePresenter.CantUpdateYetDescription, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(cantCopy, 1); cantHeader.Children.Add(cantCopy);
            Expander cantGroup = new() { Header = cantHeader, Content = StewardVisuals.ListCard(reasonRows), IsExpanded = cantUpdateGroupExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(cantGroup, $"{cantTitle}. {UpdatesPagePresenter.CantUpdateYetDescription}");
            cantGroup.Expanding += (_, _) => cantUpdateGroupExpanded = true;
            cantGroup.Collapsed += (_, _) => cantUpdateGroupExpanded = false;
            panel.Children.Add(cantGroup);
        }

        // Ignored and skipped: collapsed by default and reversible from here or from Decisions.
        if (page.IgnoredAndSkipped.Count > 0)
        {
            List<FrameworkElement> ignoredRows = [];
            foreach (UpdateRowPresentation ignored in page.IgnoredAndSkipped)
            {
                Grid line = UpdateRowGrid();
                Border avatar = AppTile(ignored.Name, ignored.StableProductKey);
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
            ignoredCopy.Children.Add(new TextBlock { Text = "You can undo these at any time, here or on the Decisions page.", Style = AppStyle("StewardDescriptionStyle") });
            Grid.SetColumn(ignoredCopy, 1); ignoredHeader.Children.Add(ignoredCopy);
            Expander ignoredGroup = new() { Header = ignoredHeader, Content = StewardVisuals.ListCard(ignoredRows), IsExpanded = ignoredGroupExpanded, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(ignoredGroup, $"Ignored and skipped ({page.IgnoredAndSkipped.Count}). You can undo these at any time.");
            ignoredGroup.Expanding += (_, _) => ignoredGroupExpanded = true;
            ignoredGroup.Collapsed += (_, _) => ignoredGroupExpanded = false;
            panel.Children.Add(ignoredGroup);
        }
        if (NothingOnOfferLink(page) is { } moreMatches) panel.Children.Add(moreMatches);
        panel.Children.Add(StewardVisuals.IconLine("\uEA18", header.Footer, styleKey: "StewardDescriptionStyle"));
        return panel;
    }

    /// <summary>
    /// An app's name on an Updates row. It opens the app's details, as a row on Installed apps does (UI review 2026-11-13, item 9).
    /// Opening details never starts, approves or changes anything.
    /// </summary>
    private Button UpdateRowName(string name, string stableProductKey)
    {
        Button open = new()
        {
            Content = new TextBlock { Text = name, Style = AppStyle("StewardBodyStrongStyle"), TextWrapping = TextWrapping.Wrap },
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        StewardVisuals.Named(open, name, "(open details)");
        open.Click += async (_, _) => { if (RowsFor([stableProductKey]) is [var row]) await ShowDetailsAsync(row); };
        return open;
    }

    /// <summary>
    /// UI review 2026-11-13 (item 10): apps with a possible match but no update on offer are listed on Installed apps, not here.
    /// This link says how many there are and opens that tab. It only navigates.
    /// </summary>
    private HyperlinkButton? NothingOnOfferLink(UpdatesPagePresentation page)
    {
        if (page.NothingOnOfferText is not { } label) return null;
        HyperlinkButton link = new() { Content = label, Padding = new Thickness(0) };
        StewardVisuals.Named(link, label, "(opens Installed apps)");
        link.Click += (_, _) => { viewModel.SetSavedFilter(InstalledAppsFilter.NeedsReview); NavigateTo(ShellPage.InstalledApps); };
        return link;
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
        // A row with nothing to add under its title (a ready update from an unknown publisher) shows no empty second line.
        TextBlock caption = new() { Text = summary, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap, Visibility = summary.Length == 0 ? Visibility.Collapsed : Visibility.Visible };
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
        if (description is not null) section.Children.Add(new TextBlock { Text = description, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
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
                        await ReverseDecisionsAsync([decisionId, .. presentation.AlsoFound.Select(other => other.DecisionId).OfType<Guid>()], []);
                        break;
                }
            };
            menu.Items.Add(item);
        }
        more.Flyout = menu;
        return more;
    }
}
