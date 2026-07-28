# Phase 1 review — repeat after Task 90

**Phase:** Phase 1 — Local inventory vertical slice
**Base commit:** `5d7e2ca5154504f093a69b0e9ab59b1052776344`
**Head commit:** `4f0e0fcfa81991e81268038e7252cb8110e8373d`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Date:** 2026-07-28

## Gate result

`FAIL`

Task 90 added provider composition, scan evidence persistence, timeout reporting, cached freshness and a deterministic workload, but mandatory correctness, cancellation, UX and acceptance-evidence criteria remain failed or unproven. Task 90 must run again and this review must be repeated.

## Evidence reviewed

- `docs/requirements/PHASE_1.md`, traceability, accepted ADR-006/010/014/015/016/019, test strategy and the complete `main...phase/1-inventory` diff.
- Targeted Domain tests: 4 passed; targeted Application integration tests: 20 passed.
- Release build: zero warnings/errors; full suite: 27 passed.
- Product workload: 30 in-memory 300-record runs, 1.00 ms median and 1.22 ms p95; its own output excludes baseline-VM, UI, idle and process-tree acceptance.
- Performance fixture integrity: 14 assertions passed; retained Task 04 evidence remains methodology-only.
- No successful clean manual scan/cancel, scaling/high-contrast exercise, approved enabled-WinGet probe or accepted baseline-VM product measurement exists.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| P1R-01 | High | `FR-INV-07`, `AC-INV-01–02/04` | `src/PCSteward.Application/InventoryScanOrchestrator.cs:43` | Orchestration calls `ToDictionary(product.Id)` before deduplication. Providers append one product per raw row, so two equivalent rows with the same stable product ID throw `ArgumentException`; the scan never reaches `NormalizeAndDeduplicate`. Existing tests exercise the rule directly but not duplicate provider output through orchestration. | Group/validate provider products before lookup, reject inconsistent collisions, and deduplicate complete raw evidence without losing provenance. | An orchestrator fixture supplies duplicate raw rows and proves one displayed instance, two raw records, no exception, and separation of scope/architecture variants. |
| P1R-02 | High | `FR-RSC-03`, `NFR-PERF-08`, `AC-NFR-03` | `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:46`; `src/PCSteward.Application/InventoryScanOrchestrator.cs:26` | `WindowsWingetProcess` kills the child only for `TimeoutException`; user cancellation raises `OperationCanceledException`, disposes the `Process` wrapper and can leave `winget.exe` running. Orchestrator timeouts likewise stop awaiting a provider without cancelling its work; the timeout test deliberately ignores cancellation and never proves the task ended. | Use a linked timeout/cancellation token and terminate/await the child process tree for both timeout and cancellation; require every blocking provider to observe cancellation or finish before scan recovery is reported. | Process fixture proves cancellation and timeout leave no child; provider fixture proves no background work continues after timeout and UI recovery remains within budget. |
| P1R-03 | High | `FR-DAT-06`, `AC-DAT-06`, `AC-UX-06` | `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:113`; `src/PCSteward.Desktop/MainWindow.cs:123` | Clear scan data deletes scan tables but preserves `app_metadata.scan_last_completed`. The UI calls `ClearScanData()` and immediately reloads that timestamp, which changes the empty dashboard back to “Cached local-only inventory” with a prior scan time instead of no-scan. Tests assert table counts only and miss the freshness lifecycle. | Delete scan-derived freshness metadata in the same clear transaction and make empty-snapshot loading ignore stale freshness. | Persistence/UI integration test scans, clears, reloads and proves no inventory, no last scan, no cached label, while preserved settings/decisions/history remain. |
| P1R-04 | High | `NFR-PERF-01/03/05–06/08–09`, `AC-NFR-01/03/06–07/09`, `AC-UX-02/04/09` | `tests/PCSteward.Performance/Program.cs:14` | The new benchmark measures in-memory fake generation, normalization and a no-op store using random records. It retains no raw samples/environment ID and excludes SQLite, Windows providers, WinGet, desktop responsiveness, five-minute idle, process-tree resources, cancellation recovery and measured concurrency. Mandatory acceptance and manual accessibility criteria remain unproven. | Implement and retain the accepted final-app measurement protocol on the agreed Windows 11 VM, including raw 30-run data, idle/process-tree/UI/cancellation/concurrency evidence and the required manual accessibility exercises. | Review raw results and manual evidence against every threshold; do not substitute Task 04 or this microbenchmark. |
| P1R-05 | High | `FR-ONB-04`, `FR-UI-02/05`, `AC-INV-05`, `AC-UX-01/06/09` | `src/PCSteward.Desktop/MainWindow.cs:46`; `src/PCSteward.Desktop/MainWindow.cs:56`; `src/PCSteward.Desktop/MainWindow.cs:63` | Connector choices are not persisted, only search is rendered despite required publisher/source/scope/status filters and sorting, and the button remains “Start local-only scan” after WinGet is enabled. The UI neither warns about the unapproved WinGet source-disclosure risk nor distinguishes that mode from the guaranteed local Windows-only scan. | Persist connector consent/settings, expose required filters/sort, and make scan mode/trust text and behavior accurately distinguish Windows-local collection from explicitly approved WinGet inventory. | Restart and desktop fixtures prove connector persistence, real provider selection, every filter/sort, accurate live/cached/local-only labels, and zero source request in local-only mode. |
| P1R-06 | Medium | `FR-INV-06/08–09`, `NFR-OBS-02` | `src/PCSteward.Application/InventoryScanOrchestrator.cs:44`; `src/PCSteward.Domain/InventoryModels.cs:4` | Normalization reconstructs evidence with `Publisher=null` and `Classification="Unknown"`, while `InstallationInstance` has no install date/path/publisher/classification and no application port reads raw records for diagnostics. Structured severity events are still not emitted. Data is partly serialized into opaque raw strings but unavailable to the UI or diagnostics flow. | Retain supported typed metadata through normalization/persistence, expose bounded raw provenance through an application use case, and emit typed structured scan events. | Composed provider-to-UI/persistence fixtures prove field retention, raw diagnostics and structured correlation/severity without logging unbounded raw payloads. |
| P1R-07 | Medium | `NFR-MNT-01/05`, ADR-014; `FR-DAT-10` | `src/PCSteward.Desktop/MainWindow.cs:29`; `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:100` | `MainWindow` still constructs/calls SQLite and providers directly, and no architecture test enforces the accepted presentation boundary. The displayed “bytes” value is a character-count estimate, not actual bytes or database size, and remains unlabeled as an estimate. | Move adapter composition behind application use cases and enforce the source boundary; define and label the scan-size metric or report actual owned storage bytes. | Architecture regression plus Unicode/all-table size fixtures prove both boundaries. |

## Missing evidence

- Clean Windows 11 x64 baseline with fixed power plan and SSD-equivalent storage.
- Thirty retained final-application scan samples, five-minute idle, full process-tree resources, UI responsiveness, cancellation recovery and bounded concurrency.
- Process termination during real staging followed by restart (`AC-NFR-04`).
- Successful keyboard-driven first scan/cancel, high contrast and text scaling.
- Approved enabled-WinGet behavior and proof that local-only mode makes no source request.

## Positive controls confirmed

- Static review found no `Win32_Product`, WMI inventory, MSI-repair invocation, elevation request or update execution in the Phase 1 diff.
- WinGet executable and arguments are fixed and shell execution is disabled.
- Successful scan promotion and clear-table deletion are transactional; stable decisions, mappings, update jobs and downloaded-artifact records remain separate.
- CSV/JSON exports retain timestamp and privacy warning behavior.
- P1-07's table coverage improved, although its unit and label remain inaccurate.

## Residual risks accepted by product owner

None recorded.

## Recommended next action

Run Task 90 with GPT-5.6 Terra / High, ordered P1R-01 through P1R-05, then mandatory evidence, then P1R-06 and P1R-07. Repeat Task 15 in a fresh GPT-5.6 Sol / High review.
