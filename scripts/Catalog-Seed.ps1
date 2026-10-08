[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $OutputPath,
    [string] $DatabasePath = (Join-Path $env:LOCALAPPDATA 'PCSteward\pcsteward.db')
)

# FR-CAT-07: owner-run and READ-ONLY. Proposes catalog entries from exact local WinGet (winget source) and
# Chocolatey matches in the PC Steward database. The draft is never loaded automatically; review it and
# merge the entries you want into catalog\pcsteward-catalog.v1.json, then run Catalog-Validate.ps1.
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $DatabasePath)) { throw "PC Steward database not found: $DatabasePath" }
$tool = Join-Path $PSScriptRoot '..\tools\PCSteward.CatalogTool\PCSteward.CatalogTool.csproj'
dotnet run --project $tool -c Release -- seed $DatabasePath $OutputPath
exit $LASTEXITCODE
