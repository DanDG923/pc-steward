# Task 29 — Performance and accessibility evidence

**Status:** Blocked — do not advance to Task 30

## Environment and artifacts

- Release build: zero warnings/errors on 2026-07-29.
- Host: Windows `10.0.26200.0`, .NET `10.0.10`, 16 logical processors. This host is not recorded as the agreed ADR-006 baseline.
- Raw fixture samples: [task-29-fixture-performance.json](raw/task-29-fixture-performance.json).
- Raw five-minute idle capture: [task-29-idle.json](raw/task-29-idle.json).
- A local 3.5-GB WPR ETL was captured with CPU, heap, XAML, desktop-composition, responsiveness and network profiles. It has 76,950,076 events and zero reported loss, but is ignored by Git because it contains system-wide metadata and requires WPA for stack analysis.

## Measured results

| Protocol | Requirement IDs | Result | Disposition |
|---|---|---:|---|
| 30 warmed, 300-record local fixture runs | `NFR-PERF-01`, `AC-NFR-01` | median 2.03 ms; p95 3.07 ms | Fixture pass only; not final-app baseline evidence. |
| 20 healthy fixture online checks | `NFR-PERF-02`, `AC-NFR-02` | median 15.50 ms; p95 15.98 ms | Fixture pass only; no live source contacted. |
| Five-minute Release-desktop idle | `NFR-PERF-05`, `AC-NFR-06` | CPU 4.9883%; peak working set 240.53 MiB; disk read/write 0 B | **Fail**: CPU exceeds 1%. Network is unverified because the non-elevated host has no process-attributed network counter. |
| Normal close / no resident process | `NFR-PERF-07`, `AC-NFR-08` | no window immediately after Alt+F4; no process after 30 seconds | Pass on this host. |
| Cancellation, independent timeout handling, second-scan rejection and cache active-artifact protection | `NFR-PERF-08–10`, `NFR-REL-01–03`, `AC-NFR-03/05/09–10` | 27 focused integration tests passed | Automated regression evidence; no new live-source action performed. |

## Accessibility and UI observation

The Release desktop was launched with Windows UI Automation. The accessibility tree exposed named primary navigation, connector controls, scan/source actions, filters, data controls, and the factory-reset control. Textual live/cached/local-only state and descriptive destructive-action scope were exposed. The app closed normally.

Keyboard traversal, Narrator narration, Windows High Contrast, and 100/125/150/175/200% text-scale matrices remain incomplete. They are `AC-UX-01–09` best-effort evidence under ADR-021, but may not be reported as passed. No destructive action, source refresh, schedule registration, or update was invoked during this review.

## Required next action

Task 29 remains blocked at the mandatory idle-resource gate. The process-attributed ETW capture now exists, but the host lacks Windows Performance Analyzer/WPA Exporter and the built-in reader cannot extract its stacks within the bounded run. Install the Microsoft Windows Performance Toolkit/WPA only with explicit approval, or analyse the ETL on a controlled WPA-equipped baseline; then identify the sustained CPU source, make only the evidenced correction, and rerun the full idle protocol plus affected performance, accessibility, reliability, and full-suite checks.
