# 65 — Diagnostic evidence readiness

**Planning status:** Finalized in Task 47 after owner acceptance of ADR-023–026 and final choices. This task starts only on its own instruction and satisfied entry gates. Accepted numerical/lifecycle choices are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Terra / High |
| Speed / mode | Standard / Agent |
| Entry | 64 committed; actual host access evidence separately authorized or explicitly unproven. |
| Phase brief | [Phase F](../docs/requirements/V2_PHASE_F.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-DIA-01–05; FR-DAT-06–07/12–15; AC-DAT-06–07/12–17; NFR-PERF-01–10`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 65 — Diagnostic evidence readiness.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** 64 committed; actual host access evidence separately authorized or explicitly unproven.

**Files to inspect / expected change boundary:** `docs/review/; scripts/Benchmark.ps1; tests/`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Commit source-by-source schema/API/access and saturation/truncation evidence, privacy sink review, deterministic fixtures, physical DB/WAL/backup/migration/expiry/clear/reset evidence and measured resources. Record exact source coverage with available/denied/missing/unsupported/timed-out states, not a clean health claim.

**Verification and failure cases:** Compare seven-day/500-source/2,000-overall/8-MiB/64-MiB/60-second policies against measured output and overhead. Include immutable replay across live history clear, explanation provenance and no accounting refund. No host-read permission is inferred from readiness task.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Pinned reproducible report; missing coverage/performance or lifecycle proof is an explicit blocker.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
