using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-034 §2 and §5: the release key script, the release-payload workflow and the release signing script. No release
/// script touches a Windows trust store. The primary key is made on the YubiKey (slot 9c, PIN and touch for every
/// signature) and never replaces an existing slot; the backup key goes to two removable drives and is deleted from the PC.
/// The payload is built on demand from a commit with both pins, and the signing script checks the run, the artefact digest,
/// its own commit and the compiled pins before it signs.
/// </summary>
[TestClass]
public sealed class ReleaseKeyScriptBoundaryTests
{
    [TestMethod]
    public void NoReleaseScriptTouchesATrustStore()
    {
        foreach (string name in new[] { "New-PCStewardReleaseKeys.ps1", "New-PCStewardRelease.ps1", "PCStewardReleaseSigning.ps1", "New-PCStewardMsi.ps1", "New-PCStewardReleaseManifest.ps1" })
        {
            string source = Script(name);
            Assert.IsFalse(Regex.IsMatch(source, @"\\(Root|TrustedPublisher|AuthRoot|CA|TrustedPeople)\b", RegexOptions.IgnoreCase), $"{name} names a trust store");
            Assert.IsFalse(source.Contains("LocalMachine", StringComparison.OrdinalIgnoreCase), $"{name} names the machine store");
            Assert.IsFalse(source.Contains("Import-PfxCertificate", StringComparison.Ordinal), $"{name} imports a PFX into a store");
        }
    }

    [TestMethod]
    public void ThePrimaryKeyLivesOnTheYubiKeyAndTheBackupLeavesThePc()
    {
        string keys = Script("New-PCStewardReleaseKeys.ps1");
        StringAssert.Contains(keys, "if ($PSVersionTable.PSEdition -ne 'Desktop')");
        StringAssert.Contains(keys, "throw 'Run this script from a normal (not elevated) PowerShell window.'");
        StringAssert.Contains(keys, "@('piv', 'keys', 'generate', '--algorithm', $algorithm, '--pin-policy', 'ALWAYS', '--touch-policy', 'ALWAYS', '9c', $publicPem)");
        StringAssert.Contains(keys, "if ($keyPresent -or ($info -join \"`n\") -match '(?m)^Slot 9C')");
        Assert.IsLessThan(keys.IndexOf("'keys', 'generate'", StringComparison.Ordinal), keys.IndexOf("$keyPresent = ", StringComparison.Ordinal), "the slot is checked before a key is generated");
        StringAssert.Contains(keys, "if ($info -match 'default (PIN|PUK|management key)')");
        // The certificate is code signing only and not a CA.
        StringAssert.Contains(keys, "X509BasicConstraintsExtension]::new($false, $false, 0, $true)");
        StringAssert.Contains(keys, "$codeSigning = '1.3.6.1.5.5.7.3.3'");

        // Exactly one -DeleteKey: the backup key leaving the personal store. Card certificates are removed without it.
        Assert.AreEqual(1, Regex.Count(keys, "-DeleteKey"));
        StringAssert.Contains(keys, "Remove-Item -LiteralPath ('Cert:\\CurrentUser\\My\\' + $created.Thumbprint) -DeleteKey");
        // The PFX goes only to the two removable drives, never overwrites, and needs a 12-character password.
        Assert.AreEqual(1, Regex.Count(keys, "Export-PfxCertificate"));
        StringAssert.Contains(keys, "-FilePath $target -Password $password -CryptoAlgorithmOption AES256_SHA256");
        StringAssert.Contains(keys, "$info.DriveType -ne [IO.DriveType]::Removable");
        StringAssert.Contains(keys, "This script never overwrites a backup key.");
        StringAssert.Contains(keys, "if ($plain.Length -lt 12)");
        Assert.IsFalse(Regex.IsMatch(keys, @"Invoke-(RestMethod|WebRequest)|Net\.WebClient|HttpClient"), "the key script makes no network call");
    }

    [TestMethod]
    public void ThePayloadIsBuiltOnDemandFromACommitWithBothPins()
    {
        string root = FindRepositoryRoot();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "release-payload.yml")).Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(workflow, "on:\n  workflow_dispatch:\n");
        Assert.IsFalse(Regex.IsMatch(workflow, @"^\s*(push|pull_request|schedule|workflow_run):", RegexOptions.Multiline));
        StringAssert.Contains(workflow, "permissions:\n  contents: read\n");
        StringAssert.Contains(workflow, "persist-credentials: false");
        StringAssert.Contains(workflow, "run: pwsh -NoProfile -File ./scripts/Test-OwnerOnlyCandidateBuilderCI.ps1 -ReleasePayload");
        StringAssert.Contains(workflow, "name: pc-steward-release-payload");
        Assert.IsFalse(Regex.IsMatch(workflow, @"secrets\.|Set-AuthenticodeSignature|signtool", RegexOptions.IgnoreCase), "CI never signs a release");

        string launcher = Script("Test-OwnerOnlyCandidateBuilderCI.ps1");
        StringAssert.Contains(launcher, "if ($ReleasePayload) { $null = Get-ReleasePins }");
        StringAssert.Contains(launcher, "'internal const string ' + $name + ' = \"([0-9A-F]{64})\";'");
        // The release payload is a clean build: -AllowDirty never appears in the launcher.
        Assert.IsFalse(launcher.Contains("AllowDirty", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TheSigningScriptChecksTheRunTheArtefactTheCommitAndThePins()
    {
        string release = Script("New-PCStewardRelease.ps1");
        StringAssert.Contains(release, "$repository = 'DanDG923/pc-steward'");
        StringAssert.Contains(release, "$workflowPath = '.github/workflows/release-payload.yml'");
        StringAssert.Contains(release, "$allowedBranches = @('phase/4-beta', 'main')");
        StringAssert.Contains(release, "if ($run.path -cne $workflowPath -or $run.event -cne 'workflow_dispatch')");
        StringAssert.Contains(release, "if ($run.status -cne 'completed' -or $run.conclusion -cne 'success')");
        StringAssert.Contains(release, "if ($zipHash -cne $expectedZip)");
        StringAssert.Contains(release, "if ($LASTEXITCODE -ne 0 -or $localCommit -cne [string]$run.head_sha)");
        StringAssert.Contains(release, "'-ValidateCandidateRoot', $candidate, '-ExpectedManifestSha256', [string]$record.manifestSha256");
        StringAssert.Contains(release, "foreach ($pin in $pins) { if (!$executionText.Contains($pin))");
        StringAssert.Contains(release, "status --porcelain --untracked-files=all --ignored -- scripts packaging");
        // Every check comes before the first signature.
        int signing = release.IndexOf("Invoke-PCStewardReleaseSigning -PayloadRoot", StringComparison.Ordinal);
        foreach (string check in new[] { "$zipHash -cne $expectedZip", "$localCommit -cne", "--untracked-files=all --ignored", "-ValidateCandidateRoot", "$executionText.Contains($pin)" })
            Assert.IsLessThan(signing, release.IndexOf(check, StringComparison.Ordinal), check);
        Assert.IsFalse(Regex.IsMatch(release, @"msiexec|Start-Process", RegexOptions.IgnoreCase), "the signing script installs nothing");

        string signingFunctions = Script("PCStewardReleaseSigning.ps1");
        StringAssert.Contains(signingFunctions, "if ((Get-PCStewardKeyPin -CertificateDer $Certificate.RawData) -cne $Pin) { throw 'The signing certificate does not carry the expected pin.' }");
        StringAssert.Contains(signingFunctions, "[void]$chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))");
        StringAssert.Contains(signingFunctions, "if ($chain.ChainElements.Count -ne 1 -or $problems.Count -ne 0)");
        // Both executables and the MSI go through the one signing step, which checks each signature it makes.
        StringAssert.Contains(signingFunctions, "foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')) { Set-PCStewardSignature (Join-Path $PayloadRoot $leaf) }");
        StringAssert.Contains(signingFunctions, "    Set-PCStewardSignature $MsiPath\n");
        StringAssert.Contains(signingFunctions, "        $null = Test-PCStewardPinnedSignature -Path $Target -Pins @($Pin)\n    }");
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
