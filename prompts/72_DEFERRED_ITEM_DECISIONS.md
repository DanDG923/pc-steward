# 72 — Deferred-item decisions only

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Owner explicitly requests reconsideration; earlier phase status recorded; no feature execution from this prompt. |
| Phase brief | [Phase H](../docs/requirements/V2_PHASE_H.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-12; FR-ID-09; retained Later/Never markers`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 72 — Deferred-item decisions only.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Owner explicitly requests reconsideration; earlier phase status recorded; no feature execution from this prompt.

**Files to inspect / expected change boundary:** `docs/review/V2_DEPENDENCY_SERVICING_PROPOSAL.md; docs/product/PC_STEWARD_2_ROADMAP.md; docs/product/PC_STEWARD_2_DELTA.md; docs/requirements/TRACEABILITY.md`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Dependency candidates:** Windows App SDK 2.x migration and, only if Task 54A recommends deferral, the WinGet projection refresh. These are candidates for individually requested post-2.0 reconsideration, not accepted migrations or package versions. See [servicing plan](../docs/review/V2_DEPENDENCY_SERVICING_PROPOSAL.md). Retain the prototype archive and manifests; no archive removal is authorized by the servicing plan.

**Deliverable:** Revisit only individually named deferred items. For each, record accept/defer/reject, need, boundaries, requirements/ADRs/security/privacy/resource/host evidence implications and follow-on planning gate. Preserve the complete deferred list and prohibitions; WS5 remains deferred unless separately reopened. New scope requires explicit owner approval and new bounded design/tasks.

**Verification and failure cases:** Check no requirement marker changed silently, no generic cleanup/startup/health-score/dump/AI-command/Windows-reset or extra actionable provider slips into implementation. No code, package install, deployment or live host/paid action.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Owner decision record only, with no inferred implementation authority.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
