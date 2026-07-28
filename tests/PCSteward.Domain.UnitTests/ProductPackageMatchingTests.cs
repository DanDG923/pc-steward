using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class ProductPackageMatchingTests
{
    [TestMethod]
    public void ExactProviderPackageIdProducesAnActionableAutomaticMapping()
    {
        ProductPackageMatchResult result = ProductPackageMatching.Resolve(Product(), [Candidate()]);

        Assert.IsNotNull(result.Mapping);
        Assert.AreEqual("Contoso.Widget", result.Mapping.PackageId);
        Assert.AreEqual(MappingConfidence.High, result.Mapping.Confidence);
        Assert.AreEqual(MappingOrigin.Automatic, result.Mapping.Origin);
        Assert.IsTrue(result.Mapping.IsActionable);
        Assert.AreEqual(VersionComparisonOutcome.Newer, result.Mapping.VersionComparison.Outcome);
    }

    [TestMethod]
    public void CuratedControlsHaveZeroFalsePositiveActionableMappings()
    {
        PackageMatchCandidate[] fixtures =
        [
            Candidate(),
            Candidate(packageId: "Fabrikam.Widget", displayName: "Contoso Widget", publisher: "Fabrikam", version: "1.2.0"),
            Candidate(architecture: "x86"),
            Candidate(channel: "Beta"),
            Candidate(version: "2026-R2"),
            Candidate(packageId: "Contoso.Widget.Preview", displayName: "Contoso Widget Preview", version: "1.2.0")
        ];

        PackageMapping?[] mappings = fixtures.Select(fixture => ProductPackageMatching.Resolve(Product(), [fixture]).Mapping).ToArray();

        Assert.AreEqual("Contoso.Widget", mappings[0]?.PackageId);
        Assert.IsTrue(mappings[0]?.IsActionable);
        Assert.AreEqual(1, mappings.Count(mapping => mapping?.IsActionable == true));
    }

    [TestMethod]
    public void ProductAndPublisherAliasesAreReviewOnlyWithoutStableIdentity()
    {
        ProductMatchProfile product = Product(providerPackageId: null, aliases: ["Contoso Widget"]);
        PackageMatchCandidate candidate = Candidate(packageId: "Contoso.Widget", aliases: ["Contoso Widget"]);

        ProductPackageMatchResult result = ProductPackageMatching.Resolve(product, [candidate]);

        Assert.IsNotNull(result.Mapping);
        Assert.AreEqual(MappingConfidence.Medium, result.Mapping.Confidence);
        Assert.IsFalse(result.Mapping.IsActionable);
    }

    [TestMethod]
    [DataRow("Fabrikam", MappingReviewReason.PublisherConflict)]
    [DataRow(null, MappingReviewReason.AmbiguousPublisher)]
    public void NearNameAndAmbiguousPublisherNeverBecomeActionable(string? publisher, MappingReviewReason reason)
    {
        ProductMatchProfile product = Product(providerPackageId: null);
        ProductPackageMatchResult result = ProductPackageMatching.Resolve(product, [Candidate(publisher: publisher)]);

        Assert.IsNotNull(result.Mapping);
        CollectionAssert.Contains(result.Mapping.ReviewReasons.ToArray(), reason);
        Assert.IsFalse(result.Mapping.IsActionable);
    }

    [TestMethod]
    [DataRow("x86", "Stable", "1.2.0", MappingReviewReason.ArchitectureConflict)]
    [DataRow("x64", "Beta", "1.2.0", MappingReviewReason.ChannelConflict)]
    [DataRow("x64", "Stable", "2026-R2", MappingReviewReason.IncomparableVersion)]
    public void IncompatibleOrIncomparableCandidatesNeverBecomeActionable(string architecture, string channel, string version, MappingReviewReason reason)
    {
        ProductPackageMatchResult result = ProductPackageMatching.Resolve(Product(), [Candidate(architecture: architecture, channel: channel, version: version)]);

        Assert.IsNotNull(result.Mapping);
        CollectionAssert.Contains(result.Mapping.ReviewReasons.ToArray(), reason);
        Assert.IsFalse(result.Mapping.IsActionable);
    }

    [TestMethod]
    public void SupportedTrustedUserMappingIsActionableAndRemovingItRestoresReviewOnlyMatching()
    {
        ProductMatchProfile product = Product(providerPackageId: null, aliases: ["Contoso Widget"]);
        PackageMatchCandidate candidate = Candidate(aliases: ["Contoso Widget"]);
        DeclaredPackageMapping manual = new(product.StableProductKey, candidate.SourceId, candidate.PackageId, MappingOrigin.UserConfirmed);

        ProductPackageMatchResult mapped = ProductPackageMatching.Resolve(product, [candidate], [manual], ["winget"]);
        ProductPackageMatchResult removed = ProductPackageMatching.Resolve(product, [candidate], [], ["winget"]);

        Assert.IsNotNull(mapped.Mapping);
        Assert.AreEqual(MappingOrigin.UserConfirmed, mapped.Mapping.Origin);
        Assert.IsTrue(mapped.Mapping.IsActionable);
        Assert.IsNotNull(removed.Mapping);
        Assert.AreEqual(MappingConfidence.Medium, removed.Mapping.Confidence);
        Assert.IsFalse(removed.Mapping.IsActionable);
    }

    [TestMethod]
    public void UnsupportedOrUntrustedManualMappingIsRejected()
    {
        PackageMatchCandidate candidate = Candidate(trusted: false);
        DeclaredPackageMapping mapping = new("product:contoso-widget", "winget", "Contoso.Widget", MappingOrigin.UserConfirmed);

        ManualMappingValidation validation = ProductPackageMatching.ValidateManualMapping(mapping, candidate, ["winget"]);
        ProductPackageMatchResult result = ProductPackageMatching.Resolve(Product(providerPackageId: null), [candidate], [mapping], ["winget"]);

        Assert.IsFalse(validation.IsValid);
        Assert.IsNotNull(result.Mapping);
        CollectionAssert.Contains(result.Mapping.ReviewReasons.ToArray(), MappingReviewReason.UnsupportedManualMapping);
        Assert.IsFalse(result.Mapping.IsActionable);
    }

    [TestMethod]
    public void MultipleEquallyStrongMappingsRequireReview()
    {
        PackageMatchCandidate providerSupplied = Candidate(packageId: "Contoso.Widget.Second") with { ProviderSuppliedStableProductKey = "product:contoso-widget" };
        ProductPackageMatchResult result = ProductPackageMatching.Resolve(Product(), [Candidate(), providerSupplied], [], ["winget"]);

        Assert.IsNull(result.Mapping);
        CollectionAssert.Contains(result.ReviewReasons.ToArray(), MappingReviewReason.AmbiguousCandidate);
    }

    [TestMethod]
    public void SamePackageIdFromAnotherTrustedSourceIsReviewOnly()
    {
        ProductPackageMatchResult result = ProductPackageMatching.Resolve(Product(sourceId: "msstore"), [Candidate(sourceId: "winget")]);

        Assert.IsNotNull(result.Mapping);
        Assert.AreEqual(MappingConfidence.Low, result.Mapping.Confidence);
        CollectionAssert.Contains(result.Mapping.ReviewReasons.ToArray(), MappingReviewReason.SourceConflict);
        Assert.IsFalse(result.Mapping.IsActionable);
    }

    private static ProductMatchProfile Product(string? providerPackageId = "Contoso.Widget", IReadOnlyCollection<string>? aliases = null, string? sourceId = "winget") => new("product:contoso-widget", "Contoso Widget", providerPackageId, null, null, "Contoso Ltd", "1.0.0", "Machine", "x64", "Stable", aliases, ["Contoso Ltd"], sourceId);
    private static PackageMatchCandidate Candidate(string packageId = "Contoso.Widget", string displayName = "Contoso Widget", string? publisher = "Contoso Ltd", string version = "1.2.0", string scope = "Machine", string architecture = "x64", string channel = "Stable", bool trusted = true, IReadOnlyCollection<string>? aliases = null, string sourceId = "winget") => new(sourceId, packageId, displayName, null, null, publisher, version, scope, architecture, channel, trusted, ProductAliases: aliases, PublisherAliases: publisher is null ? null : [publisher]);
}
