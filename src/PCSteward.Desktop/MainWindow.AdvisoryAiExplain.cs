using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Storage;

namespace PCSteward.Desktop;

/// <summary>
/// Task 67 stage 4c (FR-AI-02/04/05/06, FR-BUD-02/03; ADR-028 consent items 4–5, ADR-029): "Explain with AI…" on a Diagnostics
/// record with a known time and on a failed update in Activity. The button exists only when the build has a reviewed model
/// (<see cref="advisoryAiExplain"/> is composed only then). Each request is its own owner choice, in four steps that can each be
/// cancelled: the draft (every item can be edited or removed, and edits are redacted again), the exact request (the body and headers
/// that will be sent, where it goes and the most it could cost), sending once inside the data-operation lease, and the answer, which
/// is advice only. Earlier saved answers for the same record can be read and deleted. Nothing here retries, and nothing acts on an
/// answer.
/// </summary>
public sealed partial class MainWindow
{
    private readonly AdvisoryAiExplainService? advisoryAiExplain;
    private bool isExplainingWithAi;

    /// <summary>The row action for one Diagnostics record, or null when this build or this record can't be explained.</summary>
    private Button? ExplainIncidentButton(DiagnosticSnapshot snapshot, DiagnosticsEntry entry)
    {
        if (advisoryAiExplain is not AdvisoryAiExplainService explain || !entry.CanExplain) return null;
        DiagnosticRecordKey key = entry.Key;
        return ExplainButton(entry.Heading, AdvisoryAiSourceKind.DiagnosticTimeline, AdvisoryAiDiagnosticDraftBuilder.OriginId(snapshot, key),
            token => explain.DraftForIncidentAsync(DiagnosticTimelineBuilder.Build(snapshot), key, token));
    }

    /// <summary>The row action for one failed update in Activity, or null when this build or this outcome can't be explained.</summary>
    private Button? ExplainUpdateButton(ActivityEntry entry)
    {
        if (advisoryAiExplain is not AdvisoryAiExplainService explain || entry.Job is not UpdateJobRecord job || !AdvisoryAiFailedUpdateEvidence.IsExplainable(job.Final.Status)) return null;
        AdvisoryAiFailedUpdateFacts facts = AdvisoryAiFailedUpdateEvidence.Facts(job, updateHistory, entry.ProductName);
        return ExplainButton(entry.Title, AdvisoryAiSourceKind.FailedUpdate, AdvisoryAiFailedUpdateDraftBuilder.OriginId(job.JobId),
            token => explain.DraftForFailedUpdateAsync(job.JobId, facts, token));
    }

    private Button ExplainButton(string subject, AdvisoryAiSourceKind source, string originId, Func<CancellationToken, Task<AdvisoryAiDraftResult>> draft)
    {
        Button button = StewardVisuals.ActionButton(AdvisoryAiExplainText.ActionLabel, "(shows exactly what would be sent; nothing is sent until you approve it)");
        AutomationProperties.SetName(button, $"{AdvisoryAiExplainText.ActionLabel} {subject}");
        button.IsEnabled = !isExplainingWithAi;
        button.Click += async (_, _) => await ExplainWithAiAsync(subject, source, originId, draft);
        return button;
    }

    private async Task ExplainWithAiAsync(string subject, AdvisoryAiSourceKind source, string originId, Func<CancellationToken, Task<AdvisoryAiDraftResult>> makeDraft)
    {
        if (advisoryAiExplain is not AdvisoryAiExplainService explain || isExplainingWithAi) return;
        if (HasActiveOperation) { await ShowDataOperationUnavailableAsync(); return; }
        isExplainingWithAi = true;
        try
        {
            AdvisoryAiDraftResult result;
            try { result = await DataOperationLease.RunReadAsync(makeDraft, CancellationToken.None); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { result = new(null, AdvisoryAiExplainBlock.SettingsUnavailable, AdvisoryAiDraftProblem.None); }
            if (windowClosed) return;
            if (result.Draft is not AdvisoryAiEvidenceDraft draft)
            {
                // ADR-028: disconnecting or lowering the limits keeps saved answers, and reading or deleting one needs no key and no
                // draft, so they stay reachable when no new request can be made.
                IReadOnlyList<AdvisoryAiSavedExplanation> kept = await ReadSavedExplanationsAsync(explain, source, originId);
                if (windowClosed) return;
                if (await ShowExplainNoDraftAsync(subject, AdvisoryAiExplainText.Blocked(result.Block, result.DraftProblem), kept.Count))
                    await ShowSavedExplanationsAsync(explain, subject, kept);
                return;
            }

            List<string?> texts = [.. draft.Items.Select(item => (string?)item.Text)];
            while (!windowClosed)
            {
                IReadOnlyList<AdvisoryAiSavedExplanation> saved = await ReadSavedExplanationsAsync(explain, draft.Source, draft.OriginId);
                (DraftChoice choice, AdvisoryAiEvidenceDraft? edited) = await ShowExplainDraftAsync(explain, subject, draft, texts, saved.Count);
                if (choice == DraftChoice.Cancel || windowClosed) return;
                if (choice == DraftChoice.Saved) { await ShowSavedExplanationsAsync(explain, subject, saved); continue; }

                AdvisoryAiReview review;
                try { review = await DataOperationLease.RunReadAsync(token => explain.ReviewAsync(edited!, token), CancellationToken.None); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { review = new(null, null, null, default, AdvisoryAiExplainBlock.SettingsUnavailable, AdvisoryAiPreviewProblem.None); }
                if (windowClosed) return;
                if (!review.CanApprove)
                {
                    if (await ShowExplainBlockedAsync(subject, AdvisoryAiExplainText.Blocked(review.Block, previewProblem: review.PreviewProblem))) continue;
                    return;
                }
                ReviewChoice approval = await ShowExplainReviewAsync(subject, review);
                if (approval == ReviewChoice.Back) continue;
                if (approval != ReviewChoice.Approve || windowClosed) return;

                AdvisoryAiSendOutcome? outcome = await SendExplanationAsync(explain, review);
                if (outcome is null || windowClosed) return;
                AnnounceCompletion(OperationAnnouncements.Completed(outcome.Dispatched ? "AI explanation received" : "AI request not sent"), "advisory-ai-explained");
                await ShowExplainOutcomeAsync(subject, outcome);
                if (advisoryAiSetup is not null) await LoadAdvisoryAiAsync();
                return;
            }
        }
        finally
        {
            isExplainingWithAi = false;
            if (!windowClosed) Render();
        }
    }

    private enum DraftChoice { Cancel, Review, Saved }

    private enum ReviewChoice { Cancel, Back, Approve }

    /// <summary>
    /// ADR-028 consent item 4: the whole draft, one box per item. "Leave out" removes an item; an edit is redacted again. The owner's
    /// texts are kept in <paramref name="texts"/> so going back from the review keeps them.
    /// </summary>
    private async Task<(DraftChoice Choice, AdvisoryAiEvidenceDraft? Edited)> ShowExplainDraftAsync(AdvisoryAiExplainService explain, string subject,
        AdvisoryAiEvidenceDraft draft, List<string?> texts, int savedCount)
    {
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(Wrapped(AdvisoryAiExplainText.DraftIntro));
        body.Children.Add(StewardVisuals.InformationBanner("Check for secrets", AdvisoryAiExplainText.ResidualSecrets, "attention"));
        List<(TextBox Box, CheckBox LeaveOut)> fields = [];
        for (int index = 0; index < draft.Items.Count; index++)
        {
            AdvisoryAiEvidenceItem item = draft.Items[index];
            string label = AdvisoryAiExplainText.RoleLabel(item.Role) + (item.TextTruncated ? " (shortened)" : string.Empty);
            TextBox box = new()
            {
                Header = label,
                Text = texts[index] ?? item.Text,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                IsSpellCheckEnabled = false
            };
            AutomationProperties.SetName(box, label);
            CheckBox leaveOut = new() { Content = "Leave this out", IsChecked = texts[index] is null };
            AutomationProperties.SetHelpText(leaveOut, label);
            box.IsEnabled = texts[index] is not null;
            leaveOut.Checked += (_, _) => box.IsEnabled = false;
            leaveOut.Unchecked += (_, _) => box.IsEnabled = true;
            fields.Add((box, leaveOut));
            StackPanel field = new() { Spacing = StewardSpacing.XSmall };
            field.Children.Add(box);
            field.Children.Add(leaveOut);
            body.Children.Add(field);
        }
        if (AdvisoryAiExplainText.Omitted(draft) is string omitted) body.Children.Add(StewardVisuals.FootNote("", omitted));
        TextBlock problem = ProblemText();
        body.Children.Add(problem);

        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = $"{AdvisoryAiExplainText.ActionLabel.TrimEnd('…')}: {subject}",
            Content = new ScrollViewer { Content = body },
            PrimaryButtonText = "Review the request…",
            SecondaryButtonText = savedCount == 0 ? string.Empty : $"Earlier answers ({savedCount})",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        AdvisoryAiEvidenceDraft? edited = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            for (int index = 0; index < fields.Count; index++) texts[index] = fields[index].LeaveOut.IsChecked == true ? null : fields[index].Box.Text;
            AdvisoryAiDraftProblem result = explain.ApplyEdits(draft, texts, out AdvisoryAiEvidenceDraft changed);
            if (result == AdvisoryAiDraftProblem.None) { edited = changed; return; }
            problem.Text = ExplainEditProblem(result);
            problem.Visibility = Visibility.Visible;
            args.Cancel = true;
        };
        ContentDialogResult shown = await dialog.ShowAsync();
        // The owner's texts are kept even on Earlier answers, so coming back shows them as they were.
        for (int index = 0; index < fields.Count; index++) texts[index] = fields[index].LeaveOut.IsChecked == true ? null : fields[index].Box.Text;
        return shown switch
        {
            ContentDialogResult.Primary when edited is not null => (DraftChoice.Review, edited),
            ContentDialogResult.Secondary => (DraftChoice.Saved, null),
            _ => (DraftChoice.Cancel, null)
        };
    }

    private static string ExplainEditProblem(AdvisoryAiDraftProblem problem) => problem switch
    {
        AdvisoryAiDraftProblem.NoEvidenceLeft => "Keep at least one item, or choose Cancel to send nothing.",
        AdvisoryAiDraftProblem.EditIsEmpty => "An item can't be empty once your names are removed. Write something, or leave it out.",
        _ => "PC Steward couldn't apply that change. Nothing has been sent."
    };

    /// <summary>
    /// ADR-028 consent item 5: where it goes, the most it could cost and the exact body and headers. The key itself is never shown;
    /// its header shows only the fixed placeholder. Approve and send is never the default.
    /// </summary>
    private async Task<ReviewChoice> ShowExplainReviewAsync(string subject, AdvisoryAiReview review)
    {
        AdvisoryAiRequestPreview preview = review.Preview!;
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(Wrapped(AdvisoryAiExplainText.Route(preview)));
        body.Children.Add(Wrapped(AdvisoryAiExplainText.Cost(review, preview.Provider)));
        body.Children.Add(StewardVisuals.InformationBanner("Advice only", AdvisoryAiExplainText.AdviceOnly, "neutral"));
        body.Children.Add(StewardVisuals.InformationBanner("Check for secrets", AdvisoryAiExplainText.ResidualSecrets, "attention"));
        body.Children.Add(SectionHeading("Headers", AutomationHeadingLevel.Level3));
        foreach (KeyValuePair<string, string> header in preview.Headers) body.Children.Add(StewardVisuals.EvidenceRow(header.Key, header.Value));
        body.Children.Add(SectionHeading("Exactly what will be sent", AutomationHeadingLevel.Level3));
        TextBlock exact = new() { Text = preview.BodyText, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontFamily = new FontFamily("Consolas") };
        AutomationProperties.SetName(exact, "The request body");
        body.Children.Add(exact);
        ContentDialog dialog = new()
        {
            XamlRoot = Content.XamlRoot,
            Title = $"Check the request: {subject}",
            Content = new ScrollViewer { Content = body },
            PrimaryButtonText = AdvisoryAiExplainText.ApproveLabel,
            SecondaryButtonText = "Back to the draft",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => ReviewChoice.Approve,
            ContentDialogResult.Secondary => ReviewChoice.Back,
            _ => ReviewChoice.Cancel
        };
    }

    /// <summary>
    /// Sends the approved request once, inside the exclusive data-operation lease, while a dialog shows it is on its way. Stop cancels
    /// the wait; once the request has left the PC the workflow still records what it cost.
    /// </summary>
    private async Task<AdvisoryAiSendOutcome?> SendExplanationAsync(AdvisoryAiExplainService explain, AdvisoryAiReview review)
    {
        if (HasActiveOperation) { await ShowDataOperationUnavailableAsync(); return null; }
        using CancellationTokenSource cancellation = new();
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(new ProgressBar { IsIndeterminate = true });
        TextBlock text = Wrapped($"Sending once to {review.Preview!.Provider.Name}. This can take up to {(int)AdvisoryAiResourcePolicy.OverallDeadline.TotalSeconds} seconds.");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        body.Children.Add(text);
        ContentDialog progress = new() { XamlRoot = Content.XamlRoot, Title = "Asking for an explanation", Content = body, CloseButtonText = "Stop waiting" };
        Task<AdvisoryAiSendOutcome> send = DataOperationLease.RunAsync(token => explain.SendAsync(review, token), cancellation.Token);
        const string stoppedText = "You stopped before the request was sent. Nothing was sent and nothing was charged.";
        bool stopped = false;
        if (!send.IsCompleted)
        {
            Task<ContentDialogResult> shown = progress.ShowAsync().AsTask();
            await Task.WhenAny(send, shown);
            // Stop waiting closes the dialog and cancels; otherwise the request finished and the dialog is closed here. Either way
            // the dialog is fully closed before the next one opens.
            if (send.IsCompleted) progress.Hide();
            else { stopped = true; cancellation.Cancel(); }
            await shown;
        }
        AdvisoryAiSendOutcome outcome;
        try { outcome = await send; }
        catch (InvalidOperationException) { await ShowDataOperationUnavailableAsync(); return null; }
        catch (OperationCanceledException) { await ShowExplainMessageAsync("Stopped", stoppedText); return null; }
        catch (Exception exception) when (exception is not OutOfMemoryException) { await ShowExplainMessageAsync("Not finished", AdvisoryAiSetupText.ActionUnfinished); return null; }
        // A stop before the request left the PC is not a missing key or any other fault: say only that nothing was sent.
        if (stopped && !outcome.Dispatched) { await ShowExplainMessageAsync("Stopped", stoppedText); return null; }
        return outcome;
    }

    /// <summary>FR-AI-05: what happened and what it cost; then the answer, already redacted and marked as advice only.</summary>
    private async Task ShowExplainOutcomeAsync(string subject, AdvisoryAiSendOutcome outcome)
    {
        StackPanel body = new() { Spacing = StewardSpacing.Small };
        body.Children.Add(Wrapped(AdvisoryAiExplainText.Outcome(outcome)));
        if (AdvisoryAiExplainText.UsageCounts(outcome.UsageReport) is string counts) body.Children.Add(Wrapped(counts));
        if (outcome.AnswerText is string answer)
        {
            body.Children.Add(StewardVisuals.InformationBanner("Advice only", AdvisoryAiExplainText.AdviceOnly, "neutral"));
            body.Children.Add(new TextBlock { Text = answer + (outcome.AnswerTruncated ? " …" : string.Empty), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        }
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = $"AI explanation: {subject}", Content = new ScrollViewer { Content = body }, CloseButtonText = "Close" }.ShowAsync();
    }

    /// <summary>FR-AI-06: earlier answers for this record, newest first, each with Delete. Deleting asks nothing more of the provider.</summary>
    private async Task ShowSavedExplanationsAsync(AdvisoryAiExplainService explain, string subject, IReadOnlyList<AdvisoryAiSavedExplanation> saved)
    {
        StackPanel body = new() { Spacing = StewardSpacing.Medium };
        body.Children.Add(StewardVisuals.InformationBanner("Advice only", AdvisoryAiExplainText.AdviceOnly, "neutral"));
        foreach (AdvisoryAiSavedExplanation answer in saved)
        {
            StackPanel item = new() { Spacing = StewardSpacing.XSmall };
            item.Children.Add(new TextBlock { Text = AdvisoryAiExplainText.SavedHeading(answer), Style = AppStyle("StewardDescriptionStyle"), TextWrapping = TextWrapping.Wrap });
            item.Children.Add(new TextBlock { Text = (answer.Text ?? "No answer text was saved.") + (answer.TextTruncated ? " …" : string.Empty), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            Button delete = StewardVisuals.ActionButton("Delete this answer", "(removes it from this PC)");
            TextBlock deleted = Wrapped(string.Empty);
            AutomationProperties.SetLiveSetting(deleted, AutomationLiveSetting.Polite);
            Guid id = answer.Id;
            delete.Click += async (_, _) =>
            {
                delete.IsEnabled = false;
                bool removed;
                try { removed = await DataOperationLease.RunAsync(token => explain.DeleteSavedAsync(id, token), CancellationToken.None); }
                catch (Exception exception) when (exception is not OutOfMemoryException) { removed = false; }
                deleted.Text = removed ? "Deleted." : "PC Steward couldn't delete it just now. Try again later.";
                delete.IsEnabled = !removed;
            };
            item.Children.Add(delete);
            item.Children.Add(deleted);
            body.Children.Add(item);
        }
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = $"Earlier answers: {subject}", Content = new ScrollViewer { Content = body }, CloseButtonText = "Back" }.ShowAsync();
    }

    private static async Task<IReadOnlyList<AdvisoryAiSavedExplanation>> ReadSavedExplanationsAsync(AdvisoryAiExplainService explain, AdvisoryAiSourceKind source, string originId)
    {
        try { return await DataOperationLease.RunReadAsync(token => explain.SavedAsync(source, originId, token), CancellationToken.None); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { return []; }
    }

    /// <summary>No new request is possible: says why, and offers the earlier answers when there are any. Returns true to show them.</summary>
    private async Task<bool> ShowExplainNoDraftAsync(string subject, string message, int savedCount)
    {
        if (savedCount == 0) { await ShowExplainMessageAsync(subject, message); return false; }
        return await new ContentDialog { XamlRoot = Content.XamlRoot, Title = subject, Content = Wrapped(message), SecondaryButtonText = $"Earlier answers ({savedCount})", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Secondary;
    }

    /// <summary>A blocked review: says why. Returns true when the owner goes back to the draft.</summary>
    private async Task<bool> ShowExplainBlockedAsync(string subject, string message) =>
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = subject, Content = Wrapped(message), SecondaryButtonText = "Back to the draft", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close }.ShowAsync() == ContentDialogResult.Secondary;

    private async Task ShowExplainMessageAsync(string title, string message) =>
        await new ContentDialog { XamlRoot = Content.XamlRoot, Title = title, Content = Wrapped(message), CloseButtonText = "Close" }.ShowAsync();
}
