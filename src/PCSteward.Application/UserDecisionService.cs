using System.Text.RegularExpressions;
using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>Applies durable user choices without weakening package matching or creating update execution authority.</summary>
public sealed partial class UserDecisionService
{
    private const int MaximumReasonLength = 512;
    internal const string SkipVersionKind = "SkipVersion";
    internal const string PreferredProviderKind = "PreferredProvider";
    internal const string AutomaticUpdatesKind = "AutomaticUpdates";
    internal const string CloseAppConsentKind = "CloseAppConsent";
    private static readonly string[] PreferredProviders = ["winget", "chocolatey"];
    private readonly IUserDecisionStore store;

    public UserDecisionService(IUserDecisionStore store) => this.store = store;

    public async Task<ManualMappingValidation> CreateManualMappingAsync(string stableProductKey, SourcePackageRecord package, IReadOnlyCollection<SourceSnapshot> snapshots, string? reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableProductKey); ArgumentNullException.ThrowIfNull(package); ArgumentNullException.ThrowIfNull(snapshots);
        bool supplied = snapshots.Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached).SelectMany(snapshot => snapshot.Packages).Any(candidate => Same(candidate.SourceId, package.SourceId) && Same(candidate.PackageId, package.PackageId));
        DeclaredPackageMapping declared = new(stableProductKey, package.SourceId, package.PackageId, MappingOrigin.UserConfirmed);
        PackageMatchCandidate candidate = new(package.SourceId, package.PackageId, package.DisplayName, null, null, package.Publisher, package.AvailableVersion, package.Scope, package.Architecture, package.Channel, true, package.ProviderSuppliedStableProductKey, package.RequiresConnectorSpecificVersionSemantics);
        ManualMappingValidation validation = supplied ? ProductPackageMatching.ValidateManualMapping(declared, candidate, snapshots.Where(snapshot => snapshot.State is SourceSnapshotState.Live or SourceSnapshotState.Cached).Select(snapshot => snapshot.SourceId)) : new(false, MappingReviewReason.UnsupportedManualMapping, "Manual mappings must select an exact package ID currently supplied by an enabled trusted connector.");
        if (!validation.IsValid) return validation;
        bool alreadyMapped = (await store.GetManualMappingsAsync(cancellationToken))
            .Any(mapping => Same(mapping.StableProductKey, stableProductKey) && Same(mapping.SourceId, package.SourceId) && Same(mapping.PackageId, package.PackageId));
        if (alreadyMapped) return validation;
        await store.SaveManualMappingAsync(new ManualMapping(Guid.NewGuid(), stableProductKey, package.SourceId, package.PackageId, DateTimeOffset.UtcNow, BoundedReason(reason)), cancellationToken);
        return validation;
    }

    public Task PinCurrentVersionAsync(string stableProductKey, string currentVersion, string? reason, CancellationToken cancellationToken) => SaveDecisionAsync(stableProductKey, "PinCurrentVersion", currentVersion, reason, null, cancellationToken);
    public Task PinSourcePackageAsync(string stableProductKey, string sourceId, string packageId, string? reason, CancellationToken cancellationToken) => SaveDecisionAsync(stableProductKey, "PinSourcePackage", $"{sourceId}:{packageId}", reason, null, cancellationToken);

    public Task IgnoreAsync(string stableProductKey, DateTimeOffset? until, string? reason, CancellationToken cancellationToken)
    {
        if (until is not null && until <= DateTimeOffset.UtcNow) throw new ArgumentOutOfRangeException(nameof(until), "An ignore-until date must be in the future.");
        return SaveDecisionAsync(stableProductKey, "Ignore", until is null ? "Indefinitely" : until.Value.ToString("O"), reason, until, cancellationToken);
    }

    /// <summary>FR-POL-06: hide one available version; a newer version is offered again.</summary>
    public Task SkipVersionAsync(string stableProductKey, string version, string? reason, CancellationToken cancellationToken)
    {
        if (!IsVersion(version)) throw new ArgumentException("The version is outside the allowlisted format.", nameof(version));
        return SaveDecisionAsync(stableProductKey, SkipVersionKind, version.Trim(), reason, null, cancellationToken);
    }

    /// <summary>FR-PRV-04 / ADR-024: preferred provider for catalog name-and-publisher matches and new installs.</summary>
    public Task SetPreferredProviderAsync(string stableProductKey, string provider, string? reason, CancellationToken cancellationToken)
    {
        if (!PreferredProviders.Contains(provider, StringComparer.Ordinal)) throw new ArgumentException("Only winget or chocolatey may be preferred.", nameof(provider));
        return SaveDecisionAsync(stableProductKey, PreferredProviderKind, provider, reason, null, cancellationToken);
    }

    /// <summary>FR-AUT-01: per-app opt-in. Turning it off reverses the decision. Eligibility is decided later (FR-AUT-02).</summary>
    public Task EnableAutomaticUpdatesAsync(string stableProductKey, string? reason, CancellationToken cancellationToken) =>
        SaveDecisionAsync(stableProductKey, AutomaticUpdatesKind, "On", reason, null, cancellationToken);

    /// <summary>FR-BAT-08: per-app consent to close the app gracefully before a manual update. Never used by automation.</summary>
    public Task GrantCloseAppConsentAsync(string stableProductKey, string? reason, CancellationToken cancellationToken) =>
        SaveDecisionAsync(stableProductKey, CloseAppConsentKind, "Graceful", reason, null, cancellationToken);

    public Task ReverseDecisionAsync(Guid decisionId, CancellationToken cancellationToken) => store.DeleteAsync(decisionId, cancellationToken);
    public Task DeleteManualMappingAsync(Guid mappingId, CancellationToken cancellationToken) => store.DeleteManualMappingAsync(mappingId, cancellationToken);
    public async Task<IReadOnlyCollection<StableDecision>> GetActiveDecisionsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return (await store.GetAllAsync(cancellationToken)).Where(decision => decision.Kind != "Ignore" || decision.ExpiresAt is null || decision.ExpiresAt > now).ToArray();
    }
    public Task<IReadOnlyCollection<ManualMapping>> GetManualMappingsAsync(CancellationToken cancellationToken) => store.GetManualMappingsAsync(cancellationToken);
    public async Task<IReadOnlyCollection<ProductTimelineEntry>> GetTimelineAsync(string stableProductKey, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ProductTimelineEntry> decisions = await store.GetTimelineAsync(stableProductKey, cancellationToken);
        IReadOnlyCollection<ProductTimelineEntry> mappings = (await store.GetManualMappingsAsync(cancellationToken))
            .Where(mapping => string.Equals(mapping.StableProductKey, stableProductKey, StringComparison.Ordinal))
            .Select(mapping => new ProductTimelineEntry(mapping.CreatedAt, "ManualMapping", $"Manual mapping: {mapping.SourceId}:{mapping.PackageId}", mapping.Reason))
            .ToArray();
        return decisions.Concat(mappings).OrderByDescending(entry => entry.OccurredAt).ToArray();
    }

    public async Task<IReadOnlyDictionary<string, ProductUpdatePolicy>> GetPoliciesAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow; Dictionary<string, ProductUpdatePolicy> policies = [];
        foreach (IGrouping<string, StableDecision> group in (await store.GetAllAsync(cancellationToken)).GroupBy(decision => decision.StableProductKey, StringComparer.Ordinal))
        {
            StableDecision? pin = group.Where(decision => decision.Kind is "PinCurrentVersion" or "PinSourcePackage").OrderByDescending(decision => decision.CreatedAt).FirstOrDefault();
            StableDecision? ignore = group.Where(decision => decision.Kind == "Ignore" && (decision.ExpiresAt is null || decision.ExpiresAt > now)).OrderByDescending(decision => decision.CreatedAt).FirstOrDefault();
            // Stored rows are untrusted: values outside the allowlist are ignored and can never widen behaviour.
            string? skipped = Latest(group, SkipVersionKind, IsVersion)?.Value.Trim();
            string? preferred = Latest(group, PreferredProviderKind, value => PreferredProviders.Contains(value, StringComparer.Ordinal))?.Value;
            // CRD-04: a curated Off marker dominates any legacy On for its exact key, regardless of timestamp or expiry.
            bool restricted = group.Any(decision => decision.Kind == CuratedAutomaticUpdateRestriction.Kind && decision.Value == CuratedAutomaticUpdateRestriction.Value);
            bool automatic = !restricted && Latest(group, AutomaticUpdatesKind, value => value == "On") is not null;
            bool closeConsent = Latest(group, CloseAppConsentKind, value => value == "Graceful") is not null;
            policies[group.Key] = new(pin is not null, ignore is not null, pin is null ? null : $"The product is pinned to {pin.Value} by a recorded user decision.", ignore?.ExpiresAt, skipped, preferred, automatic, closeConsent);
        }
        return policies;
    }

    private Task SaveDecisionAsync(string stableProductKey, string kind, string value, string? reason, DateTimeOffset? expiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableProductKey); ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return store.SaveAsync(new StableDecision(Guid.NewGuid(), stableProductKey, kind, value, DateTimeOffset.UtcNow, BoundedReason(reason), expiresAt), cancellationToken);
    }

    private static StableDecision? Latest(IEnumerable<StableDecision> decisions, string kind, Func<string, bool> valid) =>
        decisions.Where(decision => decision.Kind == kind && decision.Value is not null && valid(decision.Value)).OrderByDescending(decision => decision.CreatedAt).FirstOrDefault();

    private static bool IsVersion(string? value) => value is not null && VersionPattern().IsMatch(value.Trim());

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    private static string? BoundedReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, MaximumReasonLength)];
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
