using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PCSteward.Provider.Chocolatey;

/// <summary>Passive parsed configuration, not file trust, compatible policy or execution authority. No credential values are retained.</summary>
internal sealed class ChocolateyConfigurationObservation(
    Dictionary<string, bool> features, Dictionary<string, ChocolateyFeatureSetting> observedFeatures, string cache, int commandTimeout, int webTimeout,
    bool proxyConfigured, bool proxyCredentialsConfigured, List<ChocolateyConfiguredSource> sources, string fingerprint)
{
    internal IReadOnlyDictionary<string, bool> Features { get; } = new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(features, StringComparer.InvariantCultureIgnoreCase));
    internal IReadOnlyDictionary<string, ChocolateyFeatureSetting> ObservedFeatures { get; } =
        new ReadOnlyDictionary<string, ChocolateyFeatureSetting>(new Dictionary<string, ChocolateyFeatureSetting>(observedFeatures, StringComparer.InvariantCultureIgnoreCase));
    internal string CacheLocation { get; } = cache;
    internal int CommandExecutionTimeoutSeconds { get; } = commandTimeout;
    internal int WebRequestTimeoutSeconds { get; } = webTimeout;
    internal bool ProxyConfigured { get; } = proxyConfigured;
    internal bool ProxyCredentialsConfigured { get; } = proxyCredentialsConfigured;
    internal IReadOnlyList<ChocolateyConfiguredSource> Sources { get; } = Array.AsReadOnly(sources.ToArray());
    internal string Fingerprint { get; } = fingerprint;
}

internal sealed record ChocolateyFeatureSetting(bool Enabled, bool SetExplicitly);

internal sealed class ChocolateyConfiguredSource(string id, string value, bool disabled, bool bypassProxy,
    bool selfService, bool adminOnly, int priority, bool credentials, bool certificate)
{
    internal string Id { get; } = id;
    internal string Value { get; } = value;
    internal bool Disabled { get; } = disabled;
    internal bool BypassProxy { get; } = bypassProxy;
    internal bool SelfService { get; } = selfService;
    internal bool AdminOnly { get; } = adminOnly;
    internal int Priority { get; } = priority;
    internal bool HasCredentials { get; } = credentials;
    internal bool HasClientCertificate { get; } = certificate;
}

/// <summary>Strict bounded projection of the admitted 2.7.5 XML model. It never reads files or repairs provider configuration.</summary>
internal static class ChocolateyConfigurationParser
{
    private const int MaximumBytes = 65536;
    private static readonly string[] NoAttributes = [];
    private static readonly string[] FeatureAttributes = ["name", "enabled", "setExplicitly", "description"];
    private static readonly string[] ConfigAttributes = ["key", "value", "description"];
    private static readonly string[] SourceAttributes = ["id", "value", "disabled", "bypassProxy", "selfService", "adminOnly", "user", "password", "priority", "certificate", "certificatePassword"];
    private static readonly string[] ApiAttributes = ["source", "key"];
    private static readonly ReadOnlyDictionary<string, bool> Defaults = new(new Dictionary<string, bool>(StringComparer.InvariantCultureIgnoreCase)
    {
        ["checksumFiles"] = true, ["allowEmptyChecksums"] = false, ["allowEmptyChecksumsSecure"] = true,
        ["autoUninstaller"] = true, ["failOnAutoUninstaller"] = false, ["allowGlobalConfirmation"] = false,
        ["failOnStandardError"] = false, ["powershellHost"] = true, ["logEnvironmentValues"] = false,
        ["virusCheck"] = false, ["failOnInvalidOrMissingLicense"] = false, ["ignoreInvalidOptionsSwitches"] = true,
        ["usePackageExitCodes"] = true, ["useEnhancedExitCodes"] = false, ["useFipsCompliantChecksums"] = false,
        ["showNonElevatedWarnings"] = true, ["showDownloadProgress"] = true, ["stopOnFirstPackageFailure"] = false,
        ["useRememberedArgumentsForUpgrades"] = false, ["ignoreUnfoundPackagesOnUpgradeOutdated"] = false,
        ["skipPackageUpgradesWhenNotInstalled"] = false, ["removePackageInformationOnUninstall"] = false,
        ["logWithoutColor"] = false, ["exitOnRebootDetected"] = false, ["logValidationResultsOnWarnings"] = true,
        ["usePackageRepositoryOptimizations"] = true, ["disableCompatibilityChecks"] = false,
        ["usePackageHashValidation"] = false, ["alwaysIncludeHeaders"] = false, ["useHttpCache"] = true
    });
    private static readonly string[] ConfigNames = ["cacheLocation", "commandExecutionTimeoutSeconds", "webRequestTimeoutSeconds",
        "proxy", "proxyUser", "proxyPassword", "proxyBypassList", "proxyBypassOnLocal", "upgradeAllExceptions", "defaultTemplateName", "defaultPushSource"];

    internal static IReadOnlyDictionary<string, bool> SupportedFeatureDefaults => Defaults;

    internal static bool TryRead(ReadOnlySpan<byte> bytes, out ChocolateyConfigurationObservation? observation)
    {
        observation = null;
        if (bytes.Length is 0 or > MaximumBytes) return false;
        try
        {
            Encoding encoding = bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) ? new UnicodeEncoding(false, false, true) :
                bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) ? new UnicodeEncoding(true, false, true) : new UTF8Encoding(false, true);
            // UTF32 and non-BOM UTF16 contain NULs under these admitted encodings and refuse.
            if (encoding.GetString(bytes).Contains('\0')) return false;
            using MemoryStream stream = new(bytes.ToArray(), writable: false);
            XmlReaderSettings settings = new()
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumBytes,
                IgnoreWhitespace = true, IgnoreComments = true, ConformanceLevel = ConformanceLevel.Document
            };
            // Bound depth and nodes before allocating a tree. Every supported value is an attribute.
            using (XmlReader guard = XmlReader.Create(stream, settings))
            {
                int nodes = 0;
                while (guard.Read())
                {
                    if (++nodes > 4096 || guard.Depth > 2 || guard.NodeType is XmlNodeType.ProcessingInstruction or XmlNodeType.CDATA ||
                        guard.NodeType == XmlNodeType.Text && !string.IsNullOrWhiteSpace(guard.Value)) return false;
                    if (guard.NodeType == XmlNodeType.XmlDeclaration && guard.GetAttribute("encoding") is { } declaredEncoding &&
                        !declaredEncoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase) &&
                        !declaredEncoding.Equals("utf-16", StringComparison.OrdinalIgnoreCase) &&
                        !declaredEncoding.Equals("utf-16le", StringComparison.OrdinalIgnoreCase) &&
                        !declaredEncoding.Equals("utf-16be", StringComparison.OrdinalIgnoreCase)) return false;
                }
            }
            stream.Position = 0;
            using XmlReader reader = XmlReader.Create(stream, settings);
            XElement root = XDocument.Load(reader).Root ?? throw new FormatException();
            RequireName(root, "chocolatey"); Attributes(root, NoAttributes);
            Dictionary<string, bool> features = new(Defaults, StringComparer.InvariantCultureIgnoreCase);
            Dictionary<string, ChocolateyFeatureSetting> observedFeatures = new(StringComparer.InvariantCultureIgnoreCase);
            Dictionary<string, string> config = new(StringComparer.InvariantCultureIgnoreCase);
            HashSet<string> featureNames = new(StringComparer.InvariantCultureIgnoreCase);
            HashSet<string> collections = new(StringComparer.Ordinal);
            HashSet<string> sourceIds = new(StringComparer.InvariantCultureIgnoreCase);
            HashSet<string> sourceValues = new(StringComparer.InvariantCultureIgnoreCase);
            HashSet<string> apiSources = new(StringComparer.InvariantCultureIgnoreCase);
            List<ChocolateyConfiguredSource> sources = [];
            foreach (XElement collection in root.Elements())
            {
                if (collection.Name.NamespaceName.Length != 0 || !collections.Add(collection.Name.LocalName)) throw new FormatException();
                Attributes(collection, NoAttributes);
                foreach (XElement entry in collection.Elements())
                {
                    if (entry.HasElements) throw new FormatException();
                    Dictionary<string, string> attributes;
                    switch (collection.Name.LocalName)
                    {
                        case "features":
                            RequireName(entry, "feature"); attributes = Attributes(entry, FeatureAttributes);
                            string name = Identifier(attributes, "name");
                            if (!Defaults.TryGetValue(name, out bool defaultValue) || !featureNames.Add(name)) throw new FormatException();
                            bool enabled = Boolean(attributes, "enabled");
                            bool explicitlySet = Boolean(attributes, "setExplicitly");
                            observedFeatures.Add(name, new(enabled, explicitlySet));
                            features[name] = explicitlySet ? enabled : defaultValue;
                            break;
                        case "config":
                            RequireName(entry, "add"); attributes = Attributes(entry, ConfigAttributes);
                            string key = Identifier(attributes, "key");
                            if (!ConfigNames.Contains(key, StringComparer.InvariantCultureIgnoreCase) ||
                                !attributes.TryGetValue("value", out string? value) || !config.TryAdd(key, value)) throw new FormatException();
                            break;
                        case "sources":
                            RequireName(entry, "source"); attributes = Attributes(entry, SourceAttributes);
                            string id = Identifier(attributes, "id"), url = Identifier(attributes, "value", 4096);
                            if (sources.Count >= 64 || !sourceIds.Add(id) || !sourceValues.Add(url)) throw new FormatException();
                            sources.Add(new(id, url, Boolean(attributes, "disabled"), Boolean(attributes, "bypassProxy"),
                                Boolean(attributes, "selfService"), Boolean(attributes, "adminOnly"),
                                attributes.TryGetValue("priority", out string? priority) ? XmlConvert.ToInt32(priority) : 0,
                                HasValue(attributes, "user") || HasValue(attributes, "password"),
                                HasValue(attributes, "certificate") || HasValue(attributes, "certificatePassword")));
                            break;
                        case "apiKeys":
                            RequireName(entry, "apiKeys"); attributes = Attributes(entry, ApiAttributes);
                            if (apiSources.Count >= 64 || !apiSources.Add(Identifier(attributes, "source", 4096)) || !attributes.ContainsKey("key")) throw new FormatException();
                            break;
                        default: throw new FormatException();
                    }
                    if (featureNames.Count + config.Count > 256) throw new FormatException();
                }
                if (collection.Name.LocalName is not ("features" or "config" or "sources" or "apiKeys")) throw new FormatException();
            }
            string command = config.GetValueOrDefault("commandExecutionTimeoutSeconds", "2700");
            int commandTimeout = int.TryParse(command, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedCommand) &&
                (parsedCommand > 0 || command == "0") ? parsedCommand : 2700;
            int webTimeout = int.TryParse(config.GetValueOrDefault("webRequestTimeoutSeconds", "30"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsedWeb) && parsedWeb > 0 ? parsedWeb : 30;
            observation = new(features, observedFeatures, config.GetValueOrDefault("cacheLocation", string.Empty), commandTimeout, webTimeout,
                HasValue(config, "proxy"), HasValue(config, "proxyUser") || HasValue(config, "proxyPassword"), sources,
                Convert.ToHexString(SHA256.HashData(bytes)));
            return true;
        }
        catch (Exception exception) when (exception is XmlException or FormatException or InvalidOperationException or ArgumentException or OverflowException) { return false; }
    }

    private static Dictionary<string, string> Attributes(XElement element, string[] allowed)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration && attribute.Value is "http://www.w3.org/2001/XMLSchema" or "http://www.w3.org/2001/XMLSchema-instance") continue;
            if (attribute.Name.NamespaceName.Length != 0 || !allowed.Contains(attribute.Name.LocalName, StringComparer.Ordinal) ||
                attribute.Value.Length > 4096 || !result.TryAdd(attribute.Name.LocalName, attribute.Value)) throw new FormatException();
        }
        return result;
    }
    private static string Identifier(Dictionary<string, string> values, string key, int maximum = 128) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) && value.Length <= maximum &&
        value.All(character => character is >= ' ' and <= '~') ? value : throw new FormatException();
    private static void RequireName(XElement element, string name)
    {
        if (element.Name.NamespaceName.Length != 0 || element.Name.LocalName != name) throw new FormatException();
    }
    private static bool Boolean(Dictionary<string, string> values, string key) => values.TryGetValue(key, out string? value) && XmlConvert.ToBoolean(value);
    private static bool HasValue(Dictionary<string, string> values, string key) => values.TryGetValue(key, out string? value) && value.Length != 0;
}
