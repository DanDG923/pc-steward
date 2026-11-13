using System.Collections.ObjectModel;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record ConnectorOption(string Name, string TrustCategory, bool IsEssential, bool IsEnabled, bool RequiresSourceDisclosure = false);
public sealed record InventoryRow(Guid ProductId, string Name, string Publisher, string Provider, string Scope, string Version, string Status, string Classification, IReadOnlyCollection<InstallationInstance> Instances, string StableProductKey = "")
{
    public string DisplayLabel => $"{Name}    {Version}    {Status}    {Scope} · {Provider}";
}
public sealed record ReviewCandidateChoice(string SourceId, string PackageId, string? CandidateVersion, string? Publisher, string Scope, string Architecture, string? Channel, string Evidence, IReadOnlyCollection<string> ReviewReasons)
{
    public string DisplayLabel => $"{SourceId} · {PackageId} · {CandidateVersion ?? "version unavailable"}";
}
public sealed record UpdateDetail(string StableProductKey, string InstalledVersion, string? CandidateVersion, string? SourceId, string? PackageId, string Confidence, string Explanation, string Freshness, string Architecture, string? Channel, string ActionState, IReadOnlyCollection<string> ReviewReasons, IReadOnlyCollection<string> SourceCandidates, IReadOnlyCollection<ReviewCandidateChoice> ReviewCandidates, bool CanConfirmAsCurrent = false, bool CanConfirmPackageIdentity = false, bool RequiresFreshSourceEvidence = false);

/// <summary>Presentation-only state for the local inventory journey. It contains no provider commands or trust decisions.</summary>
public sealed class InventoryExperienceViewModel
{
    private readonly List<InventoryRow> allRows = [];
    public InventoryExperienceViewModel()
    {
        Connectors = new ObservableCollection<ConnectorOption>([
            new("Windows uninstall registry", "Windows local inventory", true, true),
            new("Current-user AppX/MSIX catalog", "Windows local inventory", false, true),
            new("WinGet installed inventory", "Local command-line inventory", false, false, true),
            new("WinGet package sources", "Approved online source metadata", false, false, true)
        ]);
    }

    public ObservableCollection<ConnectorOption> Connectors { get; }
    /// <summary>Unfiltered locally recorded inventory for cross-page summary and update evidence.</summary>
    public IReadOnlyList<InventoryRow> AllRows => allRows;
    public IReadOnlyList<InventoryRow> VisibleRows { get; private set; } = [];
    /// <summary>
    /// UI review 2026-11-13 (fault 3): Installed apps opens on apps the owner recognises. Frameworks, resource packs and Windows
    /// system packages stay in the saved inventory and on every other page, and are listed here only when this is on.
    /// </summary>
    public bool ShowSystemComponents { get; private set; }
    /// <summary>The rows Installed apps lists and counts: every saved row, less system components unless they are shown.</summary>
    public IReadOnlyList<InventoryRow> ListedRows => ShowSystemComponents ? allRows : allRows.Where(row => !InstalledAppsFilters.IsSystemComponent(row)).ToArray();
    /// <summary>How many saved rows are system components.</summary>
    public int SystemComponentCount => allRows.Count(InstalledAppsFilters.IsSystemComponent);
    /// <summary>
    /// Package ID to app name for every app matched to a package, so a recorded update can be named by its app after the update
    /// gave the app a new record. Names only; nothing here is a trust or execution decision.
    /// </summary>
    public IReadOnlyDictionary<string, string> PackageNames
    {
        get
        {
            Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (InventoryRow row in allRows)
            {
                if (!UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail) || string.IsNullOrWhiteSpace(detail.PackageId) || string.IsNullOrWhiteSpace(row.Name)) continue;
                // A row named by its own package ID (WinGet's record of the app) adds nothing; prefer the app's own name.
                if (string.Equals(row.Name, detail.PackageId, StringComparison.OrdinalIgnoreCase)) continue;
                names.TryAdd(detail.PackageId, row.Name);
            }
            return names;
        }
    }
    public IReadOnlyCollection<string> AvailablePublishers => allRows.Select(row => row.Publisher).Where(publisher => !string.Equals(publisher, "Unknown", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(publisher => publisher, StringComparer.OrdinalIgnoreCase).ToArray();
    public string SearchText { get; private set; } = string.Empty;
    public string SourceFilter { get; private set; } = "All sources";
    public string PublisherFilter { get; private set; } = "All publishers";
    public string ScopeFilter { get; private set; } = "All scopes";
    public string StatusFilter { get; private set; } = "All statuses";
    public string SortField { get; private set; } = "Name";
    /// <summary>True when the chosen order is turned round (Z to A, or the last status first).</summary>
    public bool SortDescending { get; private set; }
    /// <summary>Task 73 Phase 3: the saved Installed apps filter (All, Update ready, Check the match, Has an update source, No update source, Pinned, ignored &amp; skipped).</summary>
    public InstalledAppsFilter SavedFilter { get; private set; } = InstalledAppsFilter.All;
    /// <summary>The package-source snapshots from the latest assessment, for freshness in words (FR-UI-05). Presentation only.</summary>
    public IReadOnlyCollection<SourceSnapshot> Sources { get; private set; } = [];
    public string ScanPhase { get; private set; } = ReadyToScan;
    public TimeSpan Elapsed { get; private set; }
    public bool IsScanning { get; private set; }
    /// <summary>The step the running scan is on, of <see cref="ScanSteps"/>; both are 0 when no scan is reporting steps.</summary>
    public int ScanStep { get; private set; }
    public int ScanSteps { get; private set; }
    public IReadOnlyCollection<string> LastScanWarnings { get; private set; } = [];
    public DateTimeOffset? LastScanAt { get; private set; }
    public IReadOnlyDictionary<UpdateIntelligenceStatus, int> UpdateStatusCounts { get; private set; } = Enum.GetValues<UpdateIntelligenceStatus>().ToDictionary(status => status, _ => 0);
    public IReadOnlyCollection<UpdateIntelligenceItem> AttentionNeeded { get; private set; } = [];
    public IReadOnlyDictionary<string, UpdateDetail> UpdateDetails { get; private set; } = new Dictionary<string, UpdateDetail>(StringComparer.Ordinal);
    /// <summary>
    /// Installed apps matched to a package in an update source that is turned on. UI review 2026-11-13 (item 14): these are the
    /// apps under "Has an update source" on Installed apps, which replaces the Repository apps page.
    /// </summary>
    public IReadOnlyList<InventoryRow> RowsWithUpdateSource => !IsUpdateSourceEnabled ? [] : allRows
        .Where(row => row.Status is nameof(UpdateIntelligenceStatus.Current) or nameof(UpdateIntelligenceStatus.VerifiedUpdate) or nameof(UpdateIntelligenceStatus.ReviewRequired) or nameof(UpdateIntelligenceStatus.FailedUnavailable) or nameof(UpdateIntelligenceStatus.Skipped)
            && UpdateDetails.TryGetValue(row.StableProductKey, out UpdateDetail? detail)
            && !string.IsNullOrWhiteSpace(detail.SourceId)
            && !string.IsNullOrWhiteSpace(detail.PackageId))
        .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    /// <summary>The keys of <see cref="RowsWithUpdateSource"/>, for the "Has an update source" tab and its count.</summary>
    public IReadOnlySet<string> UpdateSourceKeys => RowsWithUpdateSource.Select(row => row.StableProductKey).ToHashSet(StringComparer.Ordinal);
    public string UpdateFreshnessLabel { get; private set; } = "Update checks are off.";
    public IReadOnlyCollection<string> SourceFreshnessLabels { get; private set; } = [];
    public bool IsCheckingUpdates { get; private set; }
    public bool IsWinGetEnabled => Connectors.Any(connector => connector.Name == "WinGet installed inventory" && connector.IsEnabled);
    public bool IsUpdateSourceEnabled => Connectors.Any(connector => connector.Name == "WinGet package sources" && connector.IsEnabled);
    public bool IsWinGetTrustedSourcesEnabled => IsWinGetEnabled && IsUpdateSourceEnabled;
    /// <summary>The scan page's resting status before any scan, and with a completed scan on screen (UI review 2026-11-13, item 8).</summary>
    public const string ReadyToScan = "Ready to scan";
    public const string ShowingLastScan = "Showing your last scan";
    public string ScanModeLabel => IsWinGetEnabled ? "Apps Windows lists, plus apps WinGet lists" : "Apps Windows lists";
    public string LastScanCompletedLabel => LastScanAt is null ? "not yet" : FriendlyLocalTime(LastScanAt.Value);
    public string FreshnessLabel => LastScanAt is null ? "Not scanned yet" : $"Last scan completed {LastScanCompletedLabel}";

    public void SetConnectorEnabled(string name, bool enabled)
    {
        int index = Connectors.ToList().FindIndex(connector => connector.Name == name);
        if (index < 0 || (Connectors[index].IsEssential && !enabled)) return;
        ConnectorOption connector = Connectors[index]; Connectors[index] = connector with { IsEnabled = enabled };
        if (!IsScanning) ScanPhase = LastScanAt is null ? ReadyToScan : ShowingLastScan;
        // "Has an update source" lists nothing while update checks are off, so that tab follows the switch.
        if (SavedFilter == InstalledAppsFilter.HasUpdateSource) ApplyFilters();
    }

    public void SetFilters(string? search, string? status, string? publisher, string? source, string? scope, string? sortField) =>
        SetFilters(search, status, publisher, source, scope, sortField, SortDescending);

    /// <summary>The filters and the sort together; <paramref name="sortDescending"/> turns the chosen order round (UI review item 18).</summary>
    public void SetFilters(string? search, string? status, string? publisher, string? source, string? scope, string? sortField, bool sortDescending)
    {
        SortDescending = sortDescending;
        SearchText = search?.Trim() ?? string.Empty; StatusFilter = status ?? "All statuses"; PublisherFilter = publisher ?? "All publishers"; SourceFilter = source ?? "All sources"; ScopeFilter = scope ?? "All scopes"; SortField = sortField ?? "Name"; ApplyFilters();
    }
    public void SetFilters(string? search, string? status, string? source, string? scope, string? sortField) => SetFilters(search, status, PublisherFilter, source, scope, sortField);

    /// <summary>Applies a saved Installed apps filter. It combines with search and the other filters; saved data never changes.</summary>
    public void SetSavedFilter(InstalledAppsFilter filter)
    {
        SavedFilter = Enum.IsDefined(filter) ? filter : InstalledAppsFilter.All; ApplyFilters();
    }

    /// <summary>Shows or hides system components on Installed apps. Saved data never changes.</summary>
    public void SetShowSystemComponents(bool show) { ShowSystemComponents = show; ApplyFilters(); }

    public void LoadInventory(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, DateTimeOffset? lastScanAt = null)
    {
        allRows.Clear();
        Dictionary<Guid, InstallationInstance[]> instancesByProduct = instances.GroupBy(instance => instance.ProductId).ToDictionary(group => group.Key, group => group.OrderBy(instance => instance.Provider, StringComparer.Ordinal).ToArray());
        foreach (CanonicalProduct product in products)
        {
            InstallationInstance[] productInstances = instancesByProduct.GetValueOrDefault(product.Id, []);
            InstallationInstance? first = productInstances.FirstOrDefault();
            allRows.Add(new InventoryRow(product.Id, product.DisplayName, first?.Publisher ?? "Unknown", first?.Provider ?? "Unknown", first?.Scope ?? "Unknown", first?.Version ?? "Unknown", "Installed", first?.Classification ?? "Unknown", productInstances, product.StableKey));
        }
        LastScanAt = lastScanAt; ScanPhase = lastScanAt is null ? ReadyToScan : ShowingLastScan; ApplyFilters();
    }

    public void ReportProgress(ScanProgress progress) { IsScanning = true; ScanPhase = progress.Provider is null ? progress.Phase : $"{progress.Phase}: {progress.Provider}"; Elapsed = progress.Elapsed; ScanStep = progress.Step; ScanSteps = progress.Steps; }
    public void CompleteScan(ScanResult result, DateTimeOffset completedAt) { IsScanning = false; ScanStep = 0; ScanSteps = 0; Elapsed = result.Timings.Values.Aggregate(TimeSpan.Zero, (total, duration) => total + duration); LastScanWarnings = result.Warnings; ScanPhase = result.Cancelled ? "Scan cancelled" : result.Succeeded ? "Scan complete" : "Scan did not complete"; if (result.Succeeded) LastScanAt = completedAt; }
    public void ClearScanData() { allRows.Clear(); VisibleRows = []; LastScanAt = null; Elapsed = TimeSpan.Zero; ScanPhase = ReadyToScan; }
    public void BeginUpdateCheck() { IsCheckingUpdates = true; UpdateFreshnessLabel = "Looking for updates. Nothing is installed."; }
    public void CompleteUpdateCheck() => IsCheckingUpdates = false;
    public void LoadUpdateIntelligence(UpdateIntelligenceSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary); UpdateStatusCounts = summary.Counts; AttentionNeeded = summary.AttentionNeeded; Sources = summary.Sources.ToArray();
        Dictionary<string, UpdateIntelligenceItem> items = summary.Items.ToDictionary(item => item.StableProductKey, StringComparer.Ordinal);
        // A row the new summary has no item for (WinGet's own record of an app another row already carries) goes back to plain
        // "Installed", so a status from an earlier assessment can't keep it on the Updates page.
        for (int index = 0; index < allRows.Count; index++)
            allRows[index] = allRows[index] with { Status = items.TryGetValue(allRows[index].StableProductKey, out UpdateIntelligenceItem? item) ? item.Status.ToString() : "Installed" };
        Dictionary<string, SourceSnapshot> sources = summary.Sources.ToDictionary(snapshot => snapshot.SourceId, StringComparer.OrdinalIgnoreCase);
        SourceFreshnessLabels = summary.Sources.OrderBy(snapshot => snapshot.SourceId, StringComparer.Ordinal).Select(DescribeSource).ToArray();
        UpdateFreshnessLabel = SourceFreshnessLabels.Count == 0 ? "Update checks are off." : string.Join(" ", SourceFreshnessLabels);
        string InstalledVersionFor(string stableProductKey) => allRows.FirstOrDefault(row => row.StableProductKey == stableProductKey)?.Version ?? "Unknown";
        UpdateDetails = summary.Items.ToDictionary(item => item.StableProductKey, item =>
        {
            string installedVersion = InstalledVersionFor(item.StableProductKey);
            ReviewCandidateChoice[] reviewCandidates = (item.SourceCandidates ?? []).Select(CreateReviewCandidate).ToArray();
            bool canConfirmPackageIdentity = CanConfirmPackageIdentity(item, sources, installedVersion);
            bool requiresFreshSourceEvidence = !canConfirmPackageIdentity && RequiresFreshSourceEvidence(item, sources);
            return new UpdateDetail(
                item.StableProductKey,
                installedVersion,
                item.Mapping?.CandidateVersion,
                item.SourceId,
                item.Mapping?.PackageId,
                item.Mapping?.Confidence.ToString() ?? "None",
                item.Explanation,
                item.SourceId is not null && sources.TryGetValue(item.SourceId, out SourceSnapshot? source) ? DescribeSource(source) : UpdateFreshnessLabel,
                allRows.FirstOrDefault(row => row.StableProductKey == item.StableProductKey)?.Instances.FirstOrDefault()?.Architecture ?? "Unknown",
                allRows.FirstOrDefault(row => row.StableProductKey == item.StableProductKey)?.Instances.FirstOrDefault()?.Channel,
                DescribeAvailableActions(item, reviewCandidates, canConfirmPackageIdentity, requiresFreshSourceEvidence),
                item.Mapping?.ReviewReasons.Select(reason => reason.ToString()).ToArray() ?? [],
                reviewCandidates.Select(candidate => candidate.Evidence).ToArray(),
                reviewCandidates,
                CanConfirmAsCurrent(item, sources),
                canConfirmPackageIdentity,
                requiresFreshSourceEvidence);
        }, StringComparer.Ordinal);
        IsCheckingUpdates = false;
        ApplyFilters();
    }

    private static string DescribeSource(SourceSnapshot source)
    {
        string warningDescription = DescribeSourceWarnings(source.Warnings);
        string warningSuffix = warningDescription.Length == 0 ? string.Empty : $" {warningDescription}";
        return source.State switch
        {
            SourceSnapshotState.Live => $"{source.SourceId}: live result from {FriendlyLocalTime(source.LastSuccessAt)}.{warningSuffix}",
            SourceSnapshotState.Cached => $"{source.SourceId}: cached result from {FriendlyLocalTime(source.LastSuccessAt)}; source data may be stale.{warningSuffix}",
            SourceSnapshotState.Unavailable => $"{source.SourceId}: unavailable; last successful refresh {(source.LastSuccessAt is null ? "is unavailable" : FriendlyLocalTime(source.LastSuccessAt.Value))}.{warningSuffix}",
            _ => $"{source.SourceId}: local-only; no source check has been run.{warningSuffix}"
        };
    }

    private static string DescribeSourceWarnings(IEnumerable<string> warnings) => string.Join(" ", warnings
        .Select(warning => warning switch
        {
            "source:stale" => string.Empty,
            "source:offline" => "The source was offline.",
            _ when warning.EndsWith(":update-check-unavailable", StringComparison.OrdinalIgnoreCase) => "Update availability was not established.",
            _ when warning.EndsWith(":source-inventory-fallback", StringComparison.OrdinalIgnoreCase) => "Installed package identity came from fallback inventory.",
            _ => $"Source warning: {warning.Trim().TrimEnd('.')}."
        })
        .Where(description => description.Length > 0)
        .Distinct(StringComparer.Ordinal));

    private static string DescribeCandidate(SourceCandidateObservation candidate) => $"{candidate.SourceId}: {candidate.PackageId} {candidate.CandidateVersion ?? "unknown version"}; {candidate.State.ToString().ToLowerInvariant()} result from {(candidate.LastSuccessAt is null ? "an unknown time" : FriendlyLocalTime(candidate.LastSuccessAt.Value))}; confidence {candidate.Confidence}.";

    private static ReviewCandidateChoice CreateReviewCandidate(SourceCandidateObservation candidate) => new(
        candidate.SourceId,
        candidate.PackageId,
        candidate.CandidateVersion,
        candidate.Publisher,
        candidate.Scope,
        candidate.Architecture,
        candidate.Channel,
        DescribeCandidate(candidate),
        candidate.ReviewReasons.Select(reason => reason.ToString()).ToArray());

    private static string DescribeAvailableActions(UpdateIntelligenceItem item, ReviewCandidateChoice[] candidates, bool canConfirmPackageIdentity, bool requiresFreshSourceEvidence)
    {
        if (item.Status == UpdateIntelligenceStatus.VerifiedUpdate) return "A fresh exact match can be reviewed before an update is started.";
        if (item.Status == UpdateIntelligenceStatus.FailedUnavailable && item.Mapping is not null) return "Available action: refresh source evidence. The repository identified this package but did not establish update availability. This is not a decision for you to make; PC Steward will not guess a version or start an update.";
        if (item.Status == UpdateIntelligenceStatus.FailedUnavailable) return "Available action: refresh source evidence. No update can be considered until the repository responds with usable evidence, and no update starts.";
        if (item.Status != UpdateIntelligenceStatus.ReviewRequired) return "No automatic action is available. You can record a reversible pin or ignore decision.";
        if (canConfirmPackageIdentity) return "Available actions: confirm this exact package identity, pin the installed version, or ignore the product. None starts an update.";
        if (item.Mapping is null && candidates.Length > 0) return "Available actions: choose an exact package identity, pin the installed version, or ignore the product. None starts an update.";
        if (item.Mapping?.ReviewReasons.Contains(MappingReviewReason.IncomparableVersion) == true && requiresFreshSourceEvidence) return "Available actions: refresh source evidence, pin the installed version, or ignore the product. PC Steward will not guess the version order even after a refresh. No update starts.";
        if (item.Mapping?.ReviewReasons.Contains(MappingReviewReason.IncomparableVersion) == true) return "Available actions: pin the installed version or ignore the product. PC Steward will not guess the version order.";
        if (requiresFreshSourceEvidence) return "Available actions: refresh source evidence, pin the installed version, or ignore the product. A successful live refresh is required before this package identity can be confirmed. No update starts.";
        return "Available actions: pin the installed version or ignore the product after reviewing the evidence. None starts an update.";
    }

    private static bool RequiresFreshSourceEvidence(UpdateIntelligenceItem item, Dictionary<string, SourceSnapshot> sources)
    {
        if (item.Status is not (UpdateIntelligenceStatus.ReviewRequired or UpdateIntelligenceStatus.FailedUnavailable)) return false;
        if (item.SourceId is { } sourceId)
        {
            if (!sources.TryGetValue(sourceId, out SourceSnapshot? source) || source.State != SourceSnapshotState.Live) return true;
            if (item.Mapping is not { PackageId: { } packageId, CandidateVersion: null }) return false;
            SourcePackageRecord[] exactPackages = source.Packages
                .Where(package => string.Equals(package.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(package.PackageId, packageId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return exactPackages.Length == 1 && exactPackages[0].AvailableVersion is null;
        }

        SourceCandidateObservation[] candidates = (item.SourceCandidates ?? []).ToArray();
        if (candidates.Length > 0) return candidates.All(candidate => candidate.State != SourceSnapshotState.Live);
        return sources.Count > 0 && sources.Values.All(source => source.State != SourceSnapshotState.Live);
    }

    private static bool CanConfirmAsCurrent(UpdateIntelligenceItem item, Dictionary<string, SourceSnapshot> sources)
    {
        if (item.Status != UpdateIntelligenceStatus.ReviewRequired || item.Mapping is not { Origin: not MappingOrigin.UserConfirmed, SourceId: { } sourceId, PackageId: { } packageId }) return false;
        if (!sources.TryGetValue(sourceId, out SourceSnapshot? source) || source.State is not (SourceSnapshotState.Live or SourceSnapshotState.Cached)) return false;
        SourcePackageRecord? package = source.Packages.FirstOrDefault(candidate => string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        return package?.InstalledVersion is not null && package.AvailableVersion is not null && string.Equals(package.InstalledVersion, package.AvailableVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanConfirmPackageIdentity(UpdateIntelligenceItem item, Dictionary<string, SourceSnapshot> sources, string installedVersion)
    {
        if (item.Status != UpdateIntelligenceStatus.ReviewRequired || item.Mapping is not { Origin: not MappingOrigin.UserConfirmed, SourceId: { } sourceId, PackageId: { } packageId, CandidateVersion: { } candidateVersion }) return false;
        if (!sources.TryGetValue(sourceId, out SourceSnapshot? source) || source.State is not (SourceSnapshotState.Live or SourceSnapshotState.Cached)) return false;
        SourcePackageRecord[] exact = source.Packages.Where(candidate => string.Equals(candidate.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) && string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length != 1 || !string.Equals(exact[0].InstalledVersion, installedVersion, StringComparison.Ordinal) || !string.Equals(exact[0].AvailableVersion, candidateVersion, StringComparison.Ordinal)) return false;
        VersionComparisonOutcome outcome = VersionComparison.Compare(new(installedVersion, candidateVersion, false)).Outcome;
        return outcome == VersionComparisonOutcome.Equal || (source.State == SourceSnapshotState.Live && outcome == VersionComparisonOutcome.Newer);
    }

    private static string FriendlyLocalTime(DateTimeOffset? value) => value is null ? "an unknown time" : FriendlyLocalTime(value.Value);
    private static string FriendlyLocalTime(DateTimeOffset value) => DisplayFormat.DateTime(value);

    private void ApplyFilters()
    {
        IReadOnlySet<string>? withUpdateSource = SavedFilter == InstalledAppsFilter.HasUpdateSource ? UpdateSourceKeys : null;
        IEnumerable<InventoryRow> rows = ListedRows.Where(row => InstalledAppsFilters.Matches(SavedFilter, row, withUpdateSource));
        if (!string.IsNullOrWhiteSpace(SearchText)) rows = rows.Where(row => row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || row.Publisher.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        if (StatusFilter != "All statuses") rows = rows.Where(row => row.Status == StatusFilter);
        if (PublisherFilter != "All publishers") rows = rows.Where(row => row.Publisher == PublisherFilter);
        if (SourceFilter != "All sources") rows = rows.Where(row => row.Provider == SourceFilter);
        if (ScopeFilter != "All scopes") rows = rows.Where(row => row.Scope == ScopeFilter);
        VisibleRows = InstalledAppsFilters.Sorted(rows, SortField, SortDescending);
    }
}
