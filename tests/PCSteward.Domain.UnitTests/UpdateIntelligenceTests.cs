using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

[TestClass]
public sealed class UpdateIntelligenceTests
{
    [TestMethod]
    public void ClassifiesCurrentVerifiedReviewAndUnmanagedWithoutCreatingExecutionAuthority()
    {
        ProductMatchProfile current = Product("current", "Contoso.Current", "1.2.0"); ProductMatchProfile verified = Product("verified", "Contoso.Verified", "1.0.0"); ProductMatchProfile review = Product("review", "Contoso.Review", "1.0.0", "x64", "Stable"); ProductMatchProfile unmanaged = Product("unmanaged", null, "1.0.0");
        SourceSnapshot source = Source([Package("Contoso.Current", "1.2.0"), Package("Contoso.Verified", "1.1.0"), Package("Contoso.Review", "1.1.0", "x86")]);
        UpdateIntelligenceSummary result = UpdateIntelligence.Assess([current, verified, review, unmanaged], [source]);
        Assert.AreEqual(UpdateIntelligenceStatus.Current, result.Items.Single(item => item.StableProductKey == current.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, result.Items.Single(item => item.StableProductKey == verified.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, result.Items.Single(item => item.StableProductKey == review.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.Unmanaged, result.Items.Single(item => item.StableProductKey == unmanaged.StableProductKey).Status);
        Assert.AreEqual(1, result.Counts[UpdateIntelligenceStatus.VerifiedUpdate]);
    }

    [TestMethod]
    public void PinnedAndIgnoredOverrideCandidateStatusAndUnavailableWithoutCacheIsAttentionNeeded()
    {
        ProductMatchProfile pinned = Product("pinned", "Contoso.Pinned", "1.0.0"); ProductMatchProfile ignored = Product("ignored", "Contoso.Ignored", "1.0.0"); ProductMatchProfile unavailable = Product("unavailable", null, "1.0.0");
        UpdateIntelligenceSummary result = UpdateIntelligence.Assess([pinned, ignored, unavailable], [new("winget", null, null, SourceSnapshotState.Unavailable, [], ["source:timeout"])], policies: new Dictionary<string, ProductUpdatePolicy> { [pinned.StableProductKey] = new(true), [ignored.StableProductKey] = new(false, true) });
        Assert.AreEqual(UpdateIntelligenceStatus.Pinned, result.Items.Single(item => item.StableProductKey == pinned.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.Ignored, result.Items.Single(item => item.StableProductKey == ignored.StableProductKey).Status);
        Assert.AreEqual(UpdateIntelligenceStatus.FailedUnavailable, result.Items.Single(item => item.StableProductKey == unavailable.StableProductKey).Status);
        Assert.HasCount(1, result.AttentionNeeded);
    }

    [TestMethod]
    public void ExpiredIgnoreIsReappliedAsNormalSafeMatchingRatherThanRemainingHidden()
    {
        ProductMatchProfile product = Product("expired", "Contoso.Expired", "1.0.0");
        UpdateIntelligenceSummary result = UpdateIntelligence.Assess([product], [Source([Package("Contoso.Expired", "1.1.0")])], policies: new Dictionary<string, ProductUpdatePolicy> { [product.StableProductKey] = new(false, true, null, DateTimeOffset.UtcNow.AddMinutes(-1)) });
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, result.Items.Single().Status);
    }

    [TestMethod]
    public void DifferentTrustedSourceVersionsStayVisibleAndBlockAutomaticSelection()
    {
        ProductMatchProfile product = Product("disagreement", "Contoso.Disagreement", "1.0.0");
        SourceSnapshot winget = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.Disagreement", "disagreement", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);
        SourceSnapshot store = new("msstore", "v2", DateTimeOffset.UnixEpoch.AddMinutes(1), SourceSnapshotState.Cached, [new SourcePackageRecord("msstore", "Contoso.Disagreement.Store", "disagreement", "Contoso", "1.0.0", "1.2.0", "Machine", "x64", "Stable")], ["source:stale"]);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [winget, store]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable);
        CollectionAssert.Contains(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.SourceDisagreement);
        SourceCandidateObservation[] observations = item.SourceCandidates!.ToArray();
        Assert.HasCount(2, observations);
        Assert.IsTrue(observations.Any(candidate => candidate.SourceId == "winget" && candidate.CandidateVersion == "1.1.0"));
        Assert.IsTrue(observations.Any(candidate => candidate.SourceId == "msstore" && candidate.CandidateVersion == "1.2.0"));
    }

    [TestMethod]
    public void SourceDisagreementNeverClassifiesAnOtherwiseCurrentMappingAsCurrent()
    {
        ProductMatchProfile product = Product("current-disagreement", "Contoso.CurrentDisagreement", "1.0.0");
        SourceSnapshot winget = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.CurrentDisagreement", "current-disagreement", "Contoso", "1.0.0", "1.0.0", "Machine", "x64", "Stable")], []);
        SourceSnapshot store = new("msstore", "v2", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("msstore", "Contoso.CurrentDisagreement.Store", "current-disagreement", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);

        UpdateIntelligenceSummary result = UpdateIntelligence.Assess([product], [winget, store]);
        UpdateIntelligenceItem item = result.Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status);
        Assert.IsTrue(result.AttentionNeeded.Contains(item));
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable);
        CollectionAssert.Contains(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.SourceDisagreement);
    }

    private static ProductMatchProfile Product(string key, string? packageId, string version, string architecture = "x64", string channel = "Stable") => new("product:" + key, key, packageId, null, null, "Contoso", version, "Machine", architecture, channel, InstalledSourceId: packageId is null ? null : "winget");
    private static SourcePackageRecord Package(string id, string version, string architecture = "x64") => new("winget", id, id, "Contoso", "1.0.0", version, "Machine", architecture, "Stable");
    private static SourceSnapshot Source(IReadOnlyCollection<SourcePackageRecord> packages) => new("winget", "v1.0", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, packages, []);
}
