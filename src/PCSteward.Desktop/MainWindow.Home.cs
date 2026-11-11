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
    /// FR-UI-01/05/06 (Task 73 Phase 3, ADR-030 concept A; audit P-H4, P-M4): one status sentence with freshness in words, a primary
    /// "Review updates" and a secondary "Check for updates" (which never elevates), the compact attention view, and tiles — real
    /// buttons — that open filtered pages. Every FR-UI-01 count appears once (hero or tile); nothing restates it.
    /// </summary>
    private StackPanel BuildHomePage()
    {
        IReadOnlyList<UpdateJobRecord> jobs = UpdateJobHistory.Group(updateHistory);
        IReadOnlyList<ActivityEntry> activity = ActivityPresenter.Entries(jobs, scanRuns, activeDecisions, manualMappings, viewModel.AllRows);
        HomePresentation home = HomePresenter.Build(new HomeInputs(viewModel.AllRows, viewModel.UpdateDetails, viewModel.IsUpdateSourceEnabled, viewModel.Sources,
            activeDecisions, [.. jobs], activity, viewModel.LastScanAt, DateTimeOffset.UtcNow));
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageTitle("Home"));

        if (!home.HasInventory)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "(read-only scan of this PC)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("", "See what is installed", "The first scan reads Windows application records on this PC. It does not request administrator access, connect to package sources, or change software.", start));
            panel.Children.Add(StewardVisuals.InformationBanner("Private by default", "Inventory and decisions stay on this PC. Package sources remain off until you approve their disclosure."));
            return panel;
        }

        // The status hero: one sentence, freshness in words (never colour alone), one primary and one secondary action.
        StackPanel hero = new() { Spacing = StewardSpacing.Medium };
        TextBlock sentence = new() { Text = home.Hero, Style = AppStyle("StewardTitleStyle"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetHeadingLevel(sentence, AutomationHeadingLevel.Level2);
        hero.Children.Add(sentence);
        StewardWrapPanel freshness = StewardVisuals.ActionRow();
        Border freshnessChip = StewardVisuals.Badge(home.Freshness.Word, home.Freshness.Freshness == EvidenceFreshness.Live ? "" : "", StewardStatus.ToneName(home.Freshness.Tone), $"Update evidence: {home.Freshness.Word}");
        freshnessChip.VerticalAlignment = VerticalAlignment.Center;
        freshness.Children.Add(freshnessChip);
        freshness.Children.Add(new TextBlock { Text = home.Freshness.Sentence, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 720 });
        hero.Children.Add(freshness);
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        Button review = StewardVisuals.PrimaryButton(home.PrimaryLabel);
        // The presenter's name already starts with the visible label (HomeActivityDecisionsPresentationTests: label-in-name).
        AutomationProperties.SetName(review, home.PrimaryAccessibleName);
        review.AccessKey = "R";
        review.Click += (_, _) => NavigateTo(ShellPage.Updates);
        // Independent review L1: with sources off this is "Scan again" (CheckForUpdatesAsync then scans only).
        string checkLabel = viewModel.IsScanning ? "Scanning…" : viewModel.IsCheckingUpdates ? "Checking…" : home.SecondaryLabel;
        Button check = new() { Content = checkLabel, IsEnabled = !HasActiveOperation, AccessKey = AccessKeyFor(home.SecondaryLabel) };
        if (checkLabel == home.SecondaryLabel) AutomationProperties.SetName(check, home.SecondaryAccessibleName); else StewardVisuals.Named(check, checkLabel);
        AutomationProperties.SetHelpText(check, home.SecondaryHelpText);
        ToolTipService.SetToolTip(check, home.SecondaryHelpText);
        check.Click += async (_, _) => await CheckForUpdatesAsync();
        actions.Children.Add(review);
        actions.Children.Add(check);
        // The last scan's results page stays reachable from Home (audit P-H2), as a quiet link rather than a third button.
        HyperlinkButton results = new() { Content = "Open scan results", VerticalAlignment = VerticalAlignment.Center };
        StewardVisuals.Named(results, "Open scan results");
        results.Click += (_, _) => NavigateTo(ShellPage.Results);
        actions.Children.Add(results);
        hero.Children.Add(actions);
        if (home.SecondaryCaption.Length != 0)
            hero.Children.Add(new TextBlock { Text = home.SecondaryCaption, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Card(hero));

        // FR-UI-06: the compact attention view — failed or unverified updates and pending restarts, newest first.
        if (home.Attention.Count != 0)
        {
            StackPanel attention = new() { Spacing = StewardSpacing.Small };
            attention.Children.Add(SectionHeading("Needs your attention"));
            foreach (HomeAttentionItem item in home.Attention)
            {
                Grid line = new() { ColumnSpacing = StewardSpacing.Medium };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel copy = new() { Spacing = StewardSpacing.XSmall };
                // Independent review L6 (ADR-021 best effort): a Grid has no automation peer, so the line's name goes on its first TextBlock.
                TextBlock appName = new() { Text = item.AppName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                AutomationProperties.SetName(appName, item.AccessibleName);
                copy.Children.Add(appName);
                copy.Children.Add(new TextBlock { Text = $"{item.Summary} · {item.When}", Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
                line.Children.Add(copy);
                Border chip = StewardVisuals.Badge(item.OutcomeLabel, item.Glyph, StewardStatus.ToneName(item.Tone), $"{item.AppName}: {item.OutcomeLabel}");
                chip.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(chip, 1); line.Children.Add(chip);
                attention.Children.Add(line);
            }
            // Independent review L3: the filter opened includes every kind of item listed (Updates when a restart is pending).
            Button openActivity = new() { Content = "View in Activity" };
            AutomationProperties.SetName(openActivity, home.AttentionLinkAccessibleName);
            ActivityFilter attentionFilter = home.AttentionActivityFilter;
            openActivity.Click += (_, _) => { activityFilter = attentionFilter; NavigateTo(ShellPage.Activity); };
            attention.Children.Add(openActivity);
            panel.Children.Add(Card(attention));
        }

        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Update history unavailable", "PC Steward couldn't read its recorded update history just now, so restart and attention counts may be incomplete. Nothing was changed.", "neutral"));

        // Tiles: real buttons whose accessible names start with the visible title and say where they lead.
        Grid tiles = new() { ColumnSpacing = StewardSpacing.Medium, RowSpacing = StewardSpacing.Medium };
        const int TileColumns = 3;
        for (int column = 0; column < TileColumns; column++) tiles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int index = 0; index < home.Tiles.Count; index++)
        {
            if (index % TileColumns == 0) tiles.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Button tile = HomeTileButton(home.Tiles[index]);
            Grid.SetColumn(tile, index % TileColumns); Grid.SetRow(tile, index / TileColumns);
            tiles.Children.Add(tile);
        }
        panel.Children.Add(tiles);
        return panel;
    }

    /// <summary>A Home tile: a real Button (keyboard, Narrator and focus visuals for free) that opens its filtered page.</summary>
    private Button HomeTileButton(HomeTile tile)
    {
        (Brush foreground, _) = StewardVisuals.ToneBrushes(StewardStatus.ToneName(tile.Tone));
        StackPanel content = new() { Spacing = StewardSpacing.XSmall };
        StackPanel heading = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        heading.Children.Add(new FontIcon { Glyph = tile.Glyph, FontSize = 16, Foreground = foreground });
        heading.Children.Add(new TextBlock { Text = tile.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = tile.Value, Style = AppStyle("StewardSubtitleStyle"), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = tile.Description, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        Button button = new()
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(StewardSpacing.Large)
        };
        AutomationProperties.SetName(button, tile.AccessibleName);
        HomeTileTarget target = tile.Target;
        button.Click += (_, _) =>
        {
            // Opening a tile only navigates with a filter; it never scans, checks, approves or updates anything.
            if (target.AppsFilter is { } appsFilter) viewModel.SetSavedFilter(appsFilter);
            if (target.ActivityFilter is { } filter) activityFilter = filter;
            NavigateTo(target.Page);
        };
        return button;
    }
}
