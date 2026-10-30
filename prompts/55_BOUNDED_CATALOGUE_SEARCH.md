# 55 — Bounded catalogue and favourites

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Phase C gate passed and applicable phase handoff recorded; accepted ADR-023/025 and Q1/Q5 policies; Task 54A recommendation owner-accepted and either 54B completed with refreshed updater evidence/review or explicit projection deferral to Task 72 recorded. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-CAT-01–02/06; FR-SRC-02/07–10; NFR-PERF-03/08–09`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 55 — Bounded catalogue and favourites.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Phase C gate passed and applicable phase handoff recorded; accepted ADR-023/025 and Q1/Q5 policies; Task 54A recommendation owner-accepted and either 54B completed with refreshed updater evidence/review or explicit projection deferral to Task 72 recorded.

**Files to inspect / expected change boundary:** `src/PCSteward.Provider.WinGet/; src/PCSteward.Application/; src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs; src/PCSteward.Desktop/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Implement supported bounded COM catalogue discovery under accepted query/count/bytes/deadline/source rules. Prove bounded native enumeration before enabling a route. Show honest fields/tags only and Store information-only. Persist reversible favourites separately from inventory/curated membership under Q5; no install or update approval from results. Align ADR-015 detailed design and clear/reset disclosures before schema changes.

**Verification and failure cases:** Hostile/duplicate/fuzzy/oversize metadata, unavailable fields, exact authority-field rejection, non-BMP/escaping, disabled/revoked sources, cancelled/late results, stalled source and favourite capacity/migration/clear/reset fixtures. No owner-machine source query implied.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Bounded search/favourites and migration tests plus resource evidence; install remains unavailable until Task 56.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
