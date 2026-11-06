using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Chocolatey Confirm design §3 and §5 items 2, 3 and 6: the two production pieces the review session
/// is composed from, over scripted Desktop-role client-mode observations and fakes (portable: no token, no file, no process).
/// <list type="bullet">
/// <item>The review observation yields evidence only for a fresh, consistent observation of exactly the verified listed release
/// with a newer listed release to offer; everything else is null and observes at most once.</item>
/// <item>The composed dispatcher admits only the exact client self-update shape: the package route and any other package are
/// refused before the inner route dispatcher is asked or reached, and the progress points wrap only the one exchange.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateCompositionTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task AConsistentObservationOfTheVerifiedReleaseIsTheReviewEvidence()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease oldest, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        List<(string Target, string Asked)> calls = [];
        ChocolateyClientReviewEvidence? evidence = await ChocolateyDesktopObservation.ObserveClientReviewAsync((id, expected, _, _) =>
        {
            calls.Add((id, expected.Normalized));
            return ChocolateyClientUpdatePostcheckTests.Observation(oldest);
        }, list, oldest.Version, Budget, new FixedClock(Now), CancellationToken.None);

        Assert.IsNotNull(evidence);
        Assert.AreEqual(oldest.Version, evidence.SourceVersion);
        Assert.AreEqual(newest.Version, evidence.TargetVersion, "the compiled list's offer: the newest listed release");
        Assert.AreEqual(Now, evidence.ObservedAt, "the time the observation started");
        StringAssert.StartsWith(evidence.ProfileEvidenceId, ReviewedUpdateOperation.ProfileEvidencePrefix);
        Assert.IsTrue(ReviewedUpdateOperation.TryGetProfileEvidenceDigest(evidence.ProfileEvidenceId, out _));
        Assert.IsFalse(string.IsNullOrWhiteSpace(evidence.RequestingUserSid));
        CollectionAssert.AreEqual(new[] { (ReviewedUpdateOperation.ChocolateyClientPackageId, oldest.Version) }, calls, "one observation, of exactly the verified release");
    }

    [TestMethod]
    public async Task NoOfferOrAnUnlistedReleaseObservesNothing()
    {
        (ChocolateyReleaseList list, _, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        foreach (string? verified in new[] { newest.Version, "9.9.9", "", null, "2.7.5; rm" })
        {
            int calls = 0;
            Assert.IsNull(await ChocolateyDesktopObservation.ObserveClientReviewAsync((_, _, _, _) => { calls++; return null; }, list, verified, Budget,
                TimeProvider.System, CancellationToken.None), verified ?? "null");
            Assert.AreEqual(0, calls, verified ?? "null");
        }
    }

    [TestMethod]
    [DataRow("other-release")]
    [DataRow("registered-other")]
    [DataRow("mismatch")]
    [DataRow("refused")]
    [DataRow("timed-out")]
    [DataRow("token-refused")]
    [DataRow("throws")]
    public async Task EveryOtherObservationIsNoEvidence(string kind)
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease oldest, ChocolateyReviewedRelease newest) = ChocolateyClientUpdatePostcheckTests.Releases();
        int calls = 0;
        ChocolateyClientReviewEvidence? evidence = await ChocolateyDesktopObservation.ObserveClientReviewAsync((_, _, _, _) =>
        {
            calls++;
            return kind switch
            {
                "other-release" => ChocolateyClientUpdatePostcheckTests.Observation(newest),
                "registered-other" => ChocolateyClientUpdatePostcheckTests.Observation(oldest, installed: newest.Version),
                "mismatch" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.InstalledMismatch, newest.Version, null),
                "refused" => ChocolateyProfileResult.Refused.Create(ChocolateyProfileRefusal.SourceNotCommunity, null, null),
                "timed-out" => ChocolateyProfileResult.TimedOut.Create(),
                "token-refused" => null,
                "throws" => throw new InvalidOperationException("synthetic observer fault"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }, list, oldest.Version, Budget, TimeProvider.System, CancellationToken.None);
        Assert.IsNull(evidence);
        Assert.AreEqual(1, calls, "never a second observation");
    }

    [TestMethod]
    public async Task ACancelledReviewOrAnInvalidBudgetObservesNothing()
    {
        (ChocolateyReleaseList list, ChocolateyReviewedRelease oldest, _) = ChocolateyClientUpdatePostcheckTests.Releases();
        int calls = 0;
        ChocolateyDesktopObserve counting = (_, _, _, _) => { calls++; return ChocolateyClientUpdatePostcheckTests.Observation(oldest); };
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Assert.IsNull(await ChocolateyDesktopObservation.ObserveClientReviewAsync(counting, list, oldest.Version, Budget, TimeProvider.System, cancelled.Token));
        foreach (TimeSpan budget in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), ChocolateyDesktopObservation.Budget + TimeSpan.FromSeconds(1) })
            Assert.IsNull(await ChocolateyDesktopObservation.ObserveClientReviewAsync(counting, list, oldest.Version, budget, TimeProvider.System, CancellationToken.None));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public async Task TheComposedDispatcherAdmitsOnlyTheExactClientShape()
    {
        ReviewedUpdateOperation client = ClientRouteWorkflowAdmissionGateTests.ClientOperation("2.7.5", "2.8.0", new string('A', 64));
        ReviewedUpdateOperation package = PackageOperation();
        RecordingDispatcher inner = new();
        ChocolateyClientUpdatePhaseSignal phases = new();
        RecordingProgress progress = new();
        phases.Attach(progress);
        ClientRouteOnlyDispatcher dispatcher = new(inner, phases);

        Assert.IsTrue(dispatcher.Supports(client));
        Assert.AreEqual(1, inner.SupportCalls);
        Assert.IsFalse(dispatcher.Supports(package), "the package route stays uncomposed");
        Assert.IsFalse(dispatcher.Supports(null!));
        Assert.AreEqual(1, inner.SupportCalls, "the inner dispatcher is never asked about another shape");
        inner.Supported = false;
        Assert.IsFalse(dispatcher.Supports(client), "the route dispatcher's own check (the deployment capability) still applies");
        inner.ThrowOnSupports = true;
        Assert.IsFalse(dispatcher.Supports(client), "a fault is unsupported");
        Assert.IsFalse(ClientRouteOnlyDispatcher.IsClientRoute(null));

        ReviewedUpdateDispatchObservation refused = await dispatcher.DispatchAsync(await Ticket(package), CancellationToken.None);
        Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, refused.Invocation);
        Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, refused.Status);
        Assert.AreEqual("helper-route-unsupported", refused.Diagnostic);
        Assert.AreEqual(0, inner.DispatchCalls, "nothing reaches the helper for another shape");
        Assert.AreEqual(0, progress.Phases.Count, "no progress for a refusal");

        ReviewedUpdateDispatchObservation started = await dispatcher.DispatchAsync(await Ticket(client), CancellationToken.None);
        Assert.AreSame(inner.Observation, started);
        Assert.AreEqual(1, inner.DispatchCalls);
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdatePhase.WaitingForPermission, ChocolateyClientUpdatePhase.Verifying }, progress.Phases.ToArray());

        inner.ThrowOnDispatch = true;
        await Assert.ThrowsExactlyAsync<IOException>(async () => await dispatcher.DispatchAsync(await Ticket(client), CancellationToken.None));
        Assert.AreEqual(ChocolateyClientUpdatePhase.Verifying, progress.Phases[^1], "the verifying point follows the exchange whatever it did");
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () => await dispatcher.DispatchAsync(null!, CancellationToken.None));

        // A progress sink that throws never changes the result.
        phases.Attach(new ThrowingProgress());
        inner.ThrowOnDispatch = false;
        Assert.AreSame(inner.Observation, await dispatcher.DispatchAsync(await Ticket(client), CancellationToken.None));
    }

    [TestMethod]
    public void TheCompositionRequiresEveryPiece()
    {
        ReviewedUpdateGateRunner gate = (work, _, ct) => throw new AssertFailedException("never run");
        IUpdateDispatchStore store = new NullStore();
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> save = (_, _) => throw new AssertFailedException("never saved");
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> read = _ => Task.FromResult<IReadOnlyList<ReviewedUpdateHistoryRecord>?>([]);
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyClientUpdateComposition(null!, store, save, read));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyClientUpdateComposition(gate, null!, save, read));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyClientUpdateComposition(gate, store, null!, read));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyClientUpdateComposition(gate, store, save, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ClientRouteOnlyDispatcher(null!, new ChocolateyClientUpdatePhaseSignal()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ClientRouteOnlyDispatcher(new RecordingDispatcher(), null!));
    }

    private static ReviewedUpdateOperation PackageOperation()
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(Now);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:git.install", "machine:choco", ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64), Now,
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static Task<ReviewedUpdateDispatchTicket> Ticket(ReviewedUpdateOperation operation)
    {
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(operation, Guid.NewGuid()),
            new FreshUpdateDispatchEvidence(operation.Selection, operation.RequestingUserSid, operation.NativeInstanceId,
                operation.InstallationContextId, operation.EvidenceId, operation.ReviewedAt, operation.RouteContract, operation.Mode, true, true),
            operation.ReviewedAt);
        return Task.FromResult(new ReviewedUpdateDispatchTicket(claim));
    }

    private sealed class RecordingDispatcher : IReviewedUpdateRouteDispatcher
    {
        internal int SupportCalls, DispatchCalls;
        internal bool Supported = true, ThrowOnSupports, ThrowOnDispatch;
        internal ReviewedUpdateDispatchObservation Observation { get; } = ReviewedUpdateDispatchObservation.NotStarted(ReviewedUpdateProviderStatus.Failed, "fixture");

        public bool Supports(ReviewedUpdateOperation operation)
        {
            SupportCalls++;
            return ThrowOnSupports ? throw new InvalidOperationException("capability unreadable") : Supported;
        }

        public Task<ReviewedUpdateDispatchObservation> DispatchAsync(ReviewedUpdateDispatchTicket ticket, CancellationToken cancellationToken)
        {
            DispatchCalls++;
            return ThrowOnDispatch ? Task.FromException<ReviewedUpdateDispatchObservation>(new IOException("exchange failed")) : Task.FromResult(Observation);
        }
    }

    private sealed class RecordingProgress : IProgress<ChocolateyClientUpdatePhase>
    {
        internal List<ChocolateyClientUpdatePhase> Phases { get; } = [];
        public void Report(ChocolateyClientUpdatePhase value) => Phases.Add(value);
    }

    private sealed class ThrowingProgress : IProgress<ChocolateyClientUpdatePhase>
    {
        public void Report(ChocolateyClientUpdatePhase value) => throw new InvalidOperationException("sink failed");
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NullStore : IUpdateDispatchStore
    {
        public Task<UpdateDispatchClaim?> TryClaimAsync(ReviewedUpdateDraft draft, FreshUpdateDispatchEvidence evidence, OperationOwnership ownership, DateTimeOffset claimedAt, CancellationToken cancellationToken) =>
            throw new AssertFailedException("never claimed");
        public Task RecordFinalAsync(UpdateDispatchClaim claim, OperationOwnership ownership, UpdateDispatchTerminal terminal, UpdateDispatchSettlement settlement, CancellationToken cancellationToken) =>
            throw new AssertFailedException("never finalized");
        public Task<int> RecoverUnfinishedDispatchesAsync(OperationOwnership ownership, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<UpdateDispatchState?> GetDispatchStateAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<UpdateDispatchState?>(null);
        public Task<bool> HasUnresolvedDispatchesAsync(OperationOwnership ownership, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
