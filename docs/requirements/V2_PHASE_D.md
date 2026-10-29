# PC Steward 2.0 Phase D — Catalogue and manual installation

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

WS3 adds bounded consented COM discovery, honest metadata/tags/favourites and distinct reviewed sequential installs. Store stays information-only and Chocolatey install is excluded. Route support is earned separately for exact user/machine capability; absent proof stays unavailable.

**Requirement coverage:** `FR-CAT-01–06; FR-UPD-15; FR-SRC-02/07–10; FR-HIS-01–06; FR-DAT-06–07/12–15`.

**Entry decisions/evidence:** Accepted ADR-023/025, Q1 catalogue/progress and Q5 history/favourites policies, source consent; C gate/handoff.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 55 | Bounded catalogue and favourites | [Task 55](../../prompts/55_BOUNDED_CATALOGUE_SEARCH.md) |
| 56 | Distinct reviewed install jobs | [Task 56](../../prompts/56_DISTINCT_INSTALL_JOBS.md) |
| 57 | Install evidence readiness | [Task 57](../../prompts/57_INSTALL_EVIDENCE_READINESS.md) |
| 58 | Install security review gate | [Task 58](../../prompts/58_INSTALL_SECURITY_REVIEW_GATE.md) |
| 59 | One approved live install | [Task 59](../../prompts/59_APPROVED_LIVE_INSTALL.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Native/output bounds, hostile/fuzzy/duplicate/missing metadata, favourites/history capacities, fresh complete absence, operation substitution, independent helper validation, enforceable scope/mode/version/architecture, sequential cancellation/partial/crash and installed-state verification. Physical storage/resource/migration evidence required.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

55–56 checked implementation, committed 57, independent 58 pass and separately approved 59 verified live installation. Security readiness alone cannot close Phase D or authorize install.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
