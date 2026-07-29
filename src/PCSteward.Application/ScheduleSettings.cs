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

public interface IScheduledCheckSettingsStore
{
    Task<ScheduledCheckSettings> GetScheduledCheckSettingsAsync(CancellationToken cancellationToken);
    Task SaveScheduledCheckSettingsAsync(ScheduledCheckSettings settings, CancellationToken cancellationToken);
    Task<ScheduledCheckStatus> GetScheduledCheckStatusAsync(CancellationToken cancellationToken);
    Task SaveScheduledCheckStatusAsync(ScheduledCheckStatus status, CancellationToken cancellationToken);
}
