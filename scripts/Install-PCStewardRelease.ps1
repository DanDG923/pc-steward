[CmdletBinding()]
param(
    # The pc-steward-release-payload artefact zip and the signed MSI made from it (both in folders the owner can write).
    [Parameter(Mandatory)][string]$ArtefactZip,
    [Parameter(Mandatory)][string]$MsiPath,
    # The trust anchor, from the owner's paste: the release-payload run, its commit, the artefact's SHA-256 digest that
    # GitHub publishes, and both committed release pins.
    [Parameter(Mandatory)][long]$RunId,
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][string]$ArtefactSha256,
    [Parameter(Mandatory)][string]$PrimaryPin,
    [Parameter(Mandatory)][string]$BackupPin,
    # This run's folder in the protected execution root, for the copies, the logs and the result.
    [Parameter(Mandatory)][string]$WorkRoot,
    # First install only: remove the ADR-022 owner-only test deployment (and its test certificates) before installing.
    [switch]$RemoveOwnerOnlyTestDeployment
)

# ADR-034 security review H1 (FR-UPD-04/15, TM-03/04/13): install a signed PC Steward MSI only when what it installs is the
# CI build of the run the owner named. Elevated, and only from the protected execution bundle (Assert-OwnerOnlyExecutionEnvironment),
# started by Invoke-PCStewardReleaseInstall.ps1 with the anchor embedded in the approved command. Release signing runs
# unelevated, so nothing it wrote is trusted here:
#   1. the artefact zip is copied into the protected folder and must have the digest from the paste; it is unpacked there;
#      its release-payload.json must name that run, commit and both pins; the candidate payload must match its manifest;
#   2. the MSI is copied into the protected folder; the copy must carry a release pin, and its tables must give the digest
#      the CI build published in msi-reference.json (PCStewardMsiTables.ps1), so it runs no code and installs only there;
#   3. msiexec runs on that copy only, with fixed arguments;
#   4. the installed tree must be exactly the CI payload (each executable equal to the CI one apart from its signature,
#      which must carry a release pin) plus a release manifest that lists it, under the protected folder DACL.
# On any difference after msiexec, the product is uninstalled and the run fails. Writes nothing outside WorkRoot, the
# install folder Windows Installer manages, and (with -RemoveOwnerOnlyTestDeployment) what the reviewed removal script removes.
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'This step runs elevated, from the protected execution bundle only.' }
. (Join-Path $PSScriptRoot 'OwnerOnlyExecution.ps1')
Assert-OwnerOnlyExecutionEnvironment -ScriptRoot $PSScriptRoot
. (Join-Path $PSScriptRoot 'OwnerOnlyPayloadIntegrity.ps1')
. (Join-Path $PSScriptRoot 'PCStewardReleaseSigning.ps1')
. (Join-Path $PSScriptRoot 'PCStewardMsiTables.ps1')

$repository = 'DanDG923/pc-steward'
$upgradeCode = '{2AD17330-BAD5-46BE-A409-20814B26673C}'
$folderSddl = 'D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)'
$signedLeaves = @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')
$manifestLeaf = 'PCSteward.ReleaseManifest.json'
foreach ($value in @($ArtefactSha256, $PrimaryPin, $BackupPin)) { if ($value -cnotmatch '\A[0-9A-F]{64}\z') { throw 'The digest and both pins must be 64 uppercase hex characters.' } }
if ($PrimaryPin -ceq $BackupPin) { throw 'The two release pins must differ.' }
if ($SourceCommit -cnotmatch '\A[0-9a-f]{40}\z' -or $RunId -le 0) { throw 'The run id and source commit are malformed.' }
$pins = @($PrimaryPin, $BackupPin)
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$installRoot = Join-Path $programFiles 'PC Steward'
$stateRoot = Join-Path $programFiles 'PC Steward OwnerOnlyTest State'
$work = Assert-OwnerOnlyExecutionPath -Path $WorkRoot
$executionRoot = Split-Path -Parent $PSScriptRoot
if (!$work.StartsWith($executionRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'WorkRoot must be inside this execution root.' }
Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $work) -Directory
$phase = 'inputs'
$result = [ordered]@{ schema = 'pc-steward-release-install:1'; runId = $RunId; sourceCommit = $SourceCommit; artefactSha256 = $ArtefactSha256 }

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}
# One read of the source through a held, reparse-free handle (the path the owner gave, no alias or alternate stream), hashed,
# checked against the expected digest when there is one, and only then written into the protected folder. The copy is
# readable by administrators only, so a path aimed at another file cannot expose it; it is hashed again after writing.
function Copy-IntoProtected([string]$Source, [string]$Leaf, [long]$MaximumBytes, [string]$ExpectedSha256) {
    $lease = [PCSteward.Deployment.PayloadIntegrity]::HoldStableFile([IO.Path]::GetFullPath($Source))
    try {
        if ($lease.Length -gt $MaximumBytes) { throw "$Leaf is larger than its bound." }
        $buffer = [IO.MemoryStream]::new()
        try { $lease.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $buffer.Dispose() }
    }
    finally { $lease.Dispose() }
    $hash = Get-Sha256Hex $bytes
    if ($ExpectedSha256 -and $hash -cne $ExpectedSha256) { throw "The SHA-256 of $Leaf is $hash, not the run's $ExpectedSha256." }
    $target = Join-Path $work $Leaf
    $output = [IO.FileStream]::new($target, [IO.FileMode]::CreateNew, [Security.AccessControl.FileSystemRights]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::None, (New-OwnerOnlyExecutionSecurity -Compiler))
    try { $output.Write($bytes, 0, $bytes.Length); $output.Flush($true) } finally { $output.Dispose() }
    if ((Get-Sha256Hex ([IO.File]::ReadAllBytes($target))) -cne $hash) { throw "The protected copy of $Leaf differs." }
    [pscustomobject]@{ Path = $target; Sha256 = $hash; Bytes = [long]$bytes.Length }
}
function Get-RelatedProducts {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    @($installer.GetType().InvokeMember('RelatedProducts', 'GetProperty', $null, $installer, @($upgradeCode)) | ForEach-Object {
        [pscustomobject]@{ ProductCode = [string]$_; Version = [string]$installer.GetType().InvokeMember('ProductInfo', 'GetProperty', $null, $installer, @([string]$_, 'VersionString')) }
    })
}
function Invoke-Msiexec([string[]]$Arguments, [string]$LogLeaf) {
    $msiexec = Join-Path ([Environment]::SystemDirectory) 'msiexec.exe'
    $process = Start-Process -FilePath $msiexec -ArgumentList (@($Arguments) + @('/qn', '/norestart', '/l*v', ('"' + (Join-Path $work $LogLeaf) + '"'))) -WorkingDirectory ([Environment]::SystemDirectory) -Wait -PassThru
    $process.ExitCode
}

$installed = $false
try {
    # 1. The artefact, from the digest in the paste.
    $zip = Copy-IntoProtected $ArtefactZip 'artefact.zip' 1GB $ArtefactSha256
    $artefact = Join-Path $work 'artefact'
    [IO.Directory]::CreateDirectory($artefact, (New-OwnerOnlyExecutionSecurity -Directory)) | Out-Null
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zip.Path)
    try {
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', '\')
            if ($relative.EndsWith('\')) { continue }
            foreach ($segment in $relative.Split('\')) { if ($segment -in @('', '.', '..') -or $segment.Contains(':')) { throw "Unsafe artefact entry: $($entry.FullName)" } }
            $target = [IO.Path]::GetFullPath((Join-Path $artefact $relative))
            if (!$target.StartsWith($artefact + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $target)) { throw "Unsafe or repeated artefact entry: $($entry.FullName)" }
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            $entryStream = $entry.Open()
            try { $buffer = [IO.MemoryStream]::new(); try { $entryStream.CopyTo($buffer); [PCSteward.Deployment.PayloadIntegrity]::WriteProtectedFile($target, $buffer.ToArray()) } finally { $buffer.Dispose() } }
            finally { $entryStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $record = Get-Content -LiteralPath (Join-Path $artefact 'release-payload.json') -Raw | ConvertFrom-Json
    if ($record.schema -cne 'pc-steward-release-payload:1' -or $record.repository -cne $repository -or [string]$record.runId -cne [string]$RunId -or $record.sourceCommit -cne $SourceCommit) { throw 'release-payload.json does not name the run and commit in the paste.' }
    if ([string]$record.pins.primary -cne $PrimaryPin -or [string]$record.pins.backup -cne $BackupPin) { throw 'release-payload.json does not carry the pins in the paste.' }
    $productVersion = [string]$record.productVersion
    $msiVersion = ($productVersion -split '-', 2)[0]
    $candidateRoot = Join-Path $artefact 'payload'
    $candidate = Read-OwnerOnlyCandidateManifest -Path (Join-Path $artefact 'candidate-manifest.json') -ExpectedSha256 ([string]$record.manifestSha256) -PayloadRoot $candidateRoot
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($candidateRoot, $candidate.Files)
    $reference = Get-Content -LiteralPath (Join-Path $artefact 'msi-reference.json') -Raw | ConvertFrom-Json
    if ($reference.schema -cne 'pc-steward-msi-reference:1' -or [string]$reference.productVersion -cne $productVersion -or [string]$reference.tablesSha256 -cnotmatch '\A[0-9A-F]{64}\z') { throw 'msi-reference.json is not this payload''s MSI reference.' }
    $result.productVersion = $productVersion; $result.candidateManifestSha256 = $candidate.Sha256

    # 2. The MSI: a protected copy, a release pin, and the CI build's tables.
    $phase = 'msi'
    $msi = Copy-IntoProtected $MsiPath "PCSteward-$msiVersion.msi" 1GB ''
    $result.msiSha256 = $msi.Sha256
    $result.msiPin = Test-PCStewardPinnedSignature -Path $msi.Path -Pins $pins
    $tables = Get-PCStewardMsiTables -Path $msi.Path
    [IO.File]::WriteAllLines((Join-Path $work 'msi-tables.txt'), [string[]]$tables.Lines)
    if ($tables.Sha256 -cne [string]$reference.tablesSha256) { throw "The MSI's tables ($($tables.Sha256)) differ from the CI build's ($($reference.tablesSha256)); see msi-tables.txt." }
    if ((Get-Sha256Hex ([IO.File]::ReadAllBytes($msi.Path))) -cne $msi.Sha256) { throw 'The protected MSI copy changed.' }
    $result.tablesSha256 = $tables.Sha256

    # 3. Remove the owner-only test deployment (first install only), then install the protected copy.
    $phase = 'preinstall'
    if (@(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count) { throw 'PC Steward is running; close it first.' }
    if ($RemoveOwnerOnlyTestDeployment) {
        $removal = & (Join-Path $PSScriptRoot 'Remove-OwnerOnlyProtectedTestDeployment.ps1') -Remove
        Write-OwnerOnlyProtectedJson -Path (Join-Path $work 'removal-result.json') -Value ($removal | ConvertFrom-Json)
        if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath $stateRoot)) { throw 'The owner-only test deployment is still present after removal.' }
    }
    elseif (Test-Path -LiteralPath $stateRoot) { throw 'The owner-only test deployment is installed; run with -RemoveOwnerOnlyTestDeployment.' }
    $phase = 'msiexec'
    $before = @(Get-RelatedProducts)
    $code = Invoke-Msiexec @('/i', ('"' + $msi.Path + '"')) 'msiexec-install.log'
    $products = @(Get-RelatedProducts)
    $result.msiexecExitCode = $code
    # Anything msiexec may have committed is treated as installed, so a failure from here on uninstalls it (fail closed):
    # 3010 and 1641 are committed successes that need a restart, and any change to the installed products counts too.
    $changed = (@($products | ForEach-Object { $_.ProductCode + '=' + $_.Version }) -join ',') -cne (@($before | ForEach-Object { $_.ProductCode + '=' + $_.Version }) -join ',')
    if ($code -in @(0, 1641, 3010) -or $changed) { $installed = $true }
    if ($code -in @(1641, 3010)) { throw "msiexec committed the install but needs a restart (exit code $code), so the installed files cannot be verified now (see msiexec-install.log)." }
    if ($code -ne 0) { throw "msiexec failed with exit code $code (see msiexec-install.log)." }
    if ($products.Count -ne 1 -or $products[0].Version -cne $msiVersion) { throw "Expected one installed PC Steward $msiVersion, found: $(($products | ForEach-Object { $_.Version }) -join ', ')." }
    $result.productCode = $products[0].ProductCode; $result.replacedVersions = @($before | ForEach-Object { $_.Version })

    # 4. The installed tree is the CI payload, signed with a release key, plus a release manifest that lists it.
    $phase = 'verify'
    $null = Assert-OwnerOnlyExecutionPath -Path $installRoot
    # The folder DACL the package sets ($folderSddl): protected, SYSTEM and Administrators full control, Users read and
    # execute, all inherited by files and folders. Every file inherits exactly that.
    $acl = Get-Acl -LiteralPath $installRoot
    $inherit = [Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object { '{0}|{1}|{2}|{3}|{4}' -f $_.IdentityReference.Value, [int]$_.FileSystemRights, $_.AccessControlType, $_.IsInherited, ($_.InheritanceFlags -eq $inherit) })
    $expectedRules = @("S-1-5-18|$([int][Security.AccessControl.FileSystemRights]::FullControl)|Allow|False|True", "S-1-5-32-544|$([int][Security.AccessControl.FileSystemRights]::FullControl)|Allow|False|True", "S-1-5-32-545|$(0x1200a9)|Allow|False|True")
    if (!$acl.AreAccessRulesProtected -or @(Compare-Object ($rules | Sort-Object) ($expectedRules | Sort-Object)).Count -ne 0) { throw "The install folder DACL is not $folderSddl`: $($rules -join '; ')" }
    foreach ($item in @(Get-ChildItem -LiteralPath $installRoot -Recurse -Force)) {
        $itemRules = @((Get-Acl -LiteralPath $item.FullName).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object { '{0}|{1}|{2}' -f $_.IdentityReference.Value, [int]$_.FileSystemRights, $_.AccessControlType })
        if (@(Compare-Object ($itemRules | Sort-Object) (@("S-1-5-18|$([int][Security.AccessControl.FileSystemRights]::FullControl)|Allow", "S-1-5-32-544|$([int][Security.AccessControl.FileSystemRights]::FullControl)|Allow", "S-1-5-32-545|$(0x1200a9)|Allow") | Sort-Object)).Count -ne 0) { throw "$($item.FullName) does not carry exactly the inherited install-folder DACL." }
    }
    $expected = [Collections.Generic.List[PCSteward.Deployment.PayloadFile]]::new()
    foreach ($file in $candidate.Files) {
        if ($signedLeaves -ccontains $file.path) {
            $path = Join-Path $installRoot $file.path
            $identity = [PCSteward.Deployment.PayloadIntegrity]::GetUnsignedImage((Join-Path $candidateRoot $file.path))
            $lease = [PCSteward.Deployment.PayloadIntegrity]::HoldStableFile($path)
            try {
                [PCSteward.Deployment.PayloadIntegrity]::AssertSignedImage($path, $identity)
                $null = Test-PCStewardPinnedSignature -Path $path -Pins $pins
                [PCSteward.Deployment.PayloadIntegrity]::AssertHeldPath($lease, $path)
                $expected.Add([PCSteward.Deployment.PayloadFile]::new($file.path, $lease.Length, [PCSteward.Deployment.PayloadIntegrity]::HashHeldFile($lease)))
            }
            finally { $lease.Dispose() }
        }
        else { $expected.Add($file) }
    }
    $manifestPath = Join-Path $installRoot $manifestLeaf
    $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
    $releaseManifest = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes) | ConvertFrom-Json
    $listed = @($releaseManifest.files | ForEach-Object { '{0}|{1}|{2}' -f $_.path, $_.bytes, $_.sha256 })
    $actual = @($expected | ForEach-Object { '{0}|{1}|{2}' -f $_.path, $_.bytes, $_.sha256.ToUpperInvariant() })
    if ($releaseManifest.format -cne 'pc-steward-release-manifest:1' -or [string]$releaseManifest.productVersion -cne $productVersion -or @(Compare-Object ($listed | Sort-Object) ($actual | Sort-Object) -CaseSensitive).Count -ne 0) { throw 'The installed release manifest does not list exactly the installed CI payload.' }
    $expected.Add([PCSteward.Deployment.PayloadFile]::new($manifestLeaf, [long]$manifestBytes.Length, (Get-Sha256Hex $manifestBytes)))
    [PCSteward.Deployment.PayloadIntegrity]::AssertTree($installRoot, $expected.ToArray())
    $result.installedFiles = $expected.Count
    $result.validation = 'Pass'
    $result
}
catch {
    $failure = "$phase`: $($_.Exception.Message)"
    if ($installed) {
        # Fail closed: an install that does not verify is removed.
        foreach ($product in @(Get-RelatedProducts)) { $null = Invoke-Msiexec @('/x', $product.ProductCode) 'msiexec-uninstall.log' }
        $failure += ' The installed product was uninstalled.'
        if (@(Get-RelatedProducts).Count -ne 0 -or (Test-Path -LiteralPath $installRoot)) { $failure += ' WARNING: the uninstall did not complete; do not run PC Steward.' }
    }
    throw $failure
}
