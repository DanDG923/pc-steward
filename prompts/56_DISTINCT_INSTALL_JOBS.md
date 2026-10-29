# 56 — Distinct reviewed install jobs

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | 55 committed; supported exact API/version/scope/architecture/mode/absence capabilities proved per route; accepted ADR-023/025 and Q5. |
| Phase brief | [Phase D](../docs/requirements/V2_PHASE_D.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-CAT-03–06; FR-UPD-15; FR-HIS-01–06; FR-DAT-06–07/12–15; TM-03–04/07/13`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 56 — Distinct reviewed install jobs.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 55 committed; supported exact API/version/scope/architecture/mode/absence capabilities proved per route; accepted ADR-023/025 and Q5.

**Files to inspect / expected change boundary:** `src/PCSteward.Domain/; src/PCSteward.Application/; src/PCSteward.Provider.WinGet/; src/PCSteward.Execution/; src/PCSteward.ElevatedHelper/; src/PCSteward.Persistence.Sqlite/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Add closed Install variant, fresh complete relevant not-installed evidence, exact review and live approval binding. Independent helper validation cannot trust a database or isSafe flag. User route stays unelevated; machine route requires supported distinct protected capability. Execute sequentially with pre-launch revalidation, capacity reserved for history and operation lease. Verify expected target version/scope/architecture/user; derive recent installs only from verified results. Unknown routes stay disabled.

**Verification and failure cases:** Install/update/uninstall substitution; existing/stale/denied/mixed inventory; source/architecture/scope/mode drift; recomputed digest/replay/downgrade; unexpected UAC; queue cancel/crash/partial effects; postcondition mismatch and reliable restart distinct from verified install. History limit/clear/reset and legacy update preservation.

Define/version-test the deterministic plausible linked/unlinked identity-match predicate before enabling Install. Prove known empty/already-satisfied dependencies, bound explicit agreements, no-upgrade/no-repair invocation guard and supported scope/mode. Test uncertain claim plus external installation: installed state alone cannot attribute it to PC Steward or populate Recent installs. Carry ADR-025's finally accepted frozen-review/helper lifetime, cross-session exclusive gate, item deadline and residual-work barrier into crash/cancel/timeout fixtures.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Mocked route implementation, security/migration/full-suite passes and honest unavailable routes; no live install.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
