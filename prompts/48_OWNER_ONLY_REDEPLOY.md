# 48 — Owner-only redeploy

**Planning status:** Independent WS1 exception: this task does not require A2/operation-ADR acceptance, but still requires its own exact artifact/deployment/launch authority. Preparation never starts it. Numerical/lifecycle choices for other phases are recorded in [decisions](../docs/requirements/V2_PLANNING_DECISIONS.md).

| Setting | Use |
|---|---|
| Model / reasoning | GPT-6.1 Sol / High |
| Speed / mode | Standard / Agent |
| Entry | Exact owner-approved artifact/deployment plan; independent WS1 route may run without A2 completion. |
| Phase brief | [Phase B](../docs/requirements/V2_PHASE_B.md) |
| Shared contract | [2.0 execution, evidence and authority](../docs/requirements/V2_TASK_CONTRACT.md) |

**Requirement references:** `FR-DAT-12–15; AC-DAT-18; FRA-14–16`. These refer to retained authoritative meanings, not new requirement IDs.

## Copy/paste prompt

Task 48 — Owner-only redeploy.

Read AGENTS.md, docs/STATUS.md, this prompt, its linked phase brief, the shared contract and only directly relevant accepted decisions/source. Verify the entry conditions against current evidence before changing anything. If a decision remains Proposed or a required capability is unproven, report the affected blocker; do not invent acceptance or silently widen scope. No subagents/worktrees unless separately authorized for this task.

**Entry:** Exact owner-approved artifact/deployment plan; independent WS1 route may run without A2 completion.

**Files to inspect / expected change boundary:** `packaging/; scripts/; docs/adr/ADR-022-OWNER-ONLY-PROTECTED-TEST-DEPLOYMENT.md`. Reuse established component seams and tests; narrow concrete edit paths/signatures after inspection. New file/type names must be documented with their consuming/producing interfaces before changing callers. Do not refactor unrelated modules.

**Deliverable:** Pin Task 42 implementation plus reviewed documentation-only successor; inspect existing installer/validator and exact hashes. Prepare preservation copy/integrity and rollback plan before seeking action-time authority. After approval, use the guarded existing deployment; compare complete manifest, signer/ACL/reparse protections and installed assembly hashes. Launch only with separately explicit approval; record responsiveness and close normally. Do not refresh sources or update software.

**Verification and failure cases:** Hash/database mismatch, validator/signing/ACL failure, unapproved root or failed launch stops the run. Record database preservation hash/integrity, exact artifact, validator output and launch evidence separately; installed Task 42 remediation is not a public release.

- [ ] Confirm requirements, prerequisites, exact baseline and approved boundaries; record proposed interfaces/schema changes before implementing them.
- [ ] For implementation tasks, add focused behavior fixtures that expose the missing behavior, then make the smallest coherent change and rerun them. Review/readiness/live/documentation tasks create evidence appropriate to their deliverable, without unrelated product changes.
- [ ] Follow the shared build/test, resource, migration, independent-review and exact-host authority rules. A skipped or unproven mandatory check remains open.
- [ ] Update STATUS and TRACEABILITY with exact commands/results, artifact/snapshot identity, unmet gates and safe next action; review diff and commit only after required checks pass and identity is available. Never push/release from this prompt.

**Exit:** Validator/hash/preservation evidence and separately approved launch evidence; otherwise report which gate remains open.

Return the repository's standard completion report. Stop at this numbered task; starting the next one requires its own instruction and gates. A failed review goes to existing Task 90 then a fresh repeat of the same review; a stuck task uses Task 91. Do not launch an implementation from this planning document automatically.
