using System.Collections.ObjectModel;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record ConnectorOption(string Name, string TrustCategory, bool IsEssential, bool IsEnabled);
public sealed record InventoryRow(Guid ProductId, string Name, string Provider, string Scope, string Version, string Status, IReadOnlyCollection<InstallationInstance> Instances);

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
    public string SearchText { get; private set; } = string.Empty;
    public string SourceFilter { get; private set; } = "All sources";
    public string ScopeFilter { get; private set; } = "All scopes";
    public string StatusFilter { get; private set; } = "All statuses";
    public string SortField { get; private set; } = "Name";
    public string ScanPhase { get; private set; } = "Ready for a local-only scan";
    public TimeSpan Elapsed { get; private set; }
    public bool IsScanning { get; private set; }
    public DateTimeOffset? LastScanAt { get; private set; }
    public string FreshnessLabel => LastScanAt is null ? "No scan yet — local-only" : $"Local-only scan completed {LastScanAt:O}";

    public void SetConnectorEnabled(string name, bool enabled)
    {
        int index = Connectors.ToList().FindIndex(connector => connector.Name == name);
        if (index < 0 || (Connectors[index].IsEssential && !enabled)) return;
        ConnectorOption connector = Connectors[index]; Connectors[index] = connector with { IsEnabled = enabled };
    }

    public void SetFilters(string? search, string? status, string? source, string? scope, string? sortField)
    {
        SearchText = search?.Trim() ?? string.Empty; StatusFilter = status ?? "All statuses"; SourceFilter = source ?? "All sources"; ScopeFilter = scope ?? "All scopes"; SortField = sortField ?? "Name"; ApplyFilters();
    }

    public void LoadInventory(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances, DateTimeOffset? lastScanAt = null)
    {
        allRows.Clear();
        foreach (CanonicalProduct product in products)
        {
            InstallationInstance[] productInstances = instances.Where(instance => instance.ProductId == product.Id).OrderBy(instance => instance.Provider, StringComparer.Ordinal).ToArray();
            InstallationInstance? first = productInstances.FirstOrDefault();
            allRows.Add(new InventoryRow(product.Id, product.DisplayName, first?.Provider ?? "Unknown", first?.Scope ?? "Unknown", first?.Version ?? "Unknown", "Installed", productInstances));
        }
        LastScanAt = lastScanAt; ScanPhase = lastScanAt is null ? "Ready for a local-only scan" : "Cached local-only inventory"; ApplyFilters();
    }

    public void ReportProgress(ScanProgress progress) { IsScanning = true; ScanPhase = progress.Provider is null ? progress.Phase : $"{progress.Phase}: {progress.Provider}"; Elapsed = progress.Elapsed; }
    public void CompleteScan(ScanResult result, DateTimeOffset completedAt) { IsScanning = false; Elapsed = result.Timings.Values.Aggregate(TimeSpan.Zero, (total, duration) => total + duration); ScanPhase = result.Cancelled ? "Scan cancelled" : result.Succeeded ? "Local-only scan complete" : "Scan did not complete"; if (result.Succeeded) LastScanAt = completedAt; }
    public void ClearScanData() { allRows.Clear(); VisibleRows = []; LastScanAt = null; Elapsed = TimeSpan.Zero; ScanPhase = "No scan data — ready for a local-only scan"; }

    private void ApplyFilters()
    {
        IEnumerable<InventoryRow> rows = allRows;
        if (!string.IsNullOrWhiteSpace(SearchText)) rows = rows.Where(row => row.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        if (StatusFilter != "All statuses") rows = rows.Where(row => row.Status == StatusFilter);
        if (SourceFilter != "All sources") rows = rows.Where(row => row.Provider == SourceFilter);
        if (ScopeFilter != "All scopes") rows = rows.Where(row => row.Scope == ScopeFilter);
        VisibleRows = SortField == "Source" ? rows.OrderBy(row => row.Provider, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray() : rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
