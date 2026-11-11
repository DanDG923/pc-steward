[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PayloadRoot,
    [Parameter(Mandatory)][string]$ProductVersion
)

# ADR-034 §4 (FR-UPD-15, TM-03/TM-13): write PCSteward.ReleaseManifest.json at the root of a complete payload folder. Run it
# after the executables are signed, so every hash is of the bytes the MSI installs. The format is the one
# ReleaseManifestPolicy accepts: {"format","productVersion","files":[{"path","bytes","sha256"}]}, paths relative to the root
# with backslashes, sorted ordinally, uppercase SHA-256, UTF-8 without BOM. Local only: reads the payload and writes one file.
# Windows PowerShell 5.1 and pwsh 7 both run it.
$ErrorActionPreference = 'Stop'
$manifestLeaf = 'PCSteward.ReleaseManifest.json'
$format = 'pc-steward-release-manifest:1'

function Test-PCStewardReleaseVersion([string]$Version) {
    $Version -cmatch '\A(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?\z'
}

function Test-PCStewardReleasePath([string]$Path) {
    if ([string]::IsNullOrEmpty($Path) -or $Path.Length -gt 240) { return $false }
    foreach ($segment in $Path.Split('\')) {
        if ($segment.Length -eq 0 -or $segment -eq '.' -or $segment -eq '..' -or $segment.EndsWith('.') -or $segment.EndsWith(' ')) { return $false }
        foreach ($character in $segment.ToCharArray()) {
            if ([int]$character -lt 0x20 -or [int]$character -gt 0x7E -or '<>:"/|?*'.IndexOf($character) -ge 0) { return $false }
        }
    }
    $true
}

if (!(Test-PCStewardReleaseVersion $ProductVersion)) { throw "ProductVersion is not major.minor.patch with an optional pre-release label: $ProductVersion" }
$root = [IO.Path]::GetFullPath($PayloadRoot).TrimEnd('\')
if (!(Test-Path -LiteralPath $root -PathType Container)) { throw "PayloadRoot does not exist: $root" }
foreach ($required in @('PCSteward.Desktop.exe', 'PCSteward.ElevatedHelper.exe', 'PCSteward.DeploymentCapability.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw "The payload is missing $required." }
}

$entries = [Collections.Generic.List[object]]::new()
$sha = [Security.Cryptography.SHA256]::Create()
try {
    foreach ($item in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "The payload contains a reparse point: $($item.FullName)" }
        if ($item.PSIsContainer) { continue }
        $relative = $item.FullName.Substring($root.Length + 1)
        if ($relative -ieq $manifestLeaf) { continue }
        if (!(Test-PCStewardReleasePath $relative)) { throw "The payload holds a path the release manifest cannot list: $relative" }
        $stream = [IO.File]::Open($item.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') } finally { $stream.Dispose() }
        $entries.Add([pscustomobject]@{ Path = $relative; Bytes = [long]$item.Length; Sha256 = $hash })
    }
}
finally { $sha.Dispose() }
if ($entries.Count -eq 0 -or $entries.Count -gt 4096) { throw "The payload must hold 1 to 4096 files; it holds $($entries.Count)." }
# Ordinal order. The keys array is sorted in place (a typed string[] variable, so PowerShell passes it without copying).
$byPath = @{}
foreach ($entry in $entries) { $byPath[$entry.Path] = $entry }
[string[]]$paths = @($entries | ForEach-Object { $_.Path })
[Array]::Sort($paths, [StringComparer]::Ordinal)
$sorted = @($paths | ForEach-Object { $byPath[$_] })

# Written by hand so the bytes are the same in Windows PowerShell 5.1 and pwsh 7 (paths are printable ASCII, checked above).
function ConvertTo-PCStewardJsonString([string]$Value) { '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"' }
$builder = [Text.StringBuilder]::new()
[void]$builder.Append('{"format":').Append((ConvertTo-PCStewardJsonString $format))
[void]$builder.Append(',"productVersion":').Append((ConvertTo-PCStewardJsonString $ProductVersion)).Append(',"files":[')
for ($index = 0; $index -lt $sorted.Length; $index++) {
    if ($index -gt 0) { [void]$builder.Append(',') }
    $entry = $sorted[$index]
    [void]$builder.Append('{"path":').Append((ConvertTo-PCStewardJsonString $entry.Path))
    [void]$builder.Append(',"bytes":').Append($entry.Bytes.ToString([Globalization.CultureInfo]::InvariantCulture))
    [void]$builder.Append(',"sha256":').Append((ConvertTo-PCStewardJsonString $entry.Sha256)).Append('}')
}
[void]$builder.Append(']}')
$bytes = [Text.UTF8Encoding]::new($false).GetBytes($builder.ToString())
if ($bytes.Length -gt 1048576) { throw 'The release manifest would exceed 1 MiB.' }
$target = Join-Path $root $manifestLeaf
[IO.File]::WriteAllBytes($target, $bytes)
$check = [Security.Cryptography.SHA256]::Create()
try { $manifestSha256 = [BitConverter]::ToString($check.ComputeHash($bytes)).Replace('-', '') } finally { $check.Dispose() }
[pscustomobject]@{ manifestPath = $target; manifestSha256 = $manifestSha256; fileCount = $sorted.Length; productVersion = $ProductVersion } | ConvertTo-Json -Compress
