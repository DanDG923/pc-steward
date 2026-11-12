using System.Security.Cryptography;
using System.Text;
using PCSteward.Domain;
using PCSteward.Provider.WinGet;

namespace PCSteward.Security.FixtureTests;

/// <summary>
/// Phase D stage D4b design §5 H7b and §7 (FR-UPD-15, TM-03/04/13; owner answers Q1 and Q3, 2026-11-12): the strict reader for
/// the merged single-installer manifest that <c>winget download</c> exports, and the reader of the download folder. The sample
/// follows WinGet 1.29.380's <c>YamlWriter</c> layout through libyaml (CRLF, indentless sequences, folded long values).
/// </summary>
[TestClass]
public sealed class WingetExportedManifestTests
{
    private const string Sha = "3f1b8a2c9d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8";
    private static readonly WingetManifestExpectation Expected = new("7zip.7zip", "25.01", "x64", "msi");

    private static readonly string[] Root =
    [
        "PackageIdentifier: 7zip.7zip",
        "PackageVersion: 25.01",
        "PackageLocale: en-US",
        "Publisher: Igor Pavlov",
        "PublisherUrl: https://www.7-zip.org/",
        "Author: Igor Pavlov",
        "PackageName: 7-Zip",
        "License: LGPL-2.1-or-later",
        "Copyright: Copyright (C) 1999-2025 Igor Pavlov.",
        "ShortDescription: Free and open source file archiver with a high compression ratio.",
        "Description: '7-Zip is a file archiver with a high compression ratio. Most of the source code is under the",
        "  GNU LGPL license.'",
        "Moniker: 7zip",
        "Tags:",
        "- archiver",
        "- compression",
        "ReleaseNotes: \"Bug fixes. \\u00E9\"",
        "Localization:",
        "- PackageLocale: de-DE",
        "  ShortDescription: Kostenloser Dateiarchivierer.",
        "  Tags:",
        "  - archiv",
        "Installers:",
    ];

    private static readonly string[] Installer =
    [
        "- Architecture: x64",
        "  InstallerType: msi",
        $"  InstallerSha256: {Sha}",
        "  InstallerUrl: https://www.7-zip.org/a/7z2501-x64.msi",
        "  Scope: machine",
        "  ProductCode: '{23170F69-40C1-2702-2501-000001000000}'",
        "  AppsAndFeaturesEntries:",
        "  - DisplayName: 7-Zip 25.01 (x64 edition)",
        "    ProductCode: '{23170F69-40C1-2702-2501-000001000000}'",
        "    UpgradeCode: '{23170F69-40C1-2702-0000-000004000000}'",
        "  InstallerSwitches:",
        "    Silent: /quiet",
        "  InstallModes:",
        "  - interactive",
        "  - silent",
    ];

    private static readonly string[] VersionOnTheNextLine = ["PackageVersion:", "  25.01"];

    private static readonly string[] Tail = ["ManifestVersion: 1.12.0", "ManifestType: merged"];

    private static byte[] Bytes(IEnumerable<string> lines, string end = "\r\n") =>
        Encoding.UTF8.GetBytes(string.Concat(lines.Select(line => line + end)));

    private static string[] Lines(string[]? root = null, string[]? installer = null, string[]? tail = null) =>
        [.. root ?? Root, .. installer ?? Installer, .. tail ?? Tail];

    private static string[] WithInstallerLine(string match, string? replacement) =>
        Lines(installer: [.. Installer.SelectMany(line => line.StartsWith(match, StringComparison.Ordinal)
            ? replacement is null ? [] : replacement.Split('\n')
            : new[] { line })]);

    private static WingetManifestRefusal Read(byte[] bytes, WingetManifestExpectation? expected = null) =>
        WingetExportedManifest.Read(bytes, expected ?? Expected, out _);

    [TestMethod]
    public void TheEmitterLayoutIsReadAndItsDigestIsTheSha256OfTheExactBytes()
    {
        foreach (string end in new[] { "\r\n", "\n" })
        {
            byte[] bytes = Bytes(Lines(), end);
            Assert.AreEqual(WingetManifestRefusal.None, WingetExportedManifest.Read(bytes, Expected, out WingetManifestFacts? facts));
            Assert.IsNotNull(facts);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), facts.ManifestDigest);
            Assert.AreEqual(Sha.ToUpperInvariant(), facts.InstallerSha256);
        }
    }

    [TestMethod]
    public void DocumentMarkersQuotedValuesAndTheIdentifiersCaseAreAccepted()
    {
        string[] quoted = WithInstallerLine("  Scope:", "  Scope: 'machine'");
        Assert.AreEqual(WingetManifestRefusal.None, Read(Bytes(["---", .. quoted, "..."])));
        string[] id = Lines(root: [.. Root.Select(line => line == "PackageIdentifier: 7zip.7zip" ? "PackageIdentifier: \"7Zip.7Zip\"" : line)]);
        Assert.AreEqual(WingetManifestRefusal.None, Read(Bytes(id)));
    }

    [TestMethod]
    public void TheFileItselfMustBeStrictUtf8WithConsistentLineEnds()
    {
        byte[] good = Bytes(Lines());
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read([0xEF, 0xBB, 0xBF, .. good]), "byte-order mark");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read([.. good, 0xC3]), "invalid UTF-8");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(good[..^2]), "no final line end");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read([]), "empty");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(good).Replace(
            "Scope: machine\r\n", "Scope: machine\n", StringComparison.Ordinal))), "mixed line ends");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(WithInstallerLine("  Scope:", "  Scope:\tmachine"))), "tab");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(WithInstallerLine("  Scope:", "  Scope: machine\u0007"))), "control");
        byte[] oversized = Bytes(Lines(tail: [.. Tail, "Padding: " + new string('a', WingetExportedManifest.MaximumBytes)]));
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(oversized), "oversized");
    }

    [TestMethod]
    [DataRow("  Dependencies:\n    PackageDependencies:\n    - PackageIdentifier: Microsoft.VCRedist.2015+.x64")]
    [DataRow("  Dependencies:\n    WindowsFeatures:\n    - netfx3")]
    [DataRow("  Dependencies:\n    ExternalDependencies:\n    - Java")]
    [DataRow("  Dependencies: {}")]
    public void DependenciesAnywhereInTheInstallerRefuse(string block)
    {
        Assert.AreEqual(WingetManifestRefusal.DependenciesDeclared, Read(Bytes(WithInstallerLine("  InstallModes:", block + "\n  InstallModes:"))));
    }

    [TestMethod]
    public void AgreementsAtTheRootOrInALocalizationRefuse()
    {
        string[] atRoot = Lines(root: [.. Root[..^1], "Agreements:", "- AgreementLabel: End User License Agreement (EULA)",
            "  AgreementUrl: https://example.invalid/eula", "Installers:"]);
        Assert.AreEqual(WingetManifestRefusal.AgreementsDeclared, Read(Bytes(atRoot)));
        string[] inLocale = Lines(root: [.. Root[..^1], "  Agreements:", "  - Agreement: Terms", "Installers:"]);
        Assert.AreEqual(WingetManifestRefusal.AgreementsDeclared, Read(Bytes(inLocale)));
    }

    [TestMethod]
    public void TheScopeMustBeMachineAndPresent()
    {
        Assert.AreEqual(WingetManifestRefusal.ScopeNotMachine, Read(Bytes(WithInstallerLine("  Scope:", null))), "absent");
        Assert.AreEqual(WingetManifestRefusal.ScopeNotMachine, Read(Bytes(WithInstallerLine("  Scope:", "  Scope: user"))), "user");
        Assert.AreEqual(WingetManifestRefusal.ScopeNotMachine, Read(Bytes(WithInstallerLine("  Scope:", "  Scope: Machine"))), "case");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(WithInstallerLine("  Scope:", "  Scope:"))), "no value");
    }

    [TestMethod]
    public void NestedInstallersRefuse()
    {
        Assert.AreEqual(WingetManifestRefusal.NestedInstaller,
            Read(Bytes(WithInstallerLine("  InstallerType:", "  InstallerType: msi\n  NestedInstallerType: exe"))));
        Assert.AreEqual(WingetManifestRefusal.NestedInstaller,
            Read(Bytes(WithInstallerLine("  InstallModes:", "  NestedInstallerFiles:\n  - RelativeFilePath: setup.exe\n  InstallModes:"))));
    }

    [TestMethod]
    public void TheInstallerAndIdentityMustBeTheReviewedOnes()
    {
        byte[] bytes = Bytes(Lines());
        Assert.AreEqual(WingetManifestRefusal.InstallerMismatch, Read(bytes, Expected with { Architecture = "x86" }));
        Assert.AreEqual(WingetManifestRefusal.InstallerMismatch, Read(bytes, Expected with { InstallerType = "wix" }));
        Assert.AreEqual(WingetManifestRefusal.InstallerMismatch, Read(bytes, Expected with { Architecture = "X64" }));
        Assert.AreEqual(WingetManifestRefusal.IdentityMismatch, Read(bytes, Expected with { PackageId = "7zip.7zip.alpha" }));
        Assert.AreEqual(WingetManifestRefusal.IdentityMismatch, Read(bytes, Expected with { PackageVersion = "25.1" }));
    }

    [TestMethod]
    public void WithoutAReviewedTypeAnyRouteTypeIsAcceptedAndReported()
    {
        WingetManifestExpectation review = Expected with { InstallerType = null };
        Assert.AreEqual(WingetManifestRefusal.None, WingetExportedManifest.Read(Bytes(Lines()), review, out WingetManifestFacts? facts));
        Assert.AreEqual(WinGetMachineInstallerType.Msi, facts!.InstallerType);
        Assert.AreEqual(WingetManifestRefusal.None,
            WingetExportedManifest.Read(Bytes(WithInstallerLine("  InstallerType:", "  InstallerType: nullsoft")), review, out facts));
        Assert.AreEqual(WinGetMachineInstallerType.Nullsoft, facts!.InstallerType);
        foreach (string type in new[] { "portable", "zip", "msix", "msstore", "appx", "MSI" })
            Assert.AreEqual(WingetManifestRefusal.InstallerMismatch,
                Read(Bytes(WithInstallerLine("  InstallerType:", $"  InstallerType: {type}")), review), type);
    }

    [TestMethod]
    public void TheStructureMustBeExactlyOneMergedInstaller()
    {
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(installer: [.. Installer, .. Installer]))), "two installers");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(installer: []))), "no installer");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(tail: ["ManifestVersion: 1.12.0", "ManifestType: singleton"]))),
            "not merged");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(tail: ["ManifestType: merged"]))), "no manifest version");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(tail: [.. Tail, "PackageVersion: 25.01"]))), "duplicate root key");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(tail: [.. Tail, "Scope: user"]))), "root installer default");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(WithInstallerLine("  Scope:", "  Scope: machine\n  Scope: machine"))),
            "duplicate field");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(["  PackageIdentifier: 7zip.7zip", .. Lines()])), "indented first line");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(root: [.. Root[..^1], "Installers: []"]))), "inline installers");
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(Lines(installer: ["  - Architecture: x64", .. Installer[1..]]))),
            "indented item");
    }

    [TestMethod]
    public void RequiredValuesMustBeSingleLineAndWellFormed()
    {
        Assert.AreEqual(WingetManifestRefusal.Malformed,
            Read(Bytes(WithInstallerLine("  InstallerSha256:", $"  InstallerSha256: {Sha}\n    00"))), "continued hash");
        Assert.AreEqual(WingetManifestRefusal.Malformed,
            Read(Bytes(WithInstallerLine("  InstallerSha256:", $"  InstallerSha256: {Sha[..63]}"))), "short hash");
        Assert.AreEqual(WingetManifestRefusal.Malformed,
            Read(Bytes(WithInstallerLine("  InstallerSha256:", $"  InstallerSha256: {Sha[..63]}g"))), "not hex");
        string[] noArchitecture = Lines(installer: ["- InstallerType: msi", .. Installer[2..]]);
        Assert.AreEqual(WingetManifestRefusal.Malformed, Read(Bytes(noArchitecture)), "no architecture");
        Assert.AreEqual(WingetManifestRefusal.Malformed,
            Read(Bytes(Lines(root: [.. Root.Select(line => line == "PackageVersion: 25.01" ? "PackageVersion: \"25.01\\u0030\"" : line)]))),
            "escaped version");
        Assert.AreEqual(WingetManifestRefusal.Malformed,
            Read(Bytes(Lines(root: [.. Root.SelectMany(line => line == "PackageVersion: 25.01" ? VersionOnTheNextLine : [line])]))),
            "version on the next line");
    }

    [TestMethod]
    public async Task TheDownloadFolderMustHoldExactlyTheManifestAndAMatchingInstaller()
    {
        byte[] installer = "fixture installer bytes"u8.ToArray();
        string sha = Convert.ToHexString(SHA256.HashData(installer)).ToLowerInvariant();
        byte[] manifest = Bytes(WithInstallerLine("  InstallerSha256:", $"  InstallerSha256: {sha}"));
        string root = Path.Combine(Path.GetTempPath(), "pcs-d4b-" + Guid.NewGuid().ToString("N"));
        try
        {
            string Fresh(string name)
            {
                string folder = Path.Combine(root, name);
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "7-Zip_25.01_Machine_X64_msi_en-US.yaml"), manifest);
                File.WriteAllBytes(Path.Combine(folder, "7-Zip_25.01_Machine_X64_msi_en-US.msi"), installer);
                return folder;
            }

            (WingetManifestRefusal refusal, WingetManifestFacts? facts) = await WingetDownloadFolder.ReadAsync(Fresh("good"), Expected,
                CancellationToken.None);
            Assert.AreEqual(WingetManifestRefusal.None, refusal);
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(manifest)), facts!.ManifestDigest);
            Assert.AreEqual(sha.ToUpperInvariant(), facts.InstallerSha256);
            Assert.AreEqual(WinGetMachineInstallerType.Msi, facts.InstallerType);

            string dependencies = Fresh("dependencies");
            File.WriteAllBytes(Path.Combine(dependencies, "7-Zip_25.01_Machine_X64_msi_en-US.yaml"),
                Bytes(WithInstallerLine("  InstallModes:", "  Dependencies:\n    PackageDependencies:\n    - PackageIdentifier: Example.Runtime\n  InstallModes:")));
            Directory.CreateDirectory(Path.Combine(dependencies, "Dependencies"));
            Assert.AreEqual(WingetManifestRefusal.DependenciesDeclared,
                (await WingetDownloadFolder.ReadAsync(dependencies, Expected, default)).Refusal);

            string extra = Fresh("extra");
            File.WriteAllBytes(Path.Combine(extra, "other.bin"), [1]);
            Assert.AreEqual(WingetManifestRefusal.FolderUnexpected, (await WingetDownloadFolder.ReadAsync(extra, Expected, default)).Refusal);

            string directory = Fresh("directory");
            Directory.CreateDirectory(Path.Combine(directory, "sub"));
            Assert.AreEqual(WingetManifestRefusal.FolderUnexpected, (await WingetDownloadFolder.ReadAsync(directory, Expected, default)).Refusal);

            string twoYaml = Fresh("two-yaml");
            File.Move(Path.Combine(twoYaml, "7-Zip_25.01_Machine_X64_msi_en-US.msi"), Path.Combine(twoYaml, "copy.YAML"));
            Assert.AreEqual(WingetManifestRefusal.FolderUnexpected, (await WingetDownloadFolder.ReadAsync(twoYaml, Expected, default)).Refusal);

            string missing = Fresh("missing");
            File.Delete(Path.Combine(missing, "7-Zip_25.01_Machine_X64_msi_en-US.msi"));
            Assert.AreEqual(WingetManifestRefusal.FolderUnexpected, (await WingetDownloadFolder.ReadAsync(missing, Expected, default)).Refusal);

            string swapped = Fresh("swapped");
            File.WriteAllBytes(Path.Combine(swapped, "7-Zip_25.01_Machine_X64_msi_en-US.msi"), "another installer"u8.ToArray());
            Assert.AreEqual(WingetManifestRefusal.InstallerHashMismatch,
                (await WingetDownloadFolder.ReadAsync(swapped, Expected, default)).Refusal);

            string linked = Fresh("linked");
            string target = Path.Combine(root, "elsewhere.msi");
            File.WriteAllBytes(target, installer);
            File.Delete(Path.Combine(linked, "7-Zip_25.01_Machine_X64_msi_en-US.msi"));
            bool created = TryLink(Path.Combine(linked, "7-Zip_25.01_Machine_X64_msi_en-US.msi"), target);
            if (created)
                Assert.AreEqual(WingetManifestRefusal.FolderUnexpected, (await WingetDownloadFolder.ReadAsync(linked, Expected, default)).Refusal);

            string refused = Fresh("refused");
            File.WriteAllBytes(Path.Combine(refused, "7-Zip_25.01_Machine_X64_msi_en-US.yaml"), Bytes(WithInstallerLine("  Scope:", null)));
            Assert.AreEqual(WingetManifestRefusal.ScopeNotMachine, (await WingetDownloadFolder.ReadAsync(refused, Expected, default)).Refusal);

            Assert.AreEqual(WingetManifestRefusal.FolderUnexpected,
                (await WingetDownloadFolder.ReadAsync(Path.Combine(root, "absent"), Expected, default)).Refusal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A file symbolic link where the platform lets this account create one (Windows needs developer mode or elevation).</summary>
    private static bool TryLink(string path, string target)
    {
        try
        {
            File.CreateSymbolicLink(path, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
