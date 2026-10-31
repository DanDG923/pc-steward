using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>FR-UPD-15/01; TM-03/04/13: fixed mode/type mapping is data formatting, never dispatch authority.</summary>
[TestClass]
public sealed class WingetReviewedUpdateArgumentsTests
{
    [TestMethod]
    [DataRow(UpdateArchitecture.X86, "x86")]
    [DataRow(UpdateArchitecture.X64, "x64")]
    [DataRow(UpdateArchitecture.Arm64, "arm64")]
    public void ExactUserMappingPinsSilentModeAndMsixInstallerType(UpdateArchitecture architecture, string argument)
    {
        IReadOnlyList<string> arguments = WingetReviewedUpdateArguments.Build(Operation(architecture));
        CollectionAssert.AreEqual(new[]
        {
            "upgrade", "--id", "Contoso.Widget", "--version", "2.0.0.0", "--source", "winget", "--exact",
            "--scope", "user", "--architecture", argument, "--disable-interactivity", "--silent", "--installer-type", "msix"
        }, arguments.ToArray());
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<string>)arguments)[0] = "install");
    }

    [TestMethod]
    [DataRow("interactive")]
    [DataRow("uncontrolled")]
    [DataRow("store")]
    [DataRow("provider")]
    [DataRow("machine")]
    [DataRow("neutral")]
    [DataRow("script")]
    [DataRow("unknown-channel")]
    [DataRow("wrong-route")]
    [DataRow("legacy-route")]
    [DataRow("legacy-schema")]
    public void RecomputedIntegrityDoesNotMakeUnsupportedBindingsFormattable(string change)
    {
        string[] fields = Encoding.UTF8.GetString(Operation().GetCanonicalBytes()).Split('\n');
        switch (change)
        {
            case "interactive": fields[23] = "Interactive"; break;
            case "uncontrolled": fields[22] = "NotControllable"; fields[23] = "None"; fields[24] = "ProviderScriptControlled"; break;
            case "store": fields[8] = "MicrosoftStore"; break;
            case "provider": fields[7] = "Chocolatey"; fields[8] = "ChocolateyCommunity"; fields[12] = "Machine"; break;
            case "machine": fields[12] = "Machine"; break;
            case "neutral": fields[14] = "Neutral"; break;
            case "script": fields[13] = "ProviderScriptSelected"; fields[14] = "None"; break;
            case "unknown-channel": fields[15] = "Unknown"; break;
            case "wrong-route": fields[21] = "ChocolateyMachineRegistrationV3"; break;
            case "legacy-route": fields[21] = "WinGetUserExactV2"; break;
            case "legacy-schema": fields[0] = "pc-steward-reviewed-update:2"; fields = fields.Where((_, i) => i != 13).ToArray(); break;
            default: Assert.Fail("Unknown fixture mutation."); break;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(string.Join('\n', fields));
        Assert.IsTrue(ReviewedUpdateOperation.TryRestore(bytes, Convert.ToHexString(SHA256.HashData(bytes)), out ReviewedUpdateOperation? changed));
        Assert.ThrowsExactly<ArgumentException>(() => WingetReviewedUpdateArguments.Build(changed!));
    }

    [TestMethod]
    public void PreviewLabelDoesNotAddAnyChannelOrFallbackArgument()
    {
        ReviewedUpdateOperation operation = Operation(channel: UpdateChannel.Preview);
        CollectionAssert.AreEqual(WingetReviewedUpdateArguments.Build(Operation()).ToArray(),
            WingetReviewedUpdateArguments.Build(operation).ToArray());
    }

    private static ReviewedUpdateOperation Operation(UpdateArchitecture architecture = UpdateArchitecture.X64,
        UpdateChannel channel = UpdateChannel.Stable)
    {
        DateTimeOffset at = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.NewGuid(), Guid.NewGuid(),
            new("winget:contoso.widget", UpdateProvider.WinGet, UpdateSource.WinGet, "Contoso.Widget", "1.0.0.0", "2.0.0.0",
                UpdateScope.User, architecture, channel, "mapping-v3")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "msix:contoso", "user:contoso", "evidence:contoso", at,
            UpdateRouteContract.WinGetUserExactV3, ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }
}
