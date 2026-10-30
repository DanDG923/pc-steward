using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ReviewedUpdateReviewTests
{
    private const string User = "S-1-5-21-11-22-33-1001";
    private static readonly DateTimeOffset FrozenAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ExplicitConfirmationCreatesOneLiveClaimAttemptOnly()
    {
        ManualClock clock = new();
        ReviewedUpdateOperation operation = Operation();
        ReviewedUpdateReview review = new(operation, clock);
        LiveApprovedUpdate approval = review.Confirm();
        Assert.ThrowsExactly<InvalidOperationException>(() => review.Confirm());
        Assert.IsTrue(approval.TryConsumeClaimAttempt(operation, Owner()));
        Assert.IsFalse(approval.TryConsumeClaimAttempt(operation, Owner()));
    }

    [TestMethod]
    public void IdenticalRestoredDataCannotSubstituteForTheLiveReference()
    {
        ReviewedUpdateOperation operation = Operation();
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(operation.GetCanonicalBytes(), operation.Digest, out ReviewedUpdateOperation? restored));
        LiveApprovedUpdate approval = new ReviewedUpdateReview(operation, new ManualClock()).Confirm();
        Assert.IsFalse(approval.TryConsumeClaimAttempt(restored!, Owner()));
        Assert.IsTrue(approval.TryConsumeClaimAttempt(operation, Owner()));
    }

    [TestMethod]
    [DataRow(0, true)]
    [DataRow(1, false)]
    public void OriginalThirtyMinuteBoundaryIsInclusiveButNeverRenewed(long overTicks, bool expected)
    {
        ManualClock clock = new();
        ReviewedUpdateOperation operation = Operation();
        ReviewedUpdateReview review = new(operation, clock);
        clock.Advance(TimeSpan.FromMinutes(20));
        LiveApprovedUpdate approval = review.Confirm();
        clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromTicks(overTicks));
        Assert.AreEqual(expected, approval.TryConsumeClaimAttempt(operation, Owner()));
    }

    [TestMethod]
    public void ExpiredReviewCannotCreateNewApproval()
    {
        ManualClock clock = new();
        ReviewedUpdateReview review = new(Operation(), clock);
        clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromTicks(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => review.Confirm());
    }

    [TestMethod]
    public void CancellationRemainsBoundAfterConfirmation()
    {
        using CancellationTokenSource cancellation = new();
        ReviewedUpdateOperation operation = Operation();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(operation, new ManualClock(), cancellation.Token).Confirm();
        cancellation.Cancel();
        Assert.IsFalse(approval.TryConsumeClaimAttempt(operation, Owner()));
    }

    [TestMethod]
    public void ObservedClockRollbackPermanentlyInvalidatesApproval()
    {
        ManualClock clock = new();
        ReviewedUpdateOperation operation = Operation();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(operation, clock).Confirm();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsTrue(approval.IsCurrent);
        clock.Utc = FrozenAt.AddSeconds(30);
        Assert.IsFalse(approval.IsCurrent);
        clock.Utc = FrozenAt.AddMinutes(2);
        Assert.IsFalse(approval.TryConsumeClaimAttempt(operation, Owner()));
    }

    [TestMethod]
    public void WrongReleasedOrRecoveryOnlyOwnershipCannotConsumeAClaimAttempt()
    {
        ReviewedUpdateOperation operation = Operation();
        LiveApprovedUpdate approval = new ReviewedUpdateReview(operation, new ManualClock()).Confirm();
        OperationOwnership released = Owner();
        released.Release();
        Assert.IsFalse(approval.TryConsumeClaimAttempt(operation, released));
        Assert.IsFalse(approval.TryConsumeClaimAttempt(operation, new(User, "Local\\PCSteward.DataOperation.v1", false)));
        Assert.IsFalse(approval.TryConsumeClaimAttempt(operation, new(User, OperationGateContract.NamePrefix + User, true)));
        Assert.IsTrue(approval.TryConsumeClaimAttempt(operation, Owner()));
    }

    [TestMethod]
    [DataRow("interactive")]
    [DataRow("downgrade")]
    [DataRow("route")]
    [DataRow("store")]
    [DataRow("machine")]
    public void UnsupportedModeRouteOrSourceCannotMintLiveConfirmation(string change)
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => new ReviewedUpdateReview(Operation(change), new ManualClock()).Confirm());
    }

    [TestMethod]
    public void CapabilityMatrixIsPositiveAndUnknownCombinationsAreUnavailable()
    {
        Assert.AreEqual(ReviewedInstallerMode.Selected(InstallerMode.Silent), UpdateInstallerModeCapabilities.Resolve(UpdateProvider.WinGet, UpdateSource.WinGet, UpdateScope.User));
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve(UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, UpdateScope.Machine));
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve((UpdateProvider)99, UpdateSource.WinGet, UpdateScope.User));
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve(UpdateProvider.WinGet, (UpdateSource)99, UpdateScope.User));
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve(UpdateProvider.WinGet, UpdateSource.WinGet, (UpdateScope)99));
        Assert.IsEmpty(typeof(LiveApprovedUpdate).GetConstructors());
    }

    private static OperationOwnership Owner() => new(User, OperationGateContract.NamePrefix + User, false);

    private static ReviewedUpdateOperation Operation(string? change = null)
    {
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:contoso.widget",
            UpdateProvider.WinGet, change == "store" ? UpdateSource.MicrosoftStore : UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1",
            change == "machine" ? UpdateScope.Machine : UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(FrozenAt);
        return new(job, User, "native:123", "context:123", "evidence:123", FrozenAt.AddSeconds(-1),
            change == "route" ? UpdateRouteContract.ChocolateyMachineRegistrationV2 : UpdateRouteContract.WinGetUserExactV2,
            change == "downgrade" ? ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled)
                : ReviewedInstallerMode.Selected(change == "interactive" ? InstallerMode.Interactive : InstallerMode.Silent));
    }

    private sealed class ManualClock : TimeProvider
    {
        internal DateTimeOffset Utc { get; set; } = FrozenAt;
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => Utc;
        internal void Advance(TimeSpan elapsed) { Utc += elapsed; ticks += elapsed.Ticks; }
    }
}
