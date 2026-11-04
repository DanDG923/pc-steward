using System.Security.AccessControl;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Task 50 qualification probe spec rev 4 §3 (S3 observer additions, re-spec trigger), §4.4 (native
/// enum pins) and §7 H9, H11, H12 (slice S3). Portable: the native/wire enum pins by exact name and value, the one-entry release
/// list trigger, the saturating per-instance sink, the probe value shapes and the native probe item's ordering and disposal over
/// its seam. Windows-only (reported as failed elsewhere): the walker, ancestry, observer and G1 protection results with and
/// without the new sink, the access verdict a sink sees for a failed open and for an evaluated refusal, and a native B′
/// observation whose binding is disposed before the value is returned.
/// </summary>
[TestClass]
public sealed class QualificationProbeHelperNativeTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);
    private static readonly Func<string, bool, ChocolateyTreeContentPolicy> Classify = ChocolateyTreeCoveragePolicy.Classify;

    // ---- §4.4: native enums pinned against the Execution wire enums ----

    [TestMethod]
    public void TheNativeTokenVerdictCheckAndAccessEnumsEqualTheWireEnumsByExactNameAndValue()
    {
        AssertSameEnum<TokenVerdict, QualificationProbeTokenVerdict>();
        AssertSameEnum<ChocolateyProbeFailedCheck, QualificationProbeFailedCheck>();
        AssertSameEnum<ChocolateyProbeFailedAccess, QualificationProbeFailedAccess>();
        foreach (TokenVerdict verdict in Enum.GetValues<TokenVerdict>())
        {
            Assert.IsTrue(ReviewedHelperProbeEndpoint.TryMapByName(verdict, out QualificationProbeTokenVerdict wire));
            Assert.AreEqual(verdict.ToString(), wire.ToString());
        }
        Assert.IsFalse(ReviewedHelperProbeEndpoint.TryMapByName((TokenVerdict)99, out QualificationProbeTokenVerdict _), "an undefined value never maps");
    }

    // ---- H11: the frozenInstalled re-spec trigger ----

    [TestMethod]
    public void H11TheCompiledReleaseListHasExactlyOneEntry()
    {
        Assert.AreEqual(1, ChocolateyReleaseList.Listed.Releases.Count,
            "Task 50 qualification probe spec rev 4 §3 re-spec trigger (review L-1, H11): before any build lists a second Chocolatey release, " +
            "the probe spec must be revised (frozenInstalled is the newest listed release, so a host on an older listed release could never pass).");
        Assert.AreEqual(ChocolateyReleaseList.Listed.Releases[^1].Version, QualificationProbeContract.Compiled.ListedClientVersions[^1],
            "the probe item's frozenInstalled is the codec's Q10 newest listed release");
    }

    // ---- H9: per-instance, saturating counts ----

    [TestMethod]
    public void H9TheSinkCountsPerInstanceAndSaturatesAtItsMaximum()
    {
        RequesterAccessSink first = new(maximumNodes: 3, maximumAncestors: 2);
        RequesterAccessSink second = new(maximumNodes: 3, maximumAncestors: 2);
        for (int index = 0; index < 5; index++)
        {
            first.CountNode();
            first.CountAncestor();
        }
        second.CountNode();
        Assert.AreEqual(3, first.NodesEvaluated, "a count over the cap reports the cap");
        Assert.AreEqual(2, first.AncestorsEvaluated);
        Assert.AreEqual(1, second.NodesEvaluated, "another instance is independent");
        Assert.AreEqual(0, second.AncestorsEvaluated);

        ChocolateyObservationReport one = new(), two = new();
        one.Access.CountNode();
        Assert.AreEqual(1, one.NodesEvaluated);
        Assert.AreEqual(0, two.NodesEvaluated, "two observations report independent counts");
        Parallel.For(0, 10_000, _ => two.Access.CountAncestor());
        Assert.AreEqual(10_000, two.AncestorsEvaluated, "concurrent counting loses nothing below the cap");
    }

    [TestMethod]
    public void H9TheReportsCapsAreTheWireMaximaAndTheFailureIsRecordedOnce()
    {
        ChocolateyObservationReport report = new();
        Assert.IsNull(report.FailedStep);
        Assert.AreEqual(ChocolateyProbeFailedCheck.None, report.FailedCheck);
        Assert.AreEqual(ChocolateyProbeFailedAccess.None, report.FailedAccess);
        report.RecordFailure(5, ChocolateyProbeFailedCheck.StepRule, ChocolateyProbeFailedAccess.Granted);
        report.RecordFailure(8, ChocolateyProbeFailedCheck.InstalledVersion, ChocolateyProbeFailedAccess.Other);
        Assert.AreEqual(5, report.FailedStep);
        Assert.AreEqual(ChocolateyProbeFailedCheck.StepRule, report.FailedCheck);
        Assert.AreEqual(ChocolateyProbeFailedAccess.Granted, report.FailedAccess);
        Assert.AreEqual(QualificationProbeWireCodec.MaximumNodesEvaluated, Runtime(ChocolateyProbeValues.MaximumNodesEvaluated));
        Assert.AreEqual(QualificationProbeWireCodec.MaximumAncestorsEvaluated, Runtime(ChocolateyProbeValues.MaximumAncestorsEvaluated));
        RequesterAccessSink sink = new(1, 1);
        Assert.IsFalse(sink.WalkRefusalRecorded);
        sink.RecordWalkRefusal(null);
        Assert.IsTrue(sink.WalkRefusalRecorded);
        Assert.IsNull(sink.WalkRefusalVerdict, "a refusal that was not an access verdict carries none");
    }

    // ---- The probe value shapes ----

    [TestMethod]
    public void TheProbeValuesRefuseInconsistentShapes()
    {
        Assert.AreEqual("2.7.5", new ChocolateyProbeValues.Observed("2.7.5", 64, 1, 1).ClientVersion);
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyProbeValues.Observed("2.7", 64, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.Observed("2.7.5", 63, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.Observed("2.7.5", 64, 1_048_577, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.TimedOut(0, 65_537));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.ProfileMismatch, 9,
            ChocolateyProbeFailedCheck.StepRule, ChocolateyProbeFailedAccess.Other, null, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, 0,
            ChocolateyProbeFailedCheck.StepRule, ChocolateyProbeFailedAccess.Other, null, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, 3,
            ChocolateyProbeFailedCheck.None, ChocolateyProbeFailedAccess.Other, null, 1, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.MachineInputsUnsupported, 3,
            ChocolateyProbeFailedCheck.StepRule, ChocolateyProbeFailedAccess.None, null, 1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.InstalledMismatch, 8,
            ChocolateyProbeFailedCheck.InstalledVersion, ChocolateyProbeFailedAccess.Other, null, 1, 1));
        Assert.ThrowsExactly<ArgumentException>(() => new ChocolateyProbeValues.Refused(ChocolateyStageRefusal.SourceNotCommunity, 8,
            ChocolateyProbeFailedCheck.StepRule, ChocolateyProbeFailedAccess.Other, "2.7.5", 1, 1));
    }

    // ---- The native probe item over its seam (ordering, N4b) ----

    [TestMethod]
    public void TheNativeProbeItemAdmitsTheG1CheckOnlyBeforeOneObservationAndNothingAfterDispose()
    {
        int protects = 0, observes = 0, releases = 0;
        NativeChocolateyProbeItem item = NativeChocolateyProbeItem.CreateForTests(ChocolateyProbeItemTestSeam.CreateForTests(
            (_, _) => ++protects > 0, (_, _) =>
            {
                observes++;
                return ProbeFixture.Observed();
            }, () => releases++));
        Assert.IsTrue(item.RequesterCannotModify(@"C:\Program Files\PC Steward", ["x"]));
        Assert.IsInstanceOfType<ChocolateyProbeValues.Observed>(item.ObserveClient(Budget, CancellationToken.None));
        Assert.IsInstanceOfType<ChocolateyProbeValues.Faulted>(item.ObserveClient(Budget, CancellationToken.None), "the observation runs once");
        Assert.IsFalse(item.RequesterCannotModify(@"C:\Program Files\PC Steward", ["x"]), "no G1 check after the observation");
        Assert.AreEqual(1, protects);
        Assert.AreEqual(1, observes);
        item.Dispose();
        item.Dispose();
        Assert.AreEqual(1, releases, "the token is released once");
        Assert.IsFalse(item.RequesterCannotModify(@"C:\Program Files\PC Steward", ["x"]));
        Assert.AreEqual(1, protects);
    }

    [TestMethod]
    public void H10TheNativeProbeItemDisposeDoesNotWaitForAnInFlightCallAndReleasesWhenItReturns()
    {
        using ManualResetEventSlim entered = new(), release = new();
        bool cancelledInside = false;
        int releases = 0;
        NativeChocolateyProbeItem item = NativeChocolateyProbeItem.CreateForTests(ChocolateyProbeItemTestSeam.CreateForTests(
            (_, _) =>
            {
                entered.Set();
                release.Wait(ProbeFixture.Wait, CancellationToken.None);
                return true;
            },
            (_, _) => ProbeFixture.Observed(), () => Interlocked.Increment(ref releases)));
        Task<bool> protect = Task.Run(() => item.RequesterCannotModify(@"C:\Program Files\PC Steward", []));
        Assert.IsTrue(entered.Wait(ProbeFixture.Wait, CancellationToken.None));
        Task dispose = Task.Run(item.Dispose);
        Assert.IsTrue(dispose.Wait(ProbeFixture.Wait), "Dispose returns while the call is in flight");
        Assert.AreEqual(0, Volatile.Read(ref releases), "nothing is released while the call runs");
        Assert.IsInstanceOfType<ChocolateyProbeValues.Faulted>(item.ObserveClient(Budget, CancellationToken.None), "nothing new is admitted");
        release.Set();
        Assert.IsTrue(protect.Wait(ProbeFixture.Wait));
        Assert.AreEqual(1, Volatile.Read(ref releases), "released once the last call returned");

        // An observation in flight sees the item's lifetime token cancelled by Dispose.
        using ManualResetEventSlim observing = new(), go = new();
        NativeChocolateyProbeItem second = NativeChocolateyProbeItem.CreateForTests(ChocolateyProbeItemTestSeam.CreateForTests((_, _) => true,
            (_, token) =>
            {
                observing.Set();
                go.Wait(ProbeFixture.Wait, CancellationToken.None);
                cancelledInside = token.IsCancellationRequested;
                return new ChocolateyProbeValues.Cancelled();
            }, () => { }));
        Task<ChocolateyProbeValues> observe = Task.Run(() => second.ObserveClient(Budget, CancellationToken.None));
        Assert.IsTrue(observing.Wait(ProbeFixture.Wait, CancellationToken.None));
        second.Dispose();
        go.Set();
        Assert.IsInstanceOfType<ChocolateyProbeValues.Cancelled>(observe.Result);
        Assert.IsTrue(cancelledInside);
    }

    // ---- Windows: H12 existing callers unchanged with and without the sink ----

    [TestMethod]
    public void H12TheWalkResultIsTheSameWithAndWithoutTheSinkAndTheSinkCountsEveryNodeEvaluation()
    {
        N1NativeFixture.RequireWindows("H12 walker with and without the sink");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = Tree(fixture);
        fixture.LockTree(root);

        TreeWalkResult without = ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None);
        RequesterAccessSink sink = new(int.MaxValue, int.MaxValue);
        TreeWalkResult with = ProtectedTreeWalker.WalkWithSink(token, root, TreeWalkBounds.Profile, Classify, sink, CancellationToken.None);
        AssertSameWalk(without, with);
        Assert.AreEqual(((TreeWalkResult.Complete)with).Nodes.Count, sink.NodesEvaluated, "the root and every child are evaluated in Node mode once");
        Assert.IsFalse(sink.WalkRefusalRecorded);

        // An evaluated refusal reports its verdict (Granted).
        fixture.Protect(Path.Combine(root, "d", "b.txt"), ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.WriteData);
        sink = new(int.MaxValue, int.MaxValue);
        AssertSameWalk(ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None),
            ProtectedTreeWalker.WalkWithSink(token, root, TreeWalkBounds.Profile, Classify, sink, CancellationToken.None));
        Assert.AreEqual(RequesterModification.Granted, sink.WalkRefusalVerdict);
        fixture.Lock(Path.Combine(root, "d", "b.txt"));

        // A junction (reparse) is not an access verdict.
        string outside = fixture.Directory("outside");
        fixture.Lock(outside);
        N1NativeFixture.Junction(Path.Combine(root, "d", "j"), outside);
        sink = new(int.MaxValue, int.MaxValue);
        TreeWalkResult reparse = ProtectedTreeWalker.WalkWithSink(token, root, TreeWalkBounds.Profile, Classify, sink, CancellationToken.None);
        AssertSameWalk(ProtectedTreeWalker.Walk(token, root, TreeWalkBounds.Profile, Classify, CancellationToken.None), reparse);
        Assert.AreEqual(TreeWalkRefusal.Reparse, ((TreeWalkResult.Refused)reparse).Reason);
        Assert.IsTrue(sink.WalkRefusalRecorded);
        Assert.IsNull(sink.WalkRefusalVerdict, "a reparse point is not an access verdict");

        // A failed open is Unreadable for the walk but not an access verdict for the sink (failedAccess = Other).
        string other = fixture.Directory("other");
        string unopenable = fixture.File(@"other\u.txt");
        fixture.LockTree(other);
        // The test process cannot open it; it is deleted through the parent's FILE_DELETE_CHILD.
        fixture.ProtectExactly(unopenable, (N1NativeFixture.OwnerRights, FileSystemRights.Synchronize));
        sink = new(int.MaxValue, int.MaxValue);
        TreeWalkResult refused = ProtectedTreeWalker.WalkWithSink(token, other, TreeWalkBounds.Profile, Classify, sink, CancellationToken.None);
        AssertSameWalk(ProtectedTreeWalker.Walk(token, other, TreeWalkBounds.Profile, Classify, CancellationToken.None), refused);
        Assert.AreEqual(TreeWalkRefusal.Unreadable, ((TreeWalkResult.Refused)refused).Reason);
        Assert.IsTrue(sink.WalkRefusalRecorded);
        Assert.IsNull(sink.WalkRefusalVerdict, "a failed open is not an access verdict");
    }

    [TestMethod]
    public void H12AncestryIsTheSameWithAndWithoutTheSinkAndTheSinkCountsEveryAncestor()
    {
        N1NativeFixture.RequireWindows("H12 ancestry with and without the sink");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N1NativeFixture fixture = new();
        string root = Tree(fixture);
        foreach (string path in new[] { root, Environment.SystemDirectory, Path.Combine(Environment.SystemDirectory, "drivers", "etc"), @"C:\", "relative" })
        {
            RequesterAccessSink sink = new(int.MaxValue, int.MaxValue);
            RequesterModification without = RequesterAccess.EvaluateAncestry(token, path);
            Assert.AreEqual(without, RequesterAccess.EvaluateAncestryWithSink(token, path, sink), path);
        }
        string system = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
        RequesterAccessSink counted = new(int.MaxValue, int.MaxValue);
        RequesterModification verdict = RequesterAccess.EvaluateAncestryWithSink(token, system, counted);
        int ancestors = system.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length - 1;
        Assert.IsGreaterThan(0, counted.AncestorsEvaluated);
        Assert.IsLessThanOrEqualTo(ancestors, counted.AncestorsEvaluated);
        if (verdict == RequesterModification.Denied)
            Assert.AreEqual(ancestors, counted.AncestorsEvaluated, "every directory from the drive root down to the parent is evaluated in Ancestor mode once");
    }

    [TestMethod]
    public void H12TheObserverResultIsTheSameWithAndWithoutTheReportAndTheReportNamesTheFailedStep()
    {
        N1NativeFixture.RequireWindows("H12 observer with and without the report");
        using RequesterToken token = N1NativeFixture.DerivedToken();
        using N2ProfileFixture fixture = new();
        fixture.Build();
        fixture.Lock();
        ChocolateyProfileResult without = ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Target,
            N2ProfileFixture.Version("1.2.0"), null, Budget, fixture.Seam(), CancellationToken.None);
        ChocolateyObservationReport report = new();
        ChocolateyProfileResult with = ChocolateyProfileObserver.ObserveWithReportForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Target,
            N2ProfileFixture.Version("1.2.0"), null, Budget, fixture.Seam(), report, CancellationToken.None);
        AssertSameObservation(without, with);
        Assert.IsGreaterThan(0, report.NodesEvaluated);
        Assert.IsGreaterThan(0, report.AncestorsEvaluated);
        Assert.IsNull(report.FailedStep, "an observed result records no failure");

        // Step 8a: the installed value differs from the frozen value.
        report = new();
        with = ChocolateyProfileObserver.ObserveWithReportForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Target,
            N2ProfileFixture.Version("9.9.9"), null, Budget, fixture.Seam(), report, CancellationToken.None);
        AssertSameObservation(ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Target,
            N2ProfileFixture.Version("9.9.9"), null, Budget, fixture.Seam(), CancellationToken.None), with);
        Assert.AreEqual(8, report.FailedStep);
        Assert.AreEqual(ChocolateyProbeFailedCheck.InstalledVersion, report.FailedCheck);
        Assert.AreEqual(ChocolateyProbeFailedAccess.Other, report.FailedAccess);

        // Step 1: a requester-modifiable root is an access verdict (Granted).
        fixture.Native.Protect(fixture.Root, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.CreateFiles);
        report = new();
        with = ChocolateyProfileObserver.ObserveWithReportForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Target,
            N2ProfileFixture.Version("1.2.0"), null, Budget, fixture.Seam(), report, CancellationToken.None);
        AssertSameObservation(ChocolateyProfileObserver.ObserveForTests(token, ChocolateyObserverRole.Desktop, N2ProfileFixture.Target,
            N2ProfileFixture.Version("1.2.0"), null, Budget, fixture.Seam(), CancellationToken.None), with);
        Assert.AreEqual(1, report.FailedStep);
        Assert.AreEqual(ChocolateyProbeFailedCheck.StepRule, report.FailedCheck);
        Assert.AreEqual(ChocolateyProbeFailedAccess.Granted, report.FailedAccess);
    }

    [TestMethod]
    public void H12TheUpdateItemsG1ProtectionIsUnchangedAndEqualsTheProbeItems()
    {
        N1NativeFixture.RequireWindows("H12 G1 protection rule shared by both items");
        using N1NativeFixture fixture = new();
        string root = fixture.Directory("install");
        string file = fixture.File(@"install\PCSteward.ElevatedHelper.exe");
        // Lock the whole fixture tree, as G1Fixture does: the ancestry walk starts at the fixture root, which the CI-hardened
        // fixture leaves user-writable until it is locked (first Windows run: the locked case evaluated false).
        fixture.LockTree(fixture.Root);
        foreach (bool locked in new[] { true, false })
        {
            if (!locked) fixture.Protect(file, ownerRights: true, FileSystemRights.ReadAndExecute | FileSystemRights.WriteData);
            using NativeChocolateyHelperItem update = NativeChocolateyHelperItem.CreateOverFixtureForTests(N1NativeFixture.DerivedToken(),
                static (_, _, _, _, _, _) => throw new AssertFailedException("no observation"), fixture.Root, stageCForTests: null);
            using NativeChocolateyProbeItem probe = NativeChocolateyProbeItem.CreateOverFixtureForTests(N1NativeFixture.DerivedToken(),
                static (_, _, _, _, _) => throw new AssertFailedException("no observation"), fixture.Root);
            using RequesterToken token = N1NativeFixture.DerivedToken();
            bool rule = ChocolateyRequesterProtection.CannotModify(token, root, [file], fixture.Root);
            Assert.AreEqual(locked, rule, "the fixture is protected only while locked");
            Assert.AreEqual(rule, update.RequesterCannotModify(root, [file]));
            Assert.AreEqual(rule, probe.RequesterCannotModify(root, [file]));
        }
    }

    // ---- Windows: H2/H9/H10 native B′ ----

    [TestMethod]
    public void TheNativeProbeObservationDisposesTheBindingBeforeReturningAndReportsIndependentCounts()
    {
        N1NativeFixture.RequireWindows("native probe B′ over the N2 client fixture");
        using N2ProfileFixture fixture = ClientFixture();
        ChocolateyReleaseList list = ChocolateyReleaseList.CreateForTests([fixture.ReleaseEntry(ProbeFixture.NewestListed, new string('B', 64))]);
        ChocolateyObserverTestSeam seam = fixture.Seam(list);
        List<ChocolateyProfileResult> returned = [];
        ChocolateyProbeValues Observe()
        {
            using NativeChocolateyProbeItem item = NativeChocolateyProbeItem.CreateOverFixtureForTests(N1NativeFixture.DerivedToken(),
                (token, frozen, remaining, report, cancellationToken) =>
                {
                    ChocolateyProfileResult result = ChocolateyProfileObserver.ObserveClientWithReportForTests(token, ChocolateyObserverRole.Helper, frozen,
                        null, remaining, seam, report, cancellationToken);
                    returned.Add(result);
                    return result;
                }, fixture.Native.Root);
            return item.ObserveClient(Budget, CancellationToken.None);
        }

        ChocolateyProbeValues.Observed first = (ChocolateyProbeValues.Observed)Observe();
        ChocolateyProbeValues.Observed second = (ChocolateyProbeValues.Observed)Observe();
        Assert.AreEqual(ProbeFixture.NewestListed, first.ClientVersion);
        Assert.AreEqual(64, first.FingerprintLength);
        Assert.IsGreaterThan(0, first.NodesEvaluated);
        Assert.IsGreaterThan(0, first.AncestorsEvaluated);
        Assert.AreEqual(first.NodesEvaluated, second.NodesEvaluated, "counts are per observation, not cumulative");
        Assert.AreEqual(first.AncestorsEvaluated, second.AncestorsEvaluated);
        foreach (ChocolateyProfileResult result in returned)
            Assert.IsTrue(((ChocolateyProfileResult.Observed)result).Observation.HeldClient!.IsDisposed, "the binding was disposed before the value was returned");
    }

    [TestMethod]
    public void H10ABindingAnAbandonedObservationReturnsAfterDisposeIsDisposedAndNeverHandedOut()
    {
        N1NativeFixture.RequireWindows("native probe B′ returning after Dispose");
        using N2ProfileFixture fixture = ClientFixture();
        ChocolateyReleaseList list = ChocolateyReleaseList.CreateForTests([fixture.ReleaseEntry(ProbeFixture.NewestListed, new string('B', 64))]);
        ChocolateyObserverTestSeam seam = fixture.Seam(list);
        using ManualResetEventSlim observed = new(), disposed = new();
        ChocolateyProfileResult? returned = null;
        NativeChocolateyProbeItem item = NativeChocolateyProbeItem.CreateOverFixtureForTests(N1NativeFixture.DerivedToken(),
            (token, frozen, remaining, report, _) =>
            {
                // The observation completes, then is held back until after Dispose (an abandoned call returning late).
                returned = ChocolateyProfileObserver.ObserveClientWithReportForTests(token, ChocolateyObserverRole.Helper, frozen, null, remaining, seam,
                    report, CancellationToken.None);
                observed.Set();
                disposed.Wait(ProbeFixture.Wait, CancellationToken.None);
                return returned;
            }, fixture.Native.Root);
        Task<ChocolateyProbeValues> observe = Task.Run(() => item.ObserveClient(Budget, CancellationToken.None));
        Assert.IsTrue(observed.Wait(ProbeFixture.Wait, CancellationToken.None));
        item.Dispose();
        disposed.Set();
        Assert.IsTrue(observe.Wait(ProbeFixture.Wait));
        Assert.IsTrue(((ChocolateyProfileResult.Observed)returned!).Observation.HeldClient!.IsDisposed, "disposed by the item, never handed out");
        Assert.IsFalse(item.RequesterCannotModify(fixture.Root, []), "nothing new starts after Dispose");
    }

    // ---- Helpers ----

    /// <summary>A compile-time constant read at run time, so the pin compares the two declarations.</summary>
    private static T Runtime<T>(T value) => value;

    private static void AssertSameEnum<TNative, TWire>() where TNative : struct, Enum where TWire : struct, Enum
    {
        CollectionAssert.AreEqual(Enum.GetNames<TNative>(), Enum.GetNames<TWire>(), $"{typeof(TNative).Name} names");
        CollectionAssert.AreEqual(Enum.GetValues<TNative>().Select(value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            Enum.GetValues<TWire>().Select(value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray(), $"{typeof(TNative).Name} values");
    }

    private static string Tree(N1NativeFixture fixture)
    {
        string root = fixture.Directory("root");
        fixture.File(@"root\a.txt", "abc");
        fixture.Directory(@"root\d\e");
        fixture.File(@"root\d\b.txt");
        fixture.File(@"root\d\e\c.txt");
        fixture.Directory(@"root\logs");
        fixture.File(@"root\logs\x.log");
        return root;
    }

    private static void AssertSameWalk(TreeWalkResult expected, TreeWalkResult actual)
    {
        switch (expected)
        {
            case TreeWalkResult.Complete complete:
                TreeWalkResult.Complete other = actual as TreeWalkResult.Complete ?? throw new AssertFailedException($"expected Complete, got {actual}");
                CollectionAssert.AreEqual(complete.Nodes.ToArray(), other.Nodes.ToArray());
                break;
            default:
                Assert.AreEqual(expected, actual);
                break;
        }
    }

    private static void AssertSameObservation(ChocolateyProfileResult expected, ChocolateyProfileResult actual)
    {
        Assert.AreEqual(expected.GetType(), actual.GetType());
        switch (expected)
        {
            case ChocolateyProfileResult.Observed observed:
                Assert.AreEqual(observed.Observation.Fingerprint, ((ChocolateyProfileResult.Observed)actual).Observation.Fingerprint);
                break;
            case ChocolateyProfileResult.Refused refused:
                ChocolateyProfileResult.Refused other = (ChocolateyProfileResult.Refused)actual;
                Assert.AreEqual(refused.Reason, other.Reason);
                Assert.AreEqual(refused.Detail, other.Detail);
                Assert.AreEqual(refused.ObservedInstalled, other.ObservedInstalled);
                Assert.AreEqual(refused.ObservedFingerprint, other.ObservedFingerprint);
                break;
        }
    }

    /// <summary>The N2 tree listed as the newest compiled release with the client registered at it (client mode, Helper role).</summary>
    private static N2ProfileFixture ClientFixture()
    {
        N2ProfileFixture fixture = new();
        fixture.Build();
        fixture.WritePackage(@"lib\chocolatey\chocolatey.nupkg", "chocolatey", ProbeFixture.NewestListed);
        fixture.Lock();
        // As the N2 helper positive: the helper cache is an existing protected system directory (the one seam substitution).
        fixture.HttpCache = N1NativeFixture.FinalPathOf(Path.Combine(Environment.SystemDirectory, "drivers", "etc"), directory: true);
        return fixture;
    }
}
