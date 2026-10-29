# 70 — AI security, privacy and cost review gate

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / Extra High |
| Speed / mode | Standard / Agent |
| Entry | Committed Task 69 report and matching immutable implementation/artifact. |
| Phase brief | [Phase G](../docs/requirements/V2_PHASE_G.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-AI-01–06; FR-BUD-01–04; TM-03–04/07/13; FRA-14–16`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 70 — AI security, privacy and cost review gate.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Committed Task 69 report and matching immutable implementation/artifact.

**Files to inspect / expected change boundary:** `docs/review/; Tasks 67–69 implementation/evidence`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Independent security/privacy/cost review of exact approved egress/payload/headers, credentials/reset, advisory isolation, model/profile/token bounds, editable settings, reservations/claim/settlement/recovery, physical resources and test-envelope plan. Compare accepted ADR-027–029 full amendments with baseline design alignment.

**Verification and failure cases:** Unresolved privacy/paid-bound/continuity risk blocks; no release, key operation or paid call from review. Require source lines/scenarios/fixtures and distinguish software proof from provider/host proof. Task 90 and fresh 70 after failure.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Explicit pass for exact model/route/artifact and test plan or fail; Task 71 still requires exact owner approval.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
