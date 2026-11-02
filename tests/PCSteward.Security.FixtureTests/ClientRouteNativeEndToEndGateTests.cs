using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13, AC-UPD-07; ADR-031 design §8.6 admission gate 2 (Windows-only; reported as
/// failed elsewhere): the client self-update's native stages B, C, D and the launch, end to end, with no stage replaced. A
/// locked N2 fixture is listed as source 2.7.5 beside a target 2.8.0 built from prepared fixture files (a two-release
/// fixture list through the test-only <c>NativeChocolateyHelperItem.CreateClientOverFixtureForTests</c>). Its root engine is
/// <see cref="StandInClientUpgradeEngine"/>: for stage C it prints the client's own <c>chocolatey|2.7.5</c> record (real
/// parse and <c>ChocolateyClientStageCMapping</c>); for the upgrade it moves itself out of the root and the prepared target
/// engine and registration in (the rename-then-copy shape of pinned setup.psm1). The real endpoint runs the real native item
/// with its stage-B holds, the launcher's release of every hold at resume and the R5 watchdog armed over the binding's root
/// and listed directories and two test keys below <c>HKCU\Software\PCSteward.Tests\</c> (the test-only
/// <c>ChocolateyClientWatchTargets.UseEnvironmentWatchForTests</c>). Around it run the real workflow (admitted only in the
/// test scope), the real F′ producer and the real client verifier, both over Desktop-role client observations of the same
/// fixture and list. Nothing touches the real Chocolatey root, the real environment keys or a real package; nothing is
/// elevated (linked-token acceptance and the real Chocolatey client stay owner-host evidence).
/// </summary>
[TestClass]
public sealed class ClientRouteNativeEndToEndGateTests
{
    private const string Source = "2.7.5";
    private const string Target = "2.8.0";
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    [TestMethod]
    public async Task AnExactTargetAfterTheStandInUpgradeIsVerified()
    {
        using ClientFixture fixture = new(ClientFixture.Variant.Exact);
        (ReviewedUpdateDispatchResult result, ClientRouteWorkflowAdmissionGateTests.EndpointDispatcher dispatcher) = await fixture.RunAsync();

        EndpointRun run = dispatcher.Run!;
        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreEqual("launch-started", run.Result.Diagnostic);
        FramedUpdateResponse response = ReadClient(run);
        Assert.AreEqual(ReviewedHelperQualification.Matched, response.Reviewed.Qualification);
        Assert.AreEqual(Source, response.Reviewed.ObservedInstalledVersion);
        Assert.AreEqual(Target, response.Reviewed.ObservedCandidateVersion, "stage C matched the list's offer from the parsed client record");
        Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation, "the stand-in ran from the held stage-B engine");
        Assert.AreEqual(0, response.Facts.ExitCode);
        Assert.IsTrue(response.Facts.AggregateSettlementConfirmed);
        Assert.AreEqual(ReviewedClientRunFaultWire.None, response.ClientRunFault);

        Assert.AreEqual(UpdateDispatchOutcome.Success, result.Outcome, result.Message);
        StringAssert.EndsWith(result.Message, ReviewedUpdateDispatchWorkflow.TargetVerifiedMessage);
        Assert.AreEqual(UpdateDispatchState.Final, fixture.Store.State);
        Assert.AreEqual(1, fixture.VerifyReads, "the target was observed once after positive settlement");
        Assert.AreEqual(ReviewedUpdateHistoryPresentation.TargetVerifiedText, fixture.History().VerificationDetail);
        // The engine moved itself out (so it really ran after every hold was released) and the holds are gone.
        Assert.IsTrue(File.Exists(fixture.MovedOutEngine), "the running engine was renamed out of the root");
        fixture.AssertWritable("choco.exe");
        fixture.AssertWritable(@"tools\7z.exe");
        fixture.AssertRealStateUntouched();
    }

    [TestMethod]
    public async Task ATamperedTargetFileIsNotVerified()
    {
        using ClientFixture fixture = new(ClientFixture.Variant.TamperedTarget);
        (ReviewedUpdateDispatchResult result, ClientRouteWorkflowAdmissionGateTests.EndpointDispatcher dispatcher) = await fixture.RunAsync();

        Assert.AreEqual(UpdateWireInvocation.Started, ReadClient(dispatcher.Run!).Facts.Invocation, dispatcher.Run!.Result.Diagnostic);
        Assert.AreEqual(ReviewedClientRunFaultWire.None, ReadClient(dispatcher.Run!).ClientRunFault);
        string expected = ChocolateyClientUpdatePresentation.NotVerifiedMessage(
            ChocolateyClientUpdatePostcheck.ToResult(ChocolateyClientUpdateVerdict.NotTargetRelease).Detail);
        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        StringAssert.EndsWith(result.Message, expected);
        Assert.AreEqual(1, fixture.VerifyReads, "verification ran and found a file that is not the target release's");
        Assert.AreEqual(expected, fixture.History().VerificationDetail);
        fixture.AssertRealStateUntouched();
    }

    [TestMethod]
    public async Task AnOverrideNameSetInATestKeyMidRunStopsTheJobAndIsNotVerified()
    {
        using ClientFixture fixture = new(ClientFixture.Variant.LongRun);
        Task<(ReviewedUpdateDispatchResult, ClientRouteWorkflowAdmissionGateTests.EndpointDispatcher)> running = fixture.RunAsync();
        // The stand-in has moved the engine (so it runs, after every hold was released) and now sleeps; set the name.
        Stopwatch clock = Stopwatch.StartNew();
        while (!File.Exists(fixture.MovedOutEngine))
        {
            if (running.IsCompleted) Assert.Fail("the run ended before the stand-in moved its engine: " + (await running).Item1.Message);
            if (clock.Elapsed > TimeSpan.FromSeconds(45)) Assert.Fail("the stand-in did not start its upgrade");
            await Task.Delay(25);
        }
        fixture.SetUserTestValue(ChocolateySetupEnvironmentReader.AllowCurrentUserName, "1");
        (ReviewedUpdateDispatchResult result, ClientRouteWorkflowAdmissionGateTests.EndpointDispatcher dispatcher) = await running;

        FramedUpdateResponse response = ReadClient(dispatcher.Run!);
        Assert.AreEqual(UpdateWireInvocation.Started, response.Facts.Invocation, dispatcher.Run!.Result.Diagnostic);
        Assert.AreEqual((int)LaunchJob.TerminationExitCode, response.Facts.ExitCode, "the watchdog terminated the job");
        Assert.AreEqual(ReviewedClientRunFaultWire.SetupOverrideDuringRun, response.ClientRunFault);
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(50), "stopped long before the stand-in's own sleep would end");

        Assert.AreEqual(UpdateDispatchOutcome.Unverified, result.Outcome, result.Message);
        StringAssert.EndsWith(result.Message, ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage);
        Assert.AreEqual(0, fixture.VerifyReads, "a watchdog stop is never verified");
        Assert.AreEqual(ChocolateyClientUpdatePresentation.StoppedSetupOverrideMessage, fixture.History().VerificationDetail);
        ClientRouteWorkflowAdmissionGateTests.AssertRecoveryText(ChocolateyClientWatchdogFault.SetupOverrideDuringRun, false, result.Message);
        fixture.AssertRealStateUntouched();
    }

    private static FramedUpdateResponse ReadClient(EndpointRun run)
    {
        Assert.AreEqual(ReviewedHelperTransportOutcome.ResponseFrameReceived, run.Exchange!.Outcome, run.Result.Diagnostic);
        Assert.IsTrue(ReviewedUpdateWireCodec.TryReadResponse(run.Exchange.ResponseFrame, run.Operation.JobId, run.Operation.Digest, EndpointFixture.Nonce,
            ReviewedHelperReplyShape.ClientSelfUpdate, out FramedUpdateResponse? response), run.Result.Diagnostic);
        return response!;
    }

    /// <summary>
    /// A locked N2 fixture whose engine is the client stand-in, prepared target files in a staging directory outside the
    /// root (locked with the tree, so the moved-in files keep a protected DACL), the two-release fixture list built from those
    /// bytes, two test keys, the client operation frozen with the Desktop-role fingerprint of the source, and the workflow
    /// parts (real F′ producer and verifier sharing one baseline store).
    /// </summary>
    private sealed class ClientFixture : IDisposable
    {
        internal enum Variant { Exact, TamperedTarget, LongRun }

        private const string TestKeyRoot = @"Software\PCSteward.Tests";
        private readonly string keyBase = TestKeyRoot + @"\" + Guid.NewGuid().ToString("N");
        private readonly ChocolateySetupEnvironmentReading realBefore;
        private int verifyReads;

        internal ClientFixture(Variant variant)
        {
            N1NativeFixture.RequireWindows("client self-update stages B-Launch end to end over a fixture");
            realBefore = ChocolateySetupEnvironmentReader.Read();
            Profile = new N2ProfileFixture();
            try
            {
                string staging = Profile.Native.Directory("Staging");
                string prepared = Profile.Native.Directory(@"Staging\target");
                MovedOutEngine = Path.Combine(staging, "choco.exe.old");
                byte[] targetEngine = StandInClientUpgradeEngine.Build($"chocolatey|{Target}\r\n", [], 0, 0);
                byte[] targetPackage = Package("chocolatey", Target);
                File.WriteAllBytes(Path.Combine(prepared, "choco.exe"), targetEngine);
                File.WriteAllBytes(Path.Combine(prepared, "chocolatey.nupkg"), targetPackage);
                List<(string From, string To)> moves =
                [
                    (Profile.RootPath("choco.exe"), MovedOutEngine),
                    (Path.Combine(prepared, "choco.exe"), Profile.RootPath("choco.exe")),
                    (Path.Combine(prepared, "chocolatey.nupkg"), Profile.RootPath(@"lib\chocolatey\chocolatey.nupkg")),
                ];
                if (variant == Variant.TamperedTarget)
                {
                    File.WriteAllText(Path.Combine(prepared, "7z.exe"), "tampered");
                    moves.Add((Path.Combine(prepared, "7z.exe"), Profile.RootPath(@"tools\7z.exe")));
                }
                // Every path the stand-in can touch is inside this fixture's temp tree.
                foreach ((string from, string to) in moves)
                    Assert.IsTrue(from.StartsWith(Profile.Native.Root + '\\', StringComparison.OrdinalIgnoreCase) &&
                        to.StartsWith(Profile.Native.Root + '\\', StringComparison.OrdinalIgnoreCase), $"{from} -> {to}");
                Profile.ReleaseBinary["choco.exe"] = StandInClientUpgradeEngine.Build($"chocolatey|{Source}\r\n", moves,
                    preSleepMilliseconds: 1500, postSleepMilliseconds: variant == Variant.LongRun ? 60_000 : 0);
                Profile.ReleaseContent["choco.exe.manifest"] = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"></assembly>";
                Profile.Build();
                string sourceAsset = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Profile.RootPath(@"lib\chocolatey\chocolatey.nupkg"))));
                ChocolateyReleaseFile engine = Profile.Release.Single(file => file.Path == "choco.exe");
                Releases = ChocolateyReleaseList.CreateForTests(
                [
                    Profile.ReleaseEntry(Source, sourceAsset),
                    new ChocolateyReleaseEntry(Target, Target + ".0", "chocolatey." + Target + ".nupkg", targetPackage.Length,
                        Convert.ToHexString(SHA256.HashData(targetPackage)),
                        [.. Profile.Release.Select(file => file == engine
                            ? new ChocolateyReleaseFile("choco.exe", targetEngine.Length, Convert.ToHexString(SHA256.HashData(targetEngine))) : file)]),
                ]);
                Profile.Lock();
                Profile.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
                Registry.CurrentUser.CreateSubKey(UserKey)?.Dispose();
                Registry.CurrentUser.CreateSubKey(MachineKey)?.Dispose();
                Seam = Profile.Seam(Releases);
                AssertFixtureRootsAreNotReal();

                using RequesterToken token = N1NativeFixture.DerivedToken();
                Sid = token.Sid;
                ChocolateyProfileResult observed = ChocolateyProfileObserver.ObserveClientForTests(token, ChocolateyObserverRole.Desktop,
                    N2ProfileFixture.Version(Source), expectedFingerprint: null, Budget, Seam, CancellationToken.None);
                using ChocolateyProfileObservation observation = observed is ChocolateyProfileResult.Observed o ? o.Observation
                    : throw new AssertFailedException($"expected a source observation, got {observed}");
                Assert.AreSame(Releases.Releases[0], observation.Release);
                Operation = ClientRouteWorkflowAdmissionGateTests.ClientOperation(Source, Target, observation.Fingerprint, Sid);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal N2ProfileFixture Profile { get; }
        internal ChocolateyReleaseList Releases { get; } = null!;
        internal ChocolateyObserverTestSeam Seam { get; } = null!;
        internal string Sid { get; } = string.Empty;
        internal string MovedOutEngine { get; } = string.Empty;
        internal ReviewedUpdateOperation Operation { get; } = null!;
        internal ClientRouteWorkflowAdmissionGateTests.GateStore Store { get; } = new();
        internal int VerifyReads => Volatile.Read(ref verifyReads);
        private string UserKey => keyBase + @"\User";
        private string MachineKey => keyBase + @"\Machine";

        /// <summary>Review, confirm and run the workflow inside the test admission scope.</summary>
        internal async Task<(ReviewedUpdateDispatchResult, ClientRouteWorkflowAdmissionGateTests.EndpointDispatcher)> RunAsync()
        {
            ClientRouteWorkflowAdmissionGateTests.FixedClock clock = new(Protocol2InterpreterFixture.ReviewedAt.AddMinutes(1));
            ChocolateyDispatchBaselines baselines = new();
            ChocolateyProfileEvidenceSource evidence = ChocolateyProfileEvidenceSource.CreateClientForTests(Desktop, Releases, baselines, clock, Budget);
            ChocolateyReviewedUpdateVerifier verifier = ChocolateyReviewedUpdateVerifier.CreateClientForTests((id, expected, budget, ct) =>
            {
                Interlocked.Increment(ref verifyReads);
                return Desktop(id, expected, budget, ct);
            }, Releases, baselines, TimeProvider.System, Budget);
            EndpointHarness harness = new()
            {
                PortOverride = new FixturePort(() => NativeChocolateyHelperItem.CreateClientOverFixtureForTests(N1NativeFixture.DerivedToken(), Helper, Releases)),
                CapabilityOverride = new SupportedCapability(),
            };
            harness.Peer.UserSid = Sid;
            ClientRouteWorkflowAdmissionGateTests.EndpointDispatcher dispatcher = new(harness);
            ReviewedUpdateDispatchWorkflow workflow = new((work, _, ct) => work(ClientRouteWorkflowAdmissionGateTests.Owner(Sid), ct), evidence,
                dispatcher, verifier, Store, clock);
            using (ReviewedRouteTestAdmission.EnterClientSelfUpdateForTests())
            {
                LiveApprovedUpdate approval = new ReviewedUpdateReview(new ReviewedUpdateDraft(Operation, Guid.NewGuid()), clock).Confirm();
                ReviewedUpdateDispatchResult result = await workflow.RunAsync(approval, CancellationToken.None);
                Assert.AreEqual(1, dispatcher.Calls, result.Message);
                return (result, dispatcher);
            }
        }

        /// <summary>The persisted record as history.</summary>
        internal ReviewedUpdateHistoryView History()
        {
            Assert.IsTrue(ReviewedUpdateTerminalRecord.TryParse(Store.Payload, null, out ReviewedUpdateTerminalRecord? terminal));
            return ReviewedUpdateHistoryPresentation.Present(new ReviewedUpdateHistoryRecord(Operation.JobId, Operation, Store.State!.Value,
                Store.Claim!.ClaimedAt, terminal, true));
        }

        internal void SetUserTestValue(string name, string value)
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(UserKey, writable: true) ?? throw new AssertFailedException("no user test key");
            key.SetValue(name, value, RegistryValueKind.String);
        }

        internal void AssertWritable(string relative)
        {
            using FileStream stream = new(Profile.RootPath(relative), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.IsTrue(stream.CanWrite, relative);
        }

        /// <summary>
        /// The real environment keys still report exactly the pinned names they reported before the test (the run watched only
        /// the test keys and wrote nothing else), and the observer seam's roots are this fixture's, never the machine's.
        /// </summary>
        internal void AssertRealStateUntouched()
        {
            ChocolateySetupEnvironmentReading after = ChocolateySetupEnvironmentReader.Read();
            Assert.AreEqual(realBefore.Complete, after.Complete);
            CollectionAssert.AreEqual(realBefore.UserNames.ToArray(), after.UserNames.ToArray(), "real HKCU\\Environment unchanged");
            CollectionAssert.AreEqual(realBefore.MachineNames.ToArray(), after.MachineNames.ToArray(), "real machine environment unchanged");
            AssertFixtureRootsAreNotReal();
        }

        private void AssertFixtureRootsAreNotReal()
        {
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            foreach (string root in new[] { Seam.Roots.ProgramData, Seam.Roots.Root, Seam.Roots.WindowsRoot })
            {
                Assert.IsTrue(root.StartsWith(Profile.Native.Root + '\\', StringComparison.OrdinalIgnoreCase), root);
                Assert.IsFalse(root.StartsWith(programData, StringComparison.OrdinalIgnoreCase), root);
            }
        }

        /// <summary>A Desktop-role client observation of this fixture (fresh derived token per call, as production's current-process token).</summary>
        private ChocolateyProfileResult? Desktop(string id, ChocolateyNativeVersion expected, TimeSpan budget, CancellationToken cancellationToken)
        {
            if (id != ReviewedUpdateOperation.ChocolateyClientPackageId) return null;
            using RequesterToken token = N1NativeFixture.DerivedToken();
            return ChocolateyProfileObserver.ObserveClientForTests(token, ChocolateyObserverRole.Desktop, expected, expectedFingerprint: null, budget, Seam,
                cancellationToken);
        }

        /// <summary>
        /// The native item's Helper-role observation over this fixture; each client binding it returns arms its watchdog on the
        /// test keys instead of the real environment keys.
        /// </summary>
        private ChocolateyProfileResult Helper(RequesterToken token, string targetId, ChocolateyNativeVersion frozen, string fingerprint, TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            Assert.AreEqual(ReviewedUpdateOperation.ChocolateyClientPackageId, targetId);
            ChocolateyProfileResult result = ChocolateyProfileObserver.ObserveClientForTests(token, ChocolateyObserverRole.Helper, frozen, fingerprint,
                remaining, Seam, cancellationToken);
            if (result is ChocolateyProfileResult.Observed { Observation.HeldClient: { } client })
            {
                string user = UserKey, machine = MachineKey;
                ChocolateyClientWatchTargets.UseEnvironmentWatchForTests(client, () => ChocolateySetupEnvironmentReader.TryWatchForTests(user, machine));
            }
            return result;
        }

        private static byte[] Package(string id, string version)
        {
            using MemoryStream stream = new();
            using (ZipArchive zip = new(stream, ZipArchiveMode.Create, leaveOpen: true))
                foreach ((string name, string content) in new[]
                {
                    (id + ".nuspec", $"<package><metadata><id>{id}</id><version>{version}</version></metadata></package>"),
                    ("[Content_Types].xml", "<Types/>"),
                })
                {
                    using Stream entry = zip.CreateEntry(name).Open();
                    entry.Write(Encoding.UTF8.GetBytes(content));
                }
            return stream.ToArray();
        }

        public void Dispose()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(keyBase, throwOnMissingSubKey: false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            Profile?.Dispose();
        }
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
