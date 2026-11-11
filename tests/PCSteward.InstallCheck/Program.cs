using System.Security.Principal;
using System.Text.Json;
using PCSteward.Domain;
using PCSteward.Execution;

namespace PCSteward.InstallCheck;

/// <summary>
/// ADR-034 §5 (CI MSI job), FR-UPD-15, TM-04/13: after the MSI installs a build whose source pins the job's throwaway key,
/// prove on the real install that ADR-018 pre-launch trust passes, the desktop and helper carry the same compiled pin, and
/// the capability reader takes the release path and reports every allow-listed route Supported in both roles. Run as a
/// standard user (the caller-cannot-modify checks are about the caller). Exit 0 only when every check passes.
/// <para>With <c>--release-check-parity</c> it instead proves, for ADR-032 revision 2 §5–§6, that the C# checks the desktop and the
/// elevated helper will run agree with the PowerShell ones the release workflow and the first install used (see
/// <see cref="ReleaseCheckParity"/>).</para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        if (args.Length == 4 && args[0] == "--release-check-parity") return ReleaseCheckParity.Run(args[1], args[2], args[3]);
        if (args.Length != 0) return 2;
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string root = Path.Combine(programFiles, InstalledHelperLaunchTrust.InstallDirectoryName);
        string desktop = Path.Combine(root, InstalledHelperLaunchTrust.DesktopExecutableName);
        string helper = Path.Combine(root, InstalledHelperLaunchTrust.HelperExecutableName);
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? string.Empty;
        bool administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);

        HelperLaunchTrustResult launch = InstalledHelperLaunchTrust.Validate(new HelperLaunchPaths(desktop, helper));
        bool samePin = AuthenticodeIdentity.HaveSamePinnedSigner(desktop, helper, ReleaseSigningPins.Compiled);
        bool peerPair = AuthenticodeIdentity.IsTrustedAndSameSigner(desktop, helper);
        bool windowsTrustsDesktop = AuthenticodeIdentity.IsTrusted(desktop);
        Dictionary<string, string> capability = new(StringComparer.Ordinal);
        foreach (DeploymentCapabilityRole role in new[] { DeploymentCapabilityRole.Desktop, DeploymentCapabilityRole.Helper })
        {
            DeploymentCapabilityLayout layout = new(root, Path.Combine(programFiles, OwnerOnlyDeploymentCapabilityReader.StateDirectoryName),
                role == DeploymentCapabilityRole.Desktop ? desktop : helper, sid);
            OwnerOnlyDeploymentCapabilityReader reader = new(role, () => layout, OwnerOnlyDeploymentCapabilityReader.IsProtectedLayout);
            foreach (UpdateRouteContract route in DeploymentCapabilityPolicy.CapabilityOperationAllowlist)
                capability[$"{role}/{route}"] = reader.Evaluate(route).Status.ToString();
        }

        bool pass = !administrator && launch.Trusted && samePin && peerPair && !windowsTrustsDesktop &&
            capability.Values.All(status => status == nameof(HelperProtocolCapabilityStatus.Supported));
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["standardUser"] = !administrator,
            ["launchTrusted"] = launch.Trusted,
            ["launchRejection"] = launch.Rejection,
            ["samePin"] = samePin,
            ["peerPairAccepted"] = peerPair,
            ["windowsTrustsReleaseKey"] = windowsTrustsDesktop,
            ["capability"] = capability,
            ["pass"] = pass,
        }));
        return pass ? 0 : 1;
    }
}

/// <summary>
/// ADR-032 revision 2 §5–§6, FR-UPD-15, TM-20: on the CI MSI job's real signed MSI and signed payload, the C# release checks
/// must agree with the PowerShell ones:
/// <list type="bullet">
/// <item><see cref="ReleaseCheckPolicy.TryRead"/> accepts the <c>release-check.json</c> that <c>New-PCStewardReleaseCheck.ps1</c> wrote;</item>
/// <item><see cref="MsiTableDigest"/> gives the signed MSI the same tables digest as <c>PCStewardMsiTables.ps1</c> (recorded in the check file);</item>
/// <item><see cref="ReleaseImageComparison"/> finds each signed executable equal to its unsigned CI original, and refuses one
/// with a byte changed.</item>
/// </list>
/// </summary>
internal static class ReleaseCheckParity
{
    internal static int Run(string msiPath, string releaseCheckPath, string signedPayloadRoot)
    {
        bool recordRead = ReleaseCheckPolicy.TryRead(File.ReadAllBytes(releaseCheckPath), out ReleaseCheckRecord? record);
        string tables = MsiTableDigest.Compute(msiPath).Sha256;
        bool tablesEqual = record is not null && string.Equals(tables, record.MsiTablesSha256, StringComparison.Ordinal);
        Dictionary<string, bool> executables = new(StringComparer.Ordinal);
        bool tamperedRefused = true;
        if (record is not null)
        {
            foreach (ReleaseCheckExecutable executable in record.Executables)
            {
                ReleaseCheckFile file = record.Files.Single(candidate => string.Equals(candidate.Path, executable.Path, StringComparison.Ordinal));
                byte[] signed = File.ReadAllBytes(Path.Combine(signedPayloadRoot, executable.Path));
                executables[executable.Path] = ReleaseImageComparison.MatchesUnsignedOriginal(signed, file.Bytes, file.Sha256, executable.OriginalChecksum);
                signed[signed.Length / 3] ^= 0x5A;
                tamperedRefused &= !ReleaseImageComparison.MatchesUnsignedOriginal(signed, file.Bytes, file.Sha256, executable.OriginalChecksum);
            }
        }

        bool pass = recordRead && tablesEqual && executables.Count == 2 && executables.Values.All(match => match) && tamperedRefused;
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["recordRead"] = recordRead,
            ["tablesSha256"] = tables,
            ["tablesEqual"] = tablesEqual,
            ["executablesMatch"] = executables,
            ["tamperedRefused"] = tamperedRefused,
            ["pass"] = pass,
        }));
        return pass ? 0 : 1;
    }
}
