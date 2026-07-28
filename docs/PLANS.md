# Task 90 — remediate Phase 1 review findings

## Goal

Resolve P1-01 through P1-07 without adding update execution, elevation, network-source refresh, or any later-phase scope; leave baseline-VM/manual acceptance evidence explicitly unproven where it cannot be collected on this host.

## Requirement IDs

`FR-INV-04–09`, `FR-RSC-03/05`, `FR-HIS-01`, `FR-ONB-04`, `FR-UI-02/05`, `FR-DAT-10`, `NFR-PERF-01/03/05/06/08/09`, `NFR-MNT-01/05`, `NFR-OBS-01–02`, `AC-INV-01–02/04–07`, `AC-NFR-01/03/04/06–07/09`, `AC-UX-01/02/04/06/09`.

## Current evidence

Task 15 failed because production composition scans only the registry; raw records, scan summaries and timings are not written; cancellation is UI-thread-blocking and untimed; connector/freshness UI is misleading; the benchmark is a scaffold; and the desktop presentation calls adapter APIs directly.

## Files and components affected

Application scan/workspace ports and orchestration; Domain inventory evidence; SQLite schema and transactional persistence; Windows/MSIX and WinGet adapters; desktop composition and presentation; performance harness; unit, integration and architecture tests; review, traceability and status records.

## Steps

1. Replace the provider result with bounded raw inventory evidence, compose registry/MSIX/optional WinGet providers, and normalize before promotion (P1-01).
2. Persist scan runs, correlations, raw evidence, provider timings and supported instance metadata in one SQLite transaction (P1-02).
3. Run blocking providers away from the UI and enforce cancellable per-provider timeouts with regression tests (P1-03).
4. Replace the product performance scaffold with a deterministic 300-record workload and retain its non-baseline limitations (P1-04).
5. Make connector configuration, filters and cached freshness truthful through a desktop-facing application controller (P1-05).
6. Move adapter calls out of `MainWindow` and enforce that presentation boundary; make scan-data size account for every disposable table (P1-06, P1-07).
7. Run narrow regression suites, then Release build/full suite and affected fixture/performance evidence; append verification to the review report.
8. Update traceability/status, review the diff, commit Task 90, and stop for a fresh Task 15 review.

## Tests and measurable evidence

Composition fixtures cover all enabled providers, malformed data, deduplication and provider toggle selection. Persistence fixtures cover raw records/runs/timings, transactional failure and clear-data size. Orchestrator fixtures cover in-flight cancellation, timeout isolation and exclusive scans. The product benchmark runs 30 deterministic 300-record scans and records elapsed results; it does not claim clean-VM or UI/process-tree compliance.

## Security and failure cases

All provider input remains length-bounded and untrusted; no provider receives elevation. WinGet remains opt-in and its executable/arguments stay fixed. A timeout never grants an incomplete provider authority or replaces the last-known-good snapshot. No database row becomes command or deletion authority.

## Rollback / safe stopping point

Each scan writes a transactional replacement only after collection and normalization; cancellation, timeout or persistence failure leaves the previous snapshot intact. If baseline/manual evidence cannot be gathered, it remains a documented gate limitation rather than a pass.

## Decisions or assumptions

Current-user MSIX is enabled by default because it is a local Windows catalog. WinGet remains disabled by default, both because its installed-inventory command is optional and because no approval exists to disclose inventory to package sources. The fixed Task 04 fixture remains methodology evidence, not final baseline compliance.

## Completion record

- Commit: `29ad036` (amend pending status commit reference)
- Tests: Release build; full suite (27 tests); focused integration suite (20 tests); 30-run 300-record product workload.
- Remaining risks: Clean-VM baseline, manual UI, high contrast/text scaling and real WinGet-source behavior require later approved evidence.
