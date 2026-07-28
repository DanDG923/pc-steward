# Phase 1 review — fresh repeat after P1RR remediation

**Phase:** Phase 1 — Local inventory vertical slice
**Base commit:** `5d7e2ca5154504f093a69b0e9ab59b1052776344`
**Reviewed head commit:** `2b4b0cfe5f7907a76493b0d3e140324d5ac5e0d3`
**Reviewer model/effort:** GPT-5.6 Sol / High
**Date:** 2026-07-28

## Gate result

`FAIL`

P1RR-01, P1RR-02 and P1RR-04 through P1RR-06 are closed by implementation, automated coverage and recorded desktop evidence. P1RR-03 is materially narrowed: final-app local-scan timing, high contrast, approved WinGet behavior and restart loading are recorded, and the product owner has temporarily excepted the five-minute CPU check for this gate. Four other mandatory acceptance areas remain unproven, so the phase cannot pass.

## Evidence reviewed

- Complete `main...phase/1-inventory` diff from merge-base `5d7e2ca` through `2b4b0cf`, Phase 1 requirements, ADR-006, test strategy, traceability and Task 90 P1RR evidence.
- Release build: zero warnings and zero errors.
- Full suite: 38 passed — Domain 4, Application integration 30, Architecture 3 and Security fixture 1.
- Deterministic workload: 30 in-memory 300-record runs, 1.84 ms median and 2.32 ms p95. The harness correctly states that it is not final-app UI, idle or process-tree acceptance evidence.
- Recorded physical baseline: Windows 11 Home x64 build 26200, 16 logical processors, 31.15 GiB RAM, NVMe storage and fixed recorded `3XS-Overclock` plan.
- Thirty keyboard-started final-app local scans completed: 1,867 ms median, 1,893 ms p95 and 4,149 ms first observation.
- Approved WinGet inventory completed in 21 seconds with accurate approved-mode labels; restart loaded the cached approved inventory.
- High-contrast controls were visible. The intentional termination attempt missed the active scan, no keyboard-cancel timing was retained, and text scaling was not exercised.
- Static search found no production `Win32_Product`, WMI inventory, elevation request, HTTP client or shell-enabled process. WinGet remains fixed to `list --disable-interactivity`.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| P1RRR-01 | High | `NFR-PERF-03/06/08`, `AC-NFR-03/07`, `AC-UX-02/09` | `docs/review/TASK_90_P1RR_EVIDENCE.md:26–37`; `src/PCSteward.Desktop/MainWindow.cs:50–52` | Thirty scans prove completion timing, but no retained final-app run measures keyboard cancellation acknowledgement, return to an operable state, scan-time UI responsiveness, or process-tree CPU/peak working set. The recorded process-tree sample is an idle check, not `AC-NFR-07`. | Stage a sufficiently long final-app scan, invoke **Cancel scan** by keyboard, and retain timestamps, ready-state/data-integrity result, responsiveness and full process-tree resource samples. | Acknowledgement is at most two seconds, operable state is restored within five seconds, prior inventory remains valid, UI remains responsive and the scan-time process tree meets NFR-PERF-06. |
| P1RRR-02 | High | `NFR-REL-02`, `AC-NFR-04` | `docs/review/TASK_90_P1RR_EVIDENCE.md:31` | The termination attempt occurred after the scan had already completed. Restart loading proves ordinary recovery only; it does not prove that terminating the application during collection or publication preserves the previous successful inventory. | Stage a long scan with an existing successful snapshot, terminate the final app while the scan is active, then relaunch. | The previous snapshot, freshness and detail records remain readable and no partial replacement is visible. |
| P1RRR-03 | High | `NFR-PERF-09`, `AC-NFR-09` | `tests/PCSteward.Application.IntegrationTests/InventoryScanOrchestratorTests.cs:53–60`; `docs/review/TASK_90_P1RR_EVIDENCE.md:26–37` | Regression coverage proves that a replacement scan is rejected while an unresponsive provider holds the lease, but no retained acceptance measurement records maximum connector concurrency against the configured bound. The mandatory criterion is conjunctive. | Instrument and retain active-provider concurrency during the accepted final-app/harness scan, including a simultaneous second-scan attempt. | The second scan is rejected or routed to the first and maximum measured connector concurrency does not exceed the configured bound. |
| P1RRR-04 | High | `AC-UX-04` | `docs/review/TASK_90_P1RR_EVIDENCE.md:13`, `:37` | High-contrast rendering was exercised, but Windows text scaling was not. The criterion requires both and primary controls/status text are dynamically reconstructed inside a constrained desktop layout. | Exercise the final desktop at the accepted enlarged Windows text scale and retain the result. | Primary navigation, scan/cancel, status, filters, inventory and data/reset controls remain visible, readable and keyboard reachable. |

## Closed prior findings

- `P1RR-01`: explicit WinGet approval is persisted separately; legacy enabled state fails closed and approved mode never claims local-only behavior.
- `P1RR-02`: post-timeout faults are contained; an unresponsive provider prevents publication and holds the scan lease until quiescence.
- `P1RR-04`: publisher/status/source/scope filters and sorting persist together with stable accessible names.
- `P1RR-05`: typed inventory metadata, bounded raw diagnostics and structured allowlisted events cross the persistence/application boundary.
- `P1RR-06`: desktop behavior is routed through `InventoryWorkflow`, with an architecture regression guarding the presentation boundary.

## Residual risk accepted by product owner

`AC-NFR-06` / `NFR-PERF-05`: the five-minute idle run on `82f4a79` exceeded the CPU budget. Per product-owner direction, this check is temporarily excepted for the current Phase 1 gate and must be investigated and repeated in a later build. It is not recorded as compliance evidence.

## Required next action

Run Task 90 with GPT-5.6 Terra / High for P1RRR-01 through P1RRR-04 and retain the four mandatory evidence sets above. Then repeat Task 15 in a fresh GPT-5.6 Sol / High review.
