using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using PCSteward.Execution;
using Outcome = PCSteward.Execution.QualificationProbeDesktopOutcome;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §5.3, §5.3a bound and §7 D6 (slice S4): the closed result
/// document (schema <c>:2</c>, exact order, closed domains, privacy, 4,096-B bound) and the result-file location rules over an
/// in-memory file system (portable), plus the Windows primitives on real folders (Windows-native: they report
/// <see cref="PlatformNotSupportedException"/> elsewhere, never silently skipped).
/// </summary>
[TestClass]
public sealed class QualificationProbeResultFileTests
{
    private static readonly DateTimeOffset Started = DesktopProbeFixture.Started;
    private static readonly DateTimeOffset Finished = Started.AddSeconds(42);

    // ---- The closed document (§5.3) ----

    [TestMethod]
    public void AnAcceptedRunIsTheExactClosedDocumentInSpecOrder()
    {
        byte[] bytes = QualificationProbeResultDocument.Create(DesktopProbeFixture.Accepted(), Started, Finished);
        string json = Encoding.UTF8.GetString(bytes);
        string expected =
            "{\"schema\":\"pc-steward-qualification-probe-result:2\",\"desktopOutcome\":\"ResponseAccepted\"," +
            "\"capabilityStatus\":{\"Update/ChocolateyMachineRuntimeLimitsV3\":\"Supported\"}," +
            "\"initialLaunchTrust\":true,\"finalLaunchTrust\":true,\"helperProcessCreated\":true,\"pipeConnected\":true,\"desktopPeerVerified\":true," +
            "\"helperFileIdEqual\":true,\"responseFrameReceived\":true,\"pipeEofObserved\":true,\"helperExitObserved\":true,\"bindingHeldUntilExit\":true," +
            "\"terminationFailed\":false,\"helperExitCode\":0," +
            "\"pins\":{\"ledgerSha256\":\"" + new string('A', 64) + "\",\"capabilityFileSha256\":\"" + new string('B', 64) + "\",\"desktopImageSha256\":\"" +
            new string('C', 64) + "\",\"helperImageSha256\":\"" + new string('9', 64) + "\",\"signerThumbprint\":\"" + new string('E', 40) +
            "\",\"signerNotAfterUtc\":\"2026-11-13T20:18:09.000Z\"}," +
            "\"probe\":{\"tokenVerdict\":\"Accepted\",\"g1Status\":{\"Update/ChocolateyMachineRuntimeLimitsV3\":\"Supported\"},\"observation\":\"Observed\"," +
            "\"failedStep\":null,\"failedCheck\":\"None\",\"failedAccess\":\"None\",\"refusalReason\":\"None\",\"refusalDetail\":\"None\"," +
            "\"nodesEvaluated\":123456,\"ancestorsEvaluated\":12345,\"observedClientVersion\":\"2.7.5\",\"fingerprintPresent\":true," +
            "\"fingerprintLength\":64,\"diagnostic\":\"probe-observed\"}," +
            "\"startedAtUtc\":\"2026-11-04T10:00:00.000Z\",\"finishedAtUtc\":\"2026-11-04T10:00:42.000Z\"}";
        Assert.AreEqual(expected, json);
        Assert.IsFalse(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM");

        using JsonDocument document = JsonDocument.Parse(bytes);
        CollectionAssert.AreEqual(QualificationProbeResultDocument.Fields.ToArray(), document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(QualificationProbeResultDocument.PinFields.ToArray(),
            document.RootElement.GetProperty("pins").EnumerateObject().Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(QualificationProbeResultDocument.ProbeFields.ToArray(),
            document.RootElement.GetProperty("probe").EnumerateObject().Select(p => p.Name).ToArray());
        CollectionAssert.AreEqual(QualificationProbeWireCodec.Fields.Skip(3).ToArray(), QualificationProbeResultDocument.ProbeFields.ToArray(),
            "probe = response fields 3–16, in order (no nonce)");
    }

    [TestMethod]
    public void TheDocumentHoldsNoNoncePathSidPipeNameOrFingerprintValue()
    {
        QualificationProbeRun run = DesktopProbeFixture.Accepted();
        string json = Encoding.UTF8.GetString(QualificationProbeResultDocument.Create(run, Started, Finished));
        foreach (string secret in new[] { DesktopProbeFixture.Nonce, ProbeFixture.Sid, "S-1-5-", "pcsteward-reviewed-", "\\", "C:", "Program Files", "Temp" })
            Assert.IsFalse(json.Contains(secret, StringComparison.OrdinalIgnoreCase), $"the result names {secret}");
        Assert.IsFalse(json.Contains("\"nonce\"", StringComparison.Ordinal));
        Assert.IsFalse(json.Contains("fingerprint\"", StringComparison.Ordinal), "only fingerprintPresent and fingerprintLength");
        // The facts type carries no fingerprint value at all; only its presence and length reach the file.
        Assert.IsFalse(typeof(QualificationProbeFacts).GetProperties().Any(property => property.Name == "Fingerprint"));
    }

    [TestMethod]
    public void EveryRefusalOutcomeIsAClosedDocumentWithItsFactsAndNoProbe()
    {
        foreach (Outcome outcome in Enum.GetValues<Outcome>().Where(outcome => outcome != Outcome.ResponseAccepted))
        {
            bool beforeHold = outcome is Outcome.GateBusy or Outcome.GateUnavailable or Outcome.GateRecoveryRequired or Outcome.GateResetPending or
                Outcome.DesktopNotLimited or Outcome.ConflictingProcess;
            QualificationProbeRun run = beforeHold ? QualificationProbeRun.Refused(outcome)
                : outcome == Outcome.CapabilityNotHeld ? new(outcome, DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Mismatch),
                    QualificationProbeTransportFacts.None with { InitialLaunchTrust = true }, null, null)
                : new(outcome, DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported), DesktopProbeFixture.AllFacts(26), DesktopProbeFixture.Pins(), null);
            string json = Encoding.UTF8.GetString(QualificationProbeResultDocument.Create(run, Started, Finished));
            StringAssert.Contains(json, $"\"desktopOutcome\":\"{outcome}\"");
            StringAssert.Contains(json, "\"probe\":null");
            Assert.AreEqual(beforeHold || outcome == Outcome.CapabilityNotHeld, json.Contains("\"pins\":null", StringComparison.Ordinal), outcome.ToString());
        }
        // LaunchTrustFailed is either the initial (no hold, no pins) or the final trust (after the hold, with pins).
        Assert.IsNull(QualificationProbeResultDocument.Violation(QualificationProbeRun.Refused(Outcome.LaunchTrustFailed), Started, Finished,
            QualificationProbeContract.Compiled));
    }

    [TestMethod]
    public void AnInconsistentRunIsRefusedSoNothingIsWritten()
    {
        QualificationProbeRun accepted = DesktopProbeFixture.Accepted();
        QualificationProbeRun[] invalid =
        [
            accepted with { Probe = null },
            accepted with { Outcome = Outcome.NoValidResponse },
            accepted with { Pins = null },
            accepted with { Pins = DesktopProbeFixture.Pins() with { LedgerSha256 = new string('a', 64) } },
            accepted with { Pins = DesktopProbeFixture.Pins() with { SignerThumbprint = new string('E', 39) } },
            accepted with { CapabilityStatus = DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Mismatch) },
            accepted with { CapabilityStatus = [] },
            accepted with { Probe = DesktopProbeFixture.Passing() with { FingerprintLength = 63 } },
            accepted with { Outcome = (Outcome)77 },
            QualificationProbeRun.Refused(Outcome.GateBusy) with { Pins = DesktopProbeFixture.Pins() },
            QualificationProbeRun.Refused(Outcome.GateBusy) with { CapabilityStatus = DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported) },
            QualificationProbeRun.Refused(Outcome.CapabilityNotHeld),
            QualificationProbeRun.Refused(Outcome.ElevationDeclined),
        ];
        foreach (QualificationProbeRun run in invalid)
            Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeResultDocument.Create(run, Started, Finished), run.Outcome.ToString());
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeResultDocument.Create(accepted, Finished, Started), "finished before started");
        Assert.ThrowsExactly<ArgumentException>(() => QualificationProbeResultDocument.Create(accepted, Started.ToOffset(TimeSpan.FromHours(2)), Finished),
            "UTC only");
    }

    [TestMethod]
    public void TheLargestValidResultFileIsAtMost4096Bytes()
    {
        // §5.3a: the largest valid document over the compiled contract: every fact true, a 10-digit exit code, every pin, and the
        // largest valid accepted response (counts at their maxima; the longest verdict is a token refusal).
        QualificationProbeFacts[] largestFacts =
        [
            DesktopProbeFixture.Passing(QualificationProbeWireCodec.MaximumNodesEvaluated, QualificationProbeWireCodec.MaximumAncestorsEvaluated),
            DesktopProbeFixture.TokenRefused() with { Verdict = QualificationProbeTokenVerdict.EvaluationNotIdentificationLevel },
            new(QualificationProbeTokenVerdict.Accepted, DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported), QualificationProbeObservation.Refused, 7,
                QualificationProbeFailedCheck.SetupAccessGuard, QualificationProbeFailedAccess.Other, QualificationProbeRefusalReason.MachineInputsUnsupported,
                QualificationProbeRefusalDetail.SetupAccessOverride, QualificationProbeWireCodec.MaximumNodesEvaluated, QualificationProbeWireCodec.MaximumAncestorsEvaluated,
                null, false, 0, QualificationProbeWireCodec.ObservationRefusedDiagnostic),
        ];
        int largest = 0;
        foreach (QualificationProbeFacts facts in largestFacts)
        {
            QualificationProbeRun run = DesktopProbeFixture.Accepted(facts) with { Transport = DesktopProbeFixture.AllFacts(int.MinValue) };
            largest = Math.Max(largest, QualificationProbeResultDocument.Create(run, DateTimeOffset.MaxValue.ToUniversalTime().AddYears(-1), DateTimeOffset.MaxValue.ToUniversalTime()).Length);
        }
        Assert.IsLessThanOrEqualTo(QualificationProbeResultDocument.MaximumBytes, largest);
        Assert.AreEqual(4096, QualificationProbeResultDocument.MaximumBytes);
        // Even with eight 64-B allow-list tokens the document stays under the bound (the §4.3 allow-list assumption).
        string[] keys = [.. Enumerable.Range(0, 8).Select(index => "Update/" + new string('X', 56) + index)];
        QualificationProbeContract eight = QualificationProbeContract.CreateForTests(keys, ["2.7.5"]);
        IReadOnlyList<QualificationProbeCapabilityEntry> g1 = [.. keys.Select(key => new QualificationProbeCapabilityEntry(key, QualificationProbeCapabilityStatus.Supported))];
        QualificationProbeRun wide = DesktopProbeFixture.Accepted(largestFacts[0] with { G1Status = g1 }) with { CapabilityStatus = g1, Transport = DesktopProbeFixture.AllFacts(int.MinValue) };
        Assert.IsLessThanOrEqualTo(4096, QualificationProbeResultDocument.Create(wide, Started, Finished, eight).Length);
    }

    // ---- Location rules over an in-memory file system (D6, portable) ----

    [TestMethod]
    public void TheFolderIsCreatedUnderTheLongTempPathAfterEveryComponentIsCheckedFromTheDriveRoot()
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        using IQualificationResultFolder? folder = QualificationResultFile.TryCreateFolder(DesktopProbeFixture.Temp, fs);
        Assert.IsNotNull(folder);
        Assert.AreEqual(DesktopProbeFixture.Folder, folder.Path);
        CollectionAssert.AreEqual(new[]
        {
            "long:" + DesktopProbeFixture.Temp, @"inspect:C:\", @"inspect:C:\Users", @"inspect:C:\Users\owner", @"inspect:C:\Users\owner\AppData",
            @"inspect:C:\Users\owner\AppData\Local", "inspect:" + DesktopProbeFixture.LongTemp, "mkdir:" + DesktopProbeFixture.Folder, "open:" + DesktopProbeFixture.Folder,
        }, fs.Calls);
        Assert.IsTrue(QualificationResultFile.TryWrite(folder, [(byte)'{', (byte)'}']));
        Assert.IsFalse(QualificationResultFile.TryWrite(folder, [(byte)'{', (byte)'}']), "CREATE_NEW: never overwritten");
        Assert.IsFalse(QualificationResultFile.TryWrite(folder, []), "an empty document is not written");
        Assert.AreEqual(1, fs.Files.Count);
    }

    [TestMethod]
    public void AShortNameTempPathIsCanonicalisedToItsLongForm()
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        const string Short = @"C:\Users\OWNER~1\AppData\Local\Temp\";
        fs.LongPaths[Short] = DesktopProbeFixture.LongTemp;
        using IQualificationResultFolder? folder = QualificationResultFile.TryCreateFolder(Short, fs);
        Assert.IsNotNull(folder);
        Assert.AreEqual(DesktopProbeFixture.Folder, folder.Path, "8.3 names are expanded before any component is opened");
        Assert.IsFalse(fs.Calls.Any(call => call.Contains("~1", StringComparison.Ordinal) && !call.StartsWith("long:", StringComparison.Ordinal)));

        // A component whose final path differs from the canonical path (an unexpanded short name) refuses.
        FakeResultFileSystem unexpanded = DesktopProbeFixture.FileSystem();
        unexpanded.AddDirectory(@"C:\Users\OWNER~1", finalPath: @"C:\Users\owner");
        unexpanded.LongPaths[Short] = Short;
        Assert.IsNull(QualificationResultFile.TryCreateFolder(Short, unexpanded));
        FakeResultFileSystem failing = DesktopProbeFixture.FileSystem();
        failing.LongPaths[Short] = null!;
        Assert.IsNull(QualificationResultFile.TryCreateFolder(Short, failing), "no long form, no folder");
    }

    [TestMethod]
    [DataRow(@"\\server\share\Temp\")]
    [DataRow(@"\\?\C:\Users\owner\AppData\Local\Temp\")]
    [DataRow(@"\\.\C:\Temp\")]
    [DataRow(@"Temp\")]
    [DataRow(@"C:Temp\")]
    [DataRow(@"C:/Users/owner/Temp/")]
    [DataRow(@"C:\Users\..\Temp\")]
    [DataRow(@"C:\Users\.\Temp\")]
    [DataRow(@"C:\Users\\Temp\")]
    [DataRow(@"C:\Users\owner\Temp\\")]
    [DataRow(@"C:\Users\owner\Temp ")]
    [DataRow(@"C:\Temp:stream\")]
    [DataRow("")]
    public void AUncDeviceRelativeOrNonCanonicalTempPathRefusesBeforeAnyCall(string temp)
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        Assert.IsNull(QualificationResultFile.TryCreateFolder(temp, fs));
        Assert.AreEqual(0, fs.Calls.Count, "no file-system call for a refused temp path");
    }

    [TestMethod]
    public void AUncLongFormIsRefusedToo()
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        fs.LongPaths[DesktopProbeFixture.Temp] = @"\\server\share\Temp";
        Assert.IsNull(QualificationResultFile.TryCreateFolder(DesktopProbeFixture.Temp, fs));
        Assert.IsFalse(fs.Calls.Any(call => call.StartsWith("inspect:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AReparseMissingFileOrRedirectedComponentRefusesWithoutCreatingTheFolder()
    {
        foreach (Action<FakeResultFileSystem> fault in new Action<FakeResultFileSystem>[]
        {
            fs => fs.AddDirectory(@"C:\Users", reparse: true),
            fs => fs.AddDirectory(DesktopProbeFixture.LongTemp, reparse: true),
            fs => fs.AddDirectory(@"C:\Users\owner\AppData\Local", finalPath: @"D:\redirected\Local"),
            fs => fs.AddFile(@"C:\Users\owner\AppData"),
        })
        {
            FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
            fault(fs);
            Assert.IsNull(QualificationResultFile.TryCreateFolder(DesktopProbeFixture.Temp, fs));
            Assert.IsFalse(fs.Calls.Any(call => call.StartsWith("mkdir:", StringComparison.Ordinal)), "nothing is created");
        }
        FakeResultFileSystem missing = new();
        missing.AddDirectory(@"C:\");
        Assert.IsNull(QualificationResultFile.TryCreateFolder(DesktopProbeFixture.Temp, missing), "a missing component refuses");
    }

    [TestMethod]
    public void AnExistingFolderRefusesWithNoReuseOrMerge()
    {
        FakeResultFileSystem fs = DesktopProbeFixture.FileSystem();
        fs.AddDirectory(DesktopProbeFixture.Folder);
        Assert.IsNull(QualificationResultFile.TryCreateFolder(DesktopProbeFixture.Temp, fs));
        Assert.IsFalse(fs.Calls.Any(call => call.StartsWith("open:", StringComparison.Ordinal)), "the first run's folder is never opened");
        Assert.AreEqual(0, fs.Files.Count);
    }

    // ---- Windows primitives on real folders (Windows-native) ----

    [TestMethod]
    public void WindowsWritesOneNewFileInAFreshFolderAndRefusesAnExistingFolderOrFile()
    {
        RequireWindows("§5.3 result folder and CREATE_NEW file");
        using RealTemp temp = new();
        IQualificationResultFileSystem fs = WindowsQualificationResultFileSystem.Instance;
        byte[] document = QualificationProbeResultDocument.Create(QualificationProbeRun.Refused(Outcome.GateBusy), Started, Finished);
        using (IQualificationResultFolder? folder = QualificationResultFile.TryCreateFolder(temp.Root + "\\", fs))
        {
            Assert.IsNotNull(folder);
            Assert.AreEqual(Path.Combine(temp.Root, QualificationResultFile.FolderName), folder.Path, ignoreCase: true);
            Assert.IsTrue(QualificationResultFile.TryWrite(folder, document));
            Assert.IsFalse(QualificationResultFile.TryWrite(folder, document), "CREATE_NEW never overwrites");
            // The held folder cannot be renamed or deleted while the probe holds it (no delete share).
            bool moved;
            try { Directory.Move(folder.Path, folder.Path + "-moved"); moved = true; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { moved = false; }
            Assert.IsFalse(moved, "the held folder cannot be renamed");
        }
        string written = Path.Combine(temp.Root, QualificationResultFile.FolderName, QualificationResultFile.FileName);
        CollectionAssert.AreEqual(document, File.ReadAllBytes(written));
        Assert.IsNull(QualificationResultFile.TryCreateFolder(temp.Root + "\\", fs), "an existing folder refuses");
        CollectionAssert.AreEqual(document, File.ReadAllBytes(written), "the first run's file is untouched");
    }

    [TestMethod]
    public void WindowsRefusesAJunctionComponentAndAUncTempPath()
    {
        RequireWindows("§5.3 reparse refusal");
        using RealTemp temp = new();
        string target = Directory.CreateDirectory(Path.Combine(temp.Root, "target")).FullName;
        string link = Path.Combine(temp.Root, "link");
        N1NativeFixture.Junction(link, target);
        Assert.IsNull(QualificationResultFile.TryCreateFolder(link + "\\", WindowsQualificationResultFileSystem.Instance));
        Assert.IsFalse(Directory.Exists(Path.Combine(target, QualificationResultFile.FolderName)), "nothing was created through the junction");
        Assert.IsNull(QualificationResultFile.TryCreateFolder(@"\\localhost\C$\Windows\Temp\", WindowsQualificationResultFileSystem.Instance));
        QualificationResultNode? node = WindowsQualificationResultFileSystem.Instance.Inspect(link);
        Assert.IsNotNull(node);
        Assert.IsTrue(node.IsReparsePoint, "the component is opened without following the junction");
    }

    [TestMethod]
    public void WindowsCanonicalisesAShortNameTempPath()
    {
        RequireWindows("§5.3 8.3 canonicalisation");
        using RealTemp temp = new();
        string longDirectory = Directory.CreateDirectory(Path.Combine(temp.Root, "Long Directory Name For Short")).FullName;
        string? shortPath = ShortPath(longDirectory);
        if (shortPath is null || string.Equals(shortPath, longDirectory, StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("8.3 short names are disabled on this volume.");
        Assert.AreEqual(longDirectory, WindowsQualificationResultFileSystem.Instance.GetLongPath(shortPath), ignoreCase: true);
        using IQualificationResultFolder? folder = QualificationResultFile.TryCreateFolder(shortPath + "\\", WindowsQualificationResultFileSystem.Instance);
        Assert.IsNotNull(folder);
        Assert.AreEqual(Path.Combine(longDirectory, QualificationResultFile.FolderName), folder.Path, ignoreCase: true);
    }

    private static string? ShortPath(string path)
    {
        char[] buffer = new char[32768];
        uint length = GetShortPathName(path, buffer, (uint)buffer.Length);
        return length == 0 || length >= buffer.Length ? null : new string(buffer, 0, (int)length);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint GetShortPathName(string longPath, [System.Runtime.InteropServices.Out] char[] shortPath, uint bufferLength);

    private static void RequireWindows(string evidence)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException($"Windows-native test ({evidence}): it runs on Windows CI and is reported as failed on other platforms.");
    }

    /// <summary>A GUID-named temp directory, canonicalised to its final (long) path.</summary>
    private sealed class RealTemp : IDisposable
    {
        internal RealTemp()
        {
            string created = Path.Combine(Path.GetTempPath(), $"pcsteward-result-file-{Guid.NewGuid():N}");
            Directory.CreateDirectory(created);
            string probe = Path.Combine(created, "probe");
            File.WriteAllText(probe, "probe");
            using (SafeFileHandle handle = File.OpenHandle(probe, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert.IsTrue(DeploymentFileAccess.TryGetFinalPath(handle, out string final));
                Root = Path.GetDirectoryName(final)!;
            }
            File.Delete(probe);
        }

        internal string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
