[CmdletBinding()]
param([switch]$StandardUser, [string]$WorkRoot)

# Task 50 G3 Windows CI evidence: run scripts/New-OwnerOnlyCandidate.ps1 end to end under a disposable standard user
# (the builder refuses an elevated token, and hosted runners are elevated), then re-validate the written candidate in a
# fresh Windows PowerShell 5.1 process from the bytes on disk. No signing, certificate store, install or launch.
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$repository = Split-Path -Parent $PSScriptRoot
$builder = Join-Path $PSScriptRoot 'New-OwnerOnlyCandidate.ps1'
$windowsPowerShell = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::System)) 'WindowsPowerShell\v1.0\powershell.exe'

if ($StandardUser) {
    if ($isAdministrator) { throw 'The candidate builder CI run requires a standard-user token.' }
    if (!$WorkRoot -or !(Test-Path -LiteralPath $WorkRoot -PathType Container)) { throw 'A prepared WorkRoot is required.' }
    Set-Location -LiteralPath $repository
    $env:TEMP = Join-Path $WorkRoot 'temp'
    $env:TMP = $env:TEMP
    $env:DOTNET_CLI_HOME = Join-Path $WorkRoot 'dotnet-home'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    # Process-scoped, command-level Git configuration for this disposable user only (no Git config file is changed):
    # the checkout belongs to the runner account, so Git's ownership check would refuse rev-parse/status.
    $env:GIT_CONFIG_COUNT = '1'
    $env:GIT_CONFIG_KEY_0 = 'safe.directory'
    $env:GIT_CONFIG_VALUE_0 = '*'
    New-Item -ItemType Directory -Path $env:TEMP, $env:DOTNET_CLI_HOME -Force | Out-Null
    $candidate = Join-Path $WorkRoot 'candidate'

    & $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', $builder, '-OutputRoot', $candidate)
    if ($LASTEXITCODE -ne 0) { throw "The candidate builder failed with exit code $LASTEXITCODE." }
    $summaryPath = Join-Path $candidate 'candidate-build.json'
    $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json

    # Independent re-validation from disk, in a new Windows PowerShell 5.1 process (the installer runtime).
    $validationOutput = @(& $windowsPowerShell @('-NoProfile', '-NonInteractive', '-File', $builder, '-ValidateCandidateRoot', $candidate, '-ExpectedManifestSha256', [string]$summary.manifestSha256))
    if ($LASTEXITCODE -ne 0) { throw "Independent candidate re-validation failed with exit code $LASTEXITCODE." }
    $validation = ConvertFrom-Json -InputObject ([string]($validationOutput | Where-Object { $_ } | Select-Object -Last 1))
    $manifestBytes = [IO.File]::ReadAllBytes((Join-Path $candidate 'candidate-manifest.json'))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $manifestHash = [BitConverter]::ToString($sha.ComputeHash($manifestBytes)).Replace('-', '') } finally { $sha.Dispose() }
    $failures = [Collections.Generic.List[string]]::new()
    if ($validation.validation -cne 'Pass') { $failures.Add('validation') }
    if ($manifestHash -cne $summary.manifestSha256 -or $validation.manifestSha256 -cne $summary.manifestSha256) { $failures.Add('manifest hash') }
    if ($manifestBytes.Length -ge 3 -and $manifestBytes[0] -eq 0xEF -and $manifestBytes[1] -eq 0xBB -and $manifestBytes[2] -eq 0xBF) { $failures.Add('manifest BOM') }
    if ($summary.payloadOrigin -cne 'pinned-unsigned-v2' -or $validation.payloadOrigin -cne 'pinned-unsigned-v2') { $failures.Add('payload origin') }
    if ([string]$summary.helperProtocol -cne '2' -or [string]$validation.helperProtocol -cne '2') { $failures.Add('helper protocol') }
    if ($validation.fileCount -ne $summary.fileCount -or $validation.totalBytes -ne $summary.totalBytes) { $failures.Add('file count/bytes') }
    if ($summary.sourceDirty -or $summary.allowDirty -or $summary.signed -or $summary.deterministicBuild -or $summary.selfContained) { $failures.Add('summary flags') }
    if ($summary.sourceCommit -notmatch '\A[0-9a-f]{40}\z') { $failures.Add('source commit') }
    if ($failures.Count -gt 0) { throw "Candidate builder CI evidence failed: $($failures -join ', ')" }

    # Small JSON evidence only; the payload itself is not uploaded.
    $evidence = Join-Path $repository 'artifacts\candidate-builder-evidence'
    New-Item -ItemType Directory -Path $evidence -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $candidate 'candidate-manifest.json'), $summaryPath -Destination $evidence
    [IO.File]::WriteAllText((Join-Path $evidence 'candidate-revalidation.json'), ($validationOutput | Where-Object { $_ } | Select-Object -Last 1), [Text.UTF8Encoding]::new($false))
    Write-Output "PASS: owner-only candidate built and re-validated: $($summary.fileCount) files, $($summary.totalBytes) bytes, manifest $($summary.manifestSha256), commit $($summary.sourceCommit), SDK $($summary.sdkVersion)"
    exit 0
}

# Account creation is confined to disposable GitHub-hosted Windows runners.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or !$isAdministrator) {
    throw 'Run New-OwnerOnlyCandidate.ps1 directly on a developer machine; this launcher requires an administrator on a GitHub-hosted runner.'
}
if (!$env:RUNNER_TEMP) { throw 'RUNNER_TEMP is required.' }

$name = 'pcsc' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$password = ConvertTo-SecureString ('Aa1!' + [Guid]::NewGuid().ToString('N')) -AsPlainText -Force
$account = $null
$process = $null
$exitCode = 1
$work = Join-Path $env:RUNNER_TEMP ('pcsteward-candidate-' + [Guid]::NewGuid().ToString('N'))
try {
    $account = New-LocalUser -Name $name -Password $password -Description 'Disposable PC Steward candidate-builder CI user'
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $account
    if ($env:NUGET_PACKAGES) { New-Item -ItemType Directory -Path $env:NUGET_PACKAGES -Force | Out-Null }
    # Only the disposable checkout and work root are writable; installed programs remain protected.
    & icacls.exe $repository /grant "*$($account.SID.Value):(OI)(CI)M" /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the CI user access to the checkout.' }
    New-Item -ItemType Directory -Path $work | Out-Null
    & icacls.exe $work /grant "*$($account.SID.Value):(OI)(CI)M" /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not grant the CI user access to the work root.' }
    $logs = Join-Path $repository 'artifacts\candidate-builder-logs'
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    $stdout = Join-Path $logs 'stdout.log'
    $stderr = Join-Path $logs 'stderr.log'
    $credential = [PSCredential]::new("$env:COMPUTERNAME\$name", $password)
    $arguments = '-NoProfile -File "' + (Join-Path $PSScriptRoot 'Test-OwnerOnlyCandidateBuilderCI.ps1') + '" -StandardUser -WorkRoot "' + $work + '"'
    $process = Start-Process -FilePath (Get-Command pwsh.exe).Source -ArgumentList $arguments -Credential $credential -LoadUserProfile -WorkingDirectory $repository -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    # Retain the handle before waiting so ExitCode remains available after exit.
    $null = $process.Handle
    if (!$process.WaitForExit(2400000)) {
        $process.Kill($true)
        $process.WaitForExit()
        throw 'The candidate builder CI run exceeded forty minutes.'
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
