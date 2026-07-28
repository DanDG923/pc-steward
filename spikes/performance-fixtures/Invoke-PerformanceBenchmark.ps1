[CmdletBinding()]
param(
    [ValidateSet('local', 'online', 'all')][string]$Scenario = 'all',
    [ValidateRange(1, 100)][int]$LocalRuns = 30,
    [ValidateRange(1, 100)][int]$OnlineRuns = 20,
    [ValidateRange(1, 8)][int]$MaxConnectorConcurrency = 3,
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot 'results')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$fixturePath = Join-Path $PSScriptRoot 'fixtures\provider-records.json'
$workerPath = Join-Path $PSScriptRoot 'BenchmarkWorker.ps1'
if (-not (Test-Path -LiteralPath $fixturePath -PathType Leaf)) { throw 'Generate fixtures before benchmarking.' }
if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) { New-Item -ItemType Directory -Path $ResultsDirectory -Force | Out-Null }

$powerShell = (Get-Command pwsh -ErrorAction Stop).Source
function Start-Worker([string]$Mode, [string]$Connector, [int]$DelayMilliseconds, [string]$CancelFile, [string]$RawPath) {
    $arguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $workerPath, '-FixturePath', $fixturePath, '-Mode', $Mode, '-Connector', $Connector, '-DelayMilliseconds', $DelayMilliseconds)
    if ($CancelFile) { $arguments += @('-CancelFile', $CancelFile) }
    $argumentLine = ($arguments | ForEach-Object { '"' + ([string]$_).Replace('"', '\"') + '"' }) -join ' '
    return [pscustomobject]@{
        Process = Start-Process -FilePath $powerShell -ArgumentList $argumentLine -PassThru -RedirectStandardOutput $RawPath -RedirectStandardError ($RawPath + '.stderr.txt')
        StartedUtc = [datetime]::UtcNow
        Stopwatch = [Diagnostics.Stopwatch]::StartNew()
        RawPath = $RawPath
        Connector = $Connector
        PeakWorkingSetBytes = [int64]0
    }
}

function Complete-Worker($worker) {
    while (-not $worker.Process.HasExited) {
        $worker.Process.Refresh()
        $worker.PeakWorkingSetBytes = [Math]::Max($worker.PeakWorkingSetBytes, $worker.Process.WorkingSet64)
        Start-Sleep -Milliseconds 5
    }
    $worker.Process.WaitForExit()
    $worker.Stopwatch.Stop()
    $worker.Process.Refresh()
    $payload = Get-Content -Raw -LiteralPath $worker.RawPath | ConvertFrom-Json
    $elapsedMs = $worker.Stopwatch.Elapsed.TotalMilliseconds
    $cpuMs = [math]::Round($worker.Process.TotalProcessorTime.TotalMilliseconds, 3)
    return [pscustomobject]@{
        connector = $worker.Connector
        elapsedMs = $elapsedMs
        cpuMs = $cpuMs
        cpuPercentOfOneCore = [math]::Round(($cpuMs / $elapsedMs) * 100, 3)
        cpuPercentOfLogicalProcessors = [math]::Round(($cpuMs / ($elapsedMs * [Environment]::ProcessorCount)) * 100, 3)
        peakWorkingSetBytes = $worker.PeakWorkingSetBytes
        exitCode = $worker.Process.ExitCode
        status = $payload.status
        recordsProcessed = $payload.recordsProcessed
        rawOutput = [IO.Path]::GetFileName($worker.RawPath)
    }
}

function Get-Summary([object[]]$samples) {
    $elapsed = @($samples | ForEach-Object { [double]$_.elapsedMs } | Sort-Object)
    $cpu = @($samples | ForEach-Object { [double]$_.cpuMs } | Sort-Object)
    $workingSet = @($samples | ForEach-Object { [double]$_.peakWorkingSetBytes } | Sort-Object)
    function Percentile([double[]]$values, [double]$fraction) { $values[[Math]::Ceiling($values.Count * $fraction) - 1] }
    return [ordered]@{
        sampleCount = $samples.Count
        elapsedMs = @{ median = $elapsed[[int][math]::Floor(($elapsed.Count - 1) / 2)]; p95 = Percentile $elapsed 0.95 }
        cpuMs = @{ median = $cpu[[int][math]::Floor(($cpu.Count - 1) / 2)]; p95 = Percentile $cpu 0.95 }
        peakWorkingSetBytes = @{ median = $workingSet[[int][math]::Floor(($workingSet.Count - 1) / 2)]; p95 = Percentile $workingSet 0.95 }
    }
}

$stamp = [datetime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$localSamples = [System.Collections.Generic.List[object]]::new()
if ($Scenario -in @('local', 'all')) {
    for ($run = 1; $run -le $LocalRuns; $run++) {
        $worker = Start-Worker 'local' 'local' 0 $null (Join-Path $ResultsDirectory ("{0}-local-{1:D2}.json" -f $stamp, $run))
        $sample = Complete-Worker $worker
        $sample | Add-Member NoteProperty run $run
        $localSamples.Add($sample)
    }
}

$onlineSamples = [System.Collections.Generic.List[object]]::new()
$observedConcurrency = 0
if ($Scenario -in @('online', 'all')) {
    for ($run = 1; $run -le $OnlineRuns; $run++) {
        $connectors = @('catalog-a', 'catalog-b', 'catalog-c', 'catalog-d', 'catalog-e')
        $active = [System.Collections.Generic.List[object]]::new()
        $completed = [System.Collections.Generic.List[object]]::new()
        $next = 0
        $runWatch = [Diagnostics.Stopwatch]::StartNew()
        $runPeakWs = [int64]0
        while ($next -lt $connectors.Count -or $active.Count -gt 0) {
            while ($next -lt $connectors.Count -and $active.Count -lt $MaxConnectorConcurrency) {
                $active.Add((Start-Worker 'online' $connectors[$next] 60 $null (Join-Path $ResultsDirectory ("{0}-online-{1:D2}-{2}.json" -f $stamp, $run, $connectors[$next]))))
                $next++
            }
            $observedConcurrency = [Math]::Max($observedConcurrency, $active.Count)
            $currentWorkingSet = [int64]0
            foreach ($worker in @($active)) {
                $worker.Process.Refresh()
                if ($worker.Process.HasExited) {
                    $completed.Add((Complete-Worker $worker))
                    $active.Remove($worker) | Out-Null
                }
                else {
                    $worker.PeakWorkingSetBytes = [Math]::Max($worker.PeakWorkingSetBytes, $worker.Process.WorkingSet64)
                    $currentWorkingSet += $worker.Process.WorkingSet64
                }
            }
            $runPeakWs = [Math]::Max($runPeakWs, $currentWorkingSet)
            Start-Sleep -Milliseconds 5
        }
        $runWatch.Stop()
        $runCpu = ($completed | Measure-Object -Property cpuMs -Sum).Sum
        $onlineStatus = if (@($completed | Where-Object status -ne 'completed').Count) { 'failed' } else { 'completed' }
        $onlineElapsedMs = $runWatch.Elapsed.TotalMilliseconds
        $onlineSamples.Add([pscustomobject]@{ run = $run; elapsedMs = $onlineElapsedMs; cpuMs = [math]::Round($runCpu, 3); cpuPercentOfOneCore = [math]::Round(($runCpu / $onlineElapsedMs) * 100, 3); cpuPercentOfLogicalProcessors = [math]::Round(($runCpu / ($onlineElapsedMs * [Environment]::ProcessorCount)) * 100, 3); peakWorkingSetBytes = $runPeakWs; status = $onlineStatus; connectorSamples = $completed })
    }
}

$cancelRaw = Join-Path $ResultsDirectory ("{0}-cancellation.json" -f $stamp)
$cancelFile = Join-Path $ResultsDirectory ("{0}-cancel.request" -f $stamp)
$cancelWorker = Start-Worker 'cancellable' 'cancellation-probe' 10000 $cancelFile $cancelRaw
Start-Sleep -Milliseconds 120
$cancelWatch = [Diagnostics.Stopwatch]::StartNew()
New-Item -ItemType File -Path $cancelFile -Force | Out-Null
$cancelSample = Complete-Worker $cancelWorker
$cancelWatch.Stop()

$result = [ordered]@{
    schemaVersion = 1
    generatedUtc = [datetime]::UtcNow.ToString('o')
    harnessVersion = 'Task-04-spike-1'
    fixtureRecordCount = @((Get-Content -Raw -LiteralPath $fixturePath | ConvertFrom-Json).records).Count
    parameters = @{ localRuns = $LocalRuns; onlineRuns = $OnlineRuns; maxConnectorConcurrency = $MaxConnectorConcurrency }
    local = @{ samples = $localSamples; summary = if ($localSamples.Count) { Get-Summary $localSamples } else { $null } }
    online = @{ samples = $onlineSamples; summary = if ($onlineSamples.Count) { Get-Summary $onlineSamples } else { $null }; observedMaxConcurrency = $observedConcurrency }
    cancellation = @{ acknowledgementMs = $cancelWatch.Elapsed.TotalMilliseconds; workerStatus = $cancelSample.status; exitCode = $cancelSample.exitCode; rawOutput = $cancelSample.rawOutput }
    limitations = @('This measures the disposable PowerShell spike, not the future UI or application.', 'Online checks are deterministic local connector simulations; no network or package source is contacted.', 'Direct worker processes are the measured process tree; no unrelated process is included.')
}
$resultPath = Join-Path $ResultsDirectory ("benchmark-{0}.json" -f $stamp)
$result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
Write-Output $resultPath
