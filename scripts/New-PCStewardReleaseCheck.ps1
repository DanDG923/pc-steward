[CmdletBinding()]
param(
    # The release payload folder the release-payload workflow built: payload\, candidate-manifest.json, release-payload.json
    # and msi-reference.json.
    [Parameter(Mandatory)][string]$ReleasePayloadRoot,
    # A new, empty folder for release-check.json (uploaded alone as the pc-steward-release-check artefact).
    [Parameter(Mandatory)][string]$OutputRoot
)

# ADR-032 revision 2 §3 (FR-UPD-04/15, TM-03/04/13): the small CI record an installed PC Steward uses to tell which CI build a
# signed release must match. GitHub publishes the SHA-256 of the artefact this file is uploaded in; the app accepts the file
# only when that digest belongs to a green, hand-started release-payload.yml run of this repository at the commit it names.
# It holds the run, commit, versions and pins from release-payload.json, the MSI tables digest from msi-reference.json, every
# payload file's path, length and SHA-256 from the candidate manifest, and the PE checksum of the two executables (needed to
# compare a signed executable with its unsigned CI original). Canonical bytes: fixed key order, no whitespace, files sorted
# by ordinal path, ASCII only, no BOM, so Windows PowerShell 5.1 and pwsh 7 write the same file. Nothing is signed,
# installed or trusted here.
$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath($ReleasePayloadRoot)
$output = [IO.Path]::GetFullPath($OutputRoot)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -ne 0) { throw "$output is not empty." }
$signedLeaves = @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe')

$record = Get-Content -LiteralPath (Join-Path $root 'release-payload.json') -Raw | ConvertFrom-Json
$reference = Get-Content -LiteralPath (Join-Path $root 'msi-reference.json') -Raw | ConvertFrom-Json
$productVersion = [string]$record.productVersion
$msiVersion = ($productVersion -split '-', 2)[0]
if ($record.schema -cne 'pc-steward-release-payload:1') { throw 'release-payload.json has an unknown schema.' }
if ([string]$record.repository -cnotmatch '\A[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}\z') { throw 'release-payload.json names no repository.' }
if ([string]$record.runId -cnotmatch '\A[1-9][0-9]{0,18}\z') { throw 'release-payload.json names no run.' }
if ([string]$record.sourceCommit -cnotmatch '\A[0-9a-f]{40}\z') { throw 'release-payload.json names no commit.' }
if ($productVersion -cnotmatch '\A(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})(-[a-z0-9]+(-[a-z0-9]+)*)?\z') { throw "Unexpected product version $productVersion." }
foreach ($pin in @([string]$record.pins.primary, [string]$record.pins.backup)) { if ($pin -cnotmatch '\A[0-9A-F]{64}\z') { throw 'release-payload.json does not carry both pins.' } }
if ([string]$record.pins.primary -ceq [string]$record.pins.backup) { throw 'The two release pins must differ.' }
if ($reference.schema -cne 'pc-steward-msi-reference:1' -or [string]$reference.productVersion -cne $productVersion -or [string]$reference.tablesSha256 -cnotmatch '\A[0-9A-F]{64}\z') { throw 'msi-reference.json is not this payload''s MSI reference.' }

$payload = Join-Path $root 'payload'
function Get-Sha256Hex([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-', '') } finally { $sha.Dispose() }
}
# The candidate manifest is the one release-payload.json names, and every file it lists is in the payload with that length
# and hash. (The builder validated the tree before this; this script runs in the same job and only reads.)
$manifestBytes = [IO.File]::ReadAllBytes((Join-Path $root 'candidate-manifest.json'))
if ((Get-Sha256Hex $manifestBytes) -cne ([string]$record.manifestSha256).ToUpperInvariant()) { throw 'candidate-manifest.json is not the manifest release-payload.json names.' }
# Windows PowerShell 5.1 emits a top-level JSON array as one object, so enumerate it explicitly.
$parsedManifest = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes).TrimStart([char]0xfeff) | ConvertFrom-Json
$candidateFiles = @($parsedManifest | ForEach-Object { $_ })
if ($candidateFiles.Count -lt 1 -or $candidateFiles.Count -gt 4096) { throw 'The candidate manifest must list 1 to 4,096 files.' }
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($file in $candidateFiles) {
    if ([string]$file.path -cnotmatch '\A[\x20-\x7E]{1,260}\z' -or [string]$file.sha256 -cnotmatch '\A[0-9A-Fa-f]{64}\z' -or !$seen.Add([string]$file.path)) { throw "Malformed or repeated candidate manifest entry: $($file.path)" }
    $bytes = [IO.File]::ReadAllBytes((Join-Path $payload $file.path))
    if ($bytes.Length -ne [long]$file.bytes -or (Get-Sha256Hex $bytes) -cne ([string]$file.sha256).ToUpperInvariant()) { throw "$($file.path) differs from the candidate manifest." }
}

# The PE checksum of an unsigned PE32+ image with no certificate table and no overlay (the CI executables).
function Get-UnsignedImageChecksum([byte[]]$Data) {
    if ($Data.Length -lt 512 -or $Data[0] -ne 0x4D -or $Data[1] -ne 0x5A) { throw 'Not a PE image.' }
    $pe = [BitConverter]::ToInt32($Data, 0x3C)
    if ($pe -lt 64 -or $pe -gt $Data.Length - 264 -or [BitConverter]::ToUInt32($Data, $pe) -ne 0x00004550) { throw 'Not a PE image.' }
    $optional = $pe + 24
    if ([BitConverter]::ToUInt16($Data, $optional) -ne 0x20B) { throw 'Not a PE32+ image.' }
    $certificateOffset = [BitConverter]::ToUInt32($Data, $optional + 144)
    $certificateSize = [BitConverter]::ToUInt32($Data, $optional + 148)
    if ($certificateOffset -ne 0 -or $certificateSize -ne 0) { throw 'A CI executable must be unsigned.' }
    [BitConverter]::ToUInt32($Data, $optional + 64)
}

function ConvertTo-JsonString([string]$Value) {
    if ($Value -cnotmatch '\A[\x20-\x7E]*\z') { throw "Non-ASCII or control character in $Value." }
    '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

$files = [Collections.Generic.List[string]]::new()
$executables = [Collections.Generic.List[string]]::new()
$sorted = [Collections.Generic.List[object]]::new()
foreach ($file in $candidateFiles) { $sorted.Add($file) }
$sorted.Sort([Comparison[object]] { param($left, $right) [string]::CompareOrdinal([string]$left.path, [string]$right.path) })
foreach ($file in $sorted) {
    $path = [string]$file.path
    $hash = ([string]$file.sha256).ToUpperInvariant()
    $files.Add('{"path":' + (ConvertTo-JsonString $path) + ',"bytes":' + ([long]$file.bytes).ToString([Globalization.CultureInfo]::InvariantCulture) + ',"sha256":' + (ConvertTo-JsonString $hash) + '}')
    if ($signedLeaves -ccontains $path) {
        $checksum = Get-UnsignedImageChecksum ([IO.File]::ReadAllBytes((Join-Path $payload $path)))
        $executables.Add('{"path":' + (ConvertTo-JsonString $path) + ',"originalChecksum":' + ([uint32]$checksum).ToString([Globalization.CultureInfo]::InvariantCulture) + '}')
    }
}
if ($executables.Count -ne 2) { throw 'The payload must hold exactly the two release executables.' }

$manifestSha256 = ([string]$record.manifestSha256).ToUpperInvariant()
$json = '{"schema":"pc-steward-release-check:1"' +
    ',"repository":' + (ConvertTo-JsonString ([string]$record.repository)) +
    ',"runId":' + [string]$record.runId +
    ',"sourceCommit":' + (ConvertTo-JsonString ([string]$record.sourceCommit)) +
    ',"productVersion":' + (ConvertTo-JsonString $productVersion) +
    ',"msiVersion":' + (ConvertTo-JsonString $msiVersion) +
    ',"pins":{"primary":' + (ConvertTo-JsonString ([string]$record.pins.primary)) + ',"backup":' + (ConvertTo-JsonString ([string]$record.pins.backup)) + '}' +
    ',"candidateManifestSha256":' + (ConvertTo-JsonString $manifestSha256) +
    ',"msiTablesSha256":' + (ConvertTo-JsonString ([string]$reference.tablesSha256)) +
    ',"files":[' + ($files -join ',') + ']' +
    ',"executables":[' + ($executables -join ',') + ']}'
if ($json.Length -gt 1MB) { throw 'release-check.json is larger than 1 MiB.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$target = Join-Path $output 'release-check.json'
[IO.File]::WriteAllBytes($target, [Text.Encoding]::ASCII.GetBytes($json))
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = [BitConverter]::ToString($sha.ComputeHash([IO.File]::ReadAllBytes($target))).Replace('-', '') } finally { $sha.Dispose() }
[pscustomobject]@{ releaseCheck = $target; sha256 = $hash; files = $files.Count; productVersion = $productVersion } | ConvertTo-Json -Compress
