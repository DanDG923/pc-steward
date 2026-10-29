# 50 — Enforceable reviewed installer mode

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 49 committed; per-provider supported mode capability evidenced; accepted ADR-025. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-15; FR-UPD-01; AC-UPD-01–11; TM-03–04/07/13`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 50 — Enforceable reviewed installer mode.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 49 committed; per-provider supported mode capability evidenced; accepted ADR-025.

**Files to inspect / expected change boundary:** `src/PCSteward.Domain/UpdateJobs.cs; src/PCSteward.Application/UpdateJobWorkflow.cs; src/PCSteward.Execution/NonElevatedUpdateRunner.cs; src/PCSteward.Execution/ElevatedUpdateRunner.cs; src/PCSteward.ElevatedHelper/Program.cs`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Define a closed silent/interactive mode, source/provider capability matrix and immutable canonical serialization. Freeze supported mode in review/digest/in-memory approval and independently validate in each runner/helper. Unknown mode or unsupported provider combination is unavailable; no arbitrary flags, silent fallback, user-to-machine retry or protocol downgrade. Preserve existing constrained Chocolatey update scope.

**Verification and failure cases:** Mutate mode with recomputed digest, operation, provider, scope, approval and capability; old/new deployment mismatch must reject. Verify actual fixed provider mapping through fake process/API adapters and document any host-only enforcement claim as unproven.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Supported combinations only; mode survives review/dispatch/history with positive and negative tests.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
