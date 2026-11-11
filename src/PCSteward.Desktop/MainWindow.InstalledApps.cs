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
    /// FR-UI-01, AC-UX-03 (Task 73 Phase 3; ADR-030 decision 5; audit P-L7): saved filters (All, Updates, Needs review, Unmanaged,
    /// Pinned &amp; ignored) with counts over every saved app, search, and the detailed filters behind "More filters". The list stays
    /// virtualized with a bounded height inside the page host. Repository apps stays reachable from the header.
    /// </summary>
    private StackPanel BuildInstalledAppsPage()
    {
        StackPanel panel = CreatePagePanel();
        Button scan = StewardVisuals.ActionButton("Scan again", "(read-only scan of this PC)");
        scan.IsEnabled = !HasActiveOperation;
        scan.AccessKey = "S";
        scan.Click += async (_, _) => { NavigateTo(ShellPage.Scan); await StartScanAsync(); };
        Button repositoryApps = StewardVisuals.ActionButton("Repository apps");
        repositoryApps.Click += (_, _) => NavigateTo(ShellPage.RepositoryApps);
        StewardWrapPanel headerActions = StewardVisuals.ActionRow();
        headerActions.Children.Add(repositoryApps);
        headerActions.Children.Add(scan);
        panel.Children.Add(StewardVisuals.PageHeader("Installed apps", "Search and inspect the local inventory. Filtering never changes saved data.", null, headerActions));
        panel.Children.Add(StewardVisuals.InformationBanner("Last scan", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "information"));

        // Saved filters: the same words as the status chips, and counts over every saved app ("All" really is all).
        // Audit P-M9: filter rows wrap instead of scrolling sideways or clipping.
        StewardWrapPanel savedFilters = StewardVisuals.ActionRow();
        AutomationProperties.SetName(savedFilters, "Saved filters");
        foreach (InstalledAppsFilterOption option in InstalledAppsFilters.Options(viewModel.AllRows))
        {
            bool selected = viewModel.SavedFilter == option.Filter;
            // The selected filter stays focusable (accent style plus "Selected" item status) so a refresh can restore focus to it.
            Button filter = selected ? StewardVisuals.PrimaryButton(option.DisplayLabel) : new Button { Content = option.DisplayLabel };
            AutomationProperties.SetName(filter, option.AccessibleName);
            if (selected) AutomationProperties.SetItemStatus(filter, "Selected");
            InstalledAppsFilter value = option.Filter;
            filter.Click += (_, _) => { viewModel.SetSavedFilter(value); Render(); };
            savedFilters.Children.Add(filter);
        }
        panel.Children.Add(savedFilters);
        if (viewModel.SavedFilter == InstalledAppsFilter.Unmanaged)
            panel.Children.Add(StewardVisuals.InformationBanner("Unmanaged isn't unsafe", StatusVocabulary.For(nameof(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged)).Meaning, "neutral"));

        TextBox search = new() { Header = "Search", PlaceholderText = "Name or publisher", Text = viewModel.SearchText };
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
        status.Header = "Status"; publisher.Header = "Publisher"; source.Header = "Source"; scope.Header = "Scope"; sort.Header = "Sort by";
        AutomationProperties.SetName(status, "Filter applications by status"); AutomationProperties.SetName(publisher, "Filter applications by publisher"); AutomationProperties.SetName(source, "Filter applications by source"); AutomationProperties.SetName(scope, "Filter applications by scope"); AutomationProperties.SetName(sort, "Sort applications");
        StackPanel filterContent = new() { Spacing = StewardSpacing.Medium };
        filterContent.Children.Add(search);
        Grid filters = new() { ColumnSpacing = StewardSpacing.Medium, RowSpacing = StewardSpacing.Medium };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        FrameworkElement[] filterControls = [status, publisher, source, scope, sort];
        for (int index = 0; index < filterControls.Length; index++)
        {
            Grid.SetColumn(filterControls[index], index % 3);
            Grid.SetRow(filterControls[index], index / 3);
            filters.Children.Add(filterControls[index]);
        }
        bool detailedFiltersInUse = viewModel.StatusFilter != StatusVocabulary.AllStatusesLabel || viewModel.PublisherFilter != "All publishers" || viewModel.SourceFilter != "All sources" || viewModel.ScopeFilter != "All scopes" || viewModel.SortField != "Name";
        Expander moreFilters = new() { Header = "More filters", Content = filters, IsExpanded = moreAppFiltersExpanded || detailedFiltersInUse, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        moreFilters.Expanding += (_, _) => moreAppFiltersExpanded = true;
        moreFilters.Collapsed += (_, _) => moreAppFiltersExpanded = false;
        filterContent.Children.Add(moreFilters);
        panel.Children.Add(Card(filterContent));
        // Bounded height keeps the list virtualized inside the page's own scroll host (wheel scrolling chains to the page at the ends).
        ListView list = new() { Style = AppStyle("StewardListViewStyle"), ItemContainerStyle = AppStyle("StewardListItemStyle"), ItemTemplate = (DataTemplate)global::Microsoft.UI.Xaml.Application.Current.Resources["StewardApplicationRowTemplate"], MaxHeight = 620 };
        TextBlock matching = PageCopy(string.Empty);
        AutomationProperties.SetLiveSetting(matching, AutomationLiveSetting.Polite);
        Border empty = StewardVisuals.EmptyState("", "No applications match", "Adjust the search or filters. The saved inventory has not changed.");
        void ApplyFilters()
        {
            viewModel.SetFilters(search.Text, StatusVocabulary.FilterKeyForLabel(status.SelectedItem?.ToString()), publisher.SelectedItem?.ToString(), FilterRawFor(source.SelectedItem?.ToString(), sourceOptions, "All sources"), FilterRawFor(scope.SelectedItem?.ToString(), scopeOptions, "All scopes"), sort.SelectedItem?.ToString());
            RenderInventory(list);
            matching.Text = $"{DisplayFormat.Plural(viewModel.VisibleRows.Count, "application")} {(viewModel.VisibleRows.Count == 1 ? "matches" : "match")} {InstalledAppsFilters.Label(viewModel.SavedFilter)} and the current search.";
            list.Visibility = viewModel.VisibleRows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = viewModel.VisibleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        search.TextChanged += (_, _) => ApplyFilters(); status.SelectionChanged += (_, _) => ApplyFilters(); publisher.SelectionChanged += (_, _) => ApplyFilters(); source.SelectionChanged += (_, _) => ApplyFilters(); scope.SelectionChanged += (_, _) => ApplyFilters(); sort.SelectionChanged += (_, _) => ApplyFilters();
        list.ItemClick += async (_, args) => await ShowDetailsAsync(((ApplicationListItem)args.ClickedItem).Row);
        ApplyFilters();
        panel.Children.Add(matching);
        Grid columns = new() { Padding = new Thickness(12, 0, 12, 0), ColumnSpacing = 12 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.85, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        TextBlock applicationHeader = new() { Text = "Application", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(applicationHeader, 1); columns.Children.Add(applicationHeader);
        TextBlock versionHeader = new() { Text = "Version and source", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(versionHeader, 2); columns.Children.Add(versionHeader);
        TextBlock statusHeader = new() { Text = "Status", Style = AppStyle("StewardEyebrowStyle") }; Grid.SetColumn(statusHeader, 3); columns.Children.Add(statusHeader);
        panel.Children.Add(columns);
        panel.Children.Add(list);
        panel.Children.Add(empty);
        return panel;
    }

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
