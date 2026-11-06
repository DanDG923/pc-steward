using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-UI-03/04, FR-HIS-01, TM-03/04/07/13; Chocolatey Confirm design §3–§5: drives one open review
/// dialog of the Chocolatey client update.
/// <list type="number">
/// <item>The dialog opens as "Checking" (split token probed afresh), the read-only, non-elevated check runs off the UI thread within
/// its bound, and its result is shown (design §8.6 gate 3, unchanged).</item>
/// <item>Only when owner admission is compiled in, an offer exists, the split token is limited and no earlier client update needs
/// attention (C1, C3, C4, C6) is one review session created and prepared, automatically. While admission is not compiled in, the
/// dialog is exactly the read-only review it was before.</item>
/// <item>Confirm is pressed only through the primary button, bound to the view model (<see cref="ChocolateyClientUpdateViewModel.IsPrimaryActionEnabled"/>);
/// the session re-checks every condition and runs the workflow at most once. Close is the default button, so Enter and Esc never
/// confirm.</item>
/// <item>While the run is in flight the dialog cannot be closed, and the main window's close is refused (<see cref="IsRunning"/>):
/// closing PC Steward cannot stop an elevated run that has started.</item>
/// <item>The result shows the recorded history text; "Check again" re-runs the check. The session is spent: a new attempt needs a
/// new review.</item>
/// </list>
/// Closing the dialog cancels the check and any preparation. Nothing runs in the background after it closes.
/// </summary>
internal sealed class ChocolateyClientUpdateReviewDialog : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    private readonly ContentDialog dialog;
    private readonly Func<ChocolateyClientUpdateViewModel> openChecking;
    private readonly Func<CancellationToken, Task<ChocolateyInstalledClientVerification>> checkInstalled;
    private readonly Func<ChocolateyClientUpdateReviewSession> createSession;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory;
    private readonly Func<bool> windowClosed;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherQueueTimer refresh;
    private ChocolateyClientUpdateViewModel current;
    private ChocolateyClientUpdateReviewSession? session;
    private DateTimeOffset checkStartedAt;
    private bool earlierUpdateNeedsAttention;
    private bool checking;
    private bool running;
    private bool showingResult;

    internal ChocolateyClientUpdateReviewDialog(ContentDialog dialog, ChocolateyClientUpdateViewModel opened, Func<ChocolateyClientUpdateViewModel> openChecking,
        Func<CancellationToken, Task<ChocolateyInstalledClientVerification>> checkInstalled, Func<ChocolateyClientUpdateReviewSession> createSession,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, Func<bool> windowClosed)
    {
        this.dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        current = opened ?? throw new ArgumentNullException(nameof(opened));
        this.openChecking = openChecking ?? throw new ArgumentNullException(nameof(openChecking));
        this.checkInstalled = checkInstalled ?? throw new ArgumentNullException(nameof(checkInstalled));
        this.createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
        this.readHistory = readHistory ?? throw new ArgumentNullException(nameof(readHistory));
        this.windowClosed = windowClosed ?? throw new ArgumentNullException(nameof(windowClosed));
        refresh = dialog.DispatcherQueue.CreateTimer();
        refresh.Interval = RefreshInterval;
        refresh.Tick += (_, _) => RefreshReview();
    }

    public void Dispose()
    {
        refresh.Stop();
        lifetime.Dispose();
    }

    /// <summary>True from an accepted Confirm until its result is shown: the dialog and the main window refuse to close.</summary>
    internal bool IsRunning => running;

    /// <summary>Shows the dialog until it is closed; the check starts as it opens.</summary>
    internal async Task ShowAsync()
    {
        dialog.PrimaryButtonClick += OnPrimary;
        dialog.Closing += OnClosing;
        Task first = CheckAndPrepareAsync(current);
        try { await dialog.ShowAsync(); }
        finally
        {
            dialog.PrimaryButtonClick -= OnPrimary;
            dialog.Closing -= OnClosing;
            refresh.Stop();
            lifetime.Cancel();
        }
        await first;
    }

    // Design §4.3: Close is refused while a confirmed run is in flight.
    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (running) args.Cancel = true;
    }

    // The primary button never closes the dialog: it confirms (once) or checks again.
    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        args.Cancel = true;
        if (running || checking) return;
        if (showingResult || current.OffersCheckAgain) _ = CheckAndPrepareAsync(openChecking());
        else if (current.CanConfirm && session is { } confirmed) _ = RunAsync(confirmed);
    }

    /// <summary>
    /// The read-only check, then (only when C1, C3, C4 and C6 hold) one new review session. Never throws; stops when the dialog
    /// closes. A previous session is dropped, unconfirmed: a new review always needs a new session.
    /// </summary>
    private async Task CheckAndPrepareAsync(ChocolateyClientUpdateViewModel opened)
    {
        checking = true;
        showingResult = false;
        refresh.Stop();
        session = null;
        earlierUpdateNeedsAttention = false;
        try
        {
            current = opened;
            ChocolateyClientUpdateSurfaces.Render(dialog, current);
            CancellationToken token = lifetime.Token;
            checkStartedAt = DateTimeOffset.UtcNow;
            ChocolateyInstalledClientVerification verification = await ChocolateyInstalledClientVerification.RunAsync(
                cancellation => Task.Run(() => checkInstalled(cancellation), CancellationToken.None),
                ChocolateyInstalledClientVerification.DisplayCheckTimeout, token);
            if (token.IsCancellationRequested || windowClosed()) return;
            current = opened.WithVerification(verification);
            ChocolateyClientUpdateSurfaces.Render(dialog, current);
            if (!current.Admitted || current.OfferVersion is null || current.VerifiedInstalledVersion is not { } verified) return;

            IReadOnlyList<ReviewedUpdateHistoryRecord>? history = await readHistory(token);
            if (token.IsCancellationRequested || windowClosed()) return;
            earlierUpdateNeedsAttention = ChocolateyClientUpdateAttention.EarlierUpdateNeedsAttention(history, checkStartedAt);
            current = current.WithReview(State(null));
            ChocolateyClientUpdateSurfaces.Render(dialog, current);
            // Prepare only when the review itself is the one thing left (C1, C3, C4 and C6 hold).
            if (current.UnavailableReasons is not [ChocolateyClientUpdateUnavailableReason.PreparingReview]) return;

            ChocolateyClientUpdateReviewSession prepared = createSession();
            session = prepared;
            await prepared.PrepareAsync(verified, token);
            if (token.IsCancellationRequested || windowClosed() || !ReferenceEquals(session, prepared)) return;
            current = current.WithReview(State(prepared));
            ChocolateyClientUpdateSurfaces.Render(dialog, current);
            if (prepared.Stage == ChocolateyClientUpdateReviewStage.Ready) refresh.Start();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A fault here never starts anything; the dialog keeps the last state it rendered.
        }
        finally
        {
            checking = false;
        }
    }

    // Design §4.1: the expiry line and the 10-minute check age are re-evaluated while the review is open.
    private void RefreshReview()
    {
        if (running || showingResult || session is not { } open || windowClosed()) return;
        current = current.WithReview(State(open));
        ChocolateyClientUpdateSurfaces.Render(dialog, current);
        if (!current.CanConfirm) refresh.Stop();
    }

    /// <summary>One accepted press: the session re-checks every condition and runs the workflow at most once.</summary>
    private async Task RunAsync(ChocolateyClientUpdateReviewSession confirmed)
    {
        running = true;
        refresh.Stop();
        string title = current.Title;
        string change = confirmed.Operation is { } frozen
            ? $"Chocolatey {frozen.Selection.InstalledVersion} → {frozen.Selection.CandidateVersion}"
            : current.Title;
        ChocolateyClientUpdateRunView view;
        try
        {
            ChocolateyClientUpdateSurfaces.RenderProgress(dialog, title, change, ChocolateyClientUpdateRunPresentation.CheckingAgainText);
            // Created on the UI thread, so each phase is reported back on it.
            Progress<ChocolateyClientUpdatePhase> progress = new(phase =>
            {
                if (running) ChocolateyClientUpdateSurfaces.RenderProgress(dialog, title, change, ChocolateyClientUpdateRunPresentation.PhaseText(phase));
            });
            ChocolateyClientUpdateRunResult run = await confirmed.ConfirmAsync(checkStartedAt, progress, CancellationToken.None);
            ReviewedUpdateHistoryView? recorded = null;
            if (run.Started && confirmed.Operation is { } operation)
            {
                try { recorded = ChocolateyClientUpdateRunPresentation.RecordedView(await readHistory(CancellationToken.None), operation.JobId); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { recorded = null; }
            }
            view = ChocolateyClientUpdateRunPresentation.Present(run, recorded);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            view = new(ChocolateyClientUpdateRunPresentation.UncertainHeading, [ChocolateyClientUpdatePresentation.LostTrackCautionMessage], false);
        }
        finally
        {
            running = false;
        }
        session = null;
        showingResult = true;
        if (!windowClosed()) ChocolateyClientUpdateSurfaces.RenderResult(dialog, view);
    }

    private ChocolateyClientUpdateReviewState State(ChocolateyClientUpdateReviewSession? open) => open is null
        ? ChocolateyClientUpdateReviewState.None with { EarlierUpdateNeedsAttention = earlierUpdateNeedsAttention }
        : new(open.Stage, open.IsReviewLive, open.IsCheckFresh(checkStartedAt), earlierUpdateNeedsAttention, false,
            open.Operation?.Selection.InstalledVersion, open.Operation?.Selection.CandidateVersion, open.ExpiresAt);
}
