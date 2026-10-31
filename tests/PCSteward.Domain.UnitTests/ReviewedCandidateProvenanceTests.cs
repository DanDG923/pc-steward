using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>FR-UPD-15/01; TM-07/13: provisional intent has a distinct frozen contract, never qualification.</summary>
[TestClass]
public sealed class ReviewedCandidateProvenanceTests
{
    [TestMethod]
    public void ProvisionalRouteIsDistinctCanonicalIntentAndRoundTripsWithoutPromotion()
    {
        ReviewedUpdateOperation original = Operation(UpdateRouteContract.ChocolateyMachineRegistrationV3);
        ReviewedUpdateOperation provisional = Operation(UpdateRouteContract.ChocolateyMachineProvisionalV3);
        Assert.AreNotEqual(original.Digest, provisional.Digest);
        Assert.IsFalse(original.HasSameIdentity(provisional));
        Assert.AreEqual(ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired, original.CandidateProvenance);
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, provisional.CandidateProvenance);
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(provisional.GetCanonicalBytes(), provisional.Digest, out ReviewedUpdateOperation? restored));
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, restored!.CandidateProvenance);
        Assert.IsTrue(provisional.HasSameIdentity(restored));
    }

    [TestMethod]
    public void RecomputedRouteChangeRemainsDifferentDataRatherThanQualification()
    {
        ReviewedUpdateOperation provisional = Operation(UpdateRouteContract.ChocolateyMachineProvisionalV3);
        string[] lines = Encoding.UTF8.GetString(provisional.GetCanonicalBytes()).Split('\n');
        lines[21] = nameof(UpdateRouteContract.ChocolateyMachineRegistrationV3);
        byte[] changed = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(changed, Convert.ToHexString(SHA256.HashData(changed)), out ReviewedUpdateOperation? restored));
        Assert.IsFalse(provisional.HasSameIdentity(restored));
        Assert.AreNotEqual(provisional.Digest, restored!.Digest);
    }

    [TestMethod]
    public void LegacySchemaCannotCarryTheNewProvisionalMeaningEvenWithRecomputedDigest()
    {
        string[] lines = Encoding.UTF8.GetString(Operation(UpdateRouteContract.ChocolateyMachineProvisionalV3).GetCanonicalBytes()).Split('\n');
        lines[0] = "pc-steward-reviewed-update:2";
        lines[13] = "Neutral";
        byte[] legacy = Encoding.UTF8.GetBytes(string.Join('\n', lines.Where((_, index) => index != 14)));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(legacy, Convert.ToHexString(SHA256.HashData(legacy)), out _));
    }

    private static ReviewedUpdateOperation Operation(UpdateRouteContract route)
    {
        DateTimeOffset at = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("60000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:git",
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git", "1.0.0", "2.0.0",
                UpdateScope.Machine, UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v1")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:git", "machine:choco", "evidence:1", at,
            route, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
