using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record InventoryDashboard(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, DateTimeOffset? LastCompletedAt, ScanDataSummary ScanData, IReadOnlyDictionary<string, ConnectorState> Connectors);
public sealed record TargetedInstalledCompatibility(UpdateScope Scope, UpdateArchitecture Architecture);
public interface ITargetedInstalledInventory
{
    Task<IReadOnlyCollection<TargetedInstalledCompatibility>> ResolveExactCompatibilityAsync(UpdateSource source, string packageId, string installedVersion, CancellationToken cancellationToken);
    Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
}

/// <summary>Application use case for inventory and non-executable update intelligence. It owns no adapter implementation or UI type.</summary>
public sealed class InventoryWorkflow : IDisposable, IPostUpdateInventory
{
    private const string WingetInventoryConnector = "WinGet installed inventory";
    private const string WingetSourceConnector = "WinGet package sources";
    private readonly IInventorySnapshotStore snapshots;
    private readonly IScanDataStore scanData;
    private readonly IConnectorSettingsStore connectorSettings;
    private readonly InventoryScanOrchestrator orchestrator;
    private readonly InventoryExportService exports;
    private readonly UserDecisionService? decisions;
    private readonly UpdateSourceRefreshService? updateSources;
    private readonly IReadOnlyCollection<IUpdateSourceConnector> updateSourceConnectors;
    private readonly ITargetedInstalledInventory? targetedInstalledInventory;
    private readonly bool hasCurrentLocalInventoryProviders;

    public InventoryWorkflow(
        IInventorySnapshotStore snapshots,
        IScanDataStore scanData,
        IConnectorSettingsStore connectorSettings,
        IReadOnlyCollection<ILocalInventoryProvider> providers,
        IScanEventSink? events = null,
        IUserDecisionStore? decisions = null,
        ISourceSnapshotStore? sourceCache = null,
        IReadOnlyCollection<IUpdateSourceConnector>? updateSourceConnectors = null,
        ITargetedInstalledInventory? targetedInstalledInventory = null)
    {
        if ((sourceCache is null) != (updateSourceConnectors is null)) throw new ArgumentException("Source cache and connectors must be configured together.");
        this.snapshots = snapshots; this.scanData = scanData; this.connectorSettings = connectorSettings;
        orchestrator = new InventoryScanOrchestrator(snapshots, providers, events); exports = new InventoryExportService(snapshots);
        hasCurrentLocalInventoryProviders = providers.Count != 0;
        this.decisions = decisions is null ? null : new UserDecisionService(decisions);
        updateSources = sourceCache is null ? null : new UpdateSourceRefreshService(sourceCache);
        this.updateSourceConnectors = updateSourceConnectors ?? [];
        this.targetedInstalledInventory = targetedInstalledInventory;
    }

    public TimeSpan SourceCacheRetention => updateSources?.CacheRetention ?? TimeSpan.FromDays(30);

    public async Task<InventoryDashboard> LoadAsync(IReadOnlyCollection<ConnectorOption> connectors, CancellationToken cancellationToken)
    {
        Dictionary<string, ConnectorState> states = [];
        foreach (ConnectorOption connector in connectors)
        {
            ConnectorState state = await connectorSettings.GetConnectorStateAsync(connector.Name, connector.IsEnabled, cancellationToken).ConfigureAwait(false);
            if (connector.IsEssential) state = state with { Enabled = true };
            if (connector.RequiresSourceDisclosure && !state.SourceDisclosureApproved) state = state with { Enabled = false };
            states.Add(connector.Name, state);
        }
        return new InventoryDashboard(
            await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false),
            await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false),
            await scanData.GetLastCompletedScanAtAsync(cancellationToken).ConfigureAwait(false),
            await scanData.GetScanDataSummaryAsync(cancellationToken).ConfigureAwait(false),
            states);
    }

    public async Task SaveConnectorStateAsync(string connectorName, bool enabled, bool sourceDisclosureApproved, CancellationToken cancellationToken)
    {
        if (RequiresSourceDisclosure(connectorName) && enabled && !sourceDisclosureApproved) throw new InvalidOperationException("This connector requires explicit source-disclosure approval.");
        await connectorSettings.SaveConnectorStateAsync(connectorName, new ConnectorState(enabled, sourceDisclosureApproved), cancellationToken);
    }

    public Task<ScanResult> ScanAsync(IProgress<ScanProgress>? progress, CancellationToken cancellationToken) => orchestrator.ScanAsync(progress, cancellationToken);
    public Task<ClearScanDataResult> ClearScanDataAsync(CancellationToken cancellationToken) => scanData.ClearScanDataAsync(cancellationToken);
    public Task<InventoryExport> CreateExportAsync(bool json, CancellationToken cancellationToken) => json ? exports.CreateJsonAsync(true, cancellationToken) : exports.CreateCsvAsync(cancellationToken);

    public async Task<IReadOnlyCollection<RawProviderRecord>> GetRawDiagnosticsAsync(Guid productId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        HashSet<(string Provider, string Identity)> keys = instances.Where(instance => instance.ProductId == productId).Select(instance => (instance.Provider, instance.ProviderIdentity)).ToHashSet();
        return (await snapshots.GetRawProviderRecordsAsync(cancellationToken).ConfigureAwait(false)).Where(record => keys.Contains((record.Provider, record.RecordKey))).ToArray();
    }

    public Task<UpdateIntelligenceSummary> LoadUpdateIntelligenceAsync(bool sourcesEnabled, CancellationToken cancellationToken) => AssessUpdateIntelligenceAsync(sourcesEnabled, false, cancellationToken);
    public Task<UpdateIntelligenceSummary> RefreshUpdateIntelligenceAsync(bool sourcesEnabled, CancellationToken cancellationToken) => AssessUpdateIntelligenceAsync(sourcesEnabled, true, cancellationToken);

    /// <summary>Prepares only a fresh, persisted-consent source match for explicit update review.</summary>
    public async Task<UpdateSelectionPreparation> PrepareApprovedUpdateSelectionAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stableProductKey)) return new(null, "No installed product was selected.");
        ConnectorState sourceState = await connectorSettings.GetConnectorStateAsync(WingetSourceConnector, false, cancellationToken).ConfigureAwait(false);
        if (!sourceState.Enabled || !sourceState.SourceDisclosureApproved) return new(null, "Approved update sources are not enabled.");

        // A persisted snapshot is never confirmation authority.  Refresh local installed evidence
        // immediately before both review and execution; an unavailable or conflicting provider scan
        // fails closed before a job or provider operation can be constructed.
        if (hasCurrentLocalInventoryProviders)
        {
            ScanResult currentInventory = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
            if (!currentInventory.Succeeded) return new(null, "Current installed inventory is unavailable. No update can be started.");
        }

        UpdateIntelligenceSummary summary = await AssessUpdateIntelligenceAsync(true, true, cancellationToken).ConfigureAwait(false);
        UpdateIntelligenceItem? item = summary.Items.SingleOrDefault(candidate => string.Equals(candidate.StableProductKey, stableProductKey, StringComparison.Ordinal));
        if (item is not { Status: UpdateIntelligenceStatus.VerifiedUpdate, Mapping.IsActionable: true, Mapping.CandidateVersion: not null, SourceId: not null })
        {
            return new(null, item?.Explanation ?? "The selected product is no longer present in current inventory.");
        }

        SourceSnapshot? source = summary.Sources.SingleOrDefault(snapshot => string.Equals(snapshot.SourceId, item.SourceId, StringComparison.OrdinalIgnoreCase));
        if (source?.State != SourceSnapshotState.Live) return new(null, "The approved source did not provide a fresh live result. No update can be started.");

        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        ProductMatchProfile? profile = CreateProfiles(products, instances).SingleOrDefault(product => string.Equals(product.StableProductKey, stableProductKey, StringComparison.Ordinal));
        if (profile is null) return new(null, "The selected product is no longer present in current inventory.");
        if (!TryCreateSelection(profile, item.Mapping, out UpdateJobSelection? selection, out string message))
        {
            if (targetedInstalledInventory is null || profile.InstalledVersion is null || !TrySource(item.Mapping.SourceId, out UpdateSource updateSource)) return new(null, message);
            IReadOnlyCollection<TargetedInstalledCompatibility> compatible = await targetedInstalledInventory.ResolveExactCompatibilityAsync(updateSource, item.Mapping.PackageId, profile.InstalledVersion, cancellationToken).ConfigureAwait(false);
            if (compatible.Count != 1) return new(null, "Exact installed scope and architecture evidence is unavailable or ambiguous. No update can be started.");
            profile = profile with { Scope = compatible.Single().Scope.ToString(), Architecture = compatible.Single().Architecture.ToString() };
            if (!TryCreateSelection(profile, item.Mapping, out selection, out message)) return new(null, message);
        }
        return new(selection, "Fresh exact source evidence is ready for your review.");
    }

    /// <summary>Performs the controlled post-update full scan and only accepts an exact WinGet instance/version match.</summary>
    public async Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (targetedInstalledInventory is null || !await targetedInstalledInventory.HasExactExpectedVersionAsync(job, cancellationToken).ConfigureAwait(false)) return new(false, false);
        ScanResult scan = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
        if (!scan.Succeeded) return new(false, false);
        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        CanonicalProduct[] selectedProducts = products.Where(product => string.Equals(product.StableKey, job.Selection.StableProductKey, StringComparison.Ordinal)).ToArray();
        HashSet<Guid> productIds = selectedProducts.Select(product => product.Id).ToHashSet();
        InstallationInstance[] exactWinget = instances.Where(instance =>
            productIds.Contains(instance.ProductId) &&
            string.Equals(instance.Provider, "winget", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(instance.ProviderIdentity, job.Selection.PackageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(instance.SourceId, SourceId(job.Selection.Source), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(instance.Version, job.Selection.CandidateVersion, StringComparison.Ordinal) &&
            string.Equals(instance.Scope, job.Selection.Scope.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(instance.Architecture, job.Selection.Architecture.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
        // Scope and architecture are proven by the targeted provider query before this scan.  Do not
        // correlate a registry/MSIX row by display name: another product can share that text.
        bool expectedVersionDetected = exactWinget.Length == 1 && selectedProducts.Length == 1;
        return new(expectedVersionDetected, false);
    }

    public Task PinCurrentVersionAsync(string stableProductKey, string currentVersion, string? reason, CancellationToken cancellationToken) => RequiredDecisions().PinCurrentVersionAsync(stableProductKey, currentVersion, reason, cancellationToken);
    public Task PinSourcePackageAsync(string stableProductKey, string sourceId, string packageId, string? reason, CancellationToken cancellationToken) => RequiredDecisions().PinSourcePackageAsync(stableProductKey, sourceId, packageId, reason, cancellationToken);
    public Task IgnoreAsync(string stableProductKey, DateTimeOffset? until, string? reason, CancellationToken cancellationToken) => RequiredDecisions().IgnoreAsync(stableProductKey, until, reason, cancellationToken);
    public Task<IReadOnlyCollection<StableDecision>> GetActiveDecisionsAsync(CancellationToken cancellationToken) => RequiredDecisions().GetActiveDecisionsAsync(cancellationToken);
    public Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken) => RequiredDecisions().GetManualMappingsAsync(cancellationToken);
    public Task ReverseDecisionAsync(Guid decisionId, CancellationToken cancellationToken) => RequiredDecisions().ReverseDecisionAsync(decisionId, cancellationToken);
    public Task DeleteManualMappingAsync(Guid mappingId, CancellationToken cancellationToken) => RequiredDecisions().DeleteManualMappingAsync(mappingId, cancellationToken);
    public async Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        List<ProductTimelineEntry> entries = [.. await RequiredDecisions().GetTimelineAsync(stableProductKey, cancellationToken).ConfigureAwait(false)];
        bool exists = (await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false)).Any(product => string.Equals(product.StableKey, stableProductKey, StringComparison.Ordinal));
        DateTimeOffset? lastScan = exists ? await scanData.GetLastCompletedScanAtAsync(cancellationToken).ConfigureAwait(false) : null;
        if (lastScan is not null) entries.Add(new ProductTimelineEntry(lastScan.Value, "InventoryScan", "Present in the most recent successful inventory scan."));
        return entries.OrderByDescending(entry => entry.OccurredAt).ToArray();
    }

    public async Task<ManualMappingValidation> CreateManualMappingAsync(string stableProductKey, string sourceId, string packageId, bool sourcesEnabled, string? reason, CancellationToken cancellationToken)
    {
        if (!sourcesEnabled) return new(false, MappingReviewReason.UnsupportedManualMapping, "Enable an approved source and run a source check before mapping a package.");
        SourceSnapshot[] snapshots = await ReadSourceSnapshotsAsync(true, false, cancellationToken).ConfigureAwait(false);
        SourcePackageRecord? package = snapshots
            .SelectMany(snapshot => snapshot.Packages)
            .FirstOrDefault(candidate => string.Equals(candidate.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) && string.Equals(candidate.PackageId, packageId, StringComparison.OrdinalIgnoreCase));
        return package is null
            ? new(false, MappingReviewReason.UnsupportedManualMapping, "The package is not present in the current approved source cache.")
            : await RequiredDecisions().CreateManualMappingAsync(stableProductKey, package, snapshots, reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task<UpdateIntelligenceSummary> AssessUpdateIntelligenceAsync(bool sourcesEnabled, bool online, CancellationToken cancellationToken)
    {
        SourceSnapshot[] sourceSnapshots = await ReadSourceSnapshotsAsync(sourcesEnabled, online, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<DeclaredPackageMapping> mappings = decisions is null ? [] : (await decisions.GetManualMappingsAsync(cancellationToken).ConfigureAwait(false)).Select(mapping => new DeclaredPackageMapping(mapping.StableProductKey, mapping.SourceId, mapping.PackageId, MappingOrigin.UserConfirmed)).ToArray();
        IReadOnlyDictionary<string, ProductUpdatePolicy> policies = decisions is null ? new Dictionary<string, ProductUpdatePolicy>() : await decisions.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        return UpdateIntelligence.Assess(CreateProfiles(products, instances), sourceSnapshots, mappings, policies);
    }

    private async Task<SourceSnapshot[]> ReadSourceSnapshotsAsync(bool sourcesEnabled, bool online, CancellationToken cancellationToken)
    {
        if (!sourcesEnabled || updateSources is null || updateSourceConnectors.Count == 0) return [];
        return (await updateSources.RefreshAsync(updateSourceConnectors, online, cancellationToken).ConfigureAwait(false)).ToArray();
    }

    private static ProductMatchProfile[] CreateProfiles(IReadOnlyCollection<CanonicalProduct> products, IReadOnlyCollection<InstallationInstance> instances)
    {
        Dictionary<Guid, InstallationInstance[]> byProduct = instances.GroupBy(instance => instance.ProductId).ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<Guid, CanonicalProduct> productsById = products.ToDictionary(product => product.Id);
        return products.Select(product =>
        {
            InstallationInstance[] productInstances = byProduct.GetValueOrDefault(product.Id, []);
            InstallationInstance? preferred = productInstances.OrderBy(ProviderPriority).ThenBy(instance => instance.ProviderIdentity, StringComparer.Ordinal).FirstOrDefault();
            InstallationInstance[] wingetInstances = productInstances.Where(instance => string.Equals(instance.Provider, "winget", StringComparison.OrdinalIgnoreCase)).ToArray();
            InstallationInstance? winget = wingetInstances.OrderBy(instance => instance.SourceId, StringComparer.Ordinal).ThenBy(instance => instance.ProviderIdentity, StringComparer.Ordinal).FirstOrDefault();
            InstallationInstance? executionEvidence = ConcreteExecutionEvidence(productInstances, winget) ?? preferred;
            InstallationInstance? msix = productInstances.FirstOrDefault(instance => string.Equals(instance.Provider, "msix", StringComparison.OrdinalIgnoreCase));
            bool conflictingTargets = wingetInstances.Select(instance => (instance.ProviderIdentity, instance.SourceId, instance.Version, instance.Scope, instance.Architecture, instance.Channel)).Distinct().Skip(1).Any();
            return new ProductMatchProfile(product.StableKey, product.DisplayName, winget?.ProviderIdentity, msix is null ? null : product.StableKey, null, preferred?.Publisher, winget?.Version ?? executionEvidence?.Version, executionEvidence?.Scope ?? "Unknown", executionEvidence?.Architecture ?? "Unknown", preferred?.Channel, InstalledSourceId: winget?.SourceId, HasConflictingTargetInstances: conflictingTargets);
        }).ToArray();
    }

    private static InstallationInstance? ConcreteExecutionEvidence(IReadOnlyCollection<InstallationInstance> productInstances, InstallationInstance? winget)
    {
        if (winget?.Version is null) return null;
        InstallationInstance[] candidates = productInstances.Where(instance =>
            IsConcreteLocalEvidence(instance) &&
            string.Equals(instance.Version, winget.Version, StringComparison.OrdinalIgnoreCase) &&
            TryScope(instance.Scope, out _) &&
            TryArchitecture(instance.Architecture, out _)).ToArray();
        return candidates.Select(instance => (instance.Scope, instance.Architecture)).Distinct().Skip(1).Any()
            ? null
            : candidates.OrderBy(ProviderPriority).ThenBy(instance => instance.ProviderIdentity, StringComparer.Ordinal).FirstOrDefault();
    }

    private static bool IsConcreteLocalEvidence(InstallationInstance instance) => string.Equals(instance.Provider, "registry", StringComparison.OrdinalIgnoreCase) || string.Equals(instance.Provider, "msix", StringComparison.OrdinalIgnoreCase);

    private static string SourceId(UpdateSource source) => source switch { UpdateSource.WinGet => "winget", UpdateSource.MicrosoftStore => "msstore", _ => string.Empty };

    private static bool TryCreateSelection(ProductMatchProfile profile, PackageMapping mapping, out UpdateJobSelection? selection, out string message)
    {
        selection = null;
        if (mapping.CandidateVersion is null || profile.InstalledVersion is null) { message = "The installed or available version is missing."; return false; }
        if (!TrySource(mapping.SourceId, out UpdateSource source)) { message = "The selected source is not actionable."; return false; }
        if (!TryScope(profile.Scope, out UpdateScope scope)) { message = "The installed scope is not supported for a safe update."; return false; }
        if (!TryArchitecture(profile.Architecture, out UpdateArchitecture architecture)) { message = "The installed architecture is not supported for a safe update."; return false; }
        UpdateChannel channel = string.Equals(profile.Channel, "preview", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Preview : string.Equals(profile.Channel, "stable", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Stable : UpdateChannel.Unknown;
        try
        {
            selection = new UpdateJobSelection(profile.StableProductKey, UpdateProvider.WinGet, source, mapping.PackageId, profile.InstalledVersion, mapping.CandidateVersion, scope, architecture, channel, "source-match-v1");
            message = "Fresh exact source evidence is ready for your review.";
            return true;
        }
        catch (ArgumentException)
        {
            message = "The source evidence contained an unsupported value. No update can be started.";
            return false;
        }
    }

    private static bool TrySource(string sourceId, out UpdateSource source)
    {
        if (string.Equals(sourceId, "winget", StringComparison.OrdinalIgnoreCase)) { source = UpdateSource.WinGet; return true; }
        if (string.Equals(sourceId, "msstore", StringComparison.OrdinalIgnoreCase)) { source = UpdateSource.MicrosoftStore; return true; }
        source = default; return false;
    }

    private static bool TryScope(string scopeName, out UpdateScope scope)
    {
        if (string.Equals(scopeName, "user", StringComparison.OrdinalIgnoreCase)) { scope = UpdateScope.User; return true; }
        if (string.Equals(scopeName, "machine", StringComparison.OrdinalIgnoreCase)) { scope = UpdateScope.Machine; return true; }
        scope = default; return false;
    }

    private static bool TryArchitecture(string architectureName, out UpdateArchitecture architecture)
    {
        if (string.Equals(architectureName, "x64", StringComparison.OrdinalIgnoreCase)) { architecture = UpdateArchitecture.X64; return true; }
        if (string.Equals(architectureName, "x86", StringComparison.OrdinalIgnoreCase)) { architecture = UpdateArchitecture.X86; return true; }
        if (string.Equals(architectureName, "arm64", StringComparison.OrdinalIgnoreCase)) { architecture = UpdateArchitecture.Arm64; return true; }
        if (string.Equals(architectureName, "neutral", StringComparison.OrdinalIgnoreCase)) { architecture = UpdateArchitecture.Neutral; return true; }
        architecture = default; return false;
    }

    private static int ProviderPriority(InstallationInstance instance) => instance.Provider.ToLowerInvariant() switch { "winget" => 0, "msix" => 1, "registry" => 2, _ => 3 };
    private static bool RequiresSourceDisclosure(string connectorName) => connectorName is WingetInventoryConnector or WingetSourceConnector;
    private UserDecisionService RequiredDecisions() => decisions ?? throw new InvalidOperationException("Stable user decisions are not configured for this workflow.");
    public void Dispose() => orchestrator.Dispose();
}
