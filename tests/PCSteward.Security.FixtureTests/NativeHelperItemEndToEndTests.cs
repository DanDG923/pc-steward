using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;
using Reason = PCSteward.Execution.ReviewedHelperRefusalReason;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §3, §4 (L6), §5.2, §5.4 and §8 N4b (Windows-only; reported as
/// failed elsewhere): the native item end to end through the real stage machine, the real desktop exchange session and the
/// real protocol-2 interpreter, over an N2/N3 fixture profile whose root engine is a stand-in (a copy of <c>cmd.exe</c>, which
/// ignores the fixed Chocolatey arguments and exits at the end of its NUL input). The item runs the native Helper-role
/// observation at stages B and D (through the N2 fixture seam), its own identity comparison, and the held-binding launches.
/// The <c>cmd.exe</c> stand-in prints no Chocolatey records, so the native stage-C queries end in a settled refusal, and the
/// tests that need a match replace only the two stage-C queries with a fixed <c>Matched</c>
/// (<see cref="NativeChocolateyHelperItem.CreateOverFixtureForTests"/>). N4b review M1: one test instead uses
/// <see cref="StandInChocolateyEngine"/>, a minimal console image built in the fixture tree that prints the target's
/// <c>list</c> and <c>outdated</c> records, so the whole native chain (real parse, Map, Matched, stage D, launch) runs.
/// The evaluation token is the derived requester token (CI has no split token; linked-token acceptance is owner-host
/// evidence). No real Chocolatey client runs and nothing is elevated.
/// </summary>
[TestClass]
public sealed class NativeHelperItemEndToEndTests
{
    private const string Installed = "1.2.0";
    private const string Candidate = "1.3.0";
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    [TestMethod]
    public async Task BThroughLaunchReachesMatchedStartedThroughTheHeldBindingAndReleasesEverythingAfterwards()
    {
        using ProfileFixture fixture = new();
        EndpointHarness harness = fixture.Harness(() => new ChocolateyCandidateValues.Matched(Candidate));

        EndpointRun run = await harness.ExchangeAsync(fixture.Operation);

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification, run.Result.Diagnostic);
        Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation, "the stand-in ran from the held stage-B binding");
        Assert.IsTrue(response.Facts.DirectProcessExitObserved && response.Facts.AggregateSettlementConfirmed);
        Assert.AreEqual(Installed, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(Candidate, response.Reviewed.ObservedCandidateVersion);
        Assert.AreEqual(fixture.Fingerprint, response.Reviewed.ObservedProfileFingerprint);
        Assert.IsFalse(run.Interpretation.Observation.ExecutionUncertain, "the desktop accepts the exchange");
        // The endpoint disposed the item: every launch settled, so the engine and release holds are gone.
        fixture.AssertWritable("choco.exe");
        fixture.AssertWritable(@"tools\7z.exe");
        fixture.AssertWritable(@"hooks\pre-install-all.ps1");
    }

    [TestMethod]
    public async Task TheRealNativeChainParsesRealRecordsMatchesRevalidatesAndLaunchesThroughTheHeldBinding()
    {
        // N4b review M1: no stage is replaced. Stage C runs list --exact and outdated as real child processes of the held
        // stage-B engine (under the stage-B read-share holds) and parses their real --limit-output records; stage D re-observes
        // in full; the launch runs the same held engine.
        using ProfileFixture fixture = new(printsRecords: true);
        EndpointHarness harness = fixture.Harness(stageC: null);

        EndpointRun run = await harness.ExchangeAsync(fixture.Operation);

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification, run.Result.Diagnostic);
        Assert.AreEqual("launch-started", run.Result.Diagnostic);
        Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation, "the stand-in ran from the held stage-B binding");
        Assert.AreEqual(0, response.Facts.ExitCode);
        Assert.IsTrue(response.Facts.DirectProcessExitObserved && response.Facts.AggregateSettlementConfirmed);
        Assert.AreEqual(Installed, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(Candidate, response.Reviewed.ObservedCandidateVersion, "the candidate came from the parsed outdated record");
        Assert.AreEqual(fixture.Fingerprint, response.Reviewed.ObservedProfileFingerprint);
        Assert.IsFalse(run.Interpretation.Observation.ExecutionUncertain, "the desktop accepts the exchange");
        fixture.AssertWritable("choco.exe");
        fixture.AssertWritable(@"tools\7z.exe");
        fixture.AssertWritable(@"hooks\pre-install-all.ps1");
    }

    [TestMethod]
    public async Task TheNativeStageCQueriesOverTheStandInEndInASettledRefusalAcceptedByTheDesktop()
    {
        using ProfileFixture fixture = new();
        EndpointHarness harness = fixture.Harness(stageC: null);

        EndpointRun run = await harness.ExchangeAsync(fixture.Operation);

        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Refused, response.Reviewed.Qualification, run.Result.Diagnostic);
        // The stand-in prints its banner (malformed) or nothing (no record): never an installed value, never a launch.
        CollectionAssert.Contains(new[] { Reason.PreflightQueryFailed, Reason.InstalledMismatch }, response.Reviewed.RefusalReason, run.Result.Diagnostic);
        Assert.IsNull(response.Reviewed.ObservedCandidateVersion);
        Assert.AreEqual(UpdateWireInvocation.NotStarted, response.Facts.Invocation);
        Assert.IsTrue(response.Facts.AggregateSettlementConfirmed, "both query jobs settled");
        Assert.IsFalse(run.Interpretation.Observation.ExecutionUncertain);
        fixture.AssertWritable("choco.exe");
    }

    [TestMethod]
    public async Task DriftBetweenStageBAndStageDRefusesWithoutALaunch()
    {
        using ProfileFixture fixture = new();
        // During stage C a new extension file appears: the full stage-D re-observation no longer matches stage B.
        EndpointHarness harness = fixture.Harness(() =>
        {
            File.WriteAllText(fixture.Profile.RootPath(@"extensions\fixture\added.psm1"), "drift");
            return new ChocolateyCandidateValues.Matched(Candidate);
        });

        EndpointRun run = await harness.ExchangeAsync(fixture.Operation);

        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Refused, response.Reviewed.Qualification, run.Result.Diagnostic);
        CollectionAssert.Contains(new[] { Reason.ProfileMismatch, Reason.MachineInputsUnsupported }, response.Reviewed.RefusalReason, run.Result.Diagnostic);
        Assert.AreEqual(UpdateWireInvocation.NotStarted, response.Facts.Invocation, "no launch without a stage-D Observed");
        Assert.IsFalse(run.Interpretation.Observation.ExecutionUncertain);
    }

    [TestMethod]
    public async Task AStageBProfileMismatchCarriesTheObservedValues()
    {
        using ProfileFixture fixture = new();
        EndpointHarness harness = fixture.Harness(() => throw new AssertFailedException("stage C must not run"));

        EndpointRun run = await harness.ExchangeAsync(fixture.OperationWith(new string('A', 64)));

        FramedUpdateResponse response = run.Response;
        Assert.AreEqual(ReviewedHelperQualification.Refused, response.Reviewed.Qualification, run.Result.Diagnostic);
        Assert.AreEqual(Reason.ProfileMismatch, response.Reviewed.RefusalReason);
        Assert.AreEqual(Installed, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(fixture.Fingerprint, response.Reviewed.ObservedProfileFingerprint);
        Assert.IsFalse(run.Interpretation.Observation.ExecutionUncertain);
    }

    /// <summary>
    /// The N3 client fixture: a locked N2 profile with the <c>cmd.exe</c> stand-in engine, its Helper-role fingerprint F
    /// (observed once with a separate derived token), and a reviewed operation for the derived requester SID with F.
    /// </summary>
    private sealed class ProfileFixture : IDisposable
    {
        /// <param name="printsRecords">
        /// False: the <c>cmd.exe</c> copy (no records). True: <see cref="StandInChocolateyEngine"/>, which prints the target's
        /// installed record for <c>list</c> and its frozen candidate for <c>outdated</c>.
        /// </param>
        internal ProfileFixture(bool printsRecords = false)
        {
            N1NativeFixture.RequireWindows("native item end to end over a fixture profile");
            Profile = new N2ProfileFixture();
            try
            {
                Profile.ReleaseBinary["choco.exe"] = printsRecords
                    ? StandInChocolateyEngine.Build($"{N2ProfileFixture.Target}|{Installed}\r\n", $"{N2ProfileFixture.Target}|{Installed}|{Candidate}|false\r\n")
                    : File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
                Profile.ReleaseContent["choco.exe.manifest"] = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"></assembly>";
                Profile.Build().Lock();
                Profile.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
                using RequesterToken token = N1NativeFixture.DerivedToken();
                Sid = token.Sid;
                ChocolateyProfileResult result = ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Helper, N2ProfileFixture.Target,
                    N2ProfileFixture.Version(Installed), expectedFingerprint: null, Budget, Profile.Seam(), CancellationToken.None);
                using ChocolateyProfileObservation observation = result is ChocolateyProfileResult.Observed observed ? observed.Observation
                    : throw new AssertFailedException($"expected Observed, got {result}");
                Fingerprint = observation.Fingerprint;
                Operation = OperationWith(Fingerprint);
            }
            catch
            {
                Profile.Dispose();
                throw;
            }
        }

        internal N2ProfileFixture Profile { get; }
        internal string Sid { get; } = string.Empty;
        internal string Fingerprint { get; } = string.Empty;
        internal ReviewedUpdateOperation Operation { get; } = null!;

        internal ReviewedUpdateOperation OperationWith(string fingerprint)
        {
            ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + N2ProfileFixture.Target,
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, N2ProfileFixture.Target, Installed, Candidate, UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(Protocol2InterpreterFixture.ReviewedAt);
            return new(job, Sid, "choco:" + N2ProfileFixture.Target, "machine:choco", "choco-profile-v2:" + fingerprint,
                Protocol2InterpreterFixture.ReviewedAt.AddSeconds(-1), UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3,
                ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
        }

        /// <summary>
        /// A harness whose port hands out the native item over this fixture (a fresh derived token, owned by the item) and whose
        /// capability check is supported without reads (G1 is covered by <see cref="HelperDeploymentCapabilityCheckTests"/>).
        /// </summary>
        internal EndpointHarness Harness(Func<ChocolateyCandidateValues>? stageC)
        {
            ChocolateyObserverTestSeam seam = Profile.Seam();
            EndpointHarness harness = new() { PortOverride = new FixturePort(() => NativeChocolateyHelperItem.CreateOverFixtureForTests(N1NativeFixture.DerivedToken(),
                (token, target, frozen, fingerprint, remaining, cancellationToken) => ChocolateyProfileObserver.ObserveForTests(token,
                    ChocolateyObserverRole.Helper, target, frozen, fingerprint, remaining, seam, cancellationToken),
                ancestryBase: null, stageC)), CapabilityOverride = new SupportedCapability() };
            harness.Peer.UserSid = Sid;
            return harness;
        }

        internal void AssertWritable(string relative)
        {
            using FileStream stream = new(Profile.RootPath(relative), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(stream.CanWrite, relative);
        }

        public void Dispose() => Profile.Dispose();
    }

    private sealed class FixturePort(Func<IChocolateyHelperItem> create) : IChocolateyHelperQualificationPort
    {
        public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => throw new AssertFailedException("the stage machine never opens the peer");

        public IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal)
        {
            refusal = ChocolateyItemRefusal.None;
            return create();
        }
    }

    private sealed class SupportedCapability : IReviewedHelperCapabilityCheck
    {
        public HelperProtocolCapabilityResult Evaluate(UpdateRouteContract route, DeploymentLayoutProtectionCheck requesterCannotModify, string peerImagePath) =>
            HelperProtocolCapabilityResult.For(HelperProtocolCapabilityStatus.Supported);
    }
}
