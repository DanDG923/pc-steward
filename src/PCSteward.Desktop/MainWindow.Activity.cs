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
    /// FR-HIS-01–03 (Task 73 Phase 3, ADR-030): a filterable history (All, Updates, Scans, Problems) grouped by day. Each update shows
    /// its Phase 2 outcome, start and end time, provider, package ID and target version; Details shows the exit code, the failure
    /// class and the sanitized recorded output. The Task 50 reviewed-outcomes section (route, verification, running installers,
    /// helper report) follows unchanged. Read-only: nothing here deletes, exports or starts anything.
    /// </summary>
    private StackPanel BuildActivityPage()
    {
        StackPanel panel = CreatePagePanel();
        Button privacy = StewardVisuals.ActionButton("Data & reset", "(view and clear local data)");
        privacy.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        panel.Children.Add(StewardVisuals.PageHeader("Activity", "Scans, updates and decisions recorded on this PC. PC Steward does not upload activity telemetry.", null, privacy));
        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Recorded history unavailable", "PC Steward couldn't read its recorded update and scan history just now. Nothing was changed; try again later.", "neutral"));

        IReadOnlyList<ActivityEntry> entries = ActivityPresenter.Entries(UpdateJobHistory.Group(updateHistory), scanRuns, activeDecisions, manualMappings, viewModel.AllRows);
        if (entries.Count == 0)
        {
            Button start = StewardVisuals.PrimaryButton("Start local inventory", "(read-only scan of this PC)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("", "No local activity yet", "Complete an inventory or save a decision and its timestamped record will appear here.", start));
        }
        else
        {
            ActivityPagePresentation page = ActivityPresenter.Build(entries, activityFilter, DateTimeOffset.UtcNow);
            StewardWrapPanel filters = StewardVisuals.ActionRow();
            AutomationProperties.SetName(filters, "Activity filters");
            foreach (ActivityFilterOption option in page.Filters)
            {
                bool selected = page.Filter == option.Filter;
                // The selected filter stays focusable (accent style plus "Selected" item status) so a refresh can restore focus to it.
                Button filter = selected ? StewardVisuals.PrimaryButton(option.DisplayLabel) : new Button { Content = option.DisplayLabel };
                AutomationProperties.SetName(filter, option.AccessibleName);
                if (selected) { AutomationProperties.SetItemStatus(filter, "Selected"); pageSearchTarget = filter; }
                ActivityFilter value = option.Filter;
                filter.Click += (_, _) => { activityFilter = value; Render(); };
                filters.Children.Add(filter);
            }
            // Activity has filters rather than a search box; Ctrl+F focuses the selected filter.
            panel.Children.Add(filters);
            TextBlock summary = PageCopy(page.Summary);
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
            panel.Children.Add(summary);
            if (page.MatchingCount == 0)
                panel.Children.Add(StewardVisuals.EmptyState("", "Nothing here", page.Filter == ActivityFilter.Problems ? "No failed or unverified updates and no incomplete scans are recorded." : "Choose another filter to see more of the recorded activity."));
            foreach (ActivityDay day in page.Days)
            {
                panel.Children.Add(SectionHeading(day.Heading));
                StackPanel dayEntries = new() { Spacing = 0 };
                foreach (ActivityEntry entry in day.Entries) dayEntries.Children.Add(ActivityEntryRow(entry));
                panel.Children.Add(Card(dayEntries));
            }
        }
        AddReviewedUpdateOutcomesSection(panel);
        panel.Children.Add(StewardVisuals.InformationBanner("Package sources", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "neutral"));
        return panel;
    }

    /// <summary>One Activity entry: title and outcome chip (icon and text), time, summary, facts, and Details behind an expander.</summary>
    private static Border ActivityEntryRow(ActivityEntry entry)
    {
        StackPanel content = new() { Spacing = StewardSpacing.XSmall };
        Grid heading = new() { ColumnSpacing = StewardSpacing.Medium };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel titles = new() { Spacing = StewardSpacing.XSmall };
        // Independent review L6 (ADR-021 best effort): a Border has no automation peer, so the entry's name goes on its title TextBlock.
        TextBlock title = new() { Text = entry.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(title, entry.AccessibleName);
        titles.Children.Add(title);
        titles.Children.Add(new TextBlock { Text = entry.TimeText, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.Wrap });
        heading.Children.Add(titles);
        Border chip = StewardVisuals.Badge(entry.OutcomeLabel, entry.Glyph, StewardStatus.ToneName(entry.Tone), $"{entry.Title}: {entry.OutcomeLabel}");
        chip.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(chip, 1); heading.Children.Add(chip);
        content.Children.Add(heading);
        content.Children.Add(new TextBlock { Text = entry.Summary, TextWrapping = TextWrapping.Wrap });
        foreach (DetailFact fact in entry.Facts) content.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
        if (entry.Details.Count != 0)
        {
            StackPanel details = new() { Spacing = 0 };
            foreach (DetailFact fact in entry.Details)
            {
                Grid line = StewardVisuals.EvidenceRow(fact.Label, fact.Value);
                // Evidence can be selected and copied (for example the recorded output).
                foreach (TextBlock text in line.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = true;
                details.Children.Add(line);
            }
            content.Children.Add(new Expander { Header = "Details", Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        return new Border { BorderBrush = StewardVisuals.Brush("StewardBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, StewardSpacing.Medium, 0, StewardSpacing.Medium), Child = content };
    }

    /// <summary>Admission obligation 5: the read-only reviewed-update outcomes (route, verification, lingering processes, helper report).</summary>
    private void AddReviewedUpdateOutcomesSection(StackPanel panel)
    {
        panel.Children.Add(SectionHeading(ReviewedUpdateHistoryListViewModel.Heading));
        StackPanel outcomes = new() { Spacing = 8 };
        AutomationProperties.SetName(outcomes, ReviewedUpdateHistoryListViewModel.Heading);
        outcomes.Children.Add(PageCopy(ReviewedUpdateHistoryListViewModel.LoadingText));
        panel.Children.Add(outcomes);
        bool loaded = false;
        outcomes.Loaded += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            ReviewedUpdateHistoryListViewModel history;
            try { history = await DataOperationLease.RunReadAsync(token => ReviewedUpdateHistoryListViewModel.LoadAsync(reviewedHistory, token), CancellationToken.None); }
            catch (Exception) { history = ReviewedUpdateHistoryListViewModel.Unavailable; }
            if (!windowClosed) ReviewedRouteSurfaces.RenderHistory(outcomes, history);
        };
    }
}
