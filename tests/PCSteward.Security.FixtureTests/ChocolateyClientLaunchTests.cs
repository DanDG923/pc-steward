using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 design rev 6 §4 (M4, L2, L6), §4.1 and §8 N3 (Windows-only; reported as failed on
/// other platforms): <see cref="NativeLaunchSpec.FromBinding"/> and <see cref="LaunchJob.RunAsync"/> end to end through a
/// fixture binding that the Helper-role observer built through the N2 test seam. The fixture's root engine is a copy of
/// the system <c>cmd.exe</c> (a stand-in that ignores the fixed Chocolatey arguments and exits at end of its NUL input),
/// recorded in the fixture release table with its real length and SHA-256; no real Chocolatey client runs.
/// </summary>
[TestClass]
public sealed class ChocolateyClientLaunchTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Remaining = TimeSpan.FromMinutes(11);

    [TestMethod]
    public void TheSpecIsTheHeldEngineTheCompiledArgumentsTheCompiledEnvironmentAndTheRoot()
    {
        using ClientFixture fixture = new();
        ChocolateyHeldClientBinding client = fixture.Client;
        ChocolateyCompiledCommand.ListExact list = ChocolateyCompiledCommand.TryListExact(fixture.Operation)!;

        NativeLaunchSpec spec = NativeLaunchSpec.FromBinding(client, list);

        Assert.AreEqual(fixture.Profile.RootPath("choco.exe"), spec.ImagePath);
        Assert.AreEqual(client.EnginePath, spec.ImagePath);
        CollectionAssert.AreEqual(new[] { "list", N2ProfileFixture.Target, "--exact", "--limit-output", "--no-color" }, spec.Arguments.ToArray());
        CollectionAssert.AreEquivalent(ChocolateyCompiledEnvironment.Create(client.LaunchPaths).ToArray(), spec.Environment.ToArray());
        Assert.AreEqual(fixture.Profile.Root, spec.Environment["ChocolateyInstall"]);
        Assert.AreEqual(fixture.Profile.Cache, spec.Environment["TEMP"]);
        Assert.AreEqual(fixture.Profile.Root, spec.WorkingDirectory);
        Assert.IsTrue(spec.MayCreateProcess);
        CollectionAssert.AreEqual(ChocolateyCompiledCommand.CreateOutdated().Arguments.ToArray(),
            NativeLaunchSpec.FromBinding(client, ChocolateyCompiledCommand.CreateOutdated()).Arguments.ToArray());
        CollectionAssert.AreEqual(ChocolateyCompiledCommand.TryUpgrade(fixture.Operation)!.Arguments.ToArray(),
            NativeLaunchSpec.FromBinding(client, ChocolateyCompiledCommand.TryUpgrade(fixture.Operation)!).Arguments.ToArray());
        // N3 review L2: an upgrade spec claims the binding's one-shot upgrade launch; a second one is refused before any process.
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeLaunchSpec.FromBinding(client, ChocolateyCompiledCommand.TryUpgrade(fixture.Operation)!));
    }

    [TestMethod]
    public async Task ACommandForAnotherPackageIsRefusedBeforeLaunch()
    {
        using ClientFixture fixture = new();
        ReviewedUpdateOperation other = N3Operations.Create(id: "git.install", installed: "1.2.0", candidate: "1.3.0");
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.FromBinding(fixture.Client, ChocolateyCompiledCommand.TryListExact(other)!));
        Assert.ThrowsExactly<ArgumentException>(() => NativeLaunchSpec.FromBinding(fixture.Client, ChocolateyCompiledCommand.TryUpgrade(other)!));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => ChocolateyClientQueries.UpgradeAsync(fixture.Client,
            ChocolateyCompiledCommand.TryUpgrade(other)!, Remaining, CancellationToken.None));
        Assert.IsTrue(fixture.Client.CanLaunch && fixture.Client.CodeHeld, "a refused command changes nothing");
    }

    [TestMethod]
    public async Task AQueryRunsTheHeldEngineInItsOwnSettledJobAndKeepsEveryHold()
    {
        using ClientFixture fixture = new();
        ChocolateyCompiledCommand.ListExact list = ChocolateyCompiledCommand.TryListExact(fixture.Operation)!;

        ChocolateyQueryOutcome<ChocolateyInstalledRecord> outcome = await ChocolateyClientQueries.ListExactAsync(fixture.Client, list, Remaining,
            CancellationToken.None);

        NativeProcessObservation observation = outcome.Observation;
        Assert.AreEqual(NativeInvocation.Started, observation.Invocation, "the held engine image was mapped and ran");
        Assert.IsTrue(observation.DirectExitObserved && observation.AggregateSettled && observation.PipeEofObserved);
        Assert.IsFalse(observation.Cancelled || observation.TimedOut || observation.TerminationFailed);
        // The stand-in prints no Chocolatey record, so the strict parse never yields one installed value.
        Assert.IsFalse(outcome.Status == ChocolateyQueryStatus.Parsed && outcome.Records.Count == 1, observation.StandardOutput);
        Assert.IsNotInstanceOfType<ChocolateyCandidateValues.Matched>(ChocolateyStageCMapping.Map(fixture.Baseline, outcome, null));

        // Queries never release anything (§4 L2): the code, engine and release files are all still held.
        Assert.IsTrue(fixture.Client.CanLaunch && fixture.Client.CodeHeld);
        AssertHeld(fixture.Profile.RootPath(@"hooks\pre-install-all.ps1"));
        AssertHeld(fixture.Profile.RootPath(@"extensions\fixture\fixture.psm1"));
        AssertHeld(fixture.Profile.RootPath("choco.exe"));
        AssertHeld(fixture.Profile.RootPath(@"helpers\chocolateyInstaller.psm1"));

        ChocolateyQueryOutcome<ChocolateyNativeOutdatedRecord> outdated = await ChocolateyClientQueries.OutdatedAsync(fixture.Client,
            ChocolateyCompiledCommand.CreateOutdated(), Remaining, CancellationToken.None);
        Assert.AreEqual(NativeInvocation.Started, outdated.Observation.Invocation);
        Assert.IsTrue(outdated.Observation.AggregateSettled);
        Assert.IsTrue(fixture.Client.CodeHeld);
    }

    [TestMethod]
    public async Task TheUpgradeReleasesOnlyTheCodeHoldsAfterResumeAndTheEngineStaysHeldThroughExit()
    {
        using ClientFixture fixture = new();
        ChocolateyCompiledCommand.Upgrade upgrade = ChocolateyCompiledCommand.TryUpgrade(fixture.Operation)!;
        AssertHeld(fixture.Profile.RootPath(@"extensions\fixture\fixture.psm1"));

        NativeProcessObservation observation = await ChocolateyClientQueries.UpgradeAsync(fixture.Client, upgrade, Remaining, CancellationToken.None);

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.IsTrue(observation.DirectExitObserved && observation.AggregateSettled);
        // L2: hooks/extensions were released once creation, assignment and resume succeeded.
        Assert.IsFalse(fixture.Client.CodeHeld);
        OpenForWrite(fixture.Profile.RootPath(@"hooks\pre-install-all.ps1")).Dispose();
        OpenForWrite(fixture.Profile.RootPath(@"extensions\fixture\fixture.psm1")).Dispose();
        // M4: the engine and the release tree stay held after the process exited, until the binding is disposed.
        AssertHeld(fixture.Profile.RootPath("choco.exe"));
        AssertHeld(fixture.Profile.RootPath(@"tools\7z.exe"));
        AssertHeld(fixture.Profile.RootPath(@"helpers\functions\Get-ChocolateyPath.ps1"));
        // After the upgrade the binding launches nothing more.
        Assert.IsFalse(fixture.Client.CanLaunch);
        Assert.ThrowsExactly<InvalidOperationException>(() => NativeLaunchSpec.FromBinding(fixture.Client, ChocolateyCompiledCommand.CreateOutdated()));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ChocolateyClientQueries.UpgradeAsync(fixture.Client, upgrade, Remaining,
            CancellationToken.None));

        fixture.Observation.Dispose();
        OpenForWrite(fixture.Profile.RootPath("choco.exe")).Dispose();
        OpenForWrite(fixture.Profile.RootPath(@"tools\7z.exe")).Dispose();
    }

    [TestMethod]
    public async Task ADisposedBindingLaunchesNothing()
    {
        using ClientFixture fixture = new();
        ChocolateyCompiledCommand.ListExact list = ChocolateyCompiledCommand.TryListExact(fixture.Operation)!;
        fixture.Observation.Dispose();
        Assert.IsFalse(fixture.Client.CanLaunch);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => ChocolateyClientQueries.ListExactAsync(fixture.Client, list, Remaining,
            CancellationToken.None));
    }

    [TestMethod]
    public async Task TheLaunchDeadlineIsCappedByRemainingAndNothingStartsWithoutBudgetOrAfterCancellation()
    {
        using ClientFixture fixture = new();
        ChocolateyCompiledCommand.ListExact list = ChocolateyCompiledCommand.TryListExact(fixture.Operation)!;

        // remaining (1 ms) is far below the 30 s budget: the launch is supervised to the capped deadline and torn down.
        Stopwatch clock = Stopwatch.StartNew();
        ChocolateyQueryOutcome<ChocolateyInstalledRecord> capped = await ChocolateyClientQueries.ListExactAsync(fixture.Client, list,
            TimeSpan.FromMilliseconds(1), CancellationToken.None);
        Assert.AreEqual(NativeInvocation.Started, capped.Observation.Invocation);
        Assert.IsTrue(capped.Observation.TimedOut, "the capped deadline elapsed");
        Assert.IsTrue(capped.Observation.AggregateSettled, "teardown confirmed zero active processes");
        Assert.IsTrue(clock.Elapsed < TimeSpan.FromSeconds(30), clock.Elapsed.ToString());
        Assert.AreEqual(ChocolateyQueryStatus.Interrupted, capped.Status);
        ChocolateyCandidateValues.Undetermined undetermined = (ChocolateyCandidateValues.Undetermined)ChocolateyStageCMapping.Map(fixture.Baseline, capped, null);
        Assert.IsTrue(undetermined.TimedOut && undetermined.AggregateSettlementConfirmed);

        // Nothing left of the budget, or an already-cancelled token: no process, a settled NotStarted with the flag.
        NativeProcessObservation none = await LaunchJob.RunAsync(fixture.Client, list, TimeSpan.Zero, CancellationToken.None);
        Assert.AreEqual(NativeInvocation.NotStarted, none.Invocation);
        Assert.IsTrue(none.TimedOut && none.AggregateSettled && !none.Cancelled);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        NativeProcessObservation stopped = await LaunchJob.RunAsync(fixture.Client, list, Remaining, cancelled.Token);
        Assert.AreEqual(NativeInvocation.NotStarted, stopped.Invocation);
        Assert.IsTrue(stopped.Cancelled && stopped.AggregateSettled && !stopped.TimedOut);
        Assert.IsTrue(fixture.Client.CodeHeld);
    }

    private static void AssertHeld(string path) =>
        Assert.ThrowsExactly<IOException>(() => OpenForWrite(path), $"{path} must be held read-share only");

    private static FileStream OpenForWrite(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

    /// <summary>A locked N2 fixture whose root engine is a copy of cmd.exe, observed in the Helper role (git 1.2.0 → 1.3.0).</summary>
    private sealed class ClientFixture : IDisposable
    {
        private readonly RequesterToken token;

        internal ClientFixture()
        {
            N1NativeFixture.RequireWindows("held-binding launch of a stand-in engine");
            token = N1NativeFixture.DerivedToken();
            Profile = new N2ProfileFixture();
            try
            {
                Profile.ReleaseBinary["choco.exe"] = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
                // A valid empty side-by-side manifest beside the stand-in (the embedded manifest takes precedence anyway).
                Profile.ReleaseContent["choco.exe.manifest"] = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<assembly xmlns=\"urn:schemas-microsoft-com:asm.v1\" manifestVersion=\"1.0\"></assembly>";
                Profile.Build().Lock();
                // As in the N2 Helper-role tests: an existing protected system directory owned by another principal.
                Profile.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
                ChocolateyProfileResult result = ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Helper, N2ProfileFixture.Target,
                    N2ProfileFixture.Version("1.2.0"), expectedFingerprint: null, Budget, Profile.Seam(), CancellationToken.None);
                Observation = result is ChocolateyProfileResult.Observed observed ? observed.Observation
                    : throw new AssertFailedException($"expected Observed, got {result}");
                Client = Observation.HeldClient ?? throw new AssertFailedException("the Helper role must produce a binding");
                Operation = N3Operations.Create(id: N2ProfileFixture.Target, installed: "1.2.0", candidate: "1.3.0");
                Baseline = new ChocolateyStageCBaseline(N2ProfileFixture.Target, "1.2.0", Observation.Fingerprint, Observation.Fingerprint, "1.2.0", "1.3.0");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal N2ProfileFixture Profile { get; }
        internal ChocolateyProfileObservation Observation { get; } = null!;
        internal ChocolateyHeldClientBinding Client { get; } = null!;
        internal ReviewedUpdateOperation Operation { get; } = null!;
        internal ChocolateyStageCBaseline Baseline { get; } = null!;

        public void Dispose()
        {
            Observation?.Dispose();
            Profile.Dispose();
            token.Dispose();
        }
    }
}
