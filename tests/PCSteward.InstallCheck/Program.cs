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
/// </summary>
internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsWindows()) return 2;
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
