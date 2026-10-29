# 66 — Diagnostic review gate

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / Extra High |
| Speed / mode | Standard / Agent |
| Entry | Committed Task 65 report and matching implementation snapshot. |
| Phase brief | [Phase F](../docs/requirements/V2_PHASE_F.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-DIA-01–05; AC-DAT-06–07/12–17; NFR-MNT-01–05`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 66 — Diagnostic review gate.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Committed Task 65 report and matching implementation snapshot.

**Files to inspect / expected change boundary:** `docs/review/; Tasks 64–65 implementation/evidence`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Independent review of source-side bounds/consent/privacy, noncausal wording, denied-source honesty, exact field projections, deterministic times/correlation, immutable snapshot lifecycle/copies, app-only deletion/export and measured resource behavior.

**Verification and failure cases:** Mandatory missing evidence blocks; assess actual OS coverage only where approved host proof exists. No broad log reader/elevation, reset-root expansion or weakening of accepted R1. Task 90 followed by fresh 66 on failure.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Explicit pass or fail; diagnostics do not grant AI egress authority.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
