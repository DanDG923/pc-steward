using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;
using F = PCSteward.Security.FixtureTests.EndpointFixture;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01, TM-03/04/07/13; Task 50 design rev 6 §5.4 (rev-3 L-C, rev-4 L5) and §8 N4b, with the N4a obligations
/// (in-flight disposal, nothing started afterwards): the native item's own ordering, one-use launch permission, settlement and
/// disposal rules, driven through the test-only <see cref="ChocolateyHelperItemTestSeam"/>, which replaces only the native
/// stage work with scripted values. Every out-of-order call must return its <c>OutOfOrder</c> arm without reaching the stage
/// work ("no I/O"). Portable.
/// </summary>
[TestClass]
public sealed class NativeHelperItemTests
{
    private static readonly TimeSpan Remaining = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);
    private static readonly string[] FullOrder = ["protect", "protect", "observe", "query", "revalidate", "upgrade"];
    private static readonly string[] Stages = ["observe", "query", "revalidate", "upgrade"];
    private static readonly string[] ObserveQuery = ["observe", "query"];
    private static readonly string[] ObserveOnly = ["observe"];
    private static readonly string[] ProtectObserve = ["protect", "observe"];

    [TestMethod]
    public async Task TheFullOrderRunsEachStageOnceAndLaunchesOnce()
    {
        Script script = new();
        using NativeChocolateyHelperItem item = script.Item();

        Assert.IsTrue(item.RequesterCannotModify(@"C:\Program Files\PC Steward", ["a"]));
        Assert.IsTrue(item.RequesterCannotModify(@"C:\Program Files\PC Steward OwnerOnlyTest State", ["b"]), "G1 checks each root");
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.Matched>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        ChocolateyUpgradeResult result = await item.UpgradeAsync(Remaining, CancellationToken.None);

        Assert.IsInstanceOfType<ChocolateyUpgradeResult.Observed>(result);
        CollectionAssert.AreEqual(FullOrder, script.Calls);
        Assert.IsTrue(item.AggregateSettled);
        // The permission was consumed: no second launch, and no stage runs again.
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsFalse(item.RequesterCannotModify(@"C:\x", []), "G1 runs only before stage B");
        Assert.AreEqual(1, script.Count("upgrade"));
        Assert.AreEqual(6, script.Calls.Count, "no out-of-order call reached the stage work");
    }

    [TestMethod]
    public async Task EveryLaterStageIsOutOfOrderBeforeStageB()
    {
        Script script = new();
        using NativeChocolateyHelperItem item = script.Item();

        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));

        Assert.AreEqual(0, script.Calls.Count);
        // Out-of-order calls change nothing: the proper order still works afterwards.
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.Matched>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
    }

    [TestMethod]
    public async Task TheLaunchPermissionNeedsBothAStageCMatchAndAStageDObservation()
    {
        Script script = new();
        using NativeChocolateyHelperItem item = script.Item();
        item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None);

        // After B: neither D nor the launch.
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        await item.QueryCandidateAsync(Remaining, CancellationToken.None);
        // After C Matched: still no launch without D.
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));

        CollectionAssert.AreEqual(ObserveQuery, script.Calls);
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.Observed>(await item.UpgradeAsync(Remaining, CancellationToken.None));
    }

    [TestMethod]
    [DataRow("refused")]
    [DataRow("timed-out")]
    [DataRow("cancelled")]
    [DataRow("throws")]
    public async Task AStageBThatDoesNotObserveEndsTheSequence(string outcome)
    {
        Script script = new()
        {
            Observe = outcome switch
            {
                "refused" => () => new ChocolateyStageValues.Refused(ChocolateyStageRefusal.InstalledMismatch, F.OtherInstalled, null),
                "timed-out" => () => new ChocolateyStageValues.TimedOut(),
                "cancelled" => () => new ChocolateyStageValues.Cancelled(),
                _ => () => throw new IOException("synthetic"),
            },
        };
        using NativeChocolateyHelperItem item = script.Item();
        if (outcome == "throws")
            Assert.ThrowsExactly<IOException>(() => item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        else
            Assert.IsNotInstanceOfType<ChocolateyStageValues.Observed>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));

        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        CollectionAssert.AreEqual(ObserveOnly, script.Calls);
    }

    [TestMethod]
    [DataRow("refused")]
    [DataRow("undetermined")]
    [DataRow("throws")]
    public async Task AStageCThatDoesNotMatchEndsTheSequence(string outcome)
    {
        Script script = new()
        {
            Query = outcome switch
            {
                "refused" => () => new ChocolateyCandidateValues.Refused(ChocolateyCandidateRefusal.CandidateMismatch, F.Installed, F.OtherCandidate, F.Fingerprint, false),
                "undetermined" => () => new ChocolateyCandidateValues.Undetermined(false, true, true, false),
                _ => () => throw new IOException("synthetic"),
            },
        };
        using NativeChocolateyHelperItem item = script.Item();
        item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None);
        if (outcome == "throws")
            await Assert.ThrowsExactlyAsync<IOException>(() => item.QueryCandidateAsync(Remaining, CancellationToken.None));
        else
            Assert.IsNotInstanceOfType<ChocolateyCandidateValues.Matched>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));

        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        CollectionAssert.AreEqual(ObserveQuery, script.Calls);
    }

    [TestMethod]
    [DataRow("drift")]
    [DataRow("timed-out")]
    [DataRow("throws")]
    public async Task AStageDThatDoesNotObserveMintsNoPermission(string outcome)
    {
        Script script = new()
        {
            Revalidate = outcome switch
            {
                "drift" => () => new ChocolateyStageValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, null, null),
                "timed-out" => () => new ChocolateyStageValues.TimedOut(),
                _ => () => throw new IOException("synthetic"),
            },
        };
        using NativeChocolateyHelperItem item = script.Item();
        item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None);
        await item.QueryCandidateAsync(Remaining, CancellationToken.None);
        if (outcome == "throws")
            Assert.ThrowsExactly<IOException>(() => item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        else
            Assert.IsNotInstanceOfType<ChocolateyStageValues.Observed>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));

        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.AreEqual(0, script.Count("upgrade"));
    }

    [TestMethod]
    public async Task AnUpgradeThatThrowsConsumesThePermissionAndPropagates()
    {
        Script script = new() { Upgrade = () => throw new IOException("synthetic") };
        using NativeChocolateyHelperItem item = await script.PermittedAsync();

        await Assert.ThrowsExactlyAsync<IOException>(() => item.UpgradeAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        Assert.AreEqual(1, script.Count("upgrade"));
    }

    [TestMethod]
    public async Task ConcurrentCallsAreOutOfOrderAndTwoUpgradesNeverBothRun()
    {
        Script script = new();
        TaskCompletionSource<NativeProcessObservation> launch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        script.UpgradeTask = launch.Task;
        using NativeChocolateyHelperItem item = await script.PermittedAsync();

        Task<ChocolateyUpgradeResult> first = item.UpgradeAsync(Remaining, CancellationToken.None);
        Task<ChocolateyUpgradeResult> second = item.UpgradeAsync(Remaining, CancellationToken.None);
        Assert.IsTrue(second.IsCompleted, "the second upgrade is refused before any I/O");
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await second);
        Assert.IsFalse(item.AggregateSettled, "a launch in flight is not settled");
        Assert.IsFalse(item.RequesterCannotModify(@"C:\x", []));

        launch.SetResult(F.Started());
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.Observed>(await first.WaitAsync(Wait));
        Assert.IsTrue(item.AggregateSettled);
        Assert.AreEqual(1, script.Count("upgrade"));
    }

    [TestMethod]
    public async Task ACallDuringAnInFlightStageIsOutOfOrder()
    {
        Script script = new();
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim entered = new();
        script.Observe = () =>
        {
            entered.Set();
            release.Wait(Wait);
            return F.ObservedB();
        };
        using NativeChocolateyHelperItem item = script.Item();
        Task<ChocolateyStageValues> observing = Task.Run(() => item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsTrue(entered.Wait(Wait));

        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsFalse(item.RequesterCannotModify(@"C:\x", []));
        Assert.IsFalse(item.AggregateSettled, "work in flight is not settled");

        release.Set();
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(await observing.WaitAsync(Wait));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.Matched>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        CollectionAssert.AreEqual(ObserveQuery, script.Calls);
    }

    [TestMethod]
    public async Task StageBIsOutOfOrderWhileAG1CheckIsInFlight()
    {
        // N4b review L3: the G1 check is non-exclusive, so stage B also requires no call in flight.
        Script script = new();
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim entered = new();
        script.Protect = () =>
        {
            entered.Set();
            release.Wait(Wait);
            return true;
        };
        using NativeChocolateyHelperItem item = script.Item();
        Task<bool> checking = Task.Run(() => item.RequesterCannotModify(@"C:\Program Files\PC Steward", ["a"]));
        Assert.IsTrue(entered.Wait(Wait));

        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsFalse(item.AggregateSettled, "a G1 check in flight is not settled");

        release.Set();
        Assert.IsTrue(await checking.WaitAsync(Wait));
        // The refused call changed nothing: stage B runs once G1 has returned.
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        CollectionAssert.AreEqual(ProtectObserve, script.Calls);
    }

    [TestMethod]
    public async Task DisposeDuringASynchronousStageCancelsItReleasesAfterItReturnsAndAdmitsNothing()
    {
        Script script = new();
        using ManualResetEventSlim release = new();
        using ManualResetEventSlim entered = new();
        CancellationToken seen = default;
        script.ObserveWithToken = token =>
        {
            seen = token;
            entered.Set();
            release.Wait(Wait, CancellationToken.None);
            return F.ObservedB();
        };
        NativeChocolateyHelperItem item = script.Item();
        Task<ChocolateyStageValues> observing = Task.Run(() => item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsTrue(entered.Wait(Wait));

        item.Dispose();

        Assert.IsTrue(seen.IsCancellationRequested, "the in-flight stage observes the item's lifetime");
        Assert.AreEqual(0, script.Releases, "nothing is released while the stage is still running");
        Assert.IsFalse(item.AggregateSettled);
        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        release.Set();
        await observing.WaitAsync(Wait);
        Assert.AreEqual(1, script.Releases, "released once the last in-flight call returned");
        // Even an Observed that completed after Dispose admits nothing more.
        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        item.Dispose();
        Assert.AreEqual(1, script.Releases, "Dispose is idempotent");
        CollectionAssert.AreEqual(ObserveOnly, script.Calls);
    }

    [TestMethod]
    public async Task DisposeDuringTheLaunchCancelsItAndReleasesOnlyAfterTheLaunchReturns()
    {
        Script script = new();
        TaskCompletionSource<NativeProcessObservation> launch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seen = default;
        script.UpgradeWithToken = token =>
        {
            seen = token;
            return launch.Task;
        };
        NativeChocolateyHelperItem item = await script.PermittedAsync();
        Task<ChocolateyUpgradeResult> upgrading = item.UpgradeAsync(Remaining, CancellationToken.None);

        item.Dispose();

        Assert.IsTrue(seen.IsCancellationRequested, "the launcher's teardown is driven by the item's lifetime");
        Assert.AreEqual(0, script.Releases, "the binding stays held while the launch is in flight");
        launch.SetResult(F.Started(cancelled: true));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.Observed>(await upgrading.WaitAsync(Wait));
        Assert.AreEqual(1, script.Releases);
    }

    [TestMethod]
    public async Task ADisposedItemReachesNoStageWork()
    {
        Script script = new();
        NativeChocolateyHelperItem item = script.Item();
        item.Dispose();

        Assert.AreEqual(1, script.Releases);
        Assert.IsFalse(item.RequesterCannotModify(@"C:\x", []));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyCandidateValues.OutOfOrder>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyStageValues.OutOfOrder>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyUpgradeResult.OutOfOrder>(await item.UpgradeAsync(Remaining, CancellationToken.None));
        Assert.AreEqual(0, script.Calls.Count);
    }

    [TestMethod]
    public async Task TheCallersTokenAndTheBoundReachTheStageWorkAndSettlementFollowsTheLaunches()
    {
        Script script = new();
        using CancellationTokenSource caller = new();
        bool cancelledInside = false;
        script.ObserveWithToken = token =>
        {
            caller.Cancel();
            cancelledInside = token.IsCancellationRequested;
            return F.ObservedB();
        };
        using NativeChocolateyHelperItem item = script.Item();
        item.ObserveProfile(F.Operation(), F.Fingerprint, TimeSpan.FromSeconds(42), caller.Token);
        Assert.AreEqual(TimeSpan.FromSeconds(42), script.Bounds.Single(), "the bound reaches the stage work unchanged");
        Assert.IsTrue(cancelledInside, "the caller's cancellation reaches the stage work");

        script.Settled = false;
        Assert.IsFalse(item.AggregateSettled, "an unsettled launch job is reported");
        script.Settled = true;
        Assert.IsTrue(item.AggregateSettled);
        await Task.CompletedTask;
    }

    [TestMethod]
    public void AThrowingG1CheckIsNotProtected()
    {
        Script script = new() { Protect = () => throw new UnauthorizedAccessException("synthetic") };
        using NativeChocolateyHelperItem item = script.Item();
        Assert.IsFalse(item.RequesterCannotModify(@"C:\Program Files\PC Steward", ["a"]));
        Assert.IsFalse(item.RequesterCannotModify(null!, []));
        Assert.IsFalse(item.RequesterCannotModify(@"C:\x", null!));
        // The G1 check never consumes the stage order.
        Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
    }

    // ---- The native item behind the real stage machine (scripted work) ----

    [TestMethod]
    public async Task TheEndpointOverTheNativeItemReachesMatchedStartedAcceptedByTheDesktop()
    {
        Script script = new();
        EndpointHarness harness = new() { PortOverride = new ScriptedPort(script) };

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(ReviewedHelperEndpointExit.ResponseWritten, run.Result.Exit, run.Result.Diagnostic);
        Assert.AreEqual(ReviewedHelperQualification.Matched, run.Response.Reviewed.Qualification);
        Assert.AreEqual(UpdateWireInvocation.Started, run.Response.Facts.Invocation);
        Assert.IsFalse(run.Interpretation.Observation.ExecutionUncertain);
        CollectionAssert.AreEqual(Stages, script.Calls.Where(call => call != "protect").ToArray());
        Assert.AreEqual(1, script.Releases, "the endpoint disposed the item after the response");
    }

    [TestMethod]
    public async Task AStageBHungPastItsCapIsDisposedInFlightAndNothingMoreStarts()
    {
        Script script = new();
        using ManualResetEventSlim release = new();
        script.Observe = () =>
        {
            release.Wait(Wait);
            return F.ObservedB();
        };
        EndpointHarness harness = new()
        {
            PortOverride = new ScriptedPort(script),
            Budgets = ReviewedHelperEndpointBudgets.Production with
            {
                ExitMargin = TimeSpan.FromMilliseconds(100), ResponseMargin = TimeSpan.FromSeconds(2), LaunchFloor = TimeSpan.FromMilliseconds(200),
            },
        };
        harness.PreElapsed = harness.Budgets.Execution - harness.Budgets.ExitMargin - harness.Budgets.ResponseMargin - TimeSpan.FromMilliseconds(400);

        EndpointRun run = await harness.ExchangeAsync();

        Assert.AreEqual(ReviewedHelperQualification.Undetermined, run.Response.Reviewed.Qualification);
        Assert.IsTrue(run.Response.Facts.TimedOut);
        Assert.AreEqual(0, script.Releases, "the hung stage still runs; nothing is released under it");
        release.Set();
        await script.ReleasedTask.WaitAsync(Wait);
        Assert.AreEqual(1, script.Releases);
        CollectionAssert.AreEqual(ObserveOnly, script.Calls.Where(call => call != "protect").ToArray(), "nothing started after Dispose");
    }

    // ---- ChocolateyBindingIdentity (stage-D drift comparison, L6) ----

    [TestMethod]
    public void TheBindingIdentityMatchesOnlyItself()
    {
        ChocolateyBindingIdentity baseline = Identity();
        Assert.IsTrue(baseline.Matches(Identity()), "equal values match");
        NativeNode engine = Node(@"C:\ProgramData\chocolatey\choco.exe", "choco.exe", 7);
        ChocolateyBindingIdentity[] drifted =
        [
            Identity() with { TargetId = "git.install" },
            Identity() with { EnginePath = @"C:\ProgramData\chocolatey\bin\choco.exe" },
            Identity() with { LaunchPaths = Identity().LaunchPaths with { ProtectedCache = @"C:\other" } },
            Identity() with { Fingerprint = F.OtherFingerprint },
            Identity() with { Installed = F.OtherInstalled },
            Identity() with { ClientState = Identity().ClientState with { CodeDigest = new string('C', 64) } },
            Identity() with { Engine = new ChocolateyBoundFile("choco.exe", engine with { Identity = new NativeFileIdentity(1, 99) }) },
            Identity() with { Engine = new ChocolateyBoundFile("choco.exe", engine with { Length = 8 }) },
            Identity() with { ReleaseFiles = [File(@"helpers\a.ps1", 10), File(@"helpers\b.ps1", 12)] },
            Identity() with { ReleaseFiles = [File(@"helpers\a.ps1", 10)] },
            Identity() with { CodeFiles = [] },
            Identity() with { CodeFiles = [File(@"hooks\x.ps1", 20) with { Node = Node(@"C:\ProgramData\chocolatey\hooks\x.ps1", @"hooks\x.ps1", 20) with { FullPath = @"C:\ProgramData\chocolatey\hooks\X.ps1" } }] },
        ];
        foreach (ChocolateyBindingIdentity changed in drifted)
            Assert.IsFalse(baseline.Matches(changed), changed.ToString());
    }

    private static ChocolateyBindingIdentity Identity() => new("git", @"C:\ProgramData\chocolatey\choco.exe",
        new ChocolateyLaunchPaths(@"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\Users\u", @"C:\Users\u\AppData\Roaming",
            @"C:\Users\u\AppData\Local", @"C:\ProgramData\chocolatey", @"C:\cache"),
        F.Fingerprint, F.Installed, new ChocolateyClientStateData(new string('A', 64), new string('B', 64), null, null),
        new ChocolateyBoundFile("choco.exe", Node(@"C:\ProgramData\chocolatey\choco.exe", "choco.exe", 7)),
        [File(@"helpers\a.ps1", 10), File(@"helpers\b.ps1", 11)], [File(@"hooks\x.ps1", 20)]);

    private static ChocolateyBoundFile File(string relative, long length) =>
        new(relative, Node(@"C:\ProgramData\chocolatey\" + relative, relative, length));

    private static NativeNode Node(string full, string relative, long length) =>
        new(full, relative, false, new NativeFileIdentity(1, (UInt128)(ulong)full.GetHashCode(StringComparison.Ordinal) & 0xFFFF_FFFF), length);

    /// <summary>Scripted stage work for the native item: records each stage call that reached it.</summary>
    private sealed class Script
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock gate = new();
        private int releases;

        internal List<string> Calls { get; } = [];
        internal List<TimeSpan> Bounds { get; } = [];
        internal Func<bool> Protect { get; set; } = () => true;
        internal Func<ChocolateyStageValues> Observe { get; set; } = F.ObservedB;
        internal Func<CancellationToken, ChocolateyStageValues>? ObserveWithToken { get; set; }
        internal Func<ChocolateyCandidateValues> Query { get; set; } = () => new ChocolateyCandidateValues.Matched(F.Candidate);
        internal Func<ChocolateyStageValues> Revalidate { get; set; } = F.ObservedB;
        internal Func<NativeProcessObservation> Upgrade { get; set; } = () => F.Started();
        internal Task<NativeProcessObservation>? UpgradeTask { get; set; }
        internal Func<CancellationToken, Task<NativeProcessObservation>>? UpgradeWithToken { get; set; }
        internal bool Settled { get; set; } = true;
        internal int Releases => Volatile.Read(ref releases);
        internal Task ReleasedTask => released.Task;

        internal int Count(string call)
        {
            lock (gate) return Calls.Count(entry => entry == call);
        }

        internal NativeChocolateyHelperItem Item() => NativeChocolateyHelperItem.CreateForTests(ChocolateyHelperItemTestSeam.CreateForTests(
            (_, _) => Record("protect", Protect),
            (_, _, remaining, token) =>
            {
                lock (gate) Bounds.Add(remaining);
                return Record("observe", () => ObserveWithToken is null ? Observe() : ObserveWithToken(token));
            },
            (_, _) => Task.FromResult(Record("query", Query)),
            (_, _) => Record("revalidate", Revalidate),
            (_, token) => Record("upgrade", () => UpgradeWithToken?.Invoke(token) ?? UpgradeTask ?? Task.FromResult(Upgrade())),
            () => Settled,
            () =>
            {
                Interlocked.Increment(ref releases);
                released.TrySetResult();
            }));

        internal async Task<NativeChocolateyHelperItem> PermittedAsync()
        {
            NativeChocolateyHelperItem item = Item();
            Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.ObserveProfile(F.Operation(), F.Fingerprint, Remaining, CancellationToken.None));
            Assert.IsInstanceOfType<ChocolateyCandidateValues.Matched>(await item.QueryCandidateAsync(Remaining, CancellationToken.None));
            Assert.IsInstanceOfType<ChocolateyStageValues.Observed>(item.RevalidateBeforeLaunch(Remaining, CancellationToken.None));
            return item;
        }

        private T Record<T>(string call, Func<T> run)
        {
            lock (gate) Calls.Add(call);
            return run();
        }
    }

    /// <summary>A port that hands the endpoint the native item over scripted work.</summary>
    private sealed class ScriptedPort(Script script) : IChocolateyHelperQualificationPort
    {
        public IChocolateyPeer? OpenPeer(int pipeServerProcessId) => throw new AssertFailedException("the stage machine never opens the peer");

        public IChocolateyHelperItem? BeginItem(IChocolateyPeer peer, string expectedSid, out ChocolateyItemRefusal refusal)
        {
            refusal = ChocolateyItemRefusal.None;
            return script.Item();
        }
    }
}
