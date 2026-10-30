using PCSteward.Application;
using PCSteward.Domain;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class ReviewedUpdateDraftTests
{
    [TestMethod]
    public void EqualOperationDataDoesNotMakeDifferentDraftGenerationsEquivalent()
    {
        DateTimeOffset now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("winget:widget",
            UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0", "1.1", UpdateScope.User,
            UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1")).Confirm(now);
        ReviewedUpdateOperation operation = new(job, "S-1-5-21-11-22-33-1001", "native:123", "context:123", "evidence:123", now,
            UpdateRouteContract.WinGetUserExactV2, ReviewedInstallerMode.Selected(InstallerMode.Silent));
        Guid generation = Guid.NewGuid();
        ReviewedUpdateDraft first = new(operation, generation);
        Assert.IsTrue(first.HasSameDraftIdentity(new(operation, generation)));
        Assert.IsFalse(first.HasSameDraftIdentity(new(operation, Guid.NewGuid())));
        Assert.IsFalse(first.HasSameDraftIdentity(null));
        Assert.ThrowsExactly<ArgumentException>(() => new ReviewedUpdateDraft(operation, Guid.Empty));
    }
}
