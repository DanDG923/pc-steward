using System.Text;
using PCSteward.Execution;
using Outcome = PCSteward.Execution.QualificationProbeDesktopOutcome;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §5.1, §5.2 steps 1–2, §5.4 and §7 D7 (slice S4): the
/// desktop's start classification and the probe branch runner over an in-memory result file system, a recording gate and a
/// scripted probe. Portable: no machine gate, temp folder, window or prompt is touched.
/// </summary>
[TestClass]
public sealed class QualificationProbeDesktopEntryTests
{
    private const string Flag = "--qualify-chocolatey-helper";
    private static readonly string[] OrderedEvents = ["folder-created", "gate-acquired", "probe", "file-written", "gate-released"];

    // ---- Classify (§5.1, D7) ----

    [TestMethod]
    [DataRow(@"C:\Program Files\PC Steward\PCSteward.Desktop.exe")]
    [DataRow(@"C:\Program Files\PC Steward\PCSteward.Desktop.dll")]
    [DataRow("")]
    public void TheExactTwoElementLineIsProbeWhateverElementZeroIs(string zero)
    {
        Assert.AreEqual(QualificationProbeLaunch.Probe, QualificationProbeEntry.Classify([zero, Flag]));
        Assert.AreEqual(QualificationProbeLaunch.Probe, QualificationProbeEntry.Classify([Flag, Flag]), "element 0 is not compared");
    }

    [TestMethod]
    public void TheFlagAtAnotherIndexOrWithMoreElementsIsMalformed()
    {
        string desktop = @"C:\Program Files\PC Steward\PCSteward.Desktop.exe";
        string[][] malformed =
        [
            [Flag], [desktop, Flag, "extra"], [desktop, "extra", Flag], [Flag, desktop], [desktop, Flag, Flag], [desktop, "--other", Flag, "x"],
        ];
        foreach (string[] line in malformed)
            Assert.AreEqual(QualificationProbeLaunch.Malformed, QualificationProbeEntry.Classify(line), string.Join(" ", line));
    }

    [TestMethod]
    public void ACaseOrWhitespaceVariantOrAnyOtherLineIsNormal()
    {
        string desktop = @"C:\Program Files\PC Steward\PCSteward.Desktop.exe";
        string[] variants = ["--Qualify-Chocolatey-Helper", "--QUALIFY-CHOCOLATEY-HELPER", " --qualify-chocolatey-helper", "--qualify-chocolatey-helper ",
            "--qualify-chocolatey-helper\t", "-qualify-chocolatey-helper", "--qualify-chocolatey-helpers", "x--qualify-chocolatey-helper",
            "--qualify-chocolatey-helper=1", "--qualify\u2011chocolatey-helper", "/qualify-chocolatey-helper"];
        foreach (string variant in variants)
        {
            Assert.AreEqual(QualificationProbeLaunch.Normal, QualificationProbeEntry.Classify([desktop, variant]), variant);
            Assert.AreEqual(QualificationProbeLaunch.Normal, QualificationProbeEntry.Classify([desktop, variant, "more"]), variant);
        }
        Assert.AreEqual(QualificationProbeLaunch.Normal, QualificationProbeEntry.Classify([desktop]));
        Assert.AreEqual(QualificationProbeLaunch.Normal, QualificationProbeEntry.Classify([]));
        Assert.AreEqual(QualificationProbeLaunch.Normal, QualificationProbeEntry.Classify(null));
        Assert.AreEqual(QualificationProbeLaunch.Normal, QualificationProbeEntry.Classify([desktop, "--kind", "qualify-chocolatey-helper"]),
            "the helper's kind pair is not the desktop flag");
    }

    // ---- Runner: step 1 before the gate (§5.2 step 1, review L-2) ----

    [TestMethod]
    public void AFolderFailureAcquiresNoGateRunsNoProbeAndWritesNoFile()
    {
        foreach (Action<FakeResultFileSystem> fault in new Action<FakeResultFileSystem>[]
        {
            fs => fs.AddDirectory(DesktopProbeFixture.Folder),                     // an existing folder refuses (no reuse or merge)
            fs => fs.AddDirectory(@"C:\Users\owner\AppData", reparse: true),       // a reparse component refuses
            fs => fs.FailOpenFolder = true,                                        // the folder cannot be held
            fs => fs.FolderFinalPathOverride = @"D:\elsewhere\pcsteward-exact-artifact", // the held folder resolves elsewhere
        })
        {
            FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
            fault(fs);
            int acquisitions = 0;
            RecordingProbe probe = new(() => DesktopProbeFixture.Accepted());
            int code = DesktopProbeFixture.Runner(fs, probe.ProbeAsync).Run(() => { acquisitions++; return new FakeGateSession(); });
            Assert.AreEqual(QualificationProbeEntry.NoResultFileExitCode, code);
            Assert.AreEqual(0, acquisitions, "no gate without a folder");
            Assert.AreEqual(0, probe.Calls);
            Assert.AreEqual(0, fs.Files.Count, "no file");
            Assert.IsFalse(fs.Calls.Any(call => call.StartsWith("write:", StringComparison.Ordinal)));
        }
    }

    [TestMethod]
    public void TheFolderIsCreatedBeforeTheGateAndTheFileIsWrittenBeforeTheGateIsReleasedOnTheAcquiringThread()
    {
        ProbeEvents events = new();
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        fs.Events = events;
        FakeGateSession? session = null;
        RecordingProbe probe = new(() => DesktopProbeFixture.Accepted(), events);
        int code = DesktopProbeFixture.Runner(fs, probe.ProbeAsync).Run(() =>
        {
            events.Add("gate-acquired");
            return session = new FakeGateSession(events);
        });

        Assert.AreEqual(0, code, "ResponseAccepted is the only zero exit");
        CollectionAssert.AreEqual(OrderedEvents, events.Snapshot());
        Assert.IsNotNull(session);
        Assert.AreEqual(1, session.Disposals);
        Assert.AreEqual(session.AcquiringThread, session.DisposingThread, "the session is disposed on the acquiring thread");
        Assert.AreEqual(Environment.CurrentManagedThreadId, session.AcquiringThread, "steps 1–2 run on the calling (OnLaunched) thread");
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, probe.Thread, "steps 3–10 run on a worker the calling thread waits for");
        Assert.IsTrue(fs.HeldFolderDisposed);
        Assert.AreEqual(1, fs.Files.Count);
        Assert.IsTrue(fs.Files.ContainsKey(DesktopProbeFixture.ResultPath));
        string json = Encoding.UTF8.GetString(fs.Files[DesktopProbeFixture.ResultPath]);
        StringAssert.StartsWith(json, "{\"schema\":\"pc-steward-qualification-probe-result:2\",\"desktopOutcome\":\"ResponseAccepted\",");
    }

    // ---- Runner: step 2 refusals (D7) ----

    [TestMethod]
    public void AHeldGateIsGateBusyInTheResultFileWithNoProbe()
    {
        (int code, string json, RecordingProbe probe) = Run(() => null);
        Assert.AreEqual(QualificationProbeEntry.ExitCode(Outcome.GateBusy), code);
        AssertRefusal(json, "GateBusy");
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    public void AThrowingAcquireIsGateUnavailable()
    {
        (int code, string json, RecordingProbe probe) = Run(() => throw new UnauthorizedAccessException("synthetic"));
        Assert.AreEqual(QualificationProbeEntry.ExitCode(Outcome.GateUnavailable), code);
        AssertRefusal(json, "GateUnavailable");
        Assert.AreEqual(0, probe.Calls);
    }

    [TestMethod]
    public void AnAbandonedGateObservedByThisAcquisitionIsGateRecoveryRequiredAndReleased()
    {
        FakeGateSession session = new() { Recovery = true };
        (int code, string json, RecordingProbe probe) = Run(() => session);
        Assert.AreEqual(QualificationProbeEntry.ExitCode(Outcome.GateRecoveryRequired), code);
        AssertRefusal(json, "GateRecoveryRequired");
        Assert.AreEqual(0, probe.Calls);
        Assert.AreEqual(0, session.MarkerReads, "recovery is checked before the marker");
        Assert.AreEqual(1, session.Disposals);
        Assert.AreEqual(session.AcquiringThread, session.DisposingThread);
    }

    [TestMethod]
    public void AnUnsettledOrUnreadableResetMarkerIsGateResetPending()
    {
        foreach (FakeGateSession session in new[] { new FakeGateSession { Settled = false }, new FakeGateSession { ThrowOnMarker = true } })
        {
            (int code, string json, RecordingProbe probe) = Run(() => session);
            Assert.AreEqual(QualificationProbeEntry.ExitCode(Outcome.GateResetPending), code);
            AssertRefusal(json, "GateResetPending");
            Assert.AreEqual(0, probe.Calls);
            Assert.AreEqual(1, session.MarkerReads);
            Assert.AreEqual(1, session.Disposals);
            Assert.AreEqual(session.AcquiringThread, session.DisposingThread);
        }
    }

    [TestMethod]
    public void AProbeFaultOrAnInconsistentRunRecordsNothingButStillReleasesTheGate()
    {
        foreach (Func<QualificationProbeRun> run in new Func<QualificationProbeRun>[]
        {
            () => throw new InvalidOperationException("synthetic fault"),
            () => DesktopProbeFixture.Accepted() with { Probe = null },                       // accepted without facts
            () => DesktopProbeFixture.Accepted() with { Outcome = Outcome.PeerNotVerified },  // facts without acceptance
        })
        {
            FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
            FakeGateSession session = new();
            RecordingProbe probe = new(run);
            int code = DesktopProbeFixture.Runner(fs, probe.ProbeAsync).Run(() => session);
            Assert.AreEqual(QualificationProbeEntry.NoResultFileExitCode, code);
            Assert.AreEqual(0, fs.Files.Count);
            Assert.AreEqual(1, session.Disposals);
        }
    }

    [TestMethod]
    public void AFailedWriteIsNoResultFileAndNeverAZeroExit()
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        fs.FailWrite = true;
        FakeGateSession session = new();
        int code = DesktopProbeFixture.Runner(fs, new RecordingProbe(() => DesktopProbeFixture.Accepted()).ProbeAsync).Run(() => session);
        Assert.AreEqual(QualificationProbeEntry.NoResultFileExitCode, code);
        Assert.AreEqual(1, session.Disposals);
    }

    [TestMethod]
    public void EveryOutcomeHasAFixedDistinctExitCodeAndOnlyAcceptanceIsZero()
    {
        int[] codes = [.. Enum.GetValues<Outcome>().Select(QualificationProbeEntry.ExitCode)];
        Assert.AreEqual(codes.Length, codes.Distinct().Count());
        Assert.AreEqual(0, QualificationProbeEntry.ExitCode(Outcome.ResponseAccepted));
        Assert.AreEqual(1, codes.Count(code => code == 0));
        CollectionAssert.DoesNotContain(codes, QualificationProbeEntry.NoResultFileExitCode);
        CollectionAssert.DoesNotContain(codes, QualificationProbeEntry.MalformedExitCode);
        Assert.AreEqual(QualificationProbeEntry.NoResultFileExitCode, QualificationProbeEntry.ExitCode((Outcome)99));
    }

    private static (int Code, string Json, RecordingProbe Probe) Run(Func<IQualificationProbeGateSession?> gate)
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        RecordingProbe probe = new(() => DesktopProbeFixture.Accepted());
        int code = DesktopProbeFixture.Runner(fs, probe.ProbeAsync).Run(gate);
        Assert.AreEqual(1, fs.Files.Count, "the refusal is recorded in the result file");
        return (code, Encoding.UTF8.GetString(fs.Files[DesktopProbeFixture.ResultPath]), probe);
    }

    private static void AssertRefusal(string json, string outcome)
    {
        StringAssert.StartsWith(json, $"{{\"schema\":\"pc-steward-qualification-probe-result:2\",\"desktopOutcome\":\"{outcome}\",");
        // Both compiled allow-list tokens since the Task 50 activation (ADR-031 owner decision 7), in ordinal order.
        StringAssert.Contains(json, "\"capabilityStatus\":{\"Update/ChocolateyClientSelfUpdateV1\":\"NotEvaluated\",\"Update/ChocolateyMachineRuntimeLimitsV3\":\"NotEvaluated\",\"Update/WinGetMachineSilentV1\":\"NotEvaluated\"}");
        StringAssert.Contains(json, "\"initialLaunchTrust\":false,");
        StringAssert.Contains(json, "\"helperProcessCreated\":false,");
        StringAssert.Contains(json, "\"helperExitCode\":null,\"pins\":null,\"probe\":null,");
    }
}
