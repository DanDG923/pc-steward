using System.Text.RegularExpressions;

namespace PCSteward.ArchitectureTests;

/// <summary>
/// ADR-034 §2 and §5: the release key script, the release-payload workflow and the release signing script. No release
/// script touches a Windows trust store. The primary key is made on the YubiKey (slot 9c, PIN and touch for every
/// signature) and never replaces an existing slot; the backup key goes to one or two removable drives (a second copy can be added later) and is deleted from the PC.
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
        // An unfinished attempt is discarded only on request, and only when the slot holds this script's one-day placeholder
        // for the slot's own key under the release subject; a real release certificate is never touched.
        StringAssert.Contains(keys, "if ($DiscardUnfinished -and $keyPresent) {");
        StringAssert.Contains(keys, "$isPlaceholder = $unfinished.Subject -ceq $primarySubject -and ($unfinished.NotAfter - $unfinished.NotBefore).TotalDays -le 2 -and (Get-PCStewardKeyPin -CertificateDer $unfinished.RawData) -ceq $slotPin");
        Assert.IsLessThan(keys.IndexOf("'piv', 'certificates', 'delete', '9c'", StringComparison.Ordinal), keys.IndexOf("if ($isPlaceholder) {", StringComparison.Ordinal), "nothing is deleted before the placeholder check");
        Assert.AreEqual(1, Regex.Count(keys, @"'piv', 'keys', 'delete'"));
        // The release certificate is made with the slot key's signature only; Windows refuses to attach a smart-card key to it.
        Assert.IsFalse(keys.Contains("CreateSelfSigned", StringComparison.Ordinal));
        StringAssert.Contains(keys, "$signed = $request.Create([Security.Cryptography.X509Certificates.X500DistinguishedName]::new($primarySubject), $generator, $now.AddMinutes(-5), $now.AddYears(10), $serial)");
        // The certificate is code signing only and not a CA.
        StringAssert.Contains(keys, "X509BasicConstraintsExtension]::new($false, $false, 0, $true)");
        StringAssert.Contains(keys, "$codeSigning = '1.3.6.1.5.5.7.3.3'");

        // Exactly one -DeleteKey: the backup key leaving the personal store. Card certificates are removed without it.
        Assert.AreEqual(1, Regex.Count(keys, "-DeleteKey"));
        StringAssert.Contains(keys, "Remove-Item -LiteralPath ('Cert:\\CurrentUser\\My\\' + $created.Thumbprint) -DeleteKey");
        // The PFX goes only to removable drives, never overwrites, and needs a 12-character password.
        Assert.AreEqual(1, Regex.Count(keys, "Export-PfxCertificate"));
        StringAssert.Contains(keys, "-FilePath $target -Password $password -CryptoAlgorithmOption AES256_SHA256");
        StringAssert.Contains(keys, "$info.DriveType -ne [IO.DriveType]::Removable");
        StringAssert.Contains(keys, "This script never overwrites a backup key.");
        StringAssert.Contains(keys, "if ($length -lt 12)");
        // One or two drives now (owner, 2026-11-11); BackupCopy adds the second copy later. The copy never overwrites, is
        // written only after the source opens and carries the backup pin, and is compared byte for byte and checked again.
        StringAssert.Contains(keys, "if ($drives.Count -lt 1 -or $drives.Count -gt 2 -or $drives.Count -ne @($BackupDrives).Count)");
        StringAssert.Contains(keys, "if ($from -ieq $to) { throw");
        StringAssert.Contains(keys, "[IO.FileMode]::CreateNew");
        Assert.AreEqual(1, Regex.Count(keys, @"\[IO\.FileStream\]::new\("), "the copy is the only file the script writes itself");
        int sourceCheck = keys.IndexOf("Test-PCStewardBackupPfx $bytes $password $expected $source", StringComparison.Ordinal);
        Assert.IsGreaterThan(0, sourceCheck);
        Assert.IsLessThan(keys.IndexOf("[IO.FileMode]::CreateNew", StringComparison.Ordinal), sourceCheck, "the source is checked before the copy is written");
        StringAssert.Contains(keys, "if (!$same) { throw \"The copy on $target differs from $source.\" }");
        StringAssert.Contains(keys, "Test-PCStewardBackupPfx $written $password $expected $target");
        StringAssert.Contains(keys, "if ($expected -and $expected -cne $BackupPin) { throw");
        StringAssert.Contains(keys, "[Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)");
        // The two passwords are compared in unmanaged memory that is zeroed; neither becomes a managed string.
        Assert.IsFalse(keys.Contains("PtrToStringBSTR", StringComparison.Ordinal));
        Assert.AreEqual(2, Regex.Count(keys, "ZeroFreeBSTR"));
        // ykman never comes from PATH: Program Files or the owner's path, and a valid Yubico signature either way.
        Assert.IsFalse(Regex.IsMatch(keys, @"Get-Command|\$env:PATH|where(\.exe)?\s+ykman", RegexOptions.IgnoreCase), "ykman is not resolved from PATH");
        StringAssert.Contains(keys, "$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)");
        StringAssert.Contains(keys, "if ($ykmanSignature.Status -ne 'Valid' -or $null -eq $ykmanSignature.SignerCertificate -or $ykmanSignature.SignerCertificate.Subject -notmatch '(^|, )O=Yubico AB(,|$)')");
        Assert.IsLessThan(keys.IndexOf("function Invoke-YkmanRaw", StringComparison.Ordinal), keys.IndexOf("$ykmanSignature.Status", StringComparison.Ordinal), "the signature is checked before ykman runs");
        // Yubico's attestation of the new key is printed with its pin, so the pin can be checked away from the PC.
        StringAssert.Contains(keys, "@('piv', 'keys', 'attest', '--format', 'PEM', '9c', $attestationFile)");
        StringAssert.Contains(keys, "if ((Get-PCStewardKeyPin -CertificateDer $attestation.RawData) -cne $pin) { throw 'The attestation is not for the new key.' }");
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
        // Self-updater security review R3: a run on a tag (whose name GitHub reports as head_branch) or another branch fails first.
        StringAssert.Contains(workflow, "if ($env:REF_TYPE -cne 'branch' -or $env:REF -cnotin @('refs/heads/phase/4-beta', 'refs/heads/main')) { throw");
        Assert.IsLessThan(workflow.IndexOf("uses: actions/checkout@", StringComparison.Ordinal), workflow.IndexOf("Refuse anything but a release branch", StringComparison.Ordinal));
        StringAssert.Contains(workflow, "run: pwsh -NoProfile -File ./scripts/Test-OwnerOnlyCandidateBuilderCI.ps1 -ReleasePayload");
        StringAssert.Contains(workflow, "name: pc-steward-release-payload");
        // Every action is pinned to a full commit, so a moved tag cannot change what builds the payload.
        MatchCollection uses = Regex.Matches(workflow, @"^\s*-?\s*uses:\s*(\S+)", RegexOptions.Multiline);
        Assert.IsNotEmpty(uses);
        foreach (Match use in uses)
            Assert.IsTrue(Regex.IsMatch(use.Groups[1].Value, @"\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}\z"), use.Groups[1].Value);
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
        // The record's pins must be the ones committed at the run commit, read from the clean clone.
        StringAssert.Contains(release, "Join-Path $repositoryRoot 'src\\PCSteward.Execution\\ReleaseSigningPins.cs'");
        StringAssert.Contains(release, "if ($committedPins[0] -cne $pins[0] -or $committedPins[1] -cne $pins[1])");
        // Every check comes before the first signature.
        int signing = release.IndexOf("Invoke-PCStewardReleaseSigning -PayloadRoot", StringComparison.Ordinal);
        foreach (string check in new[] { "$zipHash -cne $expectedZip", "$localCommit -cne", "--untracked-files=all --ignored", "-ValidateCandidateRoot", "$executionText.Contains($pin)", "$committedPins[0] -cne $pins[0]" })
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
