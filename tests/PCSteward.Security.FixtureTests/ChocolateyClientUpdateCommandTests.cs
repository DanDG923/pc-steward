using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCSteward.Domain;
using PCSteward.Provider.Chocolatey.Native;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03–09, TM-03/04/13; ADR-031 design mapping §3.3 and §3.5 (portable): the client self-update's compiled
/// commands. The upgrade argv is golden, takes its version from the reviewed list entry, and exists only for the list's offer
/// (newest strictly newer listed release) to the observed installed release; every other pairing or frozen mismatch refuses.
/// </summary>
[TestClass]
public sealed class ChocolateyClientUpdateCommandTests
{
    private static readonly string[] UpgradeGolden = ["upgrade", "chocolatey", "--version", "3.0.0", "--source",
        "https://community.chocolatey.org/api/v2/", "--yes", "--limit-output", "--no-color", "--execution-timeout", "300",
        "--ignore-remembered-arguments", "--fail-on-not-installed", "--require-checksums"];
    private static readonly string[] ListGolden = ["list", "chocolatey", "--exact", "--limit-output", "--no-color"];

    [TestMethod]
    public void TheUpgradeAndListAreGoldenAndBuiltFromTheListOffer()
    {
        ChocolateyReleaseList list = ChocolateyReleaseListTests.Three();
        ChocolateyReviewedRelease installed = list.Releases[0], target = list.Releases[2];
        ReviewedUpdateOperation operation = Operation("1.0.0", "3.0.0");
        ChocolateyCompiledCommand.UpgradeClient upgrade = ChocolateyCompiledCommand.TryUpgradeClient(operation, installed, target)
            ?? throw new AssertFailedException("upgrade refused");
        CollectionAssert.AreEqual(UpgradeGolden, upgrade.Arguments.ToArray());
        Assert.AreSame(target, upgrade.TargetRelease);
        Assert.AreSame(installed, upgrade.InstalledRelease);
        Assert.AreEqual(TimeSpan.FromMinutes(10), upgrade.Budget, "the existing upgrade budget");
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)upgrade.Arguments)[3] = "9.9.9");

        ChocolateyCompiledCommand.ListClient listClient = ChocolateyCompiledCommand.TryListClient(operation, installed)
            ?? throw new AssertFailedException("list refused");
        CollectionAssert.AreEqual(ListGolden, listClient.Arguments.ToArray());
        Assert.AreEqual(TimeSpan.FromSeconds(30), listClient.Budget);
    }

    [TestMethod]
    public void NoTargetOtherThanTheNewestStrictlyNewerListedReleaseIsCompiled()
    {
        ChocolateyReleaseList list = ChocolateyReleaseListTests.Three();
        ChocolateyReviewedRelease r1 = list.Releases[0], r2 = list.Releases[1], r3 = list.Releases[2];
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("1.0.0", "2.0.0"), r1, r2), "not the newest");
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("3.0.0", "1.0.0"), r3, r1), "downgrade");
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("2.0.0", "2.0.0"), r2, r2), "equal");
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("1.0.0", "3.0.0"), r2, r3), "installed release differs from the frozen one");
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("1.0.0", "3.0.1"), r1, r3), "frozen candidate differs from the list");
        ChocolateyReviewedRelease foreign = ChocolateyReleaseListTests.Three().Releases[2];
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("1.0.0", "3.0.0"), r1, foreign), "a release of another list");
        // The production list holds only 2.7.5, so nothing can be compiled from it today.
        ChocolateyReviewedRelease listed = ChocolateyReleaseList.Listed.Releases.Single();
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(Operation("2.7.5", "2.7.5"), listed, listed));
    }

    [TestMethod]
    public void APackageRouteOperationCannotCompileAClientCommand()
    {
        ChocolateyReleaseList list = ChocolateyReleaseListTests.Three();
        ReviewedUpdateOperation package = N3Operations.Create();
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgradeClient(package, list.Releases[0], list.Releases[2]));
        Assert.IsNull(ChocolateyCompiledCommand.TryListClient(package, list.Releases[0]));
        // And the client route never yields a package command (the existing builders refuse client targets and the route).
        ReviewedUpdateOperation client = Operation("1.0.0", "3.0.0");
        Assert.IsNull(ChocolateyCompiledCommand.TryUpgrade(client));
        Assert.IsNull(ChocolateyCompiledCommand.TryListExact(client));
    }

    internal static ReviewedUpdateOperation Operation(string installed, string candidate)
    {
        DateTimeOffset at = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("72000000-0000-0000-0000-000000000001"),
            Guid.Parse("72000000-0000-0000-0000-000000000002"), new UpdateJobSelection("choco:chocolatey",
                UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "chocolatey", installed, candidate, UpdateScope.Machine,
                UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:chocolatey", "machine:choco",
            ReviewedUpdateOperation.ProfileEvidencePrefix + Convert.ToHexString(SHA256.HashData("client profile"u8)), at,
            UpdateRouteContract.ChocolateyClientSelfUpdateV1, ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled),
            ReviewedUpdateArchitecture.ProviderScriptSelected());
    }
}
