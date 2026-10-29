# 54 — Updater review gate

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / Extra High |
| Speed / mode | Standard / Agent |
| Entry | Committed Task 53 report and matching immutable implementation snapshot. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-01–15; AC-UPD-01–11; TM-03–04/07/13`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 54 — Updater review gate.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Committed Task 53 report and matching immutable implementation snapshot.

**Files to inspect / expected change boundary:** `docs/review/; relevant implementation/tests from Tasks 49–53`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Independently review the pinned Phase C scope against requirements and ADR-025/026, including progress/mode/restart/batch authority, migration, security and measured performance. The task executor must be independent of the implementation author; do not silently treat an author self-check as this gate. No delegated reviewer/worktree is implied without user authorization.

**Verification and failure cases:** Require source lines, concrete trigger/impact, severity, correction and reproducible evidence. Mandatory missing/stale evidence is unproven; AC-UX-01–09 is Optional. Failed gate routes to Task 90 then repeats this same gate on a fresh snapshot.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Explicit pass with no blocking findings, or fail and bounded remediation list; no live actions.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
