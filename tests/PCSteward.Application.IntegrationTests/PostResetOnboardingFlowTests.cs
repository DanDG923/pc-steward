using PCSteward.Storage;

namespace PCSteward.Application.IntegrationTests;

[TestClass]
public sealed class PostResetOnboardingFlowTests
{
    [TestMethod]
    public void FailedPostResetAcknowledgementWithNoneMarkerDoesNotLoadInventoryOrNavigate()
    {
        int savedInventoryLoadCount = 0;

        PostResetOnboardingOutcome result = PostResetOnboardingFlow.Evaluate(
            () => new FactoryResetResult(false, [new FactoryResetResidual("UnitTest", "MarkerStillPresent", "Simulated marker check failure")]),
            () => FactoryResetMarkerState.None,
            () =>
            {
                savedInventoryLoadCount++;
                return true;
            });

        Assert.IsFalse(result.AcknowledgementSucceeded);
        Assert.IsFalse(result.ScanNavigationRequested);
        Assert.AreEqual(0, savedInventoryLoadCount, "Failed acknowledgement should keep saved-inventory load count at zero.");
        Assert.AreEqual(0, result.SavedInventoryLoadAttempts);
    }

    [TestMethod]
    public void ResidualMarkerAfterSuccessKeepsOnboardingFlowBlocked()
    {
        int savedInventoryLoadCount = 0;
        PostResetOnboardingOutcome result = PostResetOnboardingFlow.Evaluate(
            () => new FactoryResetResult(true, []),
            () => FactoryResetMarkerState.OnboardingPending,
            () =>
            {
                savedInventoryLoadCount++;
                return true;
            });

        Assert.IsTrue(result.AcknowledgementSucceeded);
        Assert.IsFalse(result.ScanNavigationRequested);
        Assert.AreEqual(0, savedInventoryLoadCount);
        Assert.AreEqual(0, result.SavedInventoryLoadAttempts);
    }
}
