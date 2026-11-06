using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Execution;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Chocolatey package Confirm addendum §5: the production pieces the package review session is composed
/// from, over fakes (portable: no token, no file, no process).
/// <list type="bullet">
/// <item>The composed dispatcher admits only the exact package shape: the client self-update, a client identity on the package
/// route and any other route are refused before the inner route dispatcher is asked or reached, and the progress points wrap only
/// the one exchange. The client composition's dispatcher still refuses the package route.</item>
/// <item>The producer façade's check refuses an invalid or client package ID before anything runs.</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ChocolateyPackageUpdateCompositionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task TheComposedDispatcherAdmitsOnlyTheExactPackageShape()
    {
        ReviewedUpdateOperation package = Operation("ripgrep");
        ReviewedUpdateOperation clientIdentity = Operation("chocolatey");
        ReviewedUpdateOperation extensionIdentity = Operation("Chocolatey.Extension");
        ReviewedUpdateOperation client = ClientRouteWorkflowAdmissionGateTests.ClientOperation("2.7.5", "2.8.0", new string('A', 64));
        RecordingDispatcher inner = new();
        ChocolateyClientUpdatePhaseSignal phases = new();
        List<ChocolateyClientUpdatePhase> reported = [];
        phases.Attach(new Progress(reported));
        PackageRouteOnlyDispatcher dispatcher = new(inner, phases);

        Assert.IsTrue(dispatcher.Supports(package));
        Assert.AreEqual(1, inner.SupportCalls);
        foreach (ReviewedUpdateOperation other in new[] { client, clientIdentity, extensionIdentity })
            Assert.IsFalse(dispatcher.Supports(other), other.Selection.PackageId + " " + other.RouteContract);
        Assert.IsFalse(dispatcher.Supports(null!));
        Assert.AreEqual(1, inner.SupportCalls, "the inner dispatcher is never asked about another shape");
        inner.Supported = false;
        Assert.IsFalse(dispatcher.Supports(package), "the route dispatcher's own check (the deployment capability) still applies");
        inner.ThrowOnSupports = true;
        Assert.IsFalse(dispatcher.Supports(package), "a fault is unsupported");
        Assert.IsFalse(PackageRouteOnlyDispatcher.IsPackageRoute(null));

        foreach (ReviewedUpdateOperation other in new[] { client, clientIdentity })
        {
            ReviewedUpdateDispatchObservation refused = await dispatcher.DispatchAsync(Ticket(other), CancellationToken.None);
            Assert.AreEqual(ReviewedUpdateInvocation.NotStarted, refused.Invocation);
            Assert.AreEqual(ReviewedUpdateProviderStatus.Refused, refused.Status);
            Assert.AreEqual("helper-route-unsupported", refused.Diagnostic);
        }
        Assert.AreEqual(0, inner.DispatchCalls, "nothing reaches the helper for another shape");
        Assert.AreEqual(0, reported.Count, "no progress for a refusal");

        ReviewedUpdateDispatchObservation started = await dispatcher.DispatchAsync(Ticket(package), CancellationToken.None);
        Assert.AreSame(inner.Observation, started);
        Assert.AreEqual(1, inner.DispatchCalls);
        CollectionAssert.AreEqual(new[] { ChocolateyClientUpdatePhase.WaitingForPermission, ChocolateyClientUpdatePhase.Verifying }, reported);

        inner.ThrowOnDispatch = true;
        await Assert.ThrowsExactlyAsync<IOException>(async () => await dispatcher.DispatchAsync(Ticket(package), CancellationToken.None));
        Assert.AreEqual(ChocolateyClientUpdatePhase.Verifying, reported[^1]);
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(async () => await dispatcher.DispatchAsync(null!, CancellationToken.None));

        // Each composition dispatches only its own route.
        ClientRouteOnlyDispatcher clientOnly = new(new RecordingDispatcher(), new ChocolateyClientUpdatePhaseSignal());
        Assert.IsFalse(clientOnly.Supports(package));
        Assert.IsTrue(clientOnly.Supports(client));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("ripgrep; rm")]
    [DataRow("..\\ripgrep")]
    public async Task TheProducerCheckRefusesAnInvalidPackageIdBeforeAnythingRuns(string packageId)
    {
        ChocolateyPackageUpdateProducers producers = new();
        Assert.IsNull(await producers.CheckReviewIntentAsync(packageId, CancellationToken.None));
        Assert.IsNotNull(producers.EvidenceSource);
        Assert.IsNotNull(producers.Verifier);
    }

    [TestMethod]
    public async Task ACancelledCheckRunsNothing()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        Assert.IsNull(await new ChocolateyPackageUpdateProducers().CheckReviewIntentAsync("ripgrep", cancel.Token));
    }

    [TestMethod]
    public void TheCompositionRequiresEveryPiece()
    {
        ReviewedUpdateGateRunner gate = (work, _, ct) => throw new AssertFailedException("never run");
        IUpdateDispatchStore store = new NullStore();
        Func<ReviewedUpdateOperation, CancellationToken, Task<ReviewedUpdateDraft>> save = (_, _) => throw new AssertFailedException("never saved");
        Func<CancellationToken, Task<IReadOnlyList<ReviewedUpdateHistoryRecord>?>> read = _ => Task.FromResult<IReadOnlyList<ReviewedUpdateHistoryRecord>?>([]);
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyPackageUpdateComposition(null!, store, save, read));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyPackageUpdateComposition(gate, null!, save, read));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyPackageUpdateComposition(gate, store, null!, read));
        Assert.ThrowsExactly<ArgumentNullException>(() => new ChocolateyPackageUpdateComposition(gate, store, save, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PackageRouteOnlyDispatcher(null!, new ChocolateyClientUpdatePhaseSignal()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new PackageRouteOnlyDispatcher(new RecordingDispatcher(), null!));
    }

    private static ReviewedUpdateOperation Operation(string packageId)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:" + packageId,
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, packageId, "14.1.0", "15.2.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-package-v1")).Confirm(Now);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:" + packageId, "machine:chocolatey", ReviewedUpdateOperation.ProfileEvidencePrefix + new string('A', 64), Now,
            UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    private static ReviewedUpdateDispatchTicket Ticket(ReviewedUpdateOperation operation)
    {
        UpdateDispatchClaim claim = new(Guid.NewGuid(), Guid.NewGuid(), new ReviewedUpdateDraft(operation, Guid.NewGuid()),
            new FreshUpdateDispatchEvidence(operation.Selection, operation.RequestingUserSid, operation.NativeInstanceId,
                operation.InstallationContextId, operation.EvidenceId, operation.ReviewedAt, operation.RouteContract, operation.Mode, true, true),
            operation.ReviewedAt);
        return new ReviewedUpdateDispatchTicket(claim);
    }

    private sealed class Progress(List<ChocolateyClientUpdatePhase> phases) : IProgress<ChocolateyClientUpdatePhase>
    {
        public void Report(ChocolateyClientUpdatePhase value) => phases.Add(value);
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
