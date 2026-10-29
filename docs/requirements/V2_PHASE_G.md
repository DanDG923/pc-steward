# PC Steward 2.0 Phase G — Optional advisory AI and app budgets

**Planning status:** Finalized in Task 47; applicable ADRs and final choices owner-Accepted on 2026-10-29 after both Opus 5.5 reviews and disclosed final author clarifications. Future tasks retain their own instruction/evidence/action gates; Phase B's independent authority exception remains. Read the [shared contract](V2_TASK_CONTRACT.md), [decisions](V2_PLANNING_DECISIONS.md) and [host-test plans](V2_HOST_TEST_PLANS.md) with this brief. Historical Phase 0–4 documents retain their original scope.

## Objective and boundaries

WS7 provides one OpenAI advisory explanation path with exact final preview/redaction, protected current-user key, bounded official-SDK transport and durable app-only spending control. Core remains keyless/usable. No agent/tools/chat/history prompts, other provider, automatic retries, account balance access, credit purchase/recharge or AI action authority.

**Requirement coverage:** `FR-AI-01–06; FR-BUD-01–04; FR-DAT-06–07/12–15; NFR-REL-02; NFR-MNT-01–05`.

**Entry decisions/evidence:** Accepted ADR-027/028/029 and aligned baseline privacy/lifecycle/persistence/composition/reset contracts; preceding phase gates or explicit order exception.

## Task order and deliverables

| Task | Deliverable | Prompt |
|---|---|---|
| 67 | Advisory AI privacy path with mocks | [Task 67](../../prompts/67_ADVISORY_AI_WITH_MOCKS.md) |
| 68 | Transactional budget and reconciliation | [Task 68](../../prompts/68_TRANSACTIONAL_AI_BUDGET.md) |
| 69 | AI privacy and cost evidence readiness | [Task 69](../../prompts/69_AI_EVIDENCE_READINESS.md) |
| 70 | AI security, privacy and cost review gate | [Task 70](../../prompts/70_AI_SECURITY_PRIVACY_COST_GATE.md) |
| 71 | One preview-approved live explanation | [Task 71](../../prompts/71_APPROVED_LIVE_AI_EXPLANATION.md) |

No task starts from this table alone. Standard/Agent; GPT-6.1 Sol/High for implementation/live/decisions, GPT-6.1 Terra/High for readiness, GPT-6.1 Sol/Extra High for independent gates. No delegation/worktree authority is implied. Use existing Task 90 remediation followed by a fresh repeat of the failed review; use Task 91 for escalation.

## Required tests and evidence

Exact approved https://api.openai.com:443 POST /v1/responses text-only non-streaming store:false, no redirects/tools/background/hidden counting/SDK retries; one cross-process request, 45-second overall/10-second connect. Editable 8,192/1,024 initial token defaults with proven coupled <=4 MiB complete request/response/saved-item allowances, 8 MiB aggregate content and 30-day source-specific content lifecycle. Editable initial $0.25/request/$200/month, no extra dollar maxima, zero disables, positive request<=month; prospective saves invalidate unsent approval and preserve commitments. Checked micro-USD, complete worst-case reservation, durable unique claim, retained uncertainty, reliable terminal-incomplete usage settlement independent of saving, immutable UTC period/tariff and 24-hour owner-confirmed rates. Exact current-user Credential Manager target PCSteward/OpenAI/ApiKey.v1, <=2,048 UTF-8 bytes; verified disconnect/reset/recovery deletion and no prompt/body/hash/key sinks. Current+previous12-month settled detail then dedup-safe compaction, unresolved retained; 10,000-row/16-MiB logical soft ceilings. Full reset removes all app accounting/key without external marker; prior-use declaration/recognized recovery and undetectable external-restore limitation remain honest. Test races/crash/clock/rollover/usage conflicts and measure physical resources.

Apply shared security/migration/performance/traceability and canonical build/full-suite requirements. Test data is synthetic and isolated. Windows coverage, provider enforcement and actual UI/process behavior require their own evidence; distinguish them from fixture logic. Preserve existing history/settings/OS data. Align baseline detailed design with accepted additive contracts before affected capability ships.

## Host-test and review boundary

Use the [exact-host plan](V2_HOST_TEST_PLANS.md) for any deployment/launch, read, install, removal or paid call. No prompt or passing security review authorizes an unapproved host action. Pin artifacts/reports, verify target identity/freshness and stop on drift. AC-UX-01–09 remains Optional; public distribution/signing gates remain separate.

## Exit and handoff

67–68 mocked implementation/required checks, committed 69, independent 70 pass and separately approved 71 real advisory/usage evidence within cumulative $1. Exact supported model/SDK/token/profile/tariff evidence and accounting continuity are prerequisites; test-mode exit/ordinary-use artifact requires another owner decision.

Record implementation versus review versus actual host evidence separately in STATUS/TRACEABILITY; do not automatically proceed to the next phase.
