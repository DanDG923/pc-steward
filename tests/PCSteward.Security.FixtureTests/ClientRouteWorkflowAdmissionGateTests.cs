using System.Text;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.EndpointFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, AC-UPD-07; ADR-031 design §8.6 admission gate 1 (portable): the full
/// <c>ChocolateyClientSelfUpdateV1</c> workflow — review, confirm, fresh F′ evidence, durable claim, one-use permit, dispatch,
/// finalization with the store's settlement invariants, then history — through the real
/// <see cref="ReviewedUpdateDispatchWorkflow"/>, the real F′ producer and client verifier (fixture release list and synthetic
/// client-mode observations), the real helper endpoint over a real pipe pair (fake item), the real codec and the real
/// protocol-2 interpreter. Since the Task 50 activation (ADR-031 owner decision 7, step C-A) the production review gate
/// admits the exact client shape, so the gate runs without any test scope (the former test-only admission seam is removed);
/// every near miss of the shape is still refused before any claim. Owner conditions (ADR-031 decisions 4 and 5): only None with the exact target postcheck
/// is Success; each watchdog stop, the incomplete release and both combined are "not verified" with every recovery warning in
/// history; a lost launch is Uncertain with the untrusted-folder caution; a missing or invalid field is never Success.
/// The store here is in memory (<see cref="GateStore"/>, mirroring the durable invariants) and history is presented from its
/// stored payload; the SQLite claim, canonical round trip, <c>RecordFinalAsync</c> and SQLite history reader are exercised for
/// this route by <c>ClientRouteSqliteAdmissionGateTests</c> (Application integration tests).
/// </summary>
[TestClass]
public sealed class ClientRouteWorkflowAdmissionGateTests
{
    private static readonly DateTimeOffset ReviewedAt = Protocol2InterpreterFixture.ReviewedAt;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private static readonly string[] StageOrder = ["protect", "observe", "query", "revalidate", "upgrade"];

    [TestMethod]
    public void ActivationAdmitsTheExactClientShapeAndACancelledReviewIsStillNeverConfirmed()
    {
        // FR-UPD-15, ADR-031 owner decision 7 (Task 50 step C-A): the production review gate admits the exact client shape.
        Gate gate = new();
        ReviewedUpdateDraft draft = new(gate.Operation, Guid.NewGuid());
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(gate.Operation), "activation: the exact client self-update shape is admitted");

        // Admission is shape only: a cancelled review is never confirmed, so nothing is claimed and the helper is never reached.
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.ThrowsExactly<InvalidOperationException>(() => new ReviewedUpdateReview(draft, gate.Clock, cancelled.Token).Confirm());
        Assert.IsNull(gate.Store.State);
        Assert.AreEqual(0, gate.Dispatcher.Calls);
        Assert.AreEqual(0, gate.EvidenceReads + gate.VerifyReads);
        // The fixture package-route operation (the runtime-limit route's exact shape) is admitted too; nothing else is.
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(F.Operation()), "activation: the runtime-limit package route is admitted");
    }

    [TestMethod]
    public async Task NoneWithTheExactTargetPostcheckIsVerifiedAndRecordedThroughHistory()
    {
        Gate gate = new();
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome, result.Message);
        Assert.AreEqual(gate.Recorded(ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage), result.Message);
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, result.Route);
        Assert.AreEqual(UpdateDispatchState.Final, gate.Store.State);
        Assert.AreEqual(1, gate.Dispatcher.Calls);
        Assert.AreEqual(1, gate.EvidenceReads, "one F′ observation of the source");
        Assert.AreEqual(1, gate.VerifyReads, "one verification observation of the target");
        Assert.AreEqual(1, gate.Harness.Item.UpgradeCalls);
        CollectionAssert.AreEqual(StageOrder, gate.Harness.Item.Calls);
        Assert.IsTrue(gate.Dispatcher.Observation!.ClientRunFault!.IsNone);
        Assert.IsFalse(view.BlocksOtherUpdates);
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.VerificationDetail);
        Assert.AreEqual(ChocolateyClientUpdatePresentation.HistoryRouteText, view.Route);
        // The one-use F′ baseline was consumed by the verification.
        Assert.IsFalse(gate.Baselines.TryTake(gate.Operation, out _));
    }

    [TestMethod]
    public async Task NoneWithAPostcheckThatIsNotTheTargetIsUnverified()
    {
        Gate gate = new(postUpdate: release => ChocolateyClientUpdatePostcheckTests.Observation(release.Source));
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        string expected = ChocolateyClientUpdatePresentation.NotVerifiedMessage(
            ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.NotTargetRelease).Detail);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        Assert.AreEqual(gate.Recorded(expected), result.Message);
        Assert.AreEqual(UpdateDispatchState.Final, gate.Store.State);
        Assert.AreEqual(expected, view.VerificationDetail);
    }

    /// <summary>
    /// Review L4: a postcheck that returns nothing or cannot be read (the client-mode observation throws) did not finish, so
    /// the record is Unverified with the fixed "not verified" detail, never Success; the baseline was still consumed.
    /// </summary>
    [TestMethod]
    [DataRow("null")]
    [DataRow("unreadable")]
    public async Task AMissingOrUnreadablePostcheckIsUnverifiedNeverSuccess(string postcheck)
    {
        Gate gate = new(postUpdate: _ => postcheck == "null" ? null : throw new IOException("synthetic unreadable client state"));
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        string expected = ChocolateyClientUpdatePresentation.NotVerifiedMessage(
            ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.NotFinished).Detail);
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        Assert.AreEqual(gate.Recorded(expected), result.Message);
        Assert.AreEqual(1, gate.VerifyReads, "the postcheck was attempted once");
        Assert.IsFalse(gate.Baselines.TryTake(gate.Operation, out _), "the one-use baseline was consumed");
        Assert.AreEqual(expected, view.VerificationDetail);
        Assert.AreNotEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.VerificationDetail);
    }

    /// <summary>
    /// Review L4: a restart-pending verification (even beside the exact target) is never Success or RestartRequired on the
    /// client route; the workflow records Unverified with the fixed restart-contract text.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ARestartPendingVerificationIsUnverifiedNeverSuccess(bool exactTarget)
    {
        Gate gate = new(postUpdate: exactTarget ? null : release => ChocolateyClientUpdatePostcheckTests.Observation(release.Source),
            reportRestartPending: true);
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        if (exactTarget) Assert.AreEqual(gate.Recorded(ReviewedUpdateDispatchWorkflow.RestartContractViolationMessage), result.Message);
        Assert.AreEqual(1, gate.VerifyReads);
        Assert.AreNotEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, view.VerificationDetail);
        Assert.IsFalse(view.BlocksOtherUpdates);
    }

    /// <summary>
    /// Review L5: inside the test scope, <c>ReviewedRouteTestAdmission.Admits</c> still refuses every near miss of the client
    /// shape, one conjunct at a time. The operation constructor already refuses each variant on the client route, so the
    /// variant is built on the unrestricted registration route and only its frozen route is then forged to the client route
    /// (reflection, test only) to reach the seam's own conjunct. The unmodified shape forged the same way is admitted.
    /// </summary>
    [TestMethod]
    [DataRow("control")]
    [DataRow("user-scope")]
    [DataRow("other-package")]
    [DataRow("observed-architecture")]
    [DataRow("other-mode")]
    [DataRow("other-source")]
    public void TheScopeRefusesEveryNearMissOfTheClientShape(string variant)
    {
        string package = variant == "other-package" ? "chocolatey.extension" : ReviewedUpdateOperation.ChocolateyClientPackageId;
        UpdateSource source = variant == "other-source" ? UpdateSource.WinGet : UpdateSource.ChocolateyCommunity;
        ReviewedUpdateArchitecture architecture = variant == "observed-architecture"
            ? ReviewedUpdateArchitecture.Observed(UpdateArchitecture.X64) : ReviewedUpdateArchitecture.ProviderScriptSelected();
        ReviewedInstallerMode mode = variant == "other-mode"
            ? ReviewedInstallerMode.Selected(InstallerMode.Silent) : ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled);
        UpdateJobSelection selection = new("choco:chocolatey", UpdateProvider.Chocolatey, source, package, "2.7.5", "2.8.0", UpdateScope.Machine,
            architecture.Value ?? UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2");
        // The selection itself refuses a Chocolatey user scope, so that conjunct is reached only by forging the frozen scope.
        if (variant == "user-scope") Forge(selection, "<Scope>k__BackingField", UpdateScope.User);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(ReviewedAt);
        string evidence = ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64);
        if (variant != "control")
            Assert.ThrowsExactly<ArgumentException>(() => new ReviewedUpdateOperation(job, Protocol2InterpreterFixture.User, "choco:chocolatey",
                "machine:choco", evidence, ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyClientSelfUpdateV1, mode, architecture),
                "the constructor refuses the variant on the client route");
        ReviewedUpdateOperation forged = new(job, Protocol2InterpreterFixture.User, "choco:chocolatey", "machine:choco", evidence,
            ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyMachineRegistrationV3, mode, architecture);
        Forge(forged, "<RouteContract>k__BackingField", UpdateRouteContract.ChocolateyClientSelfUpdateV1);
        Assert.AreEqual(variant == "user-scope" ? UpdateScope.User : UpdateScope.Machine, forged.Selection.Scope);
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, forged.RouteContract);

        // The production review gate (Task 50 activation) admits only the exact client shape: each near miss is refused.
        Assert.AreEqual(variant == "control", ReviewedChocolateyRouteAdmission.Admits(forged), variant);
        Assert.AreEqual(variant == "control", ReviewedUpdateReview.HasSupportedMode(forged), variant);
    }

    private static void Forge<T>(T target, string field, object value) where T : class =>
        typeof(T).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);

    [TestMethod]
    [DataRow((int)ChocolateyClientWatchdogFault.SetupOverrideDuringRun, false, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.RootWritableDuringRun, false, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.WatchFailedDuringRun, false, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.None, true, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.SetupOverrideDuringRun, true, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.RootWritableDuringRun, true, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.WatchFailedDuringRun, true, 0)]
    [DataRow((int)ChocolateyClientWatchdogFault.SetupOverrideDuringRun, false, 1)]
    [DataRow((int)ChocolateyClientWatchdogFault.RootWritableDuringRun, true, 1)]
    [DataRow((int)ChocolateyClientWatchdogFault.None, true, 1)]
    public async Task EveryRunFaultIsNotVerifiedWithItsRecoveryWarningInHistoryAndNeverVerifies(int watchdog, bool releaseIncomplete, int exit)
    {
        ChocolateyClientWatchdogFault fault = (ChocolateyClientWatchdogFault)watchdog;
        Gate gate = new();
        gate.Harness.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(
            new ChocolateyUpgradeResult.Observed(F.Started(exit), fault, releaseIncomplete));
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        string expected = ExpectedMessage(fault, releaseIncomplete);
        // Owner decision 5: a started run the watchdog stopped (or whose holds were not all released) is "not verified", never
        // Success and never Failed (exit 1 would otherwise be Failed).
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        Assert.AreEqual(gate.Recorded(expected), result.Message);
        Assert.AreEqual(UpdateDispatchState.Final, gate.Store.State);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, gate.Store.Terminal!.Outcome);
        Assert.AreEqual(1, gate.EvidenceReads);
        Assert.AreEqual(0, gate.VerifyReads, "a reported fault is never verified");
        Assert.IsTrue(gate.Baselines.TryTake(gate.Operation, out _), "verification never consumed the baseline");
        Assert.AreEqual(expected, view.VerificationDetail, "history shows the fixed recovery text");
        AssertRecoveryText(fault, releaseIncomplete, view.VerificationDetail!);
    }

    [TestMethod]
    public async Task ALaunchTheHelperLostTrackOfIsUncertainWithTheUntrustedFolderCaution()
    {
        // Independent review M1: the item's upgrade threw after process creation was entered ("launch-unknown"), so the helper
        // replies Unknown with run fault None; and a started launch whose settlement the helper could not confirm.
        foreach (Func<TimeSpan, CancellationToken, Task<ChocolateyUpgradeResult>> upgrade in new Func<TimeSpan, CancellationToken, Task<ChocolateyUpgradeResult>>[]
                 {
                     (_, _) => Task.FromException<ChocolateyUpgradeResult>(new InvalidOperationException("synthetic")),
                     (_, _) => Task.FromResult<ChocolateyUpgradeResult>(new ChocolateyUpgradeResult.Observed(F.Started(0, settled: false))),
                 })
        {
            Gate gate = new();
            gate.Harness.Item.Upgrade = upgrade;
            (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();
            AssertLostTrack(gate, result, view);
            Assert.IsTrue(gate.Dispatcher.Observation!.ClientRunFault!.IsNone, "an accepted reply carrying None");
        }
    }

    [TestMethod]
    public async Task AFaultReportedForAnUnknownLaunchKeepsItsOwnWarningOnTheUncertainRecord()
    {
        Gate gate = new();
        gate.Harness.Item.Upgrade = (_, _) => Task.FromResult<ChocolateyUpgradeResult>(
            new ChocolateyUpgradeResult.Observed(F.UnknownLaunch(), ChocolateyClientWatchdogFault.RootWritableDuringRun, true));
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Uncertain, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Uncertain, result.Outcome);
        Assert.AreEqual(UpdateDispatchState.Uncertain, gate.Store.State);
        StringAssert.EndsWith(result.Message, " " + ChocolateyClientUpdatePresentation.StoppedRootWritableReleaseIncompleteMessage);
        Assert.IsFalse(result.Message.Contains(ChocolateyClientUpdatePresentation.LostTrackCautionMessage, StringComparison.Ordinal));
        Assert.IsTrue(view.BlocksOtherUpdates, "the barrier is kept");
        StringAssert.EndsWith(view.Diagnostic, ChocolateyClientUpdatePresentation.StoppedRootWritableReleaseIncompleteMessage);
        Assert.AreEqual(0, gate.VerifyReads);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unknown-value")]
    [DataRow("lowercase")]
    [DataRow("invalid-combination")]
    public async Task AMissingOrInvalidFieldOnTheWireIsUncertainAndNeverSuccess(string tamper)
    {
        Gate gate = new(tamper: frame =>
        {
            string text = Encoding.UTF8.GetString(frame);
            const string None = "\"clientRunFault\":\"None\"";
            StringAssert.Contains(text, None);
            return Encoding.UTF8.GetBytes(tamper switch
            {
                "missing" => text.Replace("," + None, string.Empty, StringComparison.Ordinal),
                "unknown-value" => text.Replace(None, "\"clientRunFault\":\"Bogus\"", StringComparison.Ordinal),
                "lowercase" => text.Replace(None, "\"clientRunFault\":\"none\"", StringComparison.Ordinal),
                _ => text.Replace(None, "\"clientRunFault\":\"SetupOverrideDuringRun+WatchFailedDuringRun\"", StringComparison.Ordinal),
            });
        });
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        Assert.AreEqual(ReviewedUpdateProtocol2Interpreter.ResponseRejectedDiagnostic, gate.Dispatcher.Observation!.Diagnostic);
        Assert.IsNull(gate.Dispatcher.Observation.ClientRunFault, "a rejected reply carries no run fault");
        AssertLostTrack(gate, result, view);
    }

    [TestMethod]
    public async Task AHelperRefusalFinalisesAsRefusedWithoutAWarning()
    {
        Gate gate = new();
        gate.Harness.Item.Observe = (_, _, _, _) => new ChocolateyStageValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, null, null);
        (ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view) = await gate.RunAsync();

        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Finalized, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Refused, result.Outcome, result.Message);
        Assert.AreEqual(UpdateDispatchState.Final, gate.Store.State);
        Assert.AreEqual(0, gate.Harness.Item.UpgradeCalls);
        Assert.IsFalse(result.Message.Contains(ChocolateyClientUpdatePresentation.LostTrackCautionMessage, StringComparison.Ordinal));
        Assert.IsFalse(view.BlocksOtherUpdates);
    }

    private static void AssertLostTrack(Gate gate, ReviewedUpdateDispatchResult result, ReviewedUpdateHistoryView view)
    {
        string caution = ChocolateyClientUpdatePresentation.LostTrackCautionMessage;
        Assert.AreEqual(ReviewedUpdateDispatchDisposition.Uncertain, result.Disposition, result.Message);
        Assert.AreEqual(UpdateDispatchOutcome.Uncertain, result.Outcome, result.Message);
        Assert.AreEqual(UpdateDispatchState.Uncertain, gate.Store.State, "the barrier stays until reconciliation");
        StringAssert.EndsWith(result.Message, " " + caution);
        Assert.IsFalse(result.Message.Contains("choco upgrade", StringComparison.Ordinal) || result.Message.Contains("--force", StringComparison.Ordinal));
        Assert.AreEqual(1, gate.EvidenceReads);
        Assert.AreEqual(0, gate.VerifyReads, "never verified");
        Assert.IsTrue(view.BlocksOtherUpdates);
        Assert.AreEqual(caution, view.VerificationDetail);
        StringAssert.StartsWith(view.Status, "Uncertain:");
    }

    private static string ExpectedMessage(ChocolateyClientWatchdogFault fault, bool releaseIncomplete) => (fault, releaseIncomplete) switch
    {
        (ChocolateyClientWatchdogFault.SetupOverrideDuringRun, false) => ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage,
        (ChocolateyClientWatchdogFault.RootWritableDuringRun, false) => ChocolateyClientUpdatePresentation.StoppedRootWritableMessage,
        (ChocolateyClientWatchdogFault.WatchFailedDuringRun, false) => ChocolateyClientUpdatePresentation.StoppedWatchFailedMessage,
        (ChocolateyClientWatchdogFault.None, true) => ChocolateyClientUpdatePresentation.NotVerifiedReleaseIncompleteMessage,
        (ChocolateyClientWatchdogFault.SetupOverrideDuringRun, true) => ChocolateyClientUpdatePresentation.StoppedSetupOverrideReleaseIncompleteMessage,
        (ChocolateyClientWatchdogFault.RootWritableDuringRun, true) => ChocolateyClientUpdatePresentation.StoppedRootWritableReleaseIncompleteMessage,
        (ChocolateyClientWatchdogFault.WatchFailedDuringRun, true) => ChocolateyClientUpdatePresentation.StoppedWatchFailedReleaseIncompleteMessage,
        _ => throw new ArgumentOutOfRangeException(nameof(fault)),
    };

    /// <summary>Owner decision 4: every required recovery warning is retained, and untrusted-folder advice is never replaced by a command.</summary>
    internal static void AssertRecoveryText(ChocolateyClientWatchdogFault fault, bool releaseIncomplete, string message)
    {
        if (fault is ChocolateyClientWatchdogFault.SetupOverrideDuringRun or ChocolateyClientWatchdogFault.RootWritableDuringRun)
        {
            StringAssert.Contains(message, ChocolateyClientUpdatePresentation.UntrustedFolderRecoveryStep);
            Assert.IsFalse(message.Contains("choco upgrade", StringComparison.Ordinal) || message.Contains("--force", StringComparison.Ordinal), message);
        }
        if (fault == ChocolateyClientWatchdogFault.WatchFailedDuringRun) StringAssert.Contains(message, ChocolateyClientUpdatePresentation.WatchdogRecoveryStep);
        if (releaseIncomplete) StringAssert.EndsWith(message, ChocolateyClientUpdatePresentation.StaysUnavailableUntilReviewedText);
        if (releaseIncomplete && fault != ChocolateyClientWatchdogFault.None) StringAssert.Contains(message, ChocolateyClientUpdatePresentation.ReleaseIncompleteAlsoNote);
        if (!releaseIncomplete) Assert.IsFalse(message.Contains(ChocolateyClientUpdatePresentation.ReleaseIncompleteAlsoNote, StringComparison.Ordinal));
        Assert.IsFalse(message.StartsWith("Installed", StringComparison.Ordinal), message);
    }

    /// <summary>
    /// One gate run: the three-release fixture list (source the oldest, target the newest), the client operation frozen with the
    /// source observation's fingerprint, the real F′ producer and client verifier sharing one baseline store, an endpoint
    /// harness whose fake item reports the frozen values, and the in-memory store.
    /// </summary>
    private sealed class Gate
    {
        internal Gate(Func<(ChocolateyReviewedRelease Source, ChocolateyReviewedRelease Target), ChocolateyProfileResult?>? postUpdate = null,
            Func<byte[], byte[]>? tamper = null, bool reportRestartPending = false)
        {
            this.reportRestartPending = reportRestartPending;
            (List, Source, Target) = ChocolateyClientUpdatePostcheckTests.Releases();
            string fingerprint = ChocolateyClientUpdatePostcheckTests.Observation(Source).Observation.Fingerprint;
            Operation = ClientOperation(Source.Version, Target.Version, fingerprint);
            Harness.Item.Observe = (_, _, _, _) => new ChocolateyStageValues.Observed(Source.Version, fingerprint);
            Harness.Item.Revalidate = (_, _) => new ChocolateyStageValues.Observed(Source.Version, fingerprint);
            Harness.Item.Query = (_, _) => Task.FromResult<ChocolateyCandidateValues>(new ChocolateyCandidateValues.Matched(Target.Version));
            Evidence = ChocolateyProfileEvidenceSource.CreateClientForTests((id, installed, _, _) =>
            {
                Interlocked.Increment(ref evidenceReads);
                return id == ReviewedUpdateOperation.ChocolateyClientPackageId && installed.Normalized == Source.Version
                    ? ChocolateyClientUpdatePostcheckTests.Observation(Source) : null;
            }, List, Baselines, Clock, Budget);
            Verifier = ChocolateyReviewedUpdateVerifier.CreateClientForTests((id, expected, _, _) =>
            {
                Interlocked.Increment(ref verifyReads);
                return id == ReviewedUpdateOperation.ChocolateyClientPackageId && expected.Normalized == Target.Version
                    ? postUpdate is null ? ChocolateyClientUpdatePostcheckTests.Observation(Target) : postUpdate((Source, Target)) : null;
            }, List, Baselines, TimeProvider.System, Budget);
            Dispatcher = new(Harness, tamper);
        }

        private readonly bool reportRestartPending;
        private int evidenceReads;
        private int verifyReads;

        internal ChocolateyReleaseList List { get; }
        internal ChocolateyReviewedRelease Source { get; }
        internal ChocolateyReviewedRelease Target { get; }
        internal ReviewedUpdateOperation Operation { get; }
        internal ChocolateyDispatchBaselines Baselines { get; } = new();
        internal FixedClock Clock { get; } = new(ReviewedAt.AddMinutes(1));
        internal EndpointHarness Harness { get; } = new();
        internal ChocolateyProfileEvidenceSource Evidence { get; }
        internal ChocolateyReviewedUpdateVerifier Verifier { get; }
        internal EndpointDispatcher Dispatcher { get; }
        internal GateStore Store { get; } = new();

        /// <summary>Client-mode observations read by the F′ producer (before the claim).</summary>
        internal int EvidenceReads => Volatile.Read(ref evidenceReads);

        /// <summary>Client-mode observations read by the verifier (after positive settlement only).</summary>
        internal int VerifyReads => Volatile.Read(ref verifyReads);

        internal ReviewedUpdateDispatchWorkflow Workflow() =>
            new((work, _, ct) => work(Owner(Operation.RequestingUserSid), ct), Evidence, Dispatcher,
                reportRestartPending ? new RestartPendingVerifier(Verifier) : Verifier, Store, Clock);

        /// <summary>
        /// The workflow's recorded form of <paramref name="message"/>: the message itself. Independent review M1: the interpreter
        /// derives no helper candidate check for the client route (the recorded check is defined for the package route only), so
        /// the workflow never drops one and never prefixes the
        /// <see cref="ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic"/> marker on a client record.
        /// </summary>
        internal string Recorded(string message)
        {
            Assert.IsFalse(Dispatcher.Observation?.HelperCheck is { Kind: not RecordedHelperCheckKind.NotObserved },
                "the client route never carries a helper candidate check");
            return message;
        }

        /// <summary>Review, confirm and run (the route is admitted in production), then present the persisted record as history.</summary>
        internal async Task<(ReviewedUpdateDispatchResult, ReviewedUpdateHistoryView)> RunAsync()
        {
            ReviewedUpdateDispatchResult result =
                await Workflow().RunAsync(new ReviewedUpdateReview(new ReviewedUpdateDraft(Operation, Guid.NewGuid()), Clock).Confirm(),
                    CancellationToken.None);
            Assert.IsNotNull(Store.Claim, result.Message);
            Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(Store.Payload, null, out ReviewedUpdateTerminalRecord? terminal), "the persisted terminal format");
            Assert.AreEqual(result.Outcome, terminal!.Outcome);
            Assert.AreEqual(result.Message, terminal.Diagnostic, "live text equals the recorded text");
            Assert.IsNull(result.HelperCheck, "no helper check is ever recorded for the client route");
            Assert.AreSame(RecordedHelperCandidateCheck.NotObserved, Store.Terminal!.HelperCheck);
            Assert.IsFalse(Dispatcher.Observation?.HelperCheck is { Kind: not RecordedHelperCheckKind.NotObserved }, "no helper check reaches the workflow");
            Assert.IsFalse(result.Message.Contains(ReviewedUpdateDispatchWorkflow.HelperCheckNotRecordedDiagnostic, StringComparison.Ordinal),
                "review M1: a client record never carries the helper-check-not-recorded marker: " + result.Message);
            ReviewedUpdateHistoryView view = ReviewedUpdateHistoryPresentation.Present(new ReviewedUpdateHistoryRecord(Operation.JobId, Operation,
                Store.State!.Value, Store.Claim!.ClaimedAt, terminal, true));
            Assert.AreEqual(Store.State != UpdateDispatchState.Final, view.BlocksOtherUpdates);
            return (result, view);
        }
    }

    internal static ReviewedUpdateOperation ClientOperation(string installed, string target, string fingerprint, string? user = null)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:chocolatey", UpdateProvider.Chocolatey,
            UpdateSource.ChocolateyCommunity, ReviewedUpdateOperation.ChocolateyClientPackageId, installed, target, UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(ReviewedAt);
        return new(job, user ?? Protocol2InterpreterFixture.User, "choco:chocolatey", "machine:choco", ReviewedUpdateOperation.ProfileEvidencePrefix + fingerprint,
            ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyClientSelfUpdateV1,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    internal static OperationOwnership Owner(string user) => new(user, OperationGateContract.NamePrefix + user, false, reconciled: true);

    /// <summary>
    /// The client route's dispatcher for the gate: the compiled route check (client route only), then one real desktop exchange
    /// with the in-process helper endpoint and the real interpreter (optionally over a tampered response frame).
    /// </summary>
    internal sealed class EndpointDispatcher(EndpointHarness harness, Func<byte[], byte[]>? tamper = null) : IReviewedUpdateRouteDispatcher
    {
        internal int Calls;
        internal EndpointRun? Run;
        internal ReviewedUpdateDispatchObservation? Observation;

        public bool Supports(ReviewedUpdateOperation operation) => operation.RouteContract == UpdateRouteContract.ChocolateyClientSelfUpdateV1;

        public async Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Run = await harness.ExchangeAsync(ticket.Operation);
            ReviewedHelperExchange exchange = Run.Exchange!;
            ReviewedUpdateProtocol2Interpretation interpretation = tamper is null ? Run.Interpretation
                : ReviewedUpdateProtocol2Interpreter.Interpret(ticket.Operation, exchange.Outcome, tamper(exchange.ResponseFrame), F.Nonce, exchange.Desktop);
            Observation = interpretation.WorkflowObservation;
            return Observation;
        }
    }

    /// <summary>The in-memory store with the durable store's invariants: one claim, one settlement consumption, outcome/settlement agreement.</summary>
    internal sealed class GateStore : IUpdateDispatchStore
    {
        internal UpdateDispatchState? State;
        internal UpdateDispatchTerminal? Terminal;
        internal UpdateDispatchClaim? Claim;
        internal byte[] Payload = [];

        public Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership,
            DateTimeOffset claimedAt, CancellationToken cancellationToken)
        {
            if (State is not null) return Task.FromResult<UpdateDispatchClaim?>(null);
            State = UpdateDispatchState.DispatchClaimed;
            Claim = new(Guid.NewGuid(), Guid.NewGuid(), draft, evidence, claimedAt);
            return Task.FromResult<UpdateDispatchClaim?>(Claim);
        }

        public Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal,
            UpdateDispatchSettlement settlement, CancellationToken cancellationToken)
        {
            if (State != UpdateDispatchState.DispatchClaimed || !ReferenceEquals(claim, Claim) || !settlement.TryConsumeFor(claim, ownership) ||
                (terminal.Outcome == UpdateDispatchOutcome.Uncertain) == settlement.ProvesSettlement)
                throw new InvalidOperationException("Settlement does not match the terminal outcome.");
            State = terminal.Outcome == UpdateDispatchOutcome.Uncertain ? UpdateDispatchState.Uncertain : UpdateDispatchState.Final;
            Terminal = terminal;
            Payload = terminal.GetReservedPayload(settlement);
            return Task.CompletedTask;
        }

        public Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult(State);
        public Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken) =>
            Task.FromResult(State is UpdateDispatchState.DispatchClaimed or UpdateDispatchState.Uncertain);
    }

    /// <summary>
    /// Review L4: the real client verifier never reports restart-pending, so this wrapper runs it unchanged and then marks its
    /// result restart-pending, to show the workflow's guard (a client self-update is never Success or RestartRequired on a
    /// restart-pending result).
    /// </summary>
    private sealed class RestartPendingVerifier(ChocolateyReviewedUpdateVerifier inner) : IReviewedUpdateVerifier, IReviewedClientUpdateVerifier
    {
        public async Task<PostUpdateInventoryResult> VerifyAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
            CancellationToken cancellationToken) =>
            (await inner.VerifyAsync(operation, ownership, cancellationToken)) with { RestartPending = true };

        public async Task<PostUpdateInventoryResult> VerifyClientAsync(ReviewedUpdateOperation operation, OperationOwnership ownership,
            ReviewedClientRunFault runFault, CancellationToken cancellationToken) =>
            (await inner.VerifyClientAsync(operation, ownership, runFault, cancellationToken)) with { RestartPending = true };
    }

    /// <summary>A wall clock fixed at one instant (monotonic time and timers stay real).</summary>
    internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
