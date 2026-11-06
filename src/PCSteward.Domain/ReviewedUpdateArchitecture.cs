namespace PCSteward.Domain;

public enum ReviewedArchitectureBinding { Observed, ProviderScriptSelected }

/// <summary>Frozen architecture data, not proof of observation or permission to relax a route constraint.</summary>
public sealed record ReviewedUpdateArchitecture
{
    private ReviewedUpdateArchitecture(ReviewedArchitectureBinding binding, UpdateArchitecture? value)
    {
        Binding = binding;
        Value = value;
    }
    public ReviewedArchitectureBinding Binding { get; }
    public UpdateArchitecture? Value { get; }
    public static ReviewedUpdateArchitecture Observed(UpdateArchitecture value) => Enum.IsDefined(value)
        ? new(ReviewedArchitectureBinding.Observed, value) : throw new ArgumentOutOfRangeException(nameof(value));
    public static ReviewedUpdateArchitecture ProviderScriptSelected() => new(ReviewedArchitectureBinding.ProviderScriptSelected, null);
}

/// <summary>Reviewed selection with explicit architecture semantics. No legacy Neutral payload label is exposed for script selection.</summary>
public sealed class ReviewedUpdateSelection
{
    private readonly UpdateJobSelection legacyDigestData;
    public ReviewedUpdateSelection(UpdateJobSelection legacy, ReviewedUpdateArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(architecture);
        if (architecture.Binding == ReviewedArchitectureBinding.Observed && architecture.Value != legacy.Architecture ||
            architecture.Binding == ReviewedArchitectureBinding.ProviderScriptSelected && legacy.Architecture != UpdateArchitecture.Neutral)
            throw new ArgumentException("Architecture binding and legacy comparison data disagree.", nameof(architecture));
        legacyDigestData = legacy;
        Architecture = architecture;
    }
    public ReviewedUpdateSelection(string stableProductKey, UpdateProvider provider, UpdateSource source, string packageId,
        string installedVersion, string candidateVersion, UpdateScope scope, ReviewedUpdateArchitecture architecture,
        UpdateChannel channel, string mappingEvidenceVersion)
        : this(new UpdateJobSelection(stableProductKey, provider, source, packageId, installedVersion, candidateVersion, scope,
            architecture?.Value ?? UpdateArchitecture.Neutral, channel, mappingEvidenceVersion),
            architecture ?? throw new ArgumentNullException(nameof(architecture))) { }

    public string StableProductKey => legacyDigestData.StableProductKey;
    public UpdateProvider Provider => legacyDigestData.Provider;
    public UpdateSource Source => legacyDigestData.Source;
    public string PackageId => legacyDigestData.PackageId;
    public string InstalledVersion => legacyDigestData.InstalledVersion;
    public string CandidateVersion => legacyDigestData.CandidateVersion;
    public UpdateScope Scope => legacyDigestData.Scope;
    public ReviewedUpdateArchitecture Architecture { get; }
    public UpdateChannel Channel => legacyDigestData.Channel;
    public string MappingEvidenceVersion => legacyDigestData.MappingEvidenceVersion;
    internal UpdateJobSelection LegacyDigestData => legacyDigestData;
    internal string CanonicalText => string.Join('\n', StableProductKey, Provider.ToString(), Source.ToString(), PackageId,
        InstalledVersion, CandidateVersion, Scope.ToString(), Architecture.Binding.ToString(), Architecture.Value?.ToString() ?? "None",
        Channel.ToString(), MappingEvidenceVersion);
    public bool EqualsEvidence(ReviewedUpdateSelection? other) => other is not null && CanonicalText == other.CanonicalText;
}
