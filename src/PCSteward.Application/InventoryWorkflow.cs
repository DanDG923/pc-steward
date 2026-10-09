using PCSteward.Catalog;
using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record InventoryDashboard(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, DateTimeOffset? LastCompletedAt, ScanDataSummary ScanData, IReadOnlyDictionary<string, ConnectorState> Connectors);
public sealed record RepositoryIdentityConfirmation(string StableProductKey, string SourceId, string PackageId);
public sealed record RepositoryIdentityConfirmationResult(int Confirmed, int Skipped, string Message);
public interface IPostUpdatePackageInventory
{
    Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
}

/// <summary>Normalized bounded result returned only by a provider-owned protected machine operation.</summary>
public sealed record ProviderProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut, bool OutputLimitExceeded = false);
public interface IProtectedMachineUpdateExecutor
{
    Task<ProviderProcessResult> ExecuteProtectedAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);
}

public sealed class CompositePostUpdatePackageInventory(IEnumerable<IPostUpdatePackageInventory> inventories) : IPostUpdatePackageInventory
{
    private readonly IPostUpdatePackageInventory[] inventories = (inventories ?? throw new ArgumentNullException(nameof(inventories))).ToArray();
    public async Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        foreach (IPostUpdatePackageInventory inventory in inventories)
        {
            if (await inventory.HasExactExpectedVersionAsync(job, cancellationToken).ConfigureAwait(false)) return true;
        }
        return false;
    }
}

/// <summary>
/// Supplies bounded provider-structured candidate evidence. Display-table fields never implement
/// this authority and therefore cannot become immutable job or provider arguments.
/// </summary>
public interface IStructuredUpdateCandidateAuthority
{
    Task<StructuredUpdateCandidateEvidence?> ResolveAsync(
        string sourceId,
        string packageId,
        CancellationToken cancellationToken);
}

public sealed record StructuredUpdateCandidateEvidence(
    string SourceId,
    string PackageId,
    string InstalledVersion,
    string CandidateVersion);

/// <summary>Deployment policy for the evidence that may reach an explicit final user review.</summary>
/// <summary>Compile-time composition policy for update scopes exposed by a deployment.</summary>
public sealed record UpdateExecutionCapabilities(bool MachineScopeEnabled)
{
    public static UpdateExecutionCapabilities UserScopeOnly { get; } = new(false);
    public static UpdateExecutionCapabilities UserAndMachine { get; } = new(true);
}

/// <summary>Routes fixed-source candidate authorities without allowing display data to become execution authority.</summary>
public sealed class CompositeStructuredUpdateCandidateAuthority(IEnumerable<IStructuredUpdateCandidateAuthority> authorities) : IStructuredUpdateCandidateAuthority
{
    private readonly IStructuredUpdateCandidateAuthority[] authorities = (authorities ?? throw new ArgumentNullException(nameof(authorities))).ToArray();
    public async Task<StructuredUpdateCandidateEvidence?> ResolveAsync(string sourceId, string packageId, CancellationToken cancellationToken)
    {
        foreach (IStructuredUpdateCandidateAuthority authority in authorities)
        {
            StructuredUpdateCandidateEvidence? evidence = await authority.ResolveAsync(sourceId, packageId, cancellationToken).ConfigureAwait(false);
            if (evidence is not null) return evidence;
        }
        return null;
    }
}

/// <summary>Application use case for inventory and non-executable update intelligence. It owns no adapter implementation or UI type.</summary>
public sealed class InventoryWorkflow : IDisposable, IPostUpdateInventory
{
    private readonly IInventorySnapshotStore snapshots;
    private readonly IScanDataStore scanData;
    private readonly IConnectorSettingsStore connectorSettings;
    private readonly InventoryScanOrchestrator orchestrator;
    private readonly InventoryExportService exports;
    private readonly UserDecisionService? decisions;
    private readonly UpdateSourceRefreshService? updateSources;
    private readonly IReadOnlyCollection<IUpdateSourceConnector> updateSourceConnectors;
    private readonly IPostUpdatePackageInventory? postUpdatePackageInventory;
    private readonly IStructuredUpdateCandidateAuthority? structuredUpdateCandidates;
    private readonly IAdvancedDataControlStore? advancedData;
    private readonly bool hasCurrentLocalInventoryProviders;
    private readonly UpdateExecutionCapabilities executionCapabilities;
    private readonly PersonalCatalog? catalog;
    private readonly IUserCatalogStore? userCatalog;
    private readonly IInstallPreflightAuthority? installPreflight;

    public InventoryWorkflow(
        IInventorySnapshotStore snapshots,
        IScanDataStore scanData,
        IConnectorSettingsStore connectorSettings,
        IReadOnlyCollection<ILocalInventoryProvider> providers,
        IScanEventSink? events = null,
        IUserDecisionStore? decisions = null,
        ISourceSnapshotStore? sourceCache = null,
        IReadOnlyCollection<IUpdateSourceConnector>? updateSourceConnectors = null,
        IPostUpdatePackageInventory? postUpdatePackageInventory = null,
        IStructuredUpdateCandidateAuthority? structuredUpdateCandidates = null,
        IAdvancedDataControlStore? advancedData = null,
        UpdateExecutionCapabilities? executionCapabilities = null,
        PersonalCatalog? catalog = null,
        IUserCatalogStore? userCatalog = null,
        IInstallPreflightAuthority? installPreflight = null)
    {
        if ((sourceCache is null) != (updateSourceConnectors is null)) throw new ArgumentException("Source cache and connectors must be configured together.");
        this.snapshots = snapshots; this.scanData = scanData; this.connectorSettings = connectorSettings;
        orchestrator = new InventoryScanOrchestrator(snapshots, providers, events); exports = new InventoryExportService(snapshots);
        hasCurrentLocalInventoryProviders = providers.Count != 0;
        this.decisions = decisions is null ? null : new UserDecisionService(decisions);
        updateSources = sourceCache is null ? null : new UpdateSourceRefreshService(sourceCache);
        this.updateSourceConnectors = updateSourceConnectors ?? [];
        this.postUpdatePackageInventory = postUpdatePackageInventory;
        this.structuredUpdateCandidates = structuredUpdateCandidates;
        this.advancedData = advancedData;
        this.executionCapabilities = executionCapabilities ?? UpdateExecutionCapabilities.UserAndMachine;
        this.catalog = catalog;
        this.userCatalog = userCatalog;
        this.installPreflight = installPreflight;
    }

    public TimeSpan SourceCacheRetention => updateSources?.CacheRetention ?? TimeSpan.FromDays(30);

    public Task<AdvancedDataClearResult> ClearSourceMetadataAsync(CancellationToken cancellationToken) => (advancedData ?? throw new InvalidOperationException("Advanced data controls are not configured.")).ClearSourceMetadataAsync(cancellationToken);
    public Task<AdvancedDataClearResult> ClearUpdateHistoryAsync(CancellationToken cancellationToken) => (advancedData ?? throw new InvalidOperationException("Advanced data controls are not configured.")).ClearUpdateHistoryAsync(cancellationToken);
    public Task<AdvancedDataClearResult> ClearMappingsAndPoliciesAsync(CancellationToken cancellationToken) => (advancedData ?? throw new InvalidOperationException("Advanced data controls are not configured.")).ClearMappingsAndPoliciesAsync(cancellationToken);

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
        TrustedSourceConnectorConsent.Normalize(states);
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

    public async Task SaveConnectorStatesAsync(IReadOnlyDictionary<string, ConnectorState> states, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach ((string connectorName, ConnectorState state) in states)
        {
            if (RequiresSourceDisclosure(connectorName) && state.Enabled && !state.SourceDisclosureApproved)
            {
                throw new InvalidOperationException("This connector requires explicit source-disclosure approval.");
            }
        }

        await connectorSettings.SaveConnectorStatesAsync(states, cancellationToken).ConfigureAwait(false);
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
        ConnectorState inventoryState = await connectorSettings.GetConnectorStateAsync(TrustedSourceConnectorConsent.InstalledInventoryConnector, false, cancellationToken).ConfigureAwait(false);
        ConnectorState sourceState = await connectorSettings.GetConnectorStateAsync(TrustedSourceConnectorConsent.PackageSourcesConnector, false, cancellationToken).ConfigureAwait(false);
        if (!TrustedSourceConnectorConsent.IsEnabled(inventoryState, sourceState)) return new(null, "Approved update sources are not enabled.");

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
        if (!executionCapabilities.MachineScopeEnabled &&
            TryScope(profile.Scope, out UpdateScope deploymentScope) &&
            deploymentScope == UpdateScope.Machine)
        {
            return new(null, "This standalone PC Steward package supports user-scope updates only. Machine-scope updates require a separately reviewed protected installation and are not actionable here.");
        }
        StructuredUpdateCandidateEvidence? candidate = structuredUpdateCandidates is null
            ? null
            : await structuredUpdateCandidates.ResolveAsync(item.Mapping.SourceId, item.Mapping.PackageId, cancellationToken).ConfigureAwait(false);
        if (!IsExactStructuredCandidate(item.Mapping, profile, candidate))
        {
            return new(null, "The provider did not supply bounded structured candidate-version evidence for the exact source and package. Display results cannot authorize an update.");
        }
        if (!TryCreateSelection(profile, item.Mapping, candidate!.CandidateVersion, out UpdateJobSelection? selection, out string message)) return new(null, message);
        return new(selection, "Fresh exact source evidence is ready for your review.");
    }

    /// <summary>
    /// Performs a controlled full scan and requires two independent authorities: exact source/ID/version
    /// from bounded WinGet export JSON, and exact version/scope/architecture from the reviewed structured
    /// registry or MSIX product. Display-table inventory is never post-update success authority.
    /// </summary>
    public async Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Selection.Operation == PackageOperationKind.Install) return await VerifyInstallAsync(job, cancellationToken).ConfigureAwait(false);
        if (postUpdatePackageInventory is null || !await postUpdatePackageInventory.HasExactExpectedVersionAsync(job, cancellationToken).ConfigureAwait(false)) return new(false, false);
        ScanResult scan = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
        if (!scan.Succeeded) return new(false, false);
        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        CanonicalProduct[] selectedProducts = products.Where(product => string.Equals(product.StableKey, job.Selection.StableProductKey, StringComparison.Ordinal)).ToArray();
        HashSet<Guid> productIds = selectedProducts.Select(product => product.Id).ToHashSet();
        InstallationInstance[] exactLocal = instances.Where(instance =>
            productIds.Contains(instance.ProductId) &&
            IsConcreteLocalEvidence(instance) &&
            string.Equals(instance.Version, job.Selection.CandidateVersion, StringComparison.Ordinal) &&
            string.Equals(instance.Scope, job.Selection.Scope.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(instance.Architecture, job.Selection.Architecture.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
        bool expectedVersionDetected = exactLocal.Length == 1 && selectedProducts.Length == 1;
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

    public async Task<RepositoryIdentityConfirmationResult> ConfirmCurrentRepositoryIdentitiesAsync(IEnumerable<RepositoryIdentityConfirmation> confirmations, bool sourcesEnabled, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(confirmations);
        RepositoryIdentityConfirmation[] requested = confirmations
            .Where(confirmation => !string.IsNullOrWhiteSpace(confirmation.StableProductKey) && !string.IsNullOrWhiteSpace(confirmation.SourceId) && !string.IsNullOrWhiteSpace(confirmation.PackageId))
            .DistinctBy(confirmation => (confirmation.StableProductKey, confirmation.SourceId, confirmation.PackageId))
            .ToArray();
        if (!sourcesEnabled) return new(0, requested.Length, "Enable an approved source and run a source check before confirming package identities.");
        SourceSnapshot[] snapshots = await ReadSourceSnapshotsAsync(true, false, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = await this.snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await this.snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, ProductUpdatePolicy> confirmationPolicies = decisions is null ? new Dictionary<string, ProductUpdatePolicy>() : await decisions.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<DeclaredPackageMapping> mappings = (await DeclaredMappingsAsync(products, instances, confirmationPolicies, cancellationToken).ConfigureAwait(false)).Mappings;
        UpdateIntelligenceSummary intelligence = UpdateIntelligence.Assess(CreateProfiles(products, instances), snapshots, mappings);
        Dictionary<string, UpdateIntelligenceItem> intelligenceByProduct = intelligence.Items.ToDictionary(item => item.StableProductKey, StringComparer.OrdinalIgnoreCase);
        int confirmed = 0;
        int skipped = 0;
        foreach (RepositoryIdentityConfirmation confirmation in requested)
        {
            SourcePackageRecord? package = snapshots
                .Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached)
                .Where(snapshot => string.Equals(snapshot.SourceId, confirmation.SourceId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(snapshot => snapshot.Packages)
                .FirstOrDefault(candidate => string.Equals(candidate.PackageId, confirmation.PackageId, StringComparison.OrdinalIgnoreCase));
            if (package?.InstalledVersion is null || package.AvailableVersion is null || !string.Equals(package.InstalledVersion, package.AvailableVersion, StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (!intelligenceByProduct.TryGetValue(confirmation.StableProductKey, out UpdateIntelligenceItem? item) || HasConflictingSources(item.SourceCandidates))
            {
                skipped++;
                continue;
            }

            ManualMappingValidation validation = await RequiredDecisions().CreateManualMappingAsync(confirmation.StableProductKey, package, snapshots, reason, cancellationToken).ConfigureAwait(false);
            if (validation.IsValid) confirmed++; else skipped++;
        }

        return new(confirmed, skipped, confirmed == 0
            ? "No source-reported current package identities were confirmed. Updates were not started."
            : $"Confirmed {confirmed} current repository mapping{(confirmed == 1 ? string.Empty : "s")}. Updates were not started.{(skipped == 0 ? string.Empty : $" Skipped {skipped} item{(skipped == 1 ? string.Empty : "s")}." )}");
    }

    private static bool HasConflictingSources(IReadOnlyCollection<SourceCandidateObservation>? observations)
    {
        SourceCandidateObservation[] candidates = observations?.ToArray() ?? [];
        if (candidates.Select(candidate => candidate.SourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1) return false;
        return candidates.Select(candidate => new
        {
            candidate.CandidateVersion,
            candidate.Publisher,
            candidate.Scope,
            candidate.Architecture,
            candidate.Channel
        }).Distinct().Count() > 1;
    }

    private async Task<UpdateIntelligenceSummary> AssessUpdateIntelligenceAsync(bool sourcesEnabled, bool online, CancellationToken cancellationToken)
    {
        SourceSnapshot[] sourceSnapshots = await ReadSourceSnapshotsAsync(sourcesEnabled, online, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, ProductUpdatePolicy> policies = decisions is null ? new Dictionary<string, ProductUpdatePolicy>() : await decisions.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<DeclaredPackageMapping> mappings = (await DeclaredMappingsAsync(products, instances, policies, cancellationToken).ConfigureAwait(false)).Mappings;
        return UpdateIntelligence.Assess(CreateProfiles(products, instances), sourceSnapshots, mappings, policies);
    }

    /// <summary>
    /// FR-INS-05: install Success needs two independent authorities. The provider must list the exact package at
    /// the target version, and a controlled re-inventory must find a product that the catalog detects exactly as
    /// this entry with concrete local evidence (registry or MSIX). A portable install without local evidence
    /// therefore stays unverified rather than becoming Success.
    /// </summary>
    private async Task<PostUpdateInventoryResult> VerifyInstallAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        if (postUpdatePackageInventory is null || !await postUpdatePackageInventory.HasExactExpectedVersionAsync(job, cancellationToken).ConfigureAwait(false)) return new(false, false);
        ScanResult scan = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
        if (!scan.Succeeded) return new(false, false);
        (PersonalCatalog? composed, _) = await ComposeCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (composed is null || !composed.Entries.Any(entry => string.Equals(entry.Id, job.Selection.StableProductKey, StringComparison.Ordinal))) return new(false, false);
        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> exactKeys = new(PersonalCatalogMatching.Match(composed, products, instances)
            .Where(match => match.Detection.IsExact && match.Detection.EntryIds.Count == 1 && string.Equals(match.Detection.EntryIds[0], job.Selection.StableProductKey, StringComparison.Ordinal))
            .Select(match => match.StableProductKey), StringComparer.Ordinal);
        HashSet<Guid> productIds = [.. products.Where(product => exactKeys.Contains(product.StableKey)).Select(product => product.Id)];
        return new(instances.Any(instance => productIds.Contains(instance.ProductId) && IsConcreteLocalEvidence(instance)), false);
    }

    /// <summary>
    /// FR-INS-01/02/04/06: prepares an Install of a catalog entry that PC Steward does not detect on this PC.
    /// The catalog only names the query; the provider must resolve it live and exactly (AC-V3-02). The default
    /// provider is used unless the user chose another listed provider. Installed apps are never reinstalled here.
    /// </summary>
    public async Task<InstallSelectionPreparation> PrepareInstallSelectionAsync(string catalogEntryId, CatalogProviderKind? providerOverride, UpdateScope scope, UpdateArchitecture architecture, CancellationToken cancellationToken)
    {
        if (installPreflight is null) return new(null, "Install preflight is not available in this build.");
        if (string.IsNullOrWhiteSpace(catalogEntryId)) return new(null, "No catalog app was selected.");
        ConnectorState inventoryState = await connectorSettings.GetConnectorStateAsync(TrustedSourceConnectorConsent.InstalledInventoryConnector, false, cancellationToken).ConfigureAwait(false);
        ConnectorState sourceState = await connectorSettings.GetConnectorStateAsync(TrustedSourceConnectorConsent.PackageSourcesConnector, false, cancellationToken).ConfigureAwait(false);
        if (!TrustedSourceConnectorConsent.IsEnabled(inventoryState, sourceState)) return new(null, "Approved update sources are not enabled.");

        (PersonalCatalog? composed, _) = await ComposeCatalogAsync(cancellationToken).ConfigureAwait(false);
        CatalogEntry? entry = composed?.Entries.SingleOrDefault(candidate => string.Equals(candidate.Id, catalogEntryId, StringComparison.Ordinal));
        if (entry is null) return new(null, "The app is not in the validated catalog.");
        CatalogProviderIdentity? provider = providerOverride is null ? entry.DefaultProvider : entry.Providers.FirstOrDefault(candidate => candidate.Provider == providerOverride);
        if (provider is null) return new(null, "The catalog entry does not list that provider.");
        if (!provider.Scopes.Contains(scope == UpdateScope.Machine ? CatalogScope.Machine : CatalogScope.User)) return new(null, "The catalog entry does not allow that install scope for this provider.");
        if (scope == UpdateScope.Machine && !executionCapabilities.MachineScopeEnabled) return new(null, "Machine-scope installs are not available in this deployment.");

        if (hasCurrentLocalInventoryProviders)
        {
            ScanResult currentInventory = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
            if (!currentInventory.Succeeded) return new(null, "Current installed inventory is unavailable. No install can be prepared.");
        }

        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        // Any detection, even a weak one, blocks an install: the app may already be here (FR-INS-06).
        if (PersonalCatalogMatching.Match(composed!, products, instances).Any(match => match.Detection.EntryIds.Contains(entry.Id, StringComparer.Ordinal)))
        {
            return new(null, "PC Steward detected this app on the PC. Installed apps are updated, not reinstalled.");
        }

        bool chocolatey = provider.Provider == CatalogProviderKind.Chocolatey;
        InstallPreflightRequest request = new(
            entry.Id,
            chocolatey ? UpdateProvider.Chocolatey : UpdateProvider.WinGet,
            provider.Source == CatalogSource.ChocolateyCommunity ? UpdateSource.ChocolateyCommunity : UpdateSource.WinGet,
            provider.PackageId,
            scope,
            chocolatey ? UpdateArchitecture.Neutral : architecture,
            entry.Channel == CatalogChannel.Preview ? UpdateChannel.Preview : UpdateChannel.Stable);
        InstallPreflightResult result = await installPreflight.PreflightAsync(request, cancellationToken).ConfigureAwait(false);
        return result.IsActionable
            ? new(result, "Fresh exact provider evidence is ready for your review.")
            : new(result, $"The provider did not return one exact, compatible, installable package ({result.Reason}). Nothing can be installed.");
    }

    /// <summary>
    /// User-confirmed mappings first; then personal-catalog mappings (ADR-023) for products the user has not
    /// mapped. Catalog mappings are declared evidence only and still pass trusted-source validation.
    /// </summary>
    private async Task<(IReadOnlyCollection<DeclaredPackageMapping> Mappings, PersonalCatalog? Catalog, IReadOnlyList<CatalogProductMatch> Matches, IReadOnlyList<string> RejectedUserEntries)> DeclaredMappingsAsync(
        IReadOnlyCollection<CanonicalProduct> products,
        IReadOnlyCollection<InstallationInstance> instances,
        IReadOnlyDictionary<string, ProductUpdatePolicy> policies,
        CancellationToken cancellationToken)
    {
        DeclaredPackageMapping[] manual = decisions is null
            ? []
            : [.. (await decisions.GetManualMappingsAsync(cancellationToken).ConfigureAwait(false)).Select(mapping => new DeclaredPackageMapping(mapping.StableProductKey, mapping.SourceId, mapping.PackageId, MappingOrigin.UserConfirmed))];
        (PersonalCatalog? composed, IReadOnlyList<string> rejected) = await ComposeCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (composed is null) return (manual, null, [], rejected);
        HashSet<string> manuallyMapped = new(manual.Select(mapping => mapping.StableProductKey), StringComparer.Ordinal);
        IReadOnlyList<CatalogProductMatch> matches = PersonalCatalogMatching.Match(composed, products, instances, policies);
        DeclaredPackageMapping[] fromCatalog = [.. matches.Where(match => match.Mapping is not null && !manuallyMapped.Contains(match.StableProductKey)).Select(match => match.Mapping!)];
        return ([.. manual, .. fromCatalog], composed, matches, rejected);
    }

    private async Task<(PersonalCatalog? Catalog, IReadOnlyList<string> Rejected)> ComposeCatalogAsync(CancellationToken cancellationToken)
    {
        if (catalog is null) return (null, []);
        if (userCatalog is null) return (catalog, []);
        CatalogCompositionResult composition = CatalogValidator.Compose(catalog, await userCatalog.GetUserCatalogEntriesAsync(cancellationToken).ConfigureAwait(false));
        return (composition.Catalog, composition.RejectedUserEntries);
    }

    /// <summary>FR-CAT-04: every catalog entry with its state. Null when no valid catalog is configured.</summary>
    public async Task<CatalogOverview?> GetCatalogOverviewAsync(bool sourcesEnabled, CancellationToken cancellationToken)
    {
        if (catalog is null) return null;
        SourceSnapshot[] sourceSnapshots = await ReadSourceSnapshotsAsync(sourcesEnabled, false, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<InstallationInstance> instances = await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<string, ProductUpdatePolicy> policies = decisions is null ? new Dictionary<string, ProductUpdatePolicy>() : await decisions.GetPoliciesAsync(cancellationToken).ConfigureAwait(false);
        (IReadOnlyCollection<DeclaredPackageMapping> mappings, PersonalCatalog? composed, IReadOnlyList<CatalogProductMatch> matches, IReadOnlyList<string> rejected) =
            await DeclaredMappingsAsync(products, instances, policies, cancellationToken).ConfigureAwait(false);
        UpdateIntelligenceSummary intelligence = UpdateIntelligence.Assess(CreateProfiles(products, instances), sourceSnapshots, mappings, policies);
        return new(composed!.CatalogVersion, PersonalCatalogMatching.Status(composed, matches, intelligence), rejected);
    }

    /// <summary>
    /// FR-CAT-06 storage: adds a reviewed user catalog entry after the same validation used on every read.
    /// Live exact resolution happens at install review (Task 48), never here.
    /// </summary>
    public async Task<(bool Added, string Message)> AddUserCatalogEntryAsync(UserCatalogEntryRecord entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (catalog is null || userCatalog is null) return (false, "The personal catalog is not available.");
        IReadOnlyCollection<UserCatalogEntryRecord> existing = await userCatalog.GetUserCatalogEntriesAsync(cancellationToken).ConfigureAwait(false);
        CatalogCompositionResult before = CatalogValidator.Compose(catalog, existing);
        CatalogCompositionResult after = CatalogValidator.Compose(catalog, [.. existing, entry]);
        if (after.Catalog.Entries.Count != before.Catalog.Entries.Count + 1) return (false, "The entry was rejected: " + string.Join(", ", after.RejectedUserEntries.Except(before.RejectedUserEntries, StringComparer.Ordinal)));
        await userCatalog.SaveUserCatalogEntryAsync(entry, DateTimeOffset.UtcNow, cancellationToken).ConfigureAwait(false);
        return (true, "Added to your catalog. Nothing was installed.");
    }

    public Task<bool> RemoveUserCatalogEntryAsync(string entryId, CancellationToken cancellationToken) =>
        (userCatalog ?? throw new InvalidOperationException("The personal catalog is not configured.")).DeleteUserCatalogEntryAsync(entryId, cancellationToken);

    public Task SkipVersionAsync(string stableProductKey, string version, string? reason, CancellationToken cancellationToken) => RequiredDecisions().SkipVersionAsync(stableProductKey, version, reason, cancellationToken);
    public Task SetPreferredProviderAsync(string stableProductKey, string provider, string? reason, CancellationToken cancellationToken) => RequiredDecisions().SetPreferredProviderAsync(stableProductKey, provider, reason, cancellationToken);
    public Task EnableAutomaticUpdatesAsync(string stableProductKey, string? reason, CancellationToken cancellationToken) => RequiredDecisions().EnableAutomaticUpdatesAsync(stableProductKey, reason, cancellationToken);
    public Task GrantCloseAppConsentAsync(string stableProductKey, string? reason, CancellationToken cancellationToken) => RequiredDecisions().GrantCloseAppConsentAsync(stableProductKey, reason, cancellationToken);

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

    private static bool IsExactStructuredCandidate(PackageMapping mapping, ProductMatchProfile profile, StructuredUpdateCandidateEvidence? candidate)
    {
        const int maximumVersionLength = 128;
        if (candidate is null ||
            !string.Equals(candidate.SourceId, mapping.SourceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.PackageId, mapping.PackageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.InstalledVersion, profile.InstalledVersion, StringComparison.Ordinal) ||
            !string.Equals(candidate.CandidateVersion, mapping.CandidateVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(candidate.InstalledVersion) ||
            candidate.InstalledVersion.Length > maximumVersionLength ||
            string.IsNullOrWhiteSpace(candidate.CandidateVersion) ||
            candidate.CandidateVersion.Length > maximumVersionLength)
        {
            return false;
        }

        return IsBoundedVersion(candidate.InstalledVersion) && IsBoundedVersion(candidate.CandidateVersion);
    }

    private static bool IsBoundedVersion(string version) => version.All(character =>
        char.IsLetterOrDigit(character) || character is '.' or '-' or '+' or '_');

    private static bool TryCreateSelection(ProductMatchProfile profile, PackageMapping mapping, string candidateVersion, out UpdateJobSelection? selection, out string message)
    {
        selection = null;
        if (profile.InstalledVersion is null) { message = "The installed version is missing."; return false; }
        VersionComparisonResult comparison = VersionComparison.Compare(new(profile.InstalledVersion, candidateVersion, false));
        if (comparison.Outcome != VersionComparisonOutcome.Newer) { message = "The structured candidate version is not a supported newer version."; return false; }
        if (!TrySource(mapping.SourceId, out UpdateSource source)) { message = "The selected source is not actionable."; return false; }
        if (!TryScope(profile.Scope, out UpdateScope scope)) { message = "The installed scope is not supported for a safe update."; return false; }
        if (!TryArchitecture(profile.Architecture, out UpdateArchitecture architecture)) { message = "The installed architecture is not supported for a safe update."; return false; }
        UpdateChannel channel = string.Equals(profile.Channel, "preview", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Preview : string.Equals(profile.Channel, "stable", StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Stable : UpdateChannel.Unknown;
        try
        {
            UpdateProvider provider = source == UpdateSource.ChocolateyCommunity ? UpdateProvider.Chocolatey : UpdateProvider.WinGet;
            selection = new UpdateJobSelection(profile.StableProductKey, provider, source, mapping.PackageId, profile.InstalledVersion, candidateVersion, scope, architecture, channel, provider == UpdateProvider.Chocolatey ? "chocolatey-fixed-community-v1" : "winget-com-v1");
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
        if (string.Equals(sourceId, "chocolatey-community", StringComparison.OrdinalIgnoreCase)) { source = UpdateSource.ChocolateyCommunity; return true; }
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
    private static bool RequiresSourceDisclosure(string connectorName) => connectorName is TrustedSourceConnectorConsent.InstalledInventoryConnector or TrustedSourceConnectorConsent.PackageSourcesConnector;
    private UserDecisionService RequiredDecisions() => decisions ?? throw new InvalidOperationException("Stable user decisions are not configured for this workflow.");
    public void Dispose() => orchestrator.Dispose();
}
