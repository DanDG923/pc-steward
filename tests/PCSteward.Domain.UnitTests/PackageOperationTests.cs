using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>Task 48: FR-INS-01/03, FR-BAT-04/05, AC-V3-05, ADR-017 v0.3.</summary>
[TestClass]
public sealed class PackageOperationTests
{
    private static readonly DateTimeOffset ApprovedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] ExpectedExecutionOrder = ["pcs:b", "winget:d", "pcs:a", "pcs:c"];
    private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    [DataRow("Notepad++.Notepad++")]
    [DataRow("Microsoft.VCRedist.2015+.x64")]
    [DataRow("Vendor_Name.Some_App")]
    public void WinGetIdentifiersWithPlusAndUnderscoreAreAccepted(string packageId) =>
        Assert.AreEqual(packageId, Upgrade(packageId: packageId).PackageId);

    [TestMethod]
    [DataRow("+Leading.Plus")]
    [DataRow("_Leading.Underscore")]
    [DataRow("Vendor.App --override x")]
    [DataRow("Vendor.App;calc")]
    [DataRow("Vendor/App")]
    [DataRow("Vendor.App\"")]
    public void HostileIdentifiersRemainRejected(string packageId) =>
        Assert.ThrowsExactly<ArgumentException>(() => Upgrade(packageId: packageId));

    [TestMethod]
    public void ChocolateyIdentifiersAcceptUnderscoreButNeverPlus()
    {
        Assert.AreEqual("some_package", Chocolatey("some_package").PackageId);
        Assert.ThrowsExactly<ArgumentException>(() => Chocolatey("notepad++"));
    }

    [TestMethod]
    public void ProviderSourcePairsAreAllowlisted() =>
        Assert.ThrowsExactly<ArgumentException>(() => new UpdateJobSelection("pcs:app", UpdateProvider.Chocolatey, UpdateSource.WinGet, "app", "1.0", "2.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "evidence-v1"));

    [TestMethod]
    public void PlainUpgradeDigestIsUnchangedFromVersionTwo()
    {
        // Persisted v0.2 jobs must keep validating after the v0.3 canonical-text extension.
        UpdateJobSelection selection = Upgrade();
        Guid id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        Guid correlation = Guid.Parse("22222222-2222-2222-2222-222222222222");
        string legacyCanonical = string.Join('\n',
            id.ToString("D", CultureInfo.InvariantCulture), correlation.ToString("D", CultureInfo.InvariantCulture),
            "winget:contoso.widget", "WinGet", "WinGet", "Contoso.Widget", "1.0.0", "1.1.0", "User", "X64", "Stable", "mapping-v1",
            ApprovedAt.ToString("O", CultureInfo.InvariantCulture));
        string legacyDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(legacyCanonical)));

        ImmutableUpdateJob restored = ImmutableUpdateJob.Restore(id, correlation, selection, ApprovedAt, legacyDigest);

        Assert.IsTrue(selection.IsPlainUpgrade);
        Assert.AreEqual(legacyDigest, restored.Digest);
    }

    [TestMethod]
    public void InstallHasNoInstalledVersionAndIsNeverAPlainUpgrade()
    {
        UpdateJobSelection install = Install();
        Assert.AreEqual(PackageOperationKind.Install, install.Operation);
        Assert.IsNull(install.InstalledVersion);
        Assert.IsFalse(install.IsPlainUpgrade);
        Assert.IsFalse(install.RunsElevated);
        Assert.ThrowsExactly<ArgumentException>(() => UpdateJobSelection.Restore(PackageOperationKind.Install, "pcs:widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "2.0.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false));
        Assert.Throws<ArgumentException>(() => UpdateJobSelection.Restore(PackageOperationKind.Upgrade, "pcs:widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", null, "2.0.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false));
    }

    [TestMethod]
    public void InstallValidationFailsClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() => UpdateJobSelection.ForInstall("pcs:widget", UpdateProvider.WinGet, UpdateSource.MicrosoftStore, "9NBLGGH4NNS1", "1.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false), "msstore installs wait for source-agreement review");
        Assert.ThrowsExactly<ArgumentException>(() => UpdateJobSelection.ForInstall("pcs:widget", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "widget", "1.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "chocolatey-fixed-community-v1", Hash, false), "Chocolatey has no agreement hash");
        Assert.ThrowsExactly<ArgumentException>(() => Install(agreementHash: Hash.ToLowerInvariant()), "hash must be canonical upper-case hex");
        Assert.ThrowsExactly<ArgumentException>(() => Install(agreementHash: "ABC"), "hash length");
        Assert.ThrowsExactly<ArgumentException>(() => UpdateJobSelection.Restore(PackageOperationKind.Install, "pcs:widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", null, "2.0.0", UpdateScope.Machine, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", null, false), "machine scope always runs elevated");
    }

    [TestMethod]
    public void InstallerThatElevatesItselfRoutesToTheElevatedSession()
    {
        Assert.IsTrue(Install(installerElevates: true).RunsElevated);
        Assert.IsTrue(Install(scope: UpdateScope.Machine).RunsElevated);
    }

    [TestMethod]
    public void DigestCoversKindAgreementAndRoute()
    {
        ImmutableUpdateJob job = Confirm(Install(agreementHash: Hash));
        UpdateJobSelection[] substitutes =
        [
            Install(agreementHash: null),
            Install(agreementHash: "F" + Hash[1..]),
            Install(agreementHash: Hash, installerElevates: true),
            UpdateJobSelection.Restore(PackageOperationKind.Upgrade, job.Selection.StableProductKey, UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "2.0.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", Hash, false)
        ];
        foreach (UpdateJobSelection substitute in substitutes)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => ImmutableUpdateJob.Restore(job.Id, job.CorrelationId, substitute, job.ApprovedAt, job.Digest));
        }

        Assert.IsTrue(ImmutableUpdateJob.Restore(job.Id, job.CorrelationId, Install(agreementHash: Hash), job.ApprovedAt, job.Digest).HasValidDigest);
    }

    [TestMethod]
    public void BatchRunsUnelevatedItemsFirstInReviewedOrder()
    {
        UpdateJobSelection machine = Install("pcs:a", "Vendor.A", UpdateScope.Machine);
        UpdateJobSelection user = Install("pcs:b", "Vendor.B");
        UpdateJobSelection selfElevating = Install("pcs:c", "Vendor.C", installerElevates: true);
        UpdateJobSelection user2 = Upgrade("winget:d", "Vendor.D");

        ApprovedOperationBatch batch = new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [machine, user, selfElevating, user2]).Confirm(ApprovedAt);

        CollectionAssert.AreEqual(ExpectedExecutionOrder, batch.Items.Select(item => item.Selection.StableProductKey).ToArray());
        Assert.AreEqual(2, batch.UnelevatedItems.Count);
        Assert.AreEqual(2, batch.ElevatedItems.Count);
        Assert.IsTrue(batch.HasValidDigest);
        Assert.IsTrue(batch.Items.All(item => item.CorrelationId == batch.CorrelationId && item.ApprovedAt == ApprovedAt));
    }

    [TestMethod]
    public void BatchRestoreRejectsSubstitutionReorderAppendAndTruncation()
    {
        ApprovedOperationBatch batch = Batch(3);
        ImmutableUpdateJob[] items = [.. batch.Items];
        ImmutableUpdateJob outsider = new UpdateJobReview(Guid.NewGuid(), batch.CorrelationId, Upgrade("winget:x", "Vendor.X")).Confirm(ApprovedAt);
        ImmutableUpdateJob substitute = new UpdateJobReview(items[1].Id, batch.CorrelationId, Upgrade("winget:y", "Vendor.Y")).Confirm(ApprovedAt);

        Assert.AreEqual(batch.Digest, ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, items, batch.Digest).Digest);
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, [items[1], items[0], items[2]], batch.Digest), "reordered");
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, [.. items, outsider], batch.Digest), "appended");
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, items[..2], batch.Digest), "truncated");
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, [items[0], substitute, items[2]], batch.Digest), "substituted");
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(Guid.NewGuid(), batch.CorrelationId, ApprovedAt, items, batch.Digest), "replayed under another batch ID");
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, items, "00" + batch.Digest[2..]), "digest");
    }

    [TestMethod]
    public void JobsApprovedElsewhereCannotJoinABatch()
    {
        ApprovedOperationBatch batch = Batch(1);
        ImmutableUpdateJob otherCorrelation = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), Upgrade("winget:x", "Vendor.X")).Confirm(ApprovedAt);
        ImmutableUpdateJob otherTime = new UpdateJobReview(Guid.NewGuid(), batch.CorrelationId, Upgrade("winget:y", "Vendor.Y")).Confirm(ApprovedAt.AddSeconds(1));
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, [batch.Items[0], otherCorrelation], batch.Digest));
        Assert.ThrowsExactly<InvalidOperationException>(() => ApprovedOperationBatch.Restore(batch.Id, batch.CorrelationId, ApprovedAt, [batch.Items[0], otherTime], batch.Digest));
    }

    [TestMethod]
    public void BatchHoldsOneToFiftyDistinctTargets()
    {
        Assert.AreEqual(50, Batch(50).Items.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Batch(51));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), []));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [Upgrade("winget:a", "Vendor.A"), Upgrade("winget:a", "Vendor.B")]), "same product");
        Assert.ThrowsExactly<ArgumentException>(() => new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [Upgrade("winget:a", "Vendor.A"), Upgrade("winget:b", "vendor.a")]), "same package, different case");
        Assert.ThrowsExactly<ArgumentException>(() => new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [Upgrade("winget:a", "Vendor.A"), Upgrade("winget:b", "Vendor.B")], () => Guid.Parse("33333333-3333-3333-3333-333333333333")), "duplicate job IDs");
    }

    [TestMethod]
    public void BatchExpiresThirtyMinutesAfterApprovalAndRejectsAMovedClock()
    {
        ApprovedOperationBatch batch = Batch(1);
        Assert.IsTrue(batch.IsUsableAt(ApprovedAt));
        Assert.IsTrue(batch.IsUsableAt(ApprovedAt.AddMinutes(29).AddSeconds(59)));
        Assert.IsFalse(batch.IsUsableAt(ApprovedAt.AddMinutes(30)));
        Assert.IsFalse(batch.IsUsableAt(ApprovedAt.AddMinutes(-5)));
        Assert.AreEqual(ApprovedAt.AddMinutes(30), batch.ExpiresAt);
    }

    [TestMethod]
    public void AgreementDigestIsDeterministicBoundedAndUnambiguous()
    {
        Assert.IsTrue(PackageAgreementDigest.TryCompute([], out string? none, out _));
        Assert.IsNull(none);

        PackageAgreementText licence = new("License", "Terms text\nwith a newline", "https://example.invalid/license");
        Assert.IsTrue(PackageAgreementDigest.TryCompute([licence], out string? first, out _));
        Assert.IsTrue(PackageAgreementDigest.TryCompute([licence with { }], out string? second, out _));
        Assert.AreEqual(first, second);
        Assert.AreEqual(64, first!.Length);
        Assert.IsTrue(first.All(character => character is >= '0' and <= '9' or >= 'A' and <= 'F'));

        Assert.IsTrue(PackageAgreementDigest.TryCompute([new("ab", "c", null)], out string? split1, out _));
        Assert.IsTrue(PackageAgreementDigest.TryCompute([new("a", "bc", null)], out string? split2, out _));
        Assert.AreNotEqual(split1, split2);
        Assert.IsTrue(PackageAgreementDigest.TryCompute([licence with { Text = licence.Text + " " }], out string? changed, out _));
        Assert.AreNotEqual(first, changed);

        Assert.IsFalse(PackageAgreementDigest.TryCompute([licence with { Text = new string('x', PackageAgreementDigest.MaximumTextLength + 1) }], out _, out string oversize));
        Assert.AreEqual("agreement-oversize", oversize);
        Assert.IsFalse(PackageAgreementDigest.TryCompute([.. Enumerable.Repeat(licence, PackageAgreementDigest.MaximumAgreements + 1)], out _, out _));
        Assert.IsFalse(PackageAgreementDigest.TryCompute([null!], out _, out _));
        Assert.IsFalse(PackageAgreementDigest.TryCompute([new(string.Empty, string.Empty, null)], out _, out _));
        Assert.IsFalse(PackageAgreementDigest.TryCompute(null, out _, out _));
    }

    [TestMethod]
    public void AgreementAcceptanceIsValidated()
    {
        AgreementAcceptance accepted = new(Guid.NewGuid(), UpdateSource.WinGet, "Notepad++.Notepad++", "8.8.3", Hash, ApprovedAt);
        Assert.AreEqual("Notepad++.Notepad++", accepted.PackageId);
        Assert.ThrowsExactly<ArgumentException>(() => new AgreementAcceptance(Guid.NewGuid(), UpdateSource.ChocolateyCommunity, "app", "1.0", Hash, ApprovedAt));
        Assert.ThrowsExactly<ArgumentException>(() => new AgreementAcceptance(Guid.NewGuid(), UpdateSource.WinGet, "Vendor.App & calc", "1.0", Hash, ApprovedAt));
        Assert.ThrowsExactly<ArgumentException>(() => new AgreementAcceptance(Guid.NewGuid(), UpdateSource.WinGet, "Vendor.App", "1.0", "abc", ApprovedAt));
        Assert.ThrowsExactly<ArgumentException>(() => new AgreementAcceptance(Guid.Empty, UpdateSource.WinGet, "Vendor.App", "1.0", Hash, ApprovedAt));
    }

    private static ApprovedOperationBatch Batch(int count) =>
        new OperationBatchReview(Guid.NewGuid(), Guid.NewGuid(), [.. Enumerable.Range(0, count).Select(index => Upgrade($"winget:app{index}", $"Vendor.App{index}"))]).Confirm(ApprovedAt);

    private static ImmutableUpdateJob Confirm(UpdateJobSelection selection) => new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), selection).Confirm(ApprovedAt);

    private static UpdateJobSelection Upgrade(string key = "winget:contoso.widget", string packageId = "Contoso.Widget") =>
        new(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "1.0.0", "1.1.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Stable, "mapping-v1");

    private static UpdateJobSelection Chocolatey(string packageId) =>
        new("pcs:choco", UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, packageId, "1.0.0", "1.1.0", UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable, "chocolatey-fixed-community-v1");

    private static UpdateJobSelection Install(string key = "pcs:widget", string packageId = "Contoso.Widget", UpdateScope scope = UpdateScope.User, string? agreementHash = null, bool installerElevates = false) =>
        UpdateJobSelection.ForInstall(key, UpdateProvider.WinGet, UpdateSource.WinGet, packageId, "2.0.0", scope, UpdateArchitecture.X64, UpdateChannel.Stable, "winget-com-v1", agreementHash, installerElevates);
}
