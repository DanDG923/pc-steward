using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    /// FR-UI-01, AC-UX-03 (Task 73 Phase 3; ADR-030 decision 5; audit P-L7): saved filters (All, Updates, Needs review, No update source,
    /// Pinned &amp; ignored) with counts over every saved app, search, and the detailed filters behind "Filters". The list stays
    /// virtualized with a bounded height inside the page host; phase 5 grows that height to the bottom of the window. UI review 2026-11-13
    /// (item 14): the Repository apps page is now the "Has an update source" tab here.
    /// UI polish phase 4 (approved target T-Installed): a quiet freshness line, filter tabs, one toolbar (search, sort, Filters and
    /// the count), column headings, and the list in one card. "No update source" is plain text with a calm footnote, not a badge
    /// on every row. Owner decision 2026-11-11: every inventory record keeps its own row here (Updates shows same-app records once).
    /// </summary>
    private StackPanel BuildInstalledAppsPage()
    {
        StackPanel panel = CreatePagePanel();
        Button scan = StewardVisuals.ActionButton("Scan again", "(reads the installed apps on this PC)");
        scan.IsEnabled = !HasActiveOperation;
        scan.AccessKey = "S";
        scan.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
        panel.Children.Add(StewardVisuals.PageHeader("Installed apps", InstalledAppsFilters.PageDescription, null, scan));
        // When the inventory was last read is a fact, not a warning, so it is a quiet line rather than a banner.
        panel.Children.Add(StewardVisuals.FootNote("\uE81C", InstalledAppsFilters.ScannedText(viewModel.LastScanAt, DateTimeOffset.UtcNow)));

        // Saved filters: the same words as the status chips, and counts over every saved app ("All" really is all).
        // Audit P-M9: filter tabs wrap instead of scrolling sideways or clipping.
        List<Button> savedFilters = [];
        // UI review 2026-11-13 (fault 3): the tabs count what the list can show, so system components count only when they are listed.
        // UI review 2026-11-13 (item 14): "Has an update source" lists the apps the Repository apps page used to.
        foreach (InstalledAppsFilterOption option in InstalledAppsFilters.Options(viewModel.ListedRows, viewModel.UpdateSourceKeys))
        {
            bool selected = viewModel.SavedFilter == option.Filter;
            // The selected filter stays focusable (bold label, accent bar and "Selected" item status) so a refresh can restore focus to it.
            Button filter = StewardVisuals.FilterTab(option.Label, option.Count, selected);
            AutomationProperties.SetName(filter, option.AccessibleName);
            if (selected) AutomationProperties.SetItemStatus(filter, "Selected");
            InstalledAppsFilter value = option.Filter;
            filter.Click += (_, _) => { viewModel.SetSavedFilter(value); Render(); };
            savedFilters.Add(filter);
        }
        panel.Children.Add(StewardVisuals.FilterBar(savedFilters, "Saved filters"));

        TextBox search = new() { PlaceholderText = "Search by name or publisher", Text = viewModel.SearchText, Width = 320 };
        AutomationProperties.SetName(search, "Search applications");
        // Ctrl+F focuses this box (AC-UX-01; research §2 item 3).
        pageSearchTarget = search;
        // Filters show the labels the rows show (StatusVocabulary, StewardPresentation), never raw enum or provider values.
        (string Raw, string Label)[] sourceOptions = LabelledFilterOptions(viewModel.AllRows.Select(row => row.Provider), StewardPresentation.Provider);
        (string Raw, string Label)[] scopeOptions = LabelledFilterOptions(viewModel.AllRows.Select(row => row.Scope), StewardPresentation.Scope);
        ComboBox status = FilterBox(StatusVocabulary.FilterLabelForKey(viewModel.StatusFilter), StatusVocabulary.FilterLabels);
        ComboBox publisher = FilterBox(viewModel.PublisherFilter, ["All publishers", .. viewModel.AvailablePublishers]);
        ComboBox source = FilterBox(FilterLabelFor(viewModel.SourceFilter, sourceOptions), ["All sources", .. sourceOptions.Select(option => option.Label)]);
        ComboBox scope = FilterBox(FilterLabelFor(viewModel.ScopeFilter, scopeOptions), ["All scopes", .. scopeOptions.Select(option => option.Label)]);
        ComboBox sort = FilterBox(viewModel.SortField, "Name", "Source", "Publisher");
        status.Header = "Status"; publisher.Header = "Publisher"; source.Header = "Source"; scope.Header = "Scope";
        AutomationProperties.SetName(status, "Filter applications by status"); AutomationProperties.SetName(publisher, "Filter applications by publisher"); AutomationProperties.SetName(source, "Filter applications by source"); AutomationProperties.SetName(scope, "Filter applications by scope"); AutomationProperties.SetName(sort, "Sort applications");
        foreach (ComboBox box in new[] { status, publisher, source, scope }) box.MinWidth = 200;

        // The detailed filters sit in a card that "Filters" shows and hides; it is open whenever a detailed filter is in use.
        StewardWrapPanel filterControls = StewardVisuals.ActionRow(StewardSpacing.Medium);
        foreach (ComboBox box in new[] { status, publisher, source, scope }) filterControls.Children.Add(box);
        bool detailedFiltersInUse = viewModel.StatusFilter != StatusVocabulary.AllStatusesLabel || viewModel.PublisherFilter != "All publishers" || viewModel.SourceFilter != "All sources" || viewModel.ScopeFilter != "All scopes";
        Border moreFilters = Card(filterControls);
        moreFilters.Visibility = moreAppFiltersExpanded || detailedFiltersInUse ? Visibility.Visible : Visibility.Collapsed;
        ToggleButton filtersToggle = new() { IsChecked = moreFilters.Visibility == Visibility.Visible };
        filtersToggle.Content = StewardVisuals.IconLabel("\uE71C", "Filters");
        StewardVisuals.Named(filtersToggle, "Filters", "(status, publisher, source and scope)");
        filtersToggle.Checked += (_, _) => { moreAppFiltersExpanded = true; moreFilters.Visibility = Visibility.Visible; };
        filtersToggle.Unchecked += (_, _) => { moreAppFiltersExpanded = false; moreFilters.Visibility = Visibility.Collapsed; };

        TextBlock matching = new() { Style = AppStyle("StewardBodyStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetLiveSetting(matching, AutomationLiveSetting.Polite);
        StewardWrapPanel sortGroup = StewardVisuals.ActionRow();
        sortGroup.Children.Add(new TextBlock { Text = "Sort by", Style = AppStyle("StewardBodyStyle"), VerticalAlignment = VerticalAlignment.Center });
        sortGroup.Children.Add(sort);
        StewardWrapPanel toolbar = StewardVisuals.ActionRow();
        toolbar.Children.Add(search);
        toolbar.Children.Add(sortGroup);
        toolbar.Children.Add(filtersToggle);
        toolbar.Children.Add(matching);
        // UI review 2026-11-13 (fault 3): frameworks, resource packs and Windows system packages are listed only on request, so
        // the page opens on apps the owner recognises. The switch is offered only when the inventory has some.
        int systemComponents = viewModel.SystemComponentCount;
        if (systemComponents > 0)
        {
            CheckBox showSystem = new() { Content = InstalledAppsFilters.SystemComponentsLabel(systemComponents), IsChecked = viewModel.ShowSystemComponents, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            StewardVisuals.Named(showSystem, InstalledAppsFilters.SystemComponentsLabel(systemComponents), "(frameworks and parts of Windows)");
            showSystem.Click += (_, _) => { viewModel.SetShowSystemComponents(showSystem.IsChecked == true); Render(); };
            toolbar.Children.Add(showSystem);
        }
        panel.Children.Add(toolbar);
        panel.Children.Add(moreFilters);
        // UI review 2026-11-13 (item 14): matches that only need a yes can be confirmed together, here under "Check the match".
        // The line counts the apps the list shows now, so a search or filter narrows what would be confirmed, and the dialog
        // names them. The confirmation dialog, the validation of each match and Undo on Decisions are unchanged; nothing is updated.
        InventoryRow[] currentMatches = [];
        TextBlock confirmText = new() { Foreground = StewardVisuals.Brush("StewardTextBrush"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        Button confirmAll = StewardVisuals.ActionButton(InstalledAppsFilters.ConfirmCurrentMatchesLabel, "(the listed apps that are already on the version their match lists)");
        confirmAll.IsEnabled = !HasActiveOperation;
        confirmAll.Click += async (_, _) => await ConfirmCurrentMatchesAsync(currentMatches);
        Grid.SetColumn(confirmAll, 1);
        Grid confirmRow = new() { ColumnSpacing = StewardSpacing.Large, Visibility = Visibility.Collapsed };
        confirmRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        confirmRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        confirmRow.Children.Add(confirmText);
        confirmRow.Children.Add(confirmAll);
        panel.Children.Add(confirmRow);

        // Bounded height keeps the list virtualized inside the page's own scroll host (wheel scrolling chains to the page at the ends).
        // Phase 5: the bound follows the window, so the card reaches the bottom of a tall window (never shorter than MinimumListHeight).
        ListView list = new() { Style = AppStyle("StewardListViewStyle"), ItemContainerStyle = AppStyle("StewardListItemStyle"), ItemTemplate = (DataTemplate)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardApplicationRowTemplate"] };
        AutomationProperties.SetName(list, "Installed apps list");
        AutomationProperties.SetAutomationId(list, "InstalledAppsList");
        Border listCard = new() { Style = AppStyle("StewardCardStyle"), Padding = new Thickness(0, StewardSpacing.XSmall, 0, StewardSpacing.XSmall), Child = list };
        StewardVisuals.FillViewportHeight(panel, listCard, list, MinimumListHeight);
        // "Has an update source" lists nothing while update checks are off, so its empty state says that and opens Settings.
        bool checksOff = viewModel.SavedFilter == InstalledAppsFilter.HasUpdateSource && !viewModel.IsUpdateSourceEnabled;
        Button? openSettings = null;
        if (checksOff)
        {
            openSettings = StewardVisuals.ActionButton("Open Settings", "(to turn on update checks)");
            openSettings.Click += (_, _) => NavigateTo(ShellPage.Settings);
        }
        Border empty = checksOff
            ? StewardVisuals.EmptyState("\uE721", InstalledAppsFilters.ChecksOffTitle, InstalledAppsFilters.ChecksOffMessage, openSettings)
            : StewardVisuals.EmptyState("\uE721", "No apps match", "Change the search or filters to see more apps.");
        // Column headings line up with StewardApplicationRowTemplate (32 | 3* | 1* | 1* | 1.4* | 16), inside the card's row padding.
        Grid columns = new() { Padding = new Thickness(StewardSpacing.Large, 0, StewardSpacing.Large, 0), ColumnSpacing = StewardSpacing.Large };
        foreach (GridLength width in new[] { new GridLength(32), new GridLength(3, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1.4, GridUnitType.Star), new GridLength(16) })
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        string[] headings = ["Name", "Version", "Installed for", "Status"];
        for (int index = 0; index < headings.Length; index++)
        {
            TextBlock heading = new() { Text = headings[index], Style = AppStyle("StewardCaptionStyle") };
            Grid.SetColumn(heading, index + 1);
            columns.Children.Add(heading);
        }
        AutomationProperties.SetAccessibilityView(columns, AccessibilityView.Raw);
        void ApplyFilters()
        {
            viewModel.SetFilters(search.Text, StatusVocabulary.FilterKeyForLabel(status.SelectedItem?.ToString()), publisher.SelectedItem?.ToString(), FilterRawFor(source.SelectedItem?.ToString(), sourceOptions, "All sources"), FilterRawFor(scope.SelectedItem?.ToString(), scopeOptions, "All scopes"), sort.SelectedItem?.ToString());
            RenderInventory(list);
            currentMatches = viewModel.SavedFilter != InstalledAppsFilter.NeedsReview || !viewModel.IsUpdateSourceEnabled ? [] : [.. viewModel.VisibleRows
                .Where(row => row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired))
                .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)];
            confirmText.Text = InstalledAppsFilters.ConfirmCurrentMatchesText(currentMatches.Length);
            confirmRow.Visibility = currentMatches.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            matching.Text = InstalledAppsFilters.CountText(viewModel.VisibleRows.Count, viewModel.ListedRows.Count);
            bool none = viewModel.VisibleRows.Count == 0;
            listCard.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            columns.Visibility = listCard.Visibility;
            empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => ApplyFilters(); status.SelectionChanged += (_, _) => ApplyFilters(); publisher.SelectionChanged += (_, _) => ApplyFilters(); source.SelectionChanged += (_, _) => ApplyFilters(); scope.SelectionChanged += (_, _) => ApplyFilters(); sort.SelectionChanged += (_, _) => ApplyFilters();
        list.ItemClick += async (_, args) => await ShowDetailsAsync(((ApplicationListItem)args.ClickedItem).Row);
        ApplyFilters();
        StackPanel table = new() { Spacing = 6 };
        table.Children.Add(columns);
        table.Children.Add(listCard);
        panel.Children.Add(table);
        panel.Children.Add(empty);
        // "No update source" is explained once, calmly, instead of a warning badge on every row.
        if (viewModel.AllRows.Any(row => row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged)))
            panel.Children.Add(StewardVisuals.FootNote("\uE946", InstalledAppsFilters.NoUpdateSourceNote));
        return panel;
    }

    /// <summary>The Installed apps list is never shorter than this (about five rows), so a short window scrolls the page instead.</summary>
    private const double MinimumListHeight = 320;

    private static (string Raw, string Label)[] LabelledFilterOptions(IEnumerable<string> rawValues, Func<string, string> label) => rawValues
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(raw => (Raw: raw, Label: label(raw)))
        .GroupBy(option => option.Label, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static string FilterLabelFor(string raw, (string Raw, string Label)[] options) =>
        options.FirstOrDefault(option => string.Equals(option.Raw, raw, StringComparison.OrdinalIgnoreCase)).Label ?? raw;

    private static string FilterRawFor(string? label, (string Raw, string Label)[] options, string all) =>
        options.FirstOrDefault(option => string.Equals(option.Label, label, StringComparison.Ordinal)).Raw ?? all;
}
