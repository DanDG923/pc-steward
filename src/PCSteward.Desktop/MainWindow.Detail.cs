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
    /// FR-POL-01–05, FR-HIS-02, FR-UI-01/04 (Task 73 Phase 3; audit P-M1, P-L6; UI review 2026-11-13, item 11): an app's details.
    /// The page leads with the answer: one headline, one sentence and at most one main button, from the portable
    /// <see cref="AppDetailSummaryPresenter"/>, with the other decisions in a "More" menu and the facts in two columns. Pin, ignore,
    /// skip and every match action use the same confirmation dialogs as the lists and the Updates page; "Update…" only opens the
    /// review. Technical details are structured sections and the app's own timeline is a list in words, not a newline dump.
    /// </summary>
    private StackPanel BuildProductDetailPage()
    {
        if (detailRow is null)
        {
            StackPanel missing = CreatePagePanel();
            missing.Children.Add(StewardVisuals.PageTitle("App details"));
            Button leave = StewardVisuals.ActionButton("Open Installed apps");
            leave.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            missing.Children.Add(StewardVisuals.EmptyState("", "App details are no longer open", "Open an app from Installed apps or Updates to see its details.", leave));
            return missing;
        }

        InventoryRow row = detailRow;
        UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
        // UI review 2026-11-13 (item 11): the page leads with the answer. The portable presenter decides the headline, the one
        // sentence and the one main action from the same evidence as the Updates page; nothing here decides what can be updated.
        AppDetailSummary summary = AppDetailSummaryPresenter.Build(row, detail, viewModel.IsUpdateSourceEnabled, activeDecisions, viewModel.Sources, DateTimeOffset.UtcNow);
        bool sourceEvidenceUnavailable = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable);

        StackPanel panel = CreatePagePanel();
        // The title bar's Back is the page's only Back button.
        Grid heading = new() { ColumnSpacing = StewardSpacing.Large };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Border avatar = AppTile(row.Name, row);
        avatar.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(avatar);
        StackPanel title = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        TextBlock name = StewardVisuals.PageTitle(row.Name);
        detailTitle = name;
        title.Children.Add(name);
        title.Children.Add(new TextBlock { Text = summary.Subtitle, Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        Grid.SetColumn(title, 1); heading.Children.Add(title);
        panel.Children.Add(heading);

        // Match it yourself: the match-by-ID form, out of Technical details. It is built first so the lead card can open it.
        // Audit §4: the source list shows the names rows show; the chosen index maps back to the same raw source ID as before.
        string[] mappingSources = ["winget", "msstore"];
        ComboBox mappingSource = FilterBox(StewardPresentation.Provider(detail?.SourceId ?? "winget"), mappingSources.Select(StewardPresentation.Provider).ToArray());
        mappingSource.Header = "Update source";
        AutomationProperties.SetName(mappingSource, $"Update source for matching {row.Name}");
        TextBox mappingPackage = new() { Header = "Exact package ID", PlaceholderText = detail?.PackageId ?? "For example: Contoso.Widget" };
        Button map = StewardVisuals.ActionButton("Match package…", $"for {row.Name}");
        map.IsEnabled = viewModel.IsUpdateSourceEnabled && !sourceEvidenceUnavailable && !HasActiveOperation;
        map.Click += async (_, _) =>
        {
            string sourceId = mappingSource.SelectedIndex >= 0 && mappingSource.SelectedIndex < mappingSources.Length ? mappingSources[mappingSource.SelectedIndex] : "winget";
            string packageId = string.IsNullOrWhiteSpace(mappingPackage.Text) ? detail?.PackageId ?? string.Empty : mappingPackage.Text.Trim();
            if (packageId.Length == 0) { await ShowUpdateOutcomeAsync("Package ID needed", "Enter the exact package ID first. Nothing was saved."); return; }
            await PromptAndCreateManualMappingAsync(row, sourceId, packageId);
        };
        StackPanel matchForm = new() { Spacing = StewardSpacing.Medium };
        matchForm.Children.Add(PageCopy(viewModel.IsUpdateSourceEnabled
            ? "If you know which package is this app, enter its exact ID. PC Steward checks it before saving. You can undo the match, and it doesn't start an update."
            : "Turn on update checks to match this app to a package."));
        matchForm.Children.Add(mappingSource);
        matchForm.Children.Add(mappingPackage);
        matchForm.Children.Add(map);
        Expander matchYourself = new() { Header = AppDetailSummaryPresenter.MatchItYourselfLabel, Content = matchForm, IsExpanded = detailMatchFormOpen, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        matchYourself.Expanding += (_, _) => detailMatchFormOpen = true;
        matchYourself.Collapsed += (_, _) => detailMatchFormOpen = false;
        void OpenMatchForm()
        {
            matchYourself.IsExpanded = true;
            // The form is laid out once the expander opens, so it is scrolled to and focused after that layout pass.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (windowClosed) return;
                matchYourself.StartBringIntoView();
                mappingPackage.Focus(FocusState.Programmatic);
            });
        }

        // The lead card: where the app stands, in one sentence, with at most one main button.
        StackPanel lead = new() { Spacing = StewardSpacing.Medium };
        Border state = StewardVisuals.Badge(summary.Headline, summary.Glyph, StewardStatus.ToneName(summary.Tone), $"{row.Name}: {summary.Headline}");
        state.HorizontalAlignment = HorizontalAlignment.Left;
        lead.Children.Add(state);
        lead.Children.Add(new TextBlock { Text = summary.Sentence, Style = AppStyle("StewardBodyLargeStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        foreach (string note in summary.Notes) lead.Children.Add(StewardVisuals.IconLine("", note, styleKey: "StewardDescriptionStyle"));
        ComboBox? candidatePicker = null;
        if (summary.Action == AppDetailAction.ChooseMatch && detail is not null)
        {
            candidatePicker = new ComboBox
            {
                Header = "Possible matches",
                ItemsSource = detail.ReviewCandidates.ToArray(),
                DisplayMemberPath = nameof(ReviewCandidateChoice.DisplayLabel),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(candidatePicker, $"Possible matches for {row.Name}");
            lead.Children.Add(candidatePicker);
        }
        StewardWrapPanel leadActions = StewardVisuals.ActionRow();
        switch (summary.Action)
        {
            case AppDetailAction.Update:
                // The same review as the Updates page, for this app alone. Nothing is installed until it is confirmed there
                // (FR-UPD-01, AC-UPD-01), and the review says whether Windows will ask for administrator permission.
                Button update = StewardVisuals.PrimaryButton(preparingReview ? PreparingReviewLabel : summary.ActionLabel, $"{row.Name} to {detail?.CandidateVersion}");
                update.IsEnabled = !HasActiveOperation && reviewDecision is null;
                AutomationProperties.SetHelpText(update, "Opens the review for this app, which says whether Windows will ask for administrator permission. Nothing is installed until you confirm there.");
                update.Click += async (_, _) => await ReviewAndExecuteAsync([row]);
                leadActions.Children.Add(update);
                break;
            case AppDetailAction.ConfirmMatch when detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true }:
                Button same = StewardVisuals.PrimaryButton(summary.ActionLabel, $"({detail.PackageId} is {row.Name})");
                same.IsEnabled = !HasActiveOperation;
                AutomationProperties.SetHelpText(same, "Opens a confirmation. You can undo it later, and it doesn't start an update.");
                same.Click += async (_, _) => await PromptAndConfirmPackageIdentityAsync(row, detail);
                leadActions.Children.Add(same);
                break;
            case AppDetailAction.ChooseMatch when candidatePicker is not null:
                Button useMatch = StewardVisuals.PrimaryButton(summary.ActionLabel, $"for {row.Name}");
                useMatch.IsEnabled = !HasActiveOperation;
                useMatch.Click += async (_, _) =>
                {
                    if (candidatePicker.SelectedItem is not ReviewCandidateChoice candidate) return;
                    await PromptAndCreateManualMappingAsync(row, candidate.SourceId, candidate.PackageId);
                };
                leadActions.Children.Add(useMatch);
                break;
            case AppDetailAction.CheckAgain:
                leadActions.Children.Add(CreateSourceEvidenceRefreshButton(row, summary.ActionLabel));
                break;
            case AppDetailAction.TurnOnUpdateChecks:
                // The existing consent: it shows what is shared, defaults to keeping checks off and starts nothing else.
                Button turnOn = StewardVisuals.PrimaryButton(summary.ActionLabel, "shows what is shared and asks you first");
                AutomationProperties.SetHelpText(turnOn, HomePresenter.TurnOnUpdateChecksHelp);
                turnOn.IsEnabled = !HasActiveOperation;
                turnOn.Click += async (_, _) => await ChangeWinGetTrustedSourcesAsync(true);
                leadActions.Children.Add(turnOn);
                break;
            case AppDetailAction.UndoDecision when summary.DecisionId is { } decisionId:
                Button undo = StewardVisuals.ActionButton(summary.ActionLabel, $"for {row.Name}");
                undo.IsEnabled = !HasActiveOperation;
                undo.Click += async (_, _) => await ReverseDecisionsAsync([decisionId], []);
                leadActions.Children.Add(undo);
                break;
        }
        // Every other decision is in one menu. Each entry opens the same confirmation as the lists and the Updates page
        // (audit P-M1); none of them can start an update.
        MenuFlyout moreMenu = new();
        foreach (UpdateRowActionPresentation action in summary.MoreActions)
        {
            MenuFlyoutItem item = new() { Text = action.Label, Icon = new FontIcon { Glyph = action.Glyph }, IsEnabled = !HasActiveOperation };
            AutomationProperties.SetName(item, $"{action.Label}: {row.Name}");
            item.Click += async (_, _) =>
            {
                switch (action.Action)
                {
                    case UpdateRowAction.PinCurrentVersion: await PromptAndPinCurrentVersionAsync(row); break;
                    case UpdateRowAction.IgnoreApp: await PromptAndIgnoreProductAsync(row); break;
                    case UpdateRowAction.SkipVersion when action.Version is { } version: await PromptAndSkipVersionAsync(row, version); break;
                }
            };
            moreMenu.Items.Add(item);
        }
        if (!summary.OffersMatchItYourself && !sourceEvidenceUnavailable)
        {
            MenuFlyoutItem matchItem = new() { Text = $"{AppDetailSummaryPresenter.MatchItYourselfLabel}…" };
            AutomationProperties.SetName(matchItem, $"{AppDetailSummaryPresenter.MatchItYourselfLabel}: {row.Name}");
            matchItem.Click += (_, _) => OpenMatchForm();
            moreMenu.Items.Add(matchItem);
        }
        if (moreMenu.Items.Count > 0)
        {
            DropDownButton more = new() { Content = "More", Flyout = moreMenu };
            StewardVisuals.Named(more, "More", $"options for {row.Name}");
            leadActions.Children.Add(more);
        }
        if (summary.OffersMatchItYourself)
        {
            HyperlinkButton matchLink = new() { Content = AppDetailSummaryPresenter.MatchItYourselfLabel, VerticalAlignment = VerticalAlignment.Center };
            StewardVisuals.Named(matchLink, AppDetailSummaryPresenter.MatchItYourselfLabel, $"({row.Name})");
            matchLink.Click += (_, _) => OpenMatchForm();
            leadActions.Children.Add(matchLink);
        }
        if (leadActions.Children.Count > 0) lead.Children.Add(leadActions);
        panel.Children.Add(Card(lead));

        // The facts, in two columns that stack on a narrow window.
        List<FrameworkElement> factCards = [];
        StackPanel onThisPc = new() { Spacing = StewardSpacing.Small };
        onThisPc.Children.Add(SectionHeading(AppDetailSummaryPresenter.OnThisPcTitle));
        foreach (AppDetailFact fact in summary.OnThisPc) onThisPc.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        factCards.Add(Card(onThisPc));
        if (summary.FromSource.Count > 0)
        {
            StackPanel fromSource = new() { Spacing = StewardSpacing.Small };
            fromSource.Children.Add(SectionHeading(summary.SourceTitle));
            foreach (AppDetailFact fact in summary.FromSource) fromSource.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
            factCards.Add(Card(fromSource));
        }
        panel.Children.Add(StewardVisuals.ResponsiveColumns([.. factCards], 340, StewardSpacing.Medium, factCards.Count));

        if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId) && ChocolateyPackageUpdateAdmission.IsOwnerApproved &&
            detail?.PackageId is { } chocolateyPackageId && ChocolateyPackageUpdateReviewSession.IsSelectablePackage(chocolateyPackageId))
        {
            // Chocolatey package Confirm addendum §4: with owner admission compiled in, the entry opens the package review.
            StackPanel packageUpdate = new() { Spacing = StewardSpacing.Small };
            packageUpdate.Children.Add(SectionHeading("Chocolatey reviewed update"));
            packageUpdate.Children.Add(PageCopy(ChocolateyPackageUpdateViewModel.DetailCopy));
            Button openPackageUpdate = ChocolateyPackageUpdateSurfaces.EntryButton(chocolateyPackageId);
            openPackageUpdate.Click += async (_, _) => await ShowChocolateyPackageUpdateAsync(chocolateyPackageId);
            packageUpdate.Children.Add(openPackageUpdate);
            panel.Children.Add(Card(packageUpdate));
        }
        else if (ChocolateyReviewedRouteReviewViewModel.AppliesTo(detail?.SourceId))
        {
            StackPanel reviewedRoute = new() { Spacing = StewardSpacing.Small };
            reviewedRoute.Children.Add(SectionHeading("Chocolatey reviewed update"));
            reviewedRoute.Children.Add(PageCopy("PC Steward does not allow this Chocolatey update route yet. You can read what it would disclose before any confirmation; it cannot start an update."));
            Button openReviewedRoute = ReviewedRouteSurfaces.ChocolateyReviewEntryButton();
            openReviewedRoute.Click += async (_, _) => await ShowChocolateyReviewedRouteAsync();
            reviewedRoute.Children.Add(openReviewedRoute);
            panel.Children.Add(Card(reviewedRoute));
        }

        if (ChocolateyClientUpdateViewModel.IsClientRow(row))
        {
            StackPanel clientUpdate = new() { Spacing = StewardSpacing.Small };
            clientUpdate.Children.Add(SectionHeading(ChocolateyClientUpdateViewModel.DetailHeading));
            clientUpdate.Children.Add(PageCopy(ChocolateyClientUpdateViewModel.CurrentDetailCopy));
            ChocolateyClientUpdateViewModel clientSummary = ChocolateyClientUpdateView();
            clientUpdate.Children.Add(StewardVisuals.EvidenceRow("Status", clientSummary.Summary));
            clientUpdate.Children.Add(ChocolateyClientUpdateEntry(clientSummary));
            panel.Children.Add(Card(clientUpdate));
        }

        // FR-HIS-02: the app's own timeline as a list, newest first.
        StackPanel history = new() { Spacing = 0 };
        history.Children.Add(SectionHeading("History"));
        IReadOnlyList<ProductTimelineItem> timeline = AppDetailPresenter.Timeline(detailTimeline);
        if (timeline.Count == 0) history.Children.Add(PageCopy("Nothing is recorded for this app yet."));
        foreach (ProductTimelineItem item in timeline)
        {
            Border entry = StewardVisuals.ActivityRow(item.When, item.Glyph, item.Title, item.Detail ?? string.Empty);
            AutomationProperties.SetName(entry, item.AccessibleName);
            history.Children.Add(entry);
        }
        panel.Children.Add(Card(history));

        panel.Children.Add(matchYourself);

        // Technical details: what the matcher recorded and the raw evidence, as structured sections (audit P-L6). This is where
        // package evidence, recorded reasons and source plumbing live, so the top of the page can stay short.
        StackPanel technical = new() { Spacing = StewardSpacing.Medium };
        technical.Children.Add(SectionHeading("Why PC Steward says this", AutomationHeadingLevel.Level3));
        technical.Children.Add(StewardVisuals.SelectableText(detail?.Explanation ?? "This app was found on this PC. No update source identifies it exactly yet."));
        foreach (string reason in ReviewReasonText.DescribeAll(detail?.ReviewReasons ?? [])) technical.Children.Add(StewardVisuals.SelectableText($"• {reason}"));
        technical.Children.Add(StewardVisuals.EvidenceRow("Status", StatusVocabulary.For(row.Status).Label));
        technical.Children.Add(StewardVisuals.EvidenceRow("Found by", $"{StewardPresentation.Provider(row.Provider)} · {StewardPresentation.Scope(row.Scope)}"));
        technical.Children.Add(StewardVisuals.EvidenceRow("Update source", detail?.SourceId ?? "None matched"));
        technical.Children.Add(StewardVisuals.EvidenceRow("Package ID", detail?.PackageId ?? "None matched"));
        technical.Children.Add(StewardVisuals.EvidenceRow("Last check", detail?.Freshness ?? "Local-only: update checks haven't run for this app."));
        technical.Children.Add(SectionHeading("Installed instances", AutomationHeadingLevel.Level3));
        IReadOnlyList<DetailItem> instances = AppDetailPresenter.Instances(row.Instances);
        if (instances.Count == 0) technical.Children.Add(PageCopy("No installed instance details are recorded."));
        foreach (DetailItem instance in instances) technical.Children.Add(DetailItemCard(instance));
        technical.Children.Add(SectionHeading("Packages found in your update sources", AutomationHeadingLevel.Level3));
        IReadOnlyList<DetailItem> candidates = AppDetailPresenter.Candidates(detail?.ReviewCandidates ?? []);
        if (candidates.Count == 0) technical.Children.Add(PageCopy("No packages were found in your update sources."));
        foreach (DetailItem candidate in candidates) technical.Children.Add(DetailItemCard(candidate));
        technical.Children.Add(PageCopy($"Technical records kept for troubleshooting: {detailRaw.Count.ToString("N0", CultureInfo.CurrentCulture)}."));
        Expander disclosure = new() { Header = "Technical details", Content = technical, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(disclosure);
        return panel;
    }

    /// <summary>One structured item (an instance or a candidate): a title and its facts, with selectable values.</summary>
    private static Border DetailItemCard(DetailItem item)
    {
        StackPanel content = new() { Spacing = 0 };
        content.Children.Add(new TextBlock { Text = item.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(0, 0, 0, StewardSpacing.XSmall) });
        foreach (DetailFact fact in item.Facts) content.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        Border card = new() { BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(StewardSpacing.XSmall), Padding = new Thickness(StewardSpacing.Medium), Child = content };
        AutomationProperties.SetName(card, item.Title);
        return card;
    }

    private async Task ShowDetailsAsync(InventoryRow row)
    {
        detailRow = row;
        detailMatchFormOpen = false;
        try { detailRaw = await DataOperationLease.RunReadAsync(token => workflow.GetRawDiagnosticsAsync(row.ProductId, token), CancellationToken.None); detailTimeline = await DataOperationLease.RunReadAsync(token => workflow.GetTimelineAsync(row.StableProductKey, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        // UI review 2026-11-13 (item 19): on a wide window a list keeps its place and the details open beside it. The list is
        // not rebuilt, so it stays where the owner scrolled it; focus stays on the list and a screen reader is told what opened.
        if (shellContent is not null && PageLayout.ShowsDetailsBeside(navigation.Current, shellContent.ActualWidth))
        {
            detailPaneOpen = true;
            ShowDetailPane(rebuild: true);
            StewardVisuals.Announce(detailPaneScroll, PageLayout.DetailsShownAnnouncement(row.Name), "details-beside-list");
            return;
        }
        NavigateTo(ShellPage.Detail);
    }

    /// <summary>
    /// UI review 2026-11-13 (item 19; FR-UI-01/04/05): shows, refreshes or hides the details column beside a list. The column
    /// holds exactly what the details page builds (<see cref="BuildProductDetailPage"/>), so every action in it is the same
    /// action with the same confirmation. It is shown only for an app the owner opened from Updates or Installed apps while the
    /// content area is wide enough (<see cref="PageLayout.ShowsDetailsBeside"/>); otherwise it is hidden and emptied.
    /// </summary>
    private void ShowDetailPane(bool rebuild)
    {
        if (detailPaneHost is null || detailPaneScroll is null || shellContent is null) return;
        if (!detailPaneOpen || detailRow is null || !PageLayout.ShowsDetailsBeside(navigation.Current, shellContent.ActualWidth))
        {
            detailPaneHost.Visibility = Visibility.Collapsed;
            detailPaneScroll.Content = null;
            detailPaneKey = null;
            // A line that confirmed a choice in the column goes with the column, as it does when its page is left.
            if (decisionNoticeInDetailPane) { decisionNotice = null; decisionNoticeInDetailPane = false; }
            return;
        }
        detailPaneHost.Width = PageLayout.DetailsPaneWidth(shellContent.ActualWidth);
        if (rebuild || detailPaneScroll.Content is null)
        {
            // The same app refreshed keeps its place in the column; another app starts at the top.
            bool sameApp = string.Equals(detailPaneKey, detailRow.StableProductKey, StringComparison.Ordinal);
            // The line that confirms a saved choice belongs to the app it was made for: another app in the column drops it.
            if (decisionNoticeInDetailPane && !sameApp) { decisionNotice = null; decisionNoticeInDetailPane = false; }
            // A line that has just been added is at the top of the column, so the column starts there and it is seen.
            double offset = sameApp && !(decisionNoticeIsNew && decisionNoticeInDetailPane) ? detailPaneScroll.VerticalOffset : 0;
            StackPanel details = BuildDetailPane(detailRow);
            detailPaneKey = detailRow.StableProductKey;
            detailPaneScroll.Content = details;
            AutomationProperties.SetName(detailPaneScroll, PageLayout.DetailsPaneName(detailRow.Name));
            StewardVisuals.FitPageColumn(detailPaneScroll);
            if (offset <= 0) detailPaneScroll.ChangeView(null, 0, null, true);
            else
            {
                // Like the page host: the rebuilt content has to be laid out before its saved offset can be reached.
                ScrollViewer column = detailPaneScroll;
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (!windowClosed && ReferenceEquals(column.Content, details)) RestoreScrollOffset(column, details, offset);
                });
            }
        }
        detailPaneHost.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The details page's own content, laid out for the column: tighter padding, a "Close details" button first, and the app's
    /// name as a level-2 heading because the list page beside it already has the level-1 title.
    /// </summary>
    private StackPanel BuildDetailPane(InventoryRow row)
    {
        StackPanel details = BuildProductDetailPage();
        details.Padding = new Thickness(StewardSpacing.XLarge, StewardSpacing.Medium, StewardSpacing.XLarge, StewardSpacing.XLarge);
        if (detailTitle is not null) AutomationProperties.SetHeadingLevel(detailTitle, AutomationHeadingLevel.Level2);
        Button close = StewardVisuals.ActionButton(PageLayout.CloseDetailsLabel, $"for {row.Name}");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (_, _) =>
        {
            detailPaneOpen = false;
            ShowDetailPane(rebuild: false);
            // The button is gone with the column, so the keyboard goes back to the page it was beside.
            FocusRegionElement(FocusRegion.Page);
        };
        details.Children.Insert(0, close);
        // FR-POL-03/05: what was just saved for this app, with Undo, in the column the choice was made in.
        if (decisionNoticeInDetailPane && decisionNotice is { } saved) details.Children.Insert(1, DecisionNoticeBar(saved));
        return details;
    }
}
