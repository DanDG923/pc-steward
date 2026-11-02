using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; ADR-031 design §2.1 R5 runtime watchdog (owner option 1). The decision rule and the monitor are
/// portable and tested with fakes. The native tests (Windows-only; reported as failed elsewhere) run a real <c>cmd.exe</c> /
/// <c>PING.EXE</c> job through the launcher's test-only watchdog entry over a locked N1 fixture root and two test keys below
/// <c>HKEY_CURRENT_USER\Software\PCSteward.Tests\</c> standing in for the user and machine environment keys (CI never writes
/// the real <c>HKCU\Environment</c>): a pinned name written into a watched key or an ACL grant to the requester on the root or
/// a listed directory terminates the job; an unrelated change or no change lets it complete; a watch that cannot be armed,
/// or a fault already visible at arming, creates no process. Independent review M2/L1: setup's own Ensure-Permissions-shaped
/// rewrite of the root is not a fault; a grant on the root's parent (Ancestor mode, caught by the backstop poll) is; a watched
/// key deleted or renamed away mid-run fails closed; a watch thread that cannot start fails closed and terminates the job.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateWatchdogTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(60);
    /// <summary>Generous CI bound on notification/poll latency plus job teardown and pipe settlement.</summary>
    private static readonly TimeSpan KillBound = TimeSpan.FromSeconds(10);

    // ---- portable: the pure rule ----

    [TestMethod]
    public void ACleanCompleteReadingWithEveryVerdictDeniedContinues()
    {
        foreach (ChocolateyClientWatchWake wake in new[] { ChocolateyClientWatchWake.Armed, ChocolateyClientWatchWake.Notification, ChocolateyClientWatchWake.Poll })
            Assert.AreEqual(ChocolateyClientWatchdogFault.None, ChocolateyClientWatchdogRule.Decide(wake, Clean(), Denied(4)), wake.ToString());
    }

    [TestMethod]
    public void AnOverrideNameInEitherKeyTerminatesFirst()
    {
        ChocolateySetupEnvironmentReading user = ChocolateySetupEnvironmentReading.Of(["ChocolateyInstallAllowCurrentUser"], []);
        ChocolateySetupEnvironmentReading machine = ChocolateySetupEnvironmentReading.Of([], ["ChocolateyAllowInsecureRootDirectory"]);
        RequesterModification[] granted = [RequesterModification.Granted, RequesterModification.Unreadable];
        foreach (ChocolateySetupEnvironmentReading reading in new[] { user, machine })
        {
            Assert.AreEqual(ChocolateyClientWatchdogFault.SetupOverrideDuringRun,
                ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.Notification, reading, Denied(3)));
            // The override outranks a writable root, an unreadable verdict and a failed re-arm.
            Assert.AreEqual(ChocolateyClientWatchdogFault.SetupOverrideDuringRun,
                ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.RearmFailed, reading, granted));
        }
    }

    [TestMethod]
    public void AGrantedVerdictTerminatesAsRootWritableEvenWhenOtherChecksFailed()
    {
        RequesterModification[] verdicts = [RequesterModification.Denied, RequesterModification.Granted, RequesterModification.Denied];
        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun,
            ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.Poll, Clean(), verdicts));
        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun,
            ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.RearmFailed, ChocolateySetupEnvironmentReading.Unreadable,
                [RequesterModification.Unreadable, RequesterModification.Granted]));
    }

    [TestMethod]
    public void AnythingThatLeavesTheRunUnwatchedFailsClosed()
    {
        ChocolateyClientWatchdogFault failed = ChocolateyClientWatchdogFault.WatchFailedDuringRun;
        Assert.AreEqual(failed, ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.RearmFailed, Clean(), Denied(3)), "re-arm failed");
        Assert.AreEqual(failed, ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.Poll, ChocolateySetupEnvironmentReading.Unreadable, Denied(3)),
            "incomplete reading");
        Assert.AreEqual(failed, ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.Poll, Clean(), []), "no verdict");
        Assert.AreEqual(failed, ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.Poll, Clean(),
            [RequesterModification.Denied, RequesterModification.Unreadable]), "unreadable verdict");
        Assert.AreEqual(failed, ChocolateyClientWatchdogRule.Decide(ChocolateyClientWatchWake.Poll, Clean(), [(RequesterModification)7]), "undefined verdict");
        Assert.AreEqual(failed, ChocolateyClientWatchdogRule.Decide((ChocolateyClientWatchWake)42, Clean(), Denied(3)), "undefined wake");
    }

    // ---- portable: the monitor with fakes ----

    [TestMethod]
    public void TheMonitorContinuesWithoutTerminatingWhileEverythingIsClean()
    {
        Fakes fakes = new();
        ChocolateyClientWatchMonitor monitor = fakes.Monitor();
        monitor.Attach(fakes.Terminate);
        for (int round = 0; round < 5; round++)
            Assert.AreEqual(ChocolateyClientWatchdogFault.None, monitor.Observe(round % 2 == 0 ? ChocolateyClientWatchWake.Poll : ChocolateyClientWatchWake.Notification));
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, monitor.Fault);
        Assert.AreEqual(0, fakes.TerminateCalls);
        Assert.AreEqual(5, fakes.EnvironmentReads);
        Assert.AreEqual(5, fakes.AccessReads);
    }

    [TestMethod]
    public void EvaluateOnlyDecidesAndRecordsNothing()
    {
        Fakes fakes = new() { Environment = ChocolateySetupEnvironmentReading.Of(["ChocolateyInstall"], []) };
        ChocolateyClientWatchMonitor monitor = fakes.Monitor();
        monitor.Attach(fakes.Terminate);
        Assert.AreEqual(ChocolateyClientWatchdogFault.SetupOverrideDuringRun, monitor.Evaluate(ChocolateyClientWatchWake.Armed));
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, monitor.Fault, "the pre-creation check records no run fault");
        Assert.AreEqual(0, fakes.TerminateCalls);
    }

    [TestMethod]
    public void TheFirstFaultTerminatesOnceAndWins()
    {
        Fakes fakes = new();
        ChocolateyClientWatchMonitor monitor = fakes.Monitor();
        monitor.Attach(fakes.Terminate);
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, monitor.Observe(ChocolateyClientWatchWake.Poll));

        fakes.Access = [RequesterModification.Denied, RequesterModification.Granted];
        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun, monitor.Observe(ChocolateyClientWatchWake.Notification));
        Assert.AreEqual(1, fakes.TerminateCalls);
        Assert.IsTrue(monitor.TerminationConfirmed);

        // Later wakes neither re-evaluate nor replace the fault, and a confirmed termination is not repeated.
        int reads = fakes.EnvironmentReads;
        fakes.Environment = ChocolateySetupEnvironmentReading.Of(["ChocolateyInstallAllowCurrentUser"], []);
        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun, monitor.Observe(ChocolateyClientWatchWake.Notification));
        monitor.Fail();
        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun, monitor.Fault);
        Assert.AreEqual(1, fakes.TerminateCalls);
        Assert.AreEqual(reads, fakes.EnvironmentReads);
    }

    [TestMethod]
    public void AnUnconfirmedTerminationIsRetriedOnLaterWakes()
    {
        Fakes fakes = new() { Environment = ChocolateySetupEnvironmentReading.Of([], ["ChocolateyInstallAllowCurrentUser"]), TerminateResults = new([false, true]) };
        ChocolateyClientWatchMonitor monitor = fakes.Monitor();
        monitor.Attach(fakes.Terminate);
        Assert.AreEqual(ChocolateyClientWatchdogFault.SetupOverrideDuringRun, monitor.Observe(ChocolateyClientWatchWake.Notification));
        Assert.IsFalse(monitor.TerminationConfirmed);
        monitor.Observe(ChocolateyClientWatchWake.Poll);
        Assert.IsTrue(monitor.TerminationConfirmed);
        Assert.AreEqual(2, fakes.TerminateCalls);
        monitor.Observe(ChocolateyClientWatchWake.Poll);
        Assert.AreEqual(2, fakes.TerminateCalls);
    }

    [TestMethod]
    public void AThrowingProbeOrTerminatorFailsClosed()
    {
        Fakes fakes = new() { ThrowFromAccess = true, ThrowFromTerminate = true };
        ChocolateyClientWatchMonitor monitor = fakes.Monitor();
        monitor.Attach(fakes.Terminate);
        Assert.AreEqual(ChocolateyClientWatchdogFault.WatchFailedDuringRun, monitor.Observe(ChocolateyClientWatchWake.Poll));
        Assert.IsFalse(monitor.TerminationConfirmed, "a terminator that throws is not a confirmed termination");
        Assert.AreEqual(1, fakes.TerminateCalls);

        Fakes failing = new();
        ChocolateyClientWatchMonitor failed = failing.Monitor();
        failed.Attach(failing.Terminate);
        failed.Fail();
        Assert.AreEqual(ChocolateyClientWatchdogFault.WatchFailedDuringRun, failed.Fault);
        Assert.AreEqual(1, failing.TerminateCalls);
    }

    // ---- Windows: the native watchdog through the launcher's test-only entry ----

    [TestMethod]
    [DoNotParallelize]
    public async Task NoChangeLetsTheJobCompleteNormally()
    {
        using WatchFixture fixture = new();
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) =
            await fixture.RunAsync(Long, "echo", "watched&", "exit", "0");
        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.AreEqual(0, observation.ExitCode);
        Assert.IsTrue(observation.AggregateSettled && observation.DirectExitObserved);
        Assert.AreEqual("watched", observation.StandardOutput.Trim());
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fault);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnUnrelatedChangeInAWatchedKeyIsReCheckedAndTheJobCompletes()
    {
        using WatchFixture fixture = new();
        Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> run = fixture.RunAsync(Long, fixture.StartedThenPing(4));
        await fixture.WaitUntilStartedAsync();
        fixture.SetUserValue("PCStewardUnrelated", "1");
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) = await run;
        Assert.AreEqual(0, observation.ExitCode, "an unrelated value is not an override");
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fault);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("user", "ChocolateyInstallAllowCurrentUser")]
    [DataRow("user", "ChocolateyInstall")]
    [DataRow("machine", "ChocolateyAllowInsecureRootDirectory")]
    [DataRow("machine", "ChocolateyInstallAllowCurrentUser")]
    public async Task APinnedNameWrittenIntoAWatchedKeyTerminatesTheJob(string scope, string name)
    {
        using WatchFixture fixture = new();
        Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> run = fixture.RunAsync(Long, fixture.StartedThenPing(40));
        await fixture.WaitUntilStartedAsync();
        Stopwatch latency = Stopwatch.StartNew();
        if (scope == "user") fixture.SetUserValue(name, "true");
        else fixture.SetMachineValue(name, "true");
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) = await run;
        latency.Stop();

        Assert.AreEqual(ChocolateyClientWatchdogFault.SetupOverrideDuringRun, fault);
        AssertTerminated(observation, latency.Elapsed);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("")]
    [DataRow("helpers")]
    public async Task AnAclGrantToTheRequesterTerminatesTheJob(string relative)
    {
        using WatchFixture fixture = new();
        Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> run = fixture.RunAsync(Long, fixture.StartedThenPing(40));
        await fixture.WaitUntilStartedAsync();
        Stopwatch latency = Stopwatch.StartNew();
        string target = relative.Length == 0 ? fixture.Root : fixture.Root + '\\' + relative;
        fixture.Profile.Protect(target, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.Modify);
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) = await run;
        latency.Stop();

        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun, fault);
        AssertTerminated(observation, latency.Elapsed);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AnEnsurePermissionsShapedRewriteOfTheRootIsNotAFault()
    {
        // Review M2a: setup.psm1's Ensure-Permissions resets the root to a protected DACL (Administrators and SYSTEM Full
        // Control, Users Read & Execute, owner Administrators). The watchdog wakes on that security change, re-evaluates the
        // requester (Administrators deny-only in the derived token, so only Users Read & Execute applies) and lets the job finish.
        using WatchFixture fixture = new();
        Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> run = fixture.RunAsync(Long, fixture.StartedThenPing(4));
        await fixture.WaitUntilStartedAsync();
        fixture.RewriteRootLikeEnsurePermissions();
        Assert.AreEqual(RequesterModification.Denied, fixture.EvaluateRoot(), "the rewritten root is not requester-modifiable (not a vacuous None)");
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) = await run;

        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.AreEqual(0, observation.ExitCode, "setup's own default reset must not stop the update");
        Assert.IsTrue(observation.AggregateSettled && observation.DirectExitObserved);
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fault);
    }

    [TestMethod]
    [DoNotParallelize]
    public async Task AGrantToTheRequesterOnTheRootsParentTerminatesTheJob()
    {
        // Review M2b: the parent is evaluated in Ancestor mode (delete-child/delete/WRITE_DAC/WRITE_OWNER); its own descriptor
        // change is not reported by the non-recursive parent watch, so the backstop poll catches it.
        using WatchFixture fixture = new();
        Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> run = fixture.RunAsync(Long, fixture.StartedThenPing(40));
        await fixture.WaitUntilStartedAsync();
        Stopwatch latency = Stopwatch.StartNew();
        fixture.Profile.Protect(fixture.Parent, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.Modify);
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) = await run;
        latency.Stop();

        Assert.AreEqual(ChocolateyClientWatchdogFault.RootWritableDuringRun, fault);
        AssertTerminated(observation, latency.Elapsed);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("delete-user")]
    [DataRow("delete-machine")]
    [DataRow("rename-user")]
    public async Task AWatchedKeyDeletedOrRenamedAwayMidRunFailsClosed(string change)
    {
        // Review M2c/L1: a deleted key fails its kept query handle (ERROR_KEY_DELETED) and its re-arm; a key renamed away no
        // longer exists by path. Either is WatchFailedDuringRun, never a clean reading.
        using WatchFixture fixture = new();
        Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> run = fixture.RunAsync(Long, fixture.StartedThenPing(40));
        await fixture.WaitUntilStartedAsync();
        Stopwatch latency = Stopwatch.StartNew();
        switch (change)
        {
            case "delete-user": fixture.DeleteKey(user: true); break;
            case "delete-machine": fixture.DeleteKey(user: false); break;
            default: fixture.RenameUserKey(); break;
        }
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) = await run;
        latency.Stop();

        Assert.AreEqual(ChocolateyClientWatchdogFault.WatchFailedDuringRun, fault, change);
        AssertTerminated(observation, latency.Elapsed);
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("throws")]
    [DataRow("already-started")]
    public async Task AWatchThreadThatCannotStartFailsClosedAndTerminatesTheJob(string failure)
    {
        // Review M2d (design §2.1 R5): the process already runs when the watch thread starts; an unwatched run is not
        // permitted, so the failure is recorded as WatchFailedDuringRun and the job is terminated at once.
        using WatchFixture fixture = new();
        Func<ThreadStart, Thread> factory = failure == "throws"
            ? _ => throw new InvalidOperationException("injected watch thread failure")
            : _ => AlreadyStarted();
        Stopwatch latency = Stopwatch.StartNew();
        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) =
            await fixture.RunAsync(Long, factory, fixture.StartedThenPing(40));
        latency.Stop();

        Assert.AreEqual(ChocolateyClientWatchdogFault.WatchFailedDuringRun, fault, failure);
        AssertTerminated(observation, latency.Elapsed);
    }

    private static Thread AlreadyStarted()
    {
        Thread started = new(() => { }) { IsBackground = true };
        started.Start();
        started.Join();
        return started;
    }

    [TestMethod]
    [DoNotParallelize]
    [DataRow("missing-key")]
    [DataRow("missing-directory")]
    [DataRow("override-present")]
    [DataRow("root-writable")]
    public async Task AWatchThatCannotBeArmedOrAFaultVisibleAtArmingCreatesNoProcess(string failure)
    {
        using WatchFixture fixture = new(createKeys: failure != "missing-key", listed: failure == "missing-directory" ? ["helpers", "absent"] : null);
        if (failure == "override-present") fixture.SetUserValue("ChocolateyInstallAllowCurrentUser", "true");
        if (failure == "root-writable") fixture.Profile.Protect(fixture.Root, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.Modify);

        (NativeProcessObservation observation, ChocolateyClientWatchdogFault fault) =
            await fixture.RunAsync(Long, "echo", "ran>" + fixture.Marker);

        Assert.AreEqual(NativeInvocation.NotStarted, observation.Invocation, failure);
        Assert.IsTrue(observation.AggregateSettled);
        Assert.AreEqual(ChocolateyClientWatchdogFault.None, fault, "nothing ran, so no run fault is reported");
        Assert.IsFalse(File.Exists(fixture.Marker), "no process may have run");
    }

    [TestMethod]
    public void TheTargetsValidateTheRootAndDirectoriesAndDeriveTheParent()
    {
        N1NativeFixture.RequireWindows("watch targets over a requester token");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        Func<ChocolateySetupEnvironmentReader.Watch?> none = () => null;
        ChocolateyClientWatchTargets targets = ChocolateyClientWatchTargets.CreateForTests(token, @"C:\ProgramData\chocolatey", ["helpers", "tools"], none);
        Assert.AreEqual(@"C:\ProgramData", targets.Parent);
        Assert.AreEqual(@"C:\", ChocolateyClientWatchTargets.CreateForTests(token, @"C:\chocolatey", ["helpers"], none).Parent);
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyClientWatchTargets.CreateForTests(token, @"C:\", ["helpers"], none));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyClientWatchTargets.CreateForTests(token, @"C:\a\..\b", ["helpers"], none));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyClientWatchTargets.CreateForTests(token, @"C:\b", [], none));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyClientWatchTargets.CreateForTests(token, @"C:\b", ["tools", "TOOLS"], none));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyClientWatchTargets.CreateForTests(token, @"C:\b", [@"a\b"], none));
        Assert.IsNull(ChocolateyClientUpdateWatchdog.TryArm(targets), "an environment watch that cannot be armed arms nothing");
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateySetupEnvironmentReader.TryWatchForTests(@"Environment", @"Software\PCSteward.Tests\x"),
            "the test seam cannot watch the real user key");
    }

    private static void AssertTerminated(NativeProcessObservation observation, TimeSpan latency)
    {
        Assert.AreEqual(NativeInvocation.Started, observation.Invocation);
        Assert.AreEqual((int)LaunchJob.TerminationExitCode, observation.ExitCode, "the job was terminated, not completed");
        Assert.IsTrue(observation.AggregateSettled, "zero active processes after the watchdog's TerminateJobObject");
        Assert.IsFalse(observation.TimedOut || observation.Cancelled);
        Assert.IsTrue(latency < KillBound, $"terminated and settled {latency.TotalMilliseconds:F0} ms after the change");
    }

    private static ChocolateySetupEnvironmentReading Clean() => ChocolateySetupEnvironmentReading.Of([], []);

    private static RequesterModification[] Denied(int count) => Enumerable.Repeat(RequesterModification.Denied, count).ToArray();

    private sealed class Fakes
    {
        internal ChocolateySetupEnvironmentReading Environment { get; set; } = Clean();
        internal RequesterModification[] Access { get; set; } = Denied(4);
        internal Queue<bool> TerminateResults { get; init; } = new();
        internal bool ThrowFromAccess { get; init; }
        internal bool ThrowFromTerminate { get; init; }
        internal int EnvironmentReads { get; private set; }
        internal int AccessReads { get; private set; }
        internal int TerminateCalls { get; private set; }

        internal ChocolateyClientWatchMonitor Monitor() => new(() =>
        {
            EnvironmentReads++;
            return Environment;
        }, () =>
        {
            AccessReads++;
            return ThrowFromAccess ? throw new InvalidOperationException("probe") : Access;
        });

        internal bool Terminate()
        {
            TerminateCalls++;
            if (ThrowFromTerminate) throw new InvalidOperationException("terminate");
            return TerminateResults.Count == 0 || TerminateResults.Dequeue();
        }
    }

    /// <summary>
    /// A locked N1 fixture <c>ProgramData\chocolatey</c> root with <c>helpers</c> and <c>tools</c> (children first), a work
    /// directory, and two test keys below <c>HKCU\Software\PCSteward.Tests\&lt;guid&gt;</c>; everything is removed on dispose.
    /// </summary>
    private sealed class WatchFixture : IDisposable
    {
        private const string TestKeyRoot = @"Software\PCSteward.Tests";
        private readonly string keyBase = TestKeyRoot + @"\" + Guid.NewGuid().ToString("N");
        private readonly RequesterToken token;
        private readonly IReadOnlyList<string> listed;

        internal WatchFixture(bool createKeys = true, IReadOnlyList<string>? listed = null)
        {
            N1NativeFixture.RequireWindows("client update watchdog over a fixture root and test keys");
            this.listed = listed ?? ["helpers", "tools"];
            Profile = new N1NativeFixture();
            try
            {
                Root = Profile.Directory(@"ProgramData\chocolatey");
                Profile.Directory(@"ProgramData\chocolatey\helpers\functions");
                Profile.Directory(@"ProgramData\chocolatey\tools");
                Work = Profile.Directory("work");
                Profile.LockTree(Profile.Root);
                if (createKeys)
                {
                    Registry.CurrentUser.CreateSubKey(UserKey)?.Dispose();
                    Registry.CurrentUser.CreateSubKey(MachineKey)?.Dispose();
                }
                token = N1NativeFixture.DerivedToken();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal N1NativeFixture Profile { get; }
        internal string Root { get; } = string.Empty;
        internal string Parent => Path.GetDirectoryName(Root) ?? throw new InvalidOperationException("No parent.");
        internal string Work { get; } = string.Empty;
        internal string Marker => Path.Combine(Work, "ran.txt");
        private string UserKey => keyBase + @"\User";
        private string MachineKey => keyBase + @"\Machine";

        internal string Started => Path.Combine(Work, "started.txt");

        /// <summary>Writes the started marker, then sleeps through <c>PING.EXE</c> for about <paramref name="count"/> - 1 seconds.</summary>
        internal string[] StartedThenPing(int count) =>
            ["echo", "started>" + Started, "&", Path.Combine(Environment.SystemDirectory, "PING.EXE"), "-n",
                count.ToString(System.Globalization.CultureInfo.InvariantCulture), "127.0.0.1", ">nul"];

        /// <summary>Waits until the job's process has demonstrably run (so a later change happens during the run, not at arming).</summary>
        internal async Task WaitUntilStartedAsync()
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!File.Exists(Started))
            {
                if (clock.Elapsed > TimeSpan.FromSeconds(30)) throw new AssertFailedException("the watched job did not start");
                await Task.Delay(25);
            }
        }

        internal void SetUserValue(string name, string value)
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(UserKey, writable: true) ?? throw new AssertFailedException("no user test key");
            key.SetValue(name, value, RegistryValueKind.String);
        }

        internal void SetMachineValue(string name, string value)
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(MachineKey, writable: true) ?? throw new AssertFailedException("no machine test key");
            key.SetValue(name, value, RegistryValueKind.String);
        }

        /// <summary>
        /// Rewrites the root as pinned setup.psm1 <c>Ensure-Permissions</c> does: protected, inheritable Administrators and
        /// SYSTEM Full Control and Users Read &amp; Execute, owner Administrators (the CI runner is an administrator without a
        /// split token, so it may assign that owner). A runner that cannot assign it keeps the user as owner with OWNER RIGHTS
        /// Read &amp; Execute, which removes the owner's implicit rights. The logon-SID Full Control ACE is fixture cleanup only:
        /// it is deny-only in the derived requester token and never credited to it.
        /// </summary>
        internal void RewriteRootLikeEnsurePermissions()
        {
            SecurityIdentifier administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
            SecurityIdentifier users = new(WellKnownSidType.BuiltinUsersSid, null);
            try
            {
                new DirectoryInfo(Root).SetAccessControl(EnsurePermissionsShape(administrators, users, administrators));
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or PrivilegeNotHeldException)
            {
                new DirectoryInfo(Root).SetAccessControl(EnsurePermissionsShape(administrators, users, Profile.User));
            }
        }

        private DirectorySecurity EnsurePermissionsShape(SecurityIdentifier administrators, SecurityIdentifier users, SecurityIdentifier owner)
        {
            const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            DirectorySecurity security = new();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(owner);
            security.AddAccessRule(new FileSystemAccessRule(administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(N1NativeFixture.LocalSystem, FileSystemRights.FullControl, inherit, PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            if (owner != administrators)
                security.AddAccessRule(new FileSystemAccessRule(N1NativeFixture.OwnerRights, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(Profile.Logon, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            return security;
        }

        /// <summary>The requester's Node-mode verdict on the root, as the watchdog evaluates it.</summary>
        internal RequesterModification EvaluateRoot()
        {
            using SafeFileHandle root = N1NativeFixture.Open(Root, directory: true, out _);
            return RequesterAccess.Evaluate(token, root, directory: true, parentDirectory: null, AccessMode.Node);
        }

        internal void DeleteKey(bool user) => Registry.CurrentUser.DeleteSubKeyTree(user ? UserKey : MachineKey, throwOnMissingSubKey: true);

        /// <summary>Renames the user test key away (RegRenameKey); the watchdog's kept handles then point at the renamed key.</summary>
        internal void RenameUserKey()
        {
            using RegistryKey parent = Registry.CurrentUser.OpenSubKey(keyBase, writable: true) ?? throw new AssertFailedException("no test key base");
            Assert.AreEqual(0, RegRenameKey(parent.Handle, "User", "UserRenamed"), "RegRenameKey failed");
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int RegRenameKey(SafeRegistryHandle key, string subKeyName, string newKeyName);

        internal Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> RunAsync(TimeSpan deadline, params string[] arguments) =>
            RunAsync(deadline, watchThread: null, arguments);

        internal async Task<(NativeProcessObservation, ChocolateyClientWatchdogFault)> RunAsync(TimeSpan deadline,
            Func<ThreadStart, Thread>? watchThread, params string[] arguments)
        {
            string system32 = Environment.SystemDirectory;
            string windows = Path.GetDirectoryName(system32) ?? throw new InvalidOperationException("No Windows directory.");
            string cmd = Path.Combine(system32, "cmd.exe");
            Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
            {
                ["SystemRoot"] = windows, ["windir"] = windows, ["ComSpec"] = cmd, ["PATH"] = $"{system32};{windows}",
                ["PATHEXT"] = ".COM;.EXE;.BAT;.CMD", ["TEMP"] = Work, ["TMP"] = Work,
            };
            NativeLaunchSpec spec = NativeLaunchSpec.ForTests(cmd, ["/d", "/c", .. arguments], environment, Work);
            ChocolateyClientWatchTargets targets = ChocolateyClientWatchTargets.CreateForTests(token, Root, listed,
                () => ChocolateySetupEnvironmentReader.TryWatchForTests(UserKey, MachineKey), watchThread);
            ChocolateyClientWatchdogFault fault = (ChocolateyClientWatchdogFault)(-1);
            NativeProcessObservation observation = await LaunchJob.RunCoreWithWatchdogForTestsAsync(spec, deadline, targets,
                stopped => fault = stopped, CancellationToken.None);
            return (observation, fault == (ChocolateyClientWatchdogFault)(-1) ? ChocolateyClientWatchdogFault.None : fault);
        }

        public void Dispose()
        {
            token?.Dispose();
            try { Registry.CurrentUser.DeleteSubKeyTree(keyBase, throwOnMissingSubKey: false); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            Profile?.Dispose();
        }
    }
}
