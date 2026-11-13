using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// How far along a scan or an update check is, for a progress bar (owner request 2026-11-13; FR-UI-01/05). When nothing is known
/// to measure against, <see cref="IsIndeterminate"/> is true and the bar only shows that work is going on.
/// </summary>
public sealed record WorkProgressPresentation(string Title, bool IsIndeterminate, int Percent, string Detail)
{
    /// <summary>The line under the bar, and the bar's accessible value.</summary>
    public string Line => Detail.Length == 0 ? Title : $"{Title} · {Detail}";
}

/// <summary>
/// Builds <see cref="WorkProgressPresentation"/>. The time left is an estimate from how long the same work took last time, and is
/// worded as one ("about"). It never reaches 100% on an estimate: the bar is full only when the work has finished. With no
/// earlier scan to compare with, a scan shows the step it is on. Presentation only: it measures nothing and starts nothing.
/// </summary>
public static class WorkProgressPresenter
{
    /// <summary>The most an estimate may show; the rest is kept for the work actually finishing.</summary>
    public const int MaximumEstimatedPercent = 95;

    public const string ScanTitle = "Scanning this PC";
    public const string UpdateCheckTitle = "Looking for updates";
    public const string LongerThanLastTime = "taking a little longer than last time";

    /// <summary>An earlier duration shorter than this is too small to estimate from.</summary>
    private static readonly TimeSpan ShortestUsefulDuration = TimeSpan.FromSeconds(1);

    /// <summary>How long the most recent completed scan took; <see langword="null"/> when there is none to compare with.</summary>
    public static TimeSpan? LastScanDuration(IEnumerable<ScanRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ScanRun? last = runs
            .Where(run => string.Equals(run.Status, "Completed", StringComparison.Ordinal) && run.CompletedAt is { } completed && completed - run.StartedAt >= ShortestUsefulDuration)
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefault();
        return last is { CompletedAt: { } at } ? at - last.StartedAt : null;
    }

    /// <summary>
    /// A scan in progress. With an earlier scan to compare with: the share of that time already spent, and about how long is
    /// left. Without one: the step the scan is on (<paramref name="step"/> of <paramref name="steps"/>, counted from 1).
    /// </summary>
    public static WorkProgressPresentation Scan(int step, int steps, TimeSpan elapsed, TimeSpan? lastDuration)
    {
        if (Estimated(ScanTitle, elapsed, lastDuration) is { } estimate) return estimate;
        if (steps <= 0 || step <= 0) return new(ScanTitle, true, 0, string.Empty);
        int current = Math.Min(step, steps);
        int percent = Math.Min(MaximumEstimatedPercent, (current - 1) * 100 / steps);
        return new(ScanTitle, false, percent, $"{percent}% · step {current} of {steps}");
    }

    /// <summary>An update check in progress: estimated from the last check's time when there is one, otherwise unmeasured.</summary>
    public static WorkProgressPresentation UpdateCheck(TimeSpan elapsed, TimeSpan? lastDuration) =>
        Estimated(UpdateCheckTitle, elapsed, lastDuration) ?? new(UpdateCheckTitle, true, 0, string.Empty);

    /// <summary>About how long is left, in words: "a few seconds left", "about 25 seconds left", "about 2 minutes left".</summary>
    public static string Remaining(TimeSpan remaining)
    {
        int seconds = (int)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        if (seconds < 10) return "a few seconds left";
        if (seconds < 60) return $"about {(seconds + 4) / 5 * 5} seconds left";
        int minutes = (int)Math.Round(seconds / 60.0, MidpointRounding.AwayFromZero);
        return minutes == 1 ? "about 1 minute left" : $"about {minutes} minutes left";
    }

    private static WorkProgressPresentation? Estimated(string title, TimeSpan elapsed, TimeSpan? lastDuration)
    {
        if (lastDuration is not { } expected || expected < ShortestUsefulDuration) return null;
        TimeSpan spent = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        int percent = (int)Math.Min(MaximumEstimatedPercent, Math.Floor(spent.TotalSeconds * 100 / expected.TotalSeconds));
        string detail = spent < expected ? Remaining(expected - spent) : LongerThanLastTime;
        return new(title, false, percent, $"{percent}% · {detail}");
    }
}
