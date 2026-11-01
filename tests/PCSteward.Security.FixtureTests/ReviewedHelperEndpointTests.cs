using System.Globalization;
using System.Security.Cryptography;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.EndpointFixture;
using Reason = PCSteward.Execution.ReviewedHelperRefusalReason;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §4, §5.2, §5.4 (rev-5 F2) and §8 N4a, fact extension rev 3
/// §3.1, §3.2, §6 and §10 N1–N4: the protocol-2 helper stage machine driven by fake port, item and peer values over a real
/// pipe pair, against the real desktop <see cref="ReviewedHelperExchangeSession"/> and
/// <see cref="ReviewedUpdateProtocol2Interpreter"/>. Every row of §5.2 is covered, and each settled refusal is asserted to
/// settle at the desktop (not Uncertain). Portable: runs on every platform.
/// </summary>
[TestClass]
public sealed class ReviewedHelperEndpointTests
{
    private static readonly string[] ThroughB = ["protect", "observe"];
    private static readonly string[] ThroughC = ["protect", "observe", "query"];
    private static readonly string[] ThroughD = ["protect", "observe", "query", "revalidate"];
    private static readonly string[] AllStages = ["protect", "observe", "query", "revalidate", "upgrade"];
    // ---- A0: uncorrelatable requests get no response ----

    [TestMethod]
    [DataRow("no-frame")]
    [DataRow("closed-without-frame")]
    [DataRow("malformed-json")]
    [DataRow("oversized-length")]
    [DataRow("digest-not-payload-hash")]
    [DataRow("payload-not-an-operation")]
    [DataRow("nonce-mismatch")]
    public async Task A0FailuresSendNoResponse(string scenario)
    {
        EndpointHarness harness = new() { Budgets = ReviewedHelperEndpointBudgets.Production with { FrameRead = TimeSpan.FromMilliseconds(300) } };
        ReviewedUpdateOperation operation = F.Operation();
        string issued = F.Start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        byte[]? frame = scenario switch
        {
            "malformed-json" => "{\"protocolVersion\":\"2\""u8.ToArray(),
            "digest-not-payload-hash" => RawRequest.Json(Convert.ToBase64String(operation.GetCanonicalBytes()), new string('A', 64), F.Nonce, issued),
            "payload-not-an-operation" => F.RequestFor("not an operation"u8.ToArray()),
            "nonce-mismatch" => F.Request(operation, F.Start, new string('E', 64)),
            _ => null,
        };
        (ReviewedHelperEndpointResult result, int written) = await harness.RawAsync(async server =>
        {
            switch (scenario)
            {
                case "closed-without-frame":
                    server.Disconnect();
                    break;
                case "oversized-length":
                    byte[] header = new byte[4];
                    System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, ReviewedUpdateWireCodec.MaximumFrameBytes + 1);
                    await server.WriteAsync(header);
                    break;
                case "no-frame":
                    break;
                default:
                    await PipeMessageFraming.WriteAsync(server, frame!, CancellationToken.None);
                    break;
            }
        });

        Assert.AreEqual(ReviewedHelperEndpointExit.NoResponse, result.Exit, scenario);
        Assert.AreEqual(0, written, "not one byte was written");
        Assert.AreEqual(0, harness.Port.BeginCalls, "nothing after A0 ran");
        Assert.AreEqual(0, harness.Item.Calls.Count);
    }

    [TestMethod]
    [DataRow("nonce-mismatch")]
    [DataRow("mutated-payload-recomputed-digest")]
    public async Task A0FailuresAreUncertainAtTheDesktop(string scenario)
    {
        EndpointHarness harness = new();
        ReviewedUpdateOperation operation = F.Operation();
        ReviewedUpdateOperation other = F.Operation("vlc");
        EndpointRun run = await harness.ExchangeAsync(operation, scenario == "nonce-mismatch"
            ? () => F.Request(operation, harness.Clock.GetUtcNow(), new string('E', 64))
            // A different operation with its own recomputed digest restores at the helper; the desktop cannot correlate the frame.
            : () => F.Request(other, harness.Clock.GetUtcNow()));

        if (scenario == "nonce-mismatch")
        {
            Assert.AreEqual(ReviewedHelperEndpointExit.NoResponse, run.Result.Exit);
            Assert.AreEqual(ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse, run.Exchange!.Outcome);
        }
        Protocol2InterpreterFixture.AssertRejected(run.Interpretation);
    }

    // ---- A1: RequestNotAdmitted, fields 16–18 null, settled at the desktop ----

    [TestMethod]
    [DataRow("stale-request")]
    [DataRow("request-from-future")]
    [DataRow("review-ceiling-lapsed")]
    [DataRow("review-in-future")]
    [DataRow("client-target-shape")]
    [DataRow("peer-sid-mismatch")]
    [DataRow("peer-identity-unreadable")]
    [DataRow("peer-identity-throws")]
    public async Task A1FailuresAreRequestNotAdmitted(string scenario)
    {
        EndpointHarness harness = new();
        ReviewedUpdateOperation operation = F.Operation(scenario == "client-target-shape" ? "chocolatey" : "git");
        Func<byte[]>? request = null;
        switch (scenario)
        {
            case "stale-request":
                request = () => F.Request(operation, harness.Clock.GetUtcNow() - ReviewedHelperEndpoint.RequestLifetime - TimeSpan.FromTicks(1));
                break;
            case "request-from-future":
                request = () => F.Request(operation, harness.Clock.GetUtcNow() + TimeSpan.FromTicks(1));
                break;
            case "review-ceiling-lapsed":
                harness.Clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromTicks(1));
                break;
            case "review-in-future":
                harness.Clock.Advance(TimeSpan.FromMinutes(-1) - TimeSpan.FromTicks(1));
                break;
            case "peer-sid-mismatch":
                harness.Peer.UserSid = "S-1-5-21-11-22-33-1002";
                break;
            case "peer-identity-unreadable":
                harness.Peer.IdentityReadable = false;
                break;
            case "peer-identity-throws":
                harness.Peer.ThrowOnIdentity = true;
                break;
        }

        EndpointRun run = await harness.ExchangeAsync(operation, request);

        AssertNotAdmitted(run, scenario == "client-target-shape" ? null : "a1-");
        Assert.AreEqual(0, harness.Port.BeginCalls, "A2 never ran");
    }

    [TestMethod]
    public async Task A1AcceptsTheExactFreshnessAndCeilingBoundaries()
    {
        EndpointHarness harness = new();
        harness.Clock.Advance(TimeSpan.FromMinutes(29));
        ReviewedUpdateOperation operation = F.Operation();
        // Exactly 2 minutes old, and exactly 30 minutes after review: both still admitted (inclusive limits).
        EndpointRun run = await harness.ExchangeAsync(operation, () => F.Request(operation, harness.Clock.GetUtcNow() - ReviewedHelperEndpoint.RequestLifetime));
        Assert.AreEqual(ReviewedHelperQualification.Matched, run.Response.Reviewed.Qualification, run.Result.Diagnostic);
    }

    // ---- A2 and A3 ----

    [TestMethod]
    [DataRow("begin-null")]
    [DataRow("begin-throws")]
    [DataRow("begin-item-with-refusal")]
    [DataRow("peer-exited")]
    [DataRow("capability-mismatch")]
    [DataRow("capability-throws")]
    [DataRow("requester-can-modify")]
    [DataRow("protection-check-throws")]
    public async Task A2AndA3FailuresAreRequestNotAdmitted(string scenario)
    {
        EndpointHarness harness = new();
        switch (scenario)
        {
            case "begin-null":
                harness.Port.Begin = () => null;
                harness.Port.Refusal = ChocolateyItemRefusal.RequesterTokenRefused;
                break;
            case "begin-throws":
                harness.Port.Begin = () => throw new InvalidOperationException("synthetic");
                break;
            case "begin-item-with-refusal":
                harness.Port.Refusal = ChocolateyItemRefusal.PeerExited;
                break;
            case "peer-exited":
                harness.Peer.Exited = true;
                break;
            case "capability-mismatch":
                harness.Capability.Status = HelperProtocolCapabilityStatus.Mismatch;
                break;
            case "capability-throws":
                harness.Capability.Throw = true;
                break;
            case "requester-can-modify":
                harness.Item.Protect = (_, _) => false;
                break;
            case "protection-check-throws":
                harness.Item.Protect = (_, _) => throw new UnauthorizedAccessException("synthetic");
                break;
        }

        EndpointRun run = await harness.ExchangeAsync();

        AssertNotAdmitted(run, scenario.StartsWith("begin", StringComparison.Ordinal) || scenario == "peer-exited" ? "a2-" : "a3-");
        Assert.IsFalse(harness.Item.Calls.Contains("observe"), "stage B never ran");
        Assert.AreEqual(Protocol2InterpreterFixture.User, harness.Port.ExpectedSid);
        if (scenario.StartsWith("capability", StringComparison.Ordinal) || scenario.Contains("modify") || scenario.Contains("protection"))
            Assert.AreEqual(F.PeerImage, harness.Capability.PeerImagePath, "G1 hashes the image read through the held peer handle");
        if (scenario != "begin-null" && scenario != "begin-throws") Assert.IsTrue(harness.Item.Disposed, "the item is disposed");
    }

    [TestMethod]
    public async Task A3UsesTheItemsRequesterCheckAsTheProtectionPrincipal()
    {
        EndpointHarness harness = new();
        List<string> roots = [];
        harness.Item.Protect = (root, _) =>
        {
            roots.Add(root);
            return true;
        };
        EndpointRun run = await harness.ExchangeAsync();
        Assert.AreEqual(ReviewedHelperQualification.Matched, run.Response.Reviewed.Qualification);
        CollectionAssert.AreEqual(new[] { FakeCapability.Root }, roots);
        Assert.AreEqual(1, harness.Capability.Calls);
    }

    // ---- The full positive path ----

    [TestMethod]
    public async Task AllStagesPassingThenAStartedSettledZeroExitIsMatchedStartedAndCompletedAtTheDesktop()
    {
        EndpointHarness harness = new();
        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(F.Installed, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(F.Candidate, response.Reviewed.ObservedCandidateVersion);
        Assert.AreEqual(F.Fingerprint, response.Reviewed.ObservedProfileFingerprint);
        Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation);
        Assert.AreEqual(0, response.Facts.ExitCode);
        Assert.IsTrue(response.Facts.AggregateSettlementConfirmed);
        CollectionAssert.AreEqual(AllStages, harness.Item.Calls);
        string? passedToStageB = harness.Item.ExpectedFingerprint;
        Assert.AreEqual(F.Fingerprint, passedToStageB, "stage B receives F from the frozen EvidenceId");
        Assert.AreEqual(1, harness.Item.UpgradeCalls, "exactly one upgrade");
        Assert.IsTrue(harness.Item.Disposed);

        ReviewedUpdateProtocol2Interpretation interpreted = run.Interpretation;
        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, interpreted.Observation.Status);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(interpreted.Observation));
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Confirmed, interpreted.CandidateCheck.Kind);
    }

    // ---- Deadlines (§4): t0 at connection, remaining = 11 min − elapsed − 5 s − 5 s ----

    [TestMethod]
    public async Task EveryStageReceivesTheRemainingBudgetFromTheConnectionClock()
    {
        EndpointHarness harness = new() { PreElapsed = TimeSpan.FromSeconds(20) };
        harness.Item.Observe = (_, _, _, _) =>
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(7));
            return F.ObservedB();
        };
        harness.Item.Query = (_, _) =>
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(11));
            return Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(F.Candidate));
        };

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(ReviewedHelperQualification.Matched, run.Response.Reviewed.Qualification);
        TimeSpan initial = TimeSpan.FromMinutes(11) - TimeSpan.FromSeconds(20) - TimeSpan.FromSeconds(10);
        // Review finding 2: the upgrade deadline keeps a 1 s reserve inside the real-time cap of remaining + exit margin.
        CollectionAssert.AreEqual(new[] { initial, initial - TimeSpan.FromSeconds(7), initial - TimeSpan.FromSeconds(18),
            initial - TimeSpan.FromSeconds(18) - ReviewedHelperEndpoint.LaunchDeadlineReserve }, harness.Item.Bounds);
    }

    [TestMethod]
    [DataRow("before-b", "b-timed-out")]
    [DataRow("during-b", "c-timed-out")]
    [DataRow("during-c-start-of-d", "d-timed-out")]
    public async Task ABudgetLapseBeforeLaunchIsUndeterminedTimedOut(string when, string diagnostic)
    {
        TimeSpan available = TimeSpan.FromMinutes(11) - TimeSpan.FromSeconds(10);
        EndpointHarness harness = new() { PreElapsed = when == "before-b" ? available : available - TimeSpan.FromSeconds(30) };
        if (when == "during-b")
            harness.Item.Observe = (_, _, _, _) =>
            {
                harness.Clock.Advance(TimeSpan.FromSeconds(30));
                return F.ObservedB();
            };
        if (when == "during-c-start-of-d")
            harness.Item.Query = (_, _) =>
            {
                harness.Clock.Advance(TimeSpan.FromSeconds(31));
                return Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(F.Candidate));
            };

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(diagnostic, run.Result.Diagnostic);
        AssertUndetermined(run, cancelled: false, timedOut: true, settled: true);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    // ---- Stage B ----

    [TestMethod]
    [DataRow("MachineInputsUnsupported", null, null)]
    [DataRow("SourceNotCommunity", null, null)]
    [DataRow("InstalledMismatch", F.OtherInstalled, null)]
    [DataRow("InstalledMismatch", null, null)]
    [DataRow("ProfileMismatch", F.Installed, "other")]
    public async Task StageBRefusalsReportTheirReasonAndValuesAndSettle(string reason, string? installed, string? fingerprint)
    {
        string? observedFingerprint = fingerprint is null ? null : F.OtherFingerprint;
        EndpointHarness harness = new();
        harness.Item.Observe = (_, _, _, _) => new ChocolateyStageValues.Refused(Enum.Parse<ChocolateyStageRefusal>(reason), installed, observedFingerprint);

        EndpointRun run = await harness.ExchangeAsync();

        AssertSettledRefusal(run, Enum.Parse<Reason>(reason), installed, null, observedFingerprint);
        CollectionAssert.AreEqual(ThroughB, harness.Item.Calls);
    }

    [TestMethod]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("throws-cancelled")]
    public async Task StageBInterruptsAreUndetermined(string scenario)
    {
        EndpointHarness harness = new();
        harness.Item.Observe = (_, _, _, _) => scenario switch
        {
            "timed-out" => new ChocolateyStageValues.TimedOut(),
            "cancelled" => new ChocolateyStageValues.Cancelled(),
            _ => CancelThenThrow(harness),
        };
        EndpointRun run = await harness.ExchangeAsync();
        AssertUndetermined(run, cancelled: scenario != "timed-out", timedOut: scenario == "timed-out", settled: true);
    }

    [TestMethod]
    [DataRow("throws", true)]
    [DataRow("throws", false)]
    [DataRow("out-of-order", true)]
    [DataRow("observed-other-installed", true)]
    [DataRow("observed-other-fingerprint", true)]
    public async Task StageBInternalFaultsFollowN2(string scenario, bool settled)
    {
        EndpointHarness harness = new();
        harness.Item.Settled = () => settled;
        harness.Item.Observe = (_, _, _, _) => scenario switch
        {
            "throws" => throw new InvalidDataException("synthetic"),
            "out-of-order" => new ChocolateyStageValues.OutOfOrder(),
            "observed-other-installed" => new ChocolateyStageValues.Observed(F.OtherInstalled, F.Fingerprint),
            _ => new ChocolateyStageValues.Observed(F.Installed, F.OtherFingerprint),
        };
        EndpointRun run = await harness.ExchangeAsync();
        if (settled) AssertSettledRefusal(run, Reason.MachineInputsUnsupported, null, null, null);
        else AssertUndetermined(run, cancelled: false, timedOut: false, settled: false);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    public async Task AFaultWithTheDisconnectTokenCancelledIsUndeterminedNeverARefusal()
    {
        EndpointHarness harness = new();
        harness.Item.Observe = (_, _, _, _) =>
        {
            harness.HelperCancellation.Cancel();
            throw new InvalidDataException("synthetic");
        };
        EndpointRun run = await harness.ExchangeAsync();
        AssertUndetermined(run, cancelled: true, timedOut: false, settled: true);
    }

    // ---- Stage C ----

    [TestMethod]
    [DataRow("installed-mismatch")]
    [DataRow("installed-gone")]
    [DataRow("preflight-failed")]
    [DataRow("preflight-overflow")]
    [DataRow("candidate-none")]
    [DataRow("candidate-differs")]
    public async Task StageCRefusalsReportTheirReasonAndValuesAndSettle(string scenario)
    {
        (ChocolateyCandidateRefusal reason, string? installed, string? candidate, bool overflow) = scenario switch
        {
            "installed-mismatch" => (ChocolateyCandidateRefusal.InstalledMismatch, F.OtherInstalled, (string?)null, false),
            "installed-gone" => (ChocolateyCandidateRefusal.InstalledMismatch, null, null, false),
            "preflight-failed" => (ChocolateyCandidateRefusal.PreflightQueryFailed, F.Installed, null, false),
            "preflight-overflow" => (ChocolateyCandidateRefusal.PreflightQueryFailed, F.Installed, null, true),
            "candidate-none" => (ChocolateyCandidateRefusal.CandidateMismatch, F.Installed, null, false),
            _ => (ChocolateyCandidateRefusal.CandidateMismatch, F.Installed, F.OtherCandidate, false),
        };
        EndpointHarness harness = new();
        harness.Item.Query = (_, _) => Task.FromResult<ChocolateyCandidateValues>(
            new ChocolateyCandidateValues.Refused(reason, installed, candidate, F.Fingerprint, overflow));

        EndpointRun run = await harness.ExchangeAsync();

        AssertSettledRefusal(run, Enum.Parse<Reason>(reason.ToString()), installed, candidate, F.Fingerprint, overflow);
        CollectionAssert.AreEqual(ThroughC, harness.Item.Calls);
    }

    [TestMethod]
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(false, false, false)]
    public async Task StageCUndeterminedKeepsItsFlags(bool cancelled, bool timedOut, bool settled)
    {
        EndpointHarness harness = new();
        harness.Item.Query = (_, _) => Task.FromResult<ChocolateyCandidateValues>(
            new ChocolateyCandidateValues.Undetermined(cancelled, timedOut, settled, false));
        EndpointRun run = await harness.ExchangeAsync();
        AssertUndetermined(run, cancelled, timedOut, settled);
    }

    [TestMethod]
    [DataRow("out-of-order", true)]
    [DataRow("out-of-order", false)]
    [DataRow("throws", true)]
    [DataRow("throws", false)]
    [DataRow("matched-other-candidate", true)]
    public async Task StageCOutOfOrderAndFaultsMapPerF2(string scenario, bool settled)
    {
        EndpointHarness harness = new();
        harness.Item.Settled = () => settled;
        harness.Item.Query = scenario switch
        {
            "out-of-order" => (_, _) => Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.OutOfOrder()),
            "throws" => (_, _) => Task.FromException<ChocolateyCandidateValues>(new IOException("synthetic")),
            _ => (_, _) => Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(F.OtherCandidate)),
        };
        EndpointRun run = await harness.ExchangeAsync();
        // rev-5 F2 stage C: the stage-B installed value and fingerprint, candidate null.
        if (settled) AssertSettledRefusal(run, Reason.MachineInputsUnsupported, F.Installed, null, F.Fingerprint);
        else AssertUndetermined(run, cancelled: false, timedOut: false, settled: false);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    public async Task AnUnresponsiveStageCIsUndeterminedTimedOutAndUnsettled()
    {
        EndpointHarness harness = HungStageHarness();
        TaskCompletionSource<ChocolateyCandidateValues> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Item.Query = (_, _) => never.Task;

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual("c-unresponsive", run.Result.Diagnostic);
        AssertUndetermined(run, cancelled: false, timedOut: true, settled: false);
    }

    [TestMethod]
    [DataRow("b")]
    [DataRow("d")]
    public async Task AnUnresponsiveSynchronousStageIsCappedUndeterminedTimedOutUnsettledAndTheItemIsDisposedInFlight(string stage)
    {
        // Review finding 1: stages B and D run under a real-time cap of remaining + exit margin.
        EndpointHarness harness = HungStageHarness();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ChocolateyStageValues Hang()
        {
            release.Task.Wait(F.Wait);
            return F.ObservedB();
        }
        if (stage == "b") harness.Item.Observe = (_, _, _, _) => Hang();
        else harness.Item.Revalidate = (_, _) => Hang();

        try
        {
            EndpointRun run = await harness.ExchangeAsync();

            Assert.AreEqual(stage + "-unresponsive", run.Result.Diagnostic);
            AssertUndetermined(run, cancelled: false, timedOut: true, settled: false);
            CollectionAssert.AreEqual(stage == "b" ? ThroughB : ThroughD, harness.Item.Calls);
            Assert.IsTrue(harness.Item.Disposed, "the item is disposed while the capped call is still in flight (N4b obligation)");
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [TestMethod]
    [DataRow("b")]
    [DataRow("c")]
    [DataRow("d")]
    [DataRow("launch")]
    public async Task AnItemCancellationWithoutEndpointCancellationIsAnInternalFaultNotCancelled(string stage)
    {
        // Review finding 5: settled item, endpoint token never cancelled, yet the item throws OperationCanceledException.
        EndpointHarness harness = new();
        switch (stage)
        {
            case "b":
                harness.Item.Observe = (_, _, _, _) => throw new OperationCanceledException();
                break;
            case "c":
                harness.Item.Query = (_, _) => Task.FromCanceled<ChocolateyCandidateValues>(new CancellationToken(true));
                break;
            case "d":
                harness.Item.Revalidate = (_, _) => throw new OperationCanceledException();
                break;
            default:
                harness.Item.Upgrade = (_, _) => throw new OperationCanceledException();
                break;
        }

        EndpointRun run = await harness.ExchangeAsync();

        Assert.IsFalse(harness.HelperCancellation.IsCancellationRequested);
        if (stage != "launch")
        {
            Assert.AreEqual(stage + "-unexpected-cancellation", run.Result.Diagnostic);
            AssertUndetermined(run, cancelled: false, timedOut: false, settled: false);
            Assert.AreEqual(0, harness.Item.UpgradeCalls);
            return;
        }
        // After launch was entered: Unknown, not cancelled, not settled.
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(UpdateWireInvocation.Unknown, response.Facts.Invocation);
        Assert.IsFalse(response.Facts.Cancelled || response.Facts.AggregateSettlementConfirmed);
        Assert.IsTrue(run.Interpretation.Observation.ExecutionUncertain);
        Assert.AreEqual(1, harness.Item.UpgradeCalls);
    }

    // ---- Stage D ----

    [TestMethod]
    [DataRow("request-freshness")]
    [DataRow("review-ceiling")]
    public async Task AnApprovalLapseAtStageDIsApprovalExpiredAndSettles(string limit)
    {
        EndpointHarness harness = new();
        if (limit == "review-ceiling") harness.Clock.Advance(TimeSpan.FromMinutes(28));
        harness.Item.Query = (_, _) =>
        {
            harness.Clock.Advance(limit == "request-freshness" ? TimeSpan.FromMinutes(2) + TimeSpan.FromTicks(1) : TimeSpan.FromMinutes(1) + TimeSpan.FromTicks(1));
            return Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(F.Candidate));
        };
        // The 30-minute case keeps the request itself fresh by issuing it late.
        EndpointRun run = await harness.ExchangeAsync();
        if (limit == "review-ceiling")
        {
            Assert.AreEqual("d-approval-expired", run.Result.Diagnostic);
        }
        AssertSettledRefusal(run, Reason.ApprovalExpired, null, null, null);
        CollectionAssert.AreEqual(ThroughC, harness.Item.Calls, "revalidation never ran");
    }

    [TestMethod]
    [DataRow("ProfileMismatch", F.Installed, true, F.Candidate)]
    [DataRow("InstalledMismatch", F.OtherInstalled, false, F.Candidate)]
    [DataRow("MachineInputsUnsupported", F.Installed, true, F.Candidate)]
    [DataRow("InstalledMismatch", null, false, null)]
    [DataRow("MachineInputsUnsupported", null, false, null)]
    [DataRow("SourceNotCommunity", null, false, null)]
    public async Task StageDDriftReportsTheStageBReasonWithStageDValuesAndNullsTheCandidateWithoutInstalled(string reason, string? installed,
        bool withFingerprint, string? expectedCandidate)
    {
        string? fingerprint = withFingerprint ? F.OtherFingerprint : null;
        EndpointHarness harness = new();
        harness.Item.Revalidate = (_, _) => new ChocolateyStageValues.Refused(Enum.Parse<ChocolateyStageRefusal>(reason), installed, fingerprint);

        EndpointRun run = await harness.ExchangeAsync();

        // Stage-D values (not stage B's), the stage-C candidate kept only with an installed value (S7, P5).
        AssertSettledRefusal(run, Enum.Parse<Reason>(reason), installed, expectedCandidate, fingerprint);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("cancelled-before-d")]
    public async Task StageDInterruptsAreUndetermined(string scenario)
    {
        EndpointHarness harness = new();
        if (scenario == "cancelled-before-d")
            harness.Item.Query = (_, _) =>
            {
                harness.HelperCancellation.Cancel();
                return Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(F.Candidate));
            };
        else
            harness.Item.Revalidate = (_, _) => scenario == "timed-out" ? new ChocolateyStageValues.TimedOut() : new ChocolateyStageValues.Cancelled();
        EndpointRun run = await harness.ExchangeAsync();
        AssertUndetermined(run, cancelled: scenario != "timed-out", timedOut: scenario == "timed-out", settled: true);
        if (scenario == "cancelled-before-d") Assert.AreEqual("d-cancelled", run.Result.Diagnostic);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    [DataRow("out-of-order", true)]
    [DataRow("out-of-order", false)]
    [DataRow("throws", true)]
    [DataRow("throws", false)]
    [DataRow("observed-other-fingerprint", true)]
    public async Task StageDOutOfOrderAndFaultsMapPerF2(string scenario, bool settled)
    {
        EndpointHarness harness = new();
        harness.Item.Settled = () => settled;
        harness.Item.Revalidate = (_, _) => scenario switch
        {
            "out-of-order" => new ChocolateyStageValues.OutOfOrder(),
            "throws" => throw new UnauthorizedAccessException("synthetic"),
            _ => new ChocolateyStageValues.Observed(F.Installed, F.OtherFingerprint),
        };
        EndpointRun run = await harness.ExchangeAsync();
        // rev-5 F2 stage D: no stage-D values exist, so installed, fingerprint and candidate are null.
        if (settled) AssertSettledRefusal(run, Reason.MachineInputsUnsupported, null, null, null);
        else AssertUndetermined(run, cancelled: false, timedOut: false, settled: false);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    // ---- Launch ----

    [TestMethod]
    [DataRow(59, false)]
    [DataRow(60, true)]
    public async Task TheSixtySecondLaunchFloorIsCheckedAfterStageD(int secondsLeftAfterD, bool launches)
    {
        TimeSpan available = TimeSpan.FromMinutes(11) - TimeSpan.FromSeconds(10);
        EndpointHarness harness = new() { PreElapsed = available - TimeSpan.FromSeconds(secondsLeftAfterD + 3) };
        harness.Item.Revalidate = (_, _) =>
        {
            harness.Clock.Advance(TimeSpan.FromSeconds(3));
            return F.ObservedB();
        };

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(launches ? 1 : 0, harness.Item.UpgradeCalls);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        if (launches)
        {
            Assert.AreEqual(TimeSpan.FromSeconds(60), harness.Item.Bounds[^1]);
            return;
        }
        Assert.AreEqual(UpdateWireInvocation.NotStarted, response.Facts.Invocation);
        Assert.IsTrue(response.Facts.TimedOut && response.Facts.AggregateSettlementConfirmed);
        ReviewedUpdateProtocol2Interpretation interpreted = run.Interpretation;
        Assert.AreEqual(ReviewedUpdateProviderStatus.TimedOut, interpreted.Observation.Status);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, interpreted.Observation.Invocation);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(interpreted.Observation));
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Confirmed, interpreted.CandidateCheck.Kind);
    }

    [TestMethod]
    public async Task CancellationAfterStageDBeforeCreationIsMatchedNotStartedCancelled()
    {
        EndpointHarness harness = new();
        harness.Item.Revalidate = (_, _) =>
        {
            harness.HelperCancellation.Cancel();
            return F.ObservedB();
        };
        EndpointRun run = await harness.ExchangeAsync();
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(UpdateWireInvocation.NotStarted, response.Facts.Invocation);
        Assert.IsTrue(response.Facts.Cancelled && response.Facts.AggregateSettlementConfirmed);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Cancelled, run.Interpretation.Observation.Status);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(run.Interpretation.Observation));
    }

    [TestMethod]
    public async Task UnsettledPreflightWorkBeforeLaunchIsUndeterminedAndNothingLaunches()
    {
        EndpointHarness harness = new();
        // Settled through stage D's checks, then the launch gate reads an unsettled aggregate.
        harness.Item.Revalidate = (_, _) =>
        {
            harness.Item.Settled = () => false;
            return F.ObservedB();
        };
        EndpointRun run = await harness.ExchangeAsync();
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
        AssertUndetermined(run, cancelled: false, timedOut: false, settled: false);
    }

    [TestMethod]
    [DataRow("not-started-settled")]
    [DataRow("not-started-settled-cancelled")]
    [DataRow("not-started-unsettled")]
    [DataRow("not-started-termination-failed")]
    [DataRow("started-exit-0")]
    [DataRow("started-exit-1")]
    [DataRow("started-timed-out")]
    [DataRow("started-cancelled-exit-0")]
    [DataRow("started-output-limit")]
    [DataRow("started-unsettled")]
    [DataRow("started-item-unsettled-after")]
    [DataRow("unknown")]
    [DataRow("upgrade-throws")]
    [DataRow("upgrade-faults")]
    [DataRow("upgrade-cancelled")]
    public async Task LaunchObservationsMapToTheLaunchRow(string scenario)
    {
        EndpointHarness harness = new();
        bool upgraded = false;
        harness.Item.Settled = () => !(upgraded && scenario == "started-item-unsettled-after");
        harness.Item.Upgrade = (_, _) =>
        {
            upgraded = true;
            return scenario switch
            {
                "upgrade-throws" => throw new InvalidOperationException("synthetic"),
                "upgrade-faults" => Task.FromException<ChocolateyUpgradeResult>(new IOException("synthetic")),
                "upgrade-cancelled" => Task.FromCanceled<ChocolateyUpgradeResult>(new CancellationToken(true)),
                _ => Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.Observed(scenario switch
                {
                    "not-started-settled" => F.NotStarted(),
                    "not-started-settled-cancelled" => F.NotStarted(cancelled: true),
                    "not-started-unsettled" => F.NotStarted(settled: false),
                    "not-started-termination-failed" => F.NotStarted(settled: false, terminationFailed: true),
                    "started-exit-0" or "started-item-unsettled-after" => F.Started(0),
                    "started-exit-1" => F.Started(1),
                    "started-timed-out" => F.Started(1, timedOut: true),
                    "started-cancelled-exit-0" => F.Started(0, cancelled: true),
                    "started-output-limit" => F.Started(0, outputLimit: true),
                    "started-unsettled" => F.Started(0, settled: false),
                    _ => F.UnknownLaunch(),
                })),
            };
        };

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(1, harness.Item.UpgradeCalls, "exactly one upgrade, never retried");
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(F.Installed, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(F.Candidate, response.Reviewed.ObservedCandidateVersion);
        Assert.AreEqual(F.Fingerprint, response.Reviewed.ObservedProfileFingerprint);
        ReviewedUpdateDispatchObservation observation = run.Interpretation.Observation;
        (UpdateWireInvocation invocation, ReviewedUpdateProviderStatus status, bool settled) = scenario switch
        {
            "not-started-settled" => (UpdateWireInvocation.NotStarted, ReviewedUpdateProviderStatus.Failed, true),
            "not-started-settled-cancelled" => (UpdateWireInvocation.NotStarted, ReviewedUpdateProviderStatus.Cancelled, true),
            "started-exit-0" => (UpdateWireInvocation.Started, ReviewedUpdateProviderStatus.Completed, true),
            "started-exit-1" => (UpdateWireInvocation.Started, ReviewedUpdateProviderStatus.ProviderFailed, true),
            "started-timed-out" => (UpdateWireInvocation.Started, ReviewedUpdateProviderStatus.TimedOut, true),
            "started-cancelled-exit-0" => (UpdateWireInvocation.Started, ReviewedUpdateProviderStatus.Cancelled, true),
            "started-output-limit" => (UpdateWireInvocation.Started, ReviewedUpdateProviderStatus.Failed, true),
            "started-unsettled" or "started-item-unsettled-after" => (UpdateWireInvocation.Started, ReviewedUpdateProviderStatus.Completed, false),
            // A NotStarted not known to be settled, Unknown, and any exception escaping UpgradeAsync are all Unknown.
            _ => (UpdateWireInvocation.Unknown, ReviewedUpdateProviderStatus.Failed, false),
        };
        Assert.AreEqual(invocation, response.Facts.Invocation, scenario);
        Assert.AreEqual(status, observation.Status, scenario);
        Assert.AreEqual(settled, Protocol2InterpreterFixture.IsSettled(observation), scenario);
        Assert.AreEqual(settled, response.Facts.AggregateSettlementConfirmed, scenario);
        if (scenario == "not-started-termination-failed") Assert.IsTrue(response.Facts.AggregateTerminationFailed);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task LaunchOutOfOrderIsMachineInputsUnsupportedWithStageDValuesAndTheCandidateOnlyWhenSettled(bool settled)
    {
        EndpointHarness harness = new();
        bool upgraded = false;
        harness.Item.Settled = () => settled || !upgraded;
        harness.Item.Upgrade = (_, _) =>
        {
            upgraded = true;
            return Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.OutOfOrder());
        };
        EndpointRun run = await harness.ExchangeAsync();
        // rev-5 F2 Launch: the stage-D values plus the stage-C candidate (S7, P5, P6).
        if (settled) AssertSettledRefusal(run, Reason.MachineInputsUnsupported, F.Installed, F.Candidate, F.Fingerprint);
        else AssertUndetermined(run, cancelled: false, timedOut: false, settled: false);
    }

    [TestMethod]
    public async Task AnUnresponsiveLaunchIsCappedMatchedUnknownUnsettledAndUncertainAtTheDesktop()
    {
        // Review findings 2 and 3: the cap stays remaining + exit margin; the upgrade deadline is remaining − 1 s, floored.
        EndpointHarness harness = HungStageHarness();
        TaskCompletionSource<ChocolateyUpgradeResult> never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Item.Upgrade = (_, _) => never.Task;

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual("launch-unresponsive", run.Result.Diagnostic);
        Assert.AreEqual(1, harness.Item.UpgradeCalls, "exactly one upgrade, never retried");
        Assert.AreEqual(harness.Budgets.LaunchFloor, harness.Item.Bounds[^1], "remaining − 1 s is below the floor, so the floor applies");
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(UpdateWireInvocation.Unknown, response.Facts.Invocation);
        Assert.IsFalse(response.Facts.AggregateSettlementConfirmed);
        Assert.IsFalse(response.Facts.Cancelled || response.Facts.TimedOut);
        ReviewedUpdateDispatchObservation observation = run.Interpretation.Observation;
        Assert.AreEqual(ReviewedUpdateInvocation.Unknown, observation.Invocation);
        Assert.IsTrue(observation.ExecutionUncertain, "the desktop records Uncertain");
        Assert.IsFalse(Protocol2InterpreterFixture.IsSettled(observation));
        Assert.IsTrue(harness.Item.Disposed);
    }

    [TestMethod]
    [DataRow("exit-without-direct-exit")]
    [DataRow("settled-with-termination-failure")]
    public async Task AnInconsistentObservationMakesCreateResponseThrowSoNothingIsWritten(string scenario)
    {
        EndpointHarness harness = new();
        NativeProcessObservation observation = scenario == "exit-without-direct-exit"
            ? new(NativeInvocation.Started, 0, false, true, true, false, false, false, false, false, false, "", "")
            : new(NativeInvocation.Started, 0, true, true, true, true, true, false, false, false, false, "", "");
        harness.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.Observed(observation));

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(ReviewedHelperEndpointExit.NoResponse, run.Result.Exit);
        StringAssert.StartsWith(run.Result.Diagnostic, "response-refused-by-codec");
        Assert.AreEqual(ReviewedHelperTransportOutcome.RequestWrittenNoValidResponse, run.Exchange!.Outcome);
        Protocol2InterpreterFixture.AssertRejected(run.Interpretation);
    }

    // ---- Disconnect monitor ----

    [TestMethod]
    public async Task ClosingTheDesktopEndCancelsProviderWork()
    {
        EndpointHarness harness = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool observedCancellation = false;
        harness.Item.Query = async (_, token) =>
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                observedCancellation = true;
            }
            return new ChocolateyCandidateValues.Undetermined(true, false, true, false);
        };

        (ReviewedHelperEndpointResult result, _) = await harness.RawAsync(async server =>
        {
            await PipeMessageFraming.WriteAsync(server, F.Request(F.Operation(), harness.Clock.GetUtcNow()), CancellationToken.None);
            await started.Task.WaitAsync(F.Wait);
            server.Disconnect();
        }, readBack: false);

        Assert.IsTrue(observedCancellation, "the monitor cancelled provider work");
        Assert.AreEqual("c-undetermined", result.Diagnostic);
        Assert.AreNotEqual(ReviewedHelperEndpointExit.NoResponse, result.Exit, "a response was attempted once");
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
        Assert.IsTrue(harness.Item.Disposed);
    }

    [TestMethod]
    public async Task TheMonitorDoesNotCancelWhileTheDesktopWaits()
    {
        EndpointHarness harness = new();
        CancellationToken seen = default;
        harness.Item.Query = async (_, token) =>
        {
            seen = token;
            await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
            return new ChocolateyCandidateValues.Matched(F.Candidate);
        };
        EndpointRun run = await harness.ExchangeAsync();
        Assert.IsTrue(seen.CanBeCanceled && !seen.IsCancellationRequested);
        Assert.AreEqual(ReviewedHelperQualification.Matched, run.Response.Reviewed.Qualification);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TheMonitorStopAndTheResponseWriteShareOneWindowFromTheResponseMargin(bool monitorHangs)
    {
        // Review finding 6: a monitor that ignores its stop and a write that never completes together take at most
        // min(ResponseWrite, ResponseMargin), not one budget each.
        TimeSpan window = TimeSpan.FromSeconds(2);
        EndpointHarness harness = new()
        {
            Budgets = ReviewedHelperEndpointBudgets.Production with { ResponseMargin = window, ResponseWrite = TimeSpan.FromSeconds(5) },
            WrapHelperPipe = pipe => new StubbornPipe(pipe, ignoreReadCancellation: monitorHangs, neverCompleteWrites: true),
        };
        long stagesDone = 0;
        harness.Item.Upgrade = (_, _) =>
        {
            stagesDone = System.Diagnostics.Stopwatch.GetTimestamp();
            return Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.Observed(F.Started()));
        };

        EndpointRun run = await harness.ExchangeAsync();
        TimeSpan responsePhase = System.Diagnostics.Stopwatch.GetElapsedTime(stagesDone);

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWriteFailed, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreNotEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, run.Exchange!.Outcome);
        Assert.IsTrue(responsePhase < window + TimeSpan.FromSeconds(1), $"response phase {responsePhase} exceeds the shared window");
        Assert.IsTrue(responsePhase >= window - TimeSpan.FromMilliseconds(200), $"response phase {responsePhase} ended before the window");
        Assert.IsTrue(harness.Item.Disposed);
    }

    // ---- No entry-point wiring (N4b) ----

    [TestMethod]
    public async Task TheProtocol2EntryPointStillExitsWithCode20()
    {
        string[] arguments =
        [
            "--protocol", "2", "--desktop-pid", "4242", "--pipe", WindowsReviewedHelperTransport.CreatePipeName(), "--nonce", F.Nonce,
        ];
        Assert.AreEqual(20, await ElevatedHelperEntryPoint.RunAsync(arguments).WaitAsync(F.Wait));
    }

    // ---- Assertions ----

    /// <summary>Remaining 400 ms, exit margin 100 ms and launch floor 200 ms on the fake clock, so the caps fire in real time.</summary>
    private static EndpointHarness HungStageHarness()
    {
        EndpointHarness harness = new()
        {
            Budgets = ReviewedHelperEndpointBudgets.Production with
            {
                ExitMargin = TimeSpan.FromMilliseconds(100), ResponseMargin = TimeSpan.FromSeconds(2), LaunchFloor = TimeSpan.FromMilliseconds(200),
            },
        };
        harness.PreElapsed = harness.Budgets.Execution - harness.Budgets.ExitMargin - harness.Budgets.ResponseMargin - TimeSpan.FromMilliseconds(400);
        return harness;
    }

    private static ChocolateyStageValues CancelThenThrow(EndpointHarness harness)
    {
        harness.HelperCancellation.Cancel();
        throw new OperationCanceledException(harness.HelperCancellation.Token);
    }

    private static void AssertNotAdmitted(EndpointRun run, string? diagnosticPrefix)
    {
        AssertSettledRefusal(run, Reason.RequestNotAdmitted, null, null, null);
        if (diagnosticPrefix is not null) StringAssert.StartsWith(run.Result.Diagnostic, diagnosticPrefix);
    }

    private static void AssertSettledRefusal(EndpointRun run, Reason reason, string? installed, string? candidate, string? fingerprint,
        bool outputLimit = false)
    {
        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Refused, response.Reviewed.Qualification, run.Result.Diagnostic);
        Assert.AreEqual(reason, response.Reviewed.RefusalReason, run.Result.Diagnostic);
        Assert.AreEqual(installed, response.Reviewed.ObservedInstalledVersion, "field 16");
        Assert.AreEqual(candidate, response.Reviewed.ObservedCandidateVersion, "field 17");
        Assert.AreEqual(fingerprint, response.Reviewed.ObservedProfileFingerprint, "field 18");
        Assert.AreEqual(UpdateWireInvocation.NotStarted, response.Facts.Invocation);
        Assert.IsTrue(response.Facts.AggregateSettlementConfirmed);
        Assert.IsFalse(response.Facts.Cancelled || response.Facts.TimedOut, "S10");
        Assert.AreEqual(outputLimit, response.Facts.OutputLimitExceeded);

        // The desktop settles the refusal: Refused, NotStarted, settled, with the reason in the candidate check.
        ReviewedUpdateProtocol2Interpretation interpreted = run.Interpretation;
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, interpreted.Observation.Status, run.Result.Diagnostic);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, interpreted.Observation.Invocation);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(interpreted.Observation), "settled, not Uncertain");
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Refused, interpreted.CandidateCheck.Kind);
        Assert.AreEqual(reason, interpreted.CandidateCheck.RefusalReason);
    }

    private static void AssertUndetermined(EndpointRun run, bool cancelled, bool timedOut, bool settled)
    {
        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Undetermined, response.Reviewed.Qualification, run.Result.Diagnostic);
        Assert.AreEqual(UpdateWireInvocation.NotStarted, response.Facts.Invocation);
        Assert.AreEqual(cancelled, response.Facts.Cancelled, "cancelled");
        Assert.AreEqual(timedOut, response.Facts.TimedOut, "timedOut");
        Assert.AreEqual(settled, response.Facts.AggregateSettlementConfirmed, "settled");

        ReviewedUpdateProtocol2Interpretation interpreted = run.Interpretation;
        Assert.AreEqual(ReviewedHelperCandidateCheckKind.Unconfirmed, interpreted.CandidateCheck.Kind);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, interpreted.Observation.Invocation);
        Assert.AreEqual(settled, Protocol2InterpreterFixture.IsSettled(interpreted.Observation));
        Assert.AreEqual(timedOut ? ReviewedUpdateProviderStatus.TimedOut : cancelled ? ReviewedUpdateProviderStatus.Cancelled : ReviewedUpdateProviderStatus.Failed,
            interpreted.Observation.Status);
    }
}
