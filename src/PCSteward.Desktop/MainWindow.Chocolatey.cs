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
    /// Admission obligation 5: the runtime-limit Chocolatey review. The split-token state is probed afresh for this review. No
    /// production composition prepares a reviewed operation for this route (it is admitted in code since the Task 50
    /// activation), so the dialog shows the no-review disclosure and Confirm stays disabled (<c>IsConfirmEnabled</c>); any
    /// confirmation goes only through the view model's <c>ReviewedUpdateReview.Confirm</c> recheck and nothing is dispatched.
    /// </summary>
    private async Task ShowChocolateyReviewedRouteAsync()
    {
        if (windowClosed) return;
        ChocolateyReviewedRouteReviewViewModel review = ChocolateyReviewedRouteReviewViewModel.Open(null, null, probeReviewedRouteSplitToken);
        ContentDialog dialog = ReviewedRouteSurfaces.ChocolateyReviewDialog(Content.XamlRoot, review);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || windowClosed) return;
        // No dispatch path is composed for this route, so nothing can start either way.
        _ = review.TryConfirm();
        await ShowUpdateOutcomeAsync("Update not started", ChocolateyReviewedRouteReviewViewModel.NotStartedText);
    }

    /// <summary>
    /// Chocolatey package Confirm addendum §4: the Chocolatey package update review. Every opening and every "Check again" probes
    /// the split token afresh and reads the trusted-sources setting now. <see cref="ChocolateyPackageUpdateReviewDialog"/> drives the
    /// dialog: it prepares one review session only when owner admission is compiled in, trusted sources are on and the split token
    /// is limited; Confirm is bound to the view model, and the session re-checks every condition and runs the workflow at most once.
    /// The row's package ID only selects which package to check. A confirmed run keeps the dialog and the window open until its
    /// result is shown.
    /// </summary>
    private async Task ShowChocolateyPackageUpdateAsync(string packageId)
    {
        if (windowClosed || activeChocolateyPackageUpdate is not null || activeChocolateyClientUpdate is not null) return;
        ChocolateyPackageUpdateViewModel OpenChecking() => ChocolateyPackageUpdateViewModel.Open(packageId, viewModel.IsWinGetTrustedSourcesEnabled, probeReviewedRouteSplitToken);
        ChocolateyPackageUpdateViewModel model = OpenChecking();
        ContentDialog dialog = ChocolateyPackageUpdateSurfaces.ReviewDialog(Content.XamlRoot, model);
        using ChocolateyPackageUpdateReviewDialog review = new(dialog, packageId, model, OpenChecking, createChocolateyPackageUpdateSession,
            ReadReviewedHistoryAsync, () => windowClosed);
        activeChocolateyPackageUpdate = review;
        try { await review.ShowAsync(); }
        finally { activeChocolateyPackageUpdate = null; }
    }

    /// <summary>
    /// ADR-031 (FR-UPD-15, FR-UI-03/04; design §8.6 gate 3): a display-only Chocolatey client update view from the compiled reviewed
    /// list, a check result and the last scan's reported release (display only). Summaries neither probe the token nor check the
    /// client ("Not checked yet"); the review dialog opens a fresh view, which probes the token, and checks the client.
    /// </summary>
    private ChocolateyClientUpdateViewModel ChocolateyClientUpdateView(Func<ReviewedRouteSplitTokenState>? probe = null,
        ChocolateyInstalledClientVerification? verification = null) =>
        ChocolateyClientUpdateViewModel.Open(chocolateyClientReleases, verification ?? ChocolateyInstalledClientVerification.NotChecked,
            ChocolateyClientUpdateViewModel.ReportedInstalledVersion(viewModel.AllRows), probe ?? (() => ReviewedRouteSplitTokenState.Unknown));

    private Button ChocolateyClientUpdateEntry(ChocolateyClientUpdateViewModel summary)
    {
        Button entry = ChocolateyClientUpdateSurfaces.EntryButton(summary);
        entry.Click += async (_, _) => await ShowChocolateyClientUpdateAsync();
        return entry;
    }

    /// <summary>
    /// ADR-031 (FR-UPD-15, FR-UI-03/04; design §8.6 gate 3) and Chocolatey Confirm design §3–§5: the Chocolatey client update review.
    /// The split-token state is probed afresh for this review and the installed client is checked now, on demand, off the UI thread
    /// within its bound; the offer comes only from that result. <see cref="ChocolateyClientUpdateReviewDialog"/> drives the dialog: it
    /// prepares one review session only when owner admission is compiled in and every other condition holds, Confirm is bound to
    /// the view model, and the session re-checks every condition and runs the workflow at most once. Closing the dialog cancels the
    /// check and any preparation; a confirmed run keeps the dialog and the window open until its result is shown.
    /// </summary>
    private async Task ShowChocolateyClientUpdateAsync()
    {
        if (windowClosed || activeChocolateyClientUpdate is not null || activeChocolateyPackageUpdate is not null) return;
        // Every opening and every "Check again" probes the split token afresh (C4).
        ChocolateyClientUpdateViewModel OpenChecking() => ChocolateyClientUpdateView(probeReviewedRouteSplitToken, ChocolateyInstalledClientVerification.Checking);
        ChocolateyClientUpdateViewModel model = OpenChecking();
        ContentDialog dialog = ChocolateyClientUpdateSurfaces.ReviewDialog(Content.XamlRoot, model);
        using ChocolateyClientUpdateReviewDialog review = new(dialog, model, OpenChecking, checkInstalledChocolateyClient,
            createChocolateyClientUpdateSession, ReadReviewedHistoryAsync, () => windowClosed);
        activeChocolateyClientUpdate = review;
        try { await review.ShowAsync(); }
        finally { activeChocolateyClientUpdate = null; }
    }
}
