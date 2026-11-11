[CmdletBinding()]
param(
    # The release payload folder: payload\, candidate-manifest.json and release-payload.json (or, in the CI MSI job, the same
    # layout built there). msi-reference.json is written into it.
    [Parameter(Mandatory)][string]$ReleasePayloadRoot
)

# ADR-034 security review H1 (FR-UPD-15, TM-04/13): build the release MSI once, unsigned, from the CI payload, exactly as
# Invoke-PCStewardReleaseSigning does on the owner's PC apart from the signatures (release manifest, then New-PCStewardMsi.ps1),
# and publish the normalised digest of its tables (PCStewardMsiTables.ps1) as msi-reference.json. The owner's signing script
# and the elevated install step both require the signed MSI to give this digest. Nothing is signed, installed or trusted;
# the build folder is deleted. The payload folder itself is not changed.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PCStewardMsiTables.ps1')
$root = [IO.Path]::GetFullPath($ReleasePayloadRoot)
$record = Get-Content -LiteralPath (Join-Path $root 'release-payload.json') -Raw | ConvertFrom-Json
$productVersion = [string]$record.productVersion
if (!$productVersion) { throw 'release-payload.json names no product version.' }
$msiVersion = ($productVersion -split '-', 2)[0]
$target = Join-Path $root 'msi-reference.json'
if (Test-Path -LiteralPath $target) { throw "msi-reference.json already exists in $root." }

$work = Join-Path ([IO.Path]::GetTempPath()) ('pcsteward-msi-reference-' + [Guid]::NewGuid().ToString('N'))
try {
    $payload = Join-Path $work 'payload'
    New-Item -ItemType Directory -Path $work | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'payload') -Destination $payload -Recurse
    $null = & (Join-Path $PSScriptRoot 'New-PCStewardReleaseManifest.ps1') -PayloadRoot $payload -ProductVersion $productVersion
    $msiPath = Join-Path $work "PCSteward-$msiVersion.msi"
    $null = & (Join-Path $PSScriptRoot 'New-PCStewardMsi.ps1') -PayloadRoot $payload -MsiVersion $msiVersion -OutputPath $msiPath
    $tables = Get-PCStewardMsiTables -Path $msiPath
    $reference = [ordered]@{
        schema = 'pc-steward-msi-reference:1'; productVersion = $productVersion; msiVersion = $msiVersion
        tablesSha256 = $tables.Sha256; normalised = @('Property ProductCode', 'File FileSize and MsiFileHash of PCSteward.Desktop.exe, PCSteward.ElevatedHelper.exe, PCSteward.ReleaseManifest.json')
        tables = $tables.Lines
    }
    [IO.File]::WriteAllText($target, ($reference | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
    [pscustomobject]@{ msiReference = $target; tablesSha256 = $tables.Sha256; productVersion = $productVersion } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }
}
