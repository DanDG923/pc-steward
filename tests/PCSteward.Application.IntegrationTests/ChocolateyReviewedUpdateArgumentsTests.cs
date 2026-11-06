using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Application;
using PCSteward.Provider.Chocolatey;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-03/04/13: fixed mapping data, never native eligibility or execution consent.</summary>
[TestClass]
public sealed class ChocolateyReviewedUpdateArgumentsTests
{
    private static readonly string[] ExpectedArguments = ["upgrade", "git.install", "--version", "2.0.0", "--source",
        "https://community.chocolatey.org/api/v2/", "--yes", "--limit-output", "--no-color", "--execution-timeout", "300",
        "--ignore-remembered-arguments", "--fail-on-not-installed", "--require-checksums"];
    [TestMethod]
    public void FixedMappingPreservesUncontrolledModeAndAllCompiledGuardArguments()
    {
        IReadOnlyList<string> arguments = ChocolateyReviewedUpdateArguments.Build(Operation());
        CollectionAssert.AreEqual(ExpectedArguments, arguments.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)arguments)[0] = "install");
        Assert.IsNull(UpdateInstallerModeCapabilities.Resolve(UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, UpdateScope.Machine));
    }

    [TestMethod]
    [DataRow(9, "chocolatey")]
    [DataRow(9, "Chocolatey.Extension")]
    [DataRow(9, "a..b")]
    [DataRow(10, "1.0")]
    [DataRow(11, "02.0.0")]
    [DataRow(11, "2.0.0-beta")]
    [DataRow(11, "2.0.0+metadata")]
    [DataRow(11, "1.0.0")]
    [DataRow(11, "0.9.0")]
    [DataRow(15, "Preview")]
    [DataRow(15, "Unknown")]
    [DataRow(21, "ChocolateyMachineRegistrationV3")]
    [DataRow(21, "ChocolateyMachineRegistrationV2")]
    [DataRow(21, "ChocolateyMachineProvisionalV3")]
    public void RecomputedDigestCannotMakeUnsupportedDataFormattable(int field, string value)
    {
        string[] lines = Encoding.UTF8.GetString(Operation().GetCanonicalBytes()).Split('\n');
        lines[field] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out ReviewedUpdateOperation? changed));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReviewedUpdateArguments.Build(changed!));
    }

    [TestMethod]
    [DataRow("observed")]
    [DataRow("selected")]
    [DataRow("winget")]
    [DataRow("legacy")]
    public void RecomputedUnionOrProviderChangeIsNeverAnAlternateMapping(string change)
    {
        string[] lines = Encoding.UTF8.GetString(Operation().GetCanonicalBytes()).Split('\n');
        if (change == "observed") { lines[13] = "Observed"; lines[14] = "Neutral"; }
        if (change == "selected") { lines[22] = "Selected"; lines[23] = "Silent"; lines[24] = "None"; }
        if (change == "winget") { lines[7] = "WinGet"; lines[8] = "WinGet"; lines[12] = "User"; lines[21] = "WinGetUserExactV3"; }
        if (change == "legacy") { lines[0] = "pc-steward-reviewed-update:2"; lines[13] = "Neutral"; lines[21] = "ChocolateyMachineRegistrationV2"; lines = lines.Where((_, index) => index != 14).ToArray(); }
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out ReviewedUpdateOperation? changed));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReviewedUpdateArguments.Build(changed!));
    }

    private static ReviewedUpdateOperation Operation(UpdateRouteContract route = UpdateRouteContract.ChocolateyMachineRuntimeLimitsV3)
    {
        DateTimeOffset at = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(), new UpdateJobSelection("choco:git.install",
            UpdateProvider.Chocolatey, UpdateSource.ChocolateyCommunity, "git.install", "1.0.0", "2.0.0", UpdateScope.Machine,
            UpdateArchitecture.Neutral, UpdateChannel.Stable, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "choco:git.install", "machine:choco", "choco-profile-v2:" + new string('A', 64), at,
            route,
            ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled), ReviewedUpdateArchitecture.ProviderScriptSelected());
    }

    [TestMethod]
    public void TheReviewAndHistoryDisclosureIsBoundToTheNewContractAndDoesNotGrantConfirmation()
    {
        ReviewedUpdateOperation operation = Operation();
        string? text = ReviewedUpdateProviderDisclosure.GetRiskText(operation);
        Assert.IsNotNull(text);
        StringAssert.Contains(text, "official Chocolatey Community feed");
        StringAssert.Contains(text, "local packages");
        StringAssert.Contains(text, "vendor installers");
        StringAssert.Contains(text, "disable checksum verification");
        StringAssert.Contains(text, "installer arguments");
        StringAssert.Contains(text, "replaced");
        // The disclosure grants nothing: confirmation follows the review gate alone. Since the Task 50 activation (ADR-031 owner
        // decision 7) the gate admits this exact runtime-limit shape; the same disclosure on a non-admitted route (the
        // provisional route) still cannot be confirmed. Frozen at review time so a refusal can only come from admission.
        Assert.IsTrue(ReviewedUpdateReview.HasSupportedMode(operation));
        Assert.IsNotNull(new ReviewedUpdateReview(new(operation, Guid.NewGuid()), new FrozenClock(operation.ReviewedAt)).Confirm());
        ReviewedUpdateOperation provisional = Operation(UpdateRouteContract.ChocolateyMachineProvisionalV3);
        Assert.IsFalse(ReviewedUpdateReview.HasSupportedMode(provisional));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            new ReviewedUpdateReview(new(provisional, Guid.NewGuid()), new FrozenClock(provisional.ReviewedAt)).Confirm());
    }

    [TestMethod]
    [DataRow(nameof(UpdateRouteContract.ChocolateyMachineProvisionalV3))]
    [DataRow(nameof(UpdateRouteContract.ChocolateyMachineRegistrationV3))]
    public void OlderChocolateyRoutesNeverInheritTheAcceptedRuntimeDisclosure(string route)
    {
        ReviewedUpdateOperation operation = Operation(Enum.Parse<UpdateRouteContract>(route));
        Assert.AreNotEqual(ReviewedProviderGuardContract.CommunityScriptRuntimeLimits, operation.ProviderGuardContract);
        Assert.IsNull(ReviewedUpdateProviderDisclosure.GetRiskText(operation));
        Assert.ThrowsExactly<ArgumentException>(() => ChocolateyReviewedUpdateArguments.Build(operation));
    }

    [TestMethod]
    public void SchemaTwoRestoredChocolateyDataNeverCarriesTheDisclosure()
    {
        string[] lines = Encoding.UTF8.GetString(Operation(UpdateRouteContract.ChocolateyMachineRegistrationV3).GetCanonicalBytes()).Split('\n');
        lines[0] = "pc-steward-reviewed-update:2";
        lines[13] = "Neutral";
        lines[21] = nameof(UpdateRouteContract.ChocolateyMachineRegistrationV2);
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines.Where((_, index) => index != 14)));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out ReviewedUpdateOperation? legacy));
        Assert.AreEqual(ReviewedProviderGuardContract.LegacyUnqualified, legacy!.ProviderGuardContract);
        Assert.IsNull(ReviewedUpdateProviderDisclosure.GetRiskText(legacy));
    }

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
