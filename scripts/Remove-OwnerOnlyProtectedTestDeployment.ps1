[CmdletBinding()]
param([Parameter(Mandatory)][switch]$Remove)

$ErrorActionPreference = 'Stop'
$schema = 'pc-steward-owner-only-protected-test-v6'
function Enter-OwnerOnlyMutex() {
    $mutex = [Threading.Mutex]::new($false, 'Global\PCSteward.OwnerOnlyTestDeployment.v1')
    try { if (!$mutex.WaitOne(0)) { $mutex.Dispose(); throw 'Another owner-only protected deployment or recovery is already running.' } }
    catch [Threading.AbandonedMutexException] { }
    catch { $mutex.Dispose(); throw }
    return $mutex
}
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
function Assert-TrustedRemovalTree([string]$Path) {
    foreach ($item in @((Get-Item -LiteralPath $Path -Force)) + @(Get-ChildItem -LiteralPath $Path -Force -Recurse)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing removal because a protected deployment path is a reparse point: $($item.FullName)" }
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
    try {
        $matches = @($store.Certificates | Where-Object { $_.Subject -eq $Subject })
        if ($matches.Count -gt 1) { throw 'Refusing pending-certificate recovery because the unique owner-only test subject is ambiguous.' }
        foreach ($certificate in $matches) { if ($certificate.Subject -notlike 'CN=PC Steward Owner-Only Test *') { throw 'Refusing to remove a certificate that is not an owner-only PC Steward test certificate.' }; $store.Remove($certificate) }
    } finally { $store.Close() }
}
function Assert-PendingCertificateAbsent([string]$Subject) {
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('My', [Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try { if (@($store.Certificates | Where-Object { $_.Subject -eq $Subject }).Count -ne 0) { throw 'The pending owner-only test certificate remains in CurrentUser\\My.' } }
    finally { $store.Close() }
}

if (!$Remove) { throw 'Removal is destructive. Re-run with the explicit -Remove switch.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this owner-only removal script from an elevated PowerShell session.' }
$deploymentMutex = Enter-OwnerOnlyMutex
try {
$installRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'PC Steward'
$stateRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'PC Steward OwnerOnlyTest State'
$statePath = Join-Path $stateRoot 'deployment.json'
if (!(Test-Path -LiteralPath $statePath -PathType Leaf)) { throw 'Refusing removal because the protected deployment state is absent.' }
Assert-TrustedRemovalTree $stateRoot
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$stores = @($state.ownedCertificateStores)
$hasThumbprint = ![string]::IsNullOrWhiteSpace($state.certificateThumbprint) -and $state.certificateThumbprint -match '^[A-Fa-f0-9]{40}$'
$hasSubject = ![string]::IsNullOrWhiteSpace($state.certificateSubject)
$pendingCertificate = !$state.completed -and !$hasThumbprint -and $hasSubject -and $state.certificateSubject -like 'CN=PC Steward Owner-Only Test *' -and $stores.Count -eq 1 -and $stores[0] -eq 'My'
$emptyIncomplete = !$state.completed -and !$hasThumbprint -and !$hasSubject -and $stores.Count -eq 0
if ($state.schema -ne $schema -or $state.installRoot -ne $installRoot -or $state.installerSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -or @($stores | Where-Object { $_ -notin @('My', 'Root', 'TrustedPublisher') }).Count -ne 0 -or (!$hasThumbprint -and !$pendingCertificate -and !$emptyIncomplete)) { throw 'Refusing removal because the protected deployment state is not recognised for the current Windows user.' }
Get-Process -Name 'PCSteward.Desktop','PCSteward.ElevatedHelper' -ErrorAction SilentlyContinue | ForEach-Object { throw "Refusing removal while PC Steward is running (PID $($_.Id)). Close it and retry." }
if (Test-Path -LiteralPath $installRoot) { Assert-TrustedRemovalTree $installRoot; Remove-Item -LiteralPath $installRoot -Recurse -Force }
if ($hasThumbprint -and $stores.Count -gt 0) { Remove-ExactCertificate -Thumbprint $state.certificateThumbprint -Stores $stores; Assert-ExactCertificateAbsent -Thumbprint $state.certificateThumbprint -Stores $stores }
if ($pendingCertificate) { Remove-PendingCertificate -Subject $state.certificateSubject; Assert-PendingCertificateAbsent -Subject $state.certificateSubject }
if (Test-Path -LiteralPath $stateRoot) { Remove-Item -LiteralPath $stateRoot -Recurse -Force }
[pscustomobject]@{ deployment = 'owner-only-protected-test'; removed = $true; recoveredIncompleteDeployment = !$state.completed; certificateThumbprint = $state.certificateThumbprint; userDataPreserved = $true } | ConvertTo-Json
}
finally {
    $deploymentMutex.ReleaseMutex()
    $deploymentMutex.Dispose()
}
