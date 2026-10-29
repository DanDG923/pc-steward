# 62 — Uninstall security review gate

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / Extra High |
| Speed / mode | Standard / Agent |
| Entry | Committed Task 61 report and pinned implementation/artifact. |
| Phase brief | [Phase E](../docs/requirements/V2_PHASE_E.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UNI-01–04; TM-03–04/07/13; FRA-14–16`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 62 — Uninstall security review gate.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Committed Task 61 report and pinned implementation/artifact.

**Files to inspect / expected change boundary:** `docs/review/; Tasks 60–61 implementation/evidence`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Independently review exact instance authority, exclusions/dependencies, scope/user binding, privilege/replay boundary, truthful verification/history/lifecycle and collateral-registration detection. Reject catalogue-ID-only targeting or invented safety. Review the disposable host plan separately from route code.

**Verification and failure cases:** Concrete findings and requirement/evidence table; missing enforcement or other-instance preservation proof blocks route. Task 90 then fresh repeat of 62 after failure; no author self-approval or implicit live permission.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Pass for explicit tested route/plan or fail; no removal until Task 63's separate exact approval.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
