# 53 — Updater evidence readiness

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Terra / High |
| Speed / mode | Standard / Agent |
| Entry | 49–52 and 53A committed; WS5 decision recorded; 53A dependency delta and packaging evidence available. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-01–15; AC-UPD-01–11; FR-HIS-01–06; NFR-PERF-01–10; NFR-REL-01–05`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 53 — Updater evidence readiness.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 49–52 and 53A committed; WS5 decision recorded; 53A dependency delta and packaging evidence available.

**Files to inspect / expected change boundary:** `docs/review/; docs/requirements/TRACEABILITY.md; tests/; scripts/Benchmark.ps1`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Produce a committed Phase C evidence report pinned to implementation SHA and dirty-state hashes. Map every mandatory updater criterion to commands/results, migration fixtures, protocol/security negatives and measured ADR-006 resources; enumerate unsupported capabilities. Record WS5 deferral with scan-only ScheduledHost and no automation code. Cite Task 53A dependency/licence/NOTICE and exact-artifact evidence, distinguishing branch-local package pins from PR #8 on phase/4-beta and every installed payload.

**Verification and failure cases:** Include original F8/F10/source-consent regression tests, old/new protocol rejection, existing Chocolatey boundary, history migration and synthetic crash cases. Report cold/warm/cache/workload/sample counts and process-tree resources; fixture measurements are not live WinUI/provider proof.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Committed reproducible sanitized report; every unmet criterion has a blocker rather than an invented pass.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
