using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PCSteward.Catalog;

/// <summary>One installed instance read from the local PC Steward database for seeding.</summary>
public sealed record CatalogSeedInstance(string DisplayName, string? Publisher, string Provider, string ProviderIdentity, string? SourceId);

public sealed record CatalogSeedResult(string ProposalJson, int ProposedEntries, IReadOnlyList<string> Skipped);

/// <summary>
/// Proposes catalog entries from exact local WinGet (source `winget`) and Chocolatey matches (FR-CAT-07).
/// The output is a draft for the owner to review; it is never loaded automatically.
/// </summary>
public static partial class CatalogSeedProposer
{
    private const string VersionTail = "[0-9.]{1,32}";
    private const string PlaceholderCategory = "Utilities";
    private const string UnknownPublisher = "Unknown publisher";

    public static CatalogSeedResult Propose(IEnumerable<CatalogSeedInstance> instances, DateOnly seededOn)
    {
        ArgumentNullException.ThrowIfNull(instances);
        CatalogSeedInstance[] all = [.. instances.Where(instance => instance is not null)];
        Dictionary<string, string> registryPublishers = new(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogSeedInstance instance in all.Where(instance => instance.Provider == "registry" && !string.IsNullOrWhiteSpace(instance.Publisher)))
        {
            registryPublishers.TryAdd(instance.DisplayName.Trim(), instance.Publisher!.Trim());
        }

        List<string> skipped = [];
        JsonArray entries = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogSeedInstance instance in all.OrderBy(instance => instance.Provider, StringComparer.Ordinal).ThenBy(instance => instance.ProviderIdentity, StringComparer.OrdinalIgnoreCase))
        {
            JsonObject? entry = instance switch
            {
                { Provider: "winget", SourceId: "winget" } => WinGetEntry(instance, registryPublishers),
                { Provider: "chocolatey" } => ChocolateyEntry(instance),
                _ => null
            };
            if (entry is null) continue;
            string identity = instance.Provider + "|" + instance.ProviderIdentity;
            if (!identities.Add(identity)) continue;
            entry["id"] = UniqueId(instance.ProviderIdentity, ids);

            string? rejection = SingleEntryRejection(entry, seededOn);
            if (rejection is not null) { skipped.Add(instance.Provider + ":" + rejection); continue; }
            entries.Add(entry);
        }

        JsonObject document = Document(seededOn, entries);
        string json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return new(json, entries.Count, skipped);
    }

    /// <summary>Anchored, escaped display-name pattern with dotted version runs generalised to a bounded class.</summary>
    public static string NamePattern(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        StringBuilder pattern = new("^");
        int last = 0;
        foreach (Match version in DottedVersion().Matches(displayName))
        {
            pattern.Append(Escape(displayName[last..version.Index])).Append(VersionTail);
            last = version.Index + version.Length;
        }

        pattern.Append(Escape(displayName[last..])).Append('$');
        return pattern.ToString();
    }

    private static JsonObject WinGetEntry(CatalogSeedInstance instance, Dictionary<string, string> registryPublishers)
    {
        string name = instance.DisplayName.Trim();
        registryPublishers.TryGetValue(name, out string? publisher);
        JsonObject detection = [];
        string namePattern = NamePattern(name);
        if (CatalogPatterns.IsAllowed(namePattern))
        {
            detection["arpDisplayName"] = namePattern;
            if (publisher is not null && CatalogPatterns.IsAllowed("^" + Escape(publisher) + "$")) detection["arpPublisher"] = "^" + Escape(publisher) + "$";
        }

        return Entry(Truncate(name), Truncate(publisher ?? instance.ProviderIdentity.Split('.')[0]), "winget", "winget", instance.ProviderIdentity, ["user", "machine"], detection);
    }

    private static JsonObject ChocolateyEntry(CatalogSeedInstance instance) =>
        Entry(Truncate(instance.ProviderIdentity), UnknownPublisher, "chocolatey", "chocolatey-community", instance.ProviderIdentity, ["machine"], []);

    private static JsonObject Entry(string name, string publisher, string provider, string source, string packageId, string[] scopes, JsonObject detection) => new()
    {
        ["id"] = string.Empty,
        ["name"] = name,
        ["publisher"] = publisher,
        ["category"] = PlaceholderCategory,
        ["providers"] = new JsonArray(new JsonObject
        {
            ["provider"] = provider,
            ["source"] = source,
            ["packageId"] = packageId,
            ["scopes"] = new JsonArray([.. scopes.Select(scope => (JsonNode?)scope)]),
            ["default"] = true
        }),
        ["detection"] = detection
    };

    private static string? SingleEntryRejection(JsonObject entry, DateOnly seededOn)
    {
        JsonObject single = Document(seededOn, new JsonArray(entry.DeepClone()));
        CatalogLoadResult result = CatalogValidator.Validate(Encoding.UTF8.GetBytes(single.ToJsonString()));
        return result.IsLoaded ? null : string.Join(',', result.Errors);
    }

    private static JsonObject Document(DateOnly seededOn, JsonArray entries) => new()
    {
        ["schemaVersion"] = CatalogValidator.SchemaVersion,
        ["catalogVersion"] = seededOn.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) + ".1",
        ["entries"] = entries
    };

    private static string UniqueId(string providerIdentity, HashSet<string> ids)
    {
        string slug = NonSlug().Replace(providerIdentity.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length < 2) slug = "app-" + slug;
        if (slug.Length > 56) slug = slug[..56].TrimEnd('-');
        string id = "pcs:" + slug;
        for (int suffix = 2; !ids.Add(id); suffix++) id = "pcs:" + slug + "-" + suffix.ToString(CultureInfo.InvariantCulture);
        return id;
    }

    private static string Truncate(string value) => value.Length <= 128 ? value : value[..128].TrimEnd();

    private static string Escape(string literal)
    {
        StringBuilder escaped = new(literal.Length);
        foreach (char character in literal)
        {
            if (@"\*+?|{}[]()^$.#".Contains(character, StringComparison.Ordinal)) escaped.Append('\\');
            escaped.Append(character);
        }

        return escaped.ToString();
    }

    [GeneratedRegex(@"\d+(\.\d+)+", RegexOptions.CultureInvariant)]
    private static partial Regex DottedVersion();

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonSlug();
}
