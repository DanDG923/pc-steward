[CmdletBinding()]
param(
    # The downloaded pc-steward-release-payload zip, and the MSI New-PCStewardRelease.ps1 signed from it.
    [Parameter(Mandatory)][string]$ArtefactZip,
    [Parameter(Mandatory)][string]$MsiPath,
    # The anchor, given in the owner's paste (never read from this PC): the run, its commit, the artefact digest GitHub
    # publishes for the run, and the two committed release pins.
    [Parameter(Mandatory)][long]$RunId,
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][string]$ArtefactSha256,
    [Parameter(Mandatory)][string]$PrimaryPin,
    [Parameter(Mandatory)][string]$BackupPin,
    # The reviewed scripts' SHA-256, from the paste: the bootstrap template and the six scripts it copies.
    [Parameter(Mandatory)][hashtable]$ScriptPins,
    # First install only: remove the ADR-022 owner-only test deployment in the same elevated run.
    [switch]$RemoveOwnerOnlyTestDeployment,
    # Compose and print the approved command and its hashes; elevate nothing. CI runs the command itself.
    [switch]$ComposeOnly
)

# ADR-034 security review H1 (FR-UPD-04/15, TM-03/04/13): the one elevated step of a release install on the owner's PC.
# Run it in 64-bit Windows PowerShell 5.1 as the owner, not elevated. It composes one encoded command holding the anchor,
# the pinned OwnerOnlyExecution.ps1 functions and the pinned bootstrap template, asks Windows for one UAC prompt to run it,
# waits, and prints the protected result. The bootstrap copies the pinned scripts into a new protected execution root and
# runs Install-PCStewardRelease.ps1 there, which installs the MSI only if it is the CI build of the named run.
$ErrorActionPreference = 'Stop'
$scriptsRoot = $PSScriptRoot
$bootstrapLeaf = 'Invoke-PCStewardReleaseInstallBootstrap.ps1'
$bundle = @('Install-PCStewardRelease.ps1', 'OwnerOnlyExecution.ps1', 'OwnerOnlyPayloadIntegrity.ps1', 'PCStewardMsiTables.ps1', 'PCStewardReleaseSigning.ps1', 'Remove-OwnerOnlyProtectedTestDeployment.ps1')
foreach ($value in @($ArtefactSha256, $PrimaryPin, $BackupPin)) { if ($value -cnotmatch '\A[0-9A-F]{64}\z') { throw 'The digest and both pins must be 64 uppercase hex characters.' } }
if ($SourceCommit -cnotmatch '\A[0-9a-f]{40}\z' -or $RunId -le 0) { throw 'The run id and source commit are malformed.' }
$names = @($ScriptPins.Keys | ForEach-Object { [string]$_ } | Sort-Object { $_ } -CaseSensitive)
$expectedNames = @(@($bundle) + $bootstrapLeaf | Sort-Object { $_ } -CaseSensitive)
if (($names -join ',') -cne ($expectedNames -join ',')) { throw "ScriptPins must name exactly: $($expectedNames -join ', ')." }

function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}
# Each pinned script is read once here and must hash to its pin; the bootstrap checks its own copies again.
$texts = @{}
foreach ($name in $expectedNames) {
    $pin = [string]$ScriptPins[$name]
    if ($pin -cnotmatch '\A[0-9A-F]{64}\z') { throw "The pin for $name is malformed." }
    $bytes = [IO.File]::ReadAllBytes((Join-Path $scriptsRoot $name))
    if ((Get-Sha256Hex $bytes) -cne $pin) { throw "$name in $scriptsRoot is not the reviewed version (SHA-256 differs from the paste)." }
    $texts[$name] = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xfeff)
}

$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$executionRoot = Join-Path $programFiles ('PC Steward OwnerOnlyTest Execution ' + [Guid]::NewGuid().ToString('N'))
$record = [ordered]@{
    artefactSha256 = $ArtefactSha256; artefactZip = [IO.Path]::GetFullPath($ArtefactZip); backupPin = $BackupPin; executionRoot = $executionRoot
    installerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value; msiPath = [IO.Path]::GetFullPath($MsiPath); primaryPin = $PrimaryPin
    removeOwnerOnlyTestDeployment = [bool]$RemoveOwnerOnlyTestDeployment; runId = $RunId; schema = 'pc-steward-release-install-command:1'
    scriptSource = [IO.Path]::GetFullPath($scriptsRoot); scripts = @($bundle | ForEach-Object { [ordered]@{ path = $_; sha256 = [string]$ScriptPins[$_] } }); sourceCommit = $SourceCommit
}
$json = ConvertTo-Json -InputObject $record -Depth 4 -Compress
foreach ($character in $json.ToCharArray()) { if ([int]$character -lt 0x20 -or [int]$character -gt 0x7E) { throw 'The embedded record must be printable ASCII (move the files to a plain ASCII path).' } }
$helpers = $texts['OwnerOnlyExecution.ps1']; $template = $texts[$bootstrapLeaf]
if (!$template.Contains('$PCStewardReleaseInstallJson') -or $helpers.Contains('$PCStewardReleaseInstallJson')) { throw 'Unexpected bootstrap template or helper text.' }
$program = "# PC Steward release install program, composed by Invoke-PCStewardReleaseInstall.ps1.`n" + '$PCStewardReleaseInstallJson=''' + $json.Replace("'", "''") + "'`n" + $helpers + "`n" + $template
# Compressed to fit the command line; the stub runs the program only if it expands to exactly these bytes (exit 20 if not).
$programBytes = [Text.UTF8Encoding]::new($false).GetBytes($program)
$buffer = [IO.MemoryStream]::new()
try {
    $gzip = [IO.Compression.GZipStream]::new($buffer, [IO.Compression.CompressionMode]::Compress, $true)
    try { $gzip.Write($programBytes, 0, $programBytes.Length) } finally { $gzip.Dispose() }
    $compressed = [Convert]::ToBase64String($buffer.ToArray())
}
finally { $buffer.Dispose() }
$programSha256 = Get-Sha256Hex $programBytes
$stub = @'
$ErrorActionPreference='Stop'
$z=[IO.Compression.GZipStream]::new([IO.MemoryStream]::new([Convert]::FromBase64String('@@GZIP@@')),[IO.Compression.CompressionMode]::Decompress)
$m=[IO.MemoryStream]::new();try{$z.CopyTo($m)}finally{$z.Dispose()}
$b=$m.ToArray();$s=[Security.Cryptography.SHA256]::Create();try{$h=[BitConverter]::ToString($s.ComputeHash($b)).Replace('-','')}finally{$s.Dispose()}
if($h -cne '@@SHA256@@'){exit 20}
. ([ScriptBlock]::Create([Text.UTF8Encoding]::new($false,$true).GetString($b)))
'@
$stub = $stub.Replace("`r`n", "`n").Replace('@@GZIP@@', $compressed).Replace('@@SHA256@@', $programSha256)
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($stub))
if ($encoded.Length -gt 30000) { throw 'The encoded command exceeds the fixed 30,000 character bound.' }
$composed = [ordered]@{ programSha256 = $programSha256; encodedCommandSha256 = (Get-Sha256Hex ([Text.Encoding]::ASCII.GetBytes($encoded))); encodedCommandLength = $encoded.Length; executionRoot = $executionRoot }
if ($ComposeOnly) {
    $composed.encodedCommand = $encoded
    $composed | ConvertTo-Json -Compress
    return
}

# The real run: the owner's own normal window, the system 64-bit Windows PowerShell, and nothing that changes how the
# elevated .NET Framework process starts.
if ($PSVersionTable.PSEdition -cne 'Desktop' -or ![Environment]::Is64BitProcess -or !$PSHOME.TrimEnd('\').Equals('C:\Windows\System32\WindowsPowerShell\v1.0', [StringComparison]::OrdinalIgnoreCase)) { throw 'Run this in 64-bit Windows PowerShell 5.1 (C:\Windows\System32\WindowsPowerShell\v1.0).' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this from a normal (not elevated) Windows PowerShell window; it asks for elevation itself.' }
$environmentNames = @([Environment]::GetEnvironmentVariables().Keys) + @([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::User).Keys) + @([Environment]::GetEnvironmentVariables([EnvironmentVariableTarget]::Machine).Keys)
if (@($environmentNames | Where-Object { $_ -match '^(COMPLUS_|COR_ENABLE_PROFILING$|COR_PROFILER|APPDOMAIN_MANAGER_)' }).Count) { throw 'A .NET Framework startup override is set in the environment; remove it first.' }
foreach ($scope in @('MachinePolicy', 'UserPolicy')) { if ([string](Get-ExecutionPolicy -Scope $scope) -cne 'Undefined') { throw 'An enforced PowerShell execution policy is configured; this script does not override it.' } }
if (@(Get-Process -Name 'PCSteward*' -ErrorAction SilentlyContinue).Count) { throw 'PC Steward is running; close it first.' }
foreach ($path in @($record.artefactZip, $record.msiPath)) { if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "$path was not found." } }
$powershell = 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'
Write-Output ('Program SHA-256 ' + $programSha256 + '. Approve the one Windows PowerShell UAC prompt to install.')
$process = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded) -WorkingDirectory 'C:\Windows\System32' -Verb RunAs -WindowStyle Hidden -PassThru
$null = $process.Handle
if (!$process.WaitForExit(1800000)) { throw "The elevated install is still running after 30 minutes (process $($process.Id)); do not start another." }
$exitCode = $process.ExitCode
$resultPath = Join-Path $executionRoot 'result.json'
if (!(Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw "The elevated install exited with $exitCode and wrote no result (20 means the command did not expand to the composed program)." }
# The result is only trusted from the protected execution root, with its protected ACL.
. ([ScriptBlock]::Create($helpers))
Assert-OwnerOnlyExecutionAcl -Acl (Get-Acl -LiteralPath $resultPath)
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$result | ConvertTo-Json -Depth 5
if ($exitCode -ne 0 -or $result.exitCode -ne 0 -or $result.validation -cne 'Pass') { throw "The release install failed: $($result.phase) $($result.failure)" }
Write-Output "PASS: PC Steward $($result.productVersion) installed from run $RunId and verified against the CI build."
