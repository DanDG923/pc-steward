# 69 — AI privacy and cost evidence readiness

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Terra / High |
| Speed / mode | Standard / Agent |
| Entry | 67–68 committed; exact provider/model/profile/pricing/SDK evidence assembled without paid calls. |
| Phase brief | [Phase G](../docs/requirements/V2_PHASE_G.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-AI-01–06; FR-BUD-01–04; FR-DAT-06–07/12–15; NFR-PERF-01–10`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 69 — AI privacy and cost evidence readiness.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 67–68 committed; exact provider/model/profile/pricing/SDK evidence assembled without paid calls.

**Files to inspect / expected change boundary:** `docs/review/; tests/; scripts/Benchmark.ps1`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Commit egress/header/wire-body/local-token/framing/profile/credential/reset/privacy/accounting/migration/crash/physical-storage/performance evidence. Capture exact supported model/tariff/configuration with official sources at implementation time; unproven model stays unavailable. Prepare one tiny synthetic-evidence paid-test plan with estimated complete bound, remaining cumulative $1 envelope, uncertainty/stop policy and exact preview; no account query or call.

**Verification and failure cases:** Prove 4-MiB request/response/item and 8-MiB content limits are compatible with every admitted setting, no SDK retries, one request across processes/45-second deadline/10-second connect, cost unaffected by save failure, accounting continuity and first-test build-policy separation. No secret/prompt in durable report.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Committed pinned report and reviewable live-test plan; insufficient envelope/profile/continuity stays blocked.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
