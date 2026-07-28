[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
dotnet test PCSteward.slnx --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
