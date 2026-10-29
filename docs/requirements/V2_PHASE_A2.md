# PC Steward 2.0 Phase A2 — Decisions and executable task boundaries

**Current status: Phase A2 and Task 47 complete.** On 2026-10-29 the owner answered **“Accept the corrected ADRs and final choices”** after both requested Opus 5.5 reviews and disclosed final N1–N6 author follow-ups. ADR-023–025 are Accepted and ADR-026 is Accepted as C (Defer); numerical and final review/gate/deadline policies are recorded in [planning decisions](V2_PLANNING_DECISIONS.md). This phase creates documentation, not feature/deployment evidence.

## Inputs and task allocation

| Task | Result | Authority / current evidence |
|---|---|---|
| 44 | Accepted requirement deltas with retained IDs | [Accepted requirements record](../review/TASK_44_ACCEPTED_REQUIREMENTS.md); specification/traceability |
| 45 | Catalogue/install, instance-removal, typed jobs/helper and automation decisions drafted | [Task 45 record](../review/TASK_45_PROPOSED_OPERATION_ADRS.md) is historical; ADR-023–026 accepted in Task 47 after requested reviews/follow-ups |
| 46 | Diagnostic lifecycle, advisory AI and accounting decisions | [Task 46 record](../review/TASK_46_PROPOSED_DIAGNOSTIC_AI_BUDGET_ADRS.md); ADR-027–029 owner-Accepted |
| 47 | Bounded phase briefs, numbered prompts, requirement/evidence mapping, host-test plans and gates | This finalized package and [Task 47 record](../review/TASK_47_PHASE_BRIEFS_AND_PROMPTS.md); all applicable documentation/design acceptance complete |

## Package and phase handoffs

- [Shared execution/evidence/authority contract](V2_TASK_CONTRACT.md), [host-test plans](V2_HOST_TEST_PLANS.md), [remaining decisions](V2_PLANNING_DECISIONS.md), [numbered prompts](../../prompts/INDEX.md#pc-steward-20--task-47-planning-package).
- [B — owner-only deployment](V2_PHASE_B.md): 48; independent exact-authority WS1 exception retained.
- [C — updater gaps](V2_PHASE_C.md): 49–52, readiness 53, independent gate 54.
- [D — catalogue/install](V2_PHASE_D.md): 55–56, readiness 57, independent gate 58, separately approved live install 59.
- [E — single-instance uninstall](V2_PHASE_E.md): Q4 evidence before 60, readiness 61, independent gate 62, separately approved live removal 63.
- [F — diagnostics](V2_PHASE_F.md): 64, readiness 65, independent gate 66.
- [G — advisory AI/budget](V2_PHASE_G.md): mocked privacy 67, accounting 68, readiness 69, independent gate 70, separately approved live call 71.
- [H — deferred decisions](V2_PHASE_H.md): 72; no automatic implementation.

No number collision exists with historical prompts 00–31/90/91; Tasks 32–46 remain historical records rather than retroactively invented prompts. Task 90 remains remediation; Task 91 remains escalation. No new task is allocated for skip-version or process control. Phase order does not silently bypass a blocked live gate; any order exception needs explicit owner instruction. Branch/merge rules remain subject to exact repository/user authorization.

## Completion criteria

1. Owner acceptance of applicable ADRs and Q1/Q3/Q5 policies is recorded; Q4 unresolved route/fixture evidence blocks the affected route before implementation, rather than being claimed solved by documentation.
2. All 25 numbered prompts 48–72 have entry/deliverable/files/requirements/tests/exit/next-gate boundaries, correct model/reasoning and linked phase/shared/host contracts.
3. All 27 accepted new requirement IDs and retained updater/data/privacy/security/performance requirements have implementation/readiness/review/live evidence owners in traceability; future tests remain unproven.
4. Accepted diagnostic/AI/accounting additive amendments and deferred/prohibited scope survive the plan; no hidden authority for real actions.
5. Relative links, UTF-8, Markdown whitespace, task/phase/model allocation and requirement preservation pass static checks; canonical build/full suite pass under repository rules. Review complete diff for unrelated changes; preserve pre-existing Task 46 working-tree edits.

Owner review/acceptance and validation are recorded in the Task 47 report. No source behavior, package dependency, deployment, credential, user data, OS record, schedule, paid service or external repository state changed in this planning task. Task 48 retains exact artifact/deployment/launch authority; Task 49 implementation needs its own instruction and phase entry. No next task starts automatically.
