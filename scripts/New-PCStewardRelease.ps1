[CmdletBinding()]
param(
    # The pc-steward-release-payload artefact zip, downloaded from the run's page on GitHub.
    [Parameter(Mandatory)][string]$PayloadZip,
    # The release-payload workflow run that built it (the number in the run's address).
    [Parameter(Mandatory)][long]$RunId,
    # A new folder for the signed payload, the MSI and release.json.
    [Parameter(Mandatory)][string]$OutputRoot,
    # Sign with the offline backup key (its PFX on a backup drive) instead of the YubiKey. Only for a release that moves
    # the primary pin to a new key.
    [string]$BackupPfx
)

# ADR-034 §5 (FR-UPD-04/15, TM-03/04/13): turn a CI-built release payload into a signed PC Steward MSI on the owner's PC.
# Run it in Windows PowerShell 5.1 as the owner, not elevated, from a clone of the repository at the payload's commit.
#   1. GitHub (read-only, no sign-in): the run is this repository's release-payload workflow, started by hand on
#      phase/4-beta or main, finished green; its one pc-steward-release-payload artefact's SHA-256 digest equals the
#      downloaded zip.
#   2. The payload: release-payload.json names that run and commit; the candidate manifest re-validates from disk with the
#      builder's own validator; both compiled pins are the ones this script signs with.
#   3. Signing: both executables with the release key (YubiKey: PIN and a touch each), then the release manifest, the MSI
#      built with the pinned WiX SDK, and the MSI signature (one more touch). Every signature is checked with the §3 rules.
# Nothing is installed, trusted or uploaded. Installing the MSI is a separate administrator step.
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Run this script in Windows PowerShell 5.1 (powershell.exe).' }
foreach ($module in @('Management', 'Utility', 'Security', 'Archive')) {
    $manifest = [IO.Path]::Combine($PSHOME, 'Modules', "Microsoft.PowerShell.$module", "Microsoft.PowerShell.$module.psd1")
    if (Test-Path -LiteralPath $manifest) { Import-Module $manifest -Force }
}
. (Join-Path $PSScriptRoot 'PCStewardReleaseSigning.ps1')
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from a normal (not elevated) PowerShell window.' }

$repository = 'DanDG923/pc-steward'
$workflowPath = '.github/workflows/release-payload.yml'
$artifactName = 'pc-steward-release-payload'
$allowedBranches = @('phase/4-beta', 'main')
$primarySubject = 'CN=PC Steward Release Signing'
$timestampServer = 'http://timestamp.digicert.com'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

$output = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) { throw "OutputRoot already exists: $output" }
$zip = [IO.Path]::GetFullPath($PayloadZip)
if (!(Test-Path -LiteralPath $zip -PathType Leaf)) { throw "PayloadZip was not found: $zip" }

function Get-PCStewardFileSha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create(); $stream = [IO.File]::OpenRead($Path)
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose(); $sha.Dispose() }
}

# 1. The run and its artefact, from GitHub's public API.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'pc-steward-release-script' }
$run = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$RunId" -Headers $headers
if ($run.repository.full_name -cne $repository -or $run.head_repository.full_name -cne $repository) { throw 'The run is not from this repository.' }
if ($run.path -cne $workflowPath -or $run.event -cne 'workflow_dispatch') { throw "Run $RunId is not a hand-started release-payload run." }
if ($run.status -cne 'completed' -or $run.conclusion -cne 'success') { throw "Run $RunId did not finish successfully." }
if ($allowedBranches -cnotcontains [string]$run.head_branch) { throw "Run $RunId built $($run.head_branch), not $($allowedBranches -join ' or ')." }
if ([string]$run.head_sha -notmatch '\A[0-9a-f]{40}\z') { throw 'The run has no commit.' }
$artifacts = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/actions/runs/$RunId/artifacts" -Headers $headers
$artifact = @($artifacts.artifacts | Where-Object { $_.name -ceq $artifactName })
if ($artifact.Count -ne 1 -or $artifact[0].expired) { throw "Run $RunId has no single, unexpired $artifactName artefact." }
if ([string]$artifact[0].digest -notmatch '\Asha256:([0-9a-fA-F]{64})\z') { throw 'GitHub publishes no SHA-256 digest for the artefact.' }
$expectedZip = $Matches[1].ToUpperInvariant()
$zipHash = Get-PCStewardFileSha256 $zip
if ($zipHash -cne $expectedZip) { throw "The zip's SHA-256 $zipHash is not the artefact's $expectedZip. Download it again from run $RunId." }

# The local scripts that build and check the MSI must be the payload's commit.
$localCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $localCommit -cne [string]$run.head_sha) { throw "Check out commit $($run.head_sha) in $repositoryRoot first (this clone is at $localCommit)." }
$dirty = @(& git -C $repositoryRoot status --porcelain --untracked-files=no)
if ($dirty.Count -ne 0) { throw 'The clone has uncommitted changes to tracked files.' }
# Nothing untracked or ignored beside the scripts and the installer project either (the MSI build itself runs on copies
# of the tracked project files in a new folder; this keeps a stray or planted file from being mistaken for one of them).
$extra = @(& git -C $repositoryRoot status --porcelain --untracked-files=all --ignored -- scripts packaging)
if ($LASTEXITCODE -ne 0 -or $extra.Count -ne 0) { throw "The clone has untracked or ignored files under scripts or packaging: $(($extra | Select-Object -First 5) -join '; ')" }

New-Item -ItemType Directory -Path $output | Out-Null
$staging = Join-Path $output 'artefact'
Expand-Archive -LiteralPath $zip -DestinationPath $staging

# 2. The payload record and the candidate re-validated with the builder's own validator.
$record = Get-Content -LiteralPath (Join-Path $staging 'release-payload.json') -Raw | ConvertFrom-Json
if ($record.schema -cne 'pc-steward-release-payload:1' -or $record.repository -cne $repository -or [string]$record.runId -cne [string]$RunId -or $record.sourceCommit -cne [string]$run.head_sha) {
    throw 'release-payload.json does not name this run and commit.'
}
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'
$candidate = Join-Path $output 'candidate'
New-Item -ItemType Directory -Path $candidate | Out-Null
Copy-Item -LiteralPath (Join-Path $staging 'payload'), (Join-Path $staging 'candidate-manifest.json') -Destination $candidate -Recurse
$validation = @(& $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'New-OwnerOnlyCandidate.ps1'), '-ValidateCandidateRoot', $candidate, '-ExpectedManifestSha256', [string]$record.manifestSha256))
if ($LASTEXITCODE -ne 0 -or (ConvertFrom-Json ([string]($validation | Where-Object { $_ } | Select-Object -Last 1))).validation -cne 'Pass') { throw 'The payload does not match its candidate manifest.' }
$pins = @([string]$record.pins.primary, [string]$record.pins.backup)
if (@($pins | Where-Object { $_ -cmatch '\A[0-9A-F]{64}\z' }).Count -ne 2 -or $pins[0] -ceq $pins[1]) { throw 'The payload does not carry two distinct release pins.' }
# They must be the pins committed at the run's commit, read from this clean clone, not only what the payload record says.
$pinsSource = [IO.File]::ReadAllText((Join-Path $repositoryRoot 'src\PCSteward.Execution\ReleaseSigningPins.cs'))
$committedPins = @(foreach ($name in @('Primary', 'Backup')) {
    $found = [regex]::Matches($pinsSource, 'internal const string ' + $name + ' = "([0-9A-F]{64})";')
    if ($found.Count -ne 1) { throw "ReleaseSigningPins.cs at this commit has no single $name pin." }
    $found[0].Groups[1].Value
})
if ($committedPins[0] -cne $pins[0] -or $committedPins[1] -cne $pins[1]) { throw 'The payload record pins differ from ReleaseSigningPins.cs at the run commit.' }
# The pins must be compiled into the payload's Execution assembly (its constants are stored as UTF-16).
$execution = [IO.File]::ReadAllBytes((Join-Path $candidate 'payload\PCSteward.Execution.dll'))
$executionText = [Text.Encoding]::Unicode.GetString($execution, 0, $execution.Length - ($execution.Length % 2)) + [Text.Encoding]::Unicode.GetString($execution, 1, ($execution.Length - 1) - (($execution.Length - 1) % 2))
foreach ($pin in $pins) { if (!$executionText.Contains($pin)) { throw "Pin $pin is not compiled into the payload." } }

# The signing key: the YubiKey certificate in the personal store, or the backup PFX.
$ownedPfx = $null
if ($BackupPfx) {
    $password = Read-Host -AsSecureString 'Backup key password'
    try { $ownedPfx = [Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.Path]::GetFullPath($BackupPfx), $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet) }
    finally { $password.Dispose() }
    $certificate = $ownedPfx
    $pin = $pins[1]
}
else {
    $pin = $pins[0]
    $found = @(Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My' | Where-Object { $_.Subject -ceq $primarySubject -and $_.HasPrivateKey -and (Get-PCStewardKeyPin -CertificateDer $_.RawData) -ceq $pin })
    if ($found.Count -ne 1) { throw 'Plug in the YubiKey: its release certificate is not in the personal store.' }
    $certificate = $found[0]
}
try {
    if ((Get-PCStewardKeyPin -CertificateDer $certificate.RawData) -cne $pin) { throw 'The signing key does not carry the payload pin it is meant for.' }
    # 3. Sign, write the manifest, build and sign the MSI.
    $payload = Join-Path $output 'payload'
    Copy-Item -LiteralPath (Join-Path $candidate 'payload') -Destination $payload -Recurse
    $msiVersion = ([string]$record.productVersion -split '-', 2)[0]
    if ($BackupPfx) { Write-Output 'Signing with the backup key.' } else { Write-Output 'Signing three files with the YubiKey: enter your PIN and touch the YubiKey each time it blinks.' }
    $result = Invoke-PCStewardReleaseSigning -PayloadRoot $payload -Certificate $certificate -Pin $pin -ProductVersion ([string]$record.productVersion) -MsiPath (Join-Path $output "PCSteward-$msiVersion.msi") -TimestampServer $timestampServer
}
finally {
    if ($ownedPfx) { $ownedPfx.Dispose() }
}
$release = [ordered]@{
    schema = 'pc-steward-release:1'; repository = $repository; runId = $RunId; sourceCommit = [string]$run.head_sha; artefactSha256 = $zipHash
    productVersion = $result.productVersion; msiVersion = $result.msiVersion; msiSha256 = $result.msiSha256; manifestSha256 = $result.manifestSha256
    fileCount = $result.fileCount; signedWith = $(if ($BackupPfx) { 'backup' } else { 'primary' }); pin = $pin
}
[IO.File]::WriteAllText((Join-Path $output 'release.json'), ($release | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Remove-Item -LiteralPath $staging, $candidate -Recurse -Force
Write-Output "PASS: signed PC Steward $($result.productVersion) MSI at $($result.msiPath) (SHA-256 $($result.msiSha256))."
