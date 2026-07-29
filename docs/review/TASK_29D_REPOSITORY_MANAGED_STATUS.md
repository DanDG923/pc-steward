# Task 29D — Repository-managed application status

## Outcome

The former current-only destination is now **Repository apps**. It lists installed applications only when an enabled WinGet or Microsoft Store source has supplied a concrete source ID and package ID. Each card identifies the repository and package, installed and available version where applicable, source freshness, and one of these statuses:

- **Managed · up to date** — the installed version exactly matches the repository version.
- **Managed · update available** — a verified source/package mapping identifies a newer version.
- **Repository match · review required** — a concrete mapping exists but its evidence needs a user decision before it can be treated as actionable.

## Exclusions and authority

Local-only inventory, unmatched applications, candidate-only matches, unavailable sources, and rows without a concrete source/package identity are excluded. Disabling the update source immediately hides stored cached claims. Refresh is explicit and checks sources only; it never downloads or starts an update.

## Verification

- Focused Application integration tests: 117 passed, including `RepositoryManagedRowsExposeCurrentAndUpdateStatusesOnlyWhenAConnectedSourceHasPackageIdentity`.
- Release build: 0 warnings, 0 errors.
- Full suite: 193 passed.

## Requirement coverage

`FR-SRC-03–04`, `FR-UI-01/03–05`, `NFR-REL-01/04`, and `AC-UX-02–06` are recorded in the traceability matrix. `PO-EXC-29-PERF-01` remains unchanged and is not a pass claim.
