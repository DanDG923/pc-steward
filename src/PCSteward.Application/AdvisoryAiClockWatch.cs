using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// ADR-029 Q7 (Task 70 T70-04): watches the wall clock against a monotonic clock while PC Steward runs. A wall clock that advanced more
/// than <see cref="AdvisoryAiClock.SuspiciousForwardJump"/> further than the monotonic one since the last observation is suspicious;
/// the request workflow then stops paid requests for the owner's review before reserving anything. The monotonic clock is the system
/// tick count, which keeps counting through sleep, so an ordinary sleep is not a jump; a false alarm only pauses, it never admits.
/// Only one process lifetime is watched: nothing here survives a restart, which is why the review block it sets is stored.
/// </summary>
public sealed class AdvisoryAiClockWatch
{
    private readonly object gate = new();
    private readonly Func<DateTimeOffset> utcNow;
    private readonly Func<TimeSpan> monotonic;
    private DateTimeOffset lastWall;
    private TimeSpan lastMonotonic;
    private bool pending;

    /// <summary>Starts watching now: the first observation is compared with this moment.</summary>
    public AdvisoryAiClockWatch(Func<DateTimeOffset> utcNow, Func<TimeSpan> monotonic)
    {
        this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        this.monotonic = monotonic ?? throw new ArgumentNullException(nameof(monotonic));
        lastWall = utcNow();
        lastMonotonic = monotonic();
    }

    /// <summary>The system clocks: UTC wall time and the system tick count.</summary>
    public static AdvisoryAiClockWatch ForSystem() =>
        new(() => DateTimeOffset.UtcNow, () => TimeSpan.FromMilliseconds(Environment.TickCount64));

    /// <summary>
    /// True when the wall clock jumped forward since the last observation, or an earlier jump hasn't been recorded yet. Each call moves
    /// the baseline to now, but a detected jump stays pending until <see cref="JumpRecorded"/>, so a failed write can't forget it.
    /// </summary>
    public bool JumpedForward()
    {
        lock (gate)
        {
            DateTimeOffset wall = utcNow();
            TimeSpan tick = monotonic();
            pending |= AdvisoryAiClock.IsSuspiciousForwardJump(wall - lastWall, tick - lastMonotonic);
            lastWall = wall;
            lastMonotonic = tick;
            return pending;
        }
    }

    /// <summary>The review block for the pending jump is stored: later observations start fresh.</summary>
    public void JumpRecorded()
    {
        lock (gate) pending = false;
    }
}
