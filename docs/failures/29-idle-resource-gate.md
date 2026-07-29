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

## ETW follow-up — captured, but analysis tooling absent

After a fresh interactive logon supplied `SeSystemProfilePrivilege`, the elevated WPR CPU, heap, XAML activity, desktop-composition, XAML-responsiveness and network profiles captured `docs/review/raw/task-29-idle.etl`. `tracerpt` reports 76,950,076 events, 3,364 buffers, zero lost events and a 603-second trace span. The intentional PC Steward idle window is one minute within that span.

The raw ETL is deliberately ignored by Git: it is 3.5 GB and contains system-wide metadata. The host has WPR but no Windows Performance Analyzer (WPA), WPA Exporter, Xperf or another stack-capable ETL analyzer. `tracerpt` can produce only aggregate provider counts; attempting to filter the ETL's kernel process events using `Get-WinEvent` timed out after 120 seconds. No source change is justified from the aggregate trace.

The smallest remaining unblocker is a local installation of the Microsoft Windows Performance Toolkit/WPA, or analysis of the ETL on a separate controlled machine with WPA. Do not install it without explicit approval.

## Scope preserved

No provider, matching, execution, deletion, scheduling, or reset behavior was changed. Both Release instances were closed normally; `Get-Process` found no `PCSteward.Desktop` process 30 seconds after normal close.
