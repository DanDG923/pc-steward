# 59 — One approved live install

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 58 passed for exact route/artifact; separate exact package/version/scope/effects/action-time approval and deployment/launch authority where needed. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-CAT-03–06; AC-UPD-06–11; FRA-14–16`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 59 — One approved live install.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 58 passed for exact route/artifact; separate exact package/version/scope/effects/action-time approval and deployment/launch authority where needed.

**Files to inspect / expected change boundary:** `docs/review/; approved protected artifact and existing validators`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Run the accepted host plan once for the exact disposable target. Capture baseline and review before authority; no substitution if already installed or capabilities change. Record approval, identity/version/scope/architecture/mode, UAC/provider evidence, selected post-inventory and other-instance preservation. No extra source refresh/app launch or association change outside plan.

**Verification and failure cases:** Stop on stale evidence, approval/artifact mismatch, unexpected effects/UAC, ambiguous target, unreliable postcondition or lost visibility. Retain uncertain/partial evidence; do not retry, roll back or remove automatically. Machine evidence proves no user route and vice versa.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Phase D closes only with verified live installed state for exact tested route; otherwise stays open.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
