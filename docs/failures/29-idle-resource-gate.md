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

## ETW follow-up — capture and approved WPA analysis

After a fresh interactive logon supplied `SeSystemProfilePrivilege`, the elevated WPR CPU, heap, XAML activity, desktop-composition, XAML-responsiveness and network profiles captured `docs/review/raw/task-29-idle.etl`. `tracerpt` reports 76,950,076 events, 3,364 buffers, zero lost events and a 603-second trace span. The intentional PC Steward idle window is one minute within that span.

The raw ETL is deliberately ignored by Git: it is 3.5 GB and contains system-wide metadata. With explicit approval, the official Microsoft Windows Performance Toolkit was installed, signature-verified, and used through `xperf`. It was removed immediately after the analysis; `wpa.exe` no longer exists under the Windows Performance Toolkit installation root.

The ETL identifies `PCSteward.Desktop.exe` PID `21580` from `501.168100` to `586.693829` seconds. Context-switch attribution records `72.734516` CPU-seconds in that `85.525729`-second window; its window thread (`36952`) consumed `64.089183` CPU-seconds (88.1%). CPU samples attribute 2.55% total-system usage to `Microsoft.UI.Xaml.dll`, versus 1.04% to `coreclr.dll` and 0.13% to `UIAutomationCore.dll`. The capture carries no usable sampled-stack records and no heap/virtual-allocation events for this PID, so it cannot connect the UI work to a particular application method or prove an allocation loop.

The controlled page isolation corroborates that finding without changing code: the complete Overview averaged `4.9883%` CPU for five minutes, while the lightweight Scan page (same process, same shared NavigationView shell, no job started) averaged `1.8392%`. The Overview therefore contributes about `3.1491` percentage points, but the shared WinUI shell still exceeds the `<=1%` budget. This is evidence of sustained UI/layout/composition work, not an app-owned background timer or recurring disk I/O. It does not establish a safe source-level remediation; do not remove or rewrite the ListView, navigation, or Overview controls on this evidence alone.

The smallest remaining unblocker is a source-level stack capture that explicitly includes `Profile` stackwalks, taken on the recorded ADR-006 baseline with its fixed power plan and process-attributed network collection. Use that capture to identify the UI-thread call path, then make only the evidenced change and repeat the full five-minute gate.

## Scope preserved

No provider, matching, execution, deletion, scheduling, or reset behavior was changed. Both Release instances were closed normally; `Get-Process` found no `PCSteward.Desktop` process 30 seconds after normal close.
