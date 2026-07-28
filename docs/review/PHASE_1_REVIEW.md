# Phase 1 review

**Phase:** Phase 1 — Local inventory vertical slice
**Base commit:** `5d7e2ca5154504f093a69b0e9ab59b1052776344`
**Head commit:** `48919ac830dde481f45c49e1c1d5796f93c861fa`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Date:** 2026-07-28

## Gate result

`FAIL`

The branch builds and its current automated tests pass, but mandatory inventory, persistence, cancellation, UX and performance criteria are unproven or contradicted by the composed desktop path. Task 90 must remediate the findings, then Task 15 must be repeated.

## Evidence reviewed

- Requirements and acceptance criteria: `docs/requirements/PHASE_1.md`; `docs/requirements/TRACEABILITY.md`.
- ADRs: accepted ADR-006, ADR-010, ADR-014, ADR-015, ADR-016 and ADR-019, plus the Phase 1-relevant accepted architecture and test-strategy documents.
- Builds/tests: targeted Domain and Application tests; Release solution build; full solution test suite; performance-fixture assertions.
- Benchmarks: `spikes/performance-fixtures/BASELINE.md`, `BENCHMARK_PROTOCOL.md`, raw aggregate `benchmark-20260728T095015Z.json`, and the current product benchmark entry point.
- Manual/VM tests: Task 13 accessibility inspection record; no successful clean manual scan/cancel exercise or accepted baseline-VM product measurement exists.
- Diff range: `5d7e2ca...48919ac` (`main...phase/1-inventory`), 25 files, 1,050 insertions and 12 deletions.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| P1-01 | High | `FR-INV-04–05/07`, `AC-INV-01–02` | `src/PCSteward.Desktop/MainWindow.cs:31`; `src/PCSteward.Domain/InventoryNormalization.cs:12` | The shipped desktop scan composes only `RegistryLocalInventoryProvider`. The MSIX and WinGet providers have no production catalog/process adapter or composition-root consumer, and normalization/deduplication is never invoked by production code. A normal scan therefore misses two required surfaces and cannot present duplicate instances once. | Add standard-user host adapters and compose enabled registry, current-user MSIX and WinGet-installed inventory paths; pass all raw evidence through conservative normalization before atomic promotion. Make connector selection control the provider set. | End-to-end composition fixture proves every expected record from all three surfaces, duplicate collapse with retained provenance, and separate user/machine and x86/x64 instances. |
| P1-02 | High | `FR-INV-06/08–09`, `FR-RSC-05`, `FR-HIS-01`, `NFR-OBS-01–02` | `src/PCSteward.Desktop/RegistryLocalInventoryProvider.cs:19`; `src/PCSteward.Application/InventoryScanOrchestrator.cs:30`; `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:26` | Registry conversion discards publisher, install date/path and classification; no provider path creates `RawProviderRecord`; and successful scans write only canonical products and instances. The schema has scan-run, raw-record and timing tables, but production code never inserts them. Displayed details and stored history therefore lack required raw provenance, field coverage, correlation and timing evidence. | Extend the application port and one transactional scan unit of work to persist bounded raw evidence, scan summary, correlation ID, provider timings/warnings and the normalized snapshot while retaining supported metadata. | Integration tests read back raw records, fields, completed/cancelled runs and timings, and prove an interrupted transaction preserves the prior complete snapshot and history. |
| P1-03 | High | `FR-RSC-03`, `NFR-PERF-03/08`, `AC-NFR-03` | `src/PCSteward.Desktop/MainWindow.cs:81`; `src/PCSteward.Desktop/RegistryLocalInventoryProvider.cs:14`; `src/PCSteward.Application/InventoryScanOrchestrator.cs:22` | The scan is entered from the UI thread and the registry provider performs its complete synchronous enumeration before returning an already-completed task. The UI cannot reliably render or activate Cancel while that work is running. The orchestrator supplies no per-provider timeout, and the only cancellation test starts with a token already cancelled, so in-flight acknowledgement, child termination and operable-state budgets are not proven. | Move blocking provider work off the UI thread behind bounded adapters, apply configurable per-provider timeouts, preserve provider isolation, and make scan/cancel state transitions exception-safe. | In-flight slow/hung provider tests plus a desktop exercise prove visible enabled cancellation, acknowledgement within two seconds, operability within five seconds, timeout isolation and unchanged last-known-good data. |
| P1-04 | High | `NFR-PERF-01/05–06/08–09`, `AC-NFR-01/03/06–07/09` | `tests/PCSteward.Performance/Program.cs:5`; `spikes/performance-fixtures/BASELINE.md:5` | No final-application performance evidence exists. The product benchmark explicitly reports a scaffold, while the retained PowerShell spike explicitly excludes the UI, persistence, real providers, WinGet, idle activity and the final process tree and says no budget is met. Mandatory 30-run local p95, five-minute idle, scan CPU/working set, UI cancellation and measured concurrency criteria are unproven. | Implement the Phase 1 product workload in the benchmark harness and execute the accepted protocol on the agreed Windows 11 x64 baseline, or document and approve the Phase 1 exit condition's concrete evidenced remediation plan without claiming compliance. | Retain raw environment/build IDs and every sample; report median/p95, five-minute idle CPU/memory/disk/network, full process-tree scan CPU/working set, cancellation acknowledgement/recovery and maximum concurrency. |
| P1-05 | High | `FR-ONB-04`, `FR-UI-02/05`, `AC-UX-01/06/09` | `src/PCSteward.Desktop/MainWindow.cs:48`; `src/PCSteward.Desktop/MainWindow.cs:63`; `src/PCSteward.Application/InventoryExperienceViewModel.cs:33` | Connector checkboxes mutate presentation state only and never change the fixed provider list; MSIX is shown enabled even though it is not scanned. The desktop renders only search, not the implemented status/source/scope/sort controls. On restart `LoadSavedInventory` does not restore last-scan metadata, so cached inventory is labelled “No scan yet”. These are materially misleading and contradict the always-distinguish-live/cached/local-only criterion. | Persist connector consent/settings and scan freshness, bind enabled connectors to orchestration, expose the required filters/sort, and label restored inventory as cached with provider freshness. | View-model and desktop integration tests cover restart, connector enable/disable behavior, all required filters/sort, cached/live/local-only labels and a complete keyboard-driven first scan. |
| P1-06 | Medium | `NFR-MNT-01/05`, ADR-014 | `src/PCSteward.Desktop/MainWindow.cs:16`; `src/PCSteward.Desktop/PCSteward.Desktop.csproj:10` | Presentation directly constructs and calls the SQLite and inventory adapters even though accepted ADR-014 requires the desktop presentation to use application use cases rather than adapter APIs. Existing architecture tests do not enforce this direction. | Move composition behind application-facing use cases/services and add a reference/source boundary test that prevents presentation code from invoking persistence/provider APIs directly. | Architecture suite fails on a deliberately forbidden UI-to-adapter dependency and passes with the corrected composition. |
| P1-07 | Medium | `FR-DAT-10` | `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:77`; `src/PCSteward.Desktop/MainWindow.cs:92` | The displayed “Scan data” byte count omits scan-run, timing and update-candidate content and all database overhead while presenting the number as the current size. It can materially understate deletion scope. | Define the metric precisely and calculate the complete owned scan-data size, or label a complete record/payload estimate accurately. | Seed every disposable scan table and verify the displayed metric and post-clear value against the documented calculation. |

## Missing evidence

- `AC-INV-04` / `NFR-REL-05`: malformed MSIX identities/fields and hostile or oversized WinGet table fields are not covered through a composed scan.
- `AC-INV-05`: no runnable enabled WinGet composition exists from which to prove that local-only mode makes no update-source request.
- `AC-NFR-04`: no process-termination-during-real-staging test proves the prior snapshot is readable after restart.
- `AC-UX-02/04/09`: no successful clean manual scan/cancel, text-scaling or high-contrast exercise exists; the prior automation did not activate the scan button.
- Accepted baseline-VM prerequisites, including fixed power plan and SSD-equivalent storage, remain unverified.

## Positive controls confirmed

- Static review found no `Win32_Product`, WMI inventory or MSI-repair invocation in the Phase 1 diff (`AC-INV-03`).
- The composed registry-only scan contains no elevation request, update execution or explicit network API.
- SQLite snapshot promotion and Clear scan data use transactions; current tests prove rollback on failed replacement and idempotent preservation of decisions, mappings, update jobs and downloaded-artifact records.
- CSV/JSON exports include an export timestamp and privacy warning.
- Release build completed with zero warnings/errors; all 24 solution tests passed; the fixture harness passed 14 assertions.

## Residual risks accepted by product owner

None recorded. A failed gate does not request residual-risk acceptance.

## Recommended next action

Run Task 90 with GPT-5.6 Terra / High, ordered P1-01 through P1-05, then mandatory evidence, then P1-06 and P1-07. Repeat Task 15 in a fresh GPT-5.6 Sol / High review after remediation is committed.
