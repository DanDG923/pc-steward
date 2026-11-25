namespace PCSteward.Application;

/// <summary>Persisted user choice for scan-only scheduled checks. It contains no update or command authority.</summary>
public enum ScheduleCadence
{
    Manual,
    Daily,
    Weekly
}

public sealed record ScheduledCheckSettings(
    ScheduleCadence Cadence,
    bool IsEnabled,
    bool NotificationsEnabled,
    DayOfWeek WeeklyDay,
    TimeOnly LocalStartTime)
{
    public static ScheduledCheckSettings Manual { get; } = new(ScheduleCadence.Manual, false, false, DayOfWeek.Monday, new TimeOnly(9, 0));

    public ScheduledCheckSettings Normalize()
    {
        if (!Enum.IsDefined(Cadence)) throw new ArgumentOutOfRangeException(nameof(Cadence));
        return Cadence == ScheduleCadence.Manual ? this with { IsEnabled = false } : this;
    }
}

/// <summary>Non-authoritative local status of the bounded scheduled-check host.</summary>
public sealed record ScheduledCheckStatus(DateTimeOffset? LastCompletedAt, string LastResult, int ConsecutiveFailures)
{
    public static ScheduledCheckStatus NotScheduled { get; } = new(null, "Not scheduled", 0);
}

/// <summary>Notification policy deliberately contains no notification transport or update authority.</summary>
public static class ScheduledCheckNotificationPolicy
{
    public static bool ShouldNotify(ScheduledCheckSettings settings, bool attentionRequired, bool scanSucceeded, int consecutiveFailures) =>
        settings.NotificationsEnabled && (attentionRequired || (!scanSucceeded && consecutiveFailures >= 2));
}

/// <summary>
/// FR-SCH-04: records a scheduled check that started and then timed out or failed, so the failure count rises and the
/// repeated-failure notice can fire. The check's own token is already cancelled by then, so this write has its own short limit.
/// </summary>
public static class ScheduledCheckFailureRecorder
{
    public const string TimedOutResult = "Timed out";
    public const string FailedResult = "Failed";
    public static readonly TimeSpan WriteLimit = TimeSpan.FromSeconds(10);

    /// <summary>Returns the saved status, or null when it could not be read and saved within <see cref="WriteLimit"/>.</summary>
    public static async Task<ScheduledCheckStatus?> RecordAsync(IScheduledCheckSettingsStore store, string result, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        try
        {
            using CancellationTokenSource limit = new(WriteLimit);
            ScheduledCheckStatus previous = await store.GetScheduledCheckStatusAsync(limit.Token).ConfigureAwait(false);
            int failures = previous.ConsecutiveFailures >= int.MaxValue - 1 ? int.MaxValue : Math.Max(0, previous.ConsecutiveFailures) + 1;
            ScheduledCheckStatus status = new((clock ?? TimeProvider.System).GetUtcNow(), result, failures);
            await store.SaveScheduledCheckStatusAsync(status, limit.Token).ConfigureAwait(false);
            return status;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }
}

public interface IScheduledCheckSettingsStore
{
    Task<ScheduledCheckSettings> GetScheduledCheckSettingsAsync(CancellationToken cancellationToken);
    Task SaveScheduledCheckSettingsAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken);
    Task<ScheduledCheckStatus> GetScheduledCheckStatusAsync(CancellationToken cancellationToken);
    Task SaveScheduledCheckStatusAsync(ScheduledCheckStatus status, CancellationToken cancellationToken);
}
