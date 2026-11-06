using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// Owner decision 2026-11-02 (FR-POL-01–05; Task 73 Phase 6): Skip this version is deterministic, applies only while the candidate
/// equals the skipped version, and can only remove actionability.
/// </summary>
[TestClass]
public sealed class SkipVersionRuleTests
{
    [TestMethod]
    [TestCategory("FR-POL-01")]
    [DataRow("1.1.0", "1.1.0", true, DisplayName = "equal")]
    [DataRow(" 1.1.0 ", "1.1.0", true, DisplayName = "equal after trimming")]
    [DataRow("1.1", "1.1.0", true, DisplayName = "equal under the domain comparer")]
    [DataRow("2024.1-custom", "2024.1-CUSTOM", true, DisplayName = "identical incomparable string the user saw")]
    [DataRow("1.1.0", "1.2.0", false, DisplayName = "newer candidate is not skipped")]
    [DataRow("1.1.0", "1.0.9", false, DisplayName = "older candidate is not skipped")]
    [DataRow("1.1.0", "1.1.0-custom", false, DisplayName = "incomparable candidate is not skipped")]
    [DataRow("1.1.0-beta", "1.1.0", false, DisplayName = "stable after a skipped prerelease is newer")]
    [DataRow(null, "1.1.0", false, DisplayName = "no skip")]
    [DataRow("1.1.0", null, false, DisplayName = "no candidate")]
    [DataRow("   ", "1.1.0", false, DisplayName = "blank skip")]
    public void AppliesOnlyWhileTheCandidateEqualsTheSkippedVersion(string? skipped, string? candidate, bool expected) =>
        Assert.AreEqual(expected, SkipVersionRule.Applies(skipped, candidate));

    [TestMethod]
    [TestCategory("FR-POL-01")]
    public void EqualCandidateIsSkippedAndNeverActionable()
    {
        ProductMatchProfile product = Product("widget", "Contoso.Widget", "1.0.0");
        UpdateIntelligenceItem item = Assess(product, "1.1.0", new(SkippedVersion: "1.1.0"));

        Assert.AreEqual(UpdateIntelligenceStatus.Skipped, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable, "a skipped candidate is never actionable");
        Assert.AreEqual("1.1.0", item.Mapping.CandidateVersion, "the skipped version stays visible");
        StringAssert.Contains(item.Explanation, "skip version 1.1.0");
    }

    [TestMethod]
    [TestCategory("FR-POL-05")]
    public void NewerCandidateReturnsToReadyAutomatically()
    {
        ProductMatchProfile product = Product("widget", "Contoso.Widget", "1.0.0");
        UpdateIntelligenceItem item = Assess(product, "1.2.0", new(SkippedVersion: "1.1.0"));

        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, item.Status);
        Assert.IsTrue(item.Mapping!.IsActionable);
    }

    [TestMethod]
    [TestCategory("FR-POL-01")]
    public void OlderOrIncomparableCandidateIsNotSkippedAndKeepsExistingRules()
    {
        ProductMatchProfile product = Product("widget", "Contoso.Widget", "1.0.0");
        UpdateIntelligenceItem older = Assess(product, "1.0.5", new(SkippedVersion: "1.1.0"));
        UpdateIntelligenceItem incomparable = Assess(product, "1.1.0-custom", new(SkippedVersion: "1.1.0"));

        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, older.Status, "an older-than-skipped but newer-than-installed candidate is assessed normally");
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, incomparable.Status, "incomparable stays review-required, never skipped or actionable");
        Assert.IsFalse(incomparable.Mapping!.IsActionable);
    }

    [TestMethod]
    [TestCategory("FR-POL-01")]
    public void ReviewRequiredCandidateEqualToTheSkipIsHiddenButNeverMadeActionable()
    {
        ProductMatchProfile product = Product("review", "Contoso.Review", "1.0.0", "x64");
        UpdateIntelligenceSummary result = UpdateIntelligence.Assess([product], [Source([Package("Contoso.Review", "1.1.0", "x86")])],
            policies: new Dictionary<string, ProductUpdatePolicy> { [product.StableProductKey] = new(SkippedVersion: "1.1.0") });

        UpdateIntelligenceItem item = result.Items.Single();
        Assert.AreEqual(UpdateIntelligenceStatus.Skipped, item.Status);
        Assert.IsFalse(item.Mapping!.IsActionable);
        Assert.AreEqual(1, result.Counts[UpdateIntelligenceStatus.Skipped]);
        Assert.IsEmpty(result.AttentionNeeded);
    }

    [TestMethod]
    [TestCategory("FR-POL-01")]
    public void IgnoreAndPinTakePrecedenceOverASkip()
    {
        ProductMatchProfile ignored = Product("ignored", "Contoso.Ignored", "1.0.0");
        ProductMatchProfile pinned = Product("pinned", "Contoso.Pinned", "1.0.0");
        UpdateIntelligenceSummary result = UpdateIntelligence.Assess([ignored, pinned],
            [Source([Package("Contoso.Ignored", "1.1.0"), Package("Contoso.Pinned", "1.1.0")])],
            policies: new Dictionary<string, ProductUpdatePolicy>
            {
                [ignored.StableProductKey] = new(IsIgnored: true, SkippedVersion: "1.1.0"),
                [pinned.StableProductKey] = new(IsPinned: true, SkippedVersion: "1.1.0")
            });

        Assert.AreEqual(UpdateIntelligenceStatus.Ignored, result.Items.Single(item => item.StableProductKey == ignored.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.Pinned, result.Items.Single(item => item.StableProductKey == pinned.StableProductKey).Status);
    }

    [TestMethod]
    public void TheSkippedStatusIsAppendedSoExistingOrdinalsAreUnchanged()
    {
        Assert.AreEqual(6, (int)UpdateIntelligenceStatus.FailedUnavailable);
        Assert.AreEqual(7, (int)UpdateIntelligenceStatus.Skipped);
    }

    private static UpdateIntelligenceItem Assess(ProductMatchProfile product, string candidate, ProductUpdatePolicy policy) =>
        UpdateIntelligence.Assess([product], [Source([Package(product.ProviderPackageId!, candidate)])],
            policies: new Dictionary<string, ProductUpdatePolicy> { [product.StableProductKey] = policy }).Items.Single();

    private static ProductMatchProfile Product(string key, string packageId, string version, string architecture = "x64") =>
        new("product:" + key, key, packageId, null, null, "Contoso", version, "Machine", architecture, "Stable", InstalledSourceId: "winget");
    private static SourcePackageRecord Package(string id, string version, string architecture = "x64") => new("winget", id, id, "Contoso", "1.0.0", version, "Machine", architecture, "Stable");
    private static SourceSnapshot Source(IReadOnlyCollection<SourcePackageRecord> packages) => new("winget", "v1.0", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, packages, []);
}
