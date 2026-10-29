[CmdletBinding()]
param([switch]$StandardUser)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$repository = Split-Path -Parent $PSScriptRoot

if ($StandardUser) {
    # TM-04/TM-06: privileged compilation guards stay enabled in production scripts.
    if ($isAdministrator) { throw 'CI fixtures require a standard-user token.' }
    Set-Location -LiteralPath $repository
    $env:TEMP = Join-Path $repository 'artifacts\ci-test-temp'
    $env:TMP = $env:TEMP
    $env:DOTNET_CLI_HOME = Join-Path $repository 'artifacts\ci-dotnet-home'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    & (Join-Path $PSScriptRoot 'Test.ps1')
    exit $LASTEXITCODE
}

# Account creation is confined to disposable GitHub-hosted Windows runners.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$isAdministrator) {
    throw 'Use Test.ps1 locally; this launcher requires an administrator on a GitHub-hosted runner.'
}

$name = 'pcst' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$password = ConvertTo-SecureString ('Aa1!' + [Guid]::NewGuid().ToString('N')) -AsPlainText -Force
$account = $null
$process = $null
$exitCode = 1
try {
    $account = New-LocalUser -Name $name -Password $password -Description 'Disposable PC Steward CI test user'
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
    # Only the disposable checkout is writable; installed programs remain protected.
    & icacls.exe $repository /grant "*$($account.SID.Value):(OI)(CI)M" /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the CI user access to the checkout.' }
    $logs = Join-Path $repository 'artifacts\ci-test-logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    $stdout = Join-Path $logs 'stdout.log'
    $stderr = Join-Path $logs 'stderr.log'
    $credential = [PSCredential]::new("$env:COMPUTERNAME\$name", $password)
    $arguments = '-NoProfile -File "' + (Join-Path $PSScriptRoot 'Test-CI.ps1') + '" -StandardUser'
    $process = Start-Process -FilePath (Get-Command pwsh.exe).Source -ArgumentList $arguments -Credential $credential -LoadUserProfile -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    # Retain the handle before waiting so ExitCode remains available after exit.
    $null = $process.Handle
    if (!$process.WaitForExit(600000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'CI tests exceeded ten minutes.'
    }
    $exitCode = $process.ExitCode
}
finally {
    if ($process) { $process.Dispose() }
    foreach ($log in @($stdout, $stderr)) {
        if ($log -and (Test-Path -LiteralPath $log)) { Get-Content -LiteralPath $log }
    }
    if ($account) { Remove-LocalUser -SID $account.SID }
    $password.Dispose()
}
exit $exitCode
