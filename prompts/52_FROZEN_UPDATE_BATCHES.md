# 52 — Frozen reviewed update batches

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 51 committed; Q2 existing deselection/pin/ignore policy and Q3 warning policy retained. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-01–11/14; AC-UPD-01–11; FR-UI-01–07; FR-HIS-01–06; NFR-REL-03`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 52 — Frozen reviewed update batches.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 51 committed; Q2 existing deselection/pin/ignore policy and Q3 warning policy retained.

**Files to inspect / expected change boundary:** `src/PCSteward.Application/UpdateBatchWorkflow.cs; src/PCSteward.Application/ReviewedUpdateExecutionWorkflow.cs; src/PCSteward.Application/InventoryExperienceViewModel.cs; tests/PCSteward.Application.IntegrationTests/SequentialUpdateBatchTests.cs`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Add multi-select/Update All to existing review/coordinator. Freeze only reviewed eligible rows; require per-item fresh source/identity/mode/scope/evidence immediately before sequential launch. A new scan cannot append/retarget items. Repeat in-use detection; new warning needs new acknowledgment. Cancellation stops the next unstarted item, preserves completed/uncertain effects and never implies rollback.

**Verification and failure cases:** Test mixed providers/outcomes, excluded/stale/newly incompatible rows, new candidates after review, late warning, cancellation during each boundary, process crash before/after invocation and restart without automatic replay. Keep scan/reset lease ordering explicit.

Apply the owner-accepted review/dispatch policy from ADR-025: 30-minute frozen review with just-in-time two-minute helper requests; test a first installer lasting four minutes, a long ScheduledHost wait, exact expiry boundaries, fresh per-item evidence and no silent approval renewal. Do not reintroduce a two-minute expiry for waiting batch rows.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Per-item results, frozen selection, cancellation/recovery and existing warning controls pass focused and full gates.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
