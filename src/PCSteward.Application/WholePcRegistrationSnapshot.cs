using PCSteward.Domain;

namespace PCSteward.Application;

/// <summary>
/// D7 design §3 item 4 (owner answer Q3-A): the whole-PC uninstall entries (HKLM, both registry views) one live inventory observation
/// holds, by view and key name with their DisplayVersion. A per-user vendor-installer update takes one just before launch (the fresh
/// re-preparation's observation) and compares it with the post-update scan: an entry that is new, gone or at another version means
/// the update changed something for every user of the PC, and the result is never recorded as successful. Entries the inventory skips
/// (hidden system components) and changes outside uninstall entries are not seen.
/// </summary>
public sealed class WholePcRegistrationSnapshot
{
    /// <summary>At most this many changed app names are kept for the history line.</summary>
    public const int MaximumNamedChanges = 5;

    /// <summary>The name the uninstall-registry inventory provider reports its warnings under (Desktop and scheduled host alike).</summary>
    public const string RegistryProviderName = "Windows uninstall registry";

    private readonly Dictionary<string, string> versions;
    private readonly Dictionary<string, string> names;

    private WholePcRegistrationSnapshot(Dictionary<string, string> versions, Dictionary<string, string> names, bool isComplete)
    {
        this.versions = versions;
        this.names = names;
        IsComplete = isComplete;
    }

    /// <summary>
    /// Codex P2 on #91: false when the observation carries a registry warning that isn't about the current user's view (a whole-PC view
    /// or entry couldn't be read, the entry budget was exceeded, or the provider failed or timed out). An incomplete snapshot is never
    /// comparison evidence: no update starts from one, and an incomplete post-update one makes no comparison.
    /// </summary>
    public bool IsComplete { get; }

    /// <summary>How many whole-PC entries the snapshot holds.</summary>
    public int Count => versions.Count;

    /// <summary>The whole-PC registry entries of <paramref name="observation"/>.</summary>
    public static WholePcRegistrationSnapshot From(LiveInventoryObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Dictionary<Guid, string> productNames = [];
        foreach (CanonicalProduct product in observation.Products) productNames.TryAdd(product.Id, product.DisplayName);
        Dictionary<string, List<string>> grouped = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (InstallationInstance instance in observation.Instances)
        {
            if (!string.Equals(instance.Provider, "registry", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(instance.Scope, nameof(UpdateScope.Machine), StringComparison.OrdinalIgnoreCase)) continue;
            string key = $"{instance.Architecture?.Trim()}\u001f{instance.ProviderIdentity?.Trim()}";
            if (!grouped.TryGetValue(key, out List<string>? list)) grouped[key] = list = [];
            list.Add(instance.Version?.Trim() ?? string.Empty);
            names.TryAdd(key, productNames.TryGetValue(instance.ProductId, out string? name) && !string.IsNullOrWhiteSpace(name)
                ? name.Trim() : instance.ProviderIdentity?.Trim() ?? string.Empty);
        }
        Dictionary<string, string> versions = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, List<string> list) in grouped)
            versions[key] = string.Join('\u001e', list.Order(StringComparer.Ordinal));
        string prefix = RegistryProviderName + ":";
        bool complete = !observation.Warnings.Any(warning => warning is null ||
            (warning.StartsWith(prefix, StringComparison.Ordinal) && !warning.AsSpan(prefix.Length).StartsWith("CurrentUser:", StringComparison.Ordinal)));
        return new(versions, names, complete);
    }

    /// <summary>
    /// The names of the entries that are new, gone or at another version in this snapshot compared with <paramref name="before"/>,
    /// sorted; empty when nothing changed.
    /// </summary>
    public IReadOnlyList<string> ChangedSince(WholePcRegistrationSnapshot before)
    {
        ArgumentNullException.ThrowIfNull(before);
        SortedSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string version) in versions)
            if (!before.versions.TryGetValue(key, out string? earlier) || !string.Equals(earlier, version, StringComparison.Ordinal))
                changed.Add(names[key]);
        foreach (string key in before.versions.Keys)
            if (!versions.ContainsKey(key)) changed.Add(before.names[key]);
        return [.. changed];
    }
}
