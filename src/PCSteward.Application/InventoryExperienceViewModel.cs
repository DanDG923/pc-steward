using System.Collections.ObjectModel;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record ConnectorOption(string Name, string TrustCategory, bool IsEssential, bool IsEnabled);
public sealed record InventoryRow(Guid ProductId, string Name, string Publisher, string Provider, string Scope, string Version, string Status, string Classification, IReadOnlyCollection<InstallationInstance> Instances);

/// <summary>Presentation-only state for the local inventory journey. It contains no provider commands or trust decisions.</summary>
public sealed class InventoryExperienceViewModel
{
    private readonly List<InventoryRow> allRows = [];
    public InventoryExperienceViewModel()
    {
        Connectors = new ObservableCollection<ConnectorOption>([
            new("Windows uninstall registry", "Windows local inventory", true, true),
            new("Current-user AppX/MSIX catalog", "Windows local inventory", false, true),
            new("WinGet installed inventory", "Local command-line inventory", false, false)
        ]);
    }

    public ObservableCollection<ConnectorOption> Connectors { get; }
    public IReadOnlyList<InventoryRow> VisibleRows { get; private set; } = [];
    public IReadOnlyCollection<string> AvailablePublishers => allRows.Select(row => row.Publisher).Where(publisher => !string.Equals(publisher, "Unknown", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(publisher => publisher, StringComparer.OrdinalIgnoreCase).ToArray();
    public string SearchText { get; private set; } = string.Empty;
    public string SourceFilter { get; private set; } = "All sources";
    public string PublisherFilter { get; private set; } = "All publishers";
    public string ScopeFilter { get; private set; } = "All scopes";
    public string StatusFilter { get; private set; } = "All statuses";
    public string SortField { get; private set; } = "Name";
    public string ScanPhase { get; private set; } = "Ready for a local-only scan";
    public TimeSpan Elapsed { get; private set; }
    public bool IsScanning { get; private set; }
    public DateTimeOffset? LastScanAt { get; private set; }
    public bool IsWinGetEnabled => Connectors.Any(connector => connector.Name == "WinGet installed inventory" && connector.IsEnabled);
    public string ScanModeLabel => IsWinGetEnabled ? "Windows-local inventory plus approved WinGet installed inventory" : "Local-only Windows inventory";
    public string FreshnessLabel => LastScanAt is null ? $"No scan yet — {ScanModeLabel}" : $"{ScanModeLabel} completed {LastScanAt:O}";

    public void SetConnectorEnabled(string name, bool enabled)
    {
        int index = Connectors.ToList().FindIndex(connector => connector.Name == name);
        if (index < 0 || (Connectors[index].IsEssential && !enabled)) return;
        ConnectorOption connector = Connectors[index]; Connectors[index] = connector with { IsEnabled = enabled };
        if (!IsScanning) ScanPhase = LastScanAt is null ? $"Ready for {ScanModeLabel}" : $"Cached {ScanModeLabel}";
    }

    public void SetFilters(string? search, string? status, string? publisher, string? source, string? scope, string? sortField)
    {
        SearchText = search?.Trim() ?? string.Empty; StatusFilter = status ?? "All statuses"; PublisherFilter = publisher ?? "All publishers"; SourceFilter = source ?? "All sources"; ScopeFilter = scope ?? "All scopes"; SortField = sortField ?? "Name"; ApplyFilters();
    }
    public void SetFilters(string? search, string? status, string? source, string? scope, string? sortField) => SetFilters(search, status, PublisherFilter, source, scope, sortField);

    public void LoadInventory(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, DateTimeOffset? lastScanAt = null)
    {
        allRows.Clear();
        Dictionary<Guid, InstallationInstance[]> instancesByProduct = instances.GroupBy(instance => instance.ProductId).ToDictionary(group => group.Key, group => group.OrderBy(instance => instance.Provider, StringComparer.Ordinal).ToArray());
        foreach (CanonicalProduct product in products)
        {
            InstallationInstance[] productInstances = instancesByProduct.GetValueOrDefault(product.Id, []);
            InstallationInstance? first = productInstances.FirstOrDefault();
            allRows.Add(new InventoryRow(product.Id, product.DisplayName, first?.Publisher ?? "Unknown", first?.Provider ?? "Unknown", first?.Scope ?? "Unknown", first?.Version ?? "Unknown", "Installed", first?.Classification ?? "Unknown", productInstances));
        }
        LastScanAt = lastScanAt; ScanPhase = lastScanAt is null ? $"Ready for a {ScanModeLabel}" : $"Cached {ScanModeLabel}"; ApplyFilters();
    }

    public void ReportProgress(ScanProgress progress) { IsScanning = true; ScanPhase = progress.Provider is null ? progress.Phase : $"{progress.Phase}: {progress.Provider}"; Elapsed = progress.Elapsed; }
    public void CompleteScan(ScanResult result, DateTimeOffset completedAt) { IsScanning = false; Elapsed = result.Timings.Values.Aggregate(TimeSpan.Zero, (total, duration) => total + duration); ScanPhase = result.Cancelled ? "Scan cancelled" : result.Succeeded ? $"{ScanModeLabel} scan complete" : "Scan did not complete"; if (result.Succeeded) LastScanAt = completedAt; }
    public void ClearScanData() { allRows.Clear(); VisibleRows = []; LastScanAt = null; Elapsed = TimeSpan.Zero; ScanPhase = $"No scan data — ready for {ScanModeLabel}"; }

    private void ApplyFilters()
    {
        IEnumerable<InventoryRow> rows = allRows;
        if (!string.IsNullOrWhiteSpace(SearchText)) rows = rows.Where(row => row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        if (StatusFilter != "All statuses") rows = rows.Where(row => row.Status == StatusFilter);
        if (PublisherFilter != "All publishers") rows = rows.Where(row => row.Publisher == PublisherFilter);
        if (SourceFilter != "All sources") rows = rows.Where(row => row.Provider == SourceFilter);
        if (ScopeFilter != "All scopes") rows = rows.Where(row => row.Scope == ScopeFilter);
        VisibleRows = SortField switch { "Source" => rows.OrderBy(row => row.Provider, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray(), "Publisher" => rows.OrderBy(row => row.Publisher, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray(), _ => rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray() };
    }
}
