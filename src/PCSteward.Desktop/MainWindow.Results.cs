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
    private StackPanel BuildResultsPage()
    {
        StackPanel panel = CreatePagePanel();
        int actionable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString());
        int decisions = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString());
        int unavailable = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable.ToString());
        int unmanaged = viewModel.AllRows.Count(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged.ToString());
        panel.Children.Add(StewardVisuals.PageHeader("Scan results", "What the last scan found, grouped by what you can do next. Nothing updates from this page.", "Last scan"));
        panel.Children.Add(StewardVisuals.InformationBanner("Last scan", viewModel.FreshnessLabel, viewModel.LastScanAt is null ? "attention" : "success"));
        StewardWrapPanel filters = StewardVisuals.ActionRow();
        foreach ((ResultsFilter option, string label) in new[]
        {
            (ResultsFilter.All, $"All results {actionable + decisions + unavailable + unmanaged}"),
            (ResultsFilter.Actionable, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate).Label} {actionable}"),
            (ResultsFilter.NeedsDecision, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired).Label} {decisions}"),
            (ResultsFilter.Unavailable, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable).Label} {unavailable}"),
            (ResultsFilter.Unmanaged, $"{StatusVocabulary.For(PCSteward.Domain.UpdateIntelligenceStatus.Unmanaged).Label} {unmanaged}")
        })
        {
            // The selected filter stays focusable (accent style plus "Selected" item status) so a refresh can restore focus to it.
            Button filter = resultsFilter == option ? StewardVisuals.PrimaryButton(label, "scan results") : StewardVisuals.ActionButton(label, "scan results");
            if (resultsFilter == option) AutomationProperties.SetItemStatus(filter, "Selected");
            filter.Click += (_, _) => { resultsFilter = option; Render(); };
            filters.Children.Add(filter);
        }
        panel.Children.Add(filters);
        InventoryRow[] currentIdentityCandidates = viewModel.AllRows
            .Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString())
            .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)
            .ToArray();
        // Audit P-M3: one verb for one action — this and the Review page both "Confirm" current package identities.
        Button confirmAllCurrent = StewardVisuals.ActionButton(
            currentIdentityCandidates.Length == 0 ? "Confirm all current package identities" : $"Confirm all {currentIdentityCandidates.Length} current package identities",
            "that the source reports as already installed");
        confirmAllCurrent.IsEnabled = currentIdentityCandidates.Length > 0;
        confirmAllCurrent.Click += async (_, _) => await ConfirmCurrentPackageIdentitiesAsync(currentIdentityCandidates);
        panel.Children.Add(confirmAllCurrent);
        if (currentIdentityCandidates.Length == 0) panel.Children.Add(PageCopy("No package identities are waiting to be confirmed as current."));
        InventoryRow[] resultRows = viewModel.AllRows.Where(row => row.Status is "VerifiedUpdate" or "ReviewRequired" or "FailedUnavailable" or "Unmanaged")
            .Where(row => resultsFilter switch
            {
                ResultsFilter.Actionable => row.Status == "VerifiedUpdate",
                ResultsFilter.NeedsDecision => row.Status == "ReviewRequired",
                ResultsFilter.Unavailable => row.Status == "FailedUnavailable",
                ResultsFilter.Unmanaged => row.Status == "Unmanaged",
                _ => true
            }).ToArray();
        if (resultRows.Length == 0) panel.Children.Add(StewardVisuals.EmptyState("\uE711", "No results match this view", "Choose another filter, or open Installed apps to see everything."));
        foreach (InventoryRow row in resultRows)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel finding = new() { Spacing = StewardSpacing.XSmall };
            finding.Children.Add(SectionHeading(row.Name, AutomationHeadingLevel.Level3));
            finding.Children.Add(StewardVisuals.StatusBadge(row.Status, $"{row.Name}: {StewardStatus.From(row.Status).Label}"));
            finding.Children.Add(PageCopy($"{StewardPresentation.Provider(row.Provider)} · {StewardPresentation.Scope(row.Scope)}"));
            finding.Children.Add(PageCopy(row.Status switch
            {
                "VerifiedUpdate" => "View the details, then review the update on the Updates page. Nothing updates until you confirm it.",
                "ReviewRequired" => "Check the details and choose what to do. It can't be updated until the match is resolved.",
                "FailedUnavailable" => "PC Steward couldn't reach the update source for this app. Check again to retry; there is no decision or update to approve.",
                _ => "Found on this PC. PC Steward doesn't know where to get updates for it, which isn't a safety problem."
            }));
            if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired))
            {
                AddReviewDecisionActions(finding, row, detail);
            }
            else if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable))
            {
                finding.Children.Add(PageCopy(detail?.ActionState ?? "Check again to retry."));
                StewardWrapPanel actions = StewardVisuals.ActionRow();
                if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled) actions.Children.Add(CreateSourceEvidenceRefreshButton(row));
                Button open = StewardVisuals.ActionButton("View details", $"for {row.Name} (update evidence unavailable)"); open.Click += async (_, _) => await ShowDetailsAsync(row); actions.Children.Add(open);
                finding.Children.Add(actions);
            }
            else
            {
                Button open = StewardVisuals.ActionButton("View details", $"for {row.Name}"); open.Click += async (_, _) => await ShowDetailsAsync(row); finding.Children.Add(open);
            }
            panel.Children.Add(Card(finding));
        }
        if (downloadCache.RecoverableBytes > 0)
        {
            Button storage = StewardVisuals.ActionButton($"Open storage: {DisplayFormat.Size(downloadCache.RecoverableBytes)} of downloaded files can be cleared"); storage.Click += (_, _) => NavigateTo(ShellPage.Storage); panel.Children.Add(storage);
        }
        Button review = StewardVisuals.ActionButton("Review decisions"); review.Click += (_, _) => NavigateTo(ShellPage.Review); panel.Children.Add(review);
        return panel;
    }
}
