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
    public void UnavailableUnrelatedSourceDoesNotHideAnInstalledWingetProduct()
    {
        ProductMatchProfile firefox = Product("firefox", "Mozilla.Firefox", "152.0.6");
        SourceSnapshot winget = Source([]);
        SourceSnapshot store = new("msstore", null, null, SourceSnapshotState.Unavailable, [], ["source:unavailable"]);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([firefox], [winget, store]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.Unmanaged, item.Status);
        Assert.AreNotEqual(UpdateIntelligenceStatus.FailedUnavailable, item.Status);
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
    public void RepositoryIdentityWithoutAnAvailableVersionIsUnavailableNotAUserDecision()
    {
        ProductMatchProfile product = Product("availability-unknown", "Contoso.AvailabilityUnknown", "1.0.0");
        SourceSnapshot source = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.AvailabilityUnknown", "Availability unknown", "Contoso", "1.0.0", null, "Machine", "x64", "Stable")], ["winget:update-check-unavailable"]);

        UpdateIntelligenceSummary summary = UpdateIntelligence.Assess([product], [source]);
        UpdateIntelligenceItem item = summary.Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.FailedUnavailable, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable);
        StringAssert.Contains(item.Explanation, "not a decision for you");
        Assert.AreEqual(0, summary.Counts[UpdateIntelligenceStatus.ReviewRequired]);
        Assert.AreEqual(1, summary.Counts[UpdateIntelligenceStatus.FailedUnavailable]);
    }

    [TestMethod]
    public void AmbiguousPublisherWithAnEqualVersionIsNeitherCurrentNorAReviewItem()
    {
        ProductMatchProfile product = Product("ambiguous-current", null, "1.0.0");
        SourceSnapshot source = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.AmbiguousCurrent", "ambiguous-current", null, "1.0.0", "1.0.0", "Machine", "x64", "Stable")], []);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [source]).Items.Single();

        // Nothing newer exists, so there is nothing to review; the unconfirmed identity is not presented as current either.
        Assert.AreEqual(UpdateIntelligenceStatus.Unmanaged, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable);
        CollectionAssert.Contains(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.AmbiguousPublisher);
        CollectionAssert.DoesNotContain(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.CandidateIsCurrent);
        StringAssert.Contains(item.Explanation, "nothing to update", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(item.Explanation, "hasn't been confirmed", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void AmbiguousPublisherWithANewerVersionStaysAReviewItem()
    {
        ProductMatchProfile product = Product("ambiguous-newer", null, "1.0.0");
        SourceSnapshot source = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.AmbiguousNewer", "ambiguous-newer", null, "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, UpdateIntelligence.Assess([product], [source]).Items.Single().Status);
    }

    [TestMethod]
    public void SameVersionWithoutInstallerEvidenceIsCurrent()
    {
        // WinGet's installed list gives no scope, architecture or channel for a package without an update; nothing would run.
        ProductMatchProfile product = new("product:same", "same", "Contoso.Same", null, null, null, "2.0.0", "Unknown", "Unknown", null, InstalledSourceId: "winget", HasConcreteLocalEvidence: false);
        SourceSnapshot source = Source([new SourcePackageRecord("winget", "Contoso.Same", "same", null, "2.0.0", "2.0.0", "Unknown", "Unknown", null)]);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [source]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.Current, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable);
        CollectionAssert.Contains(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.MissingArchitectureEvidence);
    }

    [TestMethod]
    public void NewerVersionWithoutInstallerEvidenceStaysAReviewItem()
    {
        ProductMatchProfile product = new("product:newer", "newer", "Contoso.Newer", null, null, null, "2.0.0", "Unknown", "Unknown", null, InstalledSourceId: "winget", HasConcreteLocalEvidence: false);
        SourceSnapshot source = Source([new SourcePackageRecord("winget", "Contoso.Newer", "newer", null, "2.0.0", "2.1.0", "Unknown", "Unknown", null)]);

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, UpdateIntelligence.Assess([product], [source]).Items.Single().Status);
    }

    [TestMethod]
    public void SameVersionWithAnArchitectureConflictStaysAReviewItem()
    {
        ProductMatchProfile product = Product("same-conflict", "Contoso.SameConflict", "1.0.0", "x86");
        SourceSnapshot source = Source([new SourcePackageRecord("winget", "Contoso.SameConflict", "same-conflict", "Contoso", "1.0.0", "1.0.0", "Machine", "x64", "Stable")]);

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, UpdateIntelligence.Assess([product], [source]).Items.Single().Status);
    }

    [TestMethod]
    public void WinGetsOwnRecordOfAnAppItLinksToAnUninstallEntryGetsNoRowOfItsOwn()
    {
        ProductMatchProfile entry = new("registry:entry", "Chat", null, null, null, "Contoso", "1.0.0", "User", "x64", null);
        ProductMatchProfile wingetRecord = new("winget:Contoso.Chat", "Chat", "Contoso.Chat", null, null, null, "1.0.0", "Unknown", "Unknown", null, InstalledSourceId: "winget", HasConcreteLocalEvidence: false);
        SourceSnapshot source = Source([new SourcePackageRecord("winget", "Contoso.Chat", "Chat", "Contoso", "1.0.0", "1.1.0", "User", "x64", "Stable", ProviderSuppliedStableProductKey: "REGISTRY:ENTRY")]);

        UpdateIntelligenceSummary summary = UpdateIntelligence.Assess([entry, wingetRecord], [source]);

        UpdateIntelligenceItem item = summary.Items.Single();
        Assert.AreEqual(entry.StableProductKey, item.StableProductKey);
        Assert.AreEqual(UpdateIntelligenceStatus.VerifiedUpdate, item.Status);
        Assert.AreEqual(0, summary.Counts[UpdateIntelligenceStatus.ReviewRequired]);
    }

    [TestMethod]
    public void WinGetsOwnRecordKeepsItsRowWithoutALinkOrWhenItIsTheLinkedProduct()
    {
        ProductMatchProfile entry = new("registry:entry", "Chat", null, null, null, "Contoso", "1.0.0", "User", "x64", null);
        ProductMatchProfile unlinked = new("winget:Contoso.Chat", "Chat", "Contoso.Chat", null, null, null, "1.0.0", "Unknown", "Unknown", null, InstalledSourceId: "winget", HasConcreteLocalEvidence: false);
        SourceSnapshot noLink = Source([new SourcePackageRecord("winget", "Contoso.Chat", "Chat", "Contoso", "1.0.0", "1.1.0", "User", "x64", "Stable")]);
        // A link naming only a product with no uninstall entry or MSIX package of its own hides nothing.
        SourceSnapshot linkToRecordOnly = Source([new SourcePackageRecord("winget", "Contoso.Chat", "Chat", "Contoso", "1.0.0", "1.1.0", "User", "x64", "Stable", ProviderSuppliedStableProductKey: unlinked.StableProductKey)]);

        Assert.IsTrue(UpdateIntelligence.Assess([entry, unlinked], [noLink]).Items.Any(item => item.StableProductKey == unlinked.StableProductKey));
        Assert.IsTrue(UpdateIntelligence.Assess([unlinked], [linkToRecordOnly]).Items.Any(item => item.StableProductKey == unlinked.StableProductKey));
    }

    [TestMethod]
    public void UserConfirmedExactPackageMappingMakesAnEqualVersionCurrent()
    {
        ProductMatchProfile product = Product("confirmed-current", null, "1.0.0");
        SourceSnapshot source = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.ConfirmedCurrent", "confirmed-current", null, "1.0.0", "1.0.0", "Machine", "x64", "Stable")], []);
        DeclaredPackageMapping mapping = new(product.StableProductKey, "winget", "Contoso.ConfirmedCurrent", MappingOrigin.UserConfirmed);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [source], [mapping]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.Current, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.AreEqual(MappingOrigin.UserConfirmed, item.Mapping.Origin);
        Assert.AreEqual(MappingConfidence.High, item.Mapping.Confidence);
    }

    [TestMethod]
    public void UserConfirmedMappingUsesTheSourceCurrentVersionWhenRegistryVersionIsIncompatible()
    {
        ProductMatchProfile product = Product("confirmed-registry-current", null, "80.40.55332");
        SourceSnapshot source = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.ConfirmedRegistry", "confirmed-registry-current", null, "10.0.10", "10.0.10", "Machine", "x64", "Stable")], []);
        DeclaredPackageMapping mapping = new(product.StableProductKey, "winget", "Contoso.ConfirmedRegistry", MappingOrigin.UserConfirmed);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [source], [mapping]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.Current, item.Status);
        Assert.IsNotNull(item.Mapping);
        Assert.IsFalse(item.Mapping.IsActionable);
        Assert.IsEmpty(item.Mapping.ReviewReasons);
        StringAssert.Contains(item.Explanation, "source reports", StringComparison.OrdinalIgnoreCase);
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

    [TestMethod]
    public void ScopeOnlySourceDisagreementBlocksAutomaticSelection()
    {
        ProductMatchProfile product = Product("scope-disagreement", "Contoso.ScopeDisagreement", "1.0.0");
        SourceSnapshot machine = new("winget", "v1", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("winget", "Contoso.ScopeDisagreement", "scope-disagreement", "Contoso", "1.0.0", "1.1.0", "Machine", "x64", "Stable")], []);
        SourceSnapshot user = new("msstore", "v2", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, [new SourcePackageRecord("msstore", "Contoso.ScopeDisagreement.Store", "scope-disagreement", "Contoso", "1.0.0", "1.1.0", "User", "x64", "Stable")], []);

        UpdateIntelligenceItem item = UpdateIntelligence.Assess([product], [machine, user]).Items.Single();

        Assert.AreEqual(UpdateIntelligenceStatus.ReviewRequired, item.Status);
        Assert.IsNotNull(item.Mapping);
        CollectionAssert.Contains(item.Mapping.ReviewReasons.ToArray(), MappingReviewReason.SourceDisagreement);
    }

    private static ProductMatchProfile Product(string key, string? packageId, string version, string architecture = "x64", string channel = "Stable") => new("product:" + key, key, packageId, null, null, "Contoso", version, "Machine", architecture, channel, InstalledSourceId: packageId is null ? null : "winget");
    private static SourcePackageRecord Package(string id, string version, string architecture = "x64") => new("winget", id, id, "Contoso", "1.0.0", version, "Machine", architecture, "Stable");
    private static SourceSnapshot Source(IReadOnlyCollection<SourcePackageRecord> packages) => new("winget", "v1.0", DateTimeOffset.UnixEpoch, SourceSnapshotState.Live, packages, []);
}
