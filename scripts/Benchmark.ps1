[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
dotnet run --project tests/PCSteward.Performance/PCSteward.Performance.csproj --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
