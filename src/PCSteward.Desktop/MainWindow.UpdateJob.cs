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
    /// FR-UPD-01/04–09, FR-UI-03/04, FR-DAT-09, UJ-04 (Task 73 Phase 2, ADR-030 decision 3): the update job stepper, used only for
    /// the job — Review → Update → Verify. Review shows the structured sheet for the reviewed immutable selection with Cancel
    /// focused first; Update is non-modal progress with no cancel; Verify shows the honest outcomes, evidence, restart guidance
    /// and Delete downloaded files. The page only presents; ReviewAndExecuteAsync owns the review, the approval and the run.
    /// </summary>
    private StackPanel BuildUpdateJobPage()
    {
        StackPanel panel = CreatePagePanel();
        jobProgressStateText = null;
        if (updateJob is not { } job)
        {
            panel.Children.Add(StewardVisuals.PageTitle("Update"));
            Button open = StewardVisuals.ActionButton("Open Updates");
            open.Click += (_, _) => NavigateTo(ShellPage.Updates);
            panel.Children.Add(StewardVisuals.EmptyState("", "No update is open", "Choose an app that's ready on the Updates page to review its update.", open));
            return panel;
        }
        panel.Children.Add(StewardVisuals.PageHeader(job.Step switch
        {
            UpdateJobStep.Review => job.Sheet.Title,
            UpdateJobStep.Update => $"Updating {job.Sheet.AppName}",
            _ => job.Completion?.Headline ?? "No outcome recorded"
        }, job.Sheet.AppName));
        panel.Children.Add(BuildJobStepIndicator(job.Step));
        switch (job.Step)
        {
            case UpdateJobStep.Review: AddReviewStep(panel, job); break;
            case UpdateJobStep.Update: AddUpdateStep(panel, job); break;
            default: AddVerifyStep(panel, job); break;
        }
        return panel;
    }

    private static Grid BuildJobStepIndicator(UpdateJobStep current)
    {
        Grid steps = new() { ColumnSpacing = StewardSpacing.Medium };
        AutomationProperties.SetName(steps, "Update steps");
        int column = 0;
        foreach (UpdateJobStepPresentation step in UpdateJobSteps.For(current))
        {
            steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            string tone = step.State switch { UpdateJobStepState.Completed => "success", UpdateJobStepState.Current => "information", _ => "neutral" };
            string glyph = step.State == UpdateJobStepState.Completed ? "" : step.State == UpdateJobStepState.Current ? "" : "";
            Border entry = StewardVisuals.Badge($"{step.Number} {step.Label}", glyph, tone, step.AccessibleName);
            entry.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (step.State == UpdateJobStepState.Current) entry.BorderThickness = new Thickness(2);
            Grid.SetColumn(entry, column++);
            steps.Children.Add(entry);
        }
        return steps;
    }

    private void AddReviewStep(StackPanel panel, UpdateJobSession job)
    {
        UpdateReviewSheet sheet = job.Sheet;
        bool refused = job.Completion is { ReturnsToReview: true };
        if (refused)
        {
            // A refused UAC prompt returns quietly here. The approved job was used up by that attempt, so trying again starts a
            // new review of fresh evidence; nothing was changed and it is not a failure.
            panel.Children.Add(StewardVisuals.InformationBanner("Nothing was changed", job.Completion!.Items[0].Outcome.Summary + " " + job.Completion.Items[0].Outcome.NextStep, "neutral"));
        }
        else panel.Children.Add(PageCopy(UpdateReviewSheet.Intro));

        // SPEC §9.13 G4: selected apps that couldn't be reviewed are explained and are not part of this review.
        if (sheet.Excluded.Count > 0)
            panel.Children.Add(StewardVisuals.InformationBanner($"Not included ({sheet.Excluded.Count})",
                string.Join(" ", sheet.Excluded.Select(excluded => $"{excluded.AppName}: {excluded.Reason}")), "attention"));

        // Every app in the frozen set keeps its own §17.2 facts and confidence in words.
        foreach (UpdateReviewSheetItem item in sheet.Items)
        {
            StackPanel facts = new() { Spacing = 0 };
            TextBlock factsHeading = SectionHeading(sheet.ItemCount == 1 ? "What will be updated" : item.AppName);
            factsHeading.Margin = new Thickness(0, 0, 0, StewardSpacing.Small);
            facts.Children.Add(factsHeading);
            foreach (ReviewFact fact in item.Facts) facts.Children.Add(StewardVisuals.EvidenceRow(fact.Label, fact.Value));
            if (sheet.ItemCount > 1 && item.AppearsToBeRunning)
                facts.Children.Add(StewardVisuals.EvidenceRow("Running", $"Appears to be running ({string.Join(", ", item.RunningProcessNames)})"));
            panel.Children.Add(Card(facts));

            StackPanel confidence = new() { Spacing = StewardSpacing.Small };
            confidence.Children.Add(SectionHeading(sheet.ItemCount == 1 ? "How sure PC Steward is" : $"How sure PC Steward is about {item.AppName}",
                sheet.ItemCount == 1 ? AutomationHeadingLevel.Level2 : AutomationHeadingLevel.Level3));
            confidence.Children.Add(new TextBlock { Text = item.ConfidenceSummary, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            StackPanel evidence = new() { Spacing = StewardSpacing.XSmall };
            foreach (string line in item.ConfidenceEvidence) evidence.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            confidence.Children.Add(new Expander { Header = "Evidence", Content = evidence, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            panel.Children.Add(Card(confidence));
        }

        foreach (ReviewRiskCallout risk in sheet.Risks)
            panel.Children.Add(StewardVisuals.InformationBanner(risk.Title, risk.Message, StewardStatus.ToneName(risk.Tone)));
        // The one-shot helper takes one approved job per prompt, so a batch elevates each machine-wide app on its own.
        if (sheet.ElevationNote is { } elevationNote)
            panel.Children.Add(StewardVisuals.InformationBanner("Windows may ask more than once", elevationNote, "information"));
        // Review L2: the 30-minute dispatch ceiling is shown while the review is still open.
        if (!refused && reviewDecision is not null && sheet.ExpiryNote is { } expiryNote)
            panel.Children.Add(StewardVisuals.InformationBanner("Start within 30 minutes", expiryNote, "neutral"));

        StackPanel fineprint = new() { Spacing = StewardSpacing.Small };
        fineprint.Children.Add(new TextBlock { Text = sheet.Attestation, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        fineprint.Children.Add(new TextBlock { Text = sheet.CoverageDisclosure, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        // Independent review: the attestation and coverage disclosure are what the user confirms, so they start expanded.
        panel.Children.Add(new Expander { Header = "What you're confirming", Content = fineprint, IsExpanded = true, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });

        StewardWrapPanel actions = StewardVisuals.ActionRow();
        if (refused || reviewDecision is not { } decision)
        {
            Button again = StewardVisuals.PrimaryButton(preparingReview ? PreparingReviewLabel : "Start a new review", $"of the update for {sheet.AppName}");
            again.IsEnabled = !HasActiveOperation;
            again.Click += async (_, _) => await ReviewAndExecuteAsync(job.Rows);
            Button leave = StewardVisuals.ActionButton("Back to Updates");
            leave.Click += (_, _) => { updateJob = null; jobResultPending = false; NavigateTo(ShellPage.Updates); };
            actions.Children.Add(leave);
            actions.Children.Add(again);
            panel.Children.Add(actions);
            return;
        }
        // Cancel is the default: it comes first and takes focus, and the update button has no Enter accelerator.
        Button cancel = StewardVisuals.ActionButton(UpdateReviewSheet.CancelButtonLabel, $"the review of {sheet.AppName}. Nothing is changed.");
        StackPanel primaryContent = new() { Orientation = Orientation.Horizontal, Spacing = StewardSpacing.Small };
        if (sheet.ShowsAdministratorShield) primaryContent.Children.Add(new FontIcon { Glyph = "\uEA18", FontSize = 16 });
        primaryContent.Children.Add(new TextBlock { Text = sheet.PrimaryButtonLabel });
        Button confirm = new() { Content = primaryContent, Style = AppStyle("StewardPrimaryButtonStyle") };
        AutomationProperties.SetName(confirm, sheet.PrimaryAutomationName);
        AutomationProperties.SetHelpText(confirm, sheet.AdministratorRequirement.Explanation);
        // Independent review: once the decision is made (for example the update was confirmed and is about to start), a rebuilt
        // sheet shows both buttons disabled, and Cancel can no longer navigate away. Confirm also waits for any active operation.
        bool decided = decision.Task.IsCompleted;
        cancel.IsEnabled = !decided;
        confirm.IsEnabled = !HasActiveOperation && !decided;
        cancel.Click += (_, _) =>
        {
            cancel.IsEnabled = false; confirm.IsEnabled = false;
            if (!decision.TrySetResult(false)) return;
            // Clear the review before navigating so the page shown next has its Review button enabled again.
            if (ReferenceEquals(reviewDecision, decision)) reviewDecision = null;
            if (navigation.CanGoBack) GoBack(); else NavigateTo(ShellPage.Updates);
        };
        confirm.Click += (_, _) =>
        {
            cancel.IsEnabled = false; confirm.IsEnabled = false;
            if (decision.Task.IsCompleted) return;
            // Mark execution pending before completing the decision, so no other operation can start in between.
            executionPending = true;
            bool accepted = decision.TrySetResult(true);
            if (!accepted) executionPending = false;
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        pageInitialFocus = cancel;
    }

    private void AddUpdateStep(StackPanel panel, UpdateJobSession job)
    {
        panel.Children.Add(PageCopy(UpdateJobProgressText.LeaveNote));
        StackPanel item = new() { Spacing = StewardSpacing.Small };
        item.Children.Add(SectionHeading(UpdateJobProgressText.Heading(job.Sheet.ItemCount)));
        // Task 73 Phase 6 (AC-UPD-09): one line per app, in run order. Only the app updating now follows the polled progress.
        // Review L1: a finished app shows its outcome (never a progress bar or "Waiting to start"); while the batch waits for its owned
        // work to settle, the next app carries the live "Waiting for the previous app to finish…".
        jobProgressStateText = null;
        jobProgressLine = null;
        for (int index = 0; index < job.Sheet.ItemCount; index++)
        {
            UpdateReviewSheetItem entry = job.Sheet.Items[index];
            item.Children.Add(StewardVisuals.EvidenceRow(entry.AppName, entry.Facts.Single(fact => fact.Label == "Version").Value));
            UpdateJobItemLine line = UpdateJobProgressText.Line(index, job.CurrentIndex, job.FinishedLabels, job.Stop.IsRequested);
            if (line.IsLive && jobProgressStateText is null)
            {
                jobProgressLine = line;
                jobProgressStateText = new TextBlock { Text = UpdateJobProgressText.LiveState(line, updateProgressPresentation), TextWrapping = TextWrapping.Wrap };
                // A polite live region: its text (not a fixed name) is what a screen reader hears when the state changes.
                AutomationProperties.SetLiveSetting(jobProgressStateText, AutomationLiveSetting.Polite);
                item.Children.Add(jobProgressStateText);
                ProgressBar progress = new() { IsIndeterminate = true, Height = 4 };
                AutomationProperties.SetName(progress, $"{entry.AppName} update progress");
                item.Children.Add(progress);
            }
            else item.Children.Add(PageCopy(line.Text ?? UpdateJobProgressText.QueuedItemState(index, job.CurrentIndex, job.FinishedLabels[index])));
        }
        panel.Children.Add(Card(item));
        // FR-UPD-11, AC-UPD-10: stop before the next app starts. It never interrupts the app that is updating now.
        if (job.Sheet.ItemCount > 1 && job.CurrentIndex < job.Sheet.ItemCount - 1)
        {
            if (job.Stop.IsRequested) panel.Children.Add(StewardVisuals.InformationBanner("Stopping after this app", UpdateJobProgressText.StopRequestedText, "neutral"));
            else
            {
                Button stop = StewardVisuals.ActionButton(UpdateJobProgressText.StopAfterCurrentLabel);
                AutomationProperties.SetHelpText(stop, UpdateJobProgressText.StopAfterCurrentHelp);
                stop.IsEnabled = isExecuting;
                stop.Click += (_, _) => { job.Stop.Request(); Render(); };
                panel.Children.Add(stop);
            }
        }
        if (job.Sheet.ShowsAdministratorShield)
            panel.Children.Add(StewardVisuals.InformationBanner(job.Sheet.AdministratorRequirement.Label, job.Sheet.ElevationNote ?? job.Sheet.AdministratorRequirement.Explanation, "information"));
    }

    private void AddVerifyStep(StackPanel panel, UpdateJobSession job)
    {
        UpdateJobCompletion completion = job.Completion ?? new UpdateJobCompletion([]);
        // Task 73 Phase 6 (AC-UPD-09): a batch is grouped by outcome; every app keeps its own recorded outcome and evidence.
        IReadOnlyList<UpdateOutcomeGroup> groups = completion.Items.Count > 1 ? completion.Groups : [];
        if (groups.Count > 0) panel.Children.Add(PageCopy(completion.Headline));
        foreach (UpdateJobItemOutcome item in completion.DisplayItems)
        {
            if (groups.Count > 0 && groups.FirstOrDefault(group => ReferenceEquals(group.Items[0], item)) is { } group)
                panel.Children.Add(SectionHeading(group.Heading));
            UpdateOutcomePresentation outcome = item.Outcome;
            StackPanel card = new() { Spacing = StewardSpacing.Small };
            Grid heading = new() { ColumnSpacing = StewardSpacing.Medium };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(SectionHeading(item.AppName, groups.Count > 0 ? AutomationHeadingLevel.Level3 : AutomationHeadingLevel.Level2));
            Border badge = StewardVisuals.Badge(outcome.Label, outcome.Glyph, StewardStatus.ToneName(outcome.Tone), outcome.AccessibleName(item.AppName));
            badge.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(badge, 1); heading.Children.Add(badge);
            card.Children.Add(heading);
            card.Children.Add(StewardVisuals.EvidenceRow("Version", $"{item.InstalledVersion} → {item.TargetVersion}"));
            card.Children.Add(StewardVisuals.EvidenceRow("Package ID", item.PackageId));
            card.Children.Add(new TextBlock { Text = outcome.Summary, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            card.Children.Add(PageCopy(outcome.NextStep));
            card.Children.Add(new Expander { Header = "Technical details", Content = new TextBlock { Text = outcome.TechnicalDetail, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
            if (outcome.Kind is UpdateOutcomeKind.Failed or UpdateOutcomeKind.NotStarted)
            {
                // A fresh review of this app only; never a replay of the earlier approval.
                IReadOnlyList<InventoryRow> again = item.StableProductKey is { } key ? RowsFor([key]) : job.Rows;
                Button reviewAgain = StewardVisuals.ActionButton("Review again", $"the update for {item.AppName}");
                reviewAgain.IsEnabled = !HasActiveOperation && again.Count > 0;
                reviewAgain.Click += async (_, _) => await ReviewAndExecuteAsync(again);
                card.Children.Add(reviewAgain);
            }
            panel.Children.Add(Card(card));
        }
        panel.Children.Add(StewardVisuals.InformationBanner(completion.RestartRequired ? "Restart to finish" : "Restart", completion.RestartGuidance, completion.RestartRequired ? "attention" : "neutral"));

        // FR-DAT-09 / UJ-04 step 9: Delete downloaded files on the completion view, through the existing confirmation and cleanup.
        StackPanel cleanup = new() { Spacing = StewardSpacing.Small };
        cleanup.Children.Add(SectionHeading(UpdateJobCleanupText.Heading));
        cleanup.Children.Add(PageCopy(UpdateJobCleanupText.Recoverable(downloadCache.RecoverableBytes)));
        Button delete = StewardVisuals.ActionButton("Delete downloaded files", "after update completion");
        delete.IsEnabled = downloadCache.RecoverableBytes > 0 && !HasActiveOperation;
        delete.Click += async (_, _) => await ConfirmAndClearDownloadedFilesAsync("Delete downloaded files after update completion?");
        cleanup.Children.Add(delete);
        panel.Children.Add(Card(cleanup));

        Button done = StewardVisuals.PrimaryButton("Done", "(close the update result and return to Updates)");
        done.Click += (_, _) => { updateJob = null; jobResultPending = false; NavigateTo(ShellPage.Updates); };
        panel.Children.Add(done);
    }

    /// <summary>Recent reviewed-update history for the Chocolatey review (C6 and the result text); null when it cannot be read.</summary>
    private async Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?> ReadReviewedHistoryAsync(CancellationToken cancellationToken)
    {
        try { return await DataOperationLease.RunReadAsync(token => reviewedHistory.GetReviewedUpdateHistoryAsync(ChocolateyClientUpdateAttention.HistoryWindow, token), cancellationToken); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return null; }
    }

    private void StartUpdateProgressPolling()
    {
        StopUpdateProgressPolling();
        if (windowClosed || !isExecuting || updateProgressMailbox is null || updateProgressPresentation is null || updateProgressPresentation.IsFinal || updateProgressPresentation.IsUnavailable) return;
        updateProgressTarget = operationStatusText;
        long epoch = updateProgressEpoch;
        UpdateProgressMailbox mailbox = updateProgressMailbox;
        UpdateProgressPresentation presentation = updateProgressPresentation;
        updateProgressTick = (_, _) => PollUpdateProgress(epoch, mailbox, presentation);
        updateProgressTimer.Tick += updateProgressTick;
        updateProgressTimer.Start();
    }

    private void PollUpdateProgress(long epoch, UpdateProgressMailbox mailbox, UpdateProgressPresentation presentation)
    {
        if (windowClosed || !isExecuting || epoch != updateProgressEpoch || !ReferenceEquals(mailbox, updateProgressMailbox) || !ReferenceEquals(updateProgressTarget, operationStatusText)) return;
        if (mailbox.HasDegraded) presentation.MarkUnavailable();
        if (mailbox.TryTake(out UpdateProgressFrame? frame) && frame is not null) presentation.Consume(frame);
        updateProgressLabel = presentation.Label;
        RefreshOperationVisuals();
        if (presentation.IsFinal || presentation.IsUnavailable) StopUpdateProgressPolling();
    }

    private void StopUpdateProgressPolling()
    {
        ++updateProgressEpoch;
        updateProgressTimer.Stop();
        if (updateProgressTick is not null) updateProgressTimer.Tick -= updateProgressTick;
        updateProgressTick = null;
        updateProgressTarget = null;
    }

    /// <summary>The current inventory rows for <paramref name="keys"/>, in order; keys without a row are left out.</summary>
    private IReadOnlyList<InventoryRow> RowsFor(IEnumerable<string> keys) =>
        [.. keys.Select(key => viewModel.AllRows.FirstOrDefault(row => row.StableProductKey == key)).OfType<InventoryRow>()];

    /// <summary>
    /// FR-UPD-01/04–11, AC-UPD-01/09/10, SPEC §9.13 G4 (Task 73 Phases 2 and 6): the one review and confirmation site, for one app or a
    /// frozen set. It prepares a fresh review of every app, shows one review sheet, waits for the single explicit confirmation, then
    /// runs the apps one at a time through the existing per-app revalidation, approval and (per machine-wide app) elevation.
    /// </summary>
    private async Task ReviewAndExecuteAsync(IReadOnlyList<InventoryRow> rows)
    {
        if (HasActiveOperation || windowClosed || reviewDecision is not null || rows.Count == 0) return;
        // Independent review: while the review is prepared every review entry point is disabled (HasActiveOperation) and the band
        // says "Preparing review…", so a second review cannot start. The flag covers preparation only, never the sheet.
        preparingReview = true;
        jobResultPending = false;
        // Review M2: preparation reports "n of N" and can be cancelled from the band; nothing is approved or started while preparing.
        CancellationTokenSource preparationCancellation = new();
        reviewPreparationCancellation = preparationCancellation;
        reviewPreparationProgress = null;
        Progress<UpdateBatchReviewProgress> preparationProgress = new(progress =>
        {
            if (windowClosed || !preparingReview || !ReferenceEquals(reviewPreparationCancellation, preparationCancellation)) return;
            reviewPreparationProgress = progress;
            RefreshOperationVisuals();
        });
        Render();
        string[] keys = [.. rows.Select(row => row.StableProductKey).Distinct(StringComparer.Ordinal)];
        string NameOf(string key) => rows.FirstOrDefault(row => row.StableProductKey == key)?.Name ?? key;
        UpdateBatchReviewPreparation? prepared = null;
        bool leaseUnavailable = false;
        bool preparationCancelled = false;
        try { prepared = await DataOperationLease.RunAsync(token => updates.BeginBatchReviewAsync(keys, preparationProgress, token), preparationCancellation.Token); }
        catch (OperationCanceledException) when (preparationCancellation.IsCancellationRequested) { preparationCancelled = true; }
        catch (InvalidOperationException) { leaseUnavailable = true; }
        catch (Exception) { prepared = null; }
        finally { preparingReview = false; }
        reviewPreparationCancellation = null;
        reviewPreparationProgress = null;
        preparationCancellation.Dispose();
        if (windowClosed) return;
        if (preparationCancelled)
        {
            Render();
            AnnounceCompletion("Review preparation cancelled. Nothing was changed.", "review-preparation-cancelled");
            return;
        }
        if (prepared is null)
        {
            Render();
            if (leaseUnavailable) await ShowDataOperationUnavailableAsync();
            else await ShowUpdateOutcomeAsync("Update not available", "PC Steward could not prepare this update. No update was started.");
            return;
        }
        UpdateBatchReviewPreparation preparation = prepared;
        if (preparation.Items.Count == 0)
        {
            Render();
            await ShowUpdateOutcomeAsync(keys.Length == 1 ? "Update not available" : "Updates not available",
                keys.Length == 1 ? preparation.Excluded[0].Message : string.Join(Environment.NewLine, preparation.Excluded.Select(excluded => $"{NameOf(excluded.StableProductKey)}: {excluded.Message}")));
            return;
        }
        // FR-UI-03/04, SPEC §17.2 (audit P-H6): the structured review sheet replaces the wall-of-text confirmation. It shows only each
        // reviewed immutable selection, the computed confidence, the running-app warning with its CoverageDisclosure, and the
        // administrator requirement (AdministratorRequirementPresenter.ForSelection per app). Cancel is the default, and leaving the
        // page cancels the review.
        UpdateReviewSheetInput[] inputs = [.. preparation.Items.Select(item => new UpdateReviewSheetInput(item.Preparation.Review!.Selection,
            NameOf(item.StableProductKey), rows.FirstOrDefault(row => row.StableProductKey == item.StableProductKey)?.Publisher ?? string.Empty,
            viewModel.UpdateDetails.GetValueOrDefault(item.StableProductKey), item.Preparation.RunningApplication))];
        UpdateReviewSheet sheet = UpdateReviewSheet.CreateBatch(inputs,
            [.. preparation.Excluded.Select(excluded => new UpdateReviewExclusion(NameOf(excluded.StableProductKey), excluded.Message))],
            preparation.ExpiresAt.ToLocalTime());
        UpdateJobSession job = new([.. preparation.Items.Select(item => rows.First(row => row.StableProductKey == item.StableProductKey))], sheet);
        updateJob = job;
        TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
        reviewDecision = decision;
        // A new review (for example "Start a new review" from the job page) always gets a fresh page: focus on Cancel, scroll at top.
        if (navigation.Current == ShellPage.UpdateJob) ShowCurrentPage(preserveViewState: false); else NavigateTo(ShellPage.UpdateJob);
        AnnounceCompletion(OperationAnnouncements.ReviewReady(sheet.AppName), "review-ready");
        bool confirmed = await decision.Task;
        if (ReferenceEquals(reviewDecision, decision)) reviewDecision = null;
        if (!confirmed || windowClosed)
        {
            executionPending = false;
            if (ReferenceEquals(updateJob, job)) updateJob = null;
            // Independent review: always refresh whichever page is shown, so its Review button is enabled again after Cancel.
            if (!windowClosed) Render();
            return;
        }

        isExecuting = true;
        executionPending = false;
        job.Step = UpdateJobStep.Update;
        // Progress follows the app that is updating now: each app's own review-bound mailbox is attached when its turn starts.
        updateProgressMailbox = null;
        updateProgressPresentation = null;
        updateProgressLabel = "Revalidating reviewed update";
        updateProgressSubject = null;
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher = DispatcherQueue;
        UpdateBatchRunResult? run = null;
        ReviewedUpdateExecutionResult? result = null;
        // Set only once the data lease has run the operation: a lease refusal (busy, reset pending or an unresolved earlier
        // update) happens before the workflow is entered, so nothing was started and nothing was changed.
        bool operationEntered = false;
        try
        {
            Render();
            run = await DataOperationLease.RunAsync(token =>
            {
                Volatile.Write(ref operationEntered, true);
                return updates.ConfirmAndRunBatchAsync(preparation, job.Stop, notice => dispatcher.TryEnqueue(() => OnBatchItemNotice(job, preparation, notice)), token);
            }, CancellationToken.None);
        }
        catch (InvalidOperationException) when (!Volatile.Read(ref operationEntered)) { result = new(false, UpdateOutcomePresenter.DataUnavailableNotStartedMessage, null); }
        catch (Exception) { result = new(true, "The reviewed update did not produce a recorded outcome. Execution or verification may have been interrupted. No verified success was recorded; refresh inventory before deciding what to do next.", null); }
        finally { StopUpdateProgressPolling(); isExecuting = false; updateProgressMailbox = null; updateProgressPresentation = null; updateProgressSubject = null; if (!windowClosed) LoadSavedInventory(); }
        if (windowClosed) return;
        // FR-UPD-08/09, AC-UPD-09/10, UJ-04 step 8 (audit P-H3): each app's recorded result is authoritative and maps to Verified,
        // Restart required, Installed, not verified, Failed or Not started — never a flattened "Update failed". Earlier results are
        // kept as recorded. A refused UAC prompt for every app returns quietly to Review.
        job.Completion = run is not null
            ? UpdateOutcomePresenter.ForBatch(run, NameOf)
            : new([.. preparation.Items.SelectMany(item => UpdateOutcomePresenter.ForResult(result!, item.Preparation.Review!.Selection, NameOf(item.StableProductKey)).Items)]);
        job.Step = job.Completion.ReturnsToReview ? UpdateJobStep.Review : UpdateJobStep.Verify;
        // Independent review: never move the user. Elsewhere, the band shows the result with "View result".
        jobResultPending = navigation.Current != ShellPage.UpdateJob;
        Render();
        AnnounceCompletion(OperationAnnouncements.JobFinished(sheet.AppName, job.Completion.Headline), "update-job-finished");
    }

    /// <summary>
    /// Task 73 Phase 6: a presentation notice from the batch, on the UI thread. A starting app attaches its own review-bound progress
    /// mailbox; a finished app shows its recorded outcome label. It never changes a result and is ignored once the job is gone.
    /// </summary>
    private void OnBatchItemNotice(UpdateJobSession job, UpdateBatchReviewPreparation preparation, UpdateBatchItemNotice notice)
    {
        if (windowClosed || !isExecuting || !ReferenceEquals(updateJob, job) || notice.Index < 0 || notice.Index >= preparation.Items.Count) return;
        if (notice.Finished)
        {
            if (notice.Result is { } finished)
                job.FinishedLabels[notice.Index] = UpdateOutcomePresenter.ForBatch(new([finished], false), _ => null).Items.Single().Outcome.Label;
            if (notice.Index == job.CurrentIndex)
            {
                StopUpdateProgressPolling(); updateProgressMailbox = null; updateProgressPresentation = null;
                // Review L1: while the batch waits for this app's owned work to settle, the band says so instead of naming a finished app.
                if (notice.Index < preparation.Items.Count - 1 && !job.Stop.IsRequested)
                {
                    updateProgressSubject = null;
                    updateProgressLabel = UpdateJobProgressText.WaitingForPreviousApp;
                }
            }
        }
        else
        {
            UpdateReviewPreparation item = preparation.Items[notice.Index].Preparation;
            job.CurrentIndex = notice.Index;
            updateProgressMailbox = item.Progress;
            updateProgressPresentation = new(item.Review!.JobId, item.Review.CorrelationId, item.Review.Selection.Scope);
            updateProgressLabel = item.Progress is null ? "Progress unavailable" : "Revalidating reviewed update";
            // Info (review): name the app in the band ("Updating Firefox (2 of 4)"), so a UAC prompt for it has context.
            updateProgressSubject = preparation.Items.Count == 1 ? null
                : UpdateJobProgressText.BatchSubject(job.Sheet.Items[notice.Index].AppName, notice.Index, preparation.Items.Count);
            StartUpdateProgressPolling();
        }
        Render();
    }

    private Task<ContentDialogResult> ShowUpdateOutcomeAsync(string title, string message) => new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = message, CloseButtonText = "Close" }.ShowAsync().AsTask();
}
