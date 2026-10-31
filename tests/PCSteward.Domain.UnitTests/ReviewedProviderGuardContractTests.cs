using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>FR-UPD-15/01; TM-07/13: the accepted runtime limitation changes frozen contract identity, never approval.</summary>
[TestClass]
public sealed class ReviewedProviderGuardContractTests
{
    [TestMethod]
    public void AcceptedRuntimeLimitDoesNotReinterpretTheEarlierProvisionalReview()
    {
        ReviewedUpdateOperation earlier = Operation(UpdateRouteContract.ChocolateyMachineProvisionalV3);
        ReviewedUpdateOperation current = Operation(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3);
        Assert.AreEqual(ReviewedProviderGuardContract.RequiredEffectiveControls, earlier.ProviderGuardContract);
        Assert.AreEqual(ReviewedProviderGuardContract.CommunityScriptRuntimeLimits, current.ProviderGuardContract);
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, current.CandidateProvenance);
        Assert.AreNotEqual(earlier.Digest, current.Digest);
        Assert.IsFalse(earlier.HasSameIdentity(current));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(current.GetCanonicalBytes(), current.Digest, out ReviewedUpdateOperation? restored));
        Assert.AreEqual(current.ProviderGuardContract, restored!.ProviderGuardContract);
        Assert.AreEqual(ReviewedCandidateProvenance.UserCacheIntent, restored.CandidateProvenance);
    }

    [TestMethod]
    [DataRow(7, "WinGet")]
    [DataRow(8, "WinGet")]
    [DataRow(12, "User")]
    public void TheExceptionCannotEscapeItsProviderSourceOrScopeWithRecomputedIntegrity(int field, string value)
    {
        string[] lines = Encoding.UTF8.GetString(Operation(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3).GetCanonicalBytes()).Split('\n');
        lines[field] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _));
    }

    [TestMethod]
    public void SchemaTwoCannotAcquireThisContractAndOldRouteLabelsRemainUnqualified()
    {
        string[] lines = Encoding.UTF8.GetString(Operation(UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3).GetCanonicalBytes()).Split('\n');
        lines[0] = "pc-steward-reviewed-update:2";
        lines[13] = "Neutral";
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines.Where((_, index) => index != 14)));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _));
        Assert.AreEqual(ReviewedProviderGuardContract.LegacyUnqualified, Operation(UpdateRouteContract.ChocolateyMachineRegistrationV2).ProviderGuardContract);
    }

    private static ReviewedUpdateOperation Operation(UpdateRouteContract route)
    {
        DateTimeOffset at = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("70000000-0000-0000-0000-000000000001"),
            Guid.Parse("70000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:git",
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git", "1.0.0", "2.0.0", UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:git", "machine:choco", "evidence:1", at, route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
