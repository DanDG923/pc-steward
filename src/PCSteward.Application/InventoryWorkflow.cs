using PCSteward.Domain;

namespace PCSteward.Application;

public sealed record InventoryDashboard(IReadOnlyCollection<CanonicalProduct> Products, IReadOnlyCollection<InstallationInstance> Instances, DateTimeOffset? LastCompletedAt, ScanDataSummary ScanData, IReadOnlyDictionary<string, ConnectorState> Connectors);
public sealed record RepositoryIdentityConfirmation(string StableProductKey, string SourceId, string PackageId);
public sealed record RepositoryIdentityConfirmationResult(int Confirmed, int Skipped, string Message);
public interface IPostUpdatePackageInventory
{
    Task<bool> HasExactExpectedVersionAsync(ImmutableUpdateJob job, CancellationToken cancellationToken);

    /// <summary>
    /// The product key of the Windows uninstall entry the provider itself links to the updated package now, when an update replaced
    /// the entry (an MSI upgrade gets a new product code); null when there is no single such link.
    /// </summary>
    Task<string?> GetLinkedProductKeyAfterUpdateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}

/// <summary>Normalized bounded result returned only by a provider-owned protected machine operation.</summary>
public sealed record ProviderProcessResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut,
    bool OutputLimitExceeded = false, bool Cancelled = false, bool OutputIncomplete = false, bool ExecutionUncertain = false);
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

    /// <summary>The one linked key the inventories name; none, or two different keys, give null.</summary>
    public async Task<string?> GetLinkedProductKeyAfterUpdateAsync(ImmutableUpdateJob job, CancellationToken cancellationToken)
    {
        string? linked = null;
        foreach (IPostUpdatePackageInventory inventory in inventories)
        {
            string? key = await inventory.GetLinkedProductKeyAfterUpdateAsync(job, cancellationToken).ConfigureAwait(false);
            if (key is null) continue;
            if (linked is not null && !string.Equals(linked, key, StringComparison.Ordinal)) return null;
            linked = key;
        }
        return linked;
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
    /// <summary>The design note's refusal when a 32-bit-registered app's architecture can't be proven at review (§3.3).</summary>
    public const string ArchitectureNotConfirmedMessage = "PC Steward couldn't confirm this app's architecture, so it can't be updated here. Nothing was started.";
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
        UpdateExecutionCapabilities? executionCapabilities = null)
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

    public Task<UpdateIntelligenceSummary> LoadUpdateIntelligenceAsync(bool sourcesEnabled, CancellationToken cancellationToken) => AssessUpdateIntelligenceAsync(sourcesEnabled, false, null, cancellationToken);
    public Task<UpdateIntelligenceSummary> RefreshUpdateIntelligenceAsync(bool sourcesEnabled, CancellationToken cancellationToken) => AssessUpdateIntelligenceAsync(sourcesEnabled, true, null, cancellationToken);

    /// <summary>Prepares only a fresh, persisted-consent source match for explicit update review.</summary>
    public async Task<UpdateSelectionPreparation> PrepareApprovedUpdateSelectionAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stableProductKey)) return new(null, "No installed product was selected.");
        (FreshSelectionEvidence? evidence, string failure) = await ObserveFreshSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
        return evidence is null ? new(null, failure) : await PrepareAgainstFreshEvidenceAsync(stableProductKey, evidence, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// SPEC §9.13 G4, FR-UPD-10 (Task 73 Phase 6 review M2): prepares a frozen set for one batch review from ONE fresh inventory
    /// observation and one source assessment taken now, instead of a full scan per app. Every key goes through exactly the same
    /// per-app validation as <see cref="PrepareApprovedUpdateSelectionAsync"/> against that shared observation. It is review
    /// evidence only: each app is still prepared again, on its own and freshly, immediately before its own start.
    /// <paramref name="progress"/> receives how many apps have been prepared. One result per key, in order.
    /// </summary>
    public async Task<IReadOnlyList<UpdateSelectionPreparation>> PrepareApprovedUpdateSelectionsAsync(IReadOnlyList<string> stableProductKeys, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stableProductKeys);
        (FreshSelectionEvidence? evidence, string failure) = await ObserveFreshSelectionEvidenceAsync(cancellationToken).ConfigureAwait(false);
        UpdateSelectionPreparation[] prepared = new UpdateSelectionPreparation[stableProductKeys.Count];
        for (int index = 0; index < stableProductKeys.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = stableProductKeys[index];
            if (string.IsNullOrWhiteSpace(key)) prepared[index] = new(null, "No installed product was selected.");
            else if (evidence is null) prepared[index] = new(null, failure);
            else
            {
                try { prepared[index] = await PrepareAgainstFreshEvidenceAsync(key, evidence, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { prepared[index] = new(null, ReviewedUpdateExecutionWorkflow.CouldNotPrepareMessage); }
            }
            progress?.Report(index + 1);
        }
        return prepared;
    }

    /// <summary>The fresh inventory observation, source assessment and match profiles that per-app preparation validates against.</summary>
    private sealed record FreshSelectionEvidence(LiveInventoryObservation? Observation, UpdateIntelligenceSummary Summary, ProductMatchProfile[] Profiles);

    private async Task<(FreshSelectionEvidence? Evidence, string Failure)> ObserveFreshSelectionEvidenceAsync(CancellationToken cancellationToken)
    {
        ConnectorState inventoryState = await connectorSettings.GetConnectorStateAsync(TrustedSourceConnectorConsent.InstalledInventoryConnector, false, cancellationToken).ConfigureAwait(false);
        ConnectorState sourceState = await connectorSettings.GetConnectorStateAsync(TrustedSourceConnectorConsent.PackageSourcesConnector, false, cancellationToken).ConfigureAwait(false);
        if (!TrustedSourceConnectorConsent.IsEnabled(inventoryState, sourceState)) return (null, "Approved update sources are not enabled.");

        // A persisted snapshot is never confirmation authority.  Refresh local installed evidence
        // immediately before both review and execution; an unavailable or conflicting provider scan
        // fails closed before a job or provider operation can be constructed.
        LiveInventoryObservation? observation = null;
        if (hasCurrentLocalInventoryProviders)
        {
            ScanResult currentInventory = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
            if (!currentInventory.Succeeded) return (null, "Current installed inventory is unavailable. No update can be started.");
            observation = currentInventory.Observation;
            if (observation is null || observation.CorrelationId != currentInventory.CorrelationId)
                return (null, "Current native inventory observation is unavailable. No update can be started.");
        }

        UpdateIntelligenceSummary summary = await AssessUpdateIntelligenceAsync(true, true, observation, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = observation is null ? await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false) : observation.Products;
        IReadOnlyCollection<InstallationInstance> instances = observation is null ? await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false) : observation.Instances;
        return (new(observation, summary, CreateProfiles(products, instances)), string.Empty);
    }

    private async Task<UpdateSelectionPreparation> PrepareAgainstFreshEvidenceAsync(string stableProductKey, FreshSelectionEvidence evidence, CancellationToken cancellationToken)
    {
        UpdateIntelligenceSummary summary = evidence.Summary;
        UpdateIntelligenceItem? item = summary.Items.SingleOrDefault(candidate => string.Equals(candidate.StableProductKey, stableProductKey, StringComparison.Ordinal));
        if (item is not { Status: UpdateIntelligenceStatus.VerifiedUpdate, Mapping.IsActionable: true, Mapping.CandidateVersion: not null, SourceId: not null })
        {
            return new(null, item?.Explanation ?? "The selected product is no longer present in current inventory.");
        }

        SourceSnapshot? source = summary.Sources.SingleOrDefault(snapshot => string.Equals(snapshot.SourceId, item.SourceId, StringComparison.OrdinalIgnoreCase));
        if (source?.State != SourceSnapshotState.Live) return new(null, "The approved source did not provide a fresh live result. No update can be started.");

        ProductMatchProfile? profile = evidence.Profiles.SingleOrDefault(product => string.Equals(product.StableProductKey, stableProductKey, StringComparison.Ordinal));
        if (profile is null) return new(null, "The selected product is no longer present in current inventory.");
        if (!executionCapabilities.MachineScopeEnabled &&
            TryScope(profile.Scope, out UpdateScope deploymentScope) &&
            deploymentScope == UpdateScope.Machine)
        {
            return new(null, "This standalone PC Steward package supports user-scope updates only. Machine-scope updates require a separately reviewed protected installation and are not actionable here.");
        }
        // Design note §3.2–3.3: an app whose uninstall entry is in the 32-bit view and that WinGet links to an x64 package by product
        // code is prepared as x64, with what the review must prove: WinGet's installed package version and the entry's key and version.
        bool provenAtReview = item.Mapping.ReviewReasons.Contains(MappingReviewReason.ArchitectureCheckedAtReview);
        string? registrationKey = null, registrationVersion = null;
        if (provenAtReview && (!profile.RegisteredIn32BitView || !TryMachine32Registration(evidence.Observation, stableProductKey, out registrationKey, out registrationVersion)))
        {
            return new(null, ArchitectureNotConfirmedMessage);
        }
        StructuredUpdateCandidateEvidence? candidate = structuredUpdateCandidates is null
            ? null
            : await structuredUpdateCandidates.ResolveAsync(item.Mapping.SourceId, item.Mapping.PackageId, cancellationToken).ConfigureAwait(false);
        if (!IsExactStructuredCandidate(item.Mapping, profile, candidate, installedVersionProvenAtReview: provenAtReview))
        {
            return new(null, "The provider did not supply bounded structured candidate-version evidence for the exact source and package. Display results cannot authorize an update.");
        }
        ProductMatchProfile bound = provenAtReview ? profile with { Architecture = "x64" } : profile;
        if (!TryCreateSelection(bound, item.Mapping, candidate!.CandidateVersion, out UpdateJobSelection? selection, out string message)) return new(null, message);
        if (provenAtReview)
        {
            return new(selection, "Fresh exact source evidence is ready for your review. The app's architecture is checked next.", evidence.Observation,
                new WingetArchitectureProofRequest(candidate.InstalledVersion, registrationKey!, registrationVersion!));
        }
        return new(selection, "Fresh exact source evidence is ready for your review.", evidence.Observation);
    }

    /// <summary>
    /// Performs a controlled full scan and requires two independent authorities: exact source/ID/version
    /// from bounded WinGet export JSON, and exact version/scope/architecture from the reviewed structured
    /// registry or MSIX product. Display-table inventory is never post-update success authority.
    /// <see cref="PostUpdateInventoryResult.RestartPending"/> is always false here by design (Task 51): machine-wide restart indicators
    /// are not bound to this job, so the restart evidence comes only from the provider's typed result
    /// (<see cref="UpdateProviderAttemptStatus.RestartReported"/>), which the batch coordinator records as Restart required.
    /// </summary>
    public Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, CancellationToken cancellationToken) =>
        ReInventoryAsync(job, null, cancellationToken);

    /// <summary>
    /// The post-update check. With <paramref name="expected"/> (design note §3.4, an app whose x64 architecture the review proved), the
    /// one machine-scope uninstall entry must be at the expected key, or belong to the reviewed or WinGet-linked product, and show
    /// exactly the expected DisplayVersion; its registry view is not compared, because the review's manifest proof covers it.
    /// Otherwise the target version, scope and architecture must match exactly, as before. Either way WinGet's export must show
    /// exactly the target package version.
    /// </summary>
    public async Task<PostUpdateInventoryResult> ReInventoryAsync(ImmutableUpdateJob job, ExpectedUninstallEntry? expected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (postUpdatePackageInventory is null || !await postUpdatePackageInventory.HasExactExpectedVersionAsync(job, cancellationToken).ConfigureAwait(false)) return new(false, false);
        ScanResult scan = await ScanAsync(null, cancellationToken).ConfigureAwait(false);
        if (!scan.Succeeded) return new(false, false);
        LiveInventoryObservation? observation = hasCurrentLocalInventoryProviders ? scan.Observation : null;
        if (hasCurrentLocalInventoryProviders && (observation is null || observation.CorrelationId != scan.CorrelationId)) return new(false, false);
        IReadOnlyCollection<CanonicalProduct> products = observation is null ? await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false) : observation.Products;
        IReadOnlyCollection<InstallationInstance> instances = observation is null ? await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false) : observation.Instances;
        // An MSI upgrade can replace the reviewed uninstall entry with a new one (new product code, so a new product key). The
        // provider's own link after the update names it; the old product then has no instance at the target version, and the
        // checks below still need exactly one product and exactly one exact instance across both.
        string? linkedKey = await postUpdatePackageInventory.GetLinkedProductKeyAfterUpdateAsync(job, cancellationToken).ConfigureAwait(false);
        CanonicalProduct[] reviewedOrLinked = products.Where(product =>
            string.Equals(product.StableKey, job.Selection.StableProductKey, StringComparison.Ordinal) ||
            (linkedKey is not null && string.Equals(product.StableKey, linkedKey, StringComparison.Ordinal))).ToArray();
        HashSet<Guid> productIds = reviewedOrLinked.Select(product => product.Id).ToHashSet();
        InstallationInstance[] exactLocal;
        if (expected is not null)
        {
            // A runtime patch registers under the new bundle's product code: that entry's product counts too (design note §3.4).
            productIds.UnionWith(instances.Where(instance => IsRegistryEntry(instance, expected.ProductCode)).Select(instance => instance.ProductId));
            exactLocal = instances.Where(instance =>
                productIds.Contains(instance.ProductId) &&
                string.Equals(instance.Provider, "registry", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(instance.Version, expected.DisplayVersion, StringComparison.Ordinal) &&
                string.Equals(instance.Scope, nameof(UpdateScope.Machine), StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            exactLocal = instances.Where(instance =>
                productIds.Contains(instance.ProductId) &&
                IsConcreteLocalEvidence(instance) &&
                string.Equals(instance.Version, job.Selection.CandidateVersion, StringComparison.Ordinal) &&
                string.Equals(instance.Scope, job.Selection.Scope.ToString(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(instance.Architecture, job.Selection.Architecture.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        bool expectedVersionDetected = exactLocal.Length == 1 &&
            !productIds.Any(id => id != exactLocal[0].ProductId && instances.Any(instance => instance.ProductId == id && IsConcreteLocalEvidence(instance)));
        return new(expectedVersionDetected, false);
    }

    private static bool IsRegistryEntry(InstallationInstance instance, string key) =>
        string.Equals(instance.Provider, "registry", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(instance.ProviderIdentity?.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase);

    public Task PinCurrentVersionAsync(string stableProductKey, string currentVersion, string? reason, CancellationToken cancellationToken) => RequiredDecisions().PinCurrentVersionAsync(stableProductKey, currentVersion, reason, cancellationToken);
    public Task PinSourcePackageAsync(string stableProductKey, string sourceId, string packageId, string? reason, CancellationToken cancellationToken) => RequiredDecisions().PinSourcePackageAsync(stableProductKey, sourceId, packageId, reason, cancellationToken);
    public Task IgnoreAsync(string stableProductKey, DateTimeOffset? until, string? reason, CancellationToken cancellationToken) => RequiredDecisions().IgnoreAsync(stableProductKey, until, reason, cancellationToken);
    public Task SkipVersionAsync(string stableProductKey, string version, string? reason, CancellationToken cancellationToken) => RequiredDecisions().SkipVersionAsync(stableProductKey, version, reason, cancellationToken);
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
        IReadOnlyCollection<DeclaredPackageMapping> mappings = decisions is null
            ? []
            : (await decisions.GetManualMappingsAsync(cancellationToken).ConfigureAwait(false))
                .Select(mapping => new DeclaredPackageMapping(mapping.StableProductKey, mapping.SourceId, mapping.PackageId, MappingOrigin.UserConfirmed))
                .ToArray();
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

    private async Task<UpdateIntelligenceSummary> AssessUpdateIntelligenceAsync(bool sourcesEnabled, bool online, LiveInventoryObservation? observation, CancellationToken cancellationToken)
    {
        SourceSnapshot[] sourceSnapshots = await ReadSourceSnapshotsAsync(sourcesEnabled, online, cancellationToken).ConfigureAwait(false);
        IReadOnlyCollection<CanonicalProduct> products = observation is null ? await snapshots.GetLastKnownGoodProductsAsync(cancellationToken).ConfigureAwait(false) : observation.Products;
        IReadOnlyCollection<InstallationInstance> instances = observation is null ? await snapshots.GetLastKnownGoodInstancesAsync(cancellationToken).ConfigureAwait(false) : observation.Instances;
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
            // Without a WinGet instance (an app matched through a provider key or its name and publisher), one product key can still
            // cover the same uninstall key name in several registry views; differing installed targets are ambiguous, never picked.
            bool conflictingTargets = wingetInstances.Select(instance => (instance.ProviderIdentity, instance.SourceId, instance.Version, instance.Scope, instance.Architecture, instance.Channel)).Distinct().Skip(1).Any() ||
                (winget is null && productInstances.Where(IsConcreteLocalEvidence).Select(instance => (Scope: instance.Scope.ToUpperInvariant(), Architecture: instance.Architecture.ToUpperInvariant(), instance.Version)).Distinct().Skip(1).Any());
            return new ProductMatchProfile(product.StableKey, product.DisplayName, winget?.ProviderIdentity, msix is null ? null : product.StableKey, null, preferred?.Publisher, winget?.Version ?? executionEvidence?.Version, executionEvidence?.Scope ?? "Unknown", executionEvidence?.Architecture ?? "Unknown", preferred?.Channel, InstalledSourceId: winget?.SourceId, HasConflictingTargetInstances: conflictingTargets, HasConcreteLocalEvidence: productInstances.Length == 0 || productInstances.Any(instance => !string.Equals(instance.Provider, "winget", StringComparison.OrdinalIgnoreCase)),
                RegisteredIn32BitView: winget is null && executionEvidence is not null && IsMachine32Registration(executionEvidence));
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

    // RegistryInventoryProvider records the 32-bit machine view, and only that view, as Machine + x86.
    private static bool IsMachine32Registration(InstallationInstance instance) =>
        string.Equals(instance.Provider, "registry", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(instance.Scope, "Machine", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(instance.Architecture, "x86", StringComparison.OrdinalIgnoreCase);

    private static bool IsConcreteLocalEvidence(InstallationInstance instance) => string.Equals(instance.Provider, "registry", StringComparison.OrdinalIgnoreCase) || string.Equals(instance.Provider, "msix", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one uninstall entry of <paramref name="stableProductKey"/>'s product in fresh inventory that was read from the 32-bit machine
    /// view (registry, Machine, x86): its key and DisplayVersion.
    /// </summary>
    private static bool TryMachine32Registration(LiveInventoryObservation? observation, string stableProductKey, out string? key, out string? version)
    {
        key = version = null;
        if (observation is null) return false;
        CanonicalProduct[] products = [.. observation.Products.Where(product => string.Equals(product.StableKey, stableProductKey, StringComparison.Ordinal))];
        if (products.Length != 1) return false;
        InstallationInstance[] registrations = [.. observation.Instances.Where(instance => instance.ProductId == products[0].Id && IsMachine32Registration(instance))];
        if (registrations.Length != 1 || string.IsNullOrWhiteSpace(registrations[0].ProviderIdentity) || string.IsNullOrWhiteSpace(registrations[0].Version)) return false;
        key = registrations[0].ProviderIdentity;
        version = registrations[0].Version;
        return true;
    }

    // With installedVersionProvenAtReview, WinGet's installed package version may differ from the uninstall entry's DisplayVersion
    // (8.0.33 against 8.0.33.36622): the review ties the two together from the installed version's own manifest (design note §3.3).
    private static bool IsExactStructuredCandidate(PackageMapping mapping, ProductMatchProfile profile, StructuredUpdateCandidateEvidence? candidate,
        bool installedVersionProvenAtReview = false)
    {
        const int maximumVersionLength = 128;
        if (candidate is null ||
            !string.Equals(candidate.SourceId, mapping.SourceId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.PackageId, mapping.PackageId, StringComparison.OrdinalIgnoreCase) ||
            (!installedVersionProvenAtReview && !string.Equals(candidate.InstalledVersion, profile.InstalledVersion, StringComparison.Ordinal)) ||
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
