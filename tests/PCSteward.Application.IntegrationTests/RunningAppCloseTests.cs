using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// MSIX design note §3.6 and the owner's decision of 2026-11-13 ("Also offer 'Close it for me'"): an app package can't be updated
/// while it's open, and "Close it for me" only asks apps to close, the same way as closing their window.
/// </summary>
[TestClass]
public sealed class RunningAppCloseTests
{
    private static readonly WinGetUserInstaller Msix = new(WinGetUserInstallerType.Msix, WinGetUserElevation.Prohibited);
    private static readonly WinGetUserInstaller Exe = new(WinGetUserInstallerType.Exe, WinGetUserElevation.Prohibited);
    private static readonly string[] TerminalProcess = ["WindowsTerminal"];
    private static readonly string[] WidgetProcess = ["Widget"];
    private static readonly string[] WaveLinkProcess = ["WaveLink"];
    private static readonly int[] AskedTerminalAndWidget = [10, 30];
    private static readonly int[] AskedTerminal = [10];

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public void OnlyAnMsixInstallerMustBeClosed()
    {
        Assert.IsTrue(RunningApplicationAcknowledgement.MustBeClosed(Msix));
        Assert.IsFalse(RunningApplicationAcknowledgement.MustBeClosed(Exe));
        Assert.IsFalse(RunningApplicationAcknowledgement.MustBeClosed(null));
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public void AnAcknowledgementNeverCoversAnOpenAppPackage()
    {
        RunningApplicationDetection running = new(true, TerminalProcess);
        Assert.IsTrue(RunningApplicationAcknowledgement.Covers(true, TerminalProcess, running), "another installer type: the warning can be acknowledged");
        Assert.IsFalse(RunningApplicationAcknowledgement.Covers(true, TerminalProcess, running, mustBeClosed: true), "an app package: never while open");
        Assert.IsTrue(RunningApplicationAcknowledgement.Covers(false, null, RunningApplicationDetection.NotDetected, mustBeClosed: true), "closed: nothing to cover");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    [TestCategory("FR-UI-03")]
    public void AnOpenAppPackageCantBeConfirmedAndIsAskedToClose()
    {
        UpdateJobSelection selection = Selection("Microsoft.WindowsTerminal");
        UpdateReviewSheet sheet = UpdateReviewSheet.CreateBatch(
            [new UpdateReviewSheetInput(selection, "Windows Terminal", "Microsoft Corporation", null, new RunningApplicationDetection(true, TerminalProcess), Msix)]);

        Assert.IsTrue(sheet.Items.Single().MustCloseFirst);
        Assert.IsFalse(sheet.CanConfirm, "the update button waits until the app is closed");
        Assert.AreEqual("Update 1 app", sheet.PrimaryButtonLabel, "no 'anyway': updating anyway isn't possible");
        Assert.AreEqual("Close Windows Terminal to continue. Windows can't update it while it's open.", sheet.CloseFirstNote);
        ReviewRiskCallout risk = sheet.Risks.Single(callout => callout.Kind == ReviewRiskKind.RunningApplication);
        Assert.AreEqual("Close Windows Terminal to update it", risk.Title);
        StringAssert.Contains(risk.Message, "Windows finishes updating this app only after it's closed");
        StringAssert.Contains(risk.Message, "Close Windows Terminal for me");
        Assert.IsTrue(sheet.OffersCloseForMe);
        CollectionAssert.AreEqual(TerminalProcess, sheet.RunningProcessNames.ToArray());

        UpdateReviewSheet closed = UpdateReviewSheet.CreateBatch(
            [new UpdateReviewSheetInput(selection, "Windows Terminal", "Microsoft Corporation", null, RunningApplicationDetection.NotDetected, Msix)]);
        Assert.IsTrue(closed.CanConfirm, "once closed it can be updated");
        Assert.IsNull(closed.CloseFirstNote);
        Assert.IsFalse(closed.OffersCloseForMe);
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public void ABatchWithAnOpenAppPackageWaitsAndSaysWhy()
    {
        UpdateReviewSheet sheet = UpdateReviewSheet.CreateBatch(
        [
            new UpdateReviewSheetInput(Selection("Microsoft.WindowsTerminal"), "Windows Terminal", "", null, new RunningApplicationDetection(true, TerminalProcess), Msix),
            new UpdateReviewSheetInput(Selection("Contoso.Widget"), "Contoso Widget", "", null, new RunningApplicationDetection(true, ["Widget"]), Exe)
        ]);

        Assert.IsFalse(sheet.CanConfirm);
        Assert.AreEqual("Close these apps for me", sheet.CloseForMeLabel);
        StringAssert.Contains(sheet.Risks.Single(callout => callout.Kind == ReviewRiskKind.RunningApplication).Message, "Windows finishes updating Windows Terminal only after it's closed");
        Assert.AreEqual("Update 2 apps anyway", sheet.PrimaryButtonLabel, "the other running app can still be updated anyway once Terminal is closed");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public async Task CloseForMeAsksEachWindowOnceAndReportsWhatIsStillOpen()
    {
        FakeProcesses processes = new();
        processes.Add("WindowsTerminal", 10, hasWindow: true, exitsWhenAsked: true);
        processes.Add("WaveLink", 20, hasWindow: false, exitsWhenAsked: false);
        processes.Add("Widget", 30, hasWindow: true, exitsWhenAsked: false);
        WindowCloseRunningApplicationCloser closer = new(processes, TimeSpan.FromMilliseconds(50), (_, token) => Task.Delay(5, token));

        RunningApplicationCloseResult result = await closer.RequestCloseAsync(["WindowsTerminal", "WaveLink", "Widget", "bad name!", "WindowsTerminal"], CancellationToken.None);

        CollectionAssert.AreEqual(TerminalProcess, result.Closed.ToArray());
        CollectionAssert.AreEqual(WidgetProcess, result.StillOpen.ToArray(), "asked, but didn't close (it may be asking to save)");
        CollectionAssert.AreEqual(WaveLinkProcess, result.NoWindow.ToArray(), "no window to close (a notification-area app)");
        Assert.IsFalse(result.AllClosed);
        CollectionAssert.AreEquivalent(AskedTerminalAndWidget, processes.Asked.ToArray(), "each process with a window is asked once; nothing else is touched");
        Assert.AreEqual(0, processes.Terminated, "never a force-quit");

        string line = UpdateReviewSheet.CloseResultLine(result);
        StringAssert.Contains(line, "Closed: WindowsTerminal.");
        StringAssert.Contains(line, "Still open: Widget.");
        StringAssert.Contains(line, "No window to close: WaveLink.");
        StringAssert.Contains(line, "notification area");
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public async Task CloseForMeTouchesOnlyTheNamedProcesses()
    {
        FakeProcesses processes = new();
        processes.Add("WindowsTerminal", 10, hasWindow: true, exitsWhenAsked: true);
        processes.Add("WaveLink", 20, hasWindow: true, exitsWhenAsked: true);
        WindowCloseRunningApplicationCloser closer = new(processes, TimeSpan.FromMilliseconds(50), (_, token) => Task.Delay(5, token));

        RunningApplicationCloseResult result = await closer.RequestCloseAsync(TerminalProcess, CancellationToken.None);

        Assert.IsTrue(result.AllClosed);
        CollectionAssert.AreEqual(AskedTerminal, processes.Asked.ToArray());
        Assert.AreEqual("Closed: WindowsTerminal.", UpdateReviewSheet.CloseResultLine(result));
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public async Task OnlyAnAppOpenInThisSessionMustBeClosed()
    {
        // Codex review (PR #103): an app package is registered per account, so another user's open copy is only a warning, and
        // "Close it for me" never offers to close it.
        PackageDerivedRunningApplicationDetector detector = new(_ =>
        [
            new RunningProcessObservation(RunningProcessObservationKind.Name, "WindowsTerminal", OtherSession: true),
            new RunningProcessObservation(RunningProcessObservationKind.Name, "explorer")
        ]);
        RunningApplicationDetection elsewhere = await detector.DetectAsync(Selection("Microsoft.WindowsTerminal"), CancellationToken.None);
        Assert.IsTrue(elsewhere.AppearsToBeRunning);
        CollectionAssert.AreEqual(TerminalProcess, elsewhere.OnlyInOtherSessions.ToArray());
        Assert.IsFalse(elsewhere.OpenInThisSession);
        Assert.IsTrue(RunningApplicationAcknowledgement.Covers(true, TerminalProcess, elsewhere, mustBeClosed: true), "acknowledgeable");
        Assert.IsFalse(RunningApplicationAcknowledgement.Covers(false, null, elsewhere, mustBeClosed: true), "still a warning");

        PackageDerivedRunningApplicationDetector both = new(_ =>
        [
            new RunningProcessObservation(RunningProcessObservationKind.Name, "WindowsTerminal", OtherSession: true),
            new RunningProcessObservation(RunningProcessObservationKind.Name, "WindowsTerminal")
        ]);
        RunningApplicationDetection here = await both.DetectAsync(Selection("Microsoft.WindowsTerminal"), CancellationToken.None);
        Assert.IsTrue(here.OpenInThisSession, "open here as well");
        Assert.IsFalse(RunningApplicationAcknowledgement.Covers(true, TerminalProcess, here, mustBeClosed: true));

        UpdateReviewSheet sheet = UpdateReviewSheet.CreateBatch(
            [new UpdateReviewSheetInput(Selection("Microsoft.WindowsTerminal"), "Windows Terminal", "", null, elsewhere, Msix)]);
        Assert.IsTrue(sheet.CanConfirm);
        Assert.IsFalse(sheet.OffersCloseForMe, "another user's copy is never asked to close");
        Assert.IsEmpty(sheet.RunningProcessNames);
        Assert.AreEqual("Update 1 app anyway", sheet.PrimaryButtonLabel);
        StringAssert.Contains(sheet.Risks.Single(callout => callout.Kind == ReviewRiskKind.RunningApplication).Message, UpdateReviewSheet.OnlyInAnotherSessionText);
    }

    [TestMethod]
    [TestCategory("FR-UPD-06")]
    public void CheckAgainRefreshesOnlyTheWarningTheReviewConfirms()
    {
        // Codex review (PR #103): confirming after "Check again" covers the warning now shown; the reviewed selection stays.
        UpdateJobReview review = new(Guid.NewGuid(), Guid.NewGuid(), Selection("Contoso.Widget"));
        UpdateBatchReviewPreparation prepared = new(
            [new UpdateBatchReviewItem("key", new UpdateReviewPreparation(review, "ready", RunningApplicationDetection.NotDetected))], [], DateTimeOffset.UtcNow);
        RunningApplicationDetection now = new(true, WidgetProcess);

        UpdateBatchReviewPreparation refreshed = prepared.WithRunningApplications([now]);

        Assert.AreSame(now, refreshed.Items.Single().Preparation.RunningApplication);
        Assert.AreSame(review, refreshed.Items.Single().Preparation.Review);
        Assert.AreEqual(prepared.ReviewedAt, refreshed.ReviewedAt);
        Assert.IsTrue(refreshed.TryClaimForRun());
        Assert.IsFalse(prepared.TryClaimForRun(), "the copy shares the single use");
        Assert.ThrowsExactly<ArgumentException>(() => prepared.WithRunningApplications([]));
    }

    private static UpdateJobSelection Selection(string packageId) =>
        new(packageId, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "1.0", "2.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "fixture-evidence");

    private sealed class FakeProcesses : IClosableProcesses
    {
        private readonly Dictionary<int, (string Name, bool HasWindow, bool ExitsWhenAsked)> entries = [];
        private readonly HashSet<int> exited = [];
        public List<int> Asked { get; } = [];
        public int Terminated { get; }

        public void Add(string name, int id, bool hasWindow, bool exitsWhenAsked) => entries[id] = (name, hasWindow, exitsWhenAsked);

        public IReadOnlyList<int> InCurrentSession(string processName) =>
            [.. entries.Where(entry => string.Equals(entry.Value.Name, processName, StringComparison.OrdinalIgnoreCase)).Select(entry => entry.Key)];

        public bool RequestCloseMainWindow(int processId)
        {
            if (!entries[processId].HasWindow) return false;
            Asked.Add(processId);
            if (entries[processId].ExitsWhenAsked) exited.Add(processId);
            return true;
        }

        public bool HasExited(int processId) => exited.Contains(processId);
    }
}
