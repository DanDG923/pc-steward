$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\WingetSpike.psm1') -Force

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

$version = ConvertTo-WingetVersion 'v1.29.280'
Assert-True ($version -eq [version]'1.29.280') 'Numeric WinGet version must parse.'

$help = @'
  -s,--source                     Find package using the specified source
  -e,--exact                      Find package using exact match
  -a,--architecture               Select the architecture
  --scope                         Select installed package scope filter (user or machine)
  --disable-interactivity         Disable interactive prompts
'@
$capabilities = Get-WingetHelpCapabilities $help
Assert-True $capabilities.SupportsDisableInteractivity 'Expected non-interactive capability.'
Assert-True $capabilities.SupportsSourceSelection 'Expected source selection capability.'
Assert-True $capabilities.SupportsExactId 'Expected exact ID capability.'
Assert-True $capabilities.SupportsArchitecture 'Expected architecture capability.'
Assert-True $capabilities.SupportsScope 'Expected scope capability.'
Assert-True (-not $capabilities.SupportsStructuredOutput) 'Structured output must not be inferred when absent from help.'

$plan = New-WingetUpdatePlan -PackageId 'Contoso.App' -Source 'winget' -Version '1.2.3' -Architecture x64 -Scope user
Assert-True ($plan.FileName -eq 'winget.exe') 'Only the fixed WinGet executable is allowed.'
Assert-True ($plan.PSObject.Properties.Name -notcontains 'CommandLine') 'Plans must not expose a shell command string.'
Assert-True (($plan.Arguments -join '|') -eq 'upgrade|--id|Contoso.App|--exact|--source|winget|--version|1.2.3|--disable-interactivity|--architecture|x64|--scope|user') 'Arguments must be fixed and ordered.'

$rejected = $false
try { New-WingetUpdatePlan -PackageId 'Contoso.App; whoami' -Source winget -Version 1.2.3 | Out-Null } catch { $rejected = $true }
Assert-True $rejected 'Unsafe package identifiers must be rejected before process construction.'

Write-Output 'PASS: 11 assertions'
