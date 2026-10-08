using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class CatalogOriginAndSkipVersionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [TestMethod]
    public void CatalogOriginIsValidatedLikeAUserConfirmedMapping()
    {
        PackageMatchCandidate candidate = Candidate("winget", "Igor.SevenZip", "24.09");
        Assert.IsTrue(ProductPackageMatching.ValidateManualMapping(new("registry:7zip", "winget", "Igor.SevenZip", MappingOrigin.Catalog), candidate, ["winget"]).IsValid);
        Assert.IsFalse(ProductPackageMatching.ValidateManualMapping(new("registry:7zip", "winget", "Igor.SevenZip", MappingOrigin.Catalog), candidate, ["chocolatey-community"]).IsValid, "Unsupported source");
        Assert.IsFalse(ProductPackageMatching.ValidateManualMapping(new("registry:7zip", "winget", "Igor.Other", MappingOrigin.Catalog), candidate, ["winget"]).IsValid, "Different package");
        Assert.IsFalse(ProductPackageMatching.ValidateManualMapping(new("registry:7zip", "winget", "Igor.SevenZip", MappingOrigin.Automatic), candidate, ["winget"]).IsValid, "Automatic origin is not declared authority");
    }

    [TestMethod]
    public void CatalogMappingMakesAnAliasOnlyRegistryProductActionableWhenTheExactCandidateIsNewer()
    {
        ProductMatchProfile product = Registry("registry:7zip", "7-Zip 24.08 (x64)", "24.08");
        SourceSnapshot source = Source("winget", Package("winget", "Igor.SevenZip", "7-Zip", "24.09"));

        UpdateIntelligenceItem withoutCatalog = UpdateIntelligence.Assess([product], [source]).Items.Single();
        UpdateIntelligenceItem withCatalog = UpdateIntelligence.Assess([product], [source], [new(product.StableProductKey, "winget", "Igor.SevenZip", MappingOrigin.Catalog)]).Items.Single();

        Assert.AreNotEqual(UpdateIntelligenceStatus.VerifiedUpdate, withoutCatalog.Status);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, withCatalog.Status);
        Assert.AreEqual(MappingOrigin.Catalog, withCatalog.Mapping!.Origin);
        Assert.IsTrue(withCatalog.Mapping.IsActionable);
    }

    [TestMethod]
    public void CatalogMappingStillRequiresCompatibilityAndANewerVersion()
    {
        ProductMatchProfile product = Registry("registry:7zip", "7-Zip", "24.09", architecture: "x86");
        DeclaredPackageMapping mapping = new(product.StableProductKey, "winget", "Igor.SevenZip", MappingOrigin.Catalog);

        UpdateIntelligenceItem conflict = UpdateIntelligence.Assess([product], [Source("winget", Package("winget", "Igor.SevenZip", "7-Zip", "25.00"))], [mapping]).Items.Single();
        UpdateIntelligenceItem older = UpdateIntelligence.Assess([product with { Architecture = "x64" }], [Source("winget", Package("winget", "Igor.SevenZip", "7-Zip", "24.01"))], [mapping]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, conflict.Status);
        CollectionAssert.Contains(conflict.Mapping!.ReviewReasons.ToArray(), MappingReviewReason.ArchitectureConflict);
        Assert.IsFalse(older.Mapping!.IsActionable);
    }

    [TestMethod]
    public void SkippedVersionIsNotActionableButANewerVersionIsOfferedAgain()
    {
        ProductMatchProfile product = Registry("registry:7zip", "7-Zip", "24.08");
        DeclaredPackageMapping mapping = new(product.StableProductKey, "winget", "Igor.SevenZip", MappingOrigin.Catalog);
        Dictionary<string, ProductUpdatePolicy> policies = new() { [product.StableProductKey] = new(SkippedVersion: "24.09") };

        UpdateIntelligenceSummary skipped = UpdateIntelligence.Assess([product], [Source("winget", Package("winget", "Igor.SevenZip", "7-Zip", "24.09"))], [mapping], policies);
        UpdateIntelligenceItem newer = UpdateIntelligence.Assess([product], [Source("winget", Package("winget", "Igor.SevenZip", "7-Zip", "25.01"))], [mapping], policies).Items.Single();

        UpdateIntelligenceItem item = skipped.Items.Single();
        Assert.AreEqual(UpdateIntelligenceStatus.Skipped, item.Status);
        Assert.IsFalse(item.Mapping!.IsActionable);
        StringAssert.Contains(item.Explanation, "24.09");
        Assert.AreEqual(1, skipped.Counts[UpdateIntelligenceStatus.Skipped]);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, newer.Status);
    }

    [TestMethod]
    public void SkippedVersionNeverOverridesPinOrIgnore()
    {
        ProductMatchProfile product = Registry("registry:7zip", "7-Zip", "24.08");
        Dictionary<string, ProductUpdatePolicy> policies = new() { [product.StableProductKey] = new(IsPinned: true, SkippedVersion: "24.09") };
        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [Source("winget", Package("winget", "Igor.SevenZip", "7-Zip", "24.09"))], [new(product.StableProductKey, "winget", "Igor.SevenZip", MappingOrigin.Catalog)], policies).Items.Single();
        Assert.AreEqual(UpdateIntelligenceStatus.Pinned, item.Status);
    }

    private static ProductMatchProfile Registry(string key, string name, string version, string architecture = "x64") =>
        new(key, name, null, null, null, "Igor Pavlov", version, "Machine", architecture, "Stable");

    private static PackageMatchCandidate Candidate(string source, string packageId, string version) =>
        new(source, packageId, "7-Zip", null, null, "Igor Pavlov", version, "Machine", "x64", "Stable", true);

    private static SourcePackageRecord Package(string source, string packageId, string name, string available) =>
        new(source, packageId, name, "Unrelated Publisher Ltd", null, available, "Machine", "x64", "Stable");

    private static SourceSnapshot Source(string sourceId, params SourcePackageRecord[] packages) =>
        new(sourceId, "1.0", Now, SourceSnapshotState.Live, packages, []);
}
