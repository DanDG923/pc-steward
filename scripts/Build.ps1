[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
dotnet build PCSteward.slnx --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
