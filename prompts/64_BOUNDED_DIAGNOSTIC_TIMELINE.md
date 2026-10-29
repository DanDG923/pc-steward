# 64 — Bounded local diagnostic timeline

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Prior phase gates complete or explicit owner-approved order exception; accepted ADR-027 limits; separate read-only exact-host access authority before any actual OS-record checks. |
| Phase brief | [Phase F](../docs/requirements/V2_PHASE_F.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-DIA-01–05; FR-DAT-06–07/12–15; FR-HIS-01–06; NFR-REL-02; NFR-OBS-01–05`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 64 — Bounded local diagnostic timeline.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Prior phase gates complete or explicit owner-approved order exception; accepted ADR-027 limits; separate read-only exact-host access authority before any actual OS-record checks.

**Files to inspect / expected change boundary:** `src/PCSteward.Domain/; src/PCSteward.Application/; src/PCSteward.Inventory.Windows/; src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs; src/PCSteward.Storage/FactoryReset.cs; src/PCSteward.Desktop/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Implement per-collection consent and five typed read-only source adapters with exact source-side mapping/bounds verified before support. Carry ADR-027 full quotas/time shares/schema and inclusive noncausal timeline rules; deny/unknown/all-denied remain visible. Persist redacted immutable whole snapshots; no raw logs/XML. Preview minimal bounded JSON export separately. Align accepted lifecycle/reset design and baseline ADR-010/013/014/015/016/019 detail; preserve OS records/exports/history and snapshot-owned captured history copies.

**Verification and failure cases:** Source saturation/irrelevant raw events, equal OS slots/deadlines, UTF-8/envelope limits, denied/empty/all-denied, time-zone/DST/unknown/future time, duplicates/ties/exact proximity boundaries, Kernel-Power noncausality, frozen replay, whole-snapshot expiry/capacity, cancellation/clear/reset/backup races, captured-copy confirmation and source-specific explanation clears.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Synthetic deterministic reader/migration/privacy/resource/full-suite evidence; host source coverage only from separately approved exact-host reads.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
