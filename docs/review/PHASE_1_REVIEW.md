# Phase 1 review — fresh repeat after Task 90

**Phase:** Phase 1 — Local inventory vertical slice
**Base commit:** `5d7e2ca5154504f093a69b0e9ab59b1052776344`
**Head commit:** `7d568eec61ff4bc32046b15fa48a481e500f807d`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Date:** 2026-07-28

## Gate result

`FAIL`

Task 90 fixed duplicate-product normalization and scan-freshness clearing, and it improved cooperative cancellation and connector persistence. The phase still has four High and two Medium findings. Mandatory trust, cancellation, performance, accessibility and UX criteria are failed or unproven, so the gate cannot pass. A VM is not required: the accepted baseline may be a recorded physical or virtual Windows 11 x64 environment.

## Evidence reviewed

- Complete `main...phase/1-inventory` diff from merge-base `5d7e2ca` through `7d568ee`, Phase 1 requirements, accepted ADR-006/014/016, test strategy and Task 90 evidence.
- Release build: zero warnings and zero errors.
- Full suite: 29 passed — Domain 4, Application integration 22, Architecture 2 and Security fixture 1.
- Deterministic workload: 30 in-memory 300-record runs, 1.16 ms median and 1.36 ms p95. The harness excludes SQLite, Windows providers, desktop/UI, idle and process-tree measurements and retains no acceptance samples.
- Performance fixture integrity: 14 assertions passed; Task 04 evidence remains methodology-only.
- Static search found no production `Win32_Product`, WMI inventory, elevation request, HTTP client, update execution or shell-enabled WinGet process.
- No approved enabled-WinGet exercise, final-application baseline run, five-minute idle/process-tree capture, crash/restart staging test, or successful keyboard/scaling/high-contrast exercise was supplied.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| P1RR-01 | High | `AC-INV-05`, `FR-ONB-04`, `AC-UX-06` | `src/PCSteward.Desktop/MainWindow.cs:46`, `:60`, `:66`; `src/PCSteward.Desktop/AdditionalLocalInventoryProviders.cs:41` | WinGet is visually disabled, but startup loads any persisted `true` value and the provider executes when that value is true. A database created by the prior UI can therefore run WinGet while the disabled checkbox says approval is required and the button/status still claim a local-only scan. This bypasses the intended source-disclosure gate and makes the local-only label false. | Fail closed for WinGet unless an explicit approved consent state exists; ignore or migrate legacy `true` values, and derive scan mode/labels from the effective provider plan. | Pre-seed WinGet as enabled, restart, and prove no WinGet process/source request occurs and the stored state is disabled; separately verify an approved future flow never uses local-only labels. |
| P1RR-02 | High | `FR-RSC-03`, `NFR-PERF-08`, `AC-NFR-03` | `src/PCSteward.Application/InventoryScanOrchestrator.cs:30`; `src/PCSteward.Desktop/AdditionalLocalInventoryProviders.cs:21`; `src/PCSteward.Desktop/RegistryLocalInventoryProvider.cs:15` | On timeout the orchestrator cancels and waits only one additional second, then publishes other results while an uncooperative provider may still run. A non-cancellation fault raised during that one-second wait also escapes the timeout handler and aborts the scan. The registry/MSIX adapters wrap synchronous Windows enumeration in `Task.Run`; cancellation cannot stop an enumeration already in progress. | Define a provider-quiescence boundary: every timed-out provider must be awaited to completion, hosted in a terminable worker, or prevented from outliving recovery. Contain faults raised after timeout. | An uncooperative-provider fixture proves no work remains after recovery; a post-timeout fault fixture proves successful providers still publish; Windows-provider cancellation evidence meets the UI recovery budget. |
| P1RR-03 | High | `NFR-PERF-01/03/05–06/08–09`, `AC-NFR-01/03/04/06–07/09`, `AC-UX-02/04/09` | `tests/PCSteward.Performance/Program.cs:18` | The benchmark is still an in-memory fake-provider/no-op-store microbenchmark. Mandatory final-application performance, five-minute idle, process-tree resource, UI responsiveness, cancellation recovery, concurrency, crash/restart and manual accessibility criteria remain unproven. Its message also retains obsolete “clean-VM” wording, although a VM is now optional. | Run and retain the accepted protocol against the final application on a recorded qualifying Windows 11 baseline, physical or virtual, and complete the keyboard, scaling and high-contrast exercises. | Review every raw sample and manual result against its threshold; do not substitute the Task 04 fixture or microbenchmark. |
| P1RR-04 | High | `FR-UI-02`, `AC-UX-01/06/09` | `src/PCSteward.Desktop/MainWindow.cs:75`, `:77`, `:80`; `src/PCSteward.Application/InventoryExperienceViewModel.cs:54` | Publisher and status filter controls are absent. Every row is hard-coded to `Installed`; entering search resets status/source/scope/sort, and every progress-triggered re-render recreates the combo boxes at their defaults. The required filtering/sorting experience is therefore incomplete and unstable. | Add publisher/status data and controls, preserve all selected filters across search and re-render, and expose stable accessible names/state. | View-model plus desktop fixtures exercise every filter and sort in combination and prove state survives search, progress and restart where required. |
| P1RR-05 | Medium | `FR-INV-06/08–09`, `FR-UI-07`, `NFR-OBS-02` | `src/PCSteward.Domain/InventoryModels.cs:4`; `src/PCSteward.Application/InventoryScanOrchestrator.cs:46`; `src/PCSteward.Diagnostics/PCSteward.Diagnostics.csproj:1` | Publisher, install date/path, classification and channel are stored only in opaque raw strings, then normalization replaces typed publisher/channel/classification with null/unknown. No application port reads raw records for diagnostics, and the Diagnostics project contains no typed severity events. | Preserve bounded typed metadata through normalization/persistence, expose raw provenance through an application use case, and emit allowlisted structured scan events. | Provider-to-persistence/UI tests prove typed retention and bounded raw diagnostics; logging tests prove correlation/severity and redaction. |
| P1RR-06 | Medium | `NFR-MNT-01/05`, ADR-014 | `src/PCSteward.Desktop/MainWindow.cs:16`, `:29`; `src/PCSteward.Desktop/PCSteward.Desktop.csproj:11`, `:13`; `tests/PCSteward.ArchitectureTests/ReferenceBoundaryTests.cs:10` | `MainWindow` constructs and calls SQLite and provider adapters directly, contrary to the accepted presentation boundary. Existing architecture tests cover only ScheduledHost and ElevatedHelper, so the violation is not guarded. | Move inventory/settings/data-reset composition behind application use cases and add a presentation reference/source-boundary regression. | Architecture tests fail if presentation code calls persistence/provider APIs; desktop tests use application ports or use cases only. |

## Missing mandatory evidence

- Thirty retained final-application local-scan samples on a recorded qualifying Windows 11 baseline, plus environment/build identity and warm-up details.
- Five-minute idle CPU/memory/disk/network and full process-tree local-scan measurements.
- UI responsiveness, cancellation acknowledgement/recovery and measured concurrency evidence.
- Process termination during scan staging followed by successful last-known-good restart.
- Successful keyboard-driven scan/cancel, text scaling and high-contrast exercises.
- Approved enabled-WinGet behavior and proof that effective local-only mode makes no WinGet/source request.

## Positive controls confirmed

- Duplicate provider products now reach deterministic normalization; the orchestration regression passes.
- WinGet process cancellation/timeout terminates and awaits the child tree; fixed executable, structured arguments and shell-disabled execution remain intact.
- Clear scan data removes freshness in the same transaction and preserves connector settings, decisions, mappings, history and downloaded-artifact records.
- Successful scan promotion remains transactional and cancellation preserves the previous snapshot.
- Registry, MSIX and WinGet parser fixtures retain malformed-input isolation; no update execution exists.
- Export timestamp/privacy behavior and the corrected estimated-payload label remain intact.

## Residual risks accepted by product owner

None recorded.

## Required next action

Run Task 90 with GPT-5.6 Terra / High for P1RR-01 through P1RR-06, collect the mandatory evidence on a recorded physical or virtual Windows baseline, commit the remediation, then repeat Task 15 in a fresh GPT-5.6 Sol / High review.
