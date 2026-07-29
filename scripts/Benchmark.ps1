[CmdletBinding()]
param(
    [string]$OutputPath = "docs/review/raw/task-29-fixture-performance.json"
)

$ErrorActionPreference = 'Stop'
dotnet run --project tests/PCSteward.Performance/PCSteward.Performance.csproj --configuration Release --no-build -- --output $OutputPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
