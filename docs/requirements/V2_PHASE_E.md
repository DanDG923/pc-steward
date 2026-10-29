# PC Steward 2.0 Phase E — Single-instance manual removal

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

WS4 permits one supported provider-enforceable installed instance after fresh targeting/exclusion proof. WinGet and current-user MSIX are independently gated; no catalogue-ID-only selection, all-user/provisioned/dependent/system/framework removal, registry commands, force close, bulk uninstall or leftovers.

**Requirement coverage:** `FR-UNI-01–04; FR-HIS-01–06; FR-DAT-06–07/12–15; TM-03–04/07/13`.

**Entry decisions/evidence:** Accepted ADR-024/025 and Q5; completed Phase D; owner-reviewed Q4 API/typed-field/exclusion/disposable-fixture matrix before 60.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 60 | Instance-bound manual uninstall | [Task 60](../../prompts/60_INSTANCE_BOUND_UNINSTALL.md) |
| 61 | Uninstall evidence readiness | [Task 61](../../prompts/61_UNINSTALL_EVIDENCE_READINESS.md) |
| 62 | Uninstall security review gate | [Task 62](../../prompts/62_UNINSTALL_SECURITY_REVIEW_GATE.md) |
| 63 | One approved live uninstall | [Task 63](../../prompts/63_APPROVED_LIVE_UNINSTALL.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Duplicate/mixed-user/scope/architecture, unknown/protected/dependency status and races, stale/denied inventory, wrong-operation/user replay, scope/UAC drift, target-still-present/other-instance disappearance, crash/cancellation, history capacity/migration/reset. No host feasibility inferred from mock success.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

60 checked implementation for supported routes, committed 61, independent 62 pass and separately approved 63 selected absence/other-instance preservation. If no viable route/fixture exists, keep E open; later phases require explicit owner order exception rather than silently bypassing it.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
