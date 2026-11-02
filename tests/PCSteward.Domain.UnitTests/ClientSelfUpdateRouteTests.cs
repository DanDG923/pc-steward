using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-07/13; ADR-031 design mapping §3.4: the client self-update route is shape data with its own
/// guard contract. It accepts only the current schema, Chocolatey Community machine registration of exactly <c>chocolatey</c>,
/// provider-script-controlled mode and architecture, and profile evidence; it never inherits the package route's meaning.
/// </summary>
[TestClass]
public sealed class ClientSelfUpdateRouteTests
{
    [TestMethod]
    public void TheRouteHasItsOwnGuardAndTrustedDesktopProvenanceAndRoundTrips()
    {
        ReviewedUpdateOperation operation = Operation();
        Assert.AreEqual(ReviewedProviderGuardContract.ChocolateyClientSelfUpdate, operation.ProviderGuardContract);
        Assert.AreEqual(ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired, operation.CandidateProvenance);
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(operation.GetCanonicalBytes(), operation.Digest, out ReviewedUpdateOperation? restored));
        Assert.AreEqual(UpdateRouteContract.ChocolateyClientSelfUpdateV1, restored!.RouteContract);
        Assert.AreEqual(ReviewedProviderGuardContract.ChocolateyClientSelfUpdate, restored.ProviderGuardContract);
        Assert.IsTrue(operation.HasSameIdentity(restored));
        Assert.IsFalse(operation.HasSameIdentity(Operation(packageRoute: true)));
    }

    [TestMethod]
    [DataRow("git")]
    [DataRow("Chocolatey")]
    [DataRow("chocolatey.extension")]
    public void OnlyTheExactClientPackageIdIsAccepted(string packageId) =>
        Assert.ThrowsExactly<ArgumentException>(() => Operation(packageId: packageId));

    [TestMethod]
    public void OtherScopesSourcesModesArchitecturesAndEvidenceAreRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Operation(scope: UpdateScope.User));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(mode: ReviewedInstallerMode.Selected(InstallerMode.Silent)));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(architecture: ReviewedUpdateArchitecture.Observed(UpdateArchitecture.X64)));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(evidenceId: "evidence:1"));
    }

    [TestMethod]
    [DataRow(9, "git")]
    [DataRow(12, "User")]
    [DataRow(8, "WinGet")]
    public void ARecomputedDigestCannotMoveTheRouteOffItsShape(int field, string value)
    {
        string[] lines = Encoding.UTF8.GetString(Operation().GetCanonicalBytes()).Split('\n');
        lines[field] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _));
    }

    internal static ReviewedUpdateOperation Operation(string packageId = "chocolatey", UpdateScope scope = UpdateScope.Machine,
        ReviewedInstallerMode? mode = null, ReviewedUpdateArchitecture? architecture = null, string? evidenceId = null, bool packageRoute = false)
    {
        DateTimeOffset at = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("71000000-0000-0000-0000-000000000001"),
            Guid.Parse("71000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:chocolatey",
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, packageId, "2.7.5", "2.8.0", scope,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:chocolatey", "machine:choco", evidenceId ?? ReviewedProviderGuardContractTests.ProfileEvidence,
            at, packageRoute ? UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3 : UpdateRouteContract.ChocolateyClientSelfUpdateV1,
            mode ?? ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            architecture ?? ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
