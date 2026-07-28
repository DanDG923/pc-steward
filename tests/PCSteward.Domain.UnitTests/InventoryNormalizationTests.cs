using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class InventoryNormalizationTests
{
    [TestMethod]
    public void MergesOnlyEvidenceEquivalentRawRecords()
    {
        RawInstallEvidence first = Evidence("raw-1", "Machine", "x64", "1.0", "Stable", "Contoso App");
        RawInstallEvidence duplicate = Evidence("raw-2", "Machine", "x64", "1.0", "Stable", "Contoso App");
        DeduplicationResult result = InventoryNormalization.NormalizeAndDeduplicate([first, duplicate]);
        Assert.AreEqual(1, result.Instances.Count); Assert.AreEqual(2, result.RawRecordProvenance.Single().Value.Count);
    }
    [TestMethod]
    public void DoesNotMergeScopeArchitectureVersionOrChannel()
    {
        foreach ((string scope, string architecture, string version, string channel) in new[] { ("User", "x64", "1.0", "Stable"), ("Machine", "x86", "1.0", "Stable"), ("Machine", "x64", "2.0", "Stable"), ("Machine", "x64", "1.0", "Beta") })
        {
            DeduplicationResult result = InventoryNormalization.NormalizeAndDeduplicate([Evidence("raw-1", "Machine", "x64", "1.0", "Stable", "Contoso App"), Evidence("raw-2", scope, architecture, version, channel, "Contoso App")]);
            Assert.AreEqual(2, result.Instances.Count);
        }
    }
    [TestMethod]
    public void SimilarNamesWithoutSameProviderIdentityDoNotMerge()
    {
        DeduplicationResult result = InventoryNormalization.NormalizeAndDeduplicate([Evidence("raw-1", "Machine", "x64", "1.0", "Stable", "Visual C++ Runtime", "Contoso.Runtime"), Evidence("raw-2", "Machine", "x64", "1.0", "Stable", "Visual C++ Runtime", "Fabrikam.Runtime")]);
        Assert.AreEqual(2, result.Instances.Count);
    }
    [TestMethod]
    public void SameProviderPackageIdFromDifferentTrustedSourcesStaysSeparate()
    {
        RawInstallEvidence winget = Evidence("raw-1", "Machine", "x64", "1.0", "Stable", "Contoso App", "Contoso.App") with { Provider = "winget", SourceId = "winget" };
        RawInstallEvidence store = winget with { RawRecordId = "raw-2", SourceId = "msstore" };

        DeduplicationResult result = InventoryNormalization.NormalizeAndDeduplicate([winget, store]);

        Assert.HasCount(2, result.Instances);
        Assert.AreNotEqual(result.Instances.ElementAt(0).ProductFingerprint, result.Instances.ElementAt(1).ProductFingerprint);
    }
    private static RawInstallEvidence Evidence(string rawId, string scope, string architecture, string version, string channel, string name, string identity = "Contoso.App") => new("registry", identity, name, "Contoso", version, scope, architecture, channel, "Unknown", rawId);
}
