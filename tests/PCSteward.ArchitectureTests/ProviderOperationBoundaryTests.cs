using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// Task 48 (spike §2.2 and §3.1, ADR-003/024): provider operations are built only from fixed, reviewed flags.
/// No product source may contain an installer-override, hash-bypass, forced, relocating, dependency-skipping
/// or source-agreement flag, nor set the matching WinGet COM <c>InstallOptions</c> members.
/// </summary>
[TestClass]
public sealed partial class ProviderOperationBoundaryTests
{
    private static readonly string[] ForbiddenCliFlags =
    [
        // WinGet (spike §2.2)
        "--override", "--custom", "--ignore-security-hash", "--force", "--location", "--skip-dependencies",
        "--accept-source-agreements", "--allow-reboot", "--ignore-local-archive-malware-scan", "--interactive",
        // Chocolatey (spike §3.1)
        "--install-arguments", "--override-arguments", "--params", "--package-parameters", "--skip-powershell",
        "--skip-automation-scripts", "--ignore-dependencies", "--ignore-checksums", "--allow-empty-checksums",
        "--allow-empty-checksums-secure", "--force-dependencies", "--allow-downgrade", "--side-by-side"
    ];

    private static readonly string[] ForbiddenComMembers =
    [
        "ReplacementInstallerArguments", "AdditionalInstallerArguments", "AllowHashMismatch", "AdditionalPackageCatalogArguments",
        "AuthenticationArguments", "BypassIsStoreClientBlockedPolicyCheck", "PreferredInstallLocation", "SkipDependencies",
        "AllowUpgradeToUnknownVersion", "AcceptSourceAgreements", "LogOutputPath"
    ];

    [TestMethod]
    public void ProductSourcesContainNoForbiddenProviderFlags()
    {
        foreach ((string file, string source) in ProductSources())
        {
            foreach (string flag in ForbiddenCliFlags)
            {
                Assert.IsFalse(source.Contains('"' + flag + '"', StringComparison.OrdinalIgnoreCase), $"{file} contains the forbidden provider flag {flag}.");
            }
        }
    }

    [TestMethod]
    public void ProductSourcesSetNoForbiddenWinGetInstallOptions()
    {
        foreach ((string file, string source) in ProductSources())
        {
            foreach (string member in ForbiddenComMembers)
            {
                Assert.IsFalse(Regex.IsMatch(source, $@"\b{member}\b"), $"{file} uses the forbidden WinGet COM member {member}.");
            }

            Assert.IsFalse(ForceAssignment().IsMatch(source), $"{file} sets InstallOptions.Force.");
        }
    }

    [TestMethod]
    public void InstallBuildersAddAgreementAcceptanceOnlyBehindTheRecordedHash()
    {
        string source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "PCSteward.Provider.WinGet", "WingetInstallOperations.cs"));
        Assert.AreEqual(1, Regex.Count(source, "\"--accept-package-agreements\""), "One guarded use only.");
        StringAssert.Contains(source, "if (selection.AgreementHash is not null) arguments.Add(\"--accept-package-agreements\");");
        StringAssert.Contains(source, "if (!job.Matches(freshEvidence))");
        foreach ((string file, string text) in ProductSources().Where(item => !item.File.EndsWith("WingetInstallOperations.cs", StringComparison.Ordinal)))
        {
            Assert.IsFalse(text.Contains("--accept-package-agreements", StringComparison.Ordinal), $"{file} accepts package agreements outside the install builder.");
            Assert.IsFalse(Regex.IsMatch(text, @"\bAcceptPackageAgreements\b"), $"{file} sets AcceptPackageAgreements.");
        }
    }

    [TestMethod]
    public void ForbiddenFlagScanWouldCatchAViolation()
    {
        // Guards the scan itself against silently matching nothing.
        Assert.IsTrue(ForbiddenCliFlags.Any(flag => "[\"install\", \"--force\"]".Contains('"' + flag + '"', StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(ForceAssignment().IsMatch("new InstallOptions { Force = true }"));
        Assert.IsTrue(ProductSources().Any(item => item.File.EndsWith("ChocolateyInstallOperations.cs", StringComparison.Ordinal)));
    }

    private static IEnumerable<(string File, string Source)> ProductSources()
    {
        string src = Path.Combine(FindRepositoryRoot(), "src");
        return Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                           !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => (Path.GetRelativePath(src, file), File.ReadAllText(file)));
    }

    [GeneratedRegex(@"\bForce\s*=", RegexOptions.CultureInvariant)]
    private static partial Regex ForceAssignment();

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("The repository root was not found.");
    }
}
