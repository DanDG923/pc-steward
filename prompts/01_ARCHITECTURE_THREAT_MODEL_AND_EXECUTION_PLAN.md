# 01 — Architecture, threat model, and execution plan

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Plan / read-only first** |
| Branch | `phase/0-foundation` |
| Run after | Task 00 complete |
| Expected commit | `docs: define PC Steward architecture and threat model` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 01 — produce the implementation architecture, threat model, and execution plan. Use Plan mode. Do not write production or spike code.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- `docs/environment/ENVIRONMENT_REPORT.md`
- the full product specification once
- `docs/product/DECISION_LOG.md`

Create:
1. `docs/architecture/COMPONENT_ARCHITECTURE.md`
2. `docs/architecture/DATA_FLOW_AND_TRUST_BOUNDARIES.md`
3. `docs/architecture/THREAT_MODEL.md`
4. `docs/architecture/TEST_STRATEGY.md`
5. `docs/architecture/IMPLEMENTATION_PLAN.md`
6. proposed ADR files for every open decision in specification section 22, using `docs/adr/ADR_TEMPLATE.md`

The architecture must explicitly separate:
- UI;
- application orchestration;
- domain rules;
- SQLite persistence and migrations;
- inventory providers;
- update-source connectors;
- version and matching engines;
- non-elevated update runner;
- short-lived elevated helper;
- downloaded-artifact ownership and deletion;
- scheduling/notifications;
- diagnostics and performance harnesses.

The threat model must cover malicious source metadata, incorrect product matching, arbitrary command execution, UAC/IPC abuse, reparse-point deletion escape, TOCTOU, database tampering, log leakage, stale cache, cancellation/crash recovery, and resource exhaustion.

For each ADR, list the evidence the upcoming spike must collect. Do not mark an ADR Accepted yet. Update `docs/STATUS.md` with the planned spike order and unresolved decisions.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
