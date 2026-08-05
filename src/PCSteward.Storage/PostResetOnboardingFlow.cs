namespace PCSteward.Storage;

public readonly record struct PostResetOnboardingOutcome(
    bool ScanNavigationRequested,
    int SavedInventoryLoadAttempts,
    bool AcknowledgementSucceeded,
    FactoryResetMarkerState MarkerState,
    FactoryResetResult AcknowledgementResult);

public static class PostResetOnboardingFlow
{
    public static PostResetOnboardingOutcome Evaluate(
        Func<FactoryResetResult> acknowledgeResetOnboarding,
        Func<FactoryResetMarkerState> markerState,
        Func<bool> loadSavedInventory)
    {
        ArgumentNullException.ThrowIfNull(acknowledgeResetOnboarding);
        ArgumentNullException.ThrowIfNull(markerState);
        ArgumentNullException.ThrowIfNull(loadSavedInventory);

        FactoryResetResult acknowledgementResult = acknowledgeResetOnboarding();
        FactoryResetMarkerState markerStateValue = markerState();
        if (!acknowledgementResult.Succeeded)
        {
            return new(false, 0, false, markerStateValue, acknowledgementResult);
        }

        if (markerStateValue != FactoryResetMarkerState.None)
        {
            return new(false, 0, true, markerStateValue, acknowledgementResult);
        }

        bool loaded = loadSavedInventory();
        return new(loaded, 1, true, markerStateValue, acknowledgementResult);
    }
}
