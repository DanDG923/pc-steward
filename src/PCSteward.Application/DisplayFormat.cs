using System.Globalization;

namespace PCSteward.Application;

/// <summary>
/// AC-UX-03, FR-UI-05 (Task 73 Phase 3; audit P-M6): one portable formatter for dates, times, relative ages and byte sizes, so every
/// page uses the same date format and human-readable sizes instead of raw bytes, "characters", or mixed patterns. Presentation only.
/// The culture and time zone are parameters so tests are deterministic; the Desktop passes nothing and gets the user's settings.
/// </summary>
public static class DisplayFormat
{
    /// <summary>The one date pattern used app-wide (for example "3 Nov 2026").</summary>
    public const string DatePattern = "d MMM yyyy";

    /// <summary>The one time pattern used app-wide (24-hour, for example "14:05").</summary>
    public const string TimePattern = "HH:mm";

    /// <summary>The one date-and-time pattern used app-wide (for example "3 Nov 2026 · 14:05").</summary>
    public const string DateTimePattern = DatePattern + " · " + TimePattern;

    public static string Date(DateTimeOffset value, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        Local(value, zone).ToString(DatePattern, culture ?? CultureInfo.CurrentCulture);

    public static string Time(DateTimeOffset value, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        Local(value, zone).ToString(TimePattern, culture ?? CultureInfo.CurrentCulture);

    public static string DateTime(DateTimeOffset value, CultureInfo? culture = null, TimeZoneInfo? zone = null) =>
        Local(value, zone).ToString(DateTimePattern, culture ?? CultureInfo.CurrentCulture);

    /// <summary>"Today", "Yesterday" or the date, for a day-group heading.</summary>
    public static string DayHeading(DateOnly day, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        DateOnly today = LocalDay(now, zone);
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        return day.ToString(DatePattern, culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>The local calendar day of <paramref name="value"/>.</summary>
    public static DateOnly LocalDay(DateTimeOffset value, TimeZoneInfo? zone = null) => DateOnly.FromDateTime(Local(value, zone).DateTime);

    /// <summary>
    /// How long ago, in words: "just now", "10 minutes ago", "3 hours ago", "yesterday", "4 days ago", then the date after a week.
    /// A time in the future (clock skew) reads as "just now" rather than a negative age.
    /// </summary>
    public static string Relative(DateTimeOffset value, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        TimeSpan age = now - value;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return Plural((int)age.TotalMinutes, "minute") + " ago";
        if (LocalDay(value, zone) == LocalDay(now, zone)) return Plural((int)age.TotalHours, "hour") + " ago";
        int days = LocalDay(now, zone).DayNumber - LocalDay(value, zone).DayNumber;
        if (days == 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        return "on " + Date(value, culture, zone);
    }

    /// <summary>
    /// A moment as a day and a time in words: "today at 14:50", "yesterday at 09:12", then "on 3 Nov 2026 at 14:50"
    /// (UI review 2026-11-13, item 8).
    /// </summary>
    public static string DayAndTime(DateTimeOffset value, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        int days = LocalDay(now, zone).DayNumber - LocalDay(value, zone).DayNumber;
        string day = days == 0 ? "today" : days == 1 ? "yesterday" : "on " + Date(value, culture, zone);
        return $"{day} at {Time(value, culture, zone)}";
    }

    /// <summary>A start–end time range on one line ("14:02–14:05", or with dates when the days differ).</summary>
    public static string Range(DateTimeOffset start, DateTimeOffset? end, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        if (end is not { } finished) return $"Started {DateTime(start, culture, zone)}";
        return LocalDay(start, zone) == LocalDay(finished, zone)
            ? $"{DateTime(start, culture, zone)}–{Time(finished, culture, zone)}"
            : $"{DateTime(start, culture, zone)} – {DateTime(finished, culture, zone)}";
    }

    /// <summary>
    /// The time of day for a row that sits under a day heading: "14:50" when it started and ended in the same minute (or has no
    /// end), "14:02–14:05" within one day, and the full <see cref="Range"/> when it ran past midnight (UI review 2026-11-13, fault 4).
    /// </summary>
    public static string TimeOfDay(DateTimeOffset start, DateTimeOffset? end, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        string started = Time(start, culture, zone);
        if (end is not { } finished) return started;
        if (LocalDay(start, zone) != LocalDay(finished, zone)) return Range(start, finished, culture, zone);
        string ended = Time(finished, culture, zone);
        return string.Equals(started, ended, StringComparison.Ordinal) ? ended : $"{started}–{ended}";
    }

    /// <summary>A human-readable size in bytes, KB, MB, GB or TB (1 KB = 1,024 bytes). Negative sizes read as 0 bytes.</summary>
    public static string Size(long bytes, CultureInfo? culture = null)
    {
        CultureInfo format = culture ?? CultureInfo.CurrentCulture;
        if (bytes < 0) bytes = 0;
        if (bytes < 1024) return bytes == 1 ? "1 byte" : $"{bytes.ToString(format)} bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = -1;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value.ToString("0.#", format)} {units[unit]}";
    }

    /// <summary>"1 app", "3 apps" and similar.</summary>
    public static string Plural(int count, string singular, string? plural = null) =>
        count == 1 ? $"1 {singular}" : $"{count.ToString("N0", CultureInfo.CurrentCulture)} {plural ?? singular + "s"}";

    private static DateTimeOffset Local(DateTimeOffset value, TimeZoneInfo? zone) => TimeZoneInfo.ConvertTime(value, zone ?? TimeZoneInfo.Local);
}
