[CmdletBinding()]
param([string] $CatalogPath = (Join-Path $PSScriptRoot '..\catalog\pcsteward-catalog.v1.json'))

# FR-CAT-07: strict offline validation of the personal catalog. The same rules run in the unit tests (CI).
$ErrorActionPreference = 'Stop'
$tool = Join-Path $PSScriptRoot '..\tools\PCSteward.CatalogTool\PCSteward.CatalogTool.csproj'
dotnet run --project $tool -c Release -- validate (Resolve-Path -LiteralPath $CatalogPath)
exit $LASTEXITCODE
