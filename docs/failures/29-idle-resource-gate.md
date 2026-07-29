# Task 29 — idle resource gate blocked

## Requirement IDs

`NFR-PERF-05`, `AC-NFR-06`

## First attempt — five-minute direct process capture

Command:

```powershell
pwsh -NoProfile -File .\scripts\Measure-Task29Idle.ps1 -ProcessId <Release desktop PID> -DurationSeconds 300 -IntervalSeconds 5 -OutputPath .\docs\review\raw\task-29-idle.json
```

Result: completed on 2026-07-29 against the Release desktop process. The raw capture records average CPU of `4.9883%`, exceeding the `<= 1%` requirement. Peak working set was `252,211,200` bytes (within the 300 MB budget) and process-attributed read/write I/O was zero. Per-process network activity is not available to this non-elevated harness and is deliberately `unverified`.

## Second attempt — Windows performance-counter corroboration

Command:

```powershell
$counterName = (Get-Counter -ListSet 'Process').Paths | Where-Object { $_ -match '\\% Processor Time$' -and $_ -match 'PCSteward.Desktop' } | Select-Object -First 1
```

Exact error:

```text
No PCSteward.Desktop process performance counter path was found.
```

## Smallest suspected cause

The desktop starts at the large Overview tree, which constructs a complete navigation shell plus inventory/list and data/reset presentation. The source contains no intentional idle timer or recurring asynchronous work, so the measured sustained CPU cannot yet be attributed to a safe, evidenced code change. Do not relax the idle budget or treat the first measurement as a pass. Capture an ETW/WPR profile or a host with process-attributed counters before modifying rendering/lifetime code.

## Scope preserved

No provider, matching, execution, deletion, scheduling, or reset behavior was changed. Both Release instances were closed normally; `Get-Process` found no `PCSteward.Desktop` process 30 seconds after normal close.
