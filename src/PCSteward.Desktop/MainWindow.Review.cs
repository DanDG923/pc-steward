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
    private Button CreateSourceEvidenceRefreshButton(InventoryRow row, string label = "Check again")
    {
        Button refresh = StewardVisuals.PrimaryButton(
            viewModel.IsCheckingUpdates ? "Checking…" : label,
            $"for {row.Name}");
        refresh.IsEnabled = viewModel.IsUpdateSourceEnabled && !viewModel.IsCheckingUpdates && !viewModel.IsScanning && !isExecuting;
        AutomationProperties.SetHelpText(refresh, "Looks for updates again. It doesn't download or install anything.");
        refresh.Click += async (_, _) => await CheckUpdateSourcesAsync();
        return refresh;
    }

    private async Task PromptAndConfirmPackageIdentityAsync(InventoryRow row, UpdateDetail detail) =>
        await PromptAndCreateManualMappingAsync(row, detail.SourceId!, detail.PackageId!);

    /// <summary>
    /// FR-POL-03/05 (Task 73 Phase 3; audit P-M1): the one confirmation for recording a package identity, used by the lists, the
    /// review page and every mapping action on app details. It records a reversible mapping after validation; it never approves or
    /// starts an update. The reason is read on the UI thread before the data-operation lease starts. UI review 2026-11-13 (item 12):
    /// when it is saved the page says so in one line with Undo, instead of a second dialog.
    /// </summary>
    private async Task PromptAndCreateManualMappingAsync(InventoryRow row, string sourceId, string packageId)
    {
        TextBox reason = DecisionReasonBox("Optional reason for confirming this match");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Confirm only if you recognise {packageId} from {StewardPresentation.Provider(sourceId)} as {row.Name}. You can undo this later. It does not approve or start an update."));
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Is this the same app?",
            Content = content,
            PrimaryButtonText = AppDetailSummaryPresenter.SameAppLabel,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        string decisionReason = reason.Text;
        Guid[] savedBefore = [.. manualMappings.Select(mapping => mapping.Id)];
        PCSteward.Domain.ManualMappingValidation validation;
        try { validation = await DataOperationLease.RunAsync(token => workflow.CreateManualMappingAsync(row.StableProductKey, sourceId, packageId, viewModel.IsUpdateSourceEnabled, decisionReason, token), CancellationToken.None); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        if (!validation.IsValid) { await ShowUpdateOutcomeAsync("Match not saved", validation.Explanation); return; }
        LoadSavedInventory();
        ShowDecisionNotice(DecisionNoticePresenter.MatchConfirmed(row.Name, row.StableProductKey, sourceId, packageId, manualMappings, savedBefore));
    }

    /// <summary>
    /// UI review 2026-11-13 (item 12; FR-POL-03/05): after a pin, ignore, skip or confirmed match is saved, the page it was made on
    /// says so in one line with Undo, instead of a second dialog that waits for Close. The saved data is reloaded before this is
    /// called, so the notice names exactly the records Undo reverses.
    /// </summary>
    private void ShowDecisionNotice(DecisionNotice notice)
    {
        decisionNotice = notice;
        decisionNoticePage = navigation.Current;
        Render();
        AnnounceCompletion(notice.Message, "decision-saved");
    }

    /// <summary>The line for <paramref name="notice"/>: what was saved, an Undo button when its records are known, and a close button.</summary>
    private InfoBar DecisionNoticeBar(DecisionNotice notice)
    {
        Button? undo = null;
        if (notice.CanUndo)
        {
            undo = new Button { Content = DecisionNotice.UndoLabel, IsEnabled = !HasActiveOperation };
            AutomationProperties.SetName(undo, notice.UndoAccessibleName);
            undo.Click += async (_, _) => await ReverseDecisionsAsync(notice.DecisionIds, notice.MappingIds);
        }
        InfoBar bar = StewardVisuals.InformationBanner("Saved", notice.Message, "success", undo);
        bar.IsClosable = true;
        bar.Closed += (_, _) => decisionNotice = null;
        return bar;
    }

    /// <summary>
    /// Reverses decisions and deletes confirmed matches through the existing reversal operations, in one data operation. Used by
    /// Undo and by app details. It changes saved decisions only; nothing on the PC is changed.
    /// </summary>
    private async Task ReverseDecisionsAsync(IReadOnlyList<Guid> decisionIds, IReadOnlyList<Guid> mappingIds)
    {
        try
        {
            await DataOperationLease.RunAsync(async token =>
            {
                foreach (Guid decision in decisionIds) await workflow.ReverseDecisionAsync(decision, token);
                foreach (Guid mapping in mappingIds) await workflow.DeleteManualMappingAsync(mapping, token);
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return; }
        decisionNotice = null;
        LoadSavedInventory();
        Render();
        AnnounceCompletion(DecisionNoticePresenter.Undone, "decision-undone");
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
        ShowDecisionNotice(DecisionNoticePresenter.Pinned(row.Name, row.Version, [row.StableProductKey, .. (sameApp ?? []).Select(same => same.StableProductKey)], activeDecisions));
    }

    /// <summary>Ignores the app after the confirmation, for <paramref name="sameApp"/> too (other records of the same app, phase 3).</summary>
    private async Task PromptAndIgnoreProductAsync(InventoryRow row, IReadOnlyList<InventoryRow>? sameApp = null)
    {
        CheckBox ignoreUntilEnabled = new() { Content = "Ignore until a selected date" };
        DatePicker ignoreUntil = new() { Header = "Ignore until", IsEnabled = false, Date = DateTimeOffset.UtcNow.AddDays(30) };
        ignoreUntilEnabled.Checked += (_, _) => ignoreUntil.IsEnabled = true;
        ignoreUntilEnabled.Unchecked += (_, _) => ignoreUntil.IsEnabled = false;
        TextBox reason = DecisionReasonBox("Optional reason for ignoring this app");
        StackPanel content = new() { Spacing = StewardSpacing.Medium };
        content.Children.Add(PageCopy($"Ignore {row.Name} indefinitely, or select an end date below. You can reverse this decision on the Decisions page. No application is changed."));
        content.Children.Add(ignoreUntilEnabled);
        content.Children.Add(ignoreUntil);
        content.Children.Add(reason);
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = "Ignore updates for this app?",
            Content = content,
            PrimaryButtonText = "Ignore updates",
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
        ShowDecisionNotice(DecisionNoticePresenter.Ignored(row.Name, ignoredUntil, [row.StableProductKey, .. (sameApp ?? []).Select(same => same.StableProductKey)], activeDecisions));
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
        ShowDecisionNotice(DecisionNoticePresenter.Skipped(row.Name, version, [row.StableProductKey, .. (sameApp ?? []).Select(same => same.StableProductKey)], activeDecisions));
    }

    private static TextBox DecisionReasonBox(string placeholder) => new() { Header = "Optional reason", PlaceholderText = placeholder, MaxLength = 512 };

    /// <summary>
    /// FR-POL-03/05: confirms, in one step, the matches of apps that are already on the version their match lists. UI review
    /// 2026-11-13 (item 14): it is offered on Installed apps under "Check the match", now that the Scan results and Review decisions
    /// pages are gone. Each match is validated again when it is saved and can be undone on the Decisions page; nothing is updated.
    /// </summary>
    private async Task ConfirmCurrentMatchesAsync(IReadOnlyCollection<InventoryRow> candidates)
    {
        ContentDialog confirmation = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = InstalledAppsFilters.ConfirmCurrentMatchesTitle,
            Content = InstalledAppsFilters.ConfirmCurrentMatchesExplanation,
            PrimaryButtonText = InstalledAppsFilters.ConfirmCurrentMatchesLabel(candidates.Count),
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        RepositoryIdentityConfirmation[] confirmations = candidates
            .Select(row => viewModel.UpdateDetails[row.StableProductKey])
            .Select(detail => new RepositoryIdentityConfirmation(detail.StableProductKey, detail.SourceId!, detail.PackageId!))
            .ToArray();
        try { RepositoryIdentityConfirmationResult result = await DataOperationLease.RunAsync(token => workflow.ConfirmCurrentRepositoryIdentitiesAsync(confirmations, viewModel.IsUpdateSourceEnabled, "Bulk confirmation of source-reported current package identity.", token), CancellationToken.None); LoadSavedInventory(); await ShowUpdateOutcomeAsync(result.Confirmed == 0 ? "No matches confirmed" : "Matches confirmed", result.Message); Render(); }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); }
    }
}
