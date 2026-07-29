# Task 29 — Performance and accessibility evidence

**Status:** Deferred by approved exception `PO-EXC-29-PERF-01` — do not claim the idle-resource requirement as passed

## Environment and artifacts

- Release build: zero warnings/errors on 2026-07-29.
- Host: Windows `10.0.26200.0`, .NET `10.0.10`, 16 logical processors. This host is not recorded as the agreed ADR-006 baseline.
- Raw fixture samples: [task-29-fixture-performance.json](raw/task-29-fixture-performance.json).
- Raw five-minute idle capture: [task-29-idle.json](raw/task-29-idle.json).
- A local 3.5-GB WPR ETL was captured with CPU, heap, XAML, desktop-composition, responsiveness and network profiles. It has 76,950,076 events and zero reported loss and is ignored by Git because it contains system-wide metadata. The approved Microsoft WPT/WPA installation was used to analyze it and then removed.
- A second local ETL compared the existing Scan, Activity, and Overview surfaces with CPU, heap, XAML, desktop-composition, responsiveness, and network profiles. It spanned 476.839 seconds, had 310,203,684 events and zero reported loss, and was deleted after analysis at the product owner's direction because its system-wide verbose heap collection expanded it to 50.6 GB. The temporary WPT installation was also removed.

## Measured results

| Protocol | Requirement IDs | Result | Disposition |
|---|---|---:|---|
| 30 warmed, 300-record local fixture runs | `NFR-PERF-01`, `AC-NFR-01` | median 2.03 ms; p95 3.07 ms | Fixture pass only; not final-app baseline evidence. |
| 20 healthy fixture online checks | `NFR-PERF-02`, `AC-NFR-02` | median 15.50 ms; p95 15.98 ms | Fixture pass only; no live source contacted. |
| Five-minute Release-desktop idle | `NFR-PERF-05`, `AC-NFR-06` | CPU 4.9883%; peak working set 240.53 MiB; disk read/write 0 B | **Fail**: CPU exceeds 1%. Network is unverified because the non-elevated host has no process-attributed network counter. |
| Five-minute Release-desktop, lightweight Scan page | `NFR-PERF-05`, `AC-NFR-06` | CPU 1.8392%; peak working set 227.64 MiB; disk read/write 0 B | **Fail**, but 3.1491 percentage points lower than Overview. The page has no inventory ListView or data/reset composition; the shared shell still exceeds budget. |
| Loss-free ETW, PCSteward idle interval | `NFR-PERF-05`, `AC-NFR-06` | 72.734516 CPU-seconds in 85.525729 seconds; window thread 36952: 64.089183 CPU-seconds; `Microsoft.UI.Xaml.dll`: 2.55% sampled total CPU | Sustained UI/layout/composition workload is process-attributed. No sampled stacks or heap/virtual-allocation events are present, so source-level cause remains unproven. |
| Loss-free ETW, controlled page comparison | `NFR-PERF-05`, `AC-NFR-06` | PCSteward PID 40372: Scan `Microsoft.UI.Xaml.dll` 0.22% / UI Automation 0.08%; Activity XAML 0.21% / UI Automation 0.08%; Overview XAML 1.83%, CoreCLR 0.71%, UI Automation 0.10% total-system CPU | Overview-specific XAML plus managed work is the excess; UI Automation is flat and no UI Automation provider events were captured. Individual frames remain unresolved because the required framework-symbol fallback was blocked by local Avast URL policy, so no source change was made. |
| Normal close / no resident process | `NFR-PERF-07`, `AC-NFR-08` | no window immediately after Alt+F4; no process after 30 seconds | Pass on this host. |
| Cancellation, independent timeout handling, second-scan rejection and cache active-artifact protection | `NFR-PERF-08–10`, `NFR-REL-01–03`, `AC-NFR-03/05/09–10` | 27 focused integration tests passed | Automated regression evidence; no new live-source action performed. |

## Accessibility and UI observation

The Release desktop was launched with Windows UI Automation. The accessibility tree exposed named primary navigation, connector controls, scan/source actions, filters, data controls, and the factory-reset control. Textual live/cached/local-only state and descriptive destructive-action scope were exposed. The app closed normally.

Keyboard traversal, Narrator narration, Windows High Contrast, and 100/125/150/175/200% text-scale matrices remain incomplete. They are `AC-UX-01–09` best-effort evidence under ADR-021, but may not be reported as passed. No destructive action, source refresh, schedule registration, or update was invoked during this review.

## Approved performance deferral

On 2026-07-29 the product owner approved `PO-EXC-29-PERF-01`: defer remediation and final measurement of the unmet idle-resource gate (`NFR-PERF-05`, `AC-NFR-06`) to a future performance-improvement task. This exception is limited to the recorded performance shortfall: it does not mark the requirement passed, does not change the CPU/memory/disk/network budget, and does not authorize a speculative ListView or navigation rewrite. The retained findings are that Overview-specific XAML plus managed work is the excess, UI Automation is not the driver, and the framework-symbol fallback is blocked by local Avast policy. Any future remediation must use the recorded ADR-006 baseline and fixed power plan, include process-attributed network evidence, identify the source-level cause, and repeat the complete five-minute gate.
