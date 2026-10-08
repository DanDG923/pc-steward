using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PatchStyle.Prototype;

// Task 45 spike: personal catalog validation and detection (ADR-023). Pure code.

public sealed record CatalogDocument(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] CatalogEntry[] Entries);

public sealed record CatalogEntry(
    [property: JsonRequired] string Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Publisher,
    [property: JsonRequired] string Category,
    string? Summary,
    string? Homepage,
    [property: JsonRequired] CatalogProvider[] Providers,
    [property: JsonRequired] CatalogDetection Detection,
    string[]? ProcessNames,
    CatalogFlags? Flags);

public sealed record CatalogProvider(
    [property: JsonRequired] string Provider,
    [property: JsonRequired] string Source,
    [property: JsonRequired] string PackageId,
    [property: JsonRequired] string[] Scopes,
    bool Default);

public sealed record CatalogDetection(
    string? ArpDisplayName,
    string? ArpPublisher,
    string? MsixFamilyName);

public sealed record CatalogFlags(bool SelfUpdating, bool RestartLikely, string? Channel, bool AutomationAllowed = true);

public sealed record CatalogLoadResult(bool IsValid, IReadOnlyList<CatalogEntry> Entries, IReadOnlyList<string> Errors, TimeSpan LoadTime);

public static partial class CatalogLoader
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumEntries = 1000;
    public const int MaximumPatternLength = 128;
    public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "Browsers", "Communication", "Media", "Imaging", "Documents", "Developer", "Utilities",
        "Compression", "CloudStorage", "Security", "Runtimes", "Gaming"
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow
    };

    public static CatalogLoadResult Load(byte[] utf8, string? expectedSha256)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        List<string> errors = [];
        if (utf8 is null || utf8.Length is 0 or > MaximumBytes) return Fail("size", started);
        if (expectedSha256 is not null && !string.Equals(Convert.ToHexStringLower(SHA256.HashData(utf8)), expectedSha256, StringComparison.Ordinal))
        {
            return Fail("hash-mismatch", started);
        }

        CatalogDocument? document;
        try { document = JsonSerializer.Deserialize<CatalogDocument>(utf8, Options); }
        catch (JsonException exception) { return Fail("json:" + exception.GetType().Name, started); }
        if (document is null || document.SchemaVersion != 1) return Fail("schema-version", started);
        if (document.Entries.Length is 0 or > MaximumEntries) return Fail("entry-count", started);

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> packageKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (CatalogEntry entry in document.Entries)
        {
            string prefix = entry?.Id ?? "<null>";
            if (entry is null) { errors.Add("null-entry"); continue; }
            if (!EntryId().IsMatch(entry.Id) || !ids.Add(entry.Id)) errors.Add(prefix + ":id");
            if (!Bounded(entry.Name, 128) || !Bounded(entry.Publisher, 128)) errors.Add(prefix + ":display");
            if (!Categories.Contains(entry.Category)) errors.Add(prefix + ":category");
            if (entry.Summary is not null && !Bounded(entry.Summary, 280)) errors.Add(prefix + ":summary");
            if (entry.Homepage is not null && !(Bounded(entry.Homepage, 256) && Uri.TryCreate(entry.Homepage, UriKind.Absolute, out Uri? home) && home.Scheme == Uri.UriSchemeHttps)) errors.Add(prefix + ":homepage");
            if (entry.Providers.Length is 0 or > 2 || entry.Providers.Count(provider => provider.Default) != 1) errors.Add(prefix + ":providers");
            foreach (CatalogProvider provider in entry.Providers) ValidateProvider(prefix, provider, packageKeys, errors);
            ValidateDetection(prefix, entry.Detection, errors);
            foreach (string process in entry.ProcessNames ?? []) if (!ProcessName().IsMatch(process)) errors.Add(prefix + ":process-name");
            if (entry.Flags?.Channel is not null and not ("Stable" or "Preview")) errors.Add(prefix + ":channel");
        }

        TimeSpan elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        return errors.Count == 0
            ? new(true, document.Entries, [], elapsed)
            : new(false, [], errors, elapsed);
    }

    // Spike result: NonBacktracking construction cost ~5.8 ms per pattern (3.5 s for 600 patterns), so the
    // interpreted engine is used with a per-match timeout, and the grammar check below removes the
    // constructs that cause catastrophic backtracking or are not plain matching (quantified groups other
    // than '?', backreferences, lookarounds, inline options, conditionals).
    internal static Regex CompileDetectionPattern(string pattern) =>
        new(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, PatternTimeout);

    internal static bool IsAllowedPatternGrammar(string pattern) => !ForbiddenPatternConstruct().IsMatch(pattern);

    private static void ValidateProvider(string prefix, CatalogProvider provider, HashSet<string> packageKeys, List<string> errors)
    {
        bool valid = (provider.Provider, provider.Source) switch
        {
            ("winget", "winget") or ("winget", "msstore") => WinGetId().IsMatch(provider.PackageId),
            ("chocolatey", "chocolatey-community") => ChocolateyId().IsMatch(provider.PackageId) && provider.Scopes is ["machine"],
            _ => false
        };
        if (!valid) errors.Add(prefix + ":provider-identity");
        if (provider.Scopes.Length is 0 or > 2 || provider.Scopes.Any(scope => scope is not ("user" or "machine")) || provider.Scopes.Distinct(StringComparer.Ordinal).Count() != provider.Scopes.Length)
        {
            errors.Add(prefix + ":scopes");
        }

        if (!packageKeys.Add(provider.Source + "|" + provider.PackageId)) errors.Add(prefix + ":duplicate-package");
    }

    private static void ValidateDetection(string prefix, CatalogDetection detection, List<string> errors)
    {
        if (detection.ArpDisplayName is null && detection.MsixFamilyName is null)
        {
            // Provider identities alone (WinGet/Chocolatey correlation) remain valid detection evidence.
            return;
        }

        foreach (string? pattern in new[] { detection.ArpDisplayName, detection.ArpPublisher })
        {
            if (pattern is null) continue;
            if (pattern.Length > MaximumPatternLength || !pattern.StartsWith('^') || !pattern.EndsWith('$')) { errors.Add(prefix + ":pattern-shape"); continue; }
            if (!IsAllowedPatternGrammar(pattern)) { errors.Add(prefix + ":pattern-unsupported"); continue; }
            try { _ = CompileDetectionPattern(pattern); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException) { errors.Add(prefix + ":pattern-unsupported"); }
        }

        if (detection.MsixFamilyName is not null && !MsixFamily().IsMatch(detection.MsixFamilyName)) errors.Add(prefix + ":msix-family");
    }

    private static bool Bounded(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);

    private static CatalogLoadResult Fail(string reason, long started) => new(false, [], [reason], System.Diagnostics.Stopwatch.GetElapsedTime(started));

    [GeneratedRegex("^pcs:[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryId();

    // Unescaped ')' followed by '*', '+' or '{' (quantified group); backreferences; '(?' constructs
    // (lookarounds, inline options, named/atomic groups, conditionals). Non-capturing '(?:' is also refused
    // to keep the grammar small.
    [GeneratedRegex(@"(?<!\\)\)[*+{]|\\[1-9k]|\(\?", RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenPatternConstruct();

    // Spike candidate: production allowlist plus '+' and '_' (WinGet allows both, e.g. Notepad++.Notepad++).
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9+_-]{0,31}(\.[A-Za-z0-9+_-]{1,32}){1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex WinGetId();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex ChocolateyId();

    [GeneratedRegex(@"^[A-Za-z0-9 ._+-]{1,60}\.exe$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ProcessName();

    [GeneratedRegex("^[A-Za-z0-9.-]{1,64}_[a-z0-9]{13}$", RegexOptions.CultureInvariant)]
    private static partial Regex MsixFamily();
}

public sealed record InstalledRecord(string DisplayName, string? Publisher, string Provider, string? ProviderIdentity);

public enum DetectionKind { None, ProviderIdentity, ArpPatternWithPublisher, ArpPatternWithoutPublisher, Ambiguous }

public sealed record DetectionOutcome(InstalledRecord Record, DetectionKind Kind, IReadOnlyList<string> EntryIds)
{
    /// <summary>Only provider identity or name+publisher evidence may become an Exact catalog mapping.</summary>
    public bool IsExact => Kind is DetectionKind.ProviderIdentity or DetectionKind.ArpPatternWithPublisher;
}

/// <summary>Deterministic catalog detection. Provider identity wins; ARP patterns are a fallback.</summary>
public sealed class CatalogDetector
{
    private readonly IReadOnlyList<(CatalogEntry Entry, Regex? Name, Regex? Publisher)> compiled;

    public CatalogDetector(IReadOnlyList<CatalogEntry> entries)
    {
        compiled = entries.Select(entry => (
            entry,
            entry.Detection.ArpDisplayName is null ? null : CatalogLoader.CompileDetectionPattern(entry.Detection.ArpDisplayName),
            entry.Detection.ArpPublisher is null ? null : CatalogLoader.CompileDetectionPattern(entry.Detection.ArpPublisher))).ToArray();
    }

    public DetectionOutcome Detect(InstalledRecord record)
    {
        string[] byIdentity = compiled
            .Where(item => record.ProviderIdentity is not null && item.Entry.Providers.Any(provider =>
                ProviderMatches(provider, record.Provider) &&
                string.Equals(provider.PackageId, record.ProviderIdentity, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.Entry.Id).Distinct(StringComparer.Ordinal).ToArray();
        if (byIdentity.Length == 1) return new(record, DetectionKind.ProviderIdentity, byIdentity);
        if (byIdentity.Length > 1) return new(record, DetectionKind.Ambiguous, byIdentity);

        List<(string Id, bool PublisherConfirmed)> byName = [];
        foreach ((CatalogEntry entry, Regex? name, Regex? publisher) in compiled)
        {
            if (name is null || !SafeMatch(name, record.DisplayName)) continue;
            if (publisher is null) { byName.Add((entry.Id, false)); continue; }
            if (record.Publisher is null) { byName.Add((entry.Id, false)); continue; }
            if (SafeMatch(publisher, record.Publisher)) byName.Add((entry.Id, true));
        }

        if (byName.Count == 0) return new(record, DetectionKind.None, []);
        if (byName.Count > 1) return new(record, DetectionKind.Ambiguous, byName.Select(item => item.Id).ToArray());
        return new(record, byName[0].PublisherConfirmed ? DetectionKind.ArpPatternWithPublisher : DetectionKind.ArpPatternWithoutPublisher, [byName[0].Id]);
    }

    private static bool ProviderMatches(CatalogProvider provider, string recordProvider) =>
        (provider.Provider, recordProvider.ToLowerInvariant()) is ("winget", "winget") or ("chocolatey", "chocolatey");

    private static bool SafeMatch(Regex pattern, string input)
    {
        try { return input.Length <= 512 && pattern.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
