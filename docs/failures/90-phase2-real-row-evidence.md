# Task 90 — real update-row evidence blocked

## Goal

Obtain one provenance-recorded, sanitized real WinGet update-table row from a controlled Windows 11 host to exercise the production table parser's field and source-column validation required by ADR-003.

## Approved, non-mutating attempts

1. `winget.exe upgrade --source winget --disable-interactivity`
   - Exit code: `0`
   - Duration: `1,542.32 ms`
   - Retained result: zero parser-table rows; no package identifier, name, version, listing text, or payload.

2. `winget.exe upgrade --include-unknown --source winget --disable-interactivity`
   - Exit code: `0`
   - Duration: `1,100.61 ms`
   - Retained result: zero parser-table rows; no package identifier, name, version, listing text, or payload.

3. `winget.exe upgrade --source msstore --disable-interactivity`
   - Exit code: `0`
   - Duration: `1,064.09 ms`
   - Retained result: zero parser-table rows; no package identifier, name, version, listing text, or payload.

## Blocking condition

Both material WinGet probes and the separately approved Microsoft Store probe produced no real row on this controlled host. A Microsoft Store query that adds `--include-unknown` has not been approved and would be a further external metadata disclosure.

The local WinGet diagnostic folder was also inspected by filename, timestamp, size, and generic table-marker classification only. It contains command diagnostics but no retained upgrade-table header or row, so it cannot provide a provenance-recorded captured fixture.

## Smallest next step

Provide a provenance-recorded, sanitized real update-table capture from another controlled Windows 11 host. Any retained evidence must be limited to the source column plus sanitized field shape/lengths and non-reversible digests; raw listing text and package identifiers must be discarded.

## Changed files

- `docs/review/TASK_90_P2R_EVIDENCE.md`
- `docs/STATUS.md`
- `docs/PLANS.md`
- `docs/failures/90-phase2-real-row-evidence.md`
