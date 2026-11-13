using PCSteward.Application;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// D4b 32-bit-registered apps (design note PHASE_D4B_32BIT_REGISTERED_APPS_DESIGN §3.3–3.4; owner accepted P1 and P2 on 2026-11-13):
/// the strict reader on the exact manifests that WinGet 1.29.380 exported on the owner's PC on 2026-11-13 (product facts only:
/// .NET Desktop Runtime 8 x64 at 8.0.33 and 8.0.34, Visual Studio Build Tools 2022 at 17.14.45), and the proof rules on them.
/// </summary>
[TestClass]
public sealed class WingetArchitectureProofManifestTests
{
    private const string Runtime8033Code = "{4d0027d4-962f-4f65-a01b-f8764a5f4220}";
    private const string Runtime8034Code = "{de6ce2b1-81f5-406c-a0ca-534ccdf2ad7e}";

    [TestMethod]
    public void TheRuntimeManifestsDeclareTheUninstallEntryTheirX64InstallerWrites()
    {
        WingetManifestFacts installed = Read(Runtime8033, "Microsoft.DotNet.DesktopRuntime.8", "8.0.33");
        WingetManifestFacts target = Read(Runtime8034, "Microsoft.DotNet.DesktopRuntime.8", "8.0.34");

        Assert.AreEqual(WinGetMachineInstallerType.Burn, installed.InstallerType);
        Assert.AreEqual(Runtime8033Code, installed.ProductCode);
        CollectionAssert.AreEqual(new[] { new WingetDeclaredEntry(Runtime8033Code, "8.0.33.36622") }, installed.DeclaredEntries!.ToArray());
        Assert.AreEqual(Runtime8034Code, target.ProductCode);
        Assert.AreEqual(new ExpectedUninstallEntry(Runtime8034Code, "8.0.34.36622"), WingetArchitectureProofRules.DeclaredEntry(target));
    }

    [TestMethod]
    public void TheBuildToolsManifestDeclaresNoProductCodeOrEntry()
    {
        WingetManifestFacts facts = Read(BuildTools171445, "Microsoft.VisualStudio.2022.BuildTools", "17.14.45");

        Assert.AreEqual(WinGetMachineInstallerType.Exe, facts.InstallerType);
        Assert.IsNull(facts.ProductCode);
        Assert.IsNotNull(facts.DeclaredEntries);
        Assert.IsEmpty(facts.DeclaredEntries);
        Assert.IsNull(WingetArchitectureProofRules.DeclaredEntry(facts));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesProductCode(facts, new("17.14.45", "a1b2c3d4", "17.14.45")), "no product code proves nothing");
    }

    [TestMethod]
    public void P1NeedsTheEntrysKeyAndDisplayVersionAmongWhatTheInstalledVersionDeclares()
    {
        WingetManifestFacts installed = Read(Runtime8033, "Microsoft.DotNet.DesktopRuntime.8", "8.0.33");

        Assert.IsTrue(WingetArchitectureProofRules.ProvesProductCode(installed, new("8.0.33", Runtime8033Code, "8.0.33.36622")));
        Assert.IsTrue(WingetArchitectureProofRules.ProvesProductCode(installed, new("8.0.33", Runtime8033Code.ToUpperInvariant(), "8.0.33.36622")),
            "product codes compare without case");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesProductCode(installed, new("8.0.33", Runtime8033Code, "8.0.33.36623")), "other DisplayVersion");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesProductCode(installed, new("8.0.33", Runtime8033Code, "8.0.33")),
            "WinGet's version is not the entry's DisplayVersion when the installer declares one");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesProductCode(installed, new("8.0.33", "{0aa1b8a3-0000-4000-8000-000000000000}", "8.0.33.36622")),
            "another key, for example the x86 installer's");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesProductCode(installed with { DeclaredEntries = null }, new("8.0.33", Runtime8033Code, "8.0.33.36622")),
            "an unreadable entry list proves nothing");
        WingetManifestFacts codeOnly = installed with { DeclaredEntries = [] };
        Assert.IsTrue(WingetArchitectureProofRules.ProvesProductCode(codeOnly, new("8.0.33", Runtime8033Code, "8.0.33")),
            "the installer's own product code, at WinGet's version");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesProductCode(codeOnly, new("8.0.33", Runtime8033Code, "8.0.33.36622")));
    }

    [TestMethod]
    public void TheDeclaredEntryMustBeOneCompleteItemAgreeingWithTheInstaller()
    {
        WingetManifestFacts target = Read(Runtime8034, "Microsoft.DotNet.DesktopRuntime.8", "8.0.34");
        WingetDeclaredEntry entry = new(Runtime8034Code, "8.0.34.36622");

        Assert.IsNull(WingetArchitectureProofRules.DeclaredEntry(target with { DeclaredEntries = [entry, entry with { ProductCode = Runtime8033Code }] }), "two entries");
        Assert.IsNull(WingetArchitectureProofRules.DeclaredEntry(target with { DeclaredEntries = [entry with { DisplayVersion = null }] }), "no DisplayVersion");
        Assert.IsNull(WingetArchitectureProofRules.DeclaredEntry(target with { DeclaredEntries = [entry with { ProductCode = null }] }), "no product code");
        Assert.IsNull(WingetArchitectureProofRules.DeclaredEntry(target with { ProductCode = Runtime8033Code }), "disagrees with the installer");
        Assert.IsNull(WingetArchitectureProofRules.DeclaredEntry(target with { DeclaredEntries = null }), "unreadable");
        Assert.AreEqual(new ExpectedUninstallEntry(Runtime8034Code, "8.0.34.36622"), WingetArchitectureProofRules.DeclaredEntry(target with { ProductCode = null }));
    }

    [TestMethod]
    public void AnEntryListInAnotherLayoutIsUnreadableButStillAManifest()
    {
        string shifted = Runtime8034.Replace("    DisplayVersion: 8.0.34.36622", "     DisplayVersion: 8.0.34.36622", StringComparison.Ordinal);
        string repeated = Runtime8034.Replace("    DisplayVersion: 8.0.34.36622", "    DisplayVersion: 8.0.34.36622\n    DisplayVersion: 8.0.34.1", StringComparison.Ordinal);

        Assert.IsNull(Read(shifted, "Microsoft.DotNet.DesktopRuntime.8", "8.0.34").DeclaredEntries);
        Assert.IsNull(Read(repeated, "Microsoft.DotNet.DesktopRuntime.8", "8.0.34").DeclaredEntries);
    }

    [TestMethod]
    public void P2NeedsEveryVersionOnceWithAnX64MachineInstallerAndNoX86One()
    {
        string[] versions = ["17.14.44", "17.14.45"];
        WinGetInstallerArchitectureObservation proving = new("Microsoft.VisualStudio.2022.BuildTools", true, true, true, 1, false,
            "Microsoft.VisualStudio.2022.BuildTools", [new("17.14.44", 1, true, false), new("17.14.45", 1, true, false)]);

        Assert.IsTrue(WingetArchitectureProofRules.ProvesSingleArchitecture(proving, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(null, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { CatalogIsTrusted = false }, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { CatalogIsPredefined = false }, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { ExactIdMatch = false }, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { MatchCount = 2 }, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { WasLimitExceeded = true }, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { PackageId = "Other" }, "Microsoft.VisualStudio.2022.BuildTools", versions));
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving, "Microsoft.VisualStudio.2022.BuildTools", [.. versions, "17.14.46"]), "unknown version");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { Versions = [new("17.14.44", 1, true, true), new("17.14.45", 1, true, false)] },
            "Microsoft.VisualStudio.2022.BuildTools", versions), "the installed version had an x86 installer");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { Versions = [new("17.14.44", 1, true, false), new("17.14.45", 1, false, false)] },
            "Microsoft.VisualStudio.2022.BuildTools", versions), "no x64 machine installer");
        Assert.IsFalse(WingetArchitectureProofRules.ProvesSingleArchitecture(proving with { Versions = [new("17.14.44", 2, true, false), new("17.14.45", 1, true, false)] },
            "Microsoft.VisualStudio.2022.BuildTools", versions), "the version twice in the catalog");
    }

    private static WingetManifestFacts Read(string manifest, string packageId, string version)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(manifest.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) + "\r\n");
        WingetManifestRefusal refusal = WingetExportedManifest.Read(bytes, new(packageId, version, "x64", null), out WingetManifestFacts? facts);
        Assert.AreEqual(WingetManifestRefusal.None, refusal);
        return facts!;
    }

    // Exported by `winget download --scope machine --architecture x64` (WinGet 1.29.380) on 2026-11-13; CRLF in the original, restored by Read.
    private const string Runtime8033 = """
        PackageIdentifier: Microsoft.DotNet.DesktopRuntime.8
        PackageVersion: 8.0.33
        Moniker: dotnet-desktop-8
        Tags:
        - .NET
        - .NET Core
        - dotnet
        - net
        - C#
        - csharp
        - F#
        - fsharp
        - VB
        - Visual Basic
        - Windows Desktop
        - Windows
        - runtime
        PackageLocale: en-US
        ShortDescription: .NET is a free, cross-platform, open-source developer platform for
          building many different types of applications.
        License: MIT
        PackageName: Microsoft .NET Windows Desktop Runtime 8.0
        PackageUrl: https://dotnet.microsoft.com
        Publisher: Microsoft Corporation
        Installers:
        - Architecture: x64
          InstallerType: burn
          InstallerSha256: 4de14de0b327e801d7a691cb1afc8a00db5313a698ef53b979fc6f84fce631de
          InstallerUrl: https://download.microsoft.com/download/9d8b4560-78de-40b0-91f9-d0a48dda818b/2317f3da-b666-45ac-baa4-72da1586d33c/windowsdesktop-runtime-8.0.33-win-x64.exe
          Scope: machine
          InstallerAbortsTerminal: false
          InstallLocationRequired: false
          RequireExplicitUpgrade: false
          DisplayInstallWarnings: false
          DownloadCommandProhibited: false
          ArchiveBinariesDependOnPath: false
          MinimumOSVersion: 6.1.7601
          ProductCode: '{4d0027d4-962f-4f65-a01b-f8764a5f4220}'
          UpgradeBehavior: install
          AppsAndFeaturesEntries:
          - DisplayName: Microsoft Windows Desktop Runtime - 8.0.33 (x64)
            DisplayVersion: 8.0.33.36622
            ProductCode: '{4d0027d4-962f-4f65-a01b-f8764a5f4220}'
            Publisher: Microsoft Corporation
          ExpectedReturnCodes:
          - InstallerReturnCode: 87
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 112
            ReturnResponse: diskFull
          - InstallerReturnCode: 1601
            ReturnResponse: contactSupport
          - InstallerReturnCode: 1602
            ReturnResponse: cancelledByUser
          - InstallerReturnCode: 1618
            ReturnResponse: installInProgress
          - InstallerReturnCode: 1623
            ReturnResponse: systemNotSupported
          - InstallerReturnCode: 1625
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1628
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 1633
            ReturnResponse: systemNotSupported
          - InstallerReturnCode: 1638
            ReturnResponse: alreadyInstalled
          - InstallerReturnCode: 1639
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 1640
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1641
            ReturnResponse: rebootInitiated
          - InstallerReturnCode: 1643
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1644
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1649
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1650
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 1654
            ReturnResponse: systemNotSupported
          - InstallerReturnCode: 3010
            ReturnResponse: rebootRequiredToFinish
          InstallerSwitches:
            Custom: /norestart
            Silent: /quiet
            SilentWithProgress: /passive
            Log: /log "<LOGPATH>"
            InstallLocation: TARGETDIR="<INSTALLPATH>"
        ManifestVersion: 1.12.0
        ManifestType: merged
        """;

    private const string Runtime8034 = """
        PackageIdentifier: Microsoft.DotNet.DesktopRuntime.8
        PackageVersion: 8.0.34
        Moniker: dotnet-desktop-8
        Tags:
        - .NET
        - .NET Core
        - dotnet
        - net
        - C#
        - csharp
        - F#
        - fsharp
        - VB
        - Visual Basic
        - Windows Desktop
        - Windows
        - runtime
        PackageLocale: en-US
        ShortDescription: .NET is a free, cross-platform, open-source developer platform for
          building many different types of applications.
        License: MIT
        PackageName: Microsoft .NET Windows Desktop Runtime 8.0
        PackageUrl: https://dotnet.microsoft.com
        Publisher: Microsoft Corporation
        Installers:
        - Architecture: x64
          InstallerType: burn
          InstallerSha256: 37aa8a02acc1973ddd7e3de993a02007ccef26b48fa35b09d087fa8ab4602795
          InstallerUrl: https://download.microsoft.com/download/f5126c7f-c450-4883-a6ac-db5a8625bd16/3a6bc153-80ae-4859-80d1-3f251d2485e0/windowsdesktop-runtime-8.0.34-win-x64.exe
          Scope: machine
          InstallerAbortsTerminal: false
          InstallLocationRequired: false
          RequireExplicitUpgrade: false
          DisplayInstallWarnings: false
          DownloadCommandProhibited: false
          ArchiveBinariesDependOnPath: false
          MinimumOSVersion: 6.1.7601
          ProductCode: '{de6ce2b1-81f5-406c-a0ca-534ccdf2ad7e}'
          UpgradeBehavior: install
          AppsAndFeaturesEntries:
          - DisplayName: Microsoft Windows Desktop Runtime - 8.0.34 (x64)
            DisplayVersion: 8.0.34.36622
            ProductCode: '{de6ce2b1-81f5-406c-a0ca-534ccdf2ad7e}'
            Publisher: Microsoft Corporation
          ExpectedReturnCodes:
          - InstallerReturnCode: 87
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 112
            ReturnResponse: diskFull
          - InstallerReturnCode: 1601
            ReturnResponse: contactSupport
          - InstallerReturnCode: 1602
            ReturnResponse: cancelledByUser
          - InstallerReturnCode: 1618
            ReturnResponse: installInProgress
          - InstallerReturnCode: 1623
            ReturnResponse: systemNotSupported
          - InstallerReturnCode: 1625
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1628
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 1633
            ReturnResponse: systemNotSupported
          - InstallerReturnCode: 1638
            ReturnResponse: alreadyInstalled
          - InstallerReturnCode: 1639
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 1640
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1641
            ReturnResponse: rebootInitiated
          - InstallerReturnCode: 1643
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1644
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1649
            ReturnResponse: blockedByPolicy
          - InstallerReturnCode: 1650
            ReturnResponse: invalidParameter
          - InstallerReturnCode: 1654
            ReturnResponse: systemNotSupported
          - InstallerReturnCode: 3010
            ReturnResponse: rebootRequiredToFinish
          InstallerSwitches:
            Custom: /norestart
            Silent: /quiet
            SilentWithProgress: /passive
            Log: /log "<LOGPATH>"
            InstallLocation: TARGETDIR="<INSTALLPATH>"
        ManifestVersion: 1.12.0
        ManifestType: merged
        """;

    private const string BuildTools171445 = """
        PackageIdentifier: Microsoft.VisualStudio.2022.BuildTools
        PackageVersion: 17.14.45
        Moniker: vs2022-buildtools
        Tags:
        - c#
        - c++
        - vs
        PackageLocale: en-US
        Copyright: Copyright (c) Microsoft Corporation. All rights reserved.
        ShortDescription: These Build Tools allow you to build Visual Studio projects from
          a command-line interface.
        License: Proprietary
        LicenseUrl: https://visualstudio.microsoft.com/license-terms/
        PackageName: Visual Studio BuildTools 2022
        PackageUrl: https://visualstudio.microsoft.com/
        Publisher: Microsoft Corporation
        Installers:
        - Architecture: x64
          InstallerType: exe
          InstallerSha256: 310c13b8238876afb4e823a91112230ee2d2c5a45c431d4062af7aadedff9c4c
          InstallerUrl: https://download.visualstudio.microsoft.com/download/pr/95995f57-8355-4ff5-8d90-a7e1669fb3b3/310c13b8238876afb4e823a91112230ee2d2c5a45c431d4062af7aadedff9c4c/vs_BuildTools.exe
          Scope: machine
          InstallerAbortsTerminal: false
          InstallLocationRequired: false
          RequireExplicitUpgrade: false
          DisplayInstallWarnings: false
          DownloadCommandProhibited: false
          ArchiveBinariesDependOnPath: false
          UpgradeBehavior: install
          Commands:
          - devenv
          ExpectedReturnCodes:
          - InstallerReturnCode: 740
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 1001
            ReturnResponse: installInProgress
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 1003
            ReturnResponse: fileInUse
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 1618
            ReturnResponse: installInProgress
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 1641
            ReturnResponse: rebootInitiated
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 3010
            ReturnResponse: rebootRequiredToFinish
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5001
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5002
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5003
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5004
            ReturnResponse: cancelledByUser
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5005
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5007
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 5008
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8001
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8002
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8003
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8004
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8005
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8006
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8007
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8008
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 8009
            ReturnResponse: blockedByPolicy
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 2148734213
            ReturnResponse: custom
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          - InstallerReturnCode: 3221246609
            ReturnResponse: noNetwork
            ReturnResponseUrl: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/installation/issues-workarounds-visual-studio-setup
          InstallerSwitches:
            Custom: --wait --campaign "winget"
            Silent: --quiet
            SilentWithProgress: --passive
            Upgrade: update
        ManifestVersion: 1.9.0
        ManifestType: merged
        """;
}
