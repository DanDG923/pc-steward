namespace PCSteward.Application;

/// <summary>
/// Task 73 Phase 4 independent review (fix 5; FR-DAT-01): the "Save schedule" register-then-save sequence with a distinct outcome
/// for each failure, so a Windows Task Scheduler refusal is never reported as a busy data-operation gate. If the task was changed but
/// the settings could not be saved, the previous schedule is registered again through the same registration operation; if that also
/// fails, the outcome says so honestly. The caller runs this inside the data-operation lease; it starts no scan or update.
/// </summary>
public static class ScheduleSaveOperation
{
    public static async Task<ScheduleSaveOutcome> SaveAsync(
        ScheduledCheckSettings previous,
        ScheduledCheckSettings requested,
        Func<ScheduledCheckSettings, CancellationToken, Task> applyRegistration,
        Func<ScheduledCheckSettings, CancellationToken, Task> saveSettings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(applyRegistration);
        ArgumentNullException.ThrowIfNull(saveSettings);
        try { await applyRegistration(requested, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (IsHandled(exception)) { return ScheduleSaveOutcome.SchedulerRefused; }

        try
        {
            await saveSettings(requested, cancellationToken).ConfigureAwait(false);
            return ScheduleSaveOutcome.Saved;
        }
        catch (Exception exception) when (IsHandled(exception)) { }

        try
        {
            await applyRegistration(previous, cancellationToken).ConfigureAwait(false);
            return ScheduleSaveOutcome.SettingsNotSavedTaskRestored;
        }
        catch (Exception exception) when (IsHandled(exception)) { return ScheduleSaveOutcome.SettingsNotSavedTaskChanged; }
    }

    private static bool IsHandled(Exception exception) => exception is not (OperationCanceledException or OutOfMemoryException);
}
