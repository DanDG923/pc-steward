# 68 — Transactional budget and reconciliation

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 67 committed; accepted ADR-029; reviewed pure arithmetic/state schema and async-safe cross-process lease/lock ordering. |
| Phase brief | [Phase G](../docs/requirements/V2_PHASE_G.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-BUD-01–04; FR-AI-01/04/06; FR-DAT-12–15; NFR-REL-02`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 68 — Transactional budget and reconciliation.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 67 committed; accepted ADR-029; reviewed pure arithmetic/state schema and async-safe cross-process lease/lock ordering.

**Files to inspect / expected change boundary:** `src/PCSteward.Domain/; src/PCSteward.Application/PersistencePorts.cs; src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs; src/PCSteward.Storage/FactoryReset.cs; tests/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Implement checked micro-USD upward-rounded worst-case complete tariff/token/framing reservation and immutable request/period/rate/cap binding. Use Reserved -> DispatchClaimed -> Settled/Uncertain with unique durable one-shot claim and no automatic replay. Preserve uncertainty and exact cost above reservation; reliable terminal incomplete usage can settle independently of answer/storage. Implement owner-confirmed 24-hour prices, UTC rollover/clock checks, editable prospective caps/zero behavior, cumulative development-test envelope, compaction/dedup/soft ceilings and accepted honest reset/restore/prior-use declaration. No network billing/balance lookup or surviving reset marker.

**Verification and failure cases:** Exact-cap/one-unit-over/overflow; concurrent instances/edit/reserve/send; crashes before/after claim/send/settle; missing/duplicate/conflicting usage and billable no-text answer; stale/mismatched pricing/tokens; late settlement/month/DST/clock jumps; unknown test spend; reset/key rotation/content/history clears; recognized recovery and disclosed undetectable external restores; physical ledger growth.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Mocked accounting/full-suite evidence; no paid transport until independent 70 pass and Task 71 authority.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
