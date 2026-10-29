using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
[TestCategory("FR-UPD-14")]
[TestCategory("FR-UPD-01")]
[TestCategory("FR-UPD-11")]
public sealed class RunningApplicationCoverageTests
{
    private static readonly string[] ExpectedNames = ["Contoso.Widget", "Widget"];
    [TestMethod]
    public async Task NoMatchDisclosesActualInspectedAndInvalidNameCoverage()
    {
        PackageDerivedRunningApplicationDetector detector = new(() => ["unrelated", new string('x', 65)]);
        RunningApplicationDetection result = await detector.DetectAsync(Selection(), CancellationToken.None);
        Assert.IsFalse(result.AppearsToBeRunning);
        StringAssert.Contains(result.CoverageDisclosure, "Inspected 2 process entries");
        StringAssert.Contains(result.CoverageDisclosure, "invalid names: 1");
        AssertUniversalLimits(result);
    }

    [TestMethod]
    public async Task AccessDeniedExitedAndUnavailableEntriesRemainVisibleWithNoMatch()
    {
        PackageDerivedRunningApplicationDetector detector = new(_ => [
            new(RunningProcessObservationKind.Inaccessible), new(RunningProcessObservationKind.Exited),
            new(RunningProcessObservationKind.Unavailable)]);
        RunningApplicationDetection result = await detector.DetectAsync(Selection(), CancellationToken.None);
        Assert.IsFalse(result.AppearsToBeRunning);
        Assert.AreEqual(new RunningApplicationCoverage(3, 0, 1, 1, 1, 0, false), result.Coverage);
        StringAssert.Contains(result.CoverageDisclosure, "inaccessible: 1; exited: 1; unavailable: 1");
        AssertUniversalLimits(result);
    }

    [TestMethod]
    [DataRow(4096)]
    [DataRow(4097)]
    public async Task SaturationDisclosesLimitWithoutReadingBeyond4096(int count)
    {
        int reads = 0;
        bool disposed = false;
        IEnumerable<RunningProcessObservation> Source()
        {
            try
            {
                for (int index = 0; index < count; index++)
                {
                    reads++;
                    yield return new(RunningProcessObservationKind.Name, index == 4096 ? "Widget" : "unrelated");
                }
            }
            finally { disposed = true; }
        }
        PackageDerivedRunningApplicationDetector detector = new(_ => Source());
        RunningApplicationDetection result = await detector.DetectAsync(Selection(), CancellationToken.None);
        Assert.IsFalse(result.AppearsToBeRunning);
        Assert.AreEqual(4096, reads);
        Assert.IsTrue(disposed);
        Assert.IsNotNull(result.Coverage);
        Assert.AreEqual(4096, result.Coverage.InspectedProcessCount);
        Assert.IsTrue(result.Coverage.InspectionLimitReached);
        StringAssert.Contains(result.CoverageDisclosure, "additional entries may be omitted");
    }

    [TestMethod]
    public async Task LateStartIsObservedByANewSnapshotAndBothResultsDiscloseRaceGap()
    {
        int snapshots = 0;
        PackageDerivedRunningApplicationDetector detector = new(_ => ++snapshots == 1
            ? [] : [new(RunningProcessObservationKind.Name, "Widget")]);
        RunningApplicationDetection review = await detector.DetectAsync(Selection(), CancellationToken.None);
        RunningApplicationDetection invocation = await detector.DetectAsync(Selection(), CancellationToken.None);
        Assert.IsFalse(review.AppearsToBeRunning);
        Assert.IsTrue(invocation.AppearsToBeRunning);
        Assert.AreEqual(2, snapshots);
        AssertUniversalLimits(review);
        AssertUniversalLimits(invocation);
    }

    [TestMethod]
    [DataRow("app")]
    [DataRow("application")]
    [DataRow("client")]
    [DataRow("desktop")]
    [DataRow("installer")]
    [DataRow("launcher")]
    [DataRow("setup")]
    [DataRow("update")]
    [DataRow("updater")]
    public async Task AmbiguousSuffixDoesNotMatchButFullPackageNameDoes(string suffix)
    {
        PackageDerivedRunningApplicationDetector suffixOnly = new(() => [suffix]);
        Assert.IsFalse((await suffixOnly.DetectAsync(Selection("Contoso." + suffix), CancellationToken.None)).AppearsToBeRunning);
        PackageDerivedRunningApplicationDetector full = new(() => ["Contoso." + suffix]);
        Assert.IsTrue((await full.DetectAsync(Selection("Contoso." + suffix), CancellationToken.None)).AppearsToBeRunning);
    }

    [TestMethod]
    public async Task MatchingIsExactCaseInsensitiveDistinctAndDeterministicallyOrdered()
    {
        PackageDerivedRunningApplicationDetector detector = new(() => ["WidgetHelper", "Widget", "widget", "Contoso.Widget", "Widget.exe"]);
        RunningApplicationDetection result = await detector.DetectAsync(Selection(), CancellationToken.None);
        CollectionAssert.AreEqual(ExpectedNames, result.ProcessNames.ToArray());
    }

    [TestMethod]
    public async Task NameBoundsRejectOverlengthAndInvalidNamesWithoutTruncation()
    {
        string allowed = new('x', 64);
        PackageDerivedRunningApplicationDetector detector = new(() => [new string('x', 65), "Widget\n", "", allowed]);
        RunningApplicationDetection result = await detector.DetectAsync(Selection("Contoso." + allowed), CancellationToken.None);
        Assert.IsTrue(result.AppearsToBeRunning);
        CollectionAssert.AreEqual(new[] { allowed }, result.ProcessNames.ToArray());
        Assert.IsNotNull(result.Coverage);
        Assert.AreEqual(3, result.Coverage.InvalidNameCount);
        Assert.AreEqual(1, result.Coverage.ReadableNameCount);
    }

    [TestMethod]
    public async Task PreCancellationDoesNotRequestSnapshot()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        PackageDerivedRunningApplicationDetector detector = new(_ => throw new InvalidOperationException("must not snapshot"));
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => detector.DetectAsync(Selection(), cancellation.Token));
    }

    [TestMethod]
    public async Task MidSnapshotCancellationStopsReadingAndDisposesEnumerator()
    {
        using CancellationTokenSource cancellation = new();
        bool disposed = false;
        int reads = 0;
        IEnumerable<RunningProcessObservation> Source()
        {
            try
            {
                reads++;
                yield return new(RunningProcessObservationKind.Name, "Widget");
                cancellation.Cancel();
                reads++;
                yield return new(RunningProcessObservationKind.Name, "unrelated");
                Assert.Fail("Cancellation must stop enumeration.");
            }
            finally { disposed = true; }
        }
        PackageDerivedRunningApplicationDetector detector = new(_ => Source());
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => detector.DetectAsync(Selection(), cancellation.Token));
        Assert.AreEqual(2, reads);
        Assert.IsTrue(disposed);
    }

    [TestMethod]
    public async Task EnumerationFailureReportsUnknownCoverageWithoutInventingInspectedEntries()
    {
        PackageDerivedRunningApplicationDetector detector = new(_ => [new(RunningProcessObservationKind.EnumerationUnavailable)]);
        RunningApplicationDetection result = await detector.DetectAsync(Selection(), CancellationToken.None);
        Assert.IsNotNull(result.Coverage);
        Assert.AreEqual(0, result.Coverage.InspectedProcessCount);
        Assert.IsTrue(result.Coverage.ProcessEnumerationUnavailable);
        Assert.IsFalse(result.AppearsToBeRunning);
        StringAssert.Contains(result.CoverageDisclosure, "Process enumeration was unavailable");
        AssertUniversalLimits(result);
    }

    [TestMethod]
    public void LegacyRecordConstructionDisclosesUnknownCountsAndUniversalLimits()
    {
        AssertUniversalLimits(RunningApplicationDetection.NotDetected);
        StringAssert.Contains(new RunningApplicationDetection(true, ["Widget"]).CoverageDisclosure, "counts are unavailable");
    }

    private static void AssertUniversalLimits(RunningApplicationDetection result)
    {
        StringAssert.Contains(result.CoverageDisclosure, "4,096");
        StringAssert.Contains(result.CoverageDisclosure, "64 characters");
        StringAssert.Contains(result.CoverageDisclosure, "no match does not prove it is closed");
        StringAssert.Contains(result.CoverageDisclosure, "between this check and invocation");
    }

    private static UpdateJobSelection Selection(string package = "Contoso.Widget") => new(
        "fixture:widget", UpdateProvider.WinGet, UpdateSource.WinGet, package, "1.0", "2.0",
        UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture-v1");
}
