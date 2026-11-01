[CmdletBinding()]
param(
    [Parameter(Mandatory)][switch]$Install,
    [string]$CandidateRoot,
    [string]$CandidateManifestPath,
    [string]$CandidateManifestSha256,
    [switch]$CreateOwnerOnlyCertificate,
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$schema = 'pc-steward-owner-only-protected-test-v6'
function Enter-OwnerOnlyMutex() {
    $mutex = [Threading.Mutex]::new($false, 'Global\PCSteward.OwnerOnlyTestDeployment.v1')
    try { if (!$mutex.WaitOne(0)) { $mutex.Dispose(); throw 'Another owner-only protected deployment or recovery is already running.' } }
    catch [Threading.AbandonedMutexException] { }
    catch { $mutex.Dispose(); throw }
    return $mutex
}
function Set-OwnerOnlyAcl([string]$Path) {
    $expected = @(
        [pscustomobject]@{ Sid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18'); Rights = [Security.AccessControl.FileSystemRights]::FullControl },
        [pscustomobject]@{ Sid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'); Rights = [Security.AccessControl.FileSystemRights]::FullControl },
        [pscustomobject]@{ Sid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'); Rights = ([Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize) }
    )
    foreach ($item in @((Get-Item -LiteralPath $Path -Force)) + @(Get-ChildItem -LiteralPath $Path -Force -Recurse)) {
        $security = if ($item.PSIsContainer) { [Security.AccessControl.DirectorySecurity]::new() } else { [Security.AccessControl.FileSecurity]::new() }
        $security.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
        $security.SetAccessRuleProtection($true, $false)
        foreach ($entry in $expected) { [void]$security.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($entry.Sid, $entry.Rights, [Security.AccessControl.AccessControlType]::Allow)) }
        Set-Acl -LiteralPath $item.FullName -AclObject $security
        Assert-ExactDacl $item.FullName
    }
}
function Remove-ExactCertificate([string]$Thumbprint, [string[]]$Stores) {
    $permitted = @('My', 'Root', 'TrustedPublisher')
    foreach ($storeName in $Stores) {
        if ($storeName -notin $permitted) { throw "Certificate store is not allowlisted: $storeName" }
        $store = [Security.Cryptography.X509Certificates.X509Store]::new($storeName, [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try { foreach ($certificate in @($store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Thumbprint, $false))) { if ($certificate.Subject -notlike 'CN=PC Steward Owner-Only Test *') { throw 'Refusing to remove a certificate that is not an owner-only PC Steward test certificate.' }; $store.Remove($certificate) } }
        finally { $store.Close() }
    }
}
function Assert-ExactCertificateAbsent([string]$Thumbprint, [string[]]$Stores) {
    foreach ($storeName in $Stores) {
        $store = [Security.Cryptography.X509Certificates.X509Store]::new($storeName, [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
        try { if ($store.Certificates.Find([Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint, $Thumbprint, $false).Count -ne 0) { throw "Owned certificate remains in CurrentUser\\$storeName." } }
        finally { $store.Close() }
    }
}
function Remove-PendingCertificate([string]$Subject) {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('My', [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    try { $matches = @($store.Certificates | Where-Object { $_.Subject -eq $Subject }); if ($matches.Count -gt 1) { throw 'Refusing pending-certificate recovery because the unique owner-only test subject is ambiguous.' }; foreach ($certificate in $matches) { if ($certificate.Subject -notlike 'CN=PC Steward Owner-Only Test *') { throw 'Refusing to remove a certificate that is not an owner-only PC Steward test certificate.' }; $store.Remove($certificate) } }
    finally { $store.Close() }
}
function Assert-PendingCertificateAbsent([string]$Subject) {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('My', [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try { if (@($store.Certificates | Where-Object { $_.Subject -eq $Subject }).Count -ne 0) { throw 'The pending owner-only test certificate remains in CurrentUser\\My.' } }
    finally { $store.Close() }
}
function Write-PendingLedger([object]$CertificateState, [string]$StatePath, [string]$InstallRoot, [string]$InstallerSid) {
    $thumbprint = if ($CertificateState.Certificate) { $CertificateState.Certificate.Thumbprint } else { $null }
    Write-OwnerOnlyProtectedJson -Path $StatePath -Value ([ordered]@{ schema = $schema; installRoot = $InstallRoot; installerSid = $InstallerSid; certificateThumbprint = $thumbprint; certificateSubject = $CertificateState.Subject; ownedCertificateStores = @($CertificateState.OwnedStores); completed = $false; rollbackRequired = $false; pendingCertificate = ($null -eq $CertificateState.Certificate) })
    Set-OwnerOnlyAcl (Split-Path -Parent $StatePath)
}
function New-OwnerOnlyCertificate([ref]$State, [string]$StatePath, [string]$InstallRoot, [string]$InstallerSid) {
    $subject = "CN=PC Steward Owner-Only Test $(New-Guid)"
    $State.Value = [pscustomobject]@{ Certificate = $null; Subject = $subject; OwnedStores = [Collections.Generic.List[string]]@('My') }
    Write-PendingLedger -CertificateState $State.Value -StatePath $StatePath -InstallRoot $InstallRoot -InstallerSid $InstallerSid
    $certificate = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation 'Cert:\CurrentUser\My' -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(14)
    $State.Value.Certificate = $certificate
    Write-PendingLedger -CertificateState $State.Value -StatePath $StatePath -InstallRoot $InstallRoot -InstallerSid $InstallerSid
    foreach ($storeName in @('Root', 'TrustedPublisher')) {
        $State.Value.OwnedStores.Add($storeName)
        Write-PendingLedger -CertificateState $State.Value -StatePath $StatePath -InstallRoot $InstallRoot -InstallerSid $InstallerSid
        $store = [Security.Cryptography.X509Certificates.X509Store]::new($storeName, [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try { $store.Add($certificate) }
        finally { $store.Close() }
    }
}
function Get-DeploymentFiles([string]$Path) { @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName | ForEach-Object { [ordered]@{ path = $_.FullName.Substring($Path.Length + 1); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }) }
function Assert-ExactDacl([string]$Path) {
    $expected = @{ 'S-1-5-18' = [Security.AccessControl.FileSystemRights]::FullControl; 'S-1-5-32-544' = [Security.AccessControl.FileSystemRights]::FullControl; 'S-1-5-32-545' = ([Security.AccessControl.FileSystemRights]::ReadAndExecute -bor [Security.AccessControl.FileSystemRights]::Synchronize) }
    $acl = Get-Acl -LiteralPath $Path
    try { $ownerSid = ([Security.Principal.NTAccount]::new([string]$acl.Owner)).Translate([Security.Principal.SecurityIdentifier]).Value } catch { throw "Protected ownership validation failed: $Path" }
    if (!$acl.AreAccessRulesProtected -or $ownerSid -ne 'S-1-5-32-544') { throw "Protected ownership/inheritance validation failed: $Path" }
    $observed = @{}
    foreach ($rule in $acl.Access) {
        try { $sid = $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { throw "An unresolvable ACL identity was found on $Path." }
        if ($rule.AccessControlType -ne 'Allow' -or $rule.IsInherited -or $rule.InheritanceFlags -ne [Security.AccessControl.InheritanceFlags]::None -or $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None -or !$expected.ContainsKey($sid) -or $observed.ContainsKey($sid) -or $rule.FileSystemRights -ne $expected[$sid]) { throw "Protected ACL validation failed: $Path" }
        $observed[$sid] = $true
    }
    if ($observed.Count -ne $expected.Count) { throw "Protected ACL is incomplete: $Path" }
}
function Assert-TrustedDeployment([string]$InstallRoot, [string]$StateRoot, [string]$ExpectedThumbprint, [object[]]$ExpectedFiles) {
    $desktop = Join-Path $InstallRoot 'PCSteward.Desktop.exe'; $helper = Join-Path $InstallRoot 'PCSteward.ElevatedHelper.exe'
    foreach ($path in @($StateRoot) + @(Get-ChildItem -LiteralPath $StateRoot -Force -Recurse).FullName + @($InstallRoot) + @(Get-ChildItem -LiteralPath $InstallRoot -Force -Recurse).FullName) {
        if (!(Test-Path -LiteralPath $path) -or ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Protected deployment path check failed: $path" }; Assert-ExactDacl $path
    }
    $desktopSignature = Get-AuthenticodeSignature -LiteralPath $desktop; $helperSignature = Get-AuthenticodeSignature -LiteralPath $helper
    if ($desktopSignature.Status -ne 'Valid' -or $helperSignature.Status -ne 'Valid' -or $desktopSignature.SignerCertificate.Thumbprint -ne $ExpectedThumbprint -or $helperSignature.SignerCertificate.Thumbprint -ne $ExpectedThumbprint) { throw 'Desktop/helper Authenticode validation failed or the signer differs.' }
    if (((Get-DeploymentFiles $InstallRoot | ConvertTo-Json -Depth 4 -Compress) -ne ($ExpectedFiles | ConvertTo-Json -Depth 4 -Compress))) { throw 'The protected deployment file manifest does not match the signed installation.' }
}

if (!$Install) { throw 'Installation is state-changing. Re-run with the explicit -Install switch.' }
if (!$CandidateRoot -or !$CandidateManifestPath -or !$CandidateManifestSha256) { throw 'Specify the exact approved CandidateRoot, CandidateManifestPath and CandidateManifestSha256. Installer-time compilation is not supported.' }
if ($CertificateThumbprint -or !$CreateOwnerOnlyCertificate) { throw 'The pinned route requires a fresh owner-only certificate; reused CertificateThumbprint is not supported.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this owner-only deployment script from an elevated PowerShell session.' }
. (Join-Path $PSScriptRoot 'OwnerOnlyPayloadIntegrity.ps1')
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installRoot = Join-Path $programFiles 'PC Steward'
$stateRoot = Join-Path $programFiles 'PC Steward OwnerOnlyTest State'
$staging = Join-Path $stateRoot ("staging-" + [Guid]::NewGuid().ToString('N'))
$statePath = Join-Path $stateRoot 'deployment.json'
$installerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$deploymentMutex = Enter-OwnerOnlyMutex
try {
    if (Test-Path -LiteralPath $installRoot) { throw "Refusing to replace an existing protected deployment: $installRoot" }
    if (Test-Path -LiteralPath $stateRoot) { throw "Refusing to reuse owner-only deployment state: $stateRoot" }
    # A pinned PCSteward.DeploymentCapability.txt makes this a protocol-2 payload; its bytes are validated here, before any effect.
    $candidate = Read-OwnerOnlyCandidateManifest -Path $CandidateManifestPath -ExpectedSha256 $CandidateManifestSha256 -PayloadRoot $CandidateRoot
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($CandidateRoot,$candidate.Files)
    $certificateState = $null; $ownsInstallRoot = $false; $ownsStateRoot = $false
    try {
    New-Item -ItemType Directory -Path $stateRoot -ErrorAction Stop | Out-Null; $ownsStateRoot = $true; Set-OwnerOnlyAcl $stateRoot
    New-Item -ItemType Directory -Path $staging -ErrorAction Stop | Out-Null; Set-OwnerOnlyAcl $staging
    if ((Get-Item -LiteralPath $staging).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Protected staging is a reparse point.' }
    $payloadStaging = Join-Path $staging 'payload'
    New-Item -ItemType Directory -Path $payloadStaging -ErrorAction Stop | Out-Null; Set-OwnerOnlyAcl $payloadStaging
    [PCSteward.Deployment.PayloadIntegrity]::CopyTreeProtected($CandidateRoot,$payloadStaging,$candidate.Files)
    Set-OwnerOnlyAcl $stateRoot
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($payloadStaging,$candidate.Files)
    $executableImages = @(foreach ($leaf in @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe')) {
        $path = Join-Path $payloadStaging $leaf
        if ((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'NotSigned') { throw "Candidate executable must be unsigned: $leaf" }
        $image = [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage($path)
        [ordered]@{ path=$leaf; bytes=$image.bytes; originalChecksum=[long]$image.originalChecksum }
    })
    $protectedManifest = Join-Path $stateRoot 'candidate-manifest.json'
    [PCSteward.Deployment.PayloadIntegrity]::WriteProtectedFile($protectedManifest,$candidate.Raw)
    Set-OwnerOnlyAcl $stateRoot
    $null = Read-OwnerOnlyCandidateManifest -Path $protectedManifest -ExpectedSha256 $candidate.Sha256 -PayloadRoot $payloadStaging
    # No certificate-store mutation or signing until the protected payload is exact.
    New-OwnerOnlyCertificate ([ref]$certificateState) -StatePath $statePath -InstallRoot $installRoot -InstallerSid $installerSid
    $certificate = $certificateState.Certificate; if ($certificate.NotAfter -le (Get-Date)) { throw 'The code-signing certificate is expired.' }
    Write-PendingLedger -CertificateState $certificateState -StatePath $statePath -InstallRoot $installRoot -InstallerSid $installerSid
    $verifiedSignedFiles = [Collections.Generic.Dictionary[string,PCSteward.Deployment.PayloadFile]]::new([StringComparer]::Ordinal)
    foreach ($image in $executableImages) {
        $path = Join-Path $payloadStaging $image.path
        $signature = Set-AuthenticodeSignature -FilePath $path -Certificate $certificate -HashAlgorithm SHA256
        if ($signature.Status -ne 'Valid' -or !$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Could not sign $($image.path) with the owner-only certificate." }
        $original = @($candidate.Files | Where-Object { $_.path -ceq $image.path })[0]
        $verifiedSignedFiles.Add($image.path,(Test-OwnerOnlySignedExecutable -Path $path -Identity ([PCSteward.Deployment.ImageIdentity]::new($original.bytes,$original.sha256,[uint32]$image.originalChecksum)) -ExpectedThumbprint $certificate.Thumbprint))
    }
    $signedPayload = [Collections.Generic.List[PCSteward.Deployment.PayloadFile]]::new()
    foreach ($file in $candidate.Files) {
        if ($file.path -cin @('PCSteward.Desktop.exe','PCSteward.ElevatedHelper.exe')) {
            $signedPayload.Add($verifiedSignedFiles[$file.path])
        } else { $signedPayload.Add($file) }
    }
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($payloadStaging,$signedPayload.ToArray())
    # The complete signing/identity/trust profile passes in protected staging before install.
    New-Item -ItemType Directory -Path $installRoot -ErrorAction Stop | Out-Null; $ownsInstallRoot = $true
    Set-OwnerOnlyAcl $installRoot
    [PCSteward.Deployment.PayloadIntegrity]::CopyTreeProtected($payloadStaging,$installRoot,$signedPayload.ToArray())
    Set-OwnerOnlyAcl $installRoot
    Remove-Item -LiteralPath $staging -Recurse -Force
    $files = @($signedPayload.ToArray() | Sort-Object path | ForEach-Object { [ordered]@{path=$_.path;sha256=$_.sha256} })
    $completedState = [ordered]@{ schema = $schema; installRoot = $installRoot; installerSid = $installerSid; certificateThumbprint = $certificate.Thumbprint; certificateSubject = $certificate.Subject; ownedCertificateStores = @($certificateState.OwnedStores); createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); completed = $true; files = $files; payloadOrigin = $candidate.PayloadOrigin; candidateManifestSha256 = $candidate.Sha256; executableImages = $executableImages }
    Write-OwnerOnlyProtectedJson -Path $statePath -Value $completedState
    Set-OwnerOnlyAcl $stateRoot
    Assert-TrustedDeployment -InstallRoot $installRoot -StateRoot $stateRoot -ExpectedThumbprint $certificate.Thumbprint -ExpectedFiles $files
    $origin = Assert-OwnerOnlyPayloadProvenance -InstallRoot $installRoot -StateRoot $stateRoot -State (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json)
    if ($origin -cne $candidate.PayloadOrigin) { throw 'New deployment lacks the pinned-payload provenance implied by its approved manifest.' }
    [pscustomobject]@{ deployment = 'owner-only-protected-test'; installRoot = $installRoot; certificateThumbprint = $certificate.Thumbprint; validation = 'Pass'; launched = $false; applicationUpdates = 0 } | ConvertTo-Json
}
catch {
    $originalFailure = $_.Exception.Message
    $rollbackFailures = [Collections.Generic.List[string]]::new()
    if ($ownsInstallRoot -and (Test-Path -LiteralPath $installRoot)) {
        try { Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction Stop } catch { $rollbackFailures.Add("installation removal: $($_.Exception.Message)") }
    }
    if ($ownsInstallRoot -and (Test-Path -LiteralPath $installRoot)) { $rollbackFailures.Add("installation remains at $installRoot") }
    if ($rollbackFailures.Count -eq 0 -and $certificateState -and $certificateState.Certificate) {
        try { Remove-ExactCertificate -Thumbprint $certificateState.Certificate.Thumbprint -Stores @($certificateState.OwnedStores); Assert-ExactCertificateAbsent -Thumbprint $certificateState.Certificate.Thumbprint -Stores @($certificateState.OwnedStores) } catch { $rollbackFailures.Add("certificate removal: $($_.Exception.Message)") }
    }
    if ($rollbackFailures.Count -eq 0 -and $certificateState -and !$certificateState.Certificate -and $certificateState.Subject) {
        try { Remove-PendingCertificate -Subject $certificateState.Subject; Assert-PendingCertificateAbsent -Subject $certificateState.Subject } catch { $rollbackFailures.Add("pending certificate removal: $($_.Exception.Message)") }
    }
    if ($rollbackFailures.Count -eq 0 -and $ownsStateRoot -and (Test-Path -LiteralPath $stateRoot)) {
        try { Remove-Item -LiteralPath $stateRoot -Recurse -Force -ErrorAction Stop } catch { $rollbackFailures.Add("state removal: $($_.Exception.Message)") }
    }
    if ($rollbackFailures.Count -gt 0) {
        if ($ownsStateRoot -and (Test-Path -LiteralPath $stateRoot)) {
            Write-OwnerOnlyProtectedJson -Path $statePath -Value ([ordered]@{ schema = $schema; installRoot = $installRoot; installerSid = $installerSid; certificateThumbprint = if ($certificateState -and $certificateState.Certificate) { $certificateState.Certificate.Thumbprint } else { $null }; certificateSubject = if ($certificateState) { $certificateState.Subject } else { $null }; ownedCertificateStores = if ($certificateState) { @($certificateState.OwnedStores) } else { @() }; completed = $false; rollbackRequired = $true; rollbackFailures = @($rollbackFailures); originalFailure = $originalFailure })
            Set-OwnerOnlyAcl $stateRoot
        }
        throw "Owner-only deployment failed: $originalFailure. Rollback is incomplete: $($rollbackFailures -join '; ')"
    }
    throw $originalFailure
}
}
finally {
    $deploymentMutex.ReleaseMutex()
    $deploymentMutex.Dispose()
}
