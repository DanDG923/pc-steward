using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-034 §1 and §5: the PC Steward MSI source. One per-machine package with a fixed UpgradeCode, major upgrades and a
/// refused downgrade; no element that runs code, installs a service or adds a WiX extension; the protected ADR-022 DACL on
/// the install folder; refusal beside the owner-only state folder and over a folder no MSI owns; the WiX SDK pinned; and the
/// CI job that installs it, which writes its throwaway pin into its own checkout only and puts the committed file back.
/// </summary>
[TestClass]
public sealed class MsiPackageBoundaryTests
{
    private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";

    [TestMethod]
    public void ThePackageIsPerMachineWithAFixedUpgradeCodeAndNoCode()
    {
        string source = File.ReadAllText(Path.Combine(Msi(), "Package.wxs"));
        XDocument document = XDocument.Parse(source);
        XElement package = document.Root!.Element(Wix + "Package")!;
        Assert.AreEqual("perMachine", (string?)package.Attribute("Scope"));
        Assert.AreEqual("2AD17330-BAD5-46BE-A409-20814B26673C", (string?)package.Attribute("UpgradeCode"));
        Assert.AreEqual("$(var.ProductVersion)", (string?)package.Attribute("Version"));
        XElement upgrade = package.Element(Wix + "MajorUpgrade")!;
        Assert.IsNotNull(upgrade.Attribute("DowngradeErrorMessage"), "a lower version is refused");
        Assert.IsNull(upgrade.Attribute("AllowDowngrades"));
        Assert.IsNull(upgrade.Attribute("AllowSameVersionUpgrades"));

        // Only the core WiX namespace: no util, firewall, netfx or other extension.
        string[] namespaces = [.. document.Descendants().Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => attribute.Value).Distinct()];
        CollectionAssert.AreEqual(new[] { Wix.NamespaceName }, namespaces);
        foreach (string forbidden in new[]
        {
            "CustomAction", "CustomActionRef", "Binary", "ServiceInstall", "ServiceControl", "ServiceConfig", "SetProperty",
            "SetDirectory", "Custom", "InstallExecuteSequence", "InstallUISequence", "UI", "UIRef", "Environment", "RemoveFolderEx",
            "Permission", "ExeCommand", "ScheduledTask", "BootstrapperApplication", "Bundle",
        })
            Assert.IsFalse(document.Descendants(Wix + forbidden).Any(), $"Package.wxs declares {forbidden}");

        // The protected DACL, on the install folder only, and only through MsiLockPermissionsEx.
        XElement[] permissions = [.. document.Descendants(Wix + "PermissionEx")];
        Assert.HasCount(1, permissions);
        Assert.AreEqual("D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)", (string?)permissions[0].Attribute("Sddl"));
        Assert.AreEqual("INSTALLFOLDER", (string?)permissions[0].Parent!.Parent!.Attribute("Directory"));
        XElement folder = document.Descendants(Wix + "Directory").Single(directory => (string?)directory.Attribute("Id") == "INSTALLFOLDER");
        Assert.AreEqual("PC Steward", (string?)folder.Attribute("Name"));
        Assert.AreEqual("ProgramFiles64Folder", (string?)folder.Parent!.Attribute("Id"));
        Assert.AreEqual(1, Regex.Count(source, "<Files Include="), "the payload folder is the only file source");
        StringAssert.Contains(source, "<Files Include=\"$(var.PayloadDir)\\**\">");
        // The one file declared by hand is the desktop executable, which carries the Start menu shortcut.
        Assert.AreEqual(1, document.Descendants(Wix + "File").Count());
        Assert.AreEqual("$(var.PayloadDir)\\PCSteward.Desktop.exe", (string?)document.Descendants(Wix + "File").Single().Attribute("Source"));
        Assert.AreEqual("$(var.PayloadDir)\\PCSteward.Desktop.exe", (string?)document.Descendants(Wix + "Exclude").Single().Attribute("Files"));
        Assert.IsFalse(document.Descendants(Wix + "RegistryValue").Any() || document.Descendants(Wix + "RegistryKey").Any(), "the MSI writes no registry values of its own");
    }

    [TestMethod]
    public void TheInstallIsRefusedBesideTheOwnerOnlyDeploymentOrOverAnUnownedFolder()
    {
        string source = File.ReadAllText(Path.Combine(Msi(), "Package.wxs"));
        string reader = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Execution", "OwnerOnlyDeploymentCapabilityReader.cs"));
        Match stateName = Regex.Match(reader, @"internal const string StateDirectoryName = ""([^""]+)"";");
        Assert.IsTrue(stateName.Success);
        StringAssert.Contains(source, $"Path=\"[ProgramFiles64Folder]{stateName.Groups[1].Value}\" Depth=\"0\" AssignToProperty=\"yes\"");
        StringAssert.Contains(source, "Path=\"[ProgramFiles64Folder]PC Steward\" Depth=\"0\" AssignToProperty=\"yes\"");
        StringAssert.Contains(source, "<Launch Condition=\"Installed OR NOT PCSTEWARD_OWNERONLY_STATE\"");
        StringAssert.Contains(source, "<Launch Condition=\"Installed OR WIX_UPGRADE_DETECTED OR WIX_DOWNGRADE_DETECTED OR NOT PCSTEWARD_EXISTING_FOLDER\"");
    }

    [TestMethod]
    public void TheWixSdkIsPinnedAndTheBuildScriptChecksTheTables()
    {
        string project = File.ReadAllText(Path.Combine(Msi(), "PCSteward.Installer.wixproj"));
        StringAssert.StartsWith(project, "<Project Sdk=\"WixToolset.Sdk/5.0.2\">");
        Assert.IsFalse(project.Contains("PackageReference", StringComparison.Ordinal), "no WiX extension package");
        Assert.IsFalse(File.ReadAllText(Path.Combine(FindRepositoryRoot(), "PCSteward.slnx")).Contains("PCSteward.Installer", StringComparison.Ordinal),
            "the MSI is built only by New-PCStewardMsi.ps1");

        string script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", "New-PCStewardMsi.ps1"));
        StringAssert.Contains(script, "@('ServiceInstall', 'ServiceControl', 'MsiServiceConfig', 'MsiServiceConfigFailureActions', 'Binary')");
        StringAssert.Contains(script, "if ($action[1] -cne '19' -or $action[2] -ne '')");
        StringAssert.Contains(script, "$script:FolderSddl = 'D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)'");
        StringAssert.Contains(script, "$script:UpgradeCode = '{2AD17330-BAD5-46BE-A409-20814B26673C}'");
        Assert.IsFalse(Regex.IsMatch(script, @"Set-AuthenticodeSignature|Cert:\\|msiexec", RegexOptions.IgnoreCase), "building signs and installs nothing");
    }

    [TestMethod]
    public void TheCiJobInstallsOnAHostedRunnerAndNeverCommitsItsPin()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        int start = workflow.IndexOf("  msi-install-test:", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, start, "the MSI install job is missing");
        int end = workflow.IndexOf("\n  native-helper-signing-diagnostic:", start, StringComparison.Ordinal);
        string job = workflow[start..end];
        StringAssert.Contains(job, "runs-on: windows-latest");
        StringAssert.Contains(job, "persist-credentials: false");
        StringAssert.Contains(job, "run: pwsh -NoProfile -File ./scripts/Test-PCStewardMsiCI.ps1");
        Assert.IsFalse(job.Contains("continue-on-error", StringComparison.Ordinal), "the MSI install test gates");

        string script = File.ReadAllText(Path.Combine(root, "scripts", "Test-PCStewardMsiCI.ps1"));
        StringAssert.Contains(script, "if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$isAdministrator)");
        StringAssert.Contains(script, "$placeholder = 'internal const string Primary = \"\";'");
        StringAssert.Contains(script, "[IO.File]::WriteAllBytes($pinsFile, $pinsOriginal)");
        // The committed file is read before the main try block and written back in its finally block.
        Assert.IsLessThan(script.IndexOf("\ntry {", StringComparison.Ordinal), script.IndexOf("$pinsOriginal = [IO.File]::ReadAllBytes($pinsFile)", StringComparison.Ordinal));
        Assert.IsGreaterThan(script.LastIndexOf("finally {", StringComparison.Ordinal), script.IndexOf("[IO.File]::WriteAllBytes($pinsFile, $pinsOriginal)", StringComparison.Ordinal));
        // The key never enters a trust store.
        Assert.IsFalse(Regex.IsMatch(script, @"Cert:\\(LocalMachine|CurrentUser)\\(Root|TrustedPublisher|CA)", RegexOptions.IgnoreCase));
    }

    private static string Msi() => Path.Combine(FindRepositoryRoot(), "packaging", "msi");

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
