# 49 — Truthful update progress

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | A2 decisions accepted; Phase C brief approved; Q1 progress and Q3 warning policies accepted. |
| Phase brief | [Phase C](../docs/requirements/V2_PHASE_C.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UPD-14; FR-UPD-01–11; FR-UI-01–07; NFR-OBS-01–05`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 49 — Truthful update progress.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** A2 decisions accepted; Phase C brief approved; Q1 progress and Q3 warning policies accepted.

**Files to inspect / expected change boundary:** `src/PCSteward.Application/ReviewedUpdateExecutionWorkflow.cs; src/PCSteward.Execution/ElevatedHelperProtocol.cs; src/PCSteward.Execution/WindowsElevatedHelperTransport.cs; src/PCSteward.Desktop/; tests/PCSteward.Security.FixtureTests/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Extend trusted typed job-correlated events through runner/helper, Application and existing Task 35 UI. Use accepted progress bounds and a new protocol version for changed semantics; allocate/document the exact version and closed schema before implementation. Represent review/preparing/observable execution/verification/final states; opaque installers remain indeterminate. Preserve the Q3 warning, disclose heuristic gaps and repeat before invocation.

**Verification and failure cases:** Test observable versus opaque progress, forged percentage, cross-job frames, duplicates/order/overflow, timeout/cancellation, late running app, inaccessible processes and saturation. Prove bounded queues/teardown, sanitized output and no provider launch before validation.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Focused fixtures, build/full suite, truthful UI contract and protocol compatibility evidence.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
