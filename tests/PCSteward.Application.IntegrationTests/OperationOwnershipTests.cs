using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class OperationOwnershipTests
{
    private const string User = "S-1-5-21-11-22-33-1001";

    [TestMethod]
    public void OnlyAnActiveMatchingProductionGateCanSatisfyTheDispatchPredicate()
    {
        OperationOwnership ownership = new(User, OperationGateContract.NamePrefix + User, false);
        Assert.IsTrue(ownership.IsActive);
        Assert.IsFalse(ownership.RecoveryRequired);
        Assert.IsTrue(ownership.CanDispatchFor(Operation()));
        ownership.Release();
        Assert.IsFalse(ownership.IsActive);
        Assert.IsFalse(ownership.CanDispatchFor(Operation()));
        Assert.ThrowsExactly<InvalidOperationException>(ownership.MarkReconciled);
    }

    [TestMethod]
    public void AbandonedOwnershipMustBeReconciledAndCannotBeRevivedAfterRelease()
    {
        OperationOwnership ownership = new(User, OperationGateContract.NamePrefix + User, true);
        Assert.IsTrue(ownership.IsActive);
        Assert.IsTrue(ownership.RecoveryRequired);
        Assert.IsFalse(ownership.CanDispatchFor(Operation()));
        ownership.MarkReconciled();
        Assert.IsFalse(ownership.RecoveryRequired);
        Assert.IsTrue(ownership.CanDispatchFor(Operation()));
        ownership.Release();
        Assert.ThrowsExactly<InvalidOperationException>(ownership.MarkReconciled);
        Assert.IsFalse(ownership.CanDispatchFor(Operation()));
    }

    [TestMethod]
    public void OtherUsersAndFixtureNamespacesCannotSatisfyProductionDispatch()
    {
        OperationOwnership wrongUser = new("S-1-5-21-11-22-33-1002", OperationGateContract.NamePrefix + User, false);
        OperationOwnership fixture = new(User, OperationGateContract.NamePrefix + User + ".test." + Guid.NewGuid().ToString("N"), false);
        OperationOwnership legacy = new(User, "Local\\PCSteward.DataOperation.v1", false);
        Assert.IsFalse(wrongUser.CanDispatchFor(Operation()));
        Assert.IsFalse(fixture.CanDispatchFor(Operation()));
        Assert.IsFalse(legacy.CanDispatchFor(Operation()));
    }

    [TestMethod]
    public void NativeFixturePublishesLifetimeButNeverAProductionDispatchContext()
    {
        using CurrentUserOperationGate gate = CurrentUserOperationGate.OpenForTesting(Guid.NewGuid());
        OperationGateOwnership native = gate.TryAcquire(TimeSpan.Zero, CancellationToken.None)!;
        Assert.IsNotNull(native);
        OperationOwnership context = native.Context;
        Assert.IsTrue(context.IsActive);
        Assert.IsFalse(context.CanDispatchFor(Operation()));
        native.Dispose();
        Assert.IsFalse(context.IsActive);
    }

    [TestMethod]
    public void AsyncConsumerChecksLifetimeWithoutAcquiringThreadAffinity()
    {
        OperationOwnership ownership = new(User, OperationGateContract.NamePrefix + User, false);
        bool admitted = false;
        Thread consumer = new(() => admitted = ownership.CanDispatchFor(Operation()));
        consumer.Start();
        Assert.IsTrue(consumer.Join(TimeSpan.FromSeconds(5)));
        Assert.IsTrue(admitted);
        ownership.Release();
    }

    [TestMethod]
    public void CallersHaveNoPublicConstructorForOwnershipProof()
    {
        Assert.IsEmpty(typeof(OperationOwnership).GetConstructors());
    }

    private static ReviewedUpdateOperation Operation()
    {
        DateTimeOffset reviewedAt = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection(
            "winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1",
            UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(reviewedAt);
        return new(job, User, "native:123", "context:123", "evidence:123", reviewedAt.AddSeconds(-1),
            UpdateRouteContract.WinGetUserExactV2, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }
}
