using System.Buffers.Binary;
using PCSteward.Domain;
using PCSteward.Execution;
using Outcome = PCSteward.Execution.QualificationProbeDesktopOutcome;
using Status = PCSteward.Execution.HelperProtocolCapabilityStatus;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §5.2 steps 3–10, §2 and §7 D3/D8 (slice S4): the desktop
/// probe transport's refusal ladder over fakes (no prompt is possible: the fake launcher starts nothing), the pure process and
/// hold rules, the probe line against the helper parsers, and the post-launch session over a real pipe pair, including the
/// real S3 probe endpoint as the helper end. Portable.
/// </summary>
[TestClass]
public sealed class QualificationProbeDesktopTransportTests
{
    private static readonly string[] UpdateOnlyEvents = ["limited", "processes", "paths", "trust", "identity", "pipe"];

    // ---- Steps 3–7: the refusal ladder (no UAC unless every pre-UAC check passes) ----

    [TestMethod]
    public async Task ANonLimitedDesktopTokenRefusesBeforeAnyOtherCheck()
    {
        ProbeTransportFakes fakes = new() { Limited = false };
        QualificationProbeRun run = await Probe(fakes);
        AssertRefusedBeforeHold(run, Outcome.DesktopNotLimited);
        Assert.AreEqual(0, fakes.ProcessCalls + fakes.PathCalls + fakes.TrustCalls + fakes.PipeCalls + fakes.Launches);
    }

    [TestMethod]
    [DataRow("choco")]
    [DataRow("CHOCO")]
    [DataRow("PCSteward.ScheduledHost")]
    [DataRow("pcsteward.desktop")]
    [DataRow("PCSteward.ElevatedHelper")]
    [DataRow("throw")]
    public async Task AConflictingProcessOrAFailedEnumerationRefusesBeforeAnyLaunchTrustCall(string name)
    {
        // D8: a choco process, another PCSteward* process (a second desktop has another PID), or an enumeration failure.
        ProbeTransportFakes fakes = new()
        {
            Processes = name == "throw" ? () => throw new InvalidOperationException("synthetic") : () => [(4242, "PCSteward.Desktop"), (99, name)],
        };
        QualificationProbeRun run = await Probe(fakes);
        AssertRefusedBeforeHold(run, Outcome.ConflictingProcess);
        Assert.AreEqual(0, fakes.TrustCalls, "before any launch-trust call");
        Assert.AreEqual(0, fakes.PathCalls + fakes.PipeCalls + fakes.Launches);
    }

    [TestMethod]
    public void TheProcessRuleIsChocoAnywhereOrPCStewardOtherThanTheCurrentProcess()
    {
        Assert.IsFalse(ReviewedHelperProbeTransport.HasConflictingProcess([(1, "PCSteward.Desktop"), (2, "explorer"), (3, "chocolatey"), (4, "choco.exe.old")], 1));
        Assert.IsTrue(ReviewedHelperProbeTransport.HasConflictingProcess([(1, "PCSteward.Desktop"), (2, "PCSteward.Desktop")], 1), "a second desktop");
        Assert.IsTrue(ReviewedHelperProbeTransport.HasConflictingProcess([(1, "choco")], 1), "choco even with the current PID");
        Assert.IsTrue(ReviewedHelperProbeTransport.HasConflictingProcess([(2, "PCStewardX")], 1));
        Assert.IsTrue(ReviewedHelperProbeTransport.HasConflictingProcess([(2, null!)], 1), "an unreadable name fails closed");
        Assert.IsFalse(ReviewedHelperProbeTransport.HasConflictingProcess([], 1));
    }

    [TestMethod]
    [DataRow("no-paths")]
    [DataRow("untrusted")]
    [DataRow("no-identity")]
    [DataRow("pipe")]
    public async Task InitialTrustIdentityOrPipeFailuresAreLaunchTrustFailedWithoutAHold(string scenario)
    {
        ProbeTransportFakes fakes = new();
        switch (scenario)
        {
            case "no-paths": fakes.Paths = null; break;
            case "untrusted": fakes.Trust = _ => false; break;
            case "no-identity": fakes.Identity = null; break;
            case "pipe": fakes.PipeThrows = true; break;
        }
        QualificationProbeRun run = await Probe(fakes);
        AssertRefusedBeforeHold(run, Outcome.LaunchTrustFailed);
        Assert.AreEqual(scenario is "no-identity" or "pipe", run.Transport.InitialLaunchTrust);
        Assert.AreEqual(0, ((RecordingHoldCapability)fakes.Capability).Holds.Count, "no hold before the initial trust, identity and pipe");
        Assert.AreEqual(0, fakes.Launches);
    }

    [TestMethod]
    [DataRow("Absent")]
    [DataRow("Incomplete")]
    [DataRow("Unreadable")]
    [DataRow("Mismatch")]
    [DataRow("Supported")]
    [DataRow("throw")]
    public async Task EveryAllowListTokenIsHeldAndAnythingButAHeldAgreeingSetIsCapabilityNotHeld(string scenario)
    {
        // Step 6: a status other than Supported, a Supported status with no binding (the fake never holds) and a throwing hold
        // all refuse before the final trust and before any launch; the desktop's statuses are recorded per token.
        RecordingHoldCapability capability = scenario == "throw"
            ? new(Status.Supported) { Throw = true }
            : new(Enum.Parse<Status>(scenario));
        ProbeTransportFakes fakes = new() { Capability = capability };
        QualificationProbeRun run = await Probe(fakes);

        Assert.AreEqual(Outcome.CapabilityNotHeld, run.Outcome);
        CollectionAssert.AreEqual(DeploymentCapabilityPolicy.CapabilityOperationAllowlist.OrderBy(DeploymentCapabilityPolicy.OperationToken, StringComparer.Ordinal).ToArray(),
            capability.Holds.ToArray(), "TryHold for every compiled allow-list token, in ordinal token order");
        QualificationProbeCapabilityStatus expected = scenario == "throw" ? QualificationProbeCapabilityStatus.Unreadable : Enum.Parse<QualificationProbeCapabilityStatus>(scenario);
        CollectionAssert.AreEqual(DesktopProbeFixture.G1(expected).ToArray(), run.CapabilityStatus.ToArray());
        Assert.IsNull(run.Pins);
        Assert.IsNull(run.Probe);
        Assert.AreEqual(1, fakes.TrustCalls, "the final trust is not reached");
        Assert.AreEqual(0, fakes.Launches, "no prompt");
        Assert.IsTrue(run.Transport.InitialLaunchTrust);
        Assert.IsFalse(run.Transport.FinalLaunchTrust || run.Transport.HelperProcessCreated);
        CollectionAssert.AreEqual(UpdateOnlyEvents, fakes.Events.Snapshot(), "elevation, processes, paths, initial trust, identity, pipe, then the holds");
        // The refusal is a closed result document.
        Assert.IsNull(QualificationProbeResultDocument.Violation(run, DesktopProbeFixture.Started, DesktopProbeFixture.Started, QualificationProbeContract.Compiled));
    }

    [TestMethod]
    public async Task AFailedFinalLaunchTrustAfterAnAgreeingHoldIsLaunchTrustFailedWithNoPromptAndEveryBindingReleased()
    {
        // Step 7: the initial trust passes, every token holds and agrees, then the final trust (the second call) fails.
        ProbeTransportFakes fakes = new() { Trust = n => n == 1 };
        using AgreeingHoldCapability capability = new(fakes.Paths!.HelperPath);
        fakes.Capability = capability;
        QualificationProbeRun run = await Probe(fakes);

        Assert.AreEqual(Outcome.LaunchTrustFailed, run.Outcome);
        Assert.AreEqual(2, fakes.TrustCalls, "the initial and the final launch trust");
        Assert.AreEqual(0, fakes.Launches, "no prompt");
        Assert.AreEqual(DeploymentCapabilityPolicy.CapabilityOperationAllowlist.Count, capability.Bindings.Count, "every token was held");
        Assert.IsTrue(capability.Bindings.All(static binding => binding.IsDisposed && !binding.IsHolding), "every binding is released");
        Assert.IsTrue(capability.Handles.All(static handle => handle.IsClosed), "every held handle is closed");
        CollectionAssert.AreEqual(DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.Supported).ToArray(), run.CapabilityStatus.ToArray());
        Assert.AreEqual(DesktopProbeFixture.Pins(), run.Pins, "the pins of the hold are recorded");
        Assert.IsNull(run.Probe);
        Assert.IsTrue(run.Transport.InitialLaunchTrust);
        Assert.IsFalse(run.Transport.FinalLaunchTrust);
        Assert.IsFalse(run.Transport.HelperProcessCreated || run.Transport.PipeConnected);
        CollectionAssert.AreEqual(UpdateOnlyEvents.Append("trust").ToArray(), fakes.Events.Snapshot(), "the final trust is the last step");
        Assert.IsNull(QualificationProbeResultDocument.Violation(run, DesktopProbeFixture.Started, DesktopProbeFixture.Started, QualificationProbeContract.Compiled));
    }

    [TestMethod]
    public void TheHoldsMustAllBeSupportedHoldingOnTheFixedHelperWithOneIdentityAndEqualCompletePins()
    {
        const string Helper = @"C:\Program Files\PC Steward\PCSteward.ElevatedHelper.exe";
        WindowsFileIdentity identity = new(7, 42);
        QualificationProbePins pins = DesktopProbeFixture.Pins();
        ProbeHold good = new(true, true, Helper, identity, pins);
        Assert.AreEqual(pins, ReviewedHelperProbeHold.Agree([good], Helper));
        Assert.AreEqual(pins, ReviewedHelperProbeHold.Agree([good, good], Helper.ToUpperInvariant()));
        ProbeHold[] bad =
        [
            good with { Supported = false }, good with { Holding = false }, good with { HelperPath = @"C:\Elsewhere\PCSteward.ElevatedHelper.exe" },
            good with { HelperPath = string.Empty }, good with { HelperIdentity = default }, good with { Pins = null },
            good with { Pins = pins with { LedgerSha256 = "abc" } }, good with { Pins = pins with { SignerThumbprint = new string('e', 40) } },
            good with { Pins = pins with { SignerNotAfterUtc = new DateTimeOffset(2026, 11, 13, 20, 18, 9, TimeSpan.FromHours(1)) } },
        ];
        foreach (ProbeHold hold in bad) Assert.IsNull(ReviewedHelperProbeHold.Agree([hold], Helper), hold.ToString());
        Assert.IsNull(ReviewedHelperProbeHold.Agree([good, good with { HelperIdentity = new(7, 43) }], Helper), "one FileIdInfo across tokens");
        Assert.IsNull(ReviewedHelperProbeHold.Agree([good, good with { Pins = DesktopProbeFixture.Pins('F') }], Helper), "equal pins across tokens");
        Assert.IsNull(ReviewedHelperProbeHold.Agree([], Helper));
        Assert.IsNull(ReviewedHelperProbeHold.Agree([good], string.Empty));
    }

    [TestMethod]
    public void EveryCapabilityStatusMapsByExactName()
    {
        foreach (Status status in Enum.GetValues<Status>())
            Assert.AreEqual(status.ToString(), ReviewedHelperProbeTransport.ProbeStatus(status).ToString());
        Assert.AreEqual(QualificationProbeCapabilityStatus.Unreadable, ReviewedHelperProbeTransport.ProbeStatus((Status)42));
    }

    // ---- §2 and D3: the probe line ----

    [TestMethod]
    public void TheProbeLineHasTheKindFirstAndParsesOnlyAsAProbeLine()
    {
        string pipe = WindowsReviewedHelperTransport.CreatePipeName();
        string nonce = DesktopProbeFixture.Nonce;
        string[] line = [.. ReviewedHelperProbeTransport.ProbeArguments(4242, pipe, nonce)];
        CollectionAssert.AreEqual(new[] { "--kind", "qualify-chocolatey-helper", "--protocol", "2", "--desktop-pid", "4242", "--pipe", pipe, "--nonce", nonce }, line);
        Assert.IsTrue(ReviewedHelperProbeBootstrap.SelectsProbe(line));
        Assert.IsTrue(ReviewedHelperProbeBootstrap.TryParse(line, out ReviewedHelperProbeBootstrap? parsed));
        Assert.AreEqual(new ReviewedHelperProbeBootstrap(4242, pipe, nonce), parsed);
        Assert.IsFalse(ReviewedHelperBootstrap.SelectsProtocol2(line), "the update dispatch never selects it");
        Assert.IsFalse(ReviewedHelperBootstrap.TryParse(line, out _));
        Assert.IsFalse(HelperBootstrap.TryParse(line, out _));
        // Elements 2–9 are the update line's shape; the probe parser refuses them (the update launcher never emits --kind).
        Assert.IsFalse(ReviewedHelperProbeBootstrap.TryParse(line[2..], out _));
        Assert.AreEqual(2, ReviewedHelperProbeTransport.ProbeArguments(1, pipe, nonce).Count(argument => argument.StartsWith("--kind", StringComparison.Ordinal) ||
            argument == QualificationProbeWireCodec.Kind));
    }

    [TestMethod]
    public void TheBudgetsAreTheUpdateTransportsThirtySecondsElevenMinutesAndFiveSeconds()
    {
        Assert.AreEqual(TimeSpan.FromSeconds(30), ReviewedHelperProbeTransport.Budgets.Connection);
        Assert.AreEqual(TimeSpan.FromMinutes(11), ReviewedHelperProbeTransport.Budgets.Execution);
        Assert.AreEqual(TimeSpan.FromSeconds(5), ReviewedHelperProbeTransport.Budgets.Exit);
        Assert.AreSame(ReviewedHelperBudgets.Production, ReviewedHelperProbeTransport.Budgets);
        Assert.IsGreaterThan(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(10), ReviewedHelperProbeTransport.Budgets.Execution,
            "the helper's last write (10 min 10 s after t0) ends before the desktop gives up");
    }

    // ---- Steps 8–10: the session over a real pipe pair ----

    [TestMethod]
    public async Task TheRealS3EndpointsPassingFrameIsAcceptedWithEveryFactAndNothingIsWritten()
    {
        await using DesktopSessionHarness harness = new();
        ProbeHarness fakes = new();
        Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync();
        Task<int> helper = harness.RunEndpoint(fakes, DesktopProbeFixture.Nonce);
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(0, await helper.WaitAsync(DesktopProbeFixture.Wait), "the helper wrote its frame (exit 0)");

        Assert.AreEqual(Outcome.ResponseAccepted, session.Outcome);
        Assert.IsNotNull(session.Probe);
        Assert.AreEqual(QualificationProbeTokenVerdict.Accepted, session.Probe.Verdict);
        Assert.AreEqual(QualificationProbeObservation.Observed, session.Probe.Observation);
        Assert.AreEqual("2.7.5", session.Probe.ObservedClientVersion);
        Assert.IsTrue(session.PipeConnected && session.DesktopPeerVerified && session.HelperFileIdEqual && session.ResponseFrameReceived &&
            session.PipeEofObserved && session.HelperExitObserved && session.BindingHeldUntilExit);
        Assert.IsFalse(session.TerminationFailed);
        Assert.AreEqual(0, session.HelperExitCode);
        Assert.AreEqual(0, harness.Terminations);
        Assert.AreEqual(0, harness.Channel.BytesWritten, "nothing is ever written to the pipe");
        Assert.AreEqual(1, harness.Disposals, "ownership completes once, after exit and seal");
    }

    [TestMethod]
    public async Task TheRealS3EndpointsTokenRefusalIsAnAcceptedResponseCarryingTheVerdict()
    {
        await using DesktopSessionHarness harness = new();
        ProbeHarness fakes = new();
        fakes.Port.Verdict = PCSteward.Provider.Chocolatey.Native.TokenVerdict.EvaluationPrivilegeNotAllowed;
        fakes.Port.Begin = () => null;
        Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync();
        Task<int> helper = harness.RunEndpoint(fakes, DesktopProbeFixture.Nonce);
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        await helper.WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.ResponseAccepted, session.Outcome);
        Assert.AreEqual(QualificationProbeTokenVerdict.EvaluationPrivilegeNotAllowed, session.Probe!.Verdict);
        Assert.AreEqual(QualificationProbeObservation.NotRun, session.Probe.Observation);
    }

    [TestMethod]
    public async Task AFrameForAnotherNonceIsRejectedAndEveryResponseFieldIsDiscarded()
    {
        await using DesktopSessionHarness harness = new() { ExpectedNonce = new string('E', 64) };
        Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync();
        Task<int> helper = harness.RunEndpoint(new ProbeHarness(), DesktopProbeFixture.Nonce);
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        await helper.WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.ResponseRejected, session.Outcome);
        Assert.IsNull(session.Probe);
        Assert.IsTrue(session.ResponseFrameReceived && session.PipeEofObserved && session.HelperExitObserved);
    }

    [TestMethod]
    [DataRow("trailing-byte")]
    [DataRow("second-frame")]
    [DataRow("closed-without-response")]
    [DataRow("oversized-length")]
    [DataRow("truncated-frame")]
    [DataRow("update-frame")]
    public async Task AnythingButOneValidFrameThenEndOfFileIsNoValidResponseOrRejected(string behaviour)
    {
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(DesktopProbeFixture.Nonce, DesktopProbeFixture.Passing());
        await using DesktopSessionHarness harness = new();
        Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync();
        Task helper = harness.RunHelper(async client =>
        {
            try
            {
                switch (behaviour)
                {
                    case "trailing-byte":
                        await PipeMessageFraming.WriteAsync(client, frame, CancellationToken.None);
                        await client.WriteAsync(new byte[] { 0 });
                        break;
                    case "second-frame":
                        await PipeMessageFraming.WriteAsync(client, frame, CancellationToken.None);
                        await PipeMessageFraming.WriteAsync(client, frame, CancellationToken.None);
                        break;
                    case "oversized-length":
                        byte[] header = new byte[4];
                        BinaryPrimitives.WriteInt32LittleEndian(header, ElevatedHelperProtocol.MaximumMessageBytes + 1);
                        await client.WriteAsync(header);
                        break;
                    case "truncated-frame":
                        byte[] partial = new byte[4];
                        BinaryPrimitives.WriteInt32LittleEndian(partial, frame.Length);
                        await client.WriteAsync(partial);
                        await client.WriteAsync(frame.AsMemory(0, frame.Length / 2));
                        break;
                    case "update-frame":
                        // A well-formed update response is not a probe response (Q1 exact property set).
                        ReviewedUpdateOperation operation = Protocol2InterpreterFixture.Operation();
                        await PipeMessageFraming.WriteAsync(client, Protocol2InterpreterFixture.Frame(operation, Protocol2InterpreterFixture.Started(),
                            Protocol2InterpreterFixture.Matched()), CancellationToken.None);
                        break;
                }
            }
            catch (IOException)
            {
                // The desktop may already have closed its end after the surplus bytes.
            }
        });
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        await harness.DisposeServerAsync();
        await helper.WaitAsync(DesktopProbeFixture.Wait);

        Assert.AreEqual(behaviour == "update-frame" ? Outcome.ResponseRejected : Outcome.NoValidResponse, session.Outcome, behaviour);
        Assert.IsNull(session.Probe);
        Assert.AreEqual(0, harness.Channel.BytesWritten);
        if (behaviour != "update-frame") Assert.AreEqual(1, harness.Terminations, "a failure path terminates the helper");
    }

    [TestMethod]
    public async Task ThePeerChecksRefuseAsPeerNotVerifiedAndReadNothing()
    {
        foreach (bool identityPasses in new[] { false, true })
        {
            await using DesktopSessionHarness harness = new();
            harness.Channel.Identity = () => identityPasses;
            harness.Channel.FileId = () => false;
            bool helperRead = false;
            Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync();
            Task helper = harness.RunHelper(async client =>
            {
                byte[] one = new byte[1];
                helperRead = await client.ReadAsync(one) > 0;
            });
            ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
            await harness.DisposeServerAsync();
            await helper.WaitAsync(DesktopProbeFixture.Wait);

            Assert.AreEqual(Outcome.PeerNotVerified, session.Outcome);
            Assert.AreEqual(identityPasses, session.DesktopPeerVerified);
            Assert.IsFalse(session.HelperFileIdEqual);
            Assert.AreEqual(identityPasses ? 1 : 0, harness.Channel.FileIdCalls, "the file identity is checked only after the peer identity");
            Assert.IsFalse(session.ResponseFrameReceived);
            Assert.IsFalse(helperRead, "nothing is written to the pipe");
            Assert.AreEqual(1, harness.Terminations);
        }
    }

    [TestMethod]
    public async Task HelperExitBeforeConnectionIsHelperNotConnectedAndNeverVerifies()
    {
        await using DesktopSessionHarness harness = new() { HelperExitCode = 20 };
        harness.Exit.TrySetResult();
        ReviewedHelperProbeSessionResult session = await harness.RunAsync().WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.HelperNotConnected, session.Outcome);
        Assert.IsFalse(session.PipeConnected);
        Assert.AreEqual(0, harness.Channel.IdentityCalls);
        Assert.IsTrue(session.HelperExitObserved);
        Assert.AreEqual(20, session.HelperExitCode, "an old helper's protocol-1 refusal (§6) is visible as its exit code");
    }

    [TestMethod]
    public async Task AConnectionTimeoutTerminatesTheHelper()
    {
        await using DesktopSessionHarness harness = new();
        ReviewedHelperProbeSessionResult session = await harness.RunAsync(DesktopProbeFixture.Fast with { Connection = TimeSpan.FromMilliseconds(200) })
            .WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.HelperNotConnected, session.Outcome);
        Assert.AreEqual(1, harness.Terminations);
        Assert.IsTrue(session.HelperExitObserved, "termination ended the helper");
    }

    [TestMethod]
    public async Task TheResponseBudgetLapseTerminatesAndIsNoValidResponse()
    {
        await using DesktopSessionHarness harness = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync(DesktopProbeFixture.Fast with { Execution = TimeSpan.FromMilliseconds(300) });
        Task helper = harness.RunHelper(_ => release.Task, exitAfter: false);
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        release.SetResult();
        await helper.WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.NoValidResponse, session.Outcome);
        Assert.IsTrue(session.PipeConnected && session.DesktopPeerVerified && session.HelperFileIdEqual);
        Assert.IsFalse(session.ResponseFrameReceived);
        Assert.AreEqual(1, harness.Terminations);
    }

    [TestMethod]
    public async Task AnAcceptedFrameWithoutObservedExitTerminatesAndRecordsTheMissingExitAndReleaseState()
    {
        await using DesktopSessionHarness harness = new() { TerminateSucceeds = false };
        byte[] frame = QualificationProbeWireCodec.CreateProbeResponse(DesktopProbeFixture.Nonce, DesktopProbeFixture.Passing());
        TaskCompletionSource exitNever = new();
        // The harness's terminate stand-in completes the exit task; use an exit that is never observed within the budget instead.
        Task<ReviewedHelperProbeSessionResult> running = ReviewedHelperProbeSession.RunAsync(harness.Channel, new HelperProcessWork(exitNever.Task, () => { }),
            () => false, () => 0, () => true, DesktopProbeFixture.Nonce, DesktopProbeFixture.Fast with { Exit = TimeSpan.FromMilliseconds(200) },
            QualificationProbeContract.Compiled, CancellationToken.None);
        Task helper = harness.RunHelper(client => PipeMessageFraming.WriteAsync(client, frame, CancellationToken.None), exitAfter: false);
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        await helper.WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.ResponseAccepted, session.Outcome, "the response stands; E3 reads the missing exit");
        Assert.IsFalse(session.HelperExitObserved);
        Assert.IsTrue(session.TerminationFailed);
        Assert.IsFalse(session.BindingHeldUntilExit, "no exit observed, so the binding state is not claimed");
        Assert.IsNull(session.HelperExitCode);
    }

    [TestMethod]
    public async Task ABindingReleasedBeforeExitIsRecorded()
    {
        await using DesktopSessionHarness harness = new() { Holding = false };
        Task<ReviewedHelperProbeSessionResult> running = harness.RunAsync();
        Task<int> helper = harness.RunEndpoint(new ProbeHarness(), DesktopProbeFixture.Nonce);
        ReviewedHelperProbeSessionResult session = await running.WaitAsync(DesktopProbeFixture.Wait);
        await helper.WaitAsync(DesktopProbeFixture.Wait);
        Assert.AreEqual(Outcome.ResponseAccepted, session.Outcome);
        Assert.IsTrue(session.HelperExitObserved);
        Assert.IsFalse(session.BindingHeldUntilExit);
    }

    private static Task<QualificationProbeRun> Probe(ProbeTransportFakes fakes) =>
        new ReviewedHelperProbeTransport(fakes.Dependencies()).ProbeAsync(CancellationToken.None).WaitAsync(DesktopProbeFixture.Wait);

    private static void AssertRefusedBeforeHold(QualificationProbeRun run, Outcome outcome)
    {
        Assert.AreEqual(outcome, run.Outcome);
        CollectionAssert.AreEqual(DesktopProbeFixture.G1(QualificationProbeCapabilityStatus.NotEvaluated).ToArray(), run.CapabilityStatus.ToArray());
        Assert.IsNull(run.Pins);
        Assert.IsNull(run.Probe);
        Assert.IsFalse(run.Transport.FinalLaunchTrust || run.Transport.HelperProcessCreated || run.Transport.PipeConnected);
        Assert.IsNull(QualificationProbeResultDocument.Violation(run, DesktopProbeFixture.Started, DesktopProbeFixture.Started, QualificationProbeContract.Compiled));
    }
}
