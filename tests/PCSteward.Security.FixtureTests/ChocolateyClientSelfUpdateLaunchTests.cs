using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/07/13; ADR-031 design mapping §1 and §3.6 (Windows-only; reported as failed elsewhere):
/// the client-mode observation and its client self-update binding over a locked N2 fixture whose engine is a stand-in. The
/// binding refuses every package command, and its single compiled client upgrade releases <b>every</b> hold (engine, release
/// tree, code) only after creation, assignment and resume; afterwards it launches nothing. Step 7 in client mode needs the
/// client registration at the matched release and no licensed extension registration.
/// </summary>
[TestClass]
public sealed class ChocolateyClientSelfUpdateLaunchTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Remaining = TimeSpan.FromMinutes(11);
    private static readonly string[] ListedDirectories = ["helpers", "tools"];

    [TestMethod]
    public void TheClientBindingRefusesEveryPackageCommandAndKeepsItsHolds()
    {
        using ClientUpdateFixture fixture = new();
        Assert.AreEqual(ChocolateyBindingPurpose.ClientSelfUpdate, fixture.Client.Purpose);
        Assert.AreSame(fixture.Releases.Releases[0], fixture.Client.InstalledRelease);
        Assert.AreEqual("chocolatey", fixture.Client.TargetId);
        ReviewedUpdateOperation package = N3Operations.Create(id: N2ProfileFixture.Target, installed: "1.2.0", candidate: "1.3.0");
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.FromBinding(fixture.Client, ChocolateyCompiledCommand.CreateOutdated()));
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.FromBinding(fixture.Client, ChocolateyCompiledCommand.TryUpgrade(package)!));
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.FromBinding(fixture.Client, ChocolateyCompiledCommand.TryListExact(package)!));
        Assert.IsTrue(fixture.Client.CanLaunch && fixture.Client.CodeHeld && !fixture.Client.AllReleased, "refusals change nothing");
        AssertHeld(fixture.Profile.RootPath("choco.exe"));
    }

    [TestMethod]
    public async Task TheClientUpgradeReleasesEveryHoldAfterResumeAndThenLaunchesNothing()
    {
        using ClientUpdateFixture fixture = new();
        ChocolateyCompiledCommand.UpgradeClient upgrade = ChocolateyCompiledCommand.TryUpgradeClient(fixture.Operation,
            fixture.Releases.Releases[0], fixture.Releases.Releases[1]) ?? throw new AssertFailedException("upgrade refused");
        AssertHeld(fixture.Profile.RootPath("choco.exe"));
        AssertHeld(fixture.Profile.RootPath(@"tools\7z.exe"));

        NativeProcessObservation observation = await LaunchJob.RunAsync(fixture.Client, upgrade, Remaining, CancellationToken.None);

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.IsTrue(observation.DirectExitObserved && observation.AggregateSettled);
        // Design §1: the client's own setup must rename or delete every client file, so every hold is gone after resume.
        Assert.IsTrue(fixture.Client.AllReleased);
        Assert.IsFalse(fixture.Client.CanLaunch);
        // Design §2.1 R5: the launch ran under the runtime watchdog (real environment keys, KEY_NOTIFY only) and nothing tripped it.
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fixture.Client.WatchdogFault);
        OpenForWrite(fixture.Profile.RootPath(@"tools\7z.exe")).Dispose();
        OpenForWrite(fixture.Profile.RootPath(@"helpers\functions\Get-ChocolateyPath.ps1")).Dispose();
        OpenForWrite(fixture.Profile.RootPath(@"extensions\fixture\fixture.psm1")).Dispose();
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeLaunchSpec.FromBinding(fixture.Client, upgrade));
    }

    [TestMethod]
    [DataRow((int)LaunchJobFault.AssignFails)]
    [DataRow((int)LaunchJobFault.ResumeFails)]
    public async Task AFailedAssignmentOrResumeReleasesNothingAndCreatesNothing(int injected)
    {
        LaunchJobFault fault = (LaunchJobFault)injected;
        // LOW-1: the release-all point is reached only after creation, assignment and resume all succeeded.
        using ClientUpdateFixture fixture = new();
        ChocolateyCompiledCommand.UpgradeClient upgrade = ChocolateyCompiledCommand.TryUpgradeClient(fixture.Operation,
            fixture.Releases.Releases[0], fixture.Releases.Releases[1]) ?? throw new AssertFailedException("upgrade refused");
        NativeLaunchSpec spec = NativeLaunchSpec.FromBinding(fixture.Client, upgrade);

        NativeProcessObservation observation = await LaunchJob.RunCoreWithFaultForTestsAsync(spec, Budget, fault, CancellationToken.None);

        Assert.AreEqual(NativeInvocation.NotStarted, observation.Invocation, fault.ToString());
        Assert.IsFalse(fixture.Client.AllReleased, "nothing is released without a successful resume");
        Assert.IsFalse(fixture.Client.ReleaseIncomplete);
        Assert.IsTrue(fixture.Client.CanLaunch && fixture.Client.CodeHeld);
        AssertHeld(fixture.Profile.RootPath("choco.exe"));
        AssertHeld(fixture.Profile.RootPath(@"tools\7z.exe"));
        AssertHeld(fixture.Profile.RootPath(@"extensions\fixture\fixture.psm1"));
    }

    [TestMethod]
    public void AFailedReleaseIsRecordedOnTheClientBindingOnly()
    {
        // LOW-2 (design §2 R3): the launcher reports a release that threw; only the client upgrade spec records it.
        using ClientUpdateFixture fixture = new();
        ChocolateyCompiledCommand.ListClient list = ChocolateyCompiledCommand.TryListClient(fixture.Operation, fixture.Releases.Releases[0])
            ?? throw new AssertFailedException("list refused");
        NativeLaunchSpec.FromBinding(fixture.Client, list).OnReleaseFaulted();
        Assert.IsFalse(fixture.Client.ReleaseIncomplete, "a query spec records nothing");
        ChocolateyCompiledCommand.UpgradeClient upgrade = ChocolateyCompiledCommand.TryUpgradeClient(fixture.Operation,
            fixture.Releases.Releases[0], fixture.Releases.Releases[1]) ?? throw new AssertFailedException("upgrade refused");
        NativeLaunchSpec.FromBinding(fixture.Client, upgrade).OnReleaseFaulted();
        Assert.IsTrue(fixture.Client.ReleaseIncomplete);
    }

    [TestMethod]
    public void OnlyTheClientUpgradeSpecIsWatchedAndOnlyItRecordsTheFirstWatchdogFault()
    {
        // Design §2.1 R5: the watchdog targets come only from the client upgrade spec over its binding (root, helpers/tools, the
        // observation's requester token); a query spec is not watched and records nothing.
        using ClientUpdateFixture fixture = new();
        ChocolateyCompiledCommand.ListClient list = ChocolateyCompiledCommand.TryListClient(fixture.Operation, fixture.Releases.Releases[0])
            ?? throw new AssertFailedException("list refused");
        NativeLaunchSpec query = NativeLaunchSpec.FromBinding(fixture.Client, list);
        Assert.IsNull(query.WatchTargets());
        query.OnWatchdogStopped(ChocolateyClientWatchdogFault.RootWritableDuringRun);
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fixture.Client.WatchdogFault);

        ChocolateyCompiledCommand.UpgradeClient upgrade = ChocolateyCompiledCommand.TryUpgradeClient(fixture.Operation,
            fixture.Releases.Releases[0], fixture.Releases.Releases[1]) ?? throw new AssertFailedException("upgrade refused");
        NativeLaunchSpec spec = NativeLaunchSpec.FromBinding(fixture.Client, upgrade);
        ChocolateyClientWatchTargets targets = spec.WatchTargets() ?? throw new AssertFailedException("the client upgrade must be watched");
        Assert.AreEqual(fixture.Client.LaunchPaths.ChocolateyRoot, targets.Root);
        CollectionAssert.AreEqual(ListedDirectories, targets.ListedDirectories.ToArray());
        Assert.IsNotNull(fixture.Client.Requester);
        Assert.AreSame(fixture.Client.Requester, targets.Requester);
        spec.OnWatchdogStopped(ChocolateyClientWatchdogFault.None);
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fixture.Client.WatchdogFault);
        spec.OnWatchdogStopped(ChocolateyClientWatchdogFault.SetupOverrideDuringRun);
        spec.OnWatchdogStopped(ChocolateyClientWatchdogFault.RootWritableDuringRun);
        Assert.AreEqual(ChocolateyClientWatchdogFault.SetupOverrideDuringRun, fixture.Client.WatchdogFault, "the first fault wins");
    }

    [TestMethod]
    public void TheClientPostcheckReadsTheSourceTheWatchdogFaultAndTheReleaseStateFromTheBinding()
    {
        // Review L3 (design §2.1 R5, §2 R3): src callers pass the binding, never a free fault or release flag.
        using ClientUpdateFixture fixture = new();
        ChocolateyReviewedRelease target = fixture.Releases.Releases[1];
        string sid = fixture.Client.Requester!.Sid;
        ChocolateyProfileResult refused = ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, "2.7.5", null);
        Assert.AreEqual(ChocolateyClientUpdateVerdict.NotTargetRelease, ChocolateyClientUpdatePostcheck.Evaluate(refused, fixture.Client, target, sid));

        ChocolateyCompiledCommand.UpgradeClient upgrade = ChocolateyCompiledCommand.TryUpgradeClient(fixture.Operation,
            fixture.Releases.Releases[0], target) ?? throw new AssertFailedException("upgrade refused");
        NativeLaunchSpec spec = NativeLaunchSpec.FromBinding(fixture.Client, upgrade);
        spec.OnReleaseFaulted();
        Assert.AreEqual(ChocolateyClientUpdateVerdict.ReleaseIncomplete, ChocolateyClientUpdatePostcheck.Evaluate(refused, fixture.Client, target, sid));
        spec.OnWatchdogStopped(ChocolateyClientWatchdogFault.RootWritableDuringRun);
        Assert.AreEqual(ChocolateyClientUpdateVerdict.RootWritableDuringRun,
            ChocolateyClientUpdatePostcheck.Evaluate(refused, fixture.Client, target, sid), "the recorded watchdog fault comes first");
    }

    [TestMethod]
    public void ClientModeNeedsTheRegistrationAtTheMatchedReleaseAndNoLicensedExtension()
    {
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using (N2ProfileFixture stale = ClientUpdateFixture.Build(registration: "2.6.0").Lock())
            Refused(ChocolateyProfileObserver.ObserveClientForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Version("2.6.0"), null,
                Budget, stale.Seam(ClientUpdateFixture.List(stale)), CancellationToken.None));
        using (N2ProfileFixture extension = ClientUpdateFixture.Build())
        {
            extension.WritePackage(@"lib\chocolatey.extension\chocolatey.extension.nupkg", "chocolatey.extension", "6.0.0");
            extension.Lock();
            Refused(ChocolateyProfileObserver.ObserveClientForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Version("2.7.5"), null,
                Budget, extension.Seam(ClientUpdateFixture.List(extension)), CancellationToken.None));
        }
        // The package route still refuses the client identity as its target.
        using N2ProfileFixture package = ClientUpdateFixture.Build().Lock();
        Refused(ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Desktop, "chocolatey", N2ProfileFixture.Version("2.7.5"), null,
            Budget, package.Seam(ClientUpdateFixture.List(package)), CancellationToken.None));
    }

    private static void Refused(ChocolateyProfileResult result)
    {
        if (result is ChocolateyProfileResult.Observed observed) observed.Observation.Dispose();
        Assert.AreEqual(ChocolateyProfileRefusal.MachineInputsUnsupported, (result as ChocolateyProfileResult.Refused)?.Reason, result.ToString());
    }

    private static void AssertHeld(string path) =>
        Assert.ThrowsExactly<IOException>(() => OpenForWrite(path), $"{path} must be held read-share only");

    private static FileStream OpenForWrite(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    /// <summary>A locked N2 fixture with a cmd.exe stand-in engine, listed as 2.7.5 beside a newer fixture 2.8.0, observed in client mode.</summary>
    private sealed class ClientUpdateFixture : IDisposable
    {
        private readonly RequesterToken token;

        internal ClientUpdateFixture()
        {
            N1NativeFixture.RequireWindows("client self-update launch of a stand-in engine");
            token = N1NativeFixture.DerivedToken();
            Profile = Build();
            try
            {
                Profile.Lock();
                Profile.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
                Releases = List(Profile);
                ChocolateyProfileResult result = ChocolateyProfileObserver.ObserveClientForTests(token, ChocolateyObserverRole.Helper,
                    N2ProfileFixture.Version("2.7.5"), expectedFingerprint: null, Budget, Profile.Seam(Releases), CancellationToken.None);
                Observation = result is ChocolateyProfileResult.Observed observed ? observed.Observation
                    : throw new AssertFailedException($"expected Observed, got {result}");
                Client = Observation.HeldClient ?? throw new AssertFailedException("the Helper role must produce a binding");
                Operation = ChocolateyClientUpdateCommandTests.Operation("2.7.5", "2.8.0");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal N2ProfileFixture Profile { get; }
        internal ChocolateyReleaseList Releases { get; } = null!;
        internal ChocolateyProfileObservation Observation { get; } = null!;
        internal ChocolateyHeldClientBinding Client { get; } = null!;
        internal ReviewedUpdateOperation Operation { get; } = null!;

        /// <summary>An unlocked fixture with the stand-in engine and a <c>chocolatey</c> registration at <paramref name="registration"/>.</summary>
        internal static N2ProfileFixture Build(string registration = "2.7.5")
        {
            N2ProfileFixture fixture = new();
            fixture.ReleaseBinary["choco.exe"] = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            fixture.ReleaseContent["choco.exe.manifest"] = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"></assembly>";
            fixture.Build();
            if (registration != "2.7.5") fixture.WritePackage(@"lib\chocolatey\chocolatey.nupkg", "chocolatey", registration);
            return fixture;
        }

        /// <summary>The fixture tree as listed 2.7.5 and a newer fixture release 2.8.0 with another engine.</summary>
        internal static ChocolateyReleaseList List(N2ProfileFixture fixture) => ChocolateyReleaseList.CreateForTests(
        [
            fixture.ReleaseEntry("2.7.5", new string('A', 64)),
            new ChocolateyReleaseEntry("2.8.0", "2.8.0.0", "chocolatey.2.8.0.nupkg", 1000, new string('B', 64),
                [new ChocolateyReleaseFile("choco.exe", 9, new string('8', 64))]),
        ]);

        public void Dispose()
        {
            Observation?.Dispose();
            Profile.Dispose();
            token.Dispose();
        }
    }
}
