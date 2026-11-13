using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// Owner request 2026-11-13 (FR-UI-01/05): a bar on Home that fills while a scan or an update check runs, with about how long is
/// left. The time is an estimate from the last run and is worded as one; with nothing to compare with, a scan shows its step.
/// </summary>
[TestClass]
public sealed class WorkProgressPresentationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [Description("FR-UI-01/05: with an earlier scan to compare with, the bar shows the share of that time spent and about how long is left; it never reaches 100% on an estimate.")]
    public void AScanIsEstimatedFromTheLastScan()
    {
        TimeSpan last = TimeSpan.FromSeconds(40);
        WorkProgressPresentation start = WorkProgressPresenter.Scan(0, 0, TimeSpan.Zero, last);
        Assert.IsFalse(start.IsIndeterminate, "the estimate does not wait for the first step");
        Assert.AreEqual(0, start.Percent);
        Assert.AreEqual("Scanning this PC · 0% · about 40 seconds left", start.Line);

        WorkProgressPresentation half = WorkProgressPresenter.Scan(2, 5, TimeSpan.FromSeconds(20), last);
        Assert.AreEqual(50, half.Percent);
        Assert.AreEqual("50% · about 20 seconds left", half.Detail);

        WorkProgressPresentation late = WorkProgressPresenter.Scan(5, 5, TimeSpan.FromSeconds(39.5), last);
        Assert.AreEqual(WorkProgressPresenter.MaximumEstimatedPercent, late.Percent, "98% of the time spent still shows at most 95%");
        Assert.AreEqual("95% · a few seconds left", late.Detail);

        WorkProgressPresentation over = WorkProgressPresenter.Scan(5, 5, TimeSpan.FromSeconds(90), last);
        Assert.AreEqual(95, over.Percent);
        Assert.AreEqual("95% · taking a little longer than last time", over.Detail);

        // The bar never goes backwards as time passes.
        int previous = -1;
        for (int second = 0; second <= 120; second++)
        {
            int percent = WorkProgressPresenter.Scan(1, 5, TimeSpan.FromSeconds(second), last).Percent;
            Assert.IsTrue(percent >= previous && percent <= 95, $"{second}s: {percent}%");
            previous = percent;
        }
        Assert.AreEqual(0, WorkProgressPresenter.Scan(1, 5, TimeSpan.FromSeconds(-3), last).Percent, "a clock that stepped back shows nothing spent");
    }

    [TestMethod]
    [Description("FR-UI-01/05: the first scan has nothing to compare with, so it shows the step it is on and guesses no time.")]
    public void TheFirstScanShowsItsStep()
    {
        WorkProgressPresentation waiting = WorkProgressPresenter.Scan(0, 0, TimeSpan.FromSeconds(2), null);
        Assert.IsTrue(waiting.IsIndeterminate);
        Assert.AreEqual("Scanning this PC", waiting.Line);

        WorkProgressPresentation first = WorkProgressPresenter.Scan(1, 5, TimeSpan.FromSeconds(2), null);
        Assert.IsFalse(first.IsIndeterminate);
        Assert.AreEqual(0, first.Percent);
        Assert.AreEqual("Scanning this PC · 0% · step 1 of 5", first.Line);
        Assert.AreEqual("40% · step 3 of 5", WorkProgressPresenter.Scan(3, 5, TimeSpan.FromSeconds(9), null).Detail);
        Assert.AreEqual("80% · step 5 of 5", WorkProgressPresenter.Scan(5, 5, TimeSpan.FromSeconds(9), null).Detail, "the last step is still running");
        Assert.AreEqual("80% · step 5 of 5", WorkProgressPresenter.Scan(7, 5, TimeSpan.FromSeconds(9), null).Detail, "a step past the end reads as the last one");
        Assert.IsFalse(WorkProgressPresenter.Scan(3, 5, TimeSpan.FromSeconds(9), null).Detail.Contains("left", StringComparison.Ordinal), "no time is guessed");
        // An earlier scan too short to estimate from is the same as none.
        Assert.AreEqual("40% · step 3 of 5", WorkProgressPresenter.Scan(3, 5, TimeSpan.FromSeconds(9), TimeSpan.FromMilliseconds(200)).Detail);
    }

    [TestMethod]
    [Description("FR-UI-01/05: an update check is estimated from the last check when there is one; otherwise the bar only shows that it is running.")]
    public void AnUpdateCheckIsEstimatedOnlyWhenThereIsSomethingToCompareWith()
    {
        WorkProgressPresentation unknown = WorkProgressPresenter.UpdateCheck(TimeSpan.FromSeconds(12), null);
        Assert.IsTrue(unknown.IsIndeterminate);
        Assert.AreEqual("Looking for updates", unknown.Line);

        WorkProgressPresentation known = WorkProgressPresenter.UpdateCheck(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2));
        Assert.IsFalse(known.IsIndeterminate);
        Assert.AreEqual(25, known.Percent);
        Assert.AreEqual("Looking for updates · 25% · about 2 minutes left", known.Line);
    }

    [TestMethod]
    [Description("FR-UI-05: the time left is said in plain words and rounded, never to the second.")]
    public void TheTimeLeftIsSaidInPlainWords()
    {
        Assert.AreEqual("a few seconds left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(0.2)));
        Assert.AreEqual("a few seconds left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(9)));
        Assert.AreEqual("about 10 seconds left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(10)));
        Assert.AreEqual("about 25 seconds left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(21)));
        Assert.AreEqual("about 60 seconds left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(58)));
        Assert.AreEqual("about 1 minute left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(70)));
        Assert.AreEqual("about 3 minutes left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(170)));
        Assert.AreEqual("a few seconds left", WorkProgressPresenter.Remaining(TimeSpan.FromSeconds(-5)));
    }

    [TestMethod]
    [Description("FR-UI-01/05: the estimate comes from the most recent scan that completed, never from one that failed or was cancelled.")]
    public void TheLastScanTimeComesFromTheMostRecentCompletedScan()
    {
        ScanRun older = new(Guid.NewGuid(), Start, Start.AddSeconds(50), "Completed", Guid.NewGuid());
        ScanRun newer = new(Guid.NewGuid(), Start.AddHours(1), Start.AddHours(1).AddSeconds(32), "Completed", Guid.NewGuid());
        ScanRun cancelled = new(Guid.NewGuid(), Start.AddHours(2), Start.AddHours(2).AddSeconds(3), "Cancelled", Guid.NewGuid());
        ScanRun failed = new(Guid.NewGuid(), Start.AddHours(3), Start.AddHours(3).AddSeconds(70), "Failed", Guid.NewGuid());
        ScanRun unfinished = new(Guid.NewGuid(), Start.AddHours(4), null, "Completed", Guid.NewGuid());
        Assert.AreEqual(TimeSpan.FromSeconds(32), WorkProgressPresenter.LastScanDuration([older, cancelled, newer, failed, unfinished]));
        Assert.IsNull(WorkProgressPresenter.LastScanDuration([cancelled, failed, unfinished]));
        Assert.IsNull(WorkProgressPresenter.LastScanDuration([]));
    }

    [TestMethod]
    [Description("FR-UI-01/05: the app's state carries the step the running scan is on and clears it when the scan ends.")]
    public void TheRunningScansStepIsKeptWhileItRuns()
    {
        InventoryExperienceViewModel viewModel = new();
        viewModel.ReportProgress(new ScanProgress(Guid.NewGuid(), "Collecting", TimeSpan.FromSeconds(2), "registry", 2, 5));
        Assert.AreEqual((2, 5), (viewModel.ScanStep, viewModel.ScanSteps));
        viewModel.CompleteScan(new ScanResult(Guid.NewGuid(), true, false, new Dictionary<string, TimeSpan>(), []), Start);
        Assert.AreEqual((0, 0), (viewModel.ScanStep, viewModel.ScanSteps));
    }
}
