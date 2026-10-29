# PC Steward 2.0 Phase H — Deferred capability decisions

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

Reconsider only individually owner-selected Later items from the complete roadmap list. Record accept/defer/reject and new design/requirement/test needs. This phase changes no product behavior and does not erase Never prohibitions.

**Requirement coverage:** `Retained Later/Never markers; FR-UPD-12; FR-ID-09`.

**Entry decisions/evidence:** Existing accepted roadmap/spec, prior phase outcomes and explicit owner reconsideration instruction.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 72 | Deferred-item decisions only | [Task 72](../../prompts/72_DEFERRED_ITEM_DECISIONS.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Compare all marker changes against explicit owner decisions, retain historical IDs and full deferred/prohibited list; no silently authorized system or paid action.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

Owner decision record and bounded follow-on planning needs only; no implementation without new scope/design/task approval.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
