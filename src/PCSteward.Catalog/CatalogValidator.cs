using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PCSteward.Catalog;

/// <summary>
/// Strict, fail-closed validation of a personal catalog document (FR-CAT-01–03, ADR-023, TM-19).
/// Any error disables the whole catalog; nothing partial is returned.
/// </summary>
public static partial class CatalogValidator
{
    public const int SchemaVersion = 1;
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumEntries = 1000;
    public const int MaximumProcessNames = 8;
    public const string UserEntryPrefix = "pcs:user-";

    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
    {
        "Browsers", "Communication", "Media", "Imaging", "Documents", "Developer", "Utilities",
        "Compression", "CloudStorage", "Security", "Runtimes", "Gaming"
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = 8
    };

    /// <summary>Validates catalog bytes. When <paramref name="expectedSha256"/> is given, the bytes must match it.</summary>
    public static CatalogLoadResult Validate(ReadOnlySpan<byte> utf8, string? expectedSha256 = null)
    {
        if (utf8.Length is 0 or > MaximumBytes) return CatalogLoadResult.Disabled(null, ["catalog:size"]);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(utf8));
        if (expectedSha256 is not null && !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(sha256),
                System.Text.Encoding.ASCII.GetBytes(expectedSha256.ToLowerInvariant())))
        {
            return CatalogLoadResult.Disabled(sha256, ["catalog:hash-mismatch"]);
        }

        ReadOnlySpan<byte> json = utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? utf8[3..] : utf8;
        DocumentDto? document;
        try { document = JsonSerializer.Deserialize<DocumentDto>(json, Options); }
        catch (JsonException) { return CatalogLoadResult.Disabled(sha256, ["catalog:json"]); }

        if (document is null || document.SchemaVersion != SchemaVersion) return CatalogLoadResult.Disabled(sha256, ["catalog:schema-version"]);
        if (!CatalogVersionPattern().IsMatch(document.CatalogVersion ?? string.Empty)) return CatalogLoadResult.Disabled(sha256, ["catalog:catalog-version"]);
        if (document.Entries is null || document.Entries.Length is 0 or > MaximumEntries) return CatalogLoadResult.Disabled(sha256, ["catalog:entry-count"]);

        List<string> errors = [];
        List<CatalogEntry> entries = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> identities = new(StringComparer.OrdinalIgnoreCase);
        foreach (EntryDto? dto in document.Entries)
        {
            CatalogEntry? entry = ValidateEntry(dto, ids, identities, errors);
            if (entry is not null) entries.Add(entry);
        }

        return errors.Count == 0
            ? CatalogLoadResult.Loaded(new PersonalCatalog(document.CatalogVersion!, sha256, entries.AsReadOnly()))
            : CatalogLoadResult.Disabled(sha256, errors);
    }

    /// <summary>
    /// Adds reviewed user catalog entries (FR-CAT-06) to a validated bundled catalog. Each row is untrusted:
    /// it goes through the same entry rules, must use the `pcs:user-` prefix, may name exactly one provider,
    /// carries no detection patterns (provider identity only) and cannot reuse a bundled ID or package
    /// identity. Rejected rows are reported and ignored; they never disable the bundled catalog.
    /// </summary>
    public static CatalogCompositionResult Compose(PersonalCatalog bundled, IEnumerable<UserCatalogEntryRecord?> userEntries)
    {
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(userEntries);
        HashSet<string> ids = new(bundled.Entries.Select(entry => entry.Id), StringComparer.Ordinal);
        HashSet<string> identities = new(bundled.Entries.SelectMany(entry => entry.Providers).Select(provider => provider.Source.ToString() + "|" + provider.PackageId), StringComparer.OrdinalIgnoreCase);
        List<CatalogEntry> combined = [.. bundled.Entries];
        List<string> rejected = [];
        foreach (UserCatalogEntryRecord? record in userEntries)
        {
            if (record is null) { rejected.Add("user:null"); continue; }
            if (record.Id is null || !record.Id.StartsWith(UserEntryPrefix, StringComparison.Ordinal)) { rejected.Add("user:id-prefix"); continue; }
            EntryDto dto = new(
                record.Id, record.Name, record.Publisher, record.Category, null, null,
                [new ProviderDto(record.Provider, record.Source, record.PackageId, record.Scopes?.ToArray(), true)],
                new DetectionDto(null, null, null), [], null);
            List<string> errors = [];
            CatalogEntry? entry = ValidateEntry(dto, ids, identities, errors, isUserAdded: true);
            if (entry is null || errors.Count != 0) { rejected.AddRange(errors.Count == 0 ? [record.Id + ":invalid"] : errors); continue; }
            combined.Add(entry);
        }

        return new(new PersonalCatalog(bundled.CatalogVersion, bundled.Sha256, combined.AsReadOnly()), rejected.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static CatalogEntry? ValidateEntry(EntryDto? dto, HashSet<string> ids, HashSet<string> identities, List<string> errors, bool isUserAdded = false)
    {
        if (dto is null) { errors.Add("catalog:null-entry"); return null; }
        string prefix = dto.Id is not null && EntryIdPattern().IsMatch(dto.Id) ? dto.Id : "entry";
        int before = errors.Count;
        if (dto.Id is null || !EntryIdPattern().IsMatch(dto.Id)) errors.Add(prefix + ":id");
        else if (!ids.Add(dto.Id)) errors.Add(prefix + ":duplicate-id");
        if (!Bounded(dto.Name, 128) || !Bounded(dto.Publisher, 128)) errors.Add(prefix + ":display");
        if (dto.Category is null || !Categories.Contains(dto.Category)) errors.Add(prefix + ":category");
        if (dto.Summary is not null && !Bounded(dto.Summary, 280)) errors.Add(prefix + ":summary");
        Uri? homepage = null;
        if (dto.Homepage is not null && !(Bounded(dto.Homepage, 256) && Uri.TryCreate(dto.Homepage, UriKind.Absolute, out homepage) && homepage.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(homepage.UserInfo)))
        {
            errors.Add(prefix + ":homepage");
        }

        List<CatalogProviderIdentity> providers = ValidateProviders(prefix, dto.Providers, identities, errors);
        CatalogDetection? detection = ValidateDetection(prefix, dto.Detection, errors);
        string?[] rawNames = dto.ProcessNames ?? [];
        string[] processNames = [.. rawNames.Where(name => name is not null).Select(name => name!)];
        if (rawNames.Length > MaximumProcessNames || processNames.Length != rawNames.Length || processNames.Any(name => !ProcessNamePattern().IsMatch(name)) ||
            processNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != processNames.Length)
        {
            errors.Add(prefix + ":process-names");
        }

        CatalogChannel? channel = dto.Flags?.Channel switch
        {
            null or "Stable" => CatalogChannel.Stable,
            "Preview" => CatalogChannel.Preview,
            _ => null
        };
        if (channel is null) errors.Add(prefix + ":channel");
        if (errors.Count != before || detection is null) return null;

        return new CatalogEntry(
            dto.Id!, dto.Name!, dto.Publisher!, dto.Category!, dto.Summary, homepage,
            providers.AsReadOnly(), detection, Array.AsReadOnly(processNames),
            dto.Flags?.SelfUpdating ?? false, dto.Flags?.RestartLikely ?? false, channel!.Value,
            dto.Flags?.AutomationAllowed ?? true,
            isUserAdded);
    }

    private static List<CatalogProviderIdentity> ValidateProviders(string prefix, ProviderDto?[]? providers, HashSet<string> identities, List<string> errors)
    {
        List<CatalogProviderIdentity> result = [];
        if (providers is null || providers.Length is 0 or > 2 || providers.Count(provider => provider?.Default == true) != 1)
        {
            errors.Add(prefix + ":providers");
            return result;
        }

        foreach (ProviderDto? provider in providers)
        {
            if (provider is null || provider.PackageId is null || provider.Scopes is null) { errors.Add(prefix + ":provider"); continue; }
            (CatalogProviderKind Kind, CatalogSource Source)? route = (provider.Provider, provider.Source) switch
            {
                ("winget", "winget") => (CatalogProviderKind.WinGet, CatalogSource.WinGet),
                ("chocolatey", "chocolatey-community") => (CatalogProviderKind.Chocolatey, CatalogSource.ChocolateyCommunity),
                _ => null
            };
            if (route is null) { errors.Add(prefix + ":provider-source"); continue; }

            bool idValid = route.Value.Kind == CatalogProviderKind.WinGet
                ? WinGetIdPattern().IsMatch(provider.PackageId) && provider.PackageId.Length <= 128
                : ChocolateyIdPattern().IsMatch(provider.PackageId);
            if (!idValid) errors.Add(prefix + ":package-id");

            List<CatalogScope> scopes = [];
            foreach (string? scope in provider.Scopes)
            {
                if (scope == "user") scopes.Add(CatalogScope.User);
                else if (scope == "machine") scopes.Add(CatalogScope.Machine);
                else { errors.Add(prefix + ":scopes"); break; }
            }

            if (scopes.Count == 0 || scopes.Distinct().Count() != scopes.Count) errors.Add(prefix + ":scopes");
            if (route.Value.Kind == CatalogProviderKind.Chocolatey && !(scopes.Count == 1 && scopes[0] == CatalogScope.Machine)) errors.Add(prefix + ":chocolatey-scope");
            if (result.Any(existing => existing.Provider == route.Value.Kind)) errors.Add(prefix + ":duplicate-provider");
            if (!identities.Add(route.Value.Source.ToString() + "|" + provider.PackageId)) errors.Add(prefix + ":duplicate-package");
            result.Add(new CatalogProviderIdentity(route.Value.Kind, route.Value.Source, provider.PackageId, scopes.AsReadOnly(), provider.Default));
        }

        return result;
    }

    private static CatalogDetection? ValidateDetection(string prefix, DetectionDto? detection, List<string> errors)
    {
        if (detection is null) { errors.Add(prefix + ":detection"); return null; }
        bool valid = true;
        if (detection.ArpPublisher is not null && detection.ArpDisplayName is null) { errors.Add(prefix + ":publisher-without-name"); valid = false; }
        foreach (string? pattern in new[] { detection.ArpDisplayName, detection.ArpPublisher })
        {
            if (pattern is null) continue;
            if (!CatalogPatterns.IsAllowed(pattern)) { errors.Add(prefix + ":pattern"); valid = false; continue; }
            try { _ = CatalogPatterns.Compile(pattern); }
            catch (ArgumentException) { errors.Add(prefix + ":pattern"); valid = false; }
        }

        if (detection.MsixFamilyName is not null && !MsixFamilyPattern().IsMatch(detection.MsixFamilyName)) { errors.Add(prefix + ":msix-family"); valid = false; }
        return valid ? new CatalogDetection(detection.ArpDisplayName, detection.ArpPublisher, detection.MsixFamilyName) : null;
    }

    private static bool Bounded(string? value, int maximum) =>
        value is { Length: > 0 } && value.Length <= maximum && value.Trim().Length == value.Length && !value.Any(char.IsControl);

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2}(\.\d{1,3})?$", RegexOptions.CultureInvariant)]
    private static partial Regex CatalogVersionPattern();

    [GeneratedRegex("^pcs:[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryIdPattern();

    // Subset of the WinGet PackageIdentifier schema: 2–8 dot-separated segments of letters, digits, '+', '_' and '-'.
    // Catalog acceptance is not execution authority; the operation allowlist is applied separately (Task 48).
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9+_-]{0,31}(\.[A-Za-z0-9+_-]{1,32}){1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex WinGetIdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChocolateyIdPattern();

    [GeneratedRegex(@"^[A-Za-z0-9 ._+-]{1,60}\.exe$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessNamePattern();

    [GeneratedRegex("^[A-Za-z0-9.-]{1,64}_[a-z0-9]{13}$", RegexOptions.CultureInvariant)]
    private static partial Regex MsixFamilyPattern();

    private sealed record DocumentDto(int SchemaVersion, string? CatalogVersion, EntryDto?[]? Entries);

    private sealed record EntryDto(
        string? Id,
        string? Name,
        string? Publisher,
        string? Category,
        string? Summary,
        string? Homepage,
        ProviderDto?[]? Providers,
        DetectionDto? Detection,
        string?[]? ProcessNames,
        FlagsDto? Flags);

    private sealed record ProviderDto(string? Provider, string? Source, string? PackageId, string?[]? Scopes, bool Default);

    private sealed record DetectionDto(string? ArpDisplayName, string? ArpPublisher, string? MsixFamilyName);

    private sealed record FlagsDto(bool? SelfUpdating, bool? RestartLikely, string? Channel, bool? AutomationAllowed);
}

/// <summary>A user catalog row as stored (FR-CAT-06). Untrusted until <see cref="CatalogValidator.Compose"/> accepts it.</summary>
public sealed record UserCatalogEntryRecord(string Id, string Name, string Publisher, string Category, string Provider, string Source, string PackageId, IReadOnlyList<string> Scopes);

public sealed record CatalogCompositionResult(PersonalCatalog Catalog, IReadOnlyList<string> RejectedUserEntries);
