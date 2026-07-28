# 05 — Lock ADRs and architecture

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/0-foundation` |
| Run after | Tasks 02–04 complete |
| Expected commit | `docs: accept evidence-backed architecture decisions` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 05 — lock the architecture decisions using spike evidence. Do not write production code.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- all Phase 0 architecture documents;
- all three spike reports and their test/benchmark evidence;
- all proposed ADRs;
- specification sections 12–15, 18–24.

For OD-02, OD-03, OD-06, OD-11, and OD-13:
- choose one implementable approach;
- mark the ADR Accepted or Provisional;
- state rejected alternatives and the evidence;
- define concrete safety/performance tests and reconsideration triggers.

Also finalize ADRs for:
- solution/component boundaries;
- SQLite ownership and migrations;
- structured logging/redaction;
- update-job immutability;
- helper IPC and elevation;
- app-owned artifact roots;
- scheduling without a resident process.

Reconcile contradictions across documents. Update:
- `docs/architecture/COMPONENT_ARCHITECTURE.md`
- `docs/architecture/DATA_FLOW_AND_TRUST_BOUNDARIES.md`
- `docs/architecture/THREAT_MODEL.md`
- `docs/architecture/IMPLEMENTATION_PLAN.md`
- `docs/product/DECISION_LOG.md`
- `docs/STATUS.md`

The task is complete only when Task 06 can scaffold without making a new security or packaging decision.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
