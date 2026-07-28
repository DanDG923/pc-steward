# Task 90 — remediate repeated Phase 2 review

## Goal

Resolve `P2R-07`–`P2R-10` without adding update execution, a raw connector, an arbitrary website, or an unapproved live source probe. Preserve the Local-only default and fail closed where package provenance, installation-instance evidence, or source agreement is incomplete.

## Requirement IDs

`FR-SRC-03–05`, `FR-ID-01–07`, `FR-POL-01–05`, `FR-HIS-02/04`, `FR-UI-01–07`, `NFR-REL-04`, `NFR-PERF-02`, `AC-MAT-01–03/06/07`, `AC-UX-01–06`, `AC-NFR-02/05`, `AC-DAT-04`.

## Ordered remediation

1. High — retain normalized WinGet source provenance, persist it through SQLite, and require an exact trusted source as well as package identity for automatic mappings (`P2R-07`).
2. High — preserve all WinGet target instances and fail closed when version, scope, architecture, channel, or source evidence identifies more than one target (`P2R-08`).
3. Medium — persist approval and fail-closed revocation for both fixed WinGet connector disclosures (`P2R-09`).
4. Medium — preserve per-source candidate observations in the review UI and block automatic/provider-supplied mappings where trusted sources disagree (`P2R-10`).
5. Mandatory evidence — after explicit product-owner approval, exercise fixed read-only WinGet and Store checks with output discarded, record only safe aggregates and process-scoped connection metadata, and verify real connector parser/capability behavior.
6. Verify the focused regressions, Release build, full suite, fixture benchmark, traceability, and a fresh GPT-5.6 Sol / High Task 20 review. Do not merge Phase 2 or start Phase 3.

## Evidence limit

No real WinGet or Microsoft Store query will be run until the product owner explicitly approves the installed-software metadata disclosure. Local fixture connectors, persistence tests, and presentation tests are safe to run without that approval.

## Completion record

The final GPT-5.6 Sol / High Task 20 review passed after accepting the administrator-token, process-attributed production trace for `AC-DAT-04` and verifying the `P2R-10` fail-closed correction. Three approved read-only source queries on this controlled host (WinGet default, WinGet `--include-unknown`, and Microsoft Store default) each returned zero parser-table rows. By product-owner exception, capture one documented, sanitized real update row during Phase 3 when an update becomes available; see `docs/failures/90-phase2-real-row-evidence.md`.
