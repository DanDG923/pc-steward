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
    /// FR-POL-01–05, FR-HIS-02, FR-UI-04 (Task 73 Phase 3; audit P-M1, P-L6): an app's details. Pin, ignore and every mapping action
    /// use the same confirmation dialogs as the lists and the Updates page; technical details are structured sections (instances,
    /// candidates) and the app's own timeline is a list in words, not a newline dump.
    /// </summary>
    private StackPanel BuildProductDetailPage()
    {
        if (detailRow is null)
        {
            StackPanel missing = CreatePagePanel(760);
            missing.Children.Add(StewardVisuals.PageTitle("App details"));
            Button leave = StewardVisuals.ActionButton("Open Installed apps");
            leave.Click += (_, _) => NavigateTo(ShellPage.InstalledApps);
            missing.Children.Add(StewardVisuals.EmptyState("", "App details are no longer open", "Open an app from Installed apps, Updates or the scan results to see its details.", leave));
            return missing;
        }

        InventoryRow row = detailRow;
        UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
        bool cachedCurrent = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.Current) && detail?.Freshness.Contains("cached", StringComparison.OrdinalIgnoreCase) == true;
        // One status vocabulary (StatusVocabulary); cached evidence stays visibly distinct in text (FR-UI-05).
        string status = cachedCurrent ? $"{StatusVocabulary.For(row.Status).Label} · cached evidence" : StatusVocabulary.For(row.Status).Label;

        StackPanel panel = CreatePagePanel(1000);
        Grid heading = new() { ColumnSpacing = 16 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel title = new() { Spacing = 4 };
        title.Children.Add(new TextBlock { Text = "App details", Style = AppStyle("StewardCaptionStyle") });
        title.Children.Add(StewardVisuals.PageTitle(row.Name));
        StewardStatus statusPresentation = StewardStatus.From(row.Status);
        title.Children.Add(StewardVisuals.Badge(status, statusPresentation.Glyph, statusPresentation.Tone, $"{row.Name}: {status}"));
        heading.Children.Add(title);
        // The title bar's Back owns the "B" access key, so this page-level Back has none (no duplicate keys in one scope).
        Button back = StewardVisuals.ActionButton("Back", "to the previous page"); back.Click += (_, _) => { detailRow = null; if (navigation.CanGoBack) GoBack(); else NavigateTo(ShellPage.InstalledApps); }; Grid.SetColumn(back, 1); heading.Children.Add(back);
        panel.Children.Add(heading);

        Grid comparison = new() { ColumnSpacing = 12, RowSpacing = 12 };
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        comparison.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        StackPanel installed = new() { Spacing = StewardSpacing.Small };
        installed.Children.Add(SectionHeading("Installed"));
        installed.Children.Add(StewardVisuals.EvidenceRow("Version", row.Version));
        installed.Children.Add(StewardVisuals.EvidenceRow("Publisher", row.Publisher));
        installed.Children.Add(StewardVisuals.EvidenceRow("Location", $"{StewardPresentation.Scope(row.Scope)} · {StewardPresentation.Provider(row.Provider)}"));
        comparison.Children.Add(Card(installed));
        StackPanel evidence = new() { Spacing = StewardSpacing.Small };
        evidence.Children.Add(SectionHeading("Package source"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Package ID", detail?.PackageId ?? "No managed package identity"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Source", detail?.SourceId ?? "No match from a source you turned on"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Available", detail?.CandidateVersion ?? "Not established"));
        evidence.Children.Add(StewardVisuals.EvidenceRow("Confidence", ReviewReasonText.Confidence(detail?.Confidence)));
        evidence.Children.Add(PageCopy(detail?.Freshness ?? "Local-only — no source check has been run."));
        Grid.SetColumn(evidence, 1); comparison.Children.Add(Card(evidence));
        panel.Children.Add(comparison);

        StackPanel why = new() { Spacing = StewardSpacing.Small };
        why.Children.Add(SectionHeading("Why this appears"));
        why.Children.Add(PageCopy(detail?.Explanation ?? "This app was found on this PC. No package source identifies it exactly yet."));
        IReadOnlyList<string> reasons = ReviewReasonText.DescribeAll(detail?.ReviewReasons ?? []);
        foreach (string reason in reasons) why.Children.Add(PageCopy($"• {reason}"));
        panel.Children.Add(Card(why));

        StackPanel capability = new() { Spacing = StewardSpacing.Small };
        capability.Children.Add(SectionHeading("What PC Steward can do"));
        capability.Children.Add(PageCopy(detail?.ActionState ?? "Nothing happens automatically. You can record a decision or check the package source evidence."));
        capability.Children.Add(PageCopy("This page never installs, removes or changes the app."));
        panel.Children.Add(Card(capability));

        PCSteward.Application.AdministratorRequirementPresentation administratorRequirement =
            PCSteward.Application.AdministratorRequirementPresenter.BeforeReview();
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate))
        {
            panel.Children.Add(StewardVisuals.InformationBanner(administratorRequirement.Label, administratorRequirement.Explanation, "attention"));
        }

        bool sourceEvidenceUnavailable = row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.FailedUnavailable);
        StackPanel decisions = new() { Spacing = 8 };
        decisions.Children.Add(SectionHeading(sourceEvidenceUnavailable ? "Available next step" : "Decisions"));
        if (sourceEvidenceUnavailable)
        {
            decisions.Children.Add(PageCopy("Refresh the package source evidence to try again. While it's unknown whether an update exists, there's nothing to approve, map, pin or ignore."));
        }
        else
        {
            // Audit P-M1: the same decisions, with the same confirmation dialogs (and optional reason), as everywhere else.
            decisions.Children.Add(PageCopy("Each decision opens a confirmation where you can add an optional reason. Decisions are reversible on the Decisions page and never start an update."));
        }
        ComboBox? candidatePicker = null;
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0 && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy("Choose only a package you recognise as this installed app. PC Steward checks it again before saving your choice, which you can undo. No update starts."));
            candidatePicker = new ComboBox
            {
                Header = "Matching packages from your sources",
                ItemsSource = detail.ReviewCandidates.ToArray(),
                DisplayMemberPath = nameof(ReviewCandidateChoice.DisplayLabel),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            AutomationProperties.SetName(candidatePicker, $"Matching packages from your sources for {row.Name}");
            decisions.Children.Add(candidatePicker);
        }
        StewardWrapPanel decisionActions = StewardVisuals.ActionRow();
        if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisionActions.Children.Add(CreateSourceEvidenceRefreshButton(row));
        }
        if (candidatePicker is not null)
        {
            Button confirmSelected = StewardVisuals.PrimaryButton("Confirm selected identity", $"for {row.Name}");
            confirmSelected.Click += async (_, _) =>
            {
                if (candidatePicker.SelectedItem is not ReviewCandidateChoice candidate) return;
                await PromptAndCreateManualMappingAsync(row, candidate.SourceId, candidate.PackageId);
            };
            decisionActions.Children.Add(confirmSelected);
        }
        if (!sourceEvidenceUnavailable)
        {
            Button pin = StewardVisuals.ActionButton("Pin current version", $"of {row.Name} ({row.Version})");
            pin.IsEnabled = !HasActiveOperation;
            pin.Click += async (_, _) => await PromptAndPinCurrentVersionAsync(row);
            decisionActions.Children.Add(pin);
            Button ignore = StewardVisuals.ActionButton("Ignore…", $"{row.Name}: choose how long");
            ignore.IsEnabled = !HasActiveOperation;
            ignore.Click += async (_, _) => await PromptAndIgnoreProductAsync(row);
            decisionActions.Children.Add(ignore);
        }
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired) && detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true } && viewModel.IsUpdateSourceEnabled)
        {
            decisions.Children.Add(PageCopy($"Confirm this only if you recognise {detail.PackageId} from {detail.SourceId} as this installed app. You can undo it, and it doesn't start an update."));
            Button confirmPackage = StewardVisuals.ActionButton("Confirm package identity", $"({detail.PackageId} is this installed app)");
            confirmPackage.Click += async (_, _) => await PromptAndConfirmPackageIdentityAsync(row, detail);
            decisionActions.Children.Add(confirmPackage);
        }
        if (row.Status == nameof(PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate) && viewModel.IsUpdateSourceEnabled && !isExecuting)
        {
            Button review = StewardVisuals.PrimaryButton("Review and update", $"this verified package. {administratorRequirement.Label}");
            AutomationProperties.SetHelpText(review, administratorRequirement.Explanation);
            review.IsEnabled = !HasActiveOperation && reviewDecision is null;
            review.Click += async (_, _) => await ReviewAndExecuteAsync([row]);
            decisionActions.Children.Add(review);
        }
        decisions.Children.Add(decisionActions);
        panel.Children.Add(Card(decisions));

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
        history.Children.Add(SectionHeading("History for this app"));
        IReadOnlyList<ProductTimelineItem> timeline = AppDetailPresenter.Timeline(detailTimeline);
        if (timeline.Count == 0) history.Children.Add(PageCopy("No events are recorded for this app yet."));
        foreach (ProductTimelineItem item in timeline)
        {
            Border entry = StewardVisuals.ActivityRow(item.When, item.Glyph, item.Title, item.Detail ?? string.Empty);
            AutomationProperties.SetName(entry, item.AccessibleName);
            history.Children.Add(entry);
        }
        panel.Children.Add(Card(history));

        // Technical details: structured sections instead of newline-joined text (audit P-L6).
        StackPanel technical = new() { Spacing = StewardSpacing.Medium };
        technical.Children.Add(SectionHeading("Installed instances", AutomationHeadingLevel.Level3));
        IReadOnlyList<DetailItem> instances = AppDetailPresenter.Instances(row.Instances);
        if (instances.Count == 0) technical.Children.Add(PageCopy("No installed instance details are recorded."));
        foreach (DetailItem instance in instances) technical.Children.Add(DetailItemCard(instance));
        technical.Children.Add(SectionHeading("Packages found in your sources", AutomationHeadingLevel.Level3));
        IReadOnlyList<DetailItem> candidates = AppDetailPresenter.Candidates(detail?.ReviewCandidates ?? []);
        if (candidates.Count == 0) technical.Children.Add(PageCopy("No packages were found in your sources."));
        foreach (DetailItem candidate in candidates) technical.Children.Add(DetailItemCard(candidate));
        technical.Children.Add(SectionHeading("Match a package by ID", AutomationHeadingLevel.Level3));
        // Audit §4: the source list shows the names rows show; the chosen index maps back to the same raw source ID as before.
        string[] mappingSources = ["winget", "msstore"];
        ComboBox mappingSource = FilterBox(StewardPresentation.Provider(detail?.SourceId ?? "winget"), mappingSources.Select(StewardPresentation.Provider).ToArray());
        mappingSource.Header = "Source";
        AutomationProperties.SetName(mappingSource, $"Source for matching {row.Name}");
        TextBox mappingPackage = new() { Header = "Exact package ID", PlaceholderText = detail?.PackageId ?? "For example: Contoso.Widget" };
        technical.Children.Add(mappingSource);
        technical.Children.Add(mappingPackage);
        Button map = StewardVisuals.ActionButton("Match package…", $"for {row.Name}");
        map.IsEnabled = viewModel.IsUpdateSourceEnabled && !sourceEvidenceUnavailable && !HasActiveOperation;
        map.Click += async (_, _) =>
        {
            string sourceId = mappingSource.SelectedIndex >= 0 && mappingSource.SelectedIndex < mappingSources.Length ? mappingSources[mappingSource.SelectedIndex] : "winget";
            string packageId = string.IsNullOrWhiteSpace(mappingPackage.Text) ? detail?.PackageId ?? string.Empty : mappingPackage.Text.Trim();
            if (packageId.Length == 0) { await ShowUpdateOutcomeAsync("Package ID needed", "Enter the exact package ID first. Nothing was saved."); return; }
            await PromptAndCreateManualMappingAsync(row, sourceId, packageId);
        };
        technical.Children.Add(map);
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
        try { detailRaw = await DataOperationLease.RunReadAsync(token => workflow.GetRawDiagnosticsAsync(row.ProductId, token), CancellationToken.None); detailTimeline = await DataOperationLease.RunReadAsync(token => workflow.GetTimelineAsync(row.StableProductKey, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        NavigateTo(ShellPage.Detail);
    }
}
