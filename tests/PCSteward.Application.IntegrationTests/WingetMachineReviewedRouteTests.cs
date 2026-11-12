using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Application.IntegrationTests;

/// <summary>
/// FR-UPD-15, FR-UPD-01/03, TM-03/04/07/13. Phase D stage D4b design §4, §5 H7 and §6 S2 (owner answers 2026-11-12): the machine
/// WinGet route's fixed command lines come only from a <c>WinGetMachineSilentV1</c> operation and its <c>wgm1</c> evidence, never add
/// a forbidden switch, and the elevated helper's WinGet settings check allows only settings that cannot change what is installed,
/// from where, or how.
/// </summary>
[TestClass]
[TestCategory("FR-UPD-15")]
public sealed class WingetMachineReviewedRouteTests
{
    private static readonly string Registration = new('A', 64);
    private static readonly string Manifest = new('B', 64);
    private static readonly string Installer = new('C', 64);

    private static readonly string[] Forbidden =
    [
        "--force", "--ignore-security-hash", "--accept-package-agreements", "--accept-source-agreements", "--include-unknown", "--location",
        "--override", "--custom", "--allow-reboot", "--skip-dependencies", "--interactive", "--scope=user", "user", "--manifest", "-m",
        "--uninstall-previous", "--purge", "--rename", "--header", "--authentication-mode", "--log", "--ignore-local-archive-malware-scan",
    ];

    [TestMethod]
    [DataRow("msi", UpdateArchitecture.X64, "x64")]
    [DataRow("wix", UpdateArchitecture.X86, "x86")]
    [DataRow("exe", UpdateArchitecture.Arm64, "arm64")]
    [DataRow("inno", UpdateArchitecture.X64, "x64")]
    [DataRow("nullsoft", UpdateArchitecture.X64, "x64")]
    [DataRow("burn", UpdateArchitecture.X86, "x86")]
    public void TheUpgradeLineIsFixedAndBindsArchitectureAndInstallerType(string type, UpdateArchitecture architecture, string architectureText)
    {
        ReviewedUpdateOperation operation = Operation(type, architecture);
        CollectionAssert.AreEqual(new[]
        {
            "upgrade", "--id", "7zip.7zip", "--version", "25.01", "--source", "winget", "--exact", "--scope", "machine",
            "--architecture", architectureText, "--installer-type", type, "--disable-interactivity", "--silent"
        }, WingetMachineReviewedArguments.BuildUpgrade(operation).ToArray());
    }

    [TestMethod]
    public void TheDownloadLineUsesTheSameBindingAndTheGivenFolder()
    {
        string folder = OperatingSystem.IsWindows() ? @"C:\Windows\Temp\PCSteward-0123\d" : "/tmp/PCSteward-0123/d";
        CollectionAssert.AreEqual(new[]
        {
            "download", "--id", "7zip.7zip", "--version", "25.01", "--source", "winget", "--exact", "--scope", "machine",
            "--architecture", "x64", "--installer-type", "msi", "--download-directory", folder, "--disable-interactivity"
        }, WingetMachineReviewedArguments.BuildDownload(Operation(), folder).ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildDownload(Operation(), "relative\\d"));
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildDownload(Operation(), " "));
    }

    [TestMethod]
    public void NoLineCarriesAForbiddenSwitch()
    {
        string folder = OperatingSystem.IsWindows() ? @"C:\Windows\Temp\x\d" : "/tmp/x/d";
        foreach (string type in new[] { "msi", "wix", "exe", "inno", "nullsoft", "burn" })
        {
            string[] upgrade = [.. WingetMachineReviewedArguments.BuildUpgrade(Operation(type))];
            string[] download = [.. WingetMachineReviewedArguments.BuildDownload(Operation(type), folder)];
            foreach (string forbidden in Forbidden)
            {
                CollectionAssert.DoesNotContain(upgrade, forbidden, type);
                CollectionAssert.DoesNotContain(download, forbidden, type);
            }
        }
    }

    [TestMethod]
    public void OtherRoutesAreRefusedByTheMachineMapping()
    {
        ReviewedUpdateOperation user = UserOperation();
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildUpgrade(user));
        Assert.ThrowsExactly<ArgumentException>(() => WingetMachineReviewedArguments.BuildDownload(user, OperatingSystem.IsWindows() ? @"C:\x" : "/x"));
        Assert.ThrowsExactly<ArgumentNullException>(() => WingetMachineReviewedArguments.BuildUpgrade(null!));
        Assert.IsFalse(WingetUserReviewedRoute.Admits(Operation()), "the per-user route never admits a machine operation");
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   \r\n")]
    [DataRow("{}")]
    [DataRow("{ \"$schema\": \"https://aka.ms/winget-settings.schema.json\" }")]
    [DataRow("{ \"visual\": { \"progressBar\": \"rainbow\" }, \"telemetry\": { \"disable\": true } }")]
    [DataRow("{ \"logging\": { \"level\": \"verbose\" }, \"output\": {}, \"interactivity\": { \"disable\": true } }")]
    [DataRow("{ \"source\": { \"autoUpdateIntervalInMinutes\": 0 } }")]
    [DataRow("// comment\n{ /* block */ \"visual\": {}, }")]
    public void OnlyAllowListedSettingsPass(string json) =>
        Assert.IsTrue(WingetUserSettingsPolicy.Allows(Encoding.UTF8.GetBytes(json)), json);

    [TestMethod]
    [DataRow("{ \"installBehavior\": { \"preferences\": { \"scope\": \"user\" } } }")]
    [DataRow("{ \"network\": { \"downloader\": \"wininet\" } }")]
    [DataRow("{ \"experimentalFeatures\": { \"directMSI\": true } }")]
    [DataRow("{ \"uninstallBehavior\": {} }")]
    [DataRow("{ \"downloadBehavior\": { \"defaultDownloadDirectory\": \"C:\\\\x\" } }")]
    [DataRow("{ \"configureBehavior\": {} }")]
    [DataRow("{ \"Visual\": {} }")]
    [DataRow("{ \"source\": { \"autoUpdateIntervalInMinutes\": 5, \"other\": 1 } }")]
    [DataRow("{ \"source\": [] }")]
    [DataRow("{ \"visual\": {}, \"visual\": {} }")]
    [DataRow("{ \"source\": { \"autoUpdateIntervalInMinutes\": 1, \"autoUpdateIntervalInMinutes\": 2 } }")]
    [DataRow("[]")]
    [DataRow("\"text\"")]
    [DataRow("{ \"visual\": ")]
    [DataRow("{ \"a\": 1 } { }")]
    public void AnythingElseRefuses(string json) =>
        Assert.IsFalse(WingetUserSettingsPolicy.Allows(Encoding.UTF8.GetBytes(json)), json);

    [TestMethod]
    public void EncodingDepthAndSizeAreBounded()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'];
        Assert.IsTrue(WingetUserSettingsPolicy.Allows(bom), "a UTF-8 byte order mark is accepted");
        Assert.IsFalse(WingetUserSettingsPolicy.Allows([(byte)'{', 0xC3, 0x28, (byte)'}']), "invalid UTF-8 refuses");
        Assert.IsFalse(WingetUserSettingsPolicy.Allows(Encoding.Unicode.GetBytes("{}")), "UTF-16 refuses");

        string deep = "{ \"visual\": " + string.Concat(Enumerable.Repeat("{ \"a\": ", 10)) + "1" + new string('}', 10) + " }";
        Assert.IsFalse(WingetUserSettingsPolicy.Allows(Encoding.UTF8.GetBytes(deep)), "nesting beyond the depth limit refuses");

        string padding = new(' ', WingetUserSettingsPolicy.MaximumBytes);
        Assert.IsFalse(WingetUserSettingsPolicy.Allows(Encoding.UTF8.GetBytes("{}" + padding)), "an oversized file refuses");
        Assert.IsTrue(WingetUserSettingsPolicy.Allows(Encoding.UTF8.GetBytes("{}" + padding[..^2])), "a file at the limit is read");
    }

    [TestMethod]
    public void TheProtectedRunFolderIsFreshAdministratorOnlyAndRemoved()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("The protected run folder is Windows-only; this row is Windows-CI evidence.");
        if (!IsElevated()) Assert.Inconclusive("Creating an Administrators-owned folder needs an elevated host (CI runs elevated).");
        string temp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        string first, second;
        using (HelperProtectedRunFolder? run = HelperProtectedRunFolder.TryCreate(), other = HelperProtectedRunFolder.TryCreate())
        {
            Assert.IsNotNull(run);
            Assert.IsNotNull(other);
            first = run.Path;
            second = other.Path;
            Assert.AreNotEqual(first, second, "every WinGet process gets its own folder");
            Assert.AreEqual(temp, Path.GetDirectoryName(first), ignoreCase: true);
            StringAssert.Matches(Path.GetFileName(first), new System.Text.RegularExpressions.Regex("^PCSteward-[0-9A-F]{32}$"));
            Assert.AreEqual(Path.Combine(first, "d"), run.Downloads);
            Assert.IsTrue(HelperProtectedRunFolder.IsFreshProtected(first, expectEmpty: false));
            Assert.IsTrue(HelperProtectedRunFolder.IsFreshProtected(run.Downloads, expectEmpty: true));
            DirectorySecurity security = new DirectoryInfo(first).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            Assert.AreEqual(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), security.GetOwner(typeof(SecurityIdentifier)));
            Assert.IsTrue(security.AreAccessRulesProtected, "nothing is inherited from the system Temp folder");
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                Assert.IsTrue(rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)) ||
                    rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null)), rule.IdentityReference.Value);
            File.WriteAllText(Path.Combine(run.Downloads, "manifest.yaml"), "x");
        }
        Assert.IsFalse(Directory.Exists(first), "the run folder is deleted with its contents");
        Assert.IsFalse(Directory.Exists(second));
    }

    [TestMethod]
    public void AFolderThatIsNotFreshlyProtectedIsRefused()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string plain = Directory.CreateTempSubdirectory("pcsteward-d4b-").FullName;
        try
        {
            Assert.IsFalse(HelperProtectedRunFolder.IsFreshProtected(plain, expectEmpty: true), "an inherited, user-owned folder is never accepted");
            Assert.IsFalse(HelperProtectedRunFolder.IsFreshProtected(Path.Combine(plain, "missing"), expectEmpty: true));
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }

    [TestMethod]
    public void TheProfilePathAcceptsOnlyALeadingSystemDrive()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string drive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        Assert.AreEqual(drive + @"\Users\Alice", HelperWingetSettings.ExpandSystemDrive(@"%SystemDrive%\Users\Alice"));
        Assert.AreEqual(drive + @"\Users\Alice", HelperWingetSettings.ExpandSystemDrive(@"%systemdrive%\Users\Alice"));
        Assert.AreEqual(@"D:\Profiles\Alice", HelperWingetSettings.ExpandSystemDrive(@"D:\Profiles\Alice"));
        Assert.IsNull(HelperWingetSettings.ExpandSystemDrive(@"%USERPROFILE%\x"));
        Assert.IsNull(HelperWingetSettings.ExpandSystemDrive(@"C:\Users\%USERNAME%"));
        Assert.IsNull(HelperWingetSettings.ExpandSystemDrive(@"%SystemDrive%\%x%"));
        Assert.IsFalse(HelperWingetSettings.Allows("S-1-5-21-1-2-3-999999"), "a SID with no machine profile entry refuses");
    }

    [TestMethod]
    public void AMissingOrMalformedRegistrationHasNoDigest()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string missing = "PCSteward-D4b-" + Guid.NewGuid().ToString("N");
        foreach (WinGetMachineRegistrationView view in new[] { WinGetMachineRegistrationView.Machine64, WinGetMachineRegistrationView.Machine32 })
        {
            Assert.IsNull(HelperMachineRegistration.TryDigest(view, missing, out string? version));
            Assert.IsNull(version);
            foreach (string bad in new[] { "", " ", @"a\b", "a/b", "a\nb", new string('x', 257) })
                Assert.IsNull(HelperMachineRegistration.TryDigest(view, bad, out _), bad);
        }
    }

    [TestMethod]
    public void TheTrustedClientIsFoundOnlyInTheAppInstallerPackage()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string? winget = HelperTrustedWinget.TryResolve();
        if (winget is null) Assert.Inconclusive("App Installer is not installed for this account (common on CI images).");
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
        StringAssert.StartsWith(winget, apps, StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(winget, "Microsoft.DesktopAppInstaller_", StringComparison.Ordinal);
        Assert.AreEqual("winget.exe", Path.GetFileName(winget), ignoreCase: true);
    }

    [TestMethod]
    [DataRow("Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_x64__8wekyb3d8bbwe", "x64")]
    [DataRow("Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_arm64__8wekyb3d8bbwe", "arm64")]
    [DataRow("Microsoft.DesktopAppInstaller_1.29.380.0_x86__8wekyb3d8bbwe", "x86")]
    [DataRow("Microsoft.DesktopAppInstaller_1.29.380.0_neutral_~_8wekyb3d8bbwe", "neutral")]
    [DataRow("Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_X64__8wekyb3d8bbwe", null)]
    [DataRow("Microsoft.VCLibs.140.00.UWPDesktop_14.0.33728.0_x64_8wekyb3d8bbwe", null)]
    [DataRow("Microsoft.VCLibs_14.0_x64__8wekyb3d8bbwe_extra", null)]
    [DataRow("", null)]
    [DataRow(null, null)]
    public void ThePackageArchitectureIsTheExactFolderNameField(string? folder, string? expected) =>
        Assert.AreEqual(expected, HelperTrustedWinget.PackageArchitecture(folder), folder);

    [TestMethod]
    public void AMalformedRegistrationDigestFindsNothing()
    {
        foreach (string bad in new[] { "", new string('a', 64), new string('A', 63), new string('A', 65), new string('G', 64) })
        {
            Assert.IsFalse(HelperMachineRegistration.TryFind(bad, out _, out string key, out string version), bad);
            Assert.AreEqual(string.Empty, key);
            Assert.AreEqual(string.Empty, version);
        }
    }

    [TestMethod]
    public void AnUnknownRegistrationDigestFindsNothing()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray()));
        Assert.IsFalse(HelperMachineRegistration.TryFind(digest, out WinGetMachineRegistrationView view, out string key, out string version));
        Assert.AreEqual(default, view);
        Assert.AreEqual(string.Empty, key);
        Assert.AreEqual(string.Empty, version);
    }

    [TestMethod]
    public void TheRuntimeFolderIsTheSameArchitectureFrameworkUnderWindowsApps()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string system = Environment.SystemDirectory;
        Assert.IsNull(HelperTrustedWinget.TryResolveRuntimeFolder(Path.Combine(system, "winget.exe")), "not a package folder");
        Assert.IsNull(HelperTrustedWinget.TryResolveRuntimeFolder(@"C:\x\Microsoft.DesktopAppInstaller_1.0.0.0_sparc__8wekyb3d8bbwe\winget.exe"));
        string? winget = HelperTrustedWinget.TryResolve();
        if (winget is null) Assert.Inconclusive("App Installer is not installed for this account (common on CI images).");
        string? runtime = HelperTrustedWinget.TryResolveRuntimeFolder(winget);
        if (runtime is null) Assert.Inconclusive("No same-architecture C++ runtime framework is installed here.");
        string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
        StringAssert.StartsWith(runtime, apps, StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(runtime, "Microsoft.VCLibs.140.00.UWPDesktop_", StringComparison.Ordinal);
        Assert.AreEqual(HelperTrustedWinget.PackageArchitecture(Path.GetFileName(Path.GetDirectoryName(winget))),
            HelperTrustedWinget.PackageArchitecture(Path.GetFileName(runtime)));
        Assert.IsTrue(File.Exists(Path.Combine(runtime, "vcruntime140.dll")));
    }

    [TestMethod]
    public void TheWinGetEnvironmentIsFixedAndTempIsTheRunFolder()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows-only; this row is Windows-CI evidence.");
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string run = Path.Combine(windows, "Temp", "PCSteward-" + new string('0', 32));
        string runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", "Runtime_1.0.0.0_x64__p");
        IReadOnlyDictionary<string, string> environment = HelperWingetEnvironment.Create(run, runtime);
        string[] expected =
        [
            "ALLUSERSPROFILE", "APPDATA", "ComSpec", "CommonProgramFiles", "CommonProgramFiles(x86)", "CommonProgramW6432", "LOCALAPPDATA",
            "OS", "PATH", "PATHEXT", "PROCESSOR_ARCHITECTURE", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432",
            "SystemDrive", "SystemRoot", "TEMP", "TMP", "USERPROFILE", "windir",
        ];
        CollectionAssert.AreEquivalent(expected, environment.Keys.ToArray());
        Assert.AreEqual(run, environment["TEMP"]);
        Assert.AreEqual(run, environment["TMP"]);
        string[] path = environment["PATH"].Split(';');
        Assert.AreEqual(Environment.SystemDirectory.TrimEnd('\\'), path[0], ignoreCase: true);
        Assert.AreEqual(runtime, path[^1]);
        Assert.IsFalse(HelperWingetEnvironment.Create(run, null)["PATH"].Contains("WindowsApps", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(environment.Values.Any(static value => value.Contains('%', StringComparison.Ordinal)), "nothing left to expand");

        foreach (string bad in new[] { "", "relative", @"\\server\share", @"C:\a;b", @"C:\a\..\b", @"C:\a%TEMP%", "C:/a", @"C:\a:b", "C:\\a\n" })
            Assert.ThrowsExactly<ArgumentException>(() => HelperWingetEnvironment.Create(bad, null), bad);
        Assert.ThrowsExactly<ArgumentException>(() => HelperWingetEnvironment.Create(run, @"C:\a;C:\evil"));
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static ReviewedUpdateOperation Operation(string installerType = "msi", UpdateArchitecture architecture = UpdateArchitecture.X64)
    {
        DateTimeOffset at = new(2026, 11, 12, 21, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("d4b00000-0000-0000-0000-000000000001"),
            Guid.Parse("d4b00000-0000-0000-0000-000000000002"), new UpdateJobSelection("arp:hklm64:7zip", UpdateProvider.WinGet,
                UpdateSource.WinGet, "7zip.7zip", "24.09", "25.01", UpdateScope.Machine, architecture, UpdateChannel.Unknown, "mapping-v2"))
            .Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "arp:hklm64:7zip", "machine:winget",
            $"wgm1:{installerType}:{Registration}:{Manifest}:{Installer}", at, UpdateRouteContract.WinGetMachineSilentV1,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }

    private static ReviewedUpdateOperation UserOperation()
    {
        DateTimeOffset at = new(2026, 11, 12, 21, 0, 0, TimeSpan.Zero);
        ImmutableUpdateJob job = new UpdateJobReview(Guid.Parse("d4b00000-0000-0000-0000-000000000003"),
            Guid.Parse("d4b00000-0000-0000-0000-000000000004"), new UpdateJobSelection("arp:hkcu:widget", UpdateProvider.WinGet,
                UpdateSource.WinGet, "Contoso.Widget", "1.0.0", "1.1.0", UpdateScope.User, UpdateArchitecture.X64, UpdateChannel.Unknown,
                "mapping-v2")).Confirm(at);
        return new(job, "S-1-5-21-11-22-33-1001", "arp:hkcu:widget", "user:winget", "evidence:1", at, UpdateRouteContract.WinGetUserSilentV1,
            ReviewedInstallerMode.Selected(InstallerMode.Silent));
    }
}
