# 58 — Install security review gate

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / Extra High |
| Speed / mode | Standard / Agent |
| Entry | Committed Task 57 report and matching implementation/artifact snapshot. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-CAT-01–06; TM-03–04/07/13; FRA-14–16`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 58 — Install security review gate.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Committed Task 57 report and matching implementation/artifact snapshot.

**Files to inspect / expected change boundary:** `docs/review/; Tasks 55–57 implementation/evidence`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Independent review of discovery versus authority, absence completeness, enforceable exact scope/mode, helper independence, old protocol rejection, source consent, history/lifecycle, teardown and proposed exact-host test. Check each advertised route; acceptance of one never certifies another.

**Verification and failure cases:** Findings need trigger/impact/line/fix; missing physical-resource/capability/migration/security evidence blocks applicable route. Task 90 fixes failures and a fresh repeat of 58 verifies them. No live install or action approval from review alone.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Explicit readiness pass for identified route/artifact/test plan or fail; Task 59 still needs exact owner authority.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
