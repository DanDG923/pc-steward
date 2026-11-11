using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-034 security review H1: release signing runs unelevated, so the one elevated step of a release install trusts only
/// the anchor in the owner's paste and installs an MSI only when it is the CI build of the named run. The caller composes
/// one approved command; the bootstrap copies exactly the pinned scripts into a protected execution root; the install
/// script checks the artefact digest before unpacking, the MSI's pin and tables before msiexec, and the installed tree after,
/// uninstalling on any difference. The Windows CI MSI job runs the whole step, including three refusals.
/// </summary>
[TestClass]
public sealed class ReleaseInstallBoundaryTests
{
    [TestMethod]
    public void TheInstallStepRunsOnlyFromTheProtectedBundleAndChecksBeforeItInstalls()
    {
        string install = Script("Install-PCStewardRelease.ps1");
        // Elevated, from the protected bundle, before any other script is loaded.
        int environment = install.IndexOf("Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $PSScriptRoot", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, environment);
        foreach (string loaded in new[] { "'OwnerOnlyPayloadIntegrity.ps1'", "'PCStewardReleaseSigning.ps1'", "'PCStewardMsiTables.ps1'" })
            Assert.IsLessThan(install.IndexOf(". (Join-Path $PSScriptRoot " + loaded + ")", StringComparison.Ordinal), environment, loaded);

        // The artefact digest from the paste is checked before the zip is opened; the record must name the paste's run and pins.
        StringAssert.Contains(install, "$zip = Copy-IntoProtected $ArtefactZip 'artefact.zip' 1GB $ArtefactSha256");
        int digest = install.IndexOf("if ($ExpectedSha256 -and $hash -cne $ExpectedSha256) { throw", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, digest);
        Assert.IsLessThan(install.IndexOf("$output = [IO.FileStream]::new($target", StringComparison.Ordinal), digest, "nothing is copied before its digest matches");
        Assert.IsLessThan(install.IndexOf("[IO.Compression.ZipFile]::OpenRead($zip.Path)", StringComparison.Ordinal), install.IndexOf("$zip = Copy-IntoProtected", StringComparison.Ordinal));
        StringAssert.Contains(install, "[string]$record.runId -cne [string]$RunId -or $record.sourceCommit -cne $SourceCommit");
        StringAssert.Contains(install, "if ([string]$record.pins.primary -cne $PrimaryPin -or [string]$record.pins.backup -cne $BackupPin)");
        StringAssert.Contains(install, "if (!$target.StartsWith($artefact + '\\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $target))");

        // The MSI's pin and tables are checked on the protected copy before the only install call, which uses that copy.
        Assert.AreEqual(1, Regex.Count(install, @"Invoke-Msiexec @\('/i'"));
        int msiexec = install.IndexOf("Invoke-Msiexec @('/i', ('\"' + $msi.Path + '\"'))", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, msiexec);
        foreach (string check in new[] { "Test-PCStewardPinnedSignature -Path $msi.Path -Pins $pins", "if ($tables.Sha256 -cne [string]$reference.tablesSha256)", "[PCSteward.Deployment.PayloadIntegrity]::AssertTree($candidateRoot, $candidate.Files)" })
            Before(install, check, msiexec);

        // After install: each executable equals the CI one apart from a pinned signature, and the whole tree is exact.
        StringAssert.Contains(install, "[PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($path, $identity)");
        StringAssert.Contains(install, "[PCSteward.Deployment.PayloadIntegrity]::AssertTree($installRoot, $expected.ToArray())");
        // Fail closed: a verified-install failure uninstalls.
        StringAssert.Contains(install, "if ($installed) {");
        StringAssert.Contains(install, "Invoke-Msiexec @('/x', $product.ProductCode) 'msiexec-uninstall.log'");

        Assert.IsFalse(Regex.IsMatch(install, @"Set-AuthenticodeSignature|Cert:\\|X509Store|Invoke-(RestMethod|WebRequest)|Net\.WebClient|HttpClient", RegexOptions.IgnoreCase), "the install step signs, trusts and downloads nothing");
    }

    [TestMethod]
    public void TheBootstrapCopiesOnlyThePinnedScriptsIntoANewProtectedRoot()
    {
        string bootstrap = Script("Invoke-PCStewardReleaseInstallBootstrap.ps1");
        StringAssert.Contains(bootstrap, "$allowed=@('Install-PCStewardRelease.ps1','OwnerOnlyExecution.ps1','OwnerOnlyPayloadIntegrity.ps1','PCStewardMsiTables.ps1','PCStewardReleaseSigning.ps1','Remove-OwnerOnlyProtectedTestDeployment.ps1')");
        StringAssert.Contains(bootstrap, "-or (Test-Path -LiteralPath $execution)){throw 'Unapproved or existing execution root.'}");
        StringAssert.Contains(bootstrap, "[Security.Principal.WindowsIdentity]::GetCurrent().User.Value -cne $record.installerSid");
        int hash = bootstrap.IndexOf("if($hash -cne $entry.sha256){throw", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, hash);
        Assert.IsLessThan(bootstrap.IndexOf("$output.Write($bytes,0,$bytes.Length)", StringComparison.Ordinal), hash, "a script is written only after its pin matched");
        int environment = bootstrap.IndexOf("Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $scripts", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, environment);
        Assert.IsLessThan(bootstrap.IndexOf("& (Join-Path $scripts 'Install-PCStewardRelease.ps1') @arguments", StringComparison.Ordinal), environment);
        StringAssert.Contains(bootstrap, "$env:TEMP=$compiler;$env:TMP=$compiler");
    }

    [TestMethod]
    public void TheCallerElevatesOnceWithTheComposedCommandFromANormalWindow()
    {
        string caller = Script("Invoke-PCStewardReleaseInstall.ps1");
        Assert.AreEqual(1, Regex.Count(caller, "-Verb RunAs"));
        StringAssert.Contains(caller, "if ((Get-Sha256Hex $bytes) -cne $pin) { throw");
        StringAssert.Contains(caller, "if ($encoded.Length -gt 30000)");
        StringAssert.Contains(caller, "if($h -cne '@@SHA256@@'){exit 20}");
        int start = caller.IndexOf("Start-Process -FilePath $powershell", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, start);
        foreach (string check in new[] { "throw 'Run this from a normal (not elevated) Windows PowerShell window; it asks for elevation itself.'", "'^(COMPLUS_|COR_ENABLE_PROFILING$|COR_PROFILER|APPDOMAIN_MANAGER_)'", "if ($ComposeOnly) {" })
            Before(caller, check, start);
        StringAssert.Contains(caller, "Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $resultPath)");
    }

    [TestMethod]
    public void TheCiBuildPublishesTheMsiReferenceAndTheSignerAndCiCheckIt()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-payload.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(workflow, "run: pwsh -NoProfile -File ./scripts/New-PCStewardMsiReference.ps1 -ReleasePayloadRoot artifacts/release-payload");
        Assert.IsLessThan(workflow.IndexOf("New-PCStewardMsiReference.ps1", StringComparison.Ordinal), workflow.IndexOf("-ReleasePayload\n", StringComparison.Ordinal));

        string tables = Script("PCStewardMsiTables.ps1");
        StringAssert.Contains(tables, "$script:PCStewardMsiSignedLeaves = @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'PCSteward.ReleaseManifest.json')");
        StringAssert.Contains(tables, "if (row[property] == \"ProductCode\") { row[value] = \"*\"; }");

        string release = Script("New-PCStewardRelease.ps1");
        StringAssert.Contains(release, "if ($tables.Sha256 -cne [string]$reference.tablesSha256)");

        string ci = Script("Test-PCStewardMsiCI.ps1");
        foreach (string check in new[] { "signed-msi-tables-equal-the-unsigned-ci-reference", "release-install-refuses-a-wrong-artefact-digest", "release-install-refuses-msi-tables-unlike-ci", "release-install-uninstalls-files-unlike-ci", "release-install-installs-and-verifies-the-ci-build" })
            StringAssert.Contains(ci, "Set-Check '" + check + "'");
    }

    private static void Before(string text, string check, int later)
    {
        int index = text.IndexOf(check, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, index, check);
        Assert.IsLessThan(later, index, check);
    }

    private static string Script(string name) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), "scripts", name)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "PCSteward.slnx"))) return current.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }
}
