# 63 — One approved live uninstall

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 62 passed; exact current disposable registration and separate removal/action-time authority. |
| Phase brief | [Phase E](../docs/requirements/V2_PHASE_E.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UNI-01–04; FR-HIS-01–06; FRA-14–16`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 63 — One approved live uninstall.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 62 passed; exact current disposable registration and separate removal/action-time authority.

**Files to inspect / expected change boundary:** `docs/review/; approved artifact and read-only verification routes`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Execute one exact reviewed single-instance removal through approved supported route. Record fresh exclusions/target baseline, other registrations, review/UAC/provider outcome and complete selected absence query with preserved other instances. No registry fallback, second target, force-close, document/caches/leftover deletion or automatic reinstall.

**Verification and failure cases:** Stop on target/approval/artifact/visibility drift. Denied/partial inventory is Unknown, never absence; reliable restart pending is distinct. Unexpected collateral changes are a partial/unsafe outcome with preserved evidence, not Success.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Phase E closes only with verified selected absence and other-instance preservation on the exact live route.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
