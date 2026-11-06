using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using PCSteward.Application;

namespace PCSteward.Desktop;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, FR-UI-03/04, FR-HIS-01, TM-03/04/07/13; Chocolatey package Confirm addendum §4: drives one open
/// review dialog of a Chocolatey Community package update.
/// <list type="number">
/// <item>The dialog opens as "Checking" (split token probed afresh). Only when owner admission is compiled in, trusted sources are on
/// and the split token is limited (C1, C9, C4) is one review session created and prepared: it checks the package now and freezes the
/// review from that check.</item>
/// <item>Confirm is pressed only through the primary button, bound to the view model
/// (<see cref="ChocolateyPackageUpdateViewModel.IsPrimaryActionEnabled"/>); the session re-checks every condition and runs the
/// workflow at most once. Close is the default button, so Enter and Esc never confirm.</item>
/// <item>While the run is in flight the dialog cannot be closed, and the main window's close is refused (<see cref="IsRunning"/>).</item>
/// <item>The result shows the recorded history text; "Check again" starts a new session. A used session is never reused.</item>
/// </list>
/// Closing the dialog cancels the check and any preparation. Nothing runs in the background after it closes.
/// </summary>
internal sealed class ChocolateyPackageUpdateReviewDialog : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(15);
    private readonly ContentDialog dialog;
    private readonly string packageId;
    private readonly Func<ChocolateyPackageUpdateViewModel> openChecking;
    private readonly Func<ChocolateyPackageUpdateReviewSession> createSession;
    private readonly Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory;
    private readonly Func<bool> windowClosed;
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherQueueTimer refresh;
    private ChocolateyPackageUpdateViewModel current;
    private ChocolateyPackageUpdateReviewSession? session;
    private bool earlierUpdateNeedsAttention;
    private bool checking;
    private bool running;
    private bool showingResult;

    internal ChocolateyPackageUpdateReviewDialog(ContentDialog dialog, string packageId, ChocolateyPackageUpdateViewModel opened,
        Func<ChocolateyPackageUpdateViewModel> openChecking, Func<ChocolateyPackageUpdateReviewSession> createSession,
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> readHistory, Func<bool> windowClosed)
    {
        this.dialog = dialog ?? throw new ArgumentNullException(nameof(dialog));
        this.packageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
        current = opened ?? throw new ArgumentNullException(nameof(opened));
        this.openChecking = openChecking ?? throw new ArgumentNullException(nameof(openChecking));
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

    // Close is refused while a confirmed run is in flight.
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
    /// Only when C1, C9 and C4 hold: one new review session, prepared now (the check), then C6 over recent history. Never throws;
    /// stops when the dialog closes. A previous session is dropped, unconfirmed: a new review always needs a new session.
    /// </summary>
    private async Task CheckAndPrepareAsync(ChocolateyPackageUpdateViewModel opened)
    {
        checking = true;
        showingResult = false;
        refresh.Stop();
        session = null;
        earlierUpdateNeedsAttention = false;
        try
        {
            current = opened;
            ChocolateyPackageUpdateSurfaces.Render(dialog, current);
            if (!current.ShouldPrepare) return;
            CancellationToken token = lifetime.Token;

            ChocolateyPackageUpdateReviewSession prepared = createSession();
            session = prepared;
            await prepared.PrepareAsync(packageId, token);
            if (token.IsCancellationRequested || windowClosed() || !ReferenceEquals(session, prepared)) return;
            if (prepared.Stage == ChocolateyClientUpdateReviewStage.Ready && prepared.CheckStartedAt is { } started)
            {
                IReadOnlyList<ReviewedUpdateHistoryRecord>? history = await readHistory(token);
                if (token.IsCancellationRequested || windowClosed() || !ReferenceEquals(session, prepared)) return;
                earlierUpdateNeedsAttention = ChocolateyPackageUpdateAttention.EarlierUpdateNeedsAttention(history, started);
            }
            current = current.WithReview(State(prepared));
            ChocolateyPackageUpdateSurfaces.Render(dialog, current);
            if (current.CanConfirm) refresh.Start();
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

    // The expiry line and the 10-minute check age are re-evaluated while the review is open.
    private void RefreshReview()
    {
        if (running || showingResult || session is not { } open || windowClosed()) return;
        current = current.WithReview(State(open));
        ChocolateyPackageUpdateSurfaces.Render(dialog, current);
        if (!current.CanConfirm) refresh.Stop();
    }

    /// <summary>One accepted press: the session re-checks every condition and runs the workflow at most once.</summary>
    private async Task RunAsync(ChocolateyPackageUpdateReviewSession confirmed)
    {
        running = true;
        refresh.Stop();
        string title = current.Title;
        string change = confirmed.Operation is { } frozen ? ChocolateyPackageUpdateViewModel.ChangeText(frozen) : current.Title;
        ChocolateyClientUpdateRunView view;
        try
        {
            ChocolateyPackageUpdateSurfaces.RenderProgress(dialog, title, change, ChocolateyPackageUpdateRunPresentation.CheckingAgainText);
            // Created on the UI thread, so each phase is reported back on it.
            Progress<ChocolateyClientUpdatePhase> progress = new(phase =>
            {
                if (running) ChocolateyPackageUpdateSurfaces.RenderProgress(dialog, title, change, ChocolateyPackageUpdateRunPresentation.PhaseText(phase));
            });
            ChocolateyClientUpdateRunResult run = await confirmed.ConfirmAsync(progress, CancellationToken.None);
            ReviewedUpdateHistoryView? recorded = null;
            if (run.Started && confirmed.Operation is { } operation)
            {
                try { recorded = ChocolateyClientUpdateRunPresentation.RecordedView(await readHistory(CancellationToken.None), operation.JobId); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { recorded = null; }
            }
            view = ChocolateyPackageUpdateRunPresentation.Present(run, recorded);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            view = new(ChocolateyPackageUpdateRunPresentation.UncertainHeading, [ChocolateyPackageUpdateRunPresentation.LostTrackText], false);
        }
        finally
        {
            running = false;
        }
        session = null;
        showingResult = true;
        if (!windowClosed()) ChocolateyPackageUpdateSurfaces.RenderResult(dialog, view);
    }

    private ChocolateyPackageUpdateReviewState State(ChocolateyPackageUpdateReviewSession open) =>
        new(open.Stage, open.IsReviewLive, open.IsCheckFresh, earlierUpdateNeedsAttention, open.Operation, open.Intent, open.ExpiresAt);
}
