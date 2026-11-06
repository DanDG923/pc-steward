param(
    [Parameter(Mandatory)][string]$RepositoryRoot,
    [Parameter(Mandatory)][string]$WorkRoot,
    [Parameter(Mandatory)][string]$FixtureInstallRoot,
    [Parameter(Mandatory)][string]$FixtureInstallerSid
)
# FR-UPD-15, TM-03/04/07/13; Task 50 adapter gates A.7 "Regression"/"Rollback", A2-L1; ADR-022 alignment note.
# Runs under pwsh 7 (Linux or Windows) and Windows PowerShell 5.1. It executes the REAL text of the deployment-script
# functions and ledger-writing statements, taken from their parsed AST, so a script edit changes what this fixture emits.
# Only Windows-only primitives are stubbed: the protected ACL file write (plain write into WorkRoot), Set-OwnerOnlyAcl,
# the PE identity read and the Authenticode query. No elevation, certificate store, Program Files or installed root is used.
$ErrorActionPreference = 'Stop'
$scripts = Join-Path $RepositoryRoot 'scripts'
$fixtures = Join-Path (Join-Path (Join-Path $RepositoryRoot 'tests') 'PCSteward.Security.FixtureTests') 'Fixtures'
$results = [ordered]@{ shell = ($PSVersionTable.PSEdition + ' ' + $PSVersionTable.PSVersion.ToString()) }

function Read-ScriptAst([string]$Path) {
    $tokens = $null; $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
    [pscustomobject]@{ Ast = $ast; Errors = @($errors).Count }
}
function Find-Single($Ast, [scriptblock]$Predicate, [string]$What) {
    $found = @($Ast.FindAll($Predicate, $true))
    if ($found.Count -ne 1) { throw "ASSERT: expected exactly one $What, found $($found.Count)." }
    $found[0]
}
function Get-FunctionText($Ast, [string]$Name) {
    (Find-Single $Ast ({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name }.GetNewClosure()) "function $Name").Extent.Text
}
function Get-Sha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}
function Get-Failure([scriptblock]$Action) { try { & $Action | Out-Null; return $null } catch { return $_.Exception.Message } }

# 1. Parse check: every deployment script and fixture parses with zero errors in this shell.
$parse = [ordered]@{}
foreach ($name in @('OwnerOnlyPayloadIntegrity.ps1', 'Install-OwnerOnlyProtectedTestDeployment.ps1', 'Test-OwnerOnlyProtectedTestDeployment.ps1',
        'Invoke-OwnerOnlyApprovedRedeployment.ps1', 'Remove-OwnerOnlyProtectedTestDeployment.ps1', 'OwnerOnlyExecution.ps1', 'Wait-OwnerOnlyDeploymentProcess.ps1')) {
    $parse[$name] = (Read-ScriptAst (Join-Path $scripts $name)).Errors
}
foreach ($name in @('PayloadIntegrity.ps1', 'DeploymentCapabilityScripts.ps1', 'PreV2PayloadValidator.ps1.txt')) {
    $parse['Fixtures/' + $name] = (Read-ScriptAst (Join-Path $fixtures $name)).Errors
}
$results.parseErrors = $parse

# 2. Real function definitions. Windows-only primitives are stubbed by a same-named stand-in type.
$integrityAst = (Read-ScriptAst (Join-Path $scripts 'OwnerOnlyPayloadIntegrity.ps1')).Ast
$installerAst = (Read-ScriptAst (Join-Path $scripts 'Install-OwnerOnlyProtectedTestDeployment.ps1')).Ast
Add-Type -TypeDefinition @'
using System;
using System.IO;
namespace PCSteward.Deployment {
    public sealed class PayloadFile {
        public string path { get; private set; }
        public long bytes { get; private set; }
        public string sha256 { get; private set; }
        public PayloadFile(string path, long bytes, string sha256) { this.path = path; this.bytes = bytes; this.sha256 = sha256; }
    }
    public sealed class ImageIdentity {
        public long bytes { get; private set; }
        public string sha256 { get; private set; }
        public uint originalChecksum { get; private set; }
        public ImageIdentity(long bytes, string sha256, uint originalChecksum) { this.bytes = bytes; this.sha256 = sha256; this.originalChecksum = originalChecksum; }
    }
    // Stand-in for the Windows-only primitives: a plain file write instead of the protected ACL write, and a fixed
    // pseudo-checksum instead of the PE parse. Nothing else of the real type is used by the code under test here.
    public static class PayloadIntegrity {
        public static void WriteProtectedFile(string path, byte[] data) { File.WriteAllBytes(path, data); }
        public static ImageIdentity GetUnsignedImage(string path) { byte[] data = File.ReadAllBytes(path); return new ImageIdentity(data.Length, "", (uint)(data.Length * 7)); }
    }
}
'@
function Set-OwnerOnlyAcl { param([string]$Path) }
function Get-AuthenticodeSignature { param([string]$LiteralPath) [pscustomobject]@{ Status = 'NotSigned' } }
foreach ($name in @('ConvertFrom-OwnerOnlyDeploymentCapability', 'Assert-OwnerOnlyDeploymentCapabilityBytes', 'Get-OwnerOnlyCandidatePayloadOrigin',
        'Write-OwnerOnlyProtectedJson', 'Assert-OwnerOnlyPayloadProfile', 'Assert-OwnerOnlyPayloadProvenance')) {
    . ([scriptblock]::Create((Get-FunctionText $integrityAst $name)))
}
. ([scriptblock]::Create((Get-FunctionText $installerAst 'Write-PendingLedger')))
$schemaAssignment = Find-Single $installerAst { param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$schema' } 'installer $schema assignment'
. ([scriptblock]::Create($schemaAssignment.Extent.Text))

# 3. Capability statement vectors written by the C# test: script parse versus DeploymentCapabilityPolicy.TryParseCapability.
$capabilityResults = @(foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $WorkRoot 'capability') -File | Sort-Object Name)) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $parsed = $null; $parseFailure = Get-Failure { $script:parsed = ConvertFrom-OwnerOnlyDeploymentCapability -Bytes $bytes }
    $acceptFailure = Get-Failure { Assert-OwnerOnlyDeploymentCapabilityBytes -Bytes $bytes }
    [ordered]@{
        name = $file.BaseName
        parsed = ($null -eq $parseFailure)
        protocol = $(if ($null -eq $parseFailure) { $parsed.HelperProtocol } else { $null })
        operations = $(if ($null -eq $parseFailure) { ($parsed.Operations -join ',') } else { $null })
        accepted = ($null -eq $acceptFailure)
    }
})
$results.capability = $capabilityResults

# 4. Synthetic payload and pinned manifests (v1 without, v2 with the checked-in capability statement).
$leaves = @([IO.File]::ReadAllLines((Join-Path $WorkRoot 'bindingset.txt')) | Where-Object { $_ }) + @('PCSteward.ScheduledHost.dll', 'App.xbf', 'PCSteward.Desktop.pri', 'Resources\Main.xbf')
$capabilityBytes = [IO.File]::ReadAllBytes((Join-Path (Join-Path (Join-Path $RepositoryRoot 'src') 'PCSteward.ElevatedHelper') 'PCSteward.DeploymentCapability.txt'))
$payloadStaging = Join-Path $WorkRoot 'payload'
[void][IO.Directory]::CreateDirectory($payloadStaging)
$entries = @{}
foreach ($leaf in $leaves) {
    $bytes = if ($leaf -ceq 'PCSteward.DeploymentCapability.txt') { $capabilityBytes } else { [Text.Encoding]::UTF8.GetBytes('content ' + $leaf) }
    if ($leaf -like 'PCSteward.*.exe') { [IO.File]::WriteAllBytes((Join-Path $payloadStaging $leaf), $bytes) }
    $entries[$leaf] = [PCSteward.Deployment.PayloadFile]::new($leaf, $bytes.Length, (Get-Sha256 $bytes))
}
$manifests = [ordered]@{}
foreach ($kind in @('v1', 'v2')) {
    $files = @($entries.Values | Where-Object { $kind -eq 'v2' -or $_.path -cne 'PCSteward.DeploymentCapability.txt' } | Sort-Object path)
    $json = ConvertTo-Json -InputObject @($files | ForEach-Object { [ordered]@{ bytes = $_.bytes; path = $_.path; sha256 = $_.sha256 } }) -Depth 4
    $raw = [Text.UTF8Encoding]::new($false).GetBytes($json)
    [IO.File]::WriteAllBytes((Join-Path $WorkRoot "manifest-$kind.json"), $raw)
    $manifests[$kind] = [pscustomobject]@{ Files = [PCSteward.Deployment.PayloadFile[]]$files; Sha256 = (Get-Sha256 $raw) }
}
$capabilityEntry = $entries['PCSteward.DeploymentCapability.txt']
$origins = [ordered]@{
    v1 = Get-OwnerOnlyCandidatePayloadOrigin -Files $manifests.v1.Files
    v2 = Get-OwnerOnlyCandidatePayloadOrigin -Files $manifests.v2.Files
    nested = Get-Failure { Get-OwnerOnlyCandidatePayloadOrigin -Files (@($manifests.v1.Files) + [PCSteward.Deployment.PayloadFile]::new('sub\PCSteward.DeploymentCapability.txt', 103, $capabilityEntry.sha256)) }
    wrongCase = Get-Failure { Get-OwnerOnlyCandidatePayloadOrigin -Files (@($manifests.v1.Files) + [PCSteward.Deployment.PayloadFile]::new('pcsteward.deploymentcapability.txt', 103, $capabilityEntry.sha256)) }
    empty = Get-Failure { Get-OwnerOnlyCandidatePayloadOrigin -Files (@($manifests.v1.Files) + [PCSteward.Deployment.PayloadFile]::new('PCSteward.DeploymentCapability.txt', 0, $capabilityEntry.sha256)) }
    oversize = Get-Failure { Get-OwnerOnlyCandidatePayloadOrigin -Files (@($manifests.v1.Files) + [PCSteward.Deployment.PayloadFile]::new('PCSteward.DeploymentCapability.txt', 1025, $capabilityEntry.sha256)) }
}
$results.origins = $origins

# 5. Ledgers from the real writers.
$ledgers = Join-Path $WorkRoot 'ledgers'
[void][IO.Directory]::CreateDirectory($ledgers)
$installRoot = $FixtureInstallRoot; $installerSid = $FixtureInstallerSid
$subject = 'CN=PC Steward Owner-Only Test 3f2504e0-4f89-11d3-9a0c-0305e82c3301'
$thumbprint = '0123456789ABCDEF0123456789ABCDEF01234567'
# 5a. Write-PendingLedger (New-OwnerOnlyCertificate's first call, then with the certificate and all stores).
Write-PendingLedger -CertificateState ([pscustomobject]@{ Certificate = $null; Subject = $subject; OwnedStores = [Collections.Generic.List[string]]@('My') }) -StatePath (Join-Path $ledgers 'pending-null-thumbprint.json') -InstallRoot $installRoot -InstallerSid $installerSid
Write-PendingLedger -CertificateState ([pscustomobject]@{ Certificate = [pscustomobject]@{ Thumbprint = $thumbprint }; Subject = $subject; OwnedStores = [Collections.Generic.List[string]]@('My', 'Root', 'TrustedPublisher') }) -StatePath (Join-Path $ledgers 'pending-with-certificate.json') -InstallRoot $installRoot -InstallerSid $installerSid
# 5b. The installer's failure-path rollback ledger statement, evaluated with zero, one and several owned stores.
$rollbackWrite = Find-Single $installerAst { param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Write-OwnerOnlyProtectedJson' -and $node.Extent.Text -like '*rollbackRequired = $true*' } 'rollback ledger write'
$rollbackFailures = [Collections.Generic.List[string]]::new(); $rollbackFailures.Add('certificate removal: fixture'); $originalFailure = 'fixture failure'
foreach ($case in @(
        [pscustomobject]@{ Name = 'rollback-zero-stores'; State = $null },
        [pscustomobject]@{ Name = 'rollback-one-store'; State = [pscustomobject]@{ Certificate = $null; Subject = $subject; OwnedStores = [Collections.Generic.List[string]]@('My') } },
        [pscustomobject]@{ Name = 'rollback-several-stores'; State = [pscustomobject]@{ Certificate = [pscustomobject]@{ Thumbprint = $thumbprint }; Subject = $subject; OwnedStores = [Collections.Generic.List[string]]@('My', 'Root', 'TrustedPublisher') } })) {
    $certificateState = $case.State; $statePath = Join-Path $ledgers ($case.Name + '.json')
    . ([scriptblock]::Create($rollbackWrite.Extent.Text))
}
# 5c. The completed ledger: the installer's $executableImages, $files and $completedState statements and its write.
$imagesAssignment = Find-Single $installerAst { param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$executableImages' } 'executable image assignment'
$filesAssignment = Find-Single $installerAst { param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$files' } 'ledger files assignment'
$completedAssignment = Find-Single $installerAst { param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -eq '$completedState' } 'completed ledger assignment'
$completedWrite = Find-Single $installerAst { param($node) $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Write-OwnerOnlyProtectedJson' -and $node.Extent.Text -like '*$completedState*' } 'completed ledger write'
$certificate = [pscustomobject]@{ Thumbprint = $thumbprint; Subject = $subject }
$certificateState = [pscustomobject]@{ Certificate = $certificate; Subject = $subject; OwnedStores = [Collections.Generic.List[string]]@('My', 'Root', 'TrustedPublisher') }
foreach ($kind in @('v1', 'v2')) {
    $candidate = [pscustomobject]@{ PayloadOrigin = (Get-OwnerOnlyCandidatePayloadOrigin -Files $manifests[$kind].Files); Sha256 = $manifests[$kind].Sha256 }
    $signedPayload = [Collections.Generic.List[PCSteward.Deployment.PayloadFile]]::new()
    foreach ($file in $manifests[$kind].Files) { $signedPayload.Add($file) }
    . ([scriptblock]::Create($imagesAssignment.Extent.Text))
    . ([scriptblock]::Create($filesAssignment.Extent.Text))
    . ([scriptblock]::Create($completedAssignment.Extent.Text))
    $statePath = Join-Path $ledgers "completed-$kind.json"
    . ([scriptblock]::Create($completedWrite.Extent.Text))
}

# 6. Validator profile and early provenance refusals (current code), and the FROZEN pre-change v1-only validator on a v2 ledger.
$stateRoot = Join-Path $WorkRoot 'state'
[void][IO.Directory]::CreateDirectory($stateRoot)
[IO.File]::Copy((Join-Path $WorkRoot 'manifest-v2.json'), (Join-Path $stateRoot 'candidate-manifest.json'))
$v2Ledger = [IO.File]::ReadAllText((Join-Path $ledgers 'completed-v2.json')) | ConvertFrom-Json
$unknown = [IO.File]::ReadAllText((Join-Path $ledgers 'completed-v2.json')) | ConvertFrom-Json; $unknown.payloadOrigin = 'pinned-unsigned-v3'
$nonString = [IO.File]::ReadAllText((Join-Path $ledgers 'completed-v2.json')) | ConvertFrom-Json; $nonString.payloadOrigin = @('pinned-unsigned-v2')
$results.profile = [ordered]@{
    v1 = Get-Failure { Assert-OwnerOnlyPayloadProfile -Origin 'pinned-unsigned-v1' }
    v2 = Get-Failure { Assert-OwnerOnlyPayloadProfile -Origin 'pinned-unsigned-v2' }
    legacyDefault = Get-Failure { Assert-OwnerOnlyPayloadProfile -Origin 'legacy-v6' }
    legacyAllowed = Get-Failure { Assert-OwnerOnlyPayloadProfile -Origin 'legacy-v6' -AllowLegacyPayload }
    unknown = Get-Failure { Assert-OwnerOnlyPayloadProfile -Origin 'pinned-unsigned-v3' }
    provenanceUnknownOrigin = Get-Failure { Assert-OwnerOnlyPayloadProvenance -InstallRoot $payloadStaging -StateRoot $stateRoot -State $unknown }
    provenanceNonStringOrigin = Get-Failure { Assert-OwnerOnlyPayloadProvenance -InstallRoot $payloadStaging -StateRoot $stateRoot -State $nonString }
}
$frozen = [IO.File]::ReadAllText((Join-Path $fixtures 'PreV2PayloadValidator.ps1.txt'))
$results.preV2 = & {
    . ([scriptblock]::Create($frozen))
    [ordered]@{
        provenanceOnV2Ledger = Get-Failure { Assert-OwnerOnlyPayloadProvenance -InstallRoot $payloadStaging -StateRoot $stateRoot -State $v2Ledger }
        profileOnV2 = Get-Failure { Assert-OwnerOnlyPayloadProfile -Origin 'pinned-unsigned-v2' }
    }
}
$results.manifestSha256 = [ordered]@{ v1 = $manifests.v1.Sha256; v2 = $manifests.v2.Sha256 }
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'), (ConvertTo-Json -InputObject $results -Depth 6), [Text.UTF8Encoding]::new($false))
Write-Output "PASS: deployment capability script fixtures ($($results.shell))"
