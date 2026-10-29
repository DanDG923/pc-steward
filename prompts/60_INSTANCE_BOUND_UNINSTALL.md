# 60 — Instance-bound manual uninstall

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Phase D complete; accepted ADR-024/025 and Q5; owner-reviewed Q4 API/exclusion/fixture matrix and viable host plan. A route without proof stays deferred. |
| Phase brief | [Phase E](../docs/requirements/V2_PHASE_E.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-UNI-01–04; FR-HIS-01–06; FR-DAT-06–07/12–15; TM-03–04/07/13`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 60 — Instance-bound manual uninstall.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Phase D complete; accepted ADR-024/025 and Q5; owner-reviewed Q4 API/exclusion/fixture matrix and viable host plan. A route without proof stays deferred.

**Files to inspect / expected change boundary:** `src/PCSteward.Domain/; src/PCSteward.Application/; src/PCSteward.Provider.WinGet/; src/PCSteward.Inventory.Windows/MsixInventoryProvider.cs; src/PCSteward.Execution/; src/PCSteward.Persistence.Sqlite/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Before enabling any route, prove supported typed targeting fields enforce one registration/user/scope/architecture and complete exclusions. Add distinct single-target Uninstall variant/manual review and independent runner/helper fresh validation. Current-user MSIX rejects protected/system/framework/dependency/provisioned/all-user/unknown packages and never elevates. Never run registry uninstall strings, force-close, batch uninstall or clean leftovers. Reserve history capacity and compare selected absence plus other-instance preservation.

**Verification and failure cases:** Exact-ID-but-unenforceable selection, duplicate/mixed instances/users, stale/denied/partial inventory, dependency race, protected classifications, wrong-operation replay, UAC denial, target drift, exit-zero/still-present, other-instance disappearance and uncertain crash. New history migration/clear/reset preserves existing updates and accounting.

Add exact self/provider/runtime exclusion identities, MSIX app-data impact warning, helper-only checks only on helper routes and complete unsupported classification fixtures. Apply the finally accepted ADR-025 single-target review/helper lifetime, cross-session common exclusive gate, new item deadline/bounded teardown and durable unresolved-work barrier. Re-inventory cannot authorize replay.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Supported route only, focused/full gates and explicit unproven routes. No live removal. If no viable route/fixture exists, record blocker and leave Phase E open.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
