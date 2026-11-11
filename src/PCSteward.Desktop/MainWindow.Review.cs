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
    private StackPanel BuildReviewPage()
    {
        StackPanel panel = CreatePagePanel();
        InventoryRow[] ready = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.VerifiedUpdate.ToString()).ToArray();
        InventoryRow[] decisions = viewModel.AllRows.Where(row => row.Status == PCSteward.Domain.UpdateIntelligenceStatus.ReviewRequired.ToString()).ToArray();
        panel.Children.Add(StewardVisuals.PageHeader("Review decisions", "Resolve package matches first. Updates that are ready stay separate, and each still needs your confirmation on its review page."));
        panel.Children.Add(StewardVisuals.InformationBanner("Reviewing doesn't update anything", "Viewing details or confirming a package identity never starts an update."));
        panel.Children.Add(SectionHeading("Needs a decision"));
        panel.Children.Add(PageCopy(decisions.Length == 0 ? "No package matches need a decision." : $"{DisplayFormat.Plural(decisions.Length, "item")} {(decisions.Length == 1 ? "needs" : "need")} a decision. None of them can be updated until the decision is made."));
        foreach (InventoryRow row in decisions)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel decision = new() { Spacing = StewardSpacing.XSmall };
            decision.Children.Add(SectionHeading(row.Name, AutomationHeadingLevel.Level3));
            decision.Children.Add(StewardVisuals.StatusBadge(row.Status));
            decision.Children.Add(StewardVisuals.SelectableText($"{detail?.SourceId ?? StewardPresentation.Provider(row.Provider)} · {detail?.PackageId ?? "package ID unknown"}"));
            decision.Children.Add(PageCopy(detail?.Explanation ?? "The available evidence needs review before any update can be considered."));
            AddReviewDecisionActions(decision, row, detail);
            panel.Children.Add(Card(decision));
        }
        InventoryRow[] currentIdentityCandidates = decisions
            .Where(row => viewModel.UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) && detail.CanConfirmAsCurrent)
            .ToArray();
        if (currentIdentityCandidates.Length > 0)
        {
            panel.Children.Add(PageCopy($"{DisplayFormat.Plural(currentIdentityCandidates.Length, "item")} can be confirmed together, because the package source reports the installed version is the same as the available version."));
            Button confirmAll = StewardVisuals.ActionButton("Confirm all current package identities", "that the source reports as already installed");
            confirmAll.Click += async (_, _) => await ConfirmCurrentPackageIdentitiesAsync(currentIdentityCandidates);
            panel.Children.Add(confirmAll);
        }
        // Audit P-M3: nothing is called "approved" before the user approves it on the review page.
        panel.Children.Add(SectionHeading("Ready to review"));
        panel.Children.Add(PageCopy(ready.Length == 0 ? "No updates are ready to review. Cached, unavailable, low-confidence or conflicting matches can't be updated." : $"{DisplayFormat.Plural(ready.Length, "update")} can be reviewed. Each one still needs your confirmation on its review page."));
        foreach (InventoryRow row in ready)
        {
            UpdateDetail? detail = viewModel.UpdateDetails.GetValueOrDefault(row.StableProductKey);
            StackPanel action = new() { Spacing = StewardSpacing.XSmall };
            action.Children.Add(SectionHeading($"{row.Name} {row.Version} → {detail?.CandidateVersion ?? "new version unknown"}", AutomationHeadingLevel.Level3));
            action.Children.Add(StewardVisuals.StatusBadge(row.Status));
            action.Children.Add(StewardVisuals.SelectableText($"{detail?.SourceId ?? "Unknown source"} · {StewardPresentation.Scope(row.Scope)} · {detail?.Architecture ?? "Unknown architecture"}"));
            Button open = StewardVisuals.ActionButton("View details", $"for {row.Name}"); open.Click += async (_, _) => await ShowDetailsAsync(row); action.Children.Add(open);
            panel.Children.Add(Card(action));
        }
        panel.Children.Add(PageCopy("PC Steward updates one app at a time and checks the installed version before it records an update as successful."));
        return panel;
    }

    private void AddReviewDecisionActions(StackPanel container, InventoryRow row, UpdateDetail? detail)
    {
        container.Children.Add(PageCopy(detail?.ActionState ?? "You can pin the installed version, ignore the app, or view its details."));
        StewardWrapPanel actions = StewardVisuals.ActionRow();
        if (detail is { RequiresFreshSourceEvidence: true } && viewModel.IsUpdateSourceEnabled)
        {
            actions.Children.Add(CreateSourceEvidenceRefreshButton(row));
        }
        if (detail is { SourceId: not null, PackageId: not null, CanConfirmPackageIdentity: true } && viewModel.IsUpdateSourceEnabled)
        {
            Button confirm = StewardVisuals.PrimaryButton("Confirm package identity", $"({detail.PackageId} is {row.Name})");
            confirm.Click += async (_, _) => await PromptAndConfirmPackageIdentityAsync(row, detail);
            actions.Children.Add(confirm);
        }
        else if (detail is { SourceId: null, PackageId: null } && detail.ReviewCandidates.Count > 0 && viewModel.IsUpdateSourceEnabled)
        {
            Button choose = StewardVisuals.PrimaryButton("Choose package identity", $"for {row.Name}");
            choose.Click += async (_, _) => await ShowDetailsAsync(row);
            actions.Children.Add(choose);
        }
        Button pin = StewardVisuals.ActionButton("Pin current version", $"of {row.Name} ({row.Version})");
        pin.Click += async (_, _) => await PromptAndPinCurrentVersionAsync(row);
        actions.Children.Add(pin);
        Button ignore = StewardVisuals.ActionButton("Ignore…", $"{row.Name}: choose how long");
        ignore.Click += async (_, _) => await PromptAndIgnoreProductAsync(row);
        actions.Children.Add(ignore);
        Button open = StewardVisuals.ActionButton("View details", $"for {row.Name}");
        open.Click += async (_, _) => await ShowDetailsAsync(row);
        actions.Children.Add(open);
        container.Children.Add(actions);
    }

    private Button CreateSourceEvidenceRefreshButton(InventoryRow row)
    {
        Button refresh = StewardVisuals.PrimaryButton(
            viewModel.IsCheckingUpdates ? "Checking source evidence…" : "Refresh source evidence",
            $"for {row.Name}");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting;
        AutomationProperties.SetHelpText(refresh, "Checks your package sources only. It doesn't download or install an update.");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        return refresh;
    }

    private async Task PromptAndConfirmPackageIdentityAsync(InventoryRow row, UpdateDetail detail) =>
        await PromptAndCreateManualMappingAsync(row, detail.SourceId!, detail.PackageId!);

    /// <summary>
    /// FR-POL-03/05 (Task 73 Phase 3; audit P-M1): the one confirmation for recording a package identity, used by the lists, the
    /// review page and every mapping action on app details. It records a reversible mapping after validation; it never approves or
    /// starts an update. The reason is read on the UI thread before the data-operation lease starts.
    /// </summary>
    private async Task PromptAndCreateManualMappingAsync(InventoryRow row, string sourceId, string packageId)
    {
        TextBox reason = DecisionReasonBox("Optional reason for confirming this package identity");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Confirm only if you recognise {packageId} from {sourceId} as {row.Name}. This records a reversible mapping. It does not approve or start an update."));
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirm package identity?",
            Content = content,
            PrimaryButtonText = "Confirm identity",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        string decisionReason = reason.Text;
        PCSteward.Domain.ManualMappingValidation validation;
        try { validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, sourceId, packageId, viewModel.IsUpdateSourceEnabled, decisionReason, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        if (!validation.IsValid) { await ShowUpdateOutcomeAsync("Identity not confirmed", validation.Explanation); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Package identity confirmed", validation.Explanation + " No update was started.");
    }

    /// <summary>
    /// Pins the installed version after the confirmation. <paramref name="sameApp"/> (UI polish phase 3) are other records of the same
    /// app shown as one Updates row; the same pin is recorded for each of them in the same data operation.
    /// </summary>
    private async Task PromptAndPinCurrentVersionAsync(InventoryRow row, IReadOnlyList<InventoryRow>? sameApp = null)
    {
        TextBox reason = DecisionReasonBox("Optional reason for pinning this version");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Pin {row.Name} at the installed version {row.Version}? PC Steward will stop offering a different version until you reverse this decision on the Decisions page. No application is changed."));
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Pin current version?",
            Content = content,
            PrimaryButtonText = "Pin version",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        string decisionReason = reason.Text;
        try
        {
            await DataOperationLease.RunAsync(async token =>
            {
                await workflow.PinCurrentVersionAsync(row.StableProductKey, row.Version, decisionReason, token);
                foreach (InventoryRow same in sameApp ?? []) await workflow.PinCurrentVersionAsync(same.StableProductKey, same.Version, decisionReason, token);
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Current version pinned", $"{row.Name} is pinned at {row.Version}. No application was changed.");
    }

    /// <summary>Ignores the app after the confirmation, for <paramref name="sameApp"/> too (other records of the same app, phase 3).</summary>
    private async Task PromptAndIgnoreProductAsync(InventoryRow row, IReadOnlyList<InventoryRow>? sameApp = null)
    {
        CheckBox ignoreUntilEnabled = new() { Content = "Ignore until a selected date" };
        DatePicker ignoreUntil = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) };
        ignoreUntilEnabled.Checked += (_, _) => ignoreUntil.IsEnabled = true;
        ignoreUntilEnabled.Unchecked += (_, _) => ignoreUntil.IsEnabled = false;
        TextBox reason = DecisionReasonBox("Optional reason for ignoring this product");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Ignore {row.Name} indefinitely, or select an end date below. You can reverse this decision on the Decisions page. No application is changed."));
        content.Children.Add(ignoreUntilEnabled);
        content.Children.Add(ignoreUntil);
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Ignore this product?",
            Content = content,
            PrimaryButtonText = "Ignore product",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        DateTimeOffset? ignoredUntil = ignoreUntilEnabled.IsChecked == true ? ignoreUntil.Date : null;
        string decisionReason = reason.Text;
        try
        {
            await DataOperationLease.RunAsync(async token =>
            {
                await workflow.IgnoreAsync(row.StableProductKey, ignoredUntil, decisionReason, token);
                foreach (InventoryRow same in sameApp ?? []) await workflow.IgnoreAsync(same.StableProductKey, ignoredUntil, decisionReason, token);
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Product ignored", ignoredUntil is null ? $"{row.Name} is ignored until you reverse the decision. No application was changed." : $"{row.Name} is ignored until {DisplayFormat.Date(ignoredUntil.Value)}. No application was changed.");
    }

    /// <summary>
    /// Owner decision 2026-11-02 (FR-POL-01/03/05): Skip this version, through the same confirmation pattern as pin and ignore — Cancel
    /// is the default, the reason is optional and bounded. It only hides this exact version; nothing is installed or changed. Other
    /// records of the same app shown as one Updates row (<paramref name="sameApp"/>, UI polish phase 3) skip the same version.
    /// </summary>
    private async Task PromptAndSkipVersionAsync(InventoryRow row, string version, IReadOnlyList<InventoryRow>? sameApp = null)
    {
        TextBox reason = DecisionReasonBox("Optional reason for skipping this version");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Skip version {version} of {row.Name}? PC Steward stops offering this version. When a newer version is available it is offered again. You can stop skipping it on the Decisions page. No application is changed."));
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Skip this version?",
            Content = content,
            PrimaryButtonText = "Skip version",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        string decisionReason = reason.Text;
        try
        {
            await DataOperationLease.RunAsync(async token =>
            {
                await workflow.SkipVersionAsync(row.StableProductKey, version, decisionReason, token);
                foreach (InventoryRow same in sameApp ?? []) await workflow.SkipVersionAsync(same.StableProductKey, version, decisionReason, token);
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        LoadSavedInventory();
        Render();
        await ShowUpdateOutcomeAsync("Version skipped", $"Version {version} of {row.Name} won't be offered. A newer version will be. No application was changed.");
    }

    private static TextBox DecisionReasonBox(string placeholder) => new() { Header = "Optional reason", PlaceholderText = placeholder, MaxLength = 512 };

    private async Task ConfirmCurrentPackageIdentitiesAsync(IReadOnlyCollection<InventoryRow> candidates)
    {
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Confirm current package identities?",
            Content = "This confirms the package identity only for the listed apps whose package source reports the installed version is the same as the available version. Newer, unknown, unavailable or conflicting matches are skipped. You can undo each one on the Decisions page. No update will start.",
            PrimaryButtonText = "Confirm mappings",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        RepositoryIdentityConfirmation[] confirmations = candidates
            .Select(row => viewModel.UpdateDetails[row.StableProductKey])
            .Select(detail => new RepositoryIdentityConfirmation(detail.StableProductKey, detail.SourceId!, detail.PackageId!))
            .ToArray();
        try { RepositoryIdentityConfirmationResult result = await DataOperationLease.RunAsync(token => workflow.ConfirmCurrentRepositoryIdentitiesAsync(confirmations, viewModel.IsUpdateSourceEnabled, "Bulk confirmation of source-reported current package identity.", token), CancellationToken.None); LoadSavedInventory(); await ShowUpdateOutcomeAsync(result.Confirmed == 0 ? "No mappings confirmed" : "Package identities confirmed", result.Message); Render(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
    }
}
