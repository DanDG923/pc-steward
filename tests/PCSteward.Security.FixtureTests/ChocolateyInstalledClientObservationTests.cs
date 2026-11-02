using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UI-03/04, TM-03/04/07/13; ADR-031 ("offered only from a listed, currently trusted client"), design §8.6
/// gate 3: the Desktop's display check of the installed client over scripted Desktop-role client-mode observations and a
/// fixture release list (portable: no token, no file, no process). Only a consistent observation of exactly a listed release
/// is Verified; an InstalledMismatch naming another listed release leads to one second observation only; every other outcome
/// is a closed reason, and nothing but the release's normalized version leaves the check.
/// </summary>
[TestClass]
public sealed class ChocolateyInstalledClientObservationTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    [TestMethod]
    public async Task TheNewestListedReleaseIsObservedFirstAndAConsistentObservationIsVerified()
    {
        (ChocolateyReleaseList list, _, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        List<(string Target, string Asked, TimeSpan Remaining)> calls = [];
        ChocolateyInstalledClientVerification result = await Check(list, (id, expected, remaining, _) =>
        {
            calls.Add((id, expected.Normalized, remaining));
            return expected.Normalized == newest.Version ? ChocolateyClientUpdatePostcheckTests.Observation(newest) : null;
        });
        Assert.AreEqual(ChocolateyInstalledClientCheck.Verified, result.Check);
        Assert.AreEqual(newest.Version, result.InstalledRelease);
        Assert.AreEqual(1, calls.Count, "one observation when the newest release is installed");
        Assert.AreEqual(ReviewedUpdateOperation.ChocolateyClientPackageId, calls[0].Target);
        Assert.AreEqual(newest.Version, calls[0].Asked, "no scan data: the list's newest release is observed first");
        Assert.IsTrue(calls[0].Remaining > TimeSpan.Zero && calls[0].Remaining <= Budget, "within the bound");
    }

    [TestMethod]
    public async Task AnInstalledMismatchOnAnotherListedReleaseLeadsToOneSecondObservationOnly()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease oldest, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        List<string> expected = [];
        ChocolateyInstalledClientVerification result = await Check(list, (_, version, _, _) =>
        {
            expected.Add(version.Normalized);
            return version.Normalized == oldest.Version
                ? ChocolateyClientUpdatePostcheckTests.Observation(oldest)
                : ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, oldest.Version, null);
        });
        Assert.AreEqual(ChocolateyInstalledClientCheck.Verified, result.Check);
        Assert.AreEqual(oldest.Version, result.InstalledRelease);
        CollectionAssert.AreEqual(new[] { newest.Version, oldest.Version }, expected);

        // A second mismatch, a mismatch naming an unlisted or the same release, never leads to a third observation or a release.
        foreach (string named in new[] { "9.9.9", newest.Version })
        {
            int calls = 0;
            ChocolateyInstalledClientVerification refused = await Check(list, (_, _, _, _) =>
            {
                calls++;
                return ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, named, null);
            });
            Assert.AreSame(ChocolateyInstalledClientVerification.Refused, refused, named);
            Assert.AreEqual(1, calls, named);
        }
        int twice = 0;
        ChocolateyInstalledClientVerification second = await Check(list, (_, _, _, _) =>
        {
            twice++;
            return ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, twice == 1 ? oldest.Version : newest.Version, null);
        });
        Assert.AreSame(ChocolateyInstalledClientVerification.Refused, second);
        Assert.AreEqual(2, twice, "at most two observations");
    }

    [TestMethod]
    public async Task AnObservationOfAnotherReleaseOrAnInconsistentOneIsRefused()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease oldest, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        Func<ChocolateyProfileResult?>[] observations =
        [
            // The observer matched another listed release (engine digest), or one from another list.
            () => ChocolateyClientUpdatePostcheckTests.Observation(oldest, installed: newest.Version),
            () => ChocolateyClientUpdatePostcheckTests.Observation(ChocolateyClientUpdatePostcheckTests.Releases().Item3),
            // The registered version is not the expected one.
            () => ChocolateyClientUpdatePostcheckTests.Observation(newest, installed: oldest.Version),
        ];
        foreach (Func<ChocolateyProfileResult?> observation in observations)
            Assert.AreSame(ChocolateyInstalledClientVerification.Refused, await Check(list, (_, _, _, _) => observation()));
    }

    [TestMethod]
    [DataRow("machine-inputs", ChocolateyInstalledClientCheck.Refused)]
    [DataRow("source", ChocolateyInstalledClientCheck.Refused)]
    [DataRow("profile", ChocolateyInstalledClientCheck.Refused)]
    [DataRow("timed-out", ChocolateyInstalledClientCheck.TimedOut)]
    [DataRow("interrupted", ChocolateyInstalledClientCheck.TimedOut)]
    [DataRow("token-refused", ChocolateyInstalledClientCheck.Unavailable)]
    [DataRow("throws", ChocolateyInstalledClientCheck.Unavailable)]
    public async Task EveryOtherOutcomeIsAClosedReason(string kind, ChocolateyInstalledClientCheck expected)
    {
        (ChocolateyReleaseList list, _, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        ChocolateyInstalledClientVerification result = await Check(list, (_, _, _, _) => kind switch
        {
            "machine-inputs" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.MachineInputsUnsupported, null, null),
            "source" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.SourceNotCommunity, null, null),
            "profile" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.ProfileMismatch, newest.Version, new string('D', 64)),
            "timed-out" => ChocolateyProfileResult.TimedOut.Create(),
            "interrupted" => ChocolateyProfileResult.Cancelled.Create(),
            "token-refused" => null,
            "throws" => throw new InvalidOperationException("synthetic observer fault"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        });
        Assert.AreEqual(expected, result.Check);
        Assert.IsNull(result.InstalledRelease);
    }

    [TestMethod]
    public async Task AHangingObservationIsAbandonedAtTheCapAndACallerCancellationIsCancelled()
    {
        (ChocolateyReleaseList list, _, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        // The observer ignores its token; the real-time cap (budget + margin) abandons it.
        using ManualResetEventSlim release = new(false);
        TimeSpan tiny = TimeSpan.FromMilliseconds(50);
        Task<ChocolateyInstalledClientVerification> hanging = ChocolateyDesktopObservation.ObserveInstalledClientAsync((_, _, _, _) =>
        {
            release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            return ChocolateyClientUpdatePostcheckTests.Observation(newest);
        }, list, tiny, TimeProvider.System, CancellationToken.None);
        ChocolateyInstalledClientVerification capped = await hanging.WaitAsync(ChocolateyDesktopObservation.CapMargin + TimeSpan.FromSeconds(20));
        release.Set();
        Assert.AreSame(ChocolateyInstalledClientVerification.TimedOut, capped);

        using CancellationTokenSource dialog = new();
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> observerSawCancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<ChocolateyInstalledClientVerification> running = ChocolateyDesktopObservation.ObserveInstalledClientAsync((_, _, _, token) =>
        {
            started.TrySetResult();
            observerSawCancellation.TrySetResult(token.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)));
            return ChocolateyProfileResult.Cancelled.Create();
        }, list, Budget, TimeProvider.System, dialog.Token);
        await started.Task;
        dialog.Cancel();
        Assert.AreSame(ChocolateyInstalledClientVerification.Cancelled, await running.WaitAsync(TimeSpan.FromSeconds(20)));
        Assert.IsTrue(await observerSawCancellation.Task.WaitAsync(TimeSpan.FromSeconds(20)), "the observer is told to stop");

        int calls = 0;
        Assert.AreSame(ChocolateyInstalledClientVerification.Cancelled,
            await ChocolateyDesktopObservation.ObserveInstalledClientAsync((_, _, _, _) => { calls++; return null; }, list, Budget, TimeProvider.System, dialog.Token));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task AnInvalidBudgetObservesNothing()
    {
        (ChocolateyReleaseList list, _, _) = ChocolateyClientUpdatePostcheckTests.Releases();
        int calls = 0;
        ChocolateyDesktopObserve counting = (_, _, _, _) => { calls++; return null; };
        foreach (TimeSpan budget in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), ChocolateyDesktopObservation.Budget + TimeSpan.FromSeconds(1) })
            Assert.AreSame(ChocolateyInstalledClientVerification.Unavailable,
                await ChocolateyDesktopObservation.ObserveInstalledClientAsync(counting, list, budget, TimeProvider.System, CancellationToken.None));
        Assert.AreEqual(0, calls);
    }

    private static Task<ChocolateyInstalledClientVerification> Check(ChocolateyReleaseList list, ChocolateyDesktopObserve observe) =>
        ChocolateyDesktopObservation.ObserveInstalledClientAsync(observe, list, Budget, TimeProvider.System, CancellationToken.None);
}
