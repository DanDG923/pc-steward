# 51 — Reliable restart outcomes

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 50 committed; exact provider restart semantics documented. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-05/08–11; AC-UPD-06–11; FR-HIS-01–06`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 51 — Reliable restart outcomes.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 50 committed; exact provider restart semantics documented.

**Files to inspect / expected change boundary:** `src/PCSteward.Execution/; src/PCSteward.Application/UpdateBatchWorkflow.cs; src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs; tests/PCSteward.Application.IntegrationTests/SequentialUpdateBatchTests.cs`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Carry typed reliable provider restart evidence through execution/helper/result/history/UI without inferring it from arbitrary text. Run targeted post-inventory. Keep verified update, reliable restart pending, still present/failed, cancelled and unknown distinct. Exit zero alone cannot produce verified Updated. Preserve partial effects after later failure.

**Verification and failure cases:** Test zero exit with old version, denied/partial inventory, spoofed restart text, reliable restart codes, nonzero failure, helper interruption and legacy history migration. No reboot or real update.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Operation-specific postconditions and truthful history/UI tested without restarting this PC.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
