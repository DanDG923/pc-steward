using System.Text;
using PCSteward.Catalog;

namespace PCSteward.Catalog.UnitTests;

[TestClass]
public sealed class CatalogDetectorAndCompositionTests
{
    private const string CatalogJson = """
    {
      "schemaVersion": 1,
      "catalogVersion": "2026.10.08.1",
      "entries": [
        {
          "id": "pcs:firefox", "name": "Mozilla Firefox", "publisher": "Mozilla", "category": "Browsers",
          "providers": [
            { "provider": "winget", "source": "winget", "packageId": "Mozilla.Firefox", "scopes": ["user", "machine"], "default": true },
            { "provider": "chocolatey", "source": "chocolatey-community", "packageId": "firefox", "scopes": ["machine"], "default": false }
          ],
          "detection": { "arpDisplayName": "^Mozilla Firefox( \\((x64|x86|arm64) [a-z]{2,3}(-[A-Z]{2})?\\))?$", "arpPublisher": "^Mozilla$" }
        },
        {
          "id": "pcs:terminal", "name": "Windows Terminal", "publisher": "Microsoft", "category": "Developer",
          "providers": [ { "provider": "winget", "source": "winget", "packageId": "Microsoft.WindowsTerminal", "scopes": ["user"], "default": true } ],
          "detection": { "msixFamilyName": "Microsoft.WindowsTerminal_8wekyb3d8bbwe" }
        },
        {
          "id": "pcs:tool-a", "name": "Tool", "publisher": "Vendor", "category": "Utilities",
          "providers": [ { "provider": "winget", "source": "winget", "packageId": "Vendor.ToolA", "scopes": ["user"], "default": true } ],
          "detection": { "arpDisplayName": "^Tool$", "arpPublisher": "^Vendor$" }
        },
        {
          "id": "pcs:tool-b", "name": "Tool", "publisher": "Vendor", "category": "Utilities",
          "providers": [ { "provider": "winget", "source": "winget", "packageId": "Vendor.ToolB", "scopes": ["user"], "default": true } ],
          "detection": { "arpDisplayName": "^Tool$", "arpPublisher": "^Vendor$" }
        }
      ]
    }
    """;

    private static PersonalCatalog Catalog()
    {
        CatalogLoadResult result = CatalogValidator.Validate(Encoding.UTF8.GetBytes(CatalogJson));
        Assert.IsTrue(result.IsLoaded, string.Join(", ", result.Errors));
        return result.Catalog!;
    }

    [TestMethod]
    [DataRow("winget", "Mozilla.Firefox", "winget", CatalogDetectionKind.ProviderIdentity)]
    [DataRow("winget", "mozilla.firefox", "winget", CatalogDetectionKind.ProviderIdentity)]
    [DataRow("winget", "Mozilla.Firefox", "msstore", CatalogDetectionKind.NameOnly)]
    [DataRow("winget", "Mozilla.Firefox", null, CatalogDetectionKind.NameOnly)]
    [DataRow("chocolatey", "firefox", null, CatalogDetectionKind.ProviderIdentity)]
    [DataRow("chocolatey", "Mozilla.Firefox", null, CatalogDetectionKind.NameOnly)]
    public void ProviderIdentityRequiresTrustedProvenance(string provider, string identity, string? source, CatalogDetectionKind expected)
    {
        CatalogDetectionResult result = new CatalogDetector(Catalog()).Detect(new("Mozilla Firefox (x64 en-GB)", null, provider, identity, source));
        Assert.AreEqual(expected, result.Kind);
        Assert.AreEqual(expected == CatalogDetectionKind.ProviderIdentity, result.IsExact);
    }

    [TestMethod]
    [DataRow("Mozilla Firefox (x64 en-US)", "Mozilla", CatalogDetectionKind.NameAndPublisher)]
    [DataRow("Mozilla Firefox (x64 en-US)", "Someone Else", CatalogDetectionKind.None)]
    [DataRow("Mozilla Firefox (x64 en-US)", null, CatalogDetectionKind.NameOnly)]
    [DataRow("Mozilla Firefox ESR (x64 en-US)", "Mozilla", CatalogDetectionKind.None)]
    [DataRow("Mozilla Maintenance Service", "Mozilla", CatalogDetectionKind.None)]
    [DataRow("Tool", "Vendor", CatalogDetectionKind.Ambiguous)]
    public void NamePatternsNeedPublisherEvidenceAndRejectDecoys(string displayName, string? publisher, CatalogDetectionKind expected)
    {
        CatalogDetectionResult result = new CatalogDetector(Catalog()).Detect(new(displayName, publisher, "registry", "key", null));
        Assert.AreEqual(expected, result.Kind);
        Assert.AreEqual(expected == CatalogDetectionKind.NameAndPublisher, result.IsExact);
    }

    [TestMethod]
    public void MsixFamilyIsIdentityEvidence()
    {
        CatalogDetector detector = new(Catalog());
        Assert.AreEqual(CatalogDetectionKind.ProviderIdentity, detector.Detect(new("Terminal", null, "msix", "Microsoft.WindowsTerminal_1.0_x64__8wekyb3d8bbwe", null, "Microsoft.WindowsTerminal_8wekyb3d8bbwe")).Kind);
        Assert.AreEqual(CatalogDetectionKind.None, detector.Detect(new("Terminal", null, "msix", "Other", null, "Other.Family_8wekyb3d8bbwe")).Kind);
    }

    [TestMethod]
    public void OversizeInputNeverMatches() =>
        Assert.AreEqual(CatalogDetectionKind.None, new CatalogDetector(Catalog()).Detect(new(new string('M', 600), "Mozilla", "registry", "key", null)).Kind);

    [TestMethod]
    public void ValidUserEntryIsAddedAndDetectedByProviderIdentityOnly()
    {
        CatalogCompositionResult result = CatalogValidator.Compose(Catalog(), [User("pcs:user-notepadpp", "winget", "winget", "Notepad++.Notepad++", "user", "machine")]);
        Assert.AreEqual(0, result.RejectedUserEntries.Count, string.Join(", ", result.RejectedUserEntries));
        CatalogEntry added = result.Catalog.Entries.Single(entry => entry.Id == "pcs:user-notepadpp");
        Assert.IsTrue(added.IsUserAdded);
        Assert.IsNull(added.Detection.ArpDisplayNamePattern);
        CatalogDetector detector = new(result.Catalog);
        Assert.AreEqual(CatalogDetectionKind.ProviderIdentity, detector.Detect(new("Notepad++ (64-bit x64)", null, "winget", "Notepad++.Notepad++", "winget")).Kind);
        Assert.AreEqual(CatalogDetectionKind.None, detector.Detect(new("Notepad++ (64-bit x64)", "Notepad++ Team", "registry", "key", null)).Kind);
    }

    [TestMethod]
    [DataRow("pcs:custom", "winget", "winget", "Vendor.App", "user", "user:id-prefix")]
    [DataRow("pcs:user-dupe", "winget", "winget", "Mozilla.Firefox", "user", ":duplicate-package")]
    [DataRow("pcs:user-store", "winget", "msstore", "Vendor.App", "user", ":provider-source")]
    [DataRow("pcs:user-bad", "winget", "winget", "Vendor.App & calc", "user", ":package-id")]
    [DataRow("pcs:user-choco", "chocolatey", "chocolatey-community", "tool", "user", ":chocolatey-scope")]
    [DataRow("pcs:user-UPPER", "winget", "winget", "Vendor.App", "user", "entry:id")]
    public void TamperedUserRowsAreRejectedWithoutAffectingTheBundledCatalog(string id, string provider, string source, string packageId, string scope, string expected)
    {
        PersonalCatalog bundled = Catalog();
        CatalogCompositionResult result = CatalogValidator.Compose(bundled, [User(id, provider, source, packageId, scope)]);
        Assert.AreEqual(bundled.Entries.Count, result.Catalog.Entries.Count);
        Assert.IsTrue(result.RejectedUserEntries.Any(reason => reason.Contains(expected, StringComparison.Ordinal)), string.Join(", ", result.RejectedUserEntries));
    }

    [TestMethod]
    public void BundledIdCannotBeShadowedAndNullRowsAreReported()
    {
        CatalogCompositionResult result = CatalogValidator.Compose(Catalog(), [null, User("pcs:user-a", "winget", "winget", "Vendor.A", "user"), User("pcs:user-a", "winget", "winget", "Vendor.B", "user")]);
        Assert.AreEqual(5, result.Catalog.Entries.Count);
        CollectionAssert.Contains(result.RejectedUserEntries.ToArray(), "user:null");
        Assert.IsTrue(result.RejectedUserEntries.Any(reason => reason.EndsWith(":duplicate-id", StringComparison.Ordinal)));
    }

    private static UserCatalogEntryRecord User(string id, string provider, string source, string packageId, params string[] scopes) =>
        new(id, "User App", "User Vendor", "Utilities", provider, source, packageId, scopes);
}
