using System.Globalization;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// FR-HIS-01 (Task 73 Phase 3): a bounded, read-only view of the locally recorded update-job history and scan runs for the
/// Activity page. Rows are untrusted data shown for information only; nothing read here is ever execution authority.
/// </summary>
public interface IActivityHistoryReader
{
    /// <summary>The most recent update-history events, newest first. <paramref name="maximumEntries"/> is 1–500.</summary>
    Task<IReadOnlyList<UpdateHistoryEntry>> GetRecentUpdateHistoryAsync(int maximumEntries, CancellationToken cancellationToken);

    /// <summary>The most recent scan runs, newest first. <paramref name="maximumRuns"/> is 1–500.</summary>
    Task<IReadOnlyList<ScanRun>> GetRecentScanRunsAsync(int maximumRuns, CancellationToken cancellationToken);
}

/// <summary>One update job read back from its recorded events: when it started and ended, and its final recorded outcome.</summary>
public sealed record UpdateJobRecord(
    Guid JobId,
    string StableProductKey,
    UpdateProvider Provider,
    string PackageId,
    string InstalledVersion,
    string TargetVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    UpdateHistoryEntry Final,
    UpdateOutcomePresentation Outcome);

/// <summary>
/// FR-HIS-01/03 (Task 73 Phase 3): groups recorded update events into one record per job. The last recorded event decides the
/// displayed outcome through the Phase 2 <see cref="UpdateOutcomePresenter"/>, so an exit code of zero alone never reads as Verified.
/// Presentation only; recorded history is never changed.
/// </summary>
public static class UpdateJobHistory
{
    public static IReadOnlyList<UpdateJobRecord> Group(IEnumerable<UpdateHistoryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .Where(entry => entry.JobId != Guid.Empty)
            .GroupBy(entry => entry.JobId)
            .Select(job =>
            {
                UpdateHistoryEntry final = job.OrderBy(entry => entry.OccurredAt).ThenBy(entry => Rank(entry.Status)).Last();
                DateTimeOffset started = job.Min(entry => entry.OccurredAt);
                return new UpdateJobRecord(job.Key, final.StableProductKey, final.Provider, final.PackageId, final.InstalledVersion, final.TargetVersion,
                    started, final.OccurredAt, final, UpdateOutcomePresenter.ForStatus(final.Status, final.Diagnostic, final.ExitCode, final.TargetVersion));
            })
            .OrderByDescending(job => job.EndedAt)
            .ThenBy(job => job.JobId)
            .ToArray();
    }

    /// <summary>The newest job per app. Whether its attempt still needs attention is decided by <see cref="Resolved"/>.</summary>
    public static IReadOnlyDictionary<string, UpdateJobRecord> LatestPerProduct(IEnumerable<UpdateJobRecord> jobs) => jobs
        .GroupBy(job => job.StableProductKey, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.OrderByDescending(job => job.EndedAt).First(), StringComparer.Ordinal);

    /// <summary>True when the recorded attempt still needs the user's attention (failed or not verified, and not since resolved).</summary>
    public static bool NeedsAttention(UpdateJobRecord job, string? installedVersion, DateTimeOffset? lastCompletedScanAt = null) =>
        job.Outcome.Kind is UpdateOutcomeKind.Failed or UpdateOutcomeKind.InstalledNotVerified or UpdateOutcomeKind.ResultNotVerified
        && !Resolved(job, installedVersion, lastCompletedScanAt);

    /// <summary>True when the recorded attempt reported a reliable restart-pending state that a later scan has not yet resolved.</summary>
    public static bool RestartPending(UpdateJobRecord job, string? installedVersion, DateTimeOffset? lastCompletedScanAt = null) =>
        job.Outcome.Kind == UpdateOutcomeKind.RestartRequired && !Resolved(job, installedVersion, lastCompletedScanAt);

    /// <summary>
    /// FR-UI-06, FR-HIS-01 (Task 73 Phase 3; independent review M2): an attempt no longer needs attention when the latest completed
    /// inventory shows that something has moved on — the installed version equals the target (by the domain version comparer, so
    /// "2.0" equals "2.0.0.0") or is newer than it, the installed version changed since the attempt, or the app is absent from a
    /// completed inventory taken after the attempt. Resolving only drops the attempt from Home's attention list: it never relabels
    /// the attempt as Verified, and Activity keeps its recorded outcome.
    /// </summary>
    public static bool Resolved(UpdateJobRecord job, string? installedVersion, DateTimeOffset? lastCompletedScanAt = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(installedVersion))
            return lastCompletedScanAt is { } scannedAt && scannedAt > job.EndedAt;
        VersionComparisonOutcome toTarget = VersionComparison.Compare(new VersionComparisonRequest(installedVersion, job.TargetVersion, AllowPrereleaseCandidate: true)).Outcome;
        if (toTarget is VersionComparisonOutcome.Equal or VersionComparisonOutcome.Older) return true;
        if (toTarget == VersionComparisonOutcome.Incomparable && SameText(installedVersion, job.TargetVersion)) return true;
        if (string.IsNullOrWhiteSpace(job.InstalledVersion)) return false;
        return VersionComparison.Compare(new VersionComparisonRequest(installedVersion, job.InstalledVersion, AllowPrereleaseCandidate: true)).Outcome switch
        {
            VersionComparisonOutcome.Newer or VersionComparisonOutcome.Older => true,
            VersionComparisonOutcome.Equal => false,
            _ => !SameText(installedVersion, job.InstalledVersion)
        };
    }

    /// <summary>
    /// As <see cref="FailureClass(UpdateHistoryStatus)"/>, but an admission refusal (recorded as ProductFailed with
    /// <see cref="UpdateOutcomePresenter.AdmissionRefusedDiagnostic"/>) reads as not started (Task 73 Phase 6 review L5).
    /// </summary>
    public static string FailureClass(UpdateHistoryStatus status, string? diagnostic) =>
        status == UpdateHistoryStatus.ProductFailed && string.Equals(diagnostic, UpdateOutcomePresenter.AdmissionRefusedDiagnostic, StringComparison.Ordinal)
            ? "Not started: another update or its work wasn't finished"
            : FailureClass(status);

    /// <summary>The failure class in words (FR-HIS-03: source, download, installer, elevation, timeout and verification stay distinct).</summary>
    public static string FailureClass(UpdateHistoryStatus status) => status switch
    {
        UpdateHistoryStatus.Success => "None — verified",
        UpdateHistoryStatus.RestartRequired => "None — restart pending",
        UpdateHistoryStatus.SourceFailed => "Source failure",
        UpdateHistoryStatus.DownloadFailed => "Download failure",
        UpdateHistoryStatus.InstallerFailed => "Installer failure",
        UpdateHistoryStatus.ElevationDenied => "Administrator permission declined",
        UpdateHistoryStatus.TimedOut => "Timeout",
        UpdateHistoryStatus.VerificationFailed => "Verification failure",
        UpdateHistoryStatus.Inconclusive => "Verification inconclusive",
        UpdateHistoryStatus.VerificationRequired => "Verification not completed",
        UpdateHistoryStatus.Cancelled => "Cancelled before the installer ran",
        UpdateHistoryStatus.RunningApplicationWarning => "Stopped: the app was running",
        UpdateHistoryStatus.ProductFailed => "PC Steward could not run the update",
        UpdateHistoryStatus.Queued => "Not started",
        UpdateHistoryStatus.Running => "Interrupted while running",
        _ => "Unknown"
    };

    private static bool SameText(string left, string? right) => string.Equals(left.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    // Ties on time: a terminal status outranks verification-required, which outranks queued/running.
    private static int Rank(UpdateHistoryStatus status) => status switch
    {
        UpdateHistoryStatus.Queued or UpdateHistoryStatus.Running => 0,
        UpdateHistoryStatus.VerificationRequired or UpdateHistoryStatus.RunningApplicationWarning => 1,
        _ => 2
    };
}

/// <summary>The Activity page filters (Task 73 Phase 3).</summary>
public enum ActivityFilter
{
    All,
    Updates,
    Scans,
    Problems
}

/// <summary>What an Activity entry records.</summary>
public enum ActivityKind
{
    Update,
    Scan,
    Decision
}

/// <summary>One filter button with its count.</summary>
public sealed record ActivityFilterOption(ActivityFilter Filter, string Label, int Count)
{
    public string DisplayLabel => $"{Label} ({Count.ToString("N0", CultureInfo.CurrentCulture)})";
    public string AccessibleName => $"Show {DisplayLabel} activity";
}

/// <summary>One Activity entry. <see cref="Facts"/> are always shown; <see cref="Details"/> are behind an expander.</summary>
public sealed record ActivityEntry(
    string Id,
    ActivityKind Kind,
    DateTimeOffset OccurredAt,
    string Title,
    string OutcomeLabel,
    string Glyph,
    StatusTone Tone,
    string Summary,
    string TimeText,
    bool IsProblem,
    IReadOnlyList<DetailFact> Facts,
    IReadOnlyList<DetailFact> Details,
    UpdateOutcomeKind? Outcome = null)
{
    /// <summary>The entry's accessible name: title, outcome and time.</summary>
    public string AccessibleName => $"{Title}: {OutcomeLabel}. {TimeText}";
}

/// <summary>One day of Activity, newest first.</summary>
public sealed record ActivityDay(DateOnly Day, string Heading, IReadOnlyList<ActivityEntry> Entries);

/// <summary>The Activity page.</summary>
public sealed record ActivityPagePresentation(ActivityFilter Filter, IReadOnlyList<ActivityFilterOption> Filters, IReadOnlyList<ActivityDay> Days, int MatchingCount, int ShownCount)
{
    public string Summary => MatchingCount == 0
        ? "No activity matches this filter."
        : ShownCount < MatchingCount
            ? $"Showing the newest {ShownCount.ToString("N0", CultureInfo.CurrentCulture)} of {MatchingCount.ToString("N0", CultureInfo.CurrentCulture)} records."
            : $"Showing {DisplayFormat.Plural(MatchingCount, "record")}.";
}

/// <summary>
/// FR-HIS-01–03 (Task 73 Phase 3, ADR-030; concept E's day grouping): builds the filterable Activity history from recorded update
/// jobs, scan runs and saved decisions. Update outcomes use the Phase 2 display states; details carry the exit code, the failure
/// class and the sanitized recorded output. Presentation only.
/// </summary>
public static class ActivityPresenter
{
    /// <summary>The most entries shown at once; older ones are counted but not rendered.</summary>
    public const int MaximumShown = 100;

    public static IReadOnlyList<ActivityEntry> Entries(
        IEnumerable<UpdateJobRecord> jobs,
        IEnumerable<ScanRun> scans,
        IEnumerable<StableDecision> decisions,
        IEnumerable<ManualMapping> mappings,
        IReadOnlyCollection<InventoryRow> rows,
        CultureInfo? culture = null,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(jobs); ArgumentNullException.ThrowIfNull(scans); ArgumentNullException.ThrowIfNull(decisions);
        ArgumentNullException.ThrowIfNull(mappings); ArgumentNullException.ThrowIfNull(rows);
        List<ActivityEntry> entries = [];
        foreach (UpdateJobRecord job in jobs)
        {
            string name = ProductName(rows, job.StableProductKey, job.PackageId);
            UpdateOutcomePresentation outcome = job.Outcome;
            entries.Add(new ActivityEntry(
                $"update:{job.JobId:N}", ActivityKind.Update, job.EndedAt,
                $"{name} update", outcome.Label, outcome.Glyph, outcome.Tone, outcome.Summary,
                DisplayFormat.Range(job.StartedAt, job.EndedAt, culture, zone),
                outcome.Kind is UpdateOutcomeKind.Failed or UpdateOutcomeKind.InstalledNotVerified or UpdateOutcomeKind.ResultNotVerified,
                [
                    new("Started", DisplayFormat.DateTime(job.StartedAt, culture, zone)),
                    new("Finished", DisplayFormat.DateTime(job.EndedAt, culture, zone)),
                    new("Provider", job.Provider.ToString()),
                    new("Package ID", job.PackageId),
                    new("Version", $"{job.InstalledVersion} → {job.TargetVersion}"),
                ],
                [
                    new("Exit code", job.Final.ExitCode is { } code ? code.ToString(CultureInfo.InvariantCulture) : "None recorded"),
                    new("Failure class", UpdateJobHistory.FailureClass(job.Final.Status, job.Final.Diagnostic)),
                    new("Recorded output", string.IsNullOrWhiteSpace(job.Final.Diagnostic) ? "None recorded" : SanitizedDiagnosticBundle.Sanitize(job.Final.Diagnostic)),
                ],
                outcome.Kind));
        }
        foreach (ScanRun scan in scans)
        {
            DateTimeOffset at = scan.CompletedAt ?? scan.StartedAt;
            (string label, string glyph, StatusTone tone, string summary, bool problem) = scan.Status switch
            {
                "Completed" => ("Completed", "\uE73E", StatusTone.Success, "The read-only scan finished and replaced your last completed scan.", false),
                "Cancelled" => ("Cancelled", "\uE711", StatusTone.Neutral, "You cancelled the scan. Your last completed scan was kept.", false),
                _ => ("Did not complete", "\uE7BA", StatusTone.Attention, "The scan didn't finish. Your last completed scan was kept.", true)
            };
            List<DetailFact> facts = [new("Started", DisplayFormat.DateTime(scan.StartedAt, culture, zone))];
            if (scan.CompletedAt is { } completed)
            {
                facts.Add(new("Finished", DisplayFormat.DateTime(completed, culture, zone)));
                facts.Add(new("Duration", Duration(completed - scan.StartedAt)));
            }
            entries.Add(new ActivityEntry($"scan:{scan.Id:N}", ActivityKind.Scan, at, "Inventory scan", label, glyph, tone, summary,
                DisplayFormat.Range(scan.StartedAt, scan.CompletedAt, culture, zone), problem, facts, []));
        }
        foreach (DecisionRow decision in DecisionsPresenter.Build(decisions, mappings, rows, culture, zone))
        {
            entries.Add(new ActivityEntry($"decision:{decision.Id:N}", ActivityKind.Decision, decision.CreatedAt, $"{decision.ProductName}: {decision.Title}",
                "Decision saved", decision.Glyph, StatusTone.Neutral, decision.Detail, DisplayFormat.DateTime(decision.CreatedAt, culture, zone), false,
                decision.Reason is null ? [] : [new("Reason", decision.Reason)], []));
        }
        return entries.OrderByDescending(entry => entry.OccurredAt).ThenBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
    }

    public static bool Matches(ActivityFilter filter, ActivityEntry entry) => filter switch
    {
        ActivityFilter.Updates => entry.Kind == ActivityKind.Update,
        ActivityFilter.Scans => entry.Kind == ActivityKind.Scan,
        ActivityFilter.Problems => entry.IsProblem,
        _ => true
    };

    public static ActivityPagePresentation Build(IReadOnlyList<ActivityEntry> entries, ActivityFilter filter, DateTimeOffset now, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(filter)) filter = ActivityFilter.All;
        ActivityFilterOption[] filters = Enum.GetValues<ActivityFilter>()
            .Select(option => new ActivityFilterOption(option, Label(option), entries.Count(entry => Matches(option, entry))))
            .ToArray();
        ActivityEntry[] matching = entries.Where(entry => Matches(filter, entry)).OrderByDescending(entry => entry.OccurredAt).ToArray();
        ActivityDay[] days = matching.Take(MaximumShown)
            .GroupBy(entry => DisplayFormat.LocalDay(entry.OccurredAt, zone))
            .OrderByDescending(group => group.Key)
            .Select(group => new ActivityDay(group.Key, DisplayFormat.DayHeading(group.Key, now, culture, zone), group.ToArray()))
            .ToArray();
        return new(filter, filters, days, matching.Length, Math.Min(matching.Length, MaximumShown));
    }

    public static string Label(ActivityFilter filter) => filter switch
    {
        ActivityFilter.Updates => "Updates",
        ActivityFilter.Scans => "Scans",
        ActivityFilter.Problems => "Problems",
        _ => "All"
    };

    private static string Duration(TimeSpan duration) => duration < TimeSpan.Zero ? "Unknown"
        : duration < TimeSpan.FromMinutes(1) ? DisplayFormat.Plural(Math.Max(1, (int)Math.Round(duration.TotalSeconds)), "second")
        : DisplayFormat.Plural((int)duration.TotalMinutes, "minute") + (duration.Seconds == 0 ? string.Empty : $" {DisplayFormat.Plural(duration.Seconds, "second")}");

    internal static string ProductName(IReadOnlyCollection<InventoryRow> rows, string stableProductKey, string fallback)
    {
        string? name = rows.FirstOrDefault(row => string.Equals(row.StableProductKey, stableProductKey, StringComparison.Ordinal))?.Name;
        return !string.IsNullOrWhiteSpace(name) ? name : !string.IsNullOrWhiteSpace(fallback) ? fallback : stableProductKey;
    }
}
