using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

[TestClass]
public sealed class UpdateProgressUiContractTests
{
    [TestMethod]
    [Description("FR-UPD-14; FR-UI-01–07; NFR-OBS-01–05: one bounded pull timer, epoch guards and teardown without shell rebuild per frame.")]
    public void DesktopPullsProgressAndGuardsDetachedSessions()
    {
        string source = Source();
        Assert.AreEqual(1, source.Split("DispatcherQueue.CreateTimer()", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(source, "Interval = TimeSpan.FromMilliseconds(250)");
        StringAssert.Contains(source, "epoch != updateProgressEpoch");
        StringAssert.Contains(source, "updateProgressTimer.Tick -= updateProgressTick");
        StringAssert.Contains(source, "updateProgressTimer.Stop()");
        StringAssert.Contains(source, "Closed +=");
        StringAssert.Contains(source, "progressLabel.Unloaded +=");
        string tick = Between(source, "private void PollUpdateProgress", "private void StopUpdateProgressPolling");
        StringAssert.Contains(tick, "TryTake(out UpdateProgressFrame?");
        StringAssert.Contains(tick, "HasDegraded");
        StringAssert.Contains(tick, "RefreshOperationVisuals()");
        Assert.IsFalse(tick.Contains("Render()", StringComparison.Ordinal));
        Assert.IsFalse(tick.Contains("Task.Run", StringComparison.Ordinal));
        Assert.IsFalse(tick.Contains("TryEnqueue", StringComparison.Ordinal));
        Assert.IsFalse(tick.Contains("while", StringComparison.Ordinal));
        // Task 73 Phase 1: the operation band (and so the polled label) is created once with the persistent shell.
        string shell = Between(source, "private void EnsureShell()", "private static void AddNavigationItem");
        StringAssert.Contains(shell, "if (navigationView is not null || windowRoot is null) return;");
        StringAssert.Contains(shell, "operationStatusText = new TextBlock");
        Assert.AreEqual(1, source.Split("operationStatusText = new TextBlock", StringSplitOptions.None).Length - 1);
    }

    [TestMethod]
    [Description("FR-UPD-14; FR-UPD-06; FR-UI-01–07: review-bound mailbox, coverage disclosure, indeterminate UI and authoritative final outcome.")]
    public void DesktopPreservesApprovalAndUsesActualBatchAfterTimerStops()
    {
        string source = Source();
        string review = source[source.IndexOf("private async Task ReviewAndExecuteAsync", StringComparison.Ordinal)..];
        // Task 73 Phase 2: the running-app warning and its coverage disclosure reach the review sheet page, which shows them.
        StringAssert.Contains(review, "item.Preparation.RunningApplication,");
        StringAssert.Contains(ApplicationSource("UpdateJobPresentation.cs"), "running.CoverageDisclosure");
        StringAssert.Contains(Between(source, "private void AddReviewStep(", "private void AddUpdateStep("), "sheet.CoverageDisclosure");
        // Task 73 Phase 6: each app's own review-bound mailbox is passed to its confirm-and-run and attached to the UI when its turn starts.
        // Task 52: the acknowledgement is bound to the processes named in the warning the owner confirmed.
        StringAssert.Contains(ApplicationSource("ReviewedUpdateExecutionWorkflow.cs"), "RunningApplicationDetection warned = item.Preparation.RunningApplication;");
        StringAssert.Contains(ApplicationSource("ReviewedUpdateExecutionWorkflow.cs"), "ConfirmAndRunAsync(jobReview, warned.AppearsToBeRunning, cancellationToken, item.Preparation.Progress, review.ReviewedAt,");
        StringAssert.Contains(source, "updateProgressMailbox = item.Progress;");
        StringAssert.Contains(source, "updateProgressPresentation = new(item.Review!.JobId, item.Review.CorrelationId, item.Review.Selection.Scope);");
        StringAssert.Contains(review, "dispatcher.TryEnqueue(() => OnBatchItemNotice(job, preparation, notice))");
        StringAssert.Contains(review, "CancellationToken.None");
        StringAssert.Contains(review, "finally { StopUpdateProgressPolling();");
        // The authoritative recorded batch, not a progress frame, decides the outcome (audit P-H3).
        StringAssert.Contains(review, "UpdateOutcomePresenter.ForBatch(run, NameOf)");
        StringAssert.Contains(review, "if (windowClosed) return");
        StringAssert.Contains(source, "IsIndeterminate = true");
        Assert.IsFalse(review.Contains("new UpdateProgressFrame", StringComparison.Ordinal));
        Assert.IsFalse(review.Contains("Cancel update", StringComparison.Ordinal));
    }

    [TestMethod]
    [Description("FR-UPD-14; SPEC §17.4; ADR-030 decision 3: the Update step is non-modal, indeterminate, follows the polled presentation without a rebuild per frame, and has no cancel.")]
    public void UpdateStepIsNonModalIndeterminateAndHasNoCancel()
    {
        string source = Source();
        string step = Between(source, "private void AddUpdateStep(", "private void AddVerifyStep(");
        StringAssert.Contains(step, "ProgressBar progress = new() { IsIndeterminate = true");
        // Task 73 Phase 6 review L1: the live line follows the polled presentation unless it is the next app waiting for the previous one.
        StringAssert.Contains(step, "UpdateJobProgressText.LiveState(line, updateProgressPresentation)");
        StringAssert.Contains(step, "UpdateJobProgressText.Line(index, job.CurrentIndex, job.FinishedLabels, job.Stop.IsRequested)");
        StringAssert.Contains(step, "UpdateJobProgressText.LeaveNote");
        Assert.IsFalse(step.Contains("Cancel", StringComparison.Ordinal), "no cancel once the update has started");
        Assert.IsFalse(step.Contains("ContentDialog", StringComparison.Ordinal), "progress is a page, not a modal dialog");
        string visuals = Between(source, "private void RefreshOperationVisuals()", "private static Style AppStyle(");
        // Task 73 Phase 5 (AC-UX-09): the same polled text, now set through the live-region helper (raised only when it changes).
        StringAssert.Contains(visuals, "StewardVisuals.SetLiveText(jobProgressStateText, UpdateJobProgressText.LiveState(jobProgressLine ?? new(true, null), updateProgressPresentation), announce: true);");
        StringAssert.Contains(visuals, "operationViewJob.Visibility =");
        Assert.IsFalse(ApplicationSource("UpdateJobPresentation.cs").Contains("Cancel update", StringComparison.Ordinal));
    }

    private static string ApplicationSource(string file)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "PCSteward.Application", file))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "PCSteward.Application", file));
    }

    private static string Between(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private static string Source()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "PCSteward.Desktop", "MainWindow.cs"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        return DesktopWindowSource.Read(directory.FullName);
    }
}
