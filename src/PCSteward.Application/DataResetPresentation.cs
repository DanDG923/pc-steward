using System.Globalization;

namespace PCSteward.Application;

/// <summary>A category of PC Steward-owned local data with its own card on Data &amp; reset.</summary>
public enum DataCategory
{
    ScanData,
    DownloadedFiles,
    SourceMetadata,
    LogsAndHistory,
    Decisions
}

/// <summary>An existing data operation. Each maps to exactly one existing operation and its existing confirmation dialog.</summary>
public enum DataAction
{
    ClearScanData,
    ClearDownloadedFiles,
    ClearSourceMetadata,
    ClearLogs,
    ClearUpdateHistory,
    ClearMappingsAndPolicies
}

/// <summary>One button on a Data &amp; reset card. <see cref="AccessibleName"/> starts with <see cref="Label"/> (label-in-name).</summary>
public sealed record DataActionPresentation(DataAction Action, string Label, string AccessibleName, bool IsEnabled, string? UnavailableReason);

/// <summary>One Data &amp; reset card: its facts (size, last cleaned), what it deletes and keeps, and its actions.</summary>
public sealed record DataCardPresentation(
    DataCategory Category,
    string Title,
    string Glyph,
    IReadOnlyList<DetailFact> Facts,
    string Deletes,
    string Keeps,
    IReadOnlyList<DataActionPresentation> Actions);

/// <summary>The separated danger-zone card for Factory reset PC Steward.</summary>
public sealed record FactoryResetCardPresentation(
    string Heading,
    string Title,
    string Label,
    string AccessibleName,
    string Deletes,
    string Keeps,
    string Afterwards,
    string ConfirmationPhrase,
    bool IsEnabled,
    string? UnavailableReason);

/// <summary>The Data &amp; reset page.</summary>
public sealed record DataResetPagePresentation(
    IReadOnlyList<DataCardPresentation> Cards,
    FactoryResetCardPresentation FactoryReset,
    string ProviderCacheBoundary,
    string Availability,
    bool HasActiveWork);

/// <summary>
/// What the Data &amp; reset page shows. The download figures are the existing download-cache summary; <paramref name="UpdateRecords"/>
/// is the number of recent update records read for Activity (at most <paramref name="UpdateRecordsLimit"/>).
/// </summary>
public sealed record DataResetInputs(
    ScanDataSummary ScanData,
    long DownloadTotalBytes,
    long DownloadRecoverableBytes,
    int DownloadItemCount,
    int DownloadActiveCount,
    DateTimeOffset? DownloadsLastCleanedAt,
    string SourceFreshness,
    int UpdateRecords,
    int UpdateRecordsLimit,
    bool UpdateHistoryUnavailable,
    int DecisionCount,
    bool HasActiveWork);

/// <summary>
/// FR-DAT-06–15, AC-UX-07/08, SPEC §12.5 and §17.3 (Task 73 Phase 4, ADR-030 foundation 6; audit P-H4, §6 item 9): Data &amp; reset as
/// separate cards — scan data, downloaded files, source metadata, logs and history, and decisions — each with its size and last-cleaned
/// time where PC Steward records them and explicit "Deletes / Keeps" text, then Factory reset in a separated danger zone. The exact
/// labels "Clear scan data", "Clear downloaded files" and "Factory reset PC Steward" are kept. Presentation only: this deletes nothing,
/// and every action still runs through its existing operation, confirmation, lease and active-work lockout.
/// </summary>
public static class DataResetPresenter
{
    public const string ClearScanDataLabel = "Clear scan data";
    public const string ClearDownloadedFilesLabel = "Clear downloaded files";
    public const string FactoryResetLabel = "Factory reset PC Steward";

    /// <summary>The exact, case-sensitive text the final factory-reset confirmation requires.</summary>
    public const string FactoryResetConfirmationPhrase = "RESET";

    public const string NotMeasured = "Not measured separately (kept in PC Steward's local database)";

    public const string ProviderCacheBoundary = "PC Steward never deletes Windows, WinGet, Microsoft Store or other providers' caches, and never touches installed apps. Each action removes only its named PC Steward category, and each one asks you to confirm first.";

    public const string ActiveWorkReason = "Unavailable while a scan, source check, update or cleanup is running. Cancel it or wait for it to finish.";

    public static DataResetPagePresentation Build(DataResetInputs inputs, CultureInfo? culture = null, TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.ScanData);
        bool busy = inputs.HasActiveWork;
        string? busyReason = busy ? ActiveWorkReason : null;
        string Never(DateTimeOffset? at) => at is { } value ? DisplayFormat.DateTime(value, culture, zone) : "Never";

        DataCardPresentation scan = new(DataCategory.ScanData, "Scan data", "",
            [
                new("Size", $"About {DisplayFormat.Size(inputs.ScanData.SizeBytes, culture)}"),
                new("Items", DisplayFormat.Plural(inputs.ScanData.ItemCount, "item")),
                new("Last cleared", Never(inputs.ScanData.LastClearedAt))
            ],
            "Current inventory, raw provider records, update candidates and scan-run records.",
            "Settings, enabled sources, the schedule, update history, downloaded files, and your mappings, pins and ignores (they apply again when the app is found).",
            [Action(DataAction.ClearScanData, ClearScanDataLabel, "Clear scan data: inventory, raw provider records, candidates and scan runs", !busy, busyReason)]);

        string? downloadsReason = busy ? ActiveWorkReason
            : inputs.DownloadRecoverableBytes <= 0 ? "Nothing to clear. No inactive downloaded files are stored." : null;
        List<DetailFact> downloadFacts =
        [
            new("Size", DisplayFormat.Size(inputs.DownloadTotalBytes, culture)),
            new("Can be freed", DisplayFormat.Size(inputs.DownloadRecoverableBytes, culture)),
        ];
        if (inputs.DownloadActiveCount > 0) downloadFacts.Add(new("In use", $"{DisplayFormat.Plural(inputs.DownloadActiveCount, "file")} kept for an active update"));
        downloadFacts.Add(new("Last cleaned", Never(inputs.DownloadsLastCleanedAt)));
        DataCardPresentation downloads = new(DataCategory.DownloadedFiles, "Downloaded update files", "", downloadFacts,
            "Inactive update payloads and temporary extraction files in PC Steward's own download folder.",
            "Files an active update is using, installed apps, scan data, source metadata, update history, and Windows, WinGet, Microsoft Store and other provider caches.",
            [Action(DataAction.ClearDownloadedFiles, ClearDownloadedFilesLabel, "Clear downloaded files: inactive PC Steward update files only", downloadsReason is null, downloadsReason)]);

        DataCardPresentation sources = new(DataCategory.SourceMetadata, "Source metadata", "",
            [
                new("Size", NotMeasured),
                new("Last updated", string.IsNullOrWhiteSpace(inputs.SourceFreshness) ? "Not checked yet" : inputs.SourceFreshness)
            ],
            "Cached package-source metadata and freshness records.",
            "Inventory, source consent, the schedule, update history, decisions and downloaded files. Checking sources again rebuilds it.",
            [Action(DataAction.ClearSourceMetadata, "Clear source metadata", "Clear source metadata: cached package-source records", !busy, busyReason)]);

        string records = inputs.UpdateHistoryUnavailable ? "Couldn't be read just now"
            : inputs.UpdateRecords >= inputs.UpdateRecordsLimit && inputs.UpdateRecordsLimit > 0 ? $"{inputs.UpdateRecordsLimit.ToString("N0", culture ?? CultureInfo.CurrentCulture)} or more"
            : DisplayFormat.Plural(inputs.UpdateRecords, "record");
        DataCardPresentation logs = new(DataCategory.LogsAndHistory, "Logs and history", "",
            [
                new("Size", NotMeasured),
                new("Update history", records)
            ],
            "Clear logs deletes PC Steward's own file logs only. Clear update history deletes recorded update outcomes only.",
            "Each keeps the other, plus inventory, source metadata, settings, decisions, downloaded files and provider caches.",
            [
                Action(DataAction.ClearLogs, "Clear logs", "Clear logs: PC Steward file logs only", !busy, busyReason),
                Action(DataAction.ClearUpdateHistory, "Clear update history", "Clear update history: recorded update outcomes only", !busy, busyReason)
            ]);

        DataCardPresentation decisions = new(DataCategory.Decisions, "Mappings, pins and ignores", "",
            [new("Active decisions", DisplayFormat.Plural(inputs.DecisionCount, "decision"))],
            "Package identities you confirmed, pins and ignores.",
            "Inventory, settings, source consent, source metadata, update history and downloaded files.",
            [Action(DataAction.ClearMappingsAndPolicies, "Clear mappings and policies", "Clear mappings and policies: confirmed identities, pins and ignores", !busy, busyReason)]);

        FactoryResetCardPresentation reset = new(
            "Danger zone",
            FactoryResetLabel,
            FactoryResetLabel,
            "Factory reset PC Steward: delete all local PC Steward data and settings",
            "All PC Steward scan data, source metadata, logs, update history, downloaded files, settings, source consent, decisions and the current-user scheduled check.",
            "PC Steward itself, installed apps, provider caches and your exports.",
            $"You confirm twice, the second time by typing {FactoryResetConfirmationPhrase}. PC Steward then closes, finishes the cleanup the next time it starts and checks it before first-run setup.",
            FactoryResetConfirmationPhrase,
            !busy,
            busyReason);

        return new([scan, downloads, sources, logs, decisions], reset, ProviderCacheBoundary,
            busy ? "Cleanup is unavailable while a scan, source check, update or cleanup is running. Cancel it or wait for it to finish."
                 : "Routine cleanup doesn't need administrator permission.",
            busy);
    }

    /// <summary>A short "Scan data about 2 MB · Downloads 40 MB" line for the Settings entry card and the Storage page.</summary>
    public static IReadOnlyList<DetailFact> StorageSummary(DataResetInputs inputs, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.ScanData);
        return
        [
            new("Scan data", $"About {DisplayFormat.Size(inputs.ScanData.SizeBytes, culture)} · {DisplayFormat.Plural(inputs.ScanData.ItemCount, "item")}"),
            new("Downloaded files", $"{DisplayFormat.Size(inputs.DownloadTotalBytes, culture)} · {DisplayFormat.Size(inputs.DownloadRecoverableBytes, culture)} can be freed")
        ];
    }

    private static DataActionPresentation Action(DataAction action, string label, string accessibleName, bool enabled, string? reason) =>
        new(action, label, accessibleName, enabled, enabled ? null : reason);
}
