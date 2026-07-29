# Task 29 — Performance and accessibility evidence

**Status:** Blocked — do not advance to Task 30

## Environment and artifacts

- Release build: zero warnings/errors on 2026-07-29.
- Host: Windows `10.0.26200.0`, .NET `10.0.10`, 16 logical processors. This host is not recorded as the agreed ADR-006 baseline.
- Raw fixture samples: [task-29-fixture-performance.json](raw/task-29-fixture-performance.json).
- Raw five-minute idle capture: [task-29-idle.json](raw/task-29-idle.json).
- A local 3.5-GB WPR ETL was captured with CPU, heap, XAML, desktop-composition, responsiveness and network profiles. It has 76,950,076 events and zero reported loss and is ignored by Git because it contains system-wide metadata. The approved Microsoft WPT/WPA installation was used to analyze it and then removed.

## Measured results

| Protocol | Requirement IDs | Result | Disposition |
|---|---|---:|---|
| 30 warmed, 300-record local fixture runs | `NFR-PERF-01`, `AC-NFR-01` | median 2.03 ms; p95 3.07 ms | Fixture pass only; not final-app baseline evidence. |
| 20 healthy fixture online checks | `NFR-PERF-02`, `AC-NFR-02` | median 15.50 ms; p95 15.98 ms | Fixture pass only; no live source contacted. |
| Five-minute Release-desktop idle | `NFR-PERF-05`, `AC-NFR-06` | CPU 4.9883%; peak working set 240.53 MiB; disk read/write 0 B | **Fail**: CPU exceeds 1%. Network is unverified because the non-elevated host has no process-attributed network counter. |
| Five-minute Release-desktop, lightweight Scan page | `NFR-PERF-05`, `AC-NFR-06` | CPU 1.8392%; peak working set 227.64 MiB; disk read/write 0 B | **Fail**, but 3.1491 percentage points lower than Overview. The page has no inventory ListView or data/reset composition; the shared shell still exceeds budget. |
| Loss-free ETW, PCSteward idle interval | `NFR-PERF-05`, `AC-NFR-06` | 72.734516 CPU-seconds in 85.525729 seconds; window thread 36952: 64.089183 CPU-seconds; `Microsoft.UI.Xaml.dll`: 2.55% sampled total CPU | Sustained UI/layout/composition workload is process-attributed. No sampled stacks or heap/virtual-allocation events are present, so source-level cause remains unproven. |
| Normal close / no resident process | `NFR-PERF-07`, `AC-NFR-08` | no window immediately after Alt+F4; no process after 30 seconds | Pass on this host. |
| Cancellation, independent timeout handling, second-scan rejection and cache active-artifact protection | `NFR-PERF-08–10`, `NFR-REL-01–03`, `AC-NFR-03/05/09–10` | 27 focused integration tests passed | Automated regression evidence; no new live-source action performed. |

## Accessibility and UI observation

The Release desktop was launched with Windows UI Automation. The accessibility tree exposed named primary navigation, connector controls, scan/source actions, filters, data controls, and the factory-reset control. Textual live/cached/local-only state and descriptive destructive-action scope were exposed. The app closed normally.

Keyboard traversal, Narrator narration, Windows High Contrast, and 100/125/150/175/200% text-scale matrices remain incomplete. They are `AC-UX-01–09` best-effort evidence under ADR-021, but may not be reported as passed. No destructive action, source refresh, schedule registration, or update was invoked during this review.

## Required next action

Task 29 remains blocked at the mandatory idle-resource gate. The approved WPA analysis establishes that sustained UI work is process-attributed and that the large Overview adds most of the observed cost, but the capture has no usable call stacks or allocation events. Do not guess at a ListView/navigation rewrite. Capture `Profile` stacks on the recorded ADR-006 baseline with its fixed `3XS-Overclock` power plan and process-attributed network evidence, identify the window-thread call path, make only that evidenced correction, and rerun the full idle protocol plus affected performance, accessibility, reliability, close/process, and full-suite checks.
