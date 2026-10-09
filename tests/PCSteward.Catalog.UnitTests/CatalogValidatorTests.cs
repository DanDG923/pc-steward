using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PCSteward.Catalog;

namespace PCSteward.Catalog.UnitTests;

[TestClass]
public sealed class CatalogValidatorTests
{
    private static byte[] BundledBytes => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "catalog", "pcsteward-catalog.v1.json"));

    [TestMethod]
    public void BundledCatalogIsValidAndMatchesTheRepositoryFile()
    {
        CatalogLoadResult embedded = BundledCatalog.Load();
        Assert.IsTrue(embedded.IsLoaded, string.Join(", ", embedded.Errors));
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(BundledBytes)), embedded.Catalog!.Sha256);
        Assert.IsTrue(embedded.Catalog.Entries.Count > 0);
        Assert.IsTrue(embedded.Catalog.Entries.All(entry => entry.Providers.Count(provider => provider.IsDefault) == 1));
    }

    [TestMethod]
    public void ExpectedHashIsEnforced()
    {
        byte[] bytes = BundledBytes;
        Assert.IsTrue(CatalogValidator.Validate(bytes, Convert.ToHexStringLower(SHA256.HashData(bytes))).IsLoaded);
        CatalogLoadResult wrong = CatalogValidator.Validate(bytes, new string('0', 64));
        Assert.IsFalse(wrong.IsLoaded);
        CollectionAssert.Contains(wrong.Errors.ToArray(), "catalog:hash-mismatch");
    }

    [TestMethod]
    public void Utf8ByteOrderMarkIsAccepted()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. BundledBytes];
        Assert.IsTrue(CatalogValidator.Validate(withBom).IsLoaded);
    }

    [TestMethod]
    [DataRow("unknown-field", "catalog:json")]
    [DataRow("installer-switch-field", "catalog:json")]
    [DataRow("duplicate-json-property", "catalog:json")]
    [DataRow("trailing-comma", "catalog:json")]
    [DataRow("comment", "catalog:json")]
    [DataRow("schema-2", "catalog:schema-version")]
    [DataRow("bad-catalog-version", "catalog:catalog-version")]
    [DataRow("no-entries", "catalog:entry-count")]
    [DataRow("null-entry", "catalog:null-entry")]
    [DataRow("duplicate-id", ":duplicate-id")]
    [DataRow("bad-id", "entry:id")]
    [DataRow("padded-name", ":display")]
    [DataRow("unknown-category", ":category")]
    [DataRow("http-homepage", ":homepage")]
    [DataRow("userinfo-homepage", ":homepage")]
    [DataRow("two-defaults", ":providers")]
    [DataRow("no-default", ":providers")]
    [DataRow("three-providers", ":providers")]
    [DataRow("msstore-source", ":provider-source")]
    [DataRow("unknown-provider", ":provider-source")]
    [DataRow("command-in-id", ":package-id")]
    [DataRow("single-segment-winget-id", ":package-id")]
    [DataRow("chocolatey-user-scope", ":chocolatey-scope")]
    [DataRow("unknown-scope", ":scopes")]
    [DataRow("duplicate-scope", ":scopes")]
    [DataRow("duplicate-package", ":duplicate-package")]
    [DataRow("two-winget-providers", ":duplicate-provider")]
    [DataRow("unanchored-pattern", ":pattern")]
    [DataRow("long-pattern", ":pattern")]
    [DataRow("quantified-group-pattern", ":pattern")]
    [DataRow("escaped-backslash-bypass-pattern", ":pattern")]
    [DataRow("backreference-pattern", ":pattern")]
    [DataRow("lookahead-pattern", ":pattern")]
    [DataRow("publisher-without-name", ":publisher-without-name")]
    [DataRow("bad-msix-family", ":msix-family")]
    [DataRow("traversal-process-name", ":process-names")]
    [DataRow("too-many-process-names", ":process-names")]
    [DataRow("numeric-channel", ":channel")]
    [DataRow("oversize", "catalog:size")]
    public void HostileCatalogIsDisabledWithAStableReason(string mutation, string expectedReason)
    {
        string json = Mutate(mutation);
        CatalogLoadResult result = CatalogValidator.Validate(Encoding.UTF8.GetBytes(json));
        Assert.IsFalse(result.IsLoaded, mutation);
        Assert.IsNull(result.Catalog);
        Assert.IsTrue(result.Errors.Any(error => error.Contains(expectedReason, StringComparison.Ordinal)), $"{mutation}: {string.Join(", ", result.Errors)}");
    }

    [TestMethod]
    public void EmptyInputIsDisabled() => Assert.IsFalse(CatalogValidator.Validate([]).IsLoaded);

    [TestMethod]
    public void LoadedCatalogCannotBeMutatedThroughItsCollections()
    {
        PersonalCatalog catalog = CatalogValidator.Validate(BundledBytes).Catalog!;
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<CatalogEntry>)catalog.Entries).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<CatalogProviderIdentity>)catalog.Entries[0].Providers).Clear());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<CatalogScope>)catalog.Entries[0].Providers[0].Scopes).Clear());
    }

    [TestMethod]
    public void ThreeHundredEntryCatalogValidatesQuickly()
    {
        JsonObject root = Root();
        JsonArray source = root["entries"]!.AsArray();
        JsonArray scaled = [];
        for (int index = 0; index < 300; index++)
        {
            JsonObject entry = source[index % source.Count]!.DeepClone().AsObject();
            entry["id"] = "pcs:entry-" + index;
            foreach (JsonNode? provider in entry["providers"]!.AsArray())
            {
                string id = provider!["packageId"]!.GetValue<string>();
                provider["packageId"] = provider["provider"]!.GetValue<string>() == "winget" ? id + ".N" + index : id + "-n" + index;
            }

            scaled.Add(entry);
        }

        root["entries"] = scaled;
        byte[] bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        _ = CatalogValidator.Validate(BundledBytes);
        Stopwatch watch = Stopwatch.StartNew();
        CatalogLoadResult result = CatalogValidator.Validate(bytes);
        watch.Stop();
        Assert.IsTrue(result.IsLoaded, string.Join(", ", result.Errors));
        Assert.AreEqual(300, result.Catalog!.Entries.Count);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(1), $"{watch.Elapsed.TotalMilliseconds} ms");
    }

    private static JsonObject Root() => JsonNode.Parse(BundledBytes)!.AsObject();

    private static string Mutate(string mutation)
    {
        JsonObject root = Root();
        JsonArray entries = root["entries"]!.AsArray();
        JsonObject first = entries[0]!.AsObject();
        JsonObject detection = first["detection"]!.AsObject();
        JsonObject winget = first["providers"]![0]!.AsObject();
        JsonObject chocolatey = first["providers"]![1]!.AsObject();
        switch (mutation)
        {
            case "unknown-field": first["installerUrl"] = "https://example.invalid/setup.exe"; break;
            case "installer-switch-field": winget["silentArguments"] = "/S"; break;
            case "duplicate-json-property": return root.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
            case "trailing-comma": return root.ToJsonString()[..^1] + ",}";
            case "comment": return "/* note */" + root.ToJsonString();
            case "schema-2": root["schemaVersion"] = 2; break;
            case "bad-catalog-version": root["catalogVersion"] = "latest"; break;
            case "no-entries": root["entries"] = new JsonArray(); break;
            case "null-entry": entries.Add(null); break;
            case "duplicate-id": entries[1]!["id"] = first["id"]!.GetValue<string>(); break;
            case "bad-id": first["id"] = "firefox"; break;
            case "padded-name": first["name"] = " Firefox"; break;
            case "unknown-category": first["category"] = "Games"; break;
            case "http-homepage": first["homepage"] = "http://example.invalid"; break;
            case "userinfo-homepage": first["homepage"] = "https://user:secret@example.invalid"; break;
            case "two-defaults": chocolatey["default"] = true; break;
            case "no-default": winget["default"] = false; break;
            case "three-providers": first["providers"]!.AsArray().Add(chocolatey.DeepClone()); break;
            case "msstore-source": winget["source"] = "msstore"; break;
            case "unknown-provider": winget["provider"] = "scoop"; break;
            case "command-in-id": winget["packageId"] = "Mozilla.Firefox & calc"; break;
            case "single-segment-winget-id": winget["packageId"] = "Firefox"; break;
            case "chocolatey-user-scope": chocolatey["scopes"] = new JsonArray("user"); break;
            case "unknown-scope": winget["scopes"] = new JsonArray("system"); break;
            case "duplicate-scope": winget["scopes"] = new JsonArray("user", "user"); break;
            case "duplicate-package": entries[1]!["providers"]![0]!["packageId"] = winget["packageId"]!.GetValue<string>(); break;
            case "two-winget-providers": chocolatey["provider"] = "winget"; chocolatey["source"] = "winget"; chocolatey["packageId"] = "Mozilla.Other"; chocolatey["scopes"] = new JsonArray("user"); break;
            case "unanchored-pattern": detection["arpDisplayName"] = "Mozilla Firefox"; break;
            case "long-pattern": detection["arpDisplayName"] = "^" + new string('a', 130) + "$"; break;
            case "quantified-group-pattern": detection["arpDisplayName"] = "^(a+)+$"; break;
            case "escaped-backslash-bypass-pattern": detection["arpDisplayName"] = @"^(a*\\?)+$"; break;
            case "backreference-pattern": detection["arpDisplayName"] = @"^(a)\1$"; break;
            case "lookahead-pattern": detection["arpDisplayName"] = "^(?=Moz).*$"; break;
            case "publisher-without-name": detection.Remove("arpDisplayName"); break;
            case "bad-msix-family": detection["msixFamilyName"] = "../evil"; break;
            case "traversal-process-name": first["processNames"] = new JsonArray("..\\evil.exe"); break;
            case "too-many-process-names": first["processNames"] = new JsonArray([.. Enumerable.Range(0, 9).Select(index => (JsonNode?)("p" + index + ".exe"))]); break;
            case "numeric-channel": first["flags"] = new JsonObject { ["channel"] = "1" }; break;
            case "oversize": first["summary"] = new string('x', CatalogValidator.MaximumBytes); break;
            default: Assert.Fail(mutation); break;
        }

        return root.ToJsonString();
    }
}
