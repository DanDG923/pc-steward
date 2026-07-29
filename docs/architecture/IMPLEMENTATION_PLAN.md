# Implementation plan

**Status:** Accepted execution order; `COPY_PASTE_PROMPTS.md` remains authoritative

## Phase 0 — evidence and lock

1. **Task 02:** packaging/WinGet spike. Resolve evidence for OD-02/03; collect read-only WinGet capability, parsing, Store, timeout/cancellation and shell-free command-construction evidence.
2. **Task 03:** privilege/deletion spike. Resolve helper-protocol and OD-13 feasibility; prove disposable-root containment, reparse rejection, TOCTOU limits and accurate partial cleanup.
3. **Task 04:** performance/fixture spike. Establish OD-06 baseline, 300-record matrix, benchmark protocol and raw evidence; collect installer-size assumptions needed later by OD-11.
4. **Task 05:** complete. ADR-001–020 lock the scaffold boundaries; ADR-002/003/011/013 are Provisional with explicit implementation and isolated-environment gates.
5. **Task 06:** scaffold separated projects, dependency rules, CI/static-analysis guardrails, test infrastructure and reproducible build.

Current environment blockers affect native build/package proof but not scaffolding. The host-version discrepancy and missing tooling/environment evidence must be resolved before a task claims those acceptance conditions.

## Phase 1 — inventory vertical slice

Tasks 07–14 implement SQLite/stable decisions, registry/AppX/WinGet inventory, conservative identity/deduplication, orchestration/performance, UI, export and transactional Clear scan data. Task 14A assembles criterion-level evidence before Task 15 independently gates coverage, privacy, no elevation/`Win32_Product`, cancellation, last-known-good and performance.

## Phase 2 — update intelligence

Tasks 16–19 implement version semantics, deterministic matching, WinGet source cache/freshness and user decisions/UX. Task 19A assembles criterion-level evidence before Task 20 gates zero executable false positives. Optional Task 18B is permitted only after the first successful gate and requires the evidence task and Task 20 to repeat.

## Phase 3 — safe execution

Task 21 creates immutable jobs and the constrained non-elevated runner. Task 22 adds the independently bounded helper/IPC. Task 23 adds sequential batches and post-update truth. Task 24 adds owned-artifact lifecycle and safe cleanup. Task 24A assembles criterion-level security evidence before Task 25, an independent Extra High security gate; any execution, privilege, deletion or false-success defect blocks progression.

## Phase 4 — beta

Task 26 adds scan-only scheduling/notifications. Task 26A assembles evidence before Task 27 locks reset design from actual handles/state. Task 28 implements data controls/reset. Task 29 measures and hardens accessibility/reliability/performance. Task 30 produces packaging/SBOM/release evidence. Task 30A assembles final criterion-level evidence before Task 31 independently determines READY/NOT READY.

## Decision-to-evidence map

| Decision | Evidence owner |
|---|---|
| OD-01 name/identity | Accepted ADR-001; confirm Task 30 |
| OD-02 packaging | Provisional ADR-002; confirm Task 30 |
| OD-03 WinGet route | Provisional ADR-003; exercise Tasks 10/18/21/25 |
| OD-04 GitHub scope | First Task 20 gate/product-owner choice |
| OD-05 grouping | Accepted ADR-005; verify Tasks 11/15 |
| OD-06 baseline | Accepted ADR-006; confirm Task 29 |
| OD-07 schedule | Accepted ADR-007/020; verify Task 26 |
| OD-08 licence/public source | Accepted private-beta default ADR-008; product-owner decision before publication |
| OD-09 self-update | Accepted manual path ADR-009; verify Task 30 |
| OD-10 path export | Accepted ADR-010/016; verify Tasks 14/30 |
| OD-11 cache defaults | Provisional ADR-011; verify Tasks 24/29 |
| OD-12 scheduled priority | Accepted ADR-012/020; verify Task 26 |
| OD-13 reset mechanism | Accepted next-launch ADR-013 and Task 27 design; implement/verify Task 28 |

## Cross-cutting definition of done

Each implementation task preserves requirement IDs, adds production and negative tests together, runs targeted then full checks, stores measured evidence, reviews the diff for scope/security, updates status/traceability and commits only when green. Later/prohibited features remain absent. Failed gates route only through Task 90; repeated technical blockage uses Task 91.
