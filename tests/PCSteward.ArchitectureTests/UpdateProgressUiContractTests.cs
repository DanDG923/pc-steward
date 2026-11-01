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
        StringAssert.Contains(review, "CoverageDisclosure");
        StringAssert.Contains(review, "token, preparation.Progress)");
        StringAssert.Contains(review, "CancellationToken.None");
        StringAssert.Contains(review, "finally { StopUpdateProgressPolling();");
        StringAssert.Contains(review, "ShowUpdateOutcomeAsync(GetUpdateOutcomeTitle(result), GetUpdateOutcomeMessage(result))");
        StringAssert.Contains(review, "if (windowClosed) return");
        StringAssert.Contains(source, "IsIndeterminate = true");
        Assert.IsFalse(review.Contains("new UpdateProgressFrame", StringComparison.Ordinal));
        Assert.IsFalse(review.Contains("Cancel update", StringComparison.Ordinal));
    }

    private static string Between(string source, string start, string end) =>
        source[source.IndexOf(start, StringComparison.Ordinal)..source.IndexOf(end, StringComparison.Ordinal)];

    private static string Source()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src", "PCSteward.Desktop", "MainWindow.cs"))) directory = directory.Parent;
        Assert.IsNotNull(directory);
        return File.ReadAllText(Path.Combine(directory.FullName, "src", "PCSteward.Desktop", "MainWindow.cs"));
    }
}
