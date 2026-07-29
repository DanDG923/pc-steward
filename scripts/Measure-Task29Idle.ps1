[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [int]$ProcessId,
    [int]$DurationSeconds = 300,
    [int]$IntervalSeconds = 5,
    [string]$OutputPath = "docs/review/raw/task-29-idle.json"
)

$ErrorActionPreference = 'Stop'
if ($DurationSeconds -lt $IntervalSeconds) { throw "DurationSeconds must be at least IntervalSeconds." }

$samples = [System.Collections.Generic.List[object]]::new()
$logicalProcessors = [Environment]::ProcessorCount
$previous = Get-Process -Id $ProcessId
$previousCpu = $previous.TotalProcessorTime.TotalMilliseconds
$previousRead = $previous.IOReadBytes
$previousWrite = $previous.IOWriteBytes
$startedAt = [DateTimeOffset]::UtcNow

for ($elapsed = $IntervalSeconds; $elapsed -le $DurationSeconds; $elapsed += $IntervalSeconds) {
    Start-Sleep -Seconds $IntervalSeconds
    $current = Get-Process -Id $ProcessId -ErrorAction Stop
    $cpu = $current.TotalProcessorTime.TotalMilliseconds
    $read = $current.IOReadBytes
    $write = $current.IOWriteBytes
    $samples.Add([pscustomobject]@{
        elapsedSeconds = $elapsed
        cpuPercent = [Math]::Round((($cpu - $previousCpu) / ($IntervalSeconds * 1000 * $logicalProcessors)) * 100, 4)
        workingSetBytes = $current.WorkingSet64
        readBytes = $read - $previousRead
        writeBytes = $write - $previousWrite
    })
    $previousCpu = $cpu
    $previousRead = $read
    $previousWrite = $write
}

$result = [pscustomobject]@{
    capturedAtUtc = [DateTimeOffset]::UtcNow
    startedAtUtc = $startedAt
    processId = $ProcessId
    processName = $previous.ProcessName
    durationSeconds = $DurationSeconds
    intervalSeconds = $IntervalSeconds
    logicalProcessorCount = $logicalProcessors
    averageCpuPercent = [Math]::Round((($samples | Measure-Object cpuPercent -Average).Average), 4)
    peakWorkingSetBytes = ($samples | Measure-Object workingSetBytes -Maximum).Maximum
    totalReadBytes = (($samples | Measure-Object readBytes -Sum).Sum)
    totalWriteBytes = (($samples | Measure-Object writeBytes -Sum).Sum)
    network = [pscustomobject]@{ status = "unverified"; reason = "Process-attributed network counters are unavailable to this non-elevated harness." }
    samples = $samples
}

$absoluteOutputPath = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($absoluteOutputPath)) | Out-Null
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $absoluteOutputPath -Encoding utf8
$result | ConvertTo-Json -Depth 3
