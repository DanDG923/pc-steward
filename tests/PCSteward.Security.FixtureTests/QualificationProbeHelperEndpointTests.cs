using System.Diagnostics;
using System.IO.Pipes;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.ProbeFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §3 and §7 H3, H7, H10 (slice S3): the probe entry over a
/// real pipe pair with a fake probe port, item, peer, capability check and clock. Portable. The response is read back with the
/// S2 codec (<see cref="QualificationProbeWireCodec.TryReadProbeResponse(ReadOnlySpan{byte}, string, out QualificationProbeFacts?)"/>).
/// </summary>
[TestClass]
public sealed class QualificationProbeHelperEndpointTests
{
    private static readonly string[] DisposalOrder = ["protect", "observe", "binding-disposed", "item-disposed", "write:True:True"];

    private static readonly string Token = DeploymentCapabilityPolicy.OperationToken(DeploymentCapabilityPolicy.CapabilityOperationAllowlist[0]);

    // ---- Rows (Q13) ----

    [TestMethod]
    public async Task AnObservedClientIsWrittenOnceWithTheObservedRowAndTheItemIsDisposedFirst()
    {
        ProbeHarness harness = new();
        ProbeRun run = await harness.RunAsync();

        QualificationProbeFacts facts = run.Facts;
        Assert.AreEqual(0, run.ExitCode);
        Assert.AreEqual(QualificationProbeTokenVerdict.Accepted, facts.Verdict);
        Assert.AreEqual(QualificationProbeObservation.Observed, facts.Observation);
        Assert.AreEqual(F.NewestListed, facts.ObservedClientVersion);
        Assert.IsTrue(facts.FingerprintPresent);
        Assert.AreEqual(64, facts.FingerprintLength);
        Assert.AreEqual(1234, facts.NodesEvaluated);
        Assert.AreEqual(12, facts.AncestorsEvaluated);
        Assert.AreEqual(QualificationProbeWireCodec.ObservedDiagnostic, facts.Diagnostic);
        CollectionAssert.AreEqual(new[] { new QualificationProbeCapabilityEntry(Token, QualificationProbeCapabilityStatus.Supported) }, facts.G1Status.ToArray());
        // A2 with the host SID; A3 with the item's requester check as the protection principal and the held peer image.
        Assert.IsTrue(string.Equals(F.Sid, harness.Port.ExpectedSid, StringComparison.Ordinal), "A2 uses the host SID");
        Assert.AreEqual(1, harness.Capability.Calls);
        Assert.AreEqual(EndpointFixture.PeerImage, harness.Capability.PeerImagePath);
        Assert.IsTrue(harness.Capability.ProtectionResult);
        Assert.AreEqual(1, harness.Item.ObserveCalls);
        Assert.IsTrue(harness.Item.Disposed);
        // B′ gets remaining = probe budget − elapsed (10 minutes; the fake clock does not advance).
        Assert.AreEqual(TimeSpan.FromMinutes(10), harness.Item.ObserveBound);
    }

    [TestMethod]
    public async Task H3TheBindingAndTheItemAreDisposedBeforeTheResponseBytesAreCreatedOrWritten()
    {
        ProbeHarness harness = new();
        harness.WrapHelperPipe = pipe => new RecordingPipe(pipe, () => harness.Events.Add("write:" + harness.Item.BindingDisposed + ":" + harness.Item.Disposed));
        ProbeRun run = await harness.RunAsync();

        Assert.AreEqual(QualificationProbeObservation.Observed, run.Facts.Observation);
        CollectionAssert.AreEqual(DisposalOrder, harness.Events.Snapshot());
    }

    [TestMethod]
    public async Task H3AnObservationWhoseBindingWasNotDisposedYieldsNoResponse()
    {
        // The native item reports a binding that is not disposed (or any internal fault) as Faulted; nothing is written.
        ProbeHarness harness = new();
        harness.Item.Observe = (_, _) => new ChocolateyProbeValues.Faulted();
        ProbeRun run = await harness.RunAsync();
        run.AssertNoResponse();
        Assert.AreEqual(24, run.ExitCode);
        Assert.IsTrue(harness.Item.Disposed);
    }

    [TestMethod]
    [DataRow("PeerNotLimited")]
    [DataRow("NoLinkedToken")]
    [DataRow("EvaluationPrivilegeNotAllowed")]
    [DataRow("PeerExited")]
    [DataRow("EvaluationNotIdentificationLevel")]
    public async Task ARefusedTokenIsTheTokenRowWithEveryCapabilityNotEvaluated(string name)
    {
        TokenVerdict verdict = Enum.Parse<TokenVerdict>(name);
        ProbeHarness harness = new();
        harness.Port.Verdict = verdict;
        harness.Port.Begin = () => null;
        ProbeRun run = await harness.RunAsync();

        QualificationProbeFacts facts = run.Facts;
        Assert.AreEqual(verdict.ToString(), facts.Verdict.ToString());
        Assert.AreEqual(QualificationProbeObservation.NotRun, facts.Observation);
        Assert.IsTrue(facts.G1Status.All(entry => entry.Status == QualificationProbeCapabilityStatus.NotEvaluated));
        CollectionAssert.AreEqual(QualificationProbeContract.Compiled.G1Keys.ToArray(), facts.G1Status.Select(entry => entry.Token).ToArray());
        Assert.AreEqual(QualificationProbeWireCodec.TokenRefusedDiagnostic, facts.Diagnostic);
        Assert.AreEqual(0, harness.Capability.Calls, "A3 never runs");
        Assert.AreEqual(0, harness.Item.ObserveCalls);
    }

    [TestMethod]
    public async Task AnInconsistentOrThrowingBeginProbeWritesNothing()
    {
        ProbeHarness inconsistent = new();
        inconsistent.Port.Verdict = TokenVerdict.PeerSidMismatch; // an item with a refused verdict
        ProbeRun run = await inconsistent.RunAsync();
        run.AssertNoResponse();
        Assert.IsTrue(inconsistent.Item.Disposed, "an item returned with a refused verdict is still disposed");

        ProbeHarness missing = new();
        missing.Port.Begin = () => null; // Accepted without an item
        (await missing.RunAsync()).AssertNoResponse();

        ProbeHarness throwing = new();
        throwing.Port.Throw = true;
        (await throwing.RunAsync()).AssertNoResponse();
    }

    [TestMethod]
    [DataRow("Absent")]
    [DataRow("Incomplete")]
    [DataRow("Unreadable")]
    [DataRow("Mismatch")]
    public async Task AnUnsupportedCapabilityIsTheCapabilityRowAndNoObservationRuns(string name)
    {
        HelperProtocolCapabilityStatus status = Enum.Parse<HelperProtocolCapabilityStatus>(name);
        QualificationProbeCapabilityStatus expected = Enum.Parse<QualificationProbeCapabilityStatus>(name);
        ProbeHarness harness = new();
        harness.Capability.Status = status;
        QualificationProbeFacts facts = (await harness.RunAsync()).Facts;
        Assert.AreEqual(QualificationProbeObservation.NotRun, facts.Observation);
        Assert.AreEqual(expected, facts.G1Status.Single().Status);
        Assert.AreEqual(QualificationProbeWireCodec.CapabilityRefusedDiagnostic, facts.Diagnostic);
        Assert.AreEqual(0, harness.Item.ObserveCalls);
    }

    [TestMethod]
    public async Task AnUnprotectedLayoutAndAThrowingCheckAreNotSupported()
    {
        ProbeHarness unprotected = new();
        unprotected.Item.Protect = (_, _) => false;
        Assert.AreEqual(QualificationProbeCapabilityStatus.Mismatch, (await unprotected.RunAsync()).Facts.G1Status.Single().Status);

        ProbeHarness throwing = new();
        throwing.Capability.Throw = true;
        QualificationProbeFacts facts = (await throwing.RunAsync()).Facts;
        Assert.AreEqual(QualificationProbeCapabilityStatus.Unreadable, facts.G1Status.Single().Status, "an exception is never Supported");
        Assert.AreEqual(0, throwing.Item.ObserveCalls);
    }

    [TestMethod]
    public async Task AnUnreadablePeerImageWritesNothing()
    {
        ProbeHarness harness = new();
        harness.Peer.IdentityReadable = false;
        (await harness.RunAsync()).AssertNoResponse();
        Assert.AreEqual(0, harness.Capability.Calls);
    }

    [TestMethod]
    [DataRow("MachineInputsUnsupported", 1, "StepRule", "Granted")]
    [DataRow("MachineInputsUnsupported", 4, "StepRule", "Unreadable")]
    [DataRow("MachineInputsUnsupported", 5, "StepRule", "Granted")]
    [DataRow("MachineInputsUnsupported", 6, "StepRule", "Unreadable")]
    [DataRow("MachineInputsUnsupported", 2, "StepRule", "Other")]
    [DataRow("MachineInputsUnsupported", 9, "StepRule", "Other")]
    [DataRow("MachineInputsUnsupported", 7, "SetupAccessGuard", "Other")]
    [DataRow("MachineInputsUnsupported", 7, "ReleaseLeftover", "Other")]
    [DataRow("SourceNotCommunity", 8, "StepRule", "Other")]
    public async Task ARefusedObservationIsTheRefusalRowWithTheStepCheckAccessAndDetail(string reasonName, int step, string checkName, string accessName)
    {
        ChocolateyStageRefusal reason = Enum.Parse<ChocolateyStageRefusal>(reasonName);
        ChocolateyProbeFailedCheck check = Enum.Parse<ChocolateyProbeFailedCheck>(checkName);
        ChocolateyProbeFailedAccess access = Enum.Parse<ChocolateyProbeFailedAccess>(accessName);
        ProbeHarness harness = new();
        harness.Item.Observe = (_, _) => new ChocolateyProbeValues.Refused(reason, step, check, access, null, 77, 5);
        QualificationProbeFacts facts = (await harness.RunAsync()).Facts;

        Assert.AreEqual(QualificationProbeObservation.Refused, facts.Observation);
        Assert.AreEqual(reason.ToString(), facts.RefusalReason.ToString());
        Assert.AreEqual(step, facts.FailedStep);
        Assert.AreEqual(check.ToString(), facts.FailedCheck.ToString());
        Assert.AreEqual(access.ToString(), facts.FailedAccess.ToString());
        QualificationProbeRefusalDetail detail = check switch
        {
            ChocolateyProbeFailedCheck.SetupAccessGuard => QualificationProbeRefusalDetail.SetupAccessOverride,
            ChocolateyProbeFailedCheck.ReleaseLeftover => QualificationProbeRefusalDetail.UnlistedReleaseLeftover,
            _ => QualificationProbeRefusalDetail.None,
        };
        Assert.AreEqual(detail, facts.RefusalDetail);
        Assert.AreEqual(77, facts.NodesEvaluated);
        Assert.AreEqual(5, facts.AncestorsEvaluated);
        Assert.IsNull(facts.ObservedClientVersion);
        Assert.IsFalse(facts.FingerprintPresent);
        Assert.AreEqual(QualificationProbeWireCodec.ObservationRefusedDiagnostic, facts.Diagnostic);
    }

    [TestMethod]
    public async Task ARefusalTheCodecRejectsWritesNothing()
    {
        // Q15: an access verdict at a step that evaluates no requester access; Q8: InstalledMismatch with the one-entry list (Q10).
        ProbeHarness access = new();
        access.Item.Observe = (_, _) => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, 2,
            ChocolateyProbeFailedCheck.StepRule, ChocolateyProbeFailedAccess.Granted, null, 1, 1);
        (await access.RunAsync()).AssertNoResponse();

        ProbeHarness mismatch = new();
        mismatch.Item.Observe = (_, _) => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.InstalledMismatch, 8,
            ChocolateyProbeFailedCheck.InstalledVersion, ChocolateyProbeFailedAccess.Other, "2.7.4", 1, 1);
        (await mismatch.RunAsync()).AssertNoResponse();

        ProbeHarness version = new();
        version.Item.Observe = (_, _) => new ChocolateyProbeValues.Observed("2.7.4", 64, 1, 1);
        (await version.RunAsync()).AssertNoResponse();

        ProbeHarness counts = new();
        counts.Item.Observe = (_, _) => new ChocolateyProbeValues.Observed(F.NewestListed, 64, 0, 1);
        (await counts.RunAsync()).AssertNoResponse();
    }

    [TestMethod]
    public async Task ATimedOutObservationIsTheTimedOutRowWithTheReachedCounts()
    {
        ProbeHarness harness = new();
        harness.Item.Observe = (_, _) => new ChocolateyProbeValues.TimedOut(1_048_576, 3);
        QualificationProbeFacts facts = (await harness.RunAsync()).Facts;
        Assert.AreEqual(QualificationProbeObservation.TimedOut, facts.Observation);
        Assert.AreEqual(1_048_576, facts.NodesEvaluated, "a saturated count is written as the maximum");
        Assert.AreEqual(3, facts.AncestorsEvaluated);
        Assert.IsNull(facts.FailedStep);
        Assert.AreEqual(QualificationProbeWireCodec.ObservationTimedOutDiagnostic, facts.Diagnostic);
    }

    [TestMethod]
    public async Task ACancelledOrFaultedObservationIsNeverWritten()
    {
        ProbeHarness cancelled = new();
        cancelled.Item.Observe = (_, _) => new ChocolateyProbeValues.Cancelled();
        (await cancelled.RunAsync()).AssertNoResponse();

        ProbeHarness throwing = new();
        throwing.Item.Observe = (_, _) => throw new InvalidOperationException("synthetic");
        (await throwing.RunAsync()).AssertNoResponse();
        Assert.IsTrue(throwing.Item.Disposed);
    }

    [TestMethod]
    public async Task ABudgetSpentAfterA3IsTimedOutWithoutAnyRead()
    {
        ProbeHarness harness = new();
        harness.Capability.BeforeProtection = () => harness.Clock.Advance(TimeSpan.FromMinutes(10));
        QualificationProbeFacts facts = (await harness.RunAsync()).Facts;
        Assert.AreEqual(QualificationProbeObservation.TimedOut, facts.Observation);
        Assert.AreEqual(0, facts.NodesEvaluated);
        Assert.AreEqual(0, facts.AncestorsEvaluated);
        Assert.AreEqual(0, harness.Item.ObserveCalls, "no read once the budget is spent");
    }

    // ---- H7: inbound byte, end of file, the A3 cap ----

    [TestMethod]
    public async Task H7AnInboundByteDuringA3StopsTheRunWithNoResponseAndExit27()
    {
        ProbeHarness harness = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        harness.Capability.BeforeProtection = () =>
        {
            entered.TrySetResult();
            release.Wait(F.Wait);
        };
        ProbeRun run;
        try
        {
            run = await harness.RunAsync(async server =>
            {
                await entered.Task.WaitAsync(F.Wait);
                await server.WriteAsync(new byte[] { 0x7B });
                await server.FlushAsync();
            });
        }
        finally { release.Set(); }
        run.AssertNoResponse(ReviewedHelperProbeExit.InboundByte);
        Assert.AreEqual(ReviewedHelperProbeBootstrap.InboundByteReceived, run.ExitCode);
        Assert.AreEqual(27, run.ExitCode);
        Assert.AreEqual(0, harness.Item.ObserveCalls);
    }

    [TestMethod]
    public async Task H7AnUpdateRequestFrameSentToAProbeHelperIsNeverParsedAndEndsAtExit27()
    {
        ProbeHarness harness = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Item.Observe = (_, token) =>
        {
            entered.TrySetResult();
            token.WaitHandle.WaitOne(F.Wait);
            return new ChocolateyProbeValues.Cancelled();
        };
        ProbeRun run = await harness.RunAsync(async server =>
        {
            await entered.Task.WaitAsync(F.Wait);
            await PipeMessageFraming.WriteAsync(server, EndpointFixture.Request(EndpointFixture.Operation(), EndpointFixture.Start), CancellationToken.None);
        });
        run.AssertNoResponse(ReviewedHelperProbeExit.InboundByte);
        Assert.AreEqual(27, run.ExitCode);
        Assert.IsTrue(harness.Item.Disposed);
    }

    [TestMethod]
    public async Task H7EndOfFileDuringA3EndsWithNoResponseAndExit24()
    {
        ProbeHarness harness = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        harness.Capability.BeforeProtection = () =>
        {
            entered.TrySetResult();
            release.Wait(F.Wait);
        };
        ProbeRun run;
        try
        {
            run = await harness.RunAsync(async server =>
            {
                await entered.Task.WaitAsync(F.Wait);
                server.Disconnect();
            });
        }
        finally { release.Set(); }
        Assert.AreEqual(ReviewedHelperProbeExit.NoResponse, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreEqual(24, run.ExitCode);
        Assert.AreEqual(0, harness.Item.ObserveCalls);
        // The abandoned evaluation's later protection call does not reach the item.
        await harness.Capability.Completed.Task.WaitAsync(F.Wait);
        Assert.IsFalse(harness.Capability.ProtectionResult);
        Assert.AreEqual(0, harness.Item.ProtectCalls);
    }

    [TestMethod]
    public async Task H7EndOfFileDuringBPrimeCancelsTheObservationAndEndsWithNoResponseAndExit24()
    {
        ProbeHarness harness = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // EOF abandons the observation (never awaited, "b-interrupted"), so the run can end before the observation records
        // what it saw: the observation completes this source and the test awaits it rather than reading a shared flag.
        TaskCompletionSource<bool> sawCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Item.Observe = (_, token) =>
        {
            entered.TrySetResult();
            sawCancellation.TrySetResult(token.WaitHandle.WaitOne(F.Wait));
            return new ChocolateyProbeValues.Cancelled();
        };
        ProbeRun run = await harness.RunAsync(async server =>
        {
            await entered.Task.WaitAsync(F.Wait);
            server.Disconnect();
        });
        Assert.AreEqual(ReviewedHelperProbeExit.NoResponse, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreEqual(24, run.ExitCode);
        Assert.IsTrue(await sawCancellation.Task.WaitAsync(F.Wait), "EOF cancels the observation's token");
    }

    [TestMethod]
    public async Task H7AProtectionThatBlocksPastTheBudgetIsTheCapWithNoResponseAndALaterCallDoesNotReachTheItem()
    {
        ProbeHarness harness = new() { Budgets = F.Short(probeMilliseconds: 300) };
        using ManualResetEventSlim release = new();
        harness.Capability.BeforeProtection = () => release.Wait(F.Wait);
        Stopwatch elapsed = Stopwatch.StartNew();
        ProbeRun run;
        try { run = await harness.RunAsync(); }
        finally { release.Set(); }
        elapsed.Stop();

        run.AssertNoResponse();
        Assert.AreEqual(24, run.ExitCode);
        StringAssert.Contains(run.Result.Diagnostic, "a3-unresponsive");
        Assert.IsLessThan(TimeSpan.FromSeconds(20), elapsed.Elapsed, "the cap ends A3 in real time");
        await harness.Capability.Completed.Task.WaitAsync(F.Wait);
        Assert.IsFalse(harness.Capability.ProtectionResult, "after the cap the protection check refuses");
        Assert.AreEqual(0, harness.Item.ProtectCalls, "and never reaches the item");
        Assert.AreEqual(0, harness.Item.ObserveCalls);
        Assert.IsTrue(harness.Item.Disposed);
    }

    [TestMethod]
    public async Task H7ABudgetSpentBeforeA3StartsCountsAsTheCap()
    {
        ProbeHarness harness = new() { PreElapsed = TimeSpan.FromMinutes(10) };
        ProbeRun run = await harness.RunAsync();
        run.AssertNoResponse();
        Assert.AreEqual(0, harness.Capability.Calls, "the evaluation is not started");
    }

    [TestMethod]
    public async Task H7ARunInterruptedBeforeAnA3TokenStartsBeginsNoEvaluation()
    {
        // Review (A3 abandonment race): the token is checked before Task.Run, so an interrupted run never starts an evaluation
        // that could reach the item after abandonment.
        using CancellationTokenSource run = new();
        ProbeHarness harness = new() { Cancellation = run.Token };
        harness.Peer.OnIdentity = run.Cancel; // between A2's interruption check and the first A3 token
        ProbeRun result = await harness.RunAsync();

        result.AssertNoResponse();
        Assert.AreEqual(24, result.ExitCode);
        StringAssert.Contains(result.Result.Diagnostic, "a3-interrupted");
        // Under the old order the evaluation was queued anyway; give it time to run before asserting it never started.
        Task started = await Task.WhenAny(harness.Capability.Completed.Task, Task.Delay(TimeSpan.FromMilliseconds(500)));
        Assert.AreNotSame(harness.Capability.Completed.Task, started, "the evaluation was started after the run was interrupted");
        Assert.AreEqual(0, harness.Capability.Calls);
        Assert.AreEqual(0, harness.Item.ProtectCalls);
        Assert.IsTrue(harness.Item.Disposed);
    }

    [TestMethod]
    public async Task H7AnInterruptionDuringAnA3EvaluationIsAbandonmentAtOnceAndTheProtectionCallDoesNotReachTheItem()
    {
        // Review (A3 abandonment race): the protection check reads the run's token itself, so a call made after the interruption
        // but before the entry's wait has observed it refuses without reaching the item.
        using CancellationTokenSource run = new();
        ProbeHarness harness = new() { Cancellation = run.Token };
        harness.Capability.BeforeProtection = run.Cancel; // the interruption lands on the evaluating thread, just before the call
        ProbeRun result = await harness.RunAsync();

        result.AssertNoResponse();
        Assert.AreEqual(24, result.ExitCode);
        StringAssert.Contains(result.Result.Diagnostic, "a3-interrupted");
        await harness.Capability.Completed.Task.WaitAsync(F.Wait);
        Assert.IsFalse(harness.Capability.ProtectionResult, "after the interruption the protection check refuses");
        Assert.AreEqual(0, harness.Item.ProtectCalls, "and never reaches the item");
        Assert.AreEqual(0, harness.Item.ObserveCalls);
    }

    // ---- H10: in-flight disposal and the write window ----

    [TestMethod]
    public async Task H10TheEntryDisposesTheItemWhileAnAbandonedProtectionCallRuns()
    {
        ProbeHarness harness = new() { Budgets = F.Short(probeMilliseconds: 300) };
        using ManualResetEventSlim release = new();
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Item.Protect = (_, _) =>
        {
            entered.TrySetResult();
            release.Wait(F.Wait);
            return true;
        };
        ProbeRun run;
        try { run = await harness.RunAsync(); }
        finally { release.Set(); }
        run.AssertNoResponse();
        Assert.IsTrue(harness.Item.DisposedWhileInFlight, "the item is disposed while the abandoned call is still running");
    }

    [TestMethod]
    public async Task H10AnObservationBlockedPastTheOuterCapIsAbandonedAndTheItemDisposedWhileItRuns()
    {
        ProbeHarness harness = new() { Budgets = F.Short(probeMilliseconds: 300, exitMarginMilliseconds: 200) };
        using ManualResetEventSlim release = new();
        harness.Item.Observe = (_, _) =>
        {
            release.Wait(F.Wait, CancellationToken.None); // ignores its token: only the outer cap ends the stage
            return F.Observed();
        };
        Stopwatch elapsed = Stopwatch.StartNew();
        ProbeRun run;
        try { run = await harness.RunAsync(); }
        finally { release.Set(); }
        run.AssertNoResponse();
        StringAssert.Contains(run.Result.Diagnostic, "b-unresponsive");
        Assert.IsLessThan(TimeSpan.FromSeconds(20), elapsed.Elapsed);
        Assert.IsTrue(harness.Item.DisposedWhileInFlight);
    }

    [TestMethod]
    public async Task H10TheNativeItemToleratesDisposeWhileTheObservationRunsAndStartsNothingAfterward()
    {
        // The native probe item over its scripted seam: the entry abandons the observation at the outer cap and disposes the item.
        using ManualResetEventSlim release = new();
        TaskCompletionSource<bool> observedCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int protectCalls = 0, observeCalls = 0, releases = 0;
        ChocolateyProbeItemTestSeam seam = ChocolateyProbeItemTestSeam.CreateForTests(
            (_, _) =>
            {
                Interlocked.Increment(ref protectCalls);
                return true;
            },
            (_, token) =>
            {
                Interlocked.Increment(ref observeCalls);
                release.Wait(F.Wait, CancellationToken.None);
                observedCancellation.TrySetResult(token.IsCancellationRequested);
                return F.Observed();
            },
            () => Interlocked.Increment(ref releases));
        NativeChocolateyProbeItem item = NativeChocolateyProbeItem.CreateForTests(seam);
        ProbeHarness harness = new() { Budgets = F.Short(probeMilliseconds: 300, exitMarginMilliseconds: 200), PortOverride = new SingleItemPort(item) };
        ProbeRun run;
        try
        {
            run = await harness.RunAsync();
            Assert.AreEqual(0, Volatile.Read(ref releases), "the token is not released while the observation is in flight");
        }
        finally { release.Set(); }
        run.AssertNoResponse();
        Assert.IsTrue(await observedCancellation.Task.WaitAsync(F.Wait), "Dispose cancels the in-flight work through the lifetime token");
        await WaitUntilAsync(() => Volatile.Read(ref releases) == 1);
        // Nothing new is admitted after Dispose: no G1 check and no second observation reach the seam.
        Assert.IsFalse(item.RequesterCannotModify(@"C:\Program Files\PC Steward", []));
        Assert.IsInstanceOfType<ChocolateyProbeValues.Faulted>(item.ObserveClient(TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.AreEqual(1, protectCalls);
        Assert.AreEqual(1, observeCalls);
        item.Dispose();
        Assert.AreEqual(1, releases, "released once");
    }

    [TestMethod]
    public async Task H10AWriteThatNeverCompletesEndsAtExit26WithinTheWriteWindow()
    {
        ProbeHarness harness = new()
        {
            Budgets = new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)),
            WrapHelperPipe = pipe => new StubbornPipe(pipe, ignoreReadCancellation: false, neverCompleteWrites: true),
        };
        Stopwatch elapsed = Stopwatch.StartNew();
        ProbeRun run = await harness.RunAsync();
        elapsed.Stop();
        Assert.AreEqual(ReviewedHelperProbeExit.ResponseWriteFailed, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreEqual(26, run.ExitCode);
        Assert.IsLessThan(TimeSpan.FromSeconds(20), elapsed.Elapsed, "the window bounds a write that does not observe its token");
    }

    [TestMethod]
    public async Task H10AMonitorThatDoesNotStopSpendsTheSharedWindowAndNothingIsWritten()
    {
        ProbeHarness harness = new()
        {
            Budgets = new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)),
            WrapHelperPipe = pipe => new StubbornPipe(pipe, ignoreReadCancellation: true, neverCompleteWrites: false),
        };
        ProbeRun run = await harness.RunAsync();
        Assert.AreEqual(ReviewedHelperProbeExit.ResponseWriteFailed, run.Result.Exit, run.Result.Diagnostic);
        StringAssert.Contains(run.Result.Diagnostic, "response-window-spent");
        Assert.IsNull(run.Frame);
    }

    [TestMethod]
    public async Task AnInvalidBootstrapNonceOrSidWritesNothingAndBeginsNothing()
    {
        ProbeHarness harness = new() { ExpectedSid = string.Empty };
        (await harness.RunAsync()).AssertNoResponse();
        Assert.AreEqual(0, harness.Port.BeginCalls);
    }

    [TestMethod]
    public void TheProbeBudgetsMatchTheSpecAndTheUpdateEndpointMargins()
    {
        ReviewedHelperProbeBudgets probe = ReviewedHelperProbeBudgets.Production;
        ReviewedHelperEndpointBudgets update = ReviewedHelperEndpointBudgets.Production;
        Assert.AreEqual(TimeSpan.FromMinutes(10), probe.Probe);
        Assert.AreEqual(update.ExitMargin, probe.ExitMargin);
        Assert.AreEqual(update.ResponseMargin, probe.ResponseMargin);
        Assert.AreEqual(update.ResponseWrite, probe.ResponseWrite);
        // §3: the last write ends at most 10 min 10 s after t0, under the desktop's 11-minute read.
        Assert.IsLessThan(update.Execution, probe.Probe + probe.ExitMargin + TimeSpan.FromTicks(Math.Min(probe.ResponseWrite.Ticks, probe.ResponseMargin.Ticks)));
    }

    [TestMethod]
    public void TheExitCodesAreTheSpecifiedOnes()
    {
        Assert.AreEqual(0, ReviewedHelperProbeBootstrap.ExitCode(ReviewedHelperProbeExit.ResponseWritten));
        Assert.AreEqual(24, ReviewedHelperProbeBootstrap.ExitCode(ReviewedHelperProbeExit.NoResponse));
        Assert.AreEqual(27, ReviewedHelperProbeBootstrap.ExitCode(ReviewedHelperProbeExit.InboundByte));
        Assert.AreEqual(26, ReviewedHelperProbeBootstrap.ExitCode(ReviewedHelperProbeExit.ResponseWriteFailed));
        Assert.AreEqual(21, ReviewedHelperBootstrap.LayoutUnavailable);
        Assert.AreEqual(22, ReviewedHelperBootstrap.ConnectionFailed);
        Assert.AreEqual(23, ReviewedHelperBootstrap.PeerNotVerified);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            if (elapsed.Elapsed > F.Wait) throw new AssertFailedException("condition not reached");
            await Task.Delay(10);
        }
    }

    /// <summary>A port that hands out one prepared probe item with an accepted verdict.</summary>
    private sealed class SingleItemPort(IChocolateyProbeItem item) : IChocolateyProbePort
    {
        public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => throw new AssertFailedException("the probe entry never opens the peer");

        public IChocolateyProbeItem? BeginProbe(IChocolateyPeer peer, string expectedSid, out TokenVerdict verdict)
        {
            verdict = TokenVerdict.Accepted;
            return item;
        }
    }
}
