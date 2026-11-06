using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-UPD-15, TM-07/13; adapter gates §A.6 (O2, review-9 L3): the runtime-limit route's EvidenceId is exactly
/// <c>choco-profile-v2:</c> + 64 uppercase hex, enforced by the constructor shared with TryRestore. Other routes are unchanged.
/// </summary>
[TestClass]
public sealed class ReviewedOperationProfileEvidenceTests
{
    private static readonly string Digest = ReviewedProviderGuardContractTests.ProfileEvidence[ReviewedUpdateOperation.ProfileEvidencePrefix.Length..];

    public static IEnumerable<object[]> MalformedEvidence() =>
    [
        ["digits-63", ReviewedUpdateOperation.ProfileEvidencePrefix + Digest[..63]],
        ["digits-65", ReviewedUpdateOperation.ProfileEvidencePrefix + Digest + "A"],
        ["lowercase", ReviewedUpdateOperation.ProfileEvidencePrefix + Digest.ToLowerInvariant()],
        ["mixed-case", ReviewedUpdateOperation.ProfileEvidencePrefix + "a" + Digest[1..]],
        ["profile-v1", "choco-profile-v1:" + Digest],
        ["prefix-only", ReviewedUpdateOperation.ProfileEvidencePrefix],
        ["trailing-character", ReviewedUpdateOperation.ProfileEvidencePrefix + Digest + ":"],
        ["non-hex", ReviewedUpdateOperation.ProfileEvidencePrefix + "G" + Digest[1..]],
        ["uppercase-prefix", "CHOCO-PROFILE-V2:" + Digest],
        ["opaque", "evidence:1"],
    ];

    [TestMethod]
    [DynamicData(nameof(MalformedEvidence))]
    public void RuntimeLimitConstructorRejectsMalformedEvidence(string name, string evidenceId)
    {
        Assert.ThrowsExactly<ArgumentException>(() => ReviewedProviderGuardContractTests.Operation(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3, evidenceId), name);
        Assert.IsFalse(ReviewedUpdateOperation.TryGetProfileEvidenceDigest(evidenceId, out string digest), name);
        Assert.AreEqual(string.Empty, digest);
    }

    [TestMethod]
    [DynamicData(nameof(MalformedEvidence))]
    public void TryRestoreRejectsMalformedRuntimeLimitEvidenceWithARecomputedDigest(string name, string evidenceId)
    {
        ReviewedUpdateOperation valid = ReviewedProviderGuardContractTests.Operation(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        string[] lines = Encoding.UTF8.GetString(valid.GetCanonicalBytes()).Split('\n');
        int field = Array.IndexOf(lines, valid.EvidenceId);
        Assert.IsTrue(field > 0);
        lines[field] = evidenceId;
        byte[] forged = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(forged, Convert.ToHexString(SHA256.HashData(forged)), out ReviewedUpdateOperation? restored), name);
        Assert.IsNull(restored);
    }

    [TestMethod]
    public void ConformingEvidenceRoundTripsAndExposesTheDigest()
    {
        ReviewedUpdateOperation operation = ReviewedProviderGuardContractTests.Operation(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        Assert.AreEqual(81, operation.EvidenceId.Length);
        Assert.AreEqual("choco-profile-v2:", ReviewedUpdateOperation.ProfileEvidencePrefix);
        Assert.IsTrue(ReviewedUpdateOperation.TryGetProfileEvidenceDigest(operation.EvidenceId, out string digest));
        Assert.AreEqual(Digest, digest);
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(operation.GetCanonicalBytes(), operation.Digest, out ReviewedUpdateOperation? restored));
        Assert.IsTrue(operation.HasSameIdentity(restored));
        Assert.IsFalse(ReviewedUpdateOperation.TryGetProfileEvidenceDigest(null, out _));
    }

    [TestMethod]
    [DataRow(UpdateRouteContract.ChocolateyMachineProvisionalV3)]
    [DataRow(UpdateRouteContract.ChocolateyMachineRegistrationV3)]
    public void OtherRoutesKeepTheirOpaqueEvidenceFormat(UpdateRouteContract route)
    {
        ReviewedUpdateOperation operation = ReviewedProviderGuardContractTests.Operation(route, "evidence:1");
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(operation.GetCanonicalBytes(), operation.Digest, out _));
        ReviewedUpdateOperation lowercase = ReviewedProviderGuardContractTests.Operation(route, ReviewedUpdateOperation.ProfileEvidencePrefix + Digest.ToLowerInvariant());
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(lowercase.GetCanonicalBytes(), lowercase.Digest, out _));
    }
}
