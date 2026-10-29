# 67 — Advisory AI privacy path with mocks

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Prior phase gates complete or explicit owner order exception; ADR-027/028/029 accepted; exact model/SDK/token/header/profile policy proven before support; dependency installation requires explicit approval. |
| Phase brief | [Phase G](../docs/requirements/V2_PHASE_G.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-AI-01–06; FR-BUD-01–04; FR-DIA-03–05; FR-DAT-06–07/12–15`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 67 — Advisory AI privacy path with mocks.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Prior phase gates complete or explicit owner order exception; ADR-027/028/029 accepted; exact model/SDK/token/header/profile policy proven before support; dependency installation requires explicit approval.

**Files to inspect / expected change boundary:** `src/PCSteward.Application/; src/PCSteward.Domain/; src/PCSteward.Desktop/; src/PCSteward.Persistence.Sqlite/; src/PCSteward.Storage/; tests/PCSteward.ArchitectureTests/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Create narrow advisory/credential ports and separate approved provider adapter under ADR-014 with no send/key capability in ScheduledHost/helper/action execution. Use official SDK via mocked controlled transport, exact final editable/redacted body/header preview, no tools/redirects/retries/hidden counting requests. Implement Credential Manager exact target through synthetic fake adapter tests; no real credential action now. Implement accepted coupled editable token/byte/output/save profiles, source provenance/retention/clear/reset and keyless core. Keep production paid dispatch disabled until Task 68 accounting and Task 70 review plus separate authority.

**Verification and failure cases:** Wire-body equality, SDK headers/retry controls, injection isolation, secret/path/control Unicode before every sink, malformed/oversize/compressed errors, no prompt/hash persistence, source-kind clears/captured copies, storage-failure versus usage status, exact key deletion recovery, model/framing/profile invalidity and architecture references.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Mocked privacy/transport/UI/lifecycle path passes focused/full checks; no paid call, model choice from memory or actual key write.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
