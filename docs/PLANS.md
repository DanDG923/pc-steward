# Task 90 — remediate Phase 2 review

## Goal

Resolve `P2R-01`–`P2R-06` without adding update execution, a raw connector, an arbitrary website, or an unapproved live source probe. Preserve the current Local-only default and only contact WinGet/Store after an explicit, persisted source-disclosure approval and an explicit user check.

## Requirement IDs

`FR-SRC-03–05`, `FR-POL-01–05`, `FR-HIS-02/04`, `FR-UI-01–07`, `NFR-REL-04`, `NFR-PERF-02`, `AC-MAT-03/06/07`, `AC-UX-01–06`, `AC-NFR-02/05`, `AC-DAT-04`.

## Ordered remediation

1. High — compose the fixed WinGet/Store source connectors through the application workflow, retain the Local-only default, and add explicit source-check and exact manual-mapping use cases (`P2R-01`).
2. High — make freshness source-specific in the domain-to-presentation projection and display stale/unavailable sources without presenting cached mappings as live (`P2R-02`).
3. High — bound stdout and stderr while the fixed WinGet process runs, terminate on overflow, and test overflow, timeout and cancellation (`P2R-03`).
4. Mandatory — load the checked-in fixture catalog, run its five declared mapping controls and relevant edge cases, and prove zero false-positive actionable mappings (`P2R-04`).
5. Medium — complete scoped decision controls, product timeline and source-cache retention, including accessibility labels and expiry handling (`P2R-05`).
6. Evidence — add integration/performance/desktop-safe tests, update traceability precisely, and record remaining unapproved real-network evidence as unproven (`P2R-06`).

## Evidence limits

No real WinGet or Microsoft Store query will be run without explicit approval because it can disclose installed-software metadata. Local fixture connectors, process-boundary tests and the desktop's Local-only path can be exercised safely. `AC-DAT-04` remains pending an approved network inspection; the end-to-end healthy-source performance result can only be claimed for deterministic test connectors until an approved source exercise occurs.

## Completion record

Pending. A fresh GPT-5.6 Sol / High Task 20 review is required after this remediation commit.
