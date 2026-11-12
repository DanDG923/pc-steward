using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;

namespace PCSteward.Domain.UnitTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03, TM-07/13; Phase D stage D4b design §4 (owner answers 2026-11-12): the machine WinGet route is shape data
/// only. It accepts the current schema, WinGet from the <c>winget</c> source, machine scope, a channel other than Preview, Silent,
/// an observed x86, x64 or arm64 architecture and the exact <c>wgm1</c> evidence identity; anything else is refused, and a
/// recomputed digest cannot move a stored row off that shape.
/// </summary>
[TestClass]
public sealed class WinGetMachineSilentRouteTests
{
    internal static readonly string Registration = new('A', 64);
    internal static readonly string Manifest = new('B', 64);
    internal static readonly string Installer = new('C', 64);
    internal static string Evidence(string type = "msi") => $"wgm1:{type}:{Registration}:{Manifest}:{Installer}";

    [TestMethod]
    public void TheRouteRoundTripsWithTrustedDesktopProvenanceAndNoGuardContract()
    {
        ReviewedUpdateOperation operation = Operation();
        Assert.AreEqual(ReviewedCandidateProvenance.TrustedDesktopEvidenceRequired, operation.CandidateProvenance);
        Assert.AreEqual(ReviewedProviderGuardContract.LegacyUnqualified, operation.ProviderGuardContract);
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(operation.GetCanonicalBytes(), operation.Digest, out ReviewedUpdateOperation? restored));
        Assert.AreEqual(UpdateRouteContract.WinGetMachineSilentV1, restored!.RouteContract);
        Assert.IsTrue(operation.HasSameIdentity(restored));
        Assert.IsTrue(ReviewedUpdateOperation.TryGetWinGetMachineEvidence(restored.EvidenceId, out WinGetMachineEvidence? evidence));
        Assert.AreEqual(WinGetMachineInstallerType.Msi, evidence!.InstallerType);
        Assert.AreEqual(restored.EvidenceId, evidence.ToIdentity());
    }

    [TestMethod]
    [DataRow("msi", WinGetMachineInstallerType.Msi)]
    [DataRow("wix", WinGetMachineInstallerType.Wix)]
    [DataRow("exe", WinGetMachineInstallerType.Exe)]
    [DataRow("inno", WinGetMachineInstallerType.Inno)]
    [DataRow("nullsoft", WinGetMachineInstallerType.Nullsoft)]
    [DataRow("burn", WinGetMachineInstallerType.Burn)]
    public void EachAcceptedInstallerTypeParses(string text, WinGetMachineInstallerType expected)
    {
        Assert.IsTrue(ReviewedUpdateOperation.TryGetWinGetMachineEvidence(Evidence(text), out WinGetMachineEvidence? evidence));
        Assert.AreEqual(expected, evidence!.InstallerType);
        Assert.AreEqual(text, WinGetMachineEvidence.InstallerTypeText(expected));
    }

    [TestMethod]
    [DataRow("portable")]
    [DataRow("zip")]
    [DataRow("msix")]
    [DataRow("msstore")]
    [DataRow("MSI")]
    [DataRow("")]
    public void OtherInstallerTypesAreRefused(string text)
    {
        Assert.IsFalse(ReviewedUpdateOperation.TryGetWinGetMachineEvidence(Evidence(text), out _));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(evidenceId: Evidence(text)));
    }

    [TestMethod]
    public void MalformedEvidenceIsRefused()
    {
        string lower = new('a', 64);
        foreach (string evidence in new[]
                 {
                     $"wgm2:msi:{Registration}:{Manifest}:{Installer}", $"wgm1:msi:{lower}:{Manifest}:{Installer}",
                     $"wgm1:msi:{Registration}:{Manifest}", $"wgm1:msi:{Registration}:{Manifest}:{Installer}:{Installer}",
                     $"wgm1:msi:{Registration[..63]}:{Manifest}:{Installer}", "evidence:1",
                     $"choco-profile-v2:{Registration}",
                 })
        {
            Assert.IsFalse(ReviewedUpdateOperation.TryGetWinGetMachineEvidence(evidence, out _), evidence);
            Assert.ThrowsExactly<ArgumentException>(() => Operation(evidenceId: evidence), evidence);
        }
        Assert.IsFalse(ReviewedUpdateOperation.TryGetWinGetMachineEvidence(null, out _));
        Assert.ThrowsExactly<ArgumentException>(() => new WinGetMachineEvidence(WinGetMachineInstallerType.Exe, lower, Manifest, Installer).ToIdentity());
    }

    [TestMethod]
    public void OtherScopesSourcesChannelsModesAndArchitecturesAreRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Operation(scope: UpdateScope.User));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(source: UpdateSource.MicrosoftStore));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(channel: UpdateChannel.Preview));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(mode: ReviewedInstallerMode.Selected(InstallerMode.Interactive)));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(mode: ReviewedInstallerMode.NotControllable(UncontrolledInstallerModeReason.ProviderScriptControlled)));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(architecture: UpdateArchitecture.Neutral));
        Assert.ThrowsExactly<ArgumentException>(() => Operation(architectureBinding: ReviewedUpdateArchitecture.ProviderScriptSelected()));
        _ = Operation(channel: UpdateChannel.Unknown);
        _ = Operation(architecture: UpdateArchitecture.X86);
        _ = Operation(architecture: UpdateArchitecture.Arm64);
    }

    [TestMethod]
    public void TheUserRouteCannotCarryAMachineSelection() =>
        Assert.ThrowsExactly<ArgumentException>(() => Operation(route: UpdateRouteContract.WinGetUserSilentV1));

    [TestMethod]
    [DataRow(12, "User")]
    [DataRow(8, "MicrosoftStore")]
    [DataRow(15, "Preview")]
    [DataRow(19, "evidence:1")]
    public void ARecomputedDigestCannotMoveTheRouteOffItsShape(int field, string value)
    {
        string[] lines = Encoding.UTF8.GetString(Operation().GetCanonicalBytes()).Split('\n');
        lines[field] = value;
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        Assert.IsFalse(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out _));
    }

    [TestMethod]
    public void TheRegistrationDigestIsStableAndBindsViewKeyAndVersion()
    {
        string? digest = WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, "7-Zip", "24.09");
        Assert.IsTrue(ReviewedUpdateOperation.IsUpperHex64(digest));
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("wgm1-registration\nMachine64\n7-Zip\n24.09"))), digest);
        Assert.AreNotEqual(digest, WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine32, "7-Zip", "24.09"));
        Assert.AreNotEqual(digest, WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, "7-zip", "24.09"));
        Assert.AreNotEqual(digest, WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, "7-Zip", "24.08"));
        foreach (string? bad in new[] { null, "", " ", "a\nb", "a\rb", "a\0b", new string('x', 257) })
        {
            Assert.IsNull(WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, bad, "1.0"));
            Assert.IsNull(WinGetMachineRegistration.Digest(WinGetMachineRegistrationView.Machine64, "7-Zip", bad));
        }
        Assert.IsNull(WinGetMachineRegistration.Digest((WinGetMachineRegistrationView)7, "7-Zip", "24.09"));
    }

    internal static ReviewedUpdateOperation Operation(UpdateScope scope = UpdateScope.Machine, UpdateSource source = UpdateSource.WinGet,
        UpdateChannel channel = UpdateChannel.Stable, ReviewedInstallerMode? mode = null, UpdateArchitecture architecture = UpdateArchitecture.X64,
        ReviewedUpdateArchitecture? architectureBinding = null, string? evidenceId = null,
        UpdateRouteContract route = UpdateRouteContract.WinGetMachineSilentV1)
    {
        DateTimeOffset at = new(2026, 11, 12, 21, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("d4b00000-0000-0000-0000-000000000001"),
            Guid.Parse("d4b00000-0000-0000-0000-000000000002"), new UpdateJobSelection("arp:hklm64:7zip",
                UpdateProvider.WinGet, source, "7zip.7zip", "24.09", "25.01", scope, architecture, channel, "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "arp:hklm64:7zip", "machine:winget", evidenceId ?? Evidence(), at, route,
            mode ?? ReviewedInstallerMode.Selected(InstallerMode.Silent), architectureBinding);
    }
}
