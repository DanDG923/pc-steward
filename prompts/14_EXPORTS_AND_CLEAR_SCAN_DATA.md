# 14 — Exports and Clear scan data

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 13 complete |
| Expected commit | `feat: export inventory and clear scan data` |

**Primary requirement references:** `FR-DAT-01`, `FR-DAT-02`, `FR-DAT-03`, `FR-DAT-06`, `FR-DAT-07`, `FR-DAT-10`, `FR-DAT-13`, `FR-DAT-14`, `FR-DAT-15`, `AC-DAT-01`, `AC-DAT-02`, `AC-DAT-03`, `AC-DAT-05`, `AC-DAT-06`, `AC-DAT-07`, `AC-DAT-14`, `AC-DAT-15`, `AC-DAT-17`, `AC-UX-02`, `AC-UX-07`

## Copy/paste prompt

```text
Task 14 — implement inventory export and the dedicated Clear scan data lifecycle.

Requirements are listed under Task 14 in `docs/requirements/TRACEABILITY.md`.

Implement:
- CSV and JSON current-inventory exports with export timestamp and privacy warning;
- optional update-history field in the JSON schema, empty until Phase 3;
- a Data & reset page that clearly separates routine cleanup controls from future Factory reset;
- current scan-data size/last-cleared metadata;
- Clear scan data confirmation, operation lockout during an active scan/update, transactional deletion, idempotence, and accurate partial-failure reporting;
- exact preservation of settings, enabled connectors, schedule metadata, update-job history, downloaded artifacts, manual mappings, pins, and ignores;
- dashboard return to a no-scan state;
- reapplication of stable decisions after a later rediscovery.

Do not implement Clear downloaded files or Factory reset in this task; show them only as non-actionable future controls if the UX needs layout continuity.

Add persistence/integration tests proving delete/preserve semantics, offline operation, export fidelity, and no elevation. Run the full suite and update status/traceability.
```

## Completion rule

Do not proceed to Task 14A until this task is committed and `docs/STATUS.md` points to Task 14A. Review tasks must pass; otherwise run Task 90 and repeat the same review.
