using System.Security.Cryptography;
using System.Text;

namespace PCSteward.Domain;

public sealed record RawInstallEvidence(string Provider, string ProviderIdentity, string DisplayName, string? Publisher, string? Version, string Scope, string Architecture, string? Channel, string Classification, string RawRecordId, string? SourceId = null);
public sealed record NormalizedInstall(string ProductFingerprint, string InstanceFingerprint, RawInstallEvidence Evidence, string Explanation);
public sealed record DeduplicationResult(IReadOnlyCollection<NormalizedInstall> Instances, IReadOnlyDictionary<string, IReadOnlyCollection<string>> RawRecordProvenance);

public static class InventoryNormalization
{
    public static DeduplicationResult NormalizeAndDeduplicate(IEnumerable<RawInstallEvidence> records)
    {
        Dictionary<string, List<RawInstallEvidence>> groups = [];
        foreach (RawInstallEvidence evidence in records)
        {
            string instance = InstanceFingerprint(evidence);
            if (!groups.TryGetValue(instance, out List<RawInstallEvidence>? group)) { group = []; groups.Add(instance, group); }
            group.Add(evidence);
        }
        List<NormalizedInstall> instances = []; Dictionary<string, IReadOnlyCollection<string>> provenance = [];
        foreach ((string fingerprint, List<RawInstallEvidence> group) in groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            RawInstallEvidence representative = group.OrderBy(evidence => evidence.RawRecordId, StringComparer.Ordinal).First();
            instances.Add(new NormalizedInstall(ProductFingerprint(representative), fingerprint, representative, group.Count == 1 ? "Unique provider identity." : "Equivalent provider identity, scope, architecture, channel and version."));
            provenance.Add(fingerprint, group.Select(evidence => evidence.RawRecordId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        }
        return new DeduplicationResult(instances, provenance);
    }
    public static string ProductFingerprint(RawInstallEvidence evidence) => string.IsNullOrWhiteSpace(evidence.SourceId)
        ? Hash(evidence.Provider, evidence.ProviderIdentity)
        : Hash(evidence.Provider, evidence.ProviderIdentity, evidence.SourceId);
    public static string InstanceFingerprint(RawInstallEvidence evidence) => string.IsNullOrWhiteSpace(evidence.SourceId)
        ? Hash(evidence.Provider, evidence.ProviderIdentity, evidence.Scope, evidence.Architecture, evidence.Channel ?? "", evidence.Version ?? "")
        : Hash(evidence.Provider, evidence.ProviderIdentity, evidence.SourceId, evidence.Scope, evidence.Architecture, evidence.Channel ?? "", evidence.Version ?? "");
    private static string Hash(params string[] values)
    {
        string[] normalized = values.Select(value => value.Trim().ToUpperInvariant()).ToArray();
        string encoded = normalized.Any(value => value.Contains('\u001F'))
            ? "PCSTEWARD-FINGERPRINT-V2\0" + string.Concat(normalized.Select(value => $"{Encoding.UTF8.GetByteCount(value)}:{value}"))
            : string.Join("\u001F", normalized);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(encoded)))[..24];
    }
}
