using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-003 amendment 1 (Task 54B): WinGet COM 1.11 and later ships only a <c>windows10.0.26100</c> projection, so every
/// Windows project targets <c>net10.0-windows10.0.26100.0</c>. The supported minimum stays Windows 11 22000: each Windows
/// project states <c>SupportedOSPlatformVersion</c> and <c>TargetPlatformMinVersion</c> 10.0.22000.0 itself, so a later edit
/// (or a newer SDK default) cannot raise it silently. The two WinGet COM packages share one exact pin, and the packaging
/// evidence and the shipped in-process runtime leaves follow that pin.
/// </summary>
[TestClass]
public sealed partial class WindowsPlatformTargetTests
{
    private const string WindowsTargetFramework = "net10.0-windows10.0.26100.0";
    private const string MinimumWindowsVersion = "10.0.22000.0";
    private const string ComInterop = "Microsoft.WindowsPackageManager.ComInterop";
    private const string InProcCom = "Microsoft.WindowsPackageManager.InProcCom";
    private static readonly string[] ProjectFolders = ["src", "tests"];

    private static readonly string[] WindowsProjects =
    [
        "src/PCSteward.Desktop/PCSteward.Desktop.csproj",
        "src/PCSteward.ElevatedHelper/PCSteward.ElevatedHelper.csproj",
        "src/PCSteward.Execution/PCSteward.Execution.csproj",
        "src/PCSteward.Inventory.Windows/PCSteward.Inventory.Windows.csproj",
        "src/PCSteward.Provider.Chocolatey/PCSteward.Provider.Chocolatey.csproj",
        "src/PCSteward.Provider.WinGet/PCSteward.Provider.WinGet.csproj",
        "src/PCSteward.ScheduledHost/PCSteward.ScheduledHost.csproj",
        "src/PCSteward.Scheduling.Windows/PCSteward.Scheduling.Windows.csproj",
        "src/PCSteward.SelfUpdate/PCSteward.SelfUpdate.csproj",
        "src/PCSteward.Storage/PCSteward.Storage.csproj",
        "tests/PCSteward.Application.IntegrationTests/PCSteward.Application.IntegrationTests.csproj",
        "tests/PCSteward.InstallCheck/PCSteward.InstallCheck.csproj",
        "tests/PCSteward.Security.FixtureTests/PCSteward.Security.FixtureTests.csproj",
    ];

    [TestMethod]
    public void EveryWindowsProjectTargets26100AndStatesTheWindows11Minimum()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        List<string> found = [];
        foreach (string path in ProductAndTestProjects(root))
        {
            XDocument project = XDocument.Load(path);
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            Assert.IsFalse(project.Descendants("TargetFrameworks").Any(), $"{relative} must target one framework");
            string[] frameworks = [.. project.Descendants("TargetFramework").Select(element => element.Value.Trim())];
            Assert.IsTrue(frameworks.Length <= 1, $"{relative} sets TargetFramework more than once");
            string[] minimumElements = [.. project.Descendants("SupportedOSPlatformVersion").Concat(project.Descendants("TargetPlatformMinVersion")).Select(element => element.Name.LocalName)];
            if (frameworks.Length == 0 || !frameworks[0].Contains("-windows", StringComparison.Ordinal))
            {
                Assert.AreEqual(0, minimumElements.Length, $"{relative} is not a Windows project and must not state a Windows minimum");
                continue;
            }

            found.Add(relative);
            Assert.AreEqual(WindowsTargetFramework, frameworks[0], relative);
            AssertSingleValue(project, "SupportedOSPlatformVersion", MinimumWindowsVersion, relative);
            AssertSingleValue(project, "TargetPlatformMinVersion", MinimumWindowsVersion, relative);
        }

        CollectionAssert.AreEquivalent(WindowsProjects, found, "the set of Windows projects changed; add it here with the same target and minimum");
    }

    [TestMethod]
    public void SharedBuildFilesNeitherRaiseNorOverrideTheMinimum()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        // The repository root and every product and test folder; spikes keep their own throwaway build files.
        string[] shared = [.. Directory.EnumerateFiles(root, "Directory.Build.*", SearchOption.TopDirectoryOnly)
            .Concat(ProjectFolders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "Directory.Build.*", SearchOption.AllDirectories)))
            .Where(path => !IsIgnored(root, path))];
        Assert.IsTrue(shared.Length > 0);
        foreach (string path in shared)
        {
            string text = File.ReadAllText(path);
            foreach (string property in new[] { "SupportedOSPlatformVersion", "TargetPlatformMinVersion", "TargetPlatformVersion", "WindowsSdkPackageVersion" })
                Assert.IsFalse(text.Contains(property, StringComparison.Ordinal), $"{Path.GetRelativePath(root, path)} sets {property}");
        }
    }

    [TestMethod]
    public void BothWinGetComPackagesShareOneExactStablePinThatThePackagingEvidenceFollows()
    {
        string root = CompiledIlScanner.FindRepositoryRoot();
        XDocument packages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        string Pin(string id) => packages.Descendants("PackageVersion").Single(element => (string?)element.Attribute("Include") == id).Attribute("Version")!.Value;
        string version = Pin(ComInterop);
        Assert.AreEqual(version, Pin(InProcCom), "ComInterop and InProcCom must be the same release");
        Assert.IsTrue(StableVersion().IsMatch(version), $"WinGet COM pin {version} must be an exact stable version");

        string package = File.ReadAllText(Path.Combine(root, "scripts", "New-PrivateBetaPackage.ps1"));
        StringAssert.Contains(package, $"$inProcComVersion = '{version}'");
        StringAssert.Contains(package, $"'{ComInterop}/{version}' = @{{");
        StringAssert.Contains(package, $"'{InProcCom}/{version}' = @{{");
        StringAssert.Contains(package, $@"src\PCSteward.Desktop\bin\Release\{WindowsTargetFramework}\win-x64");
        string evidence = File.ReadAllText(Path.Combine(root, "scripts", "Test-PrivateBetaPackageEvidence.ps1"));
        StringAssert.Contains(evidence, $"$expectedInProcComKey = '{InProcCom}/{version}'");
        foreach (string text in new[] { package, evidence })
            Assert.AreEqual(0, OtherWinGetComVersion().Matches(text).Count(match => match.Groups["v"].Value != version), "a stale WinGet COM version is left in the packaging scripts");
    }

    [TestMethod]
    public void DesktopShipsTheInProcComWinmdAndNotTheComInteropCopy()
    {
        // From 1.28 the two winmd files differ in bytes; the payload ships the one that belongs to the in-process runtime.
        string root = CompiledIlScanner.FindRepositoryRoot();
        XDocument desktop = XDocument.Load(Path.Combine(root, "src", "PCSteward.Desktop", "PCSteward.Desktop.csproj"));
        Assert.AreEqual("false", desktop.Descendants("MicrosoftManagementDeployment-OutputWinMD").Single().Value);
        Assert.AreEqual("none", desktop.Descendants("MicrosoftManagementDeployment-FactoryLinkage").Single().Value);
        XElement winmd = desktop.Descendants("Content").Single(element => (string?)element.Attribute("Link") == "Microsoft.Management.Deployment.winmd");
        Assert.AreEqual(@"$(PkgMicrosoft_WindowsPackageManager_InProcCom)\runtimes\win-x64\native\Microsoft.Management.Deployment.winmd", (string?)winmd.Attribute("Include"));
        Assert.AreEqual("PreserveNewest", (string?)winmd.Attribute("CopyToOutputDirectory"));

        string package = File.ReadAllText(Path.Combine(root, "scripts", "New-PrivateBetaPackage.ps1"));
        StringAssert.Contains(package, "PayloadPath = 'Microsoft.Management.Deployment.winmd'; PackagePath = 'runtimes/win-x64/native/Microsoft.Management.Deployment.winmd'");
        string evidence = File.ReadAllText(Path.Combine(root, "scripts", "Test-PrivateBetaPackageEvidence.ps1"));
        StringAssert.Contains(evidence, "'Microsoft.Management.Deployment.winmd' = 'runtimes/win-x64/native/Microsoft.Management.Deployment.winmd'");
    }

    private static void AssertSingleValue(XDocument project, string property, string expected, string relative)
    {
        string[] values = [.. project.Descendants(property).Select(element => element.Value.Trim())];
        Assert.AreEqual(1, values.Length, $"{relative} must state {property} exactly once");
        Assert.AreEqual(expected, values[0], $"{relative} {property}");
        Assert.IsNull(project.Descendants(property).Single().Attribute("Condition"), $"{relative} {property} must be unconditional");
        Assert.IsNull(project.Descendants(property).Single().Parent?.Attribute("Condition"), $"{relative} {property} must be in an unconditional group");
    }

    private static IEnumerable<string> ProductAndTestProjects(string root) =>
        ProjectFolders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !IsIgnored(root, path)).Order(StringComparer.Ordinal);

    private static bool IsIgnored(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj" or ".git" or "artifacts" or ".localtest");

    [GeneratedRegex(@"^\d+\.\d+\.\d+$")]
    private static partial Regex StableVersion();

    [GeneratedRegex(@"Microsoft\.WindowsPackageManager\.(?:ComInterop|InProcCom)/(?<v>\d+\.\d+\.\d+)")]
    private static partial Regex OtherWinGetComVersion();
}
