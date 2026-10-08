# PC Steward — Decision Log

Use this file for product decisions that do not require a full architecture decision record.

| Date | Decision ID | Decision | Reason | Requirements affected | Owner | Status |
|---|---|---|---|---|---|---|
| 2026-07-27 | D-001 | Resource efficiency and measurable scan-time limits are mandatory MVP requirements. | PC Steward must not materially degrade normal PC use or remain active unnecessarily. | G-09, FR-RSC-01–05, NFR-PERF-01–10, AC-NFR-01–11 | Product owner | Approved |
| 2026-07-27 | D-002 | Add a dedicated **Clear scan data** action that preserves unrelated settings and update history. | Users need to remove scan-derived inventory without resetting the application. | G-10, UJ-10, FR-DAT-06–07, AC-DAT-06–07 | Product owner | Approved |
| 2026-07-27 | D-003 | Add **Clear downloaded files** in Settings and **Delete downloaded files** after applicable updates. | Users need direct control over storage consumed by PC Steward-owned update payloads. | G-10, UJ-04, UJ-10, FR-DAT-08–11, AC-DAT-08–11 | Product owner | Approved |
| 2026-07-27 | D-004 | Add **Factory reset PC Steward**, removing all local state while leaving the app installed. | Users need a reliable route back to a fresh first-launch state. | G-10, UJ-11, FR-DAT-12–15, AC-DAT-12–16 | Product owner | Approved |
| 2026-07-28 | D-005 | Lock ADR-001–020 as the Phase 0 scaffold baseline; ADR-002, ADR-003, ADR-011 and ADR-013 remain Provisional behind explicit evidence gates. | Task 06 must not invent packaging, trust, persistence, privilege, deletion or scheduling boundaries. | OD-01–13, NFR-MNT-05 | Implementation and security reviewers | Approved |
| 2026-10-08 | D-006 | PC Steward is personal software for the owner's own PC only. No distribution, other users or public release are planned. | Owner statement during Task 43 overhaul planning. | Spec §14.3, §19.8; `FRA-14–16`; `docs/OVERHAUL_PLAN_PATCH_STYLE.md` §1.1, C-11, C-12 | Product owner | Approved (scope); C-11/C-12 document changes pending Task 44 |

## Open questions

| Question ID | Question | Default from specification | Needed by | Status |
|---|---|---|---|---|
| Q-001 | What exact packaging model best supports WinGet integration, on-demand elevation and reliable factory reset? | Signed unpackaged app plus traditional per-user installer. | Task 30 | Resolved provisionally — ADR-002 |
| Q-002 | Are the 2 GB download-cache soft quota and retention defaults appropriate after testing real installer sizes? | 2 GB, seven days after verified success and 24 hours for failed/incomplete files. | Tasks 24/29 | Resolved provisionally — ADR-011 |
| Q-003 | Which reset mechanism most reliably verifies full cleanup without adding privileged or arbitrary-path capability? | Verified next-launch cleanup marker; no reset helper. | Tasks 27/28 | Resolved — Accepted ADR-013; Task 28 implementation evidence remains |
| Q-004 | Patch-style overhaul catalog strategy: curated bundled catalog, full WinGet/Chocolatey repository search, or both? | Not covered; `FR-SRC-09` prohibits arbitrary web search only. | Task 44 | Open — plan recommends a personal catalog seeded from the owner's installed apps plus in-app Add from search |
| Q-005 | Automatic updates: user-scope only (Stage A) or also unattended machine-scope (Stage B)? | Prohibited in the MVP (`FR-UPD-12` Later, `FR-SCH-03`). | Task 44 | Open — plan recommends Stage A, then Stage B with admin-protected policy store after ~2 weeks of clean Stage A use |
| Q-006 | May PC Steward set up an absent Chocolatey client by installing `Chocolatey.Chocolatey` through WinGet? | Not covered; Chocolatey must already be present (`FR-SRC-07`). | Task 44 | Open — plan recommends yes, as a reviewed machine-scope Install; never the PowerShell bootstrap |
| Q-007 | May an app installed another way be reinstalled through Chocolatey (adoption)? | Not covered. | Task 44 | Open — plan recommends only as an explicit, labelled Install; never automatic |
| Q-008 | May a catalog-curated Chocolatey Community ID replace manual exact-ID confirmation? | No; every Chocolatey candidate needs manual exact mapping (spec §11.1). | Task 44 | Open — plan recommends yes, with source disclosure in the batch review sheet; search results confirmed when added |
| Q-009 | Are user-initiated uninstall and pre-batch restore points in scope? | Uninstall is a non-goal for automatic use; restore points are not covered. | Task 44 | Open — plan recommends optional Phase 10 |
| Q-010 | Does the overhaul change the owner-only distribution boundary? | Owner-only; `FRA-14–16` blocking. | Task 44 | Resolved — D-006: personal owner-only use; `FRA-14–16` to be retired in Task 44 (C-11) |

## Phase 0 ADR lock

Accepted: ADR-001, ADR-004–010, ADR-012–020.
Provisional with explicit reconsideration gates: ADR-002, ADR-003 and ADR-011.
No ADR remains Proposed.
