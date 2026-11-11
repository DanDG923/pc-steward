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
    private StackPanel BuildRepositoryAppsPage()
    {
        StackPanel panel = CreatePagePanel(960);
        IReadOnlyList<InventoryRow> managedRows = viewModel.RepositoryManagedRows;
        int upToDate = managedRows.Count(row => row.Status == "Current");
        int updatesAvailable = managedRows.Count(row => row.Status == "VerifiedUpdate");
        int reviewRequired = managedRows.Count(row => row.Status == "ReviewRequired");
        int unavailable = managedRows.Count(row => row.Status == "FailedUnavailable");
        panel.Children.Add(StewardVisuals.PageHeader("Repository apps", "Installed apps that a package source you turned on can identify exactly."));
        panel.Children.Add(StewardVisuals.InformationBanner("Package sources", viewModel.UpdateFreshnessLabel, viewModel.IsUpdateSourceEnabled ? "information" : "attention"));

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        Button refresh = StewardVisuals.ActionButton(viewModel.IsCheckingUpdates ? "Checking package sources…" : "Refresh package sources");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting;
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        actions.Children.Add(refresh);
        if (!viewModel.IsUpdateSourceEnabled)
        {
            Button settings = StewardVisuals.ActionButton("Open source settings");
            settings.Click += (_, _) => NavigateTo(ShellPage.Settings);
            actions.Children.Add(settings);
        }
        panel.Children.Add(actions);

        if (managedRows.Count == 0)
        {
            string empty = viewModel.IsUpdateSourceEnabled
                ? "No installed app is identified exactly by your package sources yet. Refresh package sources to update this view; nothing is downloaded or updated."
                : "Package sources are off, so PC Steward can't tell which apps they manage or whether they're up to date. Turn on trusted sources in Settings, then refresh when you choose.";
            panel.Children.Add(StewardVisuals.EmptyState("\uE73E", "No repository apps yet", empty));
        }
        else
        {
            panel.Children.Add(PageCopy($"{managedRows.Count:N0} repository-managed app{(managedRows.Count == 1 ? string.Empty : "s")}: {upToDate} up to date, {updatesAvailable} update{(updatesAvailable == 1 ? string.Empty : "s")} available, {reviewRequired} requiring review, {unavailable} with update evidence unavailable."));
            foreach (InventoryRow row in managedRows)
            {
                UpdateDetail detail = viewModel.UpdateDetails[row.StableProductKey];
                StackPanel current = new() { Spacing = StewardSpacing.XSmall };
                Grid heading = new() { ColumnSpacing = 12 };
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                heading.Children.Add(SectionHeading(row.Name, AutomationHeadingLevel.Level3));
                Border badge = StewardVisuals.StatusBadge(row.Status); Grid.SetColumn(badge, 1); heading.Children.Add(badge);
                current.Children.Add(heading);
                current.Children.Add(PageCopy(row.Publisher));
                current.Children.Add(StewardVisuals.SelectableText($"Installed version: {row.Version}{(detail.CandidateVersion is null || detail.CandidateVersion == row.Version ? string.Empty : $" · source version: {detail.CandidateVersion}")}"));
                current.Children.Add(StewardVisuals.SelectableText($"Source: {detail.SourceId} · Package ID: {detail.PackageId}"));
                current.Children.Add(PageCopy(detail.Freshness));
                Button evidence = StewardVisuals.ActionButton("View details", $"for {row.Name}");
                evidence.Click += async (_, _) => await ShowDetailsAsync(row);
                current.Children.Add(evidence);
                panel.Children.Add(Card(current));
            }
        }
        return panel;
    }
}
