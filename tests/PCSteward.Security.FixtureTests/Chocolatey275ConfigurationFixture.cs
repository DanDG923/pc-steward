using System.Security;
using System.Text;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, TM-03/04/07/13; Profile V2 revision 2.1 (docs/review/TASK_50_PROFILE_V2_LEGACY_KEY_REVISION.md): a
/// real-shape Chocolatey 2.7.5 <c>chocolatey.config</c>, as the pinned client (tag 2.7.5, commit
/// <c>194ac6262ac75571c44aa0a431c3ab6f36fa73be</c>) leaves it after extracting its embedded template and serializing the
/// settings its configuration builder adds. Shape facts come from that source and the owner's D1 diagnostic: UTF-8 BOM,
/// root <c>xmlns:xsd</c>/<c>xmlns:xsi</c>, collections in the order config, sources, features, apiKeys; 12 config keys
/// (the six template keys first, <c>containsLegacyPackageInstalls</c> without a description); one Community source with
/// the serializer's always-written attributes; 30 features, none set explicitly; no API keys. Descriptions are shortened.
/// </summary>
internal static class Chocolatey275ConfigurationFixture
{
    internal const string CommunitySource = "https://community.chocolatey.org/api/v2/";

    // Template features first (chocolatey.config lines 15-19), then ConfigurationBuilder.SetAllFeatureFlags order. Each
    // enabled value is the 2.7.5 default, which the builder restores for any feature that is not set explicitly.
    private static readonly (string Name, bool Enabled)[] Features =
    [
        ("checksumFiles", true), ("autoUninstaller", true), ("allowGlobalConfirmation", false), ("failOnAutoUninstaller", false),
        ("failOnStandardError", false), ("allowEmptyChecksums", false), ("allowEmptyChecksumsSecure", true), ("powershellHost", true),
        ("logEnvironmentValues", false), ("virusCheck", false), ("failOnInvalidOrMissingLicense", false),
        ("ignoreInvalidOptionsSwitches", true), ("usePackageExitCodes", true), ("useEnhancedExitCodes", false),
        ("exitOnRebootDetected", false), ("useFipsCompliantChecksums", false), ("showNonElevatedWarnings", true),
        ("showDownloadProgress", true), ("stopOnFirstPackageFailure", false), ("useRememberedArgumentsForUpgrades", false),
        ("ignoreUnfoundPackagesOnUpgradeOutdated", false), ("skipPackageUpgradesWhenNotInstalled", false),
        ("removePackageInformationOnUninstall", false), ("logWithoutColor", false), ("logValidationResultsOnWarnings", true),
        ("usePackageRepositoryOptimizations", true), ("usePackageHashValidation", false), ("disableCompatibilityChecks", false),
        ("alwaysIncludeHeaders", false), ("useHttpCache", true),
    ];

    /// <summary>
    /// The UTF-8 BOM bytes. <paramref name="legacyValue"/> null omits the <c>containsLegacyPackageInstalls</c> entry;
    /// otherwise it is written exactly (the template value is <c>true</c>).
    /// </summary>
    internal static byte[] Bytes(string cacheLocation, string? legacyValue = "true")
    {
        StringBuilder xml = new();
        xml.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n")
            .Append("<chocolatey xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">\r\n")
            .Append("  <config>\r\n");
        Add(xml, "cacheLocation", cacheLocation, "Cache location if not TEMP folder.");
        if (legacyValue is not null) Add(xml, "containsLegacyPackageInstalls", legacyValue, description: null);
        Add(xml, "commandExecutionTimeoutSeconds", "2700", "Default timeout for command execution. '0' for infinite.");
        Add(xml, "proxy", "", "Explicit proxy location.");
        Add(xml, "proxyUser", "", "Optional proxy user.");
        Add(xml, "proxyPassword", "", "Optional proxy password. Encrypted.");
        Add(xml, "webRequestTimeoutSeconds", "30", "Default timeout for web requests.");
        Add(xml, "proxyBypassList", "", "Optional proxy bypass list.");
        Add(xml, "proxyBypassOnLocal", "true", "Bypass proxy for local connections.");
        Add(xml, "upgradeAllExceptions", "", "A comma-separated list of package names that should not be upgraded.");
        Add(xml, "defaultTemplateName", "", "Default template name used when running 'choco new' command.");
        Add(xml, "defaultPushSource", "", "Default source to push packages to when running 'choco push' command.");
        xml.Append("  </config>\r\n  <sources>\r\n")
            .Append("    <source id=\"chocolatey\" value=\"").Append(CommunitySource)
            .Append("\" disabled=\"false\" bypassProxy=\"false\" selfService=\"false\" adminOnly=\"false\" priority=\"0\" />\r\n")
            .Append("  </sources>\r\n  <features>\r\n");
        foreach ((string name, bool enabled) in Features)
            xml.Append("    <feature name=\"").Append(name).Append("\" enabled=\"").Append(enabled ? "true" : "false")
                .Append("\" setExplicitly=\"false\" description=\"").Append(name).Append(" (shortened).\" />\r\n");
        xml.Append("  </features>\r\n  <apiKeys />\r\n</chocolatey>");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(xml.ToString())];
    }

    private static void Add(StringBuilder xml, string key, string value, string? description)
    {
        xml.Append("    <add key=\"").Append(key).Append("\" value=\"").Append(SecurityElement.Escape(value)).Append('"');
        if (description is not null) xml.Append(" description=\"").Append(SecurityElement.Escape(description)).Append('"');
        xml.Append(" />\r\n");
    }
}
