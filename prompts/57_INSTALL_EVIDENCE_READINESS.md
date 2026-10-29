# 57 — Install evidence readiness

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Terra / High |
| Speed / mode | Standard / Agent |
| Entry | 55–56 committed; disposable host-test plan can be reviewed. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-CAT-01–06; FR-DAT-06–07/12–15; NFR-PERF-01–10; TM-03–04/07/13`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 57 — Install evidence readiness.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 55–56 committed; disposable host-test plan can be reviewed.

**Files to inspect / expected change boundary:** `docs/review/; packaging/; scripts/; tests/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Commit evidence for each install route separately: API capability, protocol/privilege/migration/security, physical DB/WAL growth and measured UI/process resources. Prepare exact-artifact deployment/host plan using D3 7zip.7zip only if fresh absence/disposability/version/scope/effects can be established under separate approved reads; no query or execution merely to fill the report. If already relied on, request another disposable fixture.

**Verification and failure cases:** Use shared host-test contract: exact source/ID/version/architecture/scope/mode, hashes, expected UAC/effects/integrations, preservation baseline, stop conditions, outcome/re-inventory and no automatic rollback. Existing protected deployment is update-only until separately replaced/validated.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Pinned committed readiness report; unproven user/machine routes labelled blocked; no package install.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
