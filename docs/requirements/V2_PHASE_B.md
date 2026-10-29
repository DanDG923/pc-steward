# PC Steward 2.0 Phase B — Owner-only deployment

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

WS1 redeploys the Task 42 remediation using the existing protected owner-only route. This independent phase can run at any time only with its separate exact artifact/deployment/launch authority; it does not wait for A2 and does not close any release gate.

**Requirement coverage:** `FR-DAT-12–15; AC-DAT-18; FRA-14–16`.

**Entry decisions/evidence:** ADR-022; existing deployment and preservation evidence.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 48 | Owner-only redeploy | [Task 48](../../prompts/48_OWNER_ONLY_REDEPLOY.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Existing packaging/validator checks and database preservation; no real app update, source refresh or public distribution.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

Independent validator, exact installed hashes, preserved database integrity and separately approved responsive launch evidence. Unapproved launch or deployment remains pending.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
