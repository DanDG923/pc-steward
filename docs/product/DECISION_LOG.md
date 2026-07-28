# PC Steward — Decision Log

Use this file for product decisions that do not require a full architecture decision record.

| Date | Decision ID | Decision | Reason | Requirements affected | Owner | Status |
|---|---|---|---|---|---|---|
| 2026-07-27 | D-001 | Resource efficiency and measurable scan-time limits are mandatory MVP requirements. | PC Steward must not materially degrade normal PC use or remain active unnecessarily. | G-09, FR-RSC-01–05, NFR-PERF-01–10, AC-NFR-01–11 | Product owner | Approved |
| 2026-07-27 | D-002 | Add a dedicated **Clear scan data** action that preserves unrelated settings and update history. | Users need to remove scan-derived inventory without resetting the application. | G-10, UJ-10, FR-DAT-06–07, AC-DAT-06–07 | Product owner | Approved |
| 2026-07-27 | D-003 | Add **Clear downloaded files** in Settings and **Delete downloaded files** after applicable updates. | Users need direct control over storage consumed by PC Steward-owned update payloads. | G-10, UJ-04, UJ-10, FR-DAT-08–11, AC-DAT-08–11 | Product owner | Approved |
| 2026-07-27 | D-004 | Add **Factory reset PC Steward**, removing all local state while leaving the app installed. | Users need a reliable route back to a fresh first-launch state. | G-10, UJ-11, FR-DAT-12–15, AC-DAT-12–16 | Product owner | Approved |
| 2026-07-28 | D-005 | Lock ADR-001–020 as the Phase 0 scaffold baseline; ADR-002, ADR-003, ADR-011 and ADR-013 remain Provisional behind explicit evidence gates. | Task 06 must not invent packaging, trust, persistence, privilege, deletion or scheduling boundaries. | OD-01–13, NFR-MNT-05 | Implementation and security reviewers | Approved |

## Open questions

| Question ID | Question | Default from specification | Needed by | Status |
|---|---|---|---|---|
| Q-001 | What exact packaging model best supports WinGet integration, on-demand elevation and reliable factory reset? | Signed unpackaged app plus traditional per-user installer. | Task 30 | Resolved provisionally — ADR-002 |
| Q-002 | Are the 2 GB download-cache soft quota and retention defaults appropriate after testing real installer sizes? | 2 GB, seven days after verified success and 24 hours for failed/incomplete files. | Tasks 24/29 | Resolved provisionally — ADR-011 |
| Q-003 | Which reset mechanism most reliably verifies full cleanup without adding privileged or arbitrary-path capability? | Verified next-launch cleanup marker; no reset helper. | Tasks 27/28 | Resolved provisionally — ADR-013 |

## Phase 0 ADR lock

Accepted: ADR-001, ADR-004–010, ADR-012 and ADR-014–020.
Provisional with explicit reconsideration gates: ADR-002, ADR-003, ADR-011 and ADR-013.
No ADR remains Proposed.
