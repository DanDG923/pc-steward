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
    /// UI polish phase 4 (approved target T-Activity): filter tabs with counts, a heading per day, and one expandable row per record
    /// (tinted outcome icon, heading with the outcome in words, one sentence and the time); the facts and details are inside.
    /// </summary>
    private StackPanel BuildActivityPage()
    {
        StackPanel panel = CreatePagePanel();
        panel.Children.Add(StewardVisuals.PageHeader("Activity", ActivityPresenter.PageDescription));
        if (activityHistoryUnavailable)
            panel.Children.Add(StewardVisuals.InformationBanner("Recorded history unavailable", "PC Steward couldn't read its recorded update and scan history just now. Nothing was changed; try again later.", "neutral"));

        IReadOnlyList<ActivityEntry> entries = ActivityPresenter.Entries(UpdateJobHistory.Group(updateHistory), scanRuns, activeDecisions, manualMappings, viewModel.AllRows, packageNames: viewModel.PackageNames);
        if (entries.Count == 0)
        {
            Button start = StewardVisuals.PrimaryButton("Scan this PC", "(reads the installed apps; nothing is changed)");
            start.IsEnabled = !HasActiveOperation;
            start.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
            panel.Children.Add(StewardVisuals.EmptyState("\uE823", "No activity yet", "Run a scan or save a decision, and a record with its time appears here.", start));
        }
        else
        {
            ActivityPagePresentation page = ActivityPresenter.Build(entries, activityFilter, DateTimeOffset.UtcNow);
            List<Button> filters = [];
            foreach (ActivityFilterOption option in page.Filters)
            {
                bool selected = page.Filter == option.Filter;
                // The selected filter stays focusable (bold label, accent bar and "Selected" item status) so a refresh can restore focus to it.
                Button filter = StewardVisuals.FilterTab(option.Label, option.Count, selected);
                AutomationProperties.SetName(filter, option.AccessibleName);
                if (selected) { AutomationProperties.SetItemStatus(filter, "Selected"); pageSearchTarget = filter; }
                ActivityFilter value = option.Filter;
                filter.Click += (_, _) => { activityFilter = value; Render(); };
                filters.Add(filter);
            }
            // Activity has filters rather than a search box; Ctrl+F focuses the selected filter.
            panel.Children.Add(StewardVisuals.FilterBar(filters, "Activity filters"));
            TextBlock summary = new() { Text = page.Summary, Style = AppStyle("StewardDescriptionStyle") };
            AutomationProperties.SetLiveSetting(summary, AutomationLiveSetting.Polite);
            panel.Children.Add(summary);
            if (page.MatchingCount == 0)
                panel.Children.Add(StewardVisuals.EmptyState("\uE81C", "Nothing here", page.Filter == ActivityFilter.Problems ? "No failed or unverified updates and no incomplete scans are recorded." : "Choose another filter to see more of the recorded activity."));
            foreach (ActivityDay day in page.Days)
            {
                StackPanel daySection = new() { Spacing = StewardSpacing.Small };
                daySection.Children.Add(SectionHeading(day.Heading));
                StackPanel dayEntries = new() { Spacing = StewardSpacing.XSmall };
                foreach (ActivityEntry entry in day.Entries) dayEntries.Children.Add(ActivityEntryRow(entry));
                daySection.Children.Add(dayEntries);
                panel.Children.Add(daySection);
            }
            panel.Children.Add(StewardVisuals.FootNote("\uE946", ActivityPresenter.ExpandHint));
        }
        AddReviewedUpdateOutcomesSection(panel);
        // UI review 2026-11-13 (item 17): clearing data is not what this page is for, so its link is last and quiet.
        HyperlinkButton data = new() { Content = ActivityPresenter.DataLinkLabel, Padding = new Thickness(0) };
        StewardVisuals.Named(data, ActivityPresenter.DataLinkLabel, "(opens Data & reset)");
        data.Click += (_, _) => NavigateTo(ShellPage.Privacy);
        panel.Children.Add(data);
        return panel;
    }

    /// <summary>
    /// One Activity record: a tinted outcome icon, the heading with the outcome in words, one sentence and the time. Its facts and
    /// details (selectable, so evidence can be copied) are behind the row's expander; a record with neither is a plain row.
    /// </summary>
    private static FrameworkElement ActivityEntryRow(ActivityEntry entry)
    {
        TextBlock time = new() { Text = entry.RowTimeText, Style = AppStyle("StewardCaptionStyle"), TextWrapping = TextWrapping.NoWrap };
        Grid header = StewardVisuals.ListRow(StewardVisuals.IconWell(entry.Glyph, StewardStatus.ToneName(entry.Tone)), entry.Heading, entry.Summary, time);
        TextBlock title = header.Children.OfType<StackPanel>().First().Children.OfType<TextBlock>().First();
        // Independent review L6 (ADR-021 best effort): the entry's name goes on elements that have an automation peer.
        AutomationProperties.SetName(title, entry.AccessibleName);
        if (entry.Facts.Count == 0 && entry.Details.Count == 0)
            return new Border { Style = AppStyle("StewardRowCardStyle"), Child = header };
        header.Padding = new Thickness(0, StewardSpacing.Small, 0, StewardSpacing.Small);
        StackPanel details = new() { Spacing = 0 };
        foreach (DetailFact fact in entry.Facts.Concat(entry.Details))
        {
            Grid line = StewardVisuals.EvidenceRow(fact.Label, fact.Value);
            // Evidence can be selected and copied (for example the recorded output).
            foreach (TextBlock text in line.Children.OfType<TextBlock>()) text.IsTextSelectionEnabled = true;
            details.Children.Add(line);
        }
        Expander expander = new() { Header = header, Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(expander, entry.AccessibleName);
        AutomationProperties.SetHelpText(expander, entry.Summary);
        return expander;
    }

    /// <summary>
    /// Admission obligation 5: the read-only reviewed-update outcomes (route, verification, lingering processes, helper report).
    /// UI review 2026-11-13 (item 17): each update's own row already says what happened, so this record sits closed under the list
    /// as "Technical record of updates". Every record and every line is still rendered, and it opens by itself when it could not be
    /// read or a record still blocks other updates.
    /// </summary>
    private void AddReviewedUpdateOutcomesSection(StackPanel panel)
    {
        StackPanel outcomes = new() { Spacing = 8 };
        AutomationProperties.SetName(outcomes, ReviewedUpdateHistoryListViewModel.Heading);
        outcomes.Children.Add(PageCopy(ReviewedUpdateHistoryListViewModel.LoadingText));
        Grid header = StewardVisuals.ListRow(StewardVisuals.IconWell("\uE9F9"), ReviewedUpdateHistoryListViewModel.RecordTitle, ReviewedUpdateHistoryListViewModel.RecordDescription);
        header.Padding = new Thickness(0, StewardSpacing.Small, 0, StewardSpacing.Small);
        Expander record = new() { Header = header, Content = outcomes, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(record, ReviewedUpdateHistoryListViewModel.RecordTitle);
        AutomationProperties.SetHelpText(record, ReviewedUpdateHistoryListViewModel.RecordDescription);
        panel.Children.Add(record);
        bool loaded = false;
        record.Loaded += async (_, _) =>
        {
            if (loaded) return;
            loaded = true;
            ReviewedUpdateHistoryListViewModel history;
            try { history = await DataOperationLease.RunReadAsync(token => ReviewedUpdateHistoryListViewModel.LoadAsync(reviewedHistory, token), CancellationToken.None); }
            catch (Exception) { history = ReviewedUpdateHistoryListViewModel.Unavailable; }
            if (windowClosed) return;
            ReviewedRouteSurfaces.RenderHistory(outcomes, history);
            if (history.NeedsAttention) record.IsExpanded = true;
        };
    }
}
