# PC Steward 2.0 Phase F — Bounded diagnostic timeline

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

WS6 implements manual per-collection-approved read-only OS/app history snapshots, visible partial coverage, deterministic nearby changes, redaction, retention, requested export and app-only deletion. No cause diagnosis, health score, dump parser, background collector, OS-record deletion or elevation.

**Requirement coverage:** `FR-DIA-01–05; FR-DAT-06–07/12–15; FR-HIS-01–06; AC-DAT-06–07/12–17; NFR-REL-02; NFR-OBS-01–05`.

**Entry decisions/evidence:** Accepted ADR-027 and additive ADR-010/013/014/015/016/019/reset contracts; preceding phase gates or explicit order exception; separate actual host-read authority.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 64 | Bounded local diagnostic timeline | [Task 64](../../prompts/64_BOUNDED_DIAGNOSTIC_TIMELINE.md) |
| 65 | Diagnostic evidence readiness | [Task 65](../../prompts/65_DIAGNOSTIC_EVIDENCE_READINESS.md) |
| 66 | Diagnostic review gate | [Task 66](../../prompts/66_DIAGNOSTIC_REVIEW_GATE.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Five sources; app history first. Seven-day lookback, 500 matching materialized/source, 2,000 overall; equal nonredistributed OS record slots and remaining-time shares per ADR-027. 4 KiB/record, 512-scalar optional summary, 8 MiB collection/export, 64 MiB retained payload, 60-second total, 15-second per-reader ceiling; source-side allowlists/filtering. Inclusive +/-15-minute known-time proximity, stable source-local dedup/no cross-source merge, ambiguous/DST/future times, saturation/denial/cancel. Whole snapshots expire at 30 days or earlier capacity/clear/reset; preserve immutable captured history copies on live history clear with disclosure. Verify source-kind explanation clears without budget effects and measure physical DB/WAL/backup overhead.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

64 implementation and focused/full checks, committed 65 evidence, independent 66 pass with honestly labelled exact-host source coverage and no mandatory privacy/lifecycle/resource blocker.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
