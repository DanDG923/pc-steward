using System.Security.Cryptography;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.EndpointFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Phase D stage D4b design §5.1 (FR-UPD-15, FR-UPD-01/03, TM-03/04/07/13): the machine WinGet route on the protocol-2 helper,
/// admitted from stage 3. The reply has its own shape (<see cref="ReviewedHelperReplyShape.WinGetMachine"/>): the same 20 properties as the
/// package reply, with S12 checking the version fields against the reviewed version grammar. The route's profile digest is the
/// reviewed HKLM registration digest. The stage machine runs this route only through the fake item and the injected capability seam.
/// The real A3 allow-list lists it from stage 3.
/// </summary>
[TestClass]
public sealed class WingetMachineHelperRouteTests
{
    private const string Installed = "24.09";
    private const string Candidate = "25.01";
    private static readonly string Registration = Convert.ToHexString(SHA256.HashData("wgm1 registration fixture"u8));
    private static readonly string Manifest = new('B', 64);
    private static readonly string InstallerHash = new('C', 64);
    private static readonly Guid Job = Guid.Parse("d4b10000-0000-0000-0000-000000000001");
    private static readonly string Nonce = new('C', 64);
    private static readonly string Digest = Convert.ToHexString(SHA256.HashData("operation"u8));
    private static readonly string[] S12Only = ["S12"];
    private static readonly string[] AllStages = ["protect", "observe", "query", "revalidate", "upgrade"];

    internal static ReviewedUpdateOperation Operation(string installed = Installed, string candidate = Candidate)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("d4b10000-0000-0000-0000-000000000002"),
            Guid.Parse("d4b10000-0000-0000-0000-000000000003"), new UpdateJobSelection("arp:hklm64:7zip", UpdateProvider.WinGet,
                UpdateSource.WinGet, "7zip.7zip", installed, candidate, UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable,
                "mapping-v2")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
        return new(job, Protocol2InterpreterFixture.User, "arp:hklm64:7zip", WingetMachineReviewedRoute.InstallationContextId,
            $"wgm1:msi:{Registration}:{Manifest}:{InstallerHash}", Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1),
            UpdateRouteContract.WinGetMachineSilentV1, ReviewedInstallerMode.Selected(InstallerMode.Silent), null);
    }

    // ---- Shape and route gate ----

    [TestMethod]
    public void TheRouteHasItsShapeAndIsAdmittedFromStage3()
    {
        ReviewedUpdateOperation operation = Operation();
        Assert.IsTrue(WingetMachineReviewedRoute.TryGetShape(operation, out WinGetMachineEvidence? evidence, out string architecture,
            out string installerType));
        Assert.AreEqual(Registration, evidence!.RegistrationDigest);
        Assert.AreEqual("x64", architecture);
        Assert.AreEqual("msi", installerType);
        Assert.IsTrue(WingetMachineReviewedRoute.Admits(operation), "D4b stage 3: the review gate admits the route");
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(operation));
        CollectionAssert.Contains(DeploymentCapabilityPolicy.CapabilityOperationAllowlist.ToArray(), UpdateRouteContract.WinGetMachineSilentV1,
            "A3 lists the route from stage 3");
        Assert.IsFalse(WingetMachineReviewedRoute.TryGetShape(null, out _, out _, out _));
        Assert.IsFalse(WingetMachineReviewedRoute.TryGetShape(F.Operation(), out _, out _, out _));
        Assert.IsFalse(WingetMachineReviewedRoute.TryGetShape(ReviewedHelperEndpointClientRunFaultTests.ClientOperation(), out _, out _, out _));
    }

    [TestMethod]
    public void TheProfileDigestIsTheReviewedRegistrationDigestWithTheWinGetShape()
    {
        Assert.IsTrue(ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(Operation(), out string digest, out ReviewedHelperReplyShape shape));
        Assert.AreEqual(ReviewedHelperReplyShape.WinGetMachine, shape);
        Assert.AreEqual(Registration, digest);
        Assert.IsFalse(ReviewedUpdateProtocol2Interpreter.TryGetProfileDigest(Operation(), out _), "never the Chocolatey package shape");
        Assert.IsFalse(ReviewedUpdateProtocol2Interpreter.TryGetClientProfileDigest(Operation(), out _));

        Assert.IsTrue(ReviewedUpdateProtocol2Interpreter.TryGetRouteProfileDigest(F.Operation(), out _, out ReviewedHelperReplyShape package));
        Assert.AreEqual(ReviewedHelperReplyShape.Package, package);
    }

    // ---- Version text grammar ----

    [TestMethod]
    [DataRow("24.09", true)]
    [DataRow("1.2.3", true)]
    [DataRow("2024.11.12-beta+7", true)]
    [DataRow("v1_2", true)]
    [DataRow("", false)]
    [DataRow(" 24.09", false)]
    [DataRow("24.09 ", false)]
    [DataRow("24.09\n", false)]
    [DataRow("24.09\r\n", false)]
    [DataRow(".24", false)]
    [DataRow("24 09", false)]
    [DataRow("24/09", false)]
    [DataRow("24.09é", false)]
    public void ReviewedVersionTextIsExact(string value, bool expected) =>
        Assert.AreEqual(expected, UpdateJobSelection.IsVersionText(value), value);

    [TestMethod]
    public void ReviewedVersionTextIsBounded()
    {
        Assert.IsFalse(UpdateJobSelection.IsVersionText(null));
        Assert.IsTrue(UpdateJobSelection.IsVersionText("1" + new string('0', 127)));
        Assert.IsFalse(UpdateJobSelection.IsVersionText("1" + new string('0', 128)));
    }

    // ---- Codec: the WinGetMachine reply shape ----

    [TestMethod]
    public void TheWinGetReplyAcceptsReviewedTextThatThePackageReplyRefuses()
    {
        Assert.IsFalse(ReviewedUpdateWireCodec.IsNormalizedVersion(Installed), "24.09 is not a normalized Chocolatey version");
        ReviewedHelperFacts matched = Protocol2InterpreterFixture.Matched(Installed, Candidate, Registration);
        UpdateWireFacts started = Protocol2InterpreterFixture.Started();

        CollectionAssert.AreEqual(Array.Empty<string>(),
            ReviewedUpdateWireCodec.StructuralViolations(started, matched, ReviewedHelperReplyShape.WinGetMachine).ToArray());
        CollectionAssert.AreEqual(S12Only, ReviewedUpdateWireCodec.StructuralViolations(started, matched).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, started, matched, "ok"));

        byte[] frame = ReviewedUpdateWireCodec.CreateWinGetMachineResponse(Job, Digest, Nonce, started, matched, "ok");
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, ReviewedHelperReplyShape.WinGetMachine,
            out FramedUpdateResponse? response));
        Assert.AreEqual(Installed, response!.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(Candidate, response.Reviewed.ObservedCandidateVersion);
        Assert.AreEqual(Registration, response.Reviewed.ObservedProfileFingerprint);
        Assert.IsNull(response.ClientRunFault);
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, out _), "the package reader refuses 24.09");
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(frame, Job, Digest, Nonce, ReviewedHelperReplyShape.ClientSelfUpdate, out _));
    }

    [TestMethod]
    public void AVersionAcceptedByBothGrammarsGivesTheSameBytes()
    {
        ReviewedHelperFacts matched = Protocol2InterpreterFixture.Matched("1.2.3", "1.2.4", Registration);
        UpdateWireFacts started = Protocol2InterpreterFixture.Started();
        CollectionAssert.AreEqual(ReviewedUpdateWireCodec.CreateResponse(Job, Digest, Nonce, started, matched, "ok"),
            ReviewedUpdateWireCodec.CreateWinGetMachineResponse(Job, Digest, Nonce, started, matched, "ok"), "the shape adds no property");
    }

    [TestMethod]
    [DataRow("24.09\n")]
    [DataRow(" 24.09")]
    [DataRow("24 09")]
    [DataRow("")]
    public void TheWinGetReplyRefusesTextOffTheReviewedGrammar(string version)
    {
        UpdateWireFacts started = Protocol2InterpreterFixture.Started();
        foreach (ReviewedHelperFacts reviewed in new[]
                 {
                     Protocol2InterpreterFixture.Matched(version, Candidate, Registration),
                     Protocol2InterpreterFixture.Matched(Installed, version, Registration),
                 })
        {
            CollectionAssert.AreEqual(S12Only,
                ReviewedUpdateWireCodec.StructuralViolations(started, reviewed, ReviewedHelperReplyShape.WinGetMachine).ToArray());
            Assert.ThrowsExactly<ArgumentException>(() =>
                ReviewedUpdateWireCodec.CreateWinGetMachineResponse(Job, Digest, Nonce, started, reviewed, "ok"));
        }
    }

    [TestMethod]
    public void AnUndefinedShapeFailsS12()
    {
        ReviewedHelperFacts matched = Protocol2InterpreterFixture.Matched("1.2.3", "1.2.4", Registration);
        CollectionAssert.AreEqual(S12Only, ReviewedUpdateWireCodec.StructuralViolations(Protocol2InterpreterFixture.Started(), matched,
            (ReviewedHelperReplyShape)7).ToArray());
    }

    // ---- Interpreter ----

    [TestMethod]
    public void TheDesktopReadsTheWinGetReplyAndRecordsNoCandidateCheck()
    {
        ReviewedUpdateOperation operation = Operation();
        byte[] frame = ReviewedUpdateWireCodec.CreateWinGetMachineResponse(operation.JobId, operation.Digest, Protocol2InterpreterFixture.Nonce,
            Protocol2InterpreterFixture.Started(), Protocol2InterpreterFixture.Matched(Installed, Candidate, Registration), "ok");
        ReviewedUpdateProtocol2Interpretation result = Protocol2InterpreterFixture.Interpret(operation, frame);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, result.Observation.Status);
        Assert.IsTrue(Protocol2InterpreterFixture.IsSettled(result.Observation));
        Assert.AreSame(ReviewedHelperCandidateCheck.NotObserved, result.CandidateCheck, "the recorded check is defined for the package route only");
    }

    [TestMethod]
    [DataRow("fingerprint")]
    [DataRow("installed")]
    [DataRow("candidate")]
    public void TheDesktopRejectsAWinGetReplyOffTheFrozenOperation(string field)
    {
        ReviewedUpdateOperation operation = Operation();
        ReviewedHelperFacts reviewed = field switch
        {
            "fingerprint" => Protocol2InterpreterFixture.Matched(Installed, Candidate, new string('E', 64)),
            "installed" => Protocol2InterpreterFixture.Matched("24.08", Candidate, Registration),
            _ => Protocol2InterpreterFixture.Matched(Installed, "25.02", Registration),
        };
        byte[] frame = ReviewedUpdateWireCodec.CreateWinGetMachineResponse(operation.JobId, operation.Digest, Protocol2InterpreterFixture.Nonce,
            Protocol2InterpreterFixture.Started(), reviewed, "ok");
        Protocol2InterpreterFixture.AssertRejected(Protocol2InterpreterFixture.Interpret(operation, frame));
    }

    // ---- Helper values ----

    [TestMethod]
    public void TheWinGetValueFactoriesTakeReviewedTextOnly()
    {
        ChocolateyStageValues.Observed observed = ChocolateyStageValues.Observed.ForWinGetMachine(Installed, Registration);
        Assert.AreEqual(Installed, observed.Installed);
        Assert.AreEqual(Registration, observed.Fingerprint);
        Assert.AreEqual(Candidate, ChocolateyCandidateValues.Matched.ForWinGetMachine(Candidate).Candidate);

        foreach (string bad in new[] { "24.09\n", " 24.09", "", "24 09" })
        {
            Assert.ThrowsExactly<ArgumentException>(() => ChocolateyStageValues.Observed.ForWinGetMachine(bad, Registration), bad);
            Assert.ThrowsExactly<ArgumentException>(() => ChocolateyCandidateValues.Matched.ForWinGetMachine(bad), bad);
        }
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyStageValues.Observed.ForWinGetMachine(Installed, "not-hex"));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyStageValues.Observed(Installed, Registration), "the Chocolatey form stays normalized");
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyCandidateValues.Matched(Candidate), "the Chocolatey form stays normalized");
    }

    // ---- Stage machine (fake item, injected capability) ----

    [TestMethod]
    public async Task AllStagesPassingGiveAMatchedWinGetReplyThatTheDesktopCompletes()
    {
        EndpointHarness harness = WinGetHarness();
        ReviewedUpdateOperation operation = Operation();
        EndpointRun run = await harness.ExchangeAsync(operation);

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        FramedUpdateResponse response = Read(run);
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(Installed, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(Candidate, response.Reviewed.ObservedCandidateVersion);
        Assert.AreEqual(Registration, response.Reviewed.ObservedProfileFingerprint);
        Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation);
        CollectionAssert.AreEqual(AllStages, harness.Item.Calls);
        string? passedToStageB = harness.Item.ExpectedFingerprint;
        Assert.AreEqual(Registration, passedToStageB, "stage B receives the reviewed registration digest");
        Assert.IsTrue(harness.Item.Disposed);
        Assert.IsFalse(ReviewedUpdateWireCodec.TryReadResponse(run.Exchange!.ResponseFrame, operation.JobId, operation.Digest, F.Nonce, out _),
            "never a valid package reply");

        Assert.AreEqual(ReviewedUpdateProviderStatus.Completed, run.Interpretation.Observation.Status);
        Assert.AreSame(ReviewedHelperCandidateCheck.NotObserved, run.Interpretation.CandidateCheck);
    }

    [TestMethod]
    public async Task ADownloadLongerThanTheRequestLifetimeStillReachesTheUpgrade()
    {
        // D4b design §5.5 (Codex, PR #77): stage C includes the H7b download (up to 10 minutes). The request must be fresh when stage
        // C begins; it is not re-aged against the 2-minute limit after the download, and the route's own budget covers the upgrade.
        EndpointHarness harness = WinGetHarness();
        harness.Item.Query = (_, _) =>
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(12));
            return Task.FromResult<ChocolateyCandidateValues>(ChocolateyCandidateValues.Matched.ForWinGetMachine(Candidate));
        };
        EndpointRun run = await harness.ExchangeAsync(Operation());

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreEqual(UpdateWireInvocation.Started, Read(run).Facts.Invocation, run.Result.Diagnostic);
        Assert.AreEqual(1, harness.Item.UpgradeCalls, "a 12-minute download fits the route's 27-minute budget, past the 11-minute default");
    }

    [TestMethod]
    [DataRow("request-stale-before-download")]
    [DataRow("review-ceiling")]
    public async Task TheWinGetRouteStillRefusesAStaleRequestOrReviewAtStageD(string limit)
    {
        EndpointHarness harness = WinGetHarness();
        if (limit == "review-ceiling") harness.Clock.Advance(TimeSpan.FromMinutes(28));
        else
            harness.Item.Observe = (_, _, _, _) =>
            {
                harness.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromTicks(1));
                return ChocolateyStageValues.Observed.ForWinGetMachine(Installed, Registration);
            };
        harness.Item.Query = (_, _) =>
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromTicks(1));
            return Task.FromResult<ChocolateyCandidateValues>(ChocolateyCandidateValues.Matched.ForWinGetMachine(Candidate));
        };
        EndpointRun run = await harness.ExchangeAsync(Operation());

        Assert.AreEqual("d-approval-expired", run.Result.Diagnostic, limit);
        Assert.AreEqual(ReviewedHelperRefusalReason.ApprovalExpired, Read(run).Reviewed.RefusalReason);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    public void OnlyTheWholePcRouteGetsTheLongerExecutionBudgetOnBothEnds()
    {
        TimeSpan expected = TimeSpan.FromMinutes(27);
        Assert.AreEqual(expected, ReviewedHelperBudgets.WinGetMachineRouteExecution);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.FromMinutes(1) + TimeSpan.FromMinutes(10) + TimeSpan.FromMinutes(15), expected,
            "source query, H7b download and upgrade at their own budgets");
        Assert.AreEqual(expected, ReviewedHelperEndpointBudgets.Production.ForRoute(UpdateRouteContract.WinGetMachineSilentV1).Execution);
        Assert.AreEqual(expected, ReviewedHelperBudgets.Production.ForRoute(UpdateRouteContract.WinGetMachineSilentV1).Execution);
        foreach (UpdateRouteContract route in new[] { UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, UpdateRouteContract.ChocolateyClientSelfUpdateV1 })
        {
            Assert.AreEqual(TimeSpan.FromMinutes(11), ReviewedHelperEndpointBudgets.Production.ForRoute(route).Execution, route.ToString());
            Assert.AreEqual(TimeSpan.FromMinutes(11), ReviewedHelperBudgets.Production.ForRoute(route).Execution, route.ToString());
        }
        ReviewedHelperEndpointBudgets fixture = ReviewedHelperEndpointBudgets.Production with { WinGetMachineExecution = null };
        Assert.AreEqual(TimeSpan.FromMinutes(11), fixture.ForRoute(UpdateRouteContract.WinGetMachineSilentV1).Execution, "no route budget, no change");
    }

    [TestMethod]
    public async Task ARealAllowListMismatchIsRequestNotAdmittedInTheWinGetShape()
    {
        EndpointHarness harness = WinGetHarness();
        harness.Capability.Status = HelperProtocolCapabilityStatus.Mismatch;
        EndpointRun run = await harness.ExchangeAsync(Operation());

        FramedUpdateResponse response = Read(run);
        Assert.AreEqual(ReviewedHelperRefusalReason.RequestNotAdmitted, response.Reviewed.RefusalReason);
        StringAssert.StartsWith(response.Diagnostic, "a3-");
        Assert.IsFalse(harness.Item.Calls.Contains("observe"), "stage B never ran");
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    public async Task ChocolateyValuesForTheWinGetRouteAreAnInternalFaultAndNeverLaunch()
    {
        // The default fake returns the Chocolatey package's installed value and fingerprint, not the frozen WinGet ones.
        EndpointHarness harness = new();
        EndpointRun run = await harness.ExchangeAsync(Operation());

        FramedUpdateResponse response = Read(run);
        Assert.AreNotEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification, "never matched");
        Assert.AreEqual("b-observed-inconsistent", response.Diagnostic);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    public async Task AStageBRegistrationRefusalIsInstalledMismatchInTheWinGetShape()
    {
        EndpointHarness harness = WinGetHarness();
        harness.Item.Observe = (_, _, _, _) => new ChocolateyStageValues.Refused(ChocolateyStageRefusal.InstalledMismatch, null, null);
        EndpointRun run = await harness.ExchangeAsync(Operation());

        FramedUpdateResponse response = Read(run);
        Assert.AreEqual(ReviewedHelperQualification.Refused, response.Reviewed.Qualification);
        Assert.AreEqual(ReviewedHelperRefusalReason.InstalledMismatch, response.Reviewed.RefusalReason);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    [TestMethod]
    public async Task AStageCSourceRefusalIsPreflightQueryFailedInTheWinGetShape()
    {
        EndpointHarness harness = WinGetHarness();
        harness.Item.Query = (_, _) => Task.FromResult<ChocolateyCandidateValues>(
            new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.PreflightQueryFailed, null, null, Registration, false));
        EndpointRun run = await harness.ExchangeAsync(Operation());

        FramedUpdateResponse response = Read(run);
        Assert.AreEqual(ReviewedHelperRefusalReason.PreflightQueryFailed, response.Reviewed.RefusalReason);
        Assert.AreEqual(Registration, response.Reviewed.ObservedProfileFingerprint);
        Assert.AreEqual(0, harness.Item.UpgradeCalls);
    }

    private static EndpointHarness WinGetHarness()
    {
        EndpointHarness harness = new();
        harness.Item.Observe = (_, _, _, _) => ChocolateyStageValues.Observed.ForWinGetMachine(Installed, Registration);
        harness.Item.Query = (_, _) => Task.FromResult<ChocolateyCandidateValues>(ChocolateyCandidateValues.Matched.ForWinGetMachine(Candidate));
        harness.Item.Revalidate = (_, _) => ChocolateyStageValues.Observed.ForWinGetMachine(Installed, Registration);
        return harness;
    }

    private static FramedUpdateResponse Read(EndpointRun run)
    {
        Assert.AreEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, run.Exchange!.Outcome, run.Result.Diagnostic);
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(run.Exchange.ResponseFrame, run.Operation.JobId, run.Operation.Digest, F.Nonce,
            ReviewedHelperReplyShape.WinGetMachine, out FramedUpdateResponse? response), run.Result.Diagnostic);
        return response!;
    }
}
