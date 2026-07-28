[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
dotnet restore PCSteward.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
