[CmdletBinding()]
param()

# Security review H1 follow-up (owner decision 2026-11-06: Native AOT helper before G9-T1), feasibility evidence only.
# Compiles PCSteward.ElevatedHelper with Native AOT into a scratch folder, lists every IL trim/AOT warning, checks the
# output has no managed runtime or PCSteward assemblies, then starts the native image (never elevated, never connected to
# anything) with .NET runtime settings in its environment beside the same checks on a framework-dependent publish:
#   - DOTNET_GCName (a GC DLL the CoreCLR runtime would load) and DOTNET_EnableEventPipe/DOTNET_EventPipeOutputPath (a file
#     the CoreCLR runtime would write) must have no effect on the native image, which must reach its own environment
#     check and exit 20;
#   - with no settings the same well-formed protocol-1 arguments must get past that check and the argument and elevation
#     checks (hosted runners are elevated) and stop at the next one, exit 21 (no desktop beside the helper), so 20 above is
#     the environment check and nothing else; no pipe is ever opened;
#   - the framework-dependent control shows what the same settings do to today's helper.
# Writes only into one fixed new folder, artifacts\helper-native-aot under this checkout, and deletes nothing: an existing
# folder there stops the run (remove it by hand). Changes no repository file, certificate store, install or deployment. The
# owner-only candidate builder (hash-pinned) is not used or changed.
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$OutputRoot = Join-Path $repository 'artifacts\helper-native-aot'
if (Test-Path -LiteralPath $OutputRoot) { throw "The output folder already exists; remove it by hand first: $OutputRoot" }
New-Item -ItemType Directory -Path $OutputRoot -ErrorAction Stop | Out-Null
$project = Join-Path $repository 'src\PCSteward.ElevatedHelper\PCSteward.ElevatedHelper.csproj'
$aotOut = Join-Path $OutputRoot 'aot'
$fdOut = Join-Path $OutputRoot 'framework-dependent'
$refusedExitCode = 20
$noDesktopExitCode = 21
# Well-formed protocol-1 bootstrap arguments naming nothing that exists (pid 4 is the System process).
$helperArguments = @('--protocol', '1', '--desktop-pid', '4', '--pipe', ('pcsteward-elevated-' + ('0' * 32)), '--nonce', ('0' * 64))

function Invoke-Publish([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $OutputRoot "$Name.log"
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    # MSBuild ends each line with the project path in brackets; drop it so only the warning text is matched below.
    $warnings = @(Get-Content -LiteralPath $log | Where-Object { $_ -match 'warning IL\d{4}' } | ForEach-Object { ($_ -replace '\s*\[[^\]]*\.csproj[^\]]*\]\s*$', '').Trim() } | Sort-Object -Unique)
    [pscustomobject]@{ Name = $Name; ExitCode = $code; Warnings = $warnings }
}

# PublishAot on the command line is a global property, so it would also turn on the Roslyn AOT/trim analyzers in every
# referenced project (which flag all of their code, reachable or not, and fail under TreatWarningsAsErrors). Set in the
# helper project, as ADR-033 plans, it would not. The analyzers are switched off here to match that; the ILC compiler still
# reports every warning on code the helper actually reaches, which is what this check lists.
$aot = Invoke-Publish 'publish-aot' @('publish', $project, '--configuration', 'Release', '--runtime', 'win-x64',
    '-p:PublishAot=true', '-p:EnableAotAnalyzer=false', '-p:EnableTrimAnalyzer=false', '-p:EnableSingleFileAnalyzer=false', '-p:TrimmerSingleWarn=false', '-p:IlcTreatWarningsAsErrors=false', '-p:ILLinkTreatWarningsAsErrors=false', '-p:IlcGenerateMapFile=false', '--output', $aotOut, '--disable-build-servers')
$fd = Invoke-Publish 'publish-framework-dependent' @('publish', $project, '--configuration', 'Release', '--runtime', 'win-x64',
    '--self-contained', 'false', '-p:PublishAot=false', '--output', $fdOut, '--disable-build-servers')

# A child started with the given extra settings and every inherited .NET runtime name removed (the runner's own DOTNET_ROOT
# and similar would otherwise trip the helper's environment check in every run).
function Invoke-Helper([string]$Image, [hashtable]$Extra) {
    $info = [Diagnostics.ProcessStartInfo]::new($Image)
    foreach ($argument in $helperArguments) { $info.ArgumentList.Add($argument) }
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = $OutputRoot
    foreach ($name in @($info.Environment.Keys)) {
        if ($name -match '^(DOTNET_|COMPlus_|CORECLR_|COR_|APPDOMAIN_MANAGER_)') { [void]$info.Environment.Remove($name) }
    }
    foreach ($key in $Extra.Keys) { $info.Environment[$key] = [string]$Extra[$key] }
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(30000)) { $process.Kill(); $process.WaitForExit(); return [pscustomobject]@{ ExitCode = 'timeout'; StdErr = '' } }
    [pscustomobject]@{ ExitCode = $process.ExitCode; StdErr = ([string]$stderr.Result).Trim() }
}

$results = [ordered]@{}
$failures = [Collections.Generic.List[string]]::new()
$aotImage = Join-Path $aotOut 'PCSteward.ElevatedHelper.exe'
$fdImage = Join-Path $fdOut 'PCSteward.ElevatedHelper.exe'

if ($aot.ExitCode -ne 0 -or !(Test-Path -LiteralPath $aotImage)) { $failures.Add("native AOT publish failed (exit $($aot.ExitCode))") }
else {
    $files = @(Get-ChildItem -LiteralPath $aotOut -File -Recurse | ForEach-Object { $_.FullName.Substring($aotOut.Length + 1) } | Sort-Object)
    $results.aotFiles = $files
    $results.aotImageBytes = (Get-Item -LiteralPath $aotImage).Length
    foreach ($leaf in $files) {
        if ($leaf -match '^(coreclr|hostfxr|hostpolicy|clrjit)\.dll$' -or $leaf -match '\.(deps|runtimeconfig)\.json$' -or $leaf -match '^PCSteward\..*\.dll$') {
            $failures.Add("native output still holds a managed runtime or assembly file: $leaf")
        }
    }
    $ourWarnings = @($aot.Warnings | Where-Object { $_ -match 'PCSteward\.' })
    $results.aotWarningCount = $aot.Warnings.Count
    $results.aotWarningsInPcSteward = $ourWarnings
    $results.aotWarningsOther = @($aot.Warnings | Where-Object { $_ -notmatch 'PCSteward\.' })
    foreach ($warning in $ourWarnings) { $failures.Add("trim/AOT warning in PC Steward code: $warning") }

    $eventPipeFile = Join-Path $OutputRoot 'aot-eventpipe.nettrace'
    $results.aotNoSettings = Invoke-Helper $aotImage @{}
    $results.aotGcName = Invoke-Helper $aotImage @{ DOTNET_GCName = 'pcsteward-missing-gc.dll' }
    $results.aotEventPipe = Invoke-Helper $aotImage @{ DOTNET_EnableEventPipe = '1'; DOTNET_EventPipeOutputPath = $eventPipeFile }
    $results.aotEventPipeFileWritten = Test-Path -LiteralPath $eventPipeFile
    if ($results.aotGcName.ExitCode -ne $refusedExitCode) { $failures.Add("native image with DOTNET_GCName exited $($results.aotGcName.ExitCode), not $refusedExitCode") }
    if ($results.aotEventPipe.ExitCode -ne $refusedExitCode) { $failures.Add("native image with EventPipe settings exited $($results.aotEventPipe.ExitCode), not $refusedExitCode") }
    if ($results.aotEventPipeFileWritten) { $failures.Add('native image wrote the EventPipe output file') }
    if ($results.aotNoSettings.ExitCode -ne $noDesktopExitCode) { $failures.Add("native image with a clean environment exited $($results.aotNoSettings.ExitCode), not $noDesktopExitCode") }
}

if ($fd.ExitCode -eq 0 -and (Test-Path -LiteralPath $fdImage)) {
    # Control only (never fails the job): today's framework-dependent helper under the same settings.
    $fdEventPipeFile = Join-Path $OutputRoot 'framework-dependent-eventpipe.nettrace'
    $results.controlNoSettings = Invoke-Helper $fdImage @{}
    $results.controlGcName = Invoke-Helper $fdImage @{ DOTNET_GCName = 'pcsteward-missing-gc.dll' }
    $results.controlEventPipe = Invoke-Helper $fdImage @{ DOTNET_EnableEventPipe = '1'; DOTNET_EventPipeOutputPath = $fdEventPipeFile }
    $results.controlEventPipeFileWritten = Test-Path -LiteralPath $fdEventPipeFile
}

$results.failures = @($failures)
$summary = Join-Path $OutputRoot 'helper-native-aot-summary.json'
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summary -Encoding utf8
Get-Content -LiteralPath $summary | Out-Host
if ($failures.Count -gt 0) { throw "Native AOT helper feasibility check failed: $($failures -join '; ')" }
'Native AOT helper feasibility check passed.'
