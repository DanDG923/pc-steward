# PC Steward 2.0 Phase C — Updater gaps and automation decision

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

WS2 closes G1 truthful progress, G2 enforceable reviewed mode, G3 reliable restart propagation and G4 frozen multi-select/Update All. WS5 is a recorded deferral only; no automation implementation.

**Requirement coverage:** `FR-UPD-01–15; AC-UPD-01–11; FR-UI-01–07; FR-HIS-01–06; FR-SCH-01–05; NFR-REL-03`.

**Entry decisions/evidence:** ADR-017/018/020/022 and accepted applicable ADR-025/026; Q1 progress/Q3 warning policy.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 49 | Truthful update progress | [Task 49](../../prompts/49_TRUTHFUL_UPDATE_PROGRESS.md) |
| 50 | Enforceable reviewed installer mode | [Task 50](../../prompts/50_REVIEWED_INSTALLER_MODE.md) |
| 51 | Reliable restart outcomes | [Task 51](../../prompts/51_RELIABLE_RESTART_OUTCOMES.md) |
| 52 | Frozen reviewed update batches | [Task 52](../../prompts/52_FROZEN_UPDATE_BATCHES.md) |
| 53A | Dependency servicing before updater evidence (**Proposed 2026-10-30, not owner-accepted**; see [proposal](../review/V2_DEPENDENCY_SERVICING_PROPOSAL.md)) | [Task 53A](../../prompts/53A_DEPENDENCY_SERVICING.md) |
| 53 | Updater evidence readiness | [Task 53](../../prompts/53_UPDATER_EVIDENCE_READINESS.md) |
| 54 | Updater review gate | [Task 54](../../prompts/54_UPDATER_REVIEW_GATE.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Opaque versus observable progress; typed mode/capability mutation and old-protocol rejection; reliable restart versus zero-exit/unknown postcondition; mixed/stale/excluded batches, late process warning, crash/cancel and no replay. Preserve current trusted-source/Chocolatey/F8/F10 regressions.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

49–52 implementation and required checks committed, 53 readiness report committed, independent 54 passed, applicable handoff/merge rule fulfilled with explicit merge authority. No implied live update or deployment.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
