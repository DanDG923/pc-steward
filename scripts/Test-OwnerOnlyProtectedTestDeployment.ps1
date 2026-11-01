[CmdletBinding()]
param([switch]$RequirePinnedPayload,[switch]$AllowLegacyPayload)

$ErrorActionPreference = 'Stop'
if ($RequirePinnedPayload -and $AllowLegacyPayload) { throw 'Choose the pinned profile or explicit legacy validation, not both.' }
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

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installRoot = Join-Path $programFiles 'PC Steward'; $stateRoot = Join-Path $programFiles 'PC Steward OwnerOnlyTest State'; $statePath = Join-Path $stateRoot 'deployment.json'
if (!(Test-Path -LiteralPath $statePath -PathType Leaf)) { throw 'No owner-only protected PC Steward test deployment is present.' }
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ($state.schema -ne 'pc-steward-owner-only-protected-test-v6' -or !$state.completed -or $state.rollbackRequired -or $state.installRoot -ne $installRoot -or $state.installerSid -ne [Security.Principal.WindowsIdentity]::GetCurrent().User.Value) { throw 'The protected deployment state is not recognised for the current Windows user.' }
$desktop = Join-Path $installRoot 'PCSteward.Desktop.exe'; $helper = Join-Path $installRoot 'PCSteward.ElevatedHelper.exe'
foreach ($leaf in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'App.xbf', 'PCSteward.Desktop.pri')) { if (!(Test-Path -LiteralPath (Join-Path $installRoot $leaf))) { throw "Protected deployment is missing required payload: $leaf" } }
if (@(Get-ChildItem -LiteralPath $installRoot -Filter '*.xbf' -File -Recurse).Count -eq 0) { throw 'Protected deployment is missing WinUI XBF resources.' }
foreach ($path in @($stateRoot) + @(Get-ChildItem -LiteralPath $stateRoot -Force -Recurse).FullName + @($installRoot) + @(Get-ChildItem -LiteralPath $installRoot -Force -Recurse).FullName) {
    if (!(Test-Path -LiteralPath $path) -or ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Protected deployment path check failed: $path" }; Assert-ExactDacl $path
}
$desktopSignature = Get-AuthenticodeSignature -LiteralPath $desktop; $helperSignature = Get-AuthenticodeSignature -LiteralPath $helper
if ($desktopSignature.Status -ne 'Valid' -or $helperSignature.Status -ne 'Valid' -or $desktopSignature.SignerCertificate.Thumbprint -ne $state.certificateThumbprint -or $helperSignature.SignerCertificate.Thumbprint -ne $state.certificateThumbprint) { throw 'Desktop/helper signing validation failed.' }
if (((Get-DeploymentFiles $installRoot | ConvertTo-Json -Depth 4 -Compress) -ne (@($state.files) | ConvertTo-Json -Depth 4 -Compress))) { throw 'The protected deployment file manifest does not match.' }
. (Join-Path $PSScriptRoot 'OwnerOnlyPayloadIntegrity.ps1')
$origin = Assert-OwnerOnlyPayloadProvenance -InstallRoot $installRoot -StateRoot $stateRoot -State $state
Assert-OwnerOnlyPayloadProfile -Origin $origin -AllowLegacyPayload:$AllowLegacyPayload
$report = [ordered]@{ deployment = 'owner-only-protected-test'; installRoot = $installRoot; certificateThumbprint = $state.certificateThumbprint; validation = 'Pass'; payloadOrigin = $origin; candidateManifestSha256 = $state.candidateManifestSha256; launched = $false; applicationUpdates = 0 }
# ADR-022 alignment note item 2: a protocol-2 deployment also reports the helper protocol of its pinned capability statement.
if ($origin -ceq 'pinned-unsigned-v2') { $report.helperProtocol = (Read-OwnerOnlyCandidateManifest -Path (Join-Path $stateRoot 'candidate-manifest.json') -ExpectedSha256 $state.candidateManifestSha256 -PayloadRoot $installRoot).HelperProtocol }
[pscustomobject]$report | ConvertTo-Json
