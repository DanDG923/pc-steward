# 26 — Scheduled checks and Windows notifications

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Phase 3 gate passed and merged |
| Expected commit | `feat: add scheduled checks and notifications` |

**Primary requirement references:** `FR-SCH-01`, `FR-SCH-02`, `FR-SCH-03`, `FR-SCH-04`, `FR-SCH-05`, `FR-RSC-04`, `NFR-PERF-07`, `AC-NFR-08`, `AC-NFR-11`, `AC-UX-02`, `AC-UX-04`, `AC-UX-05`

## Copy/paste prompt

```text
Task 26 — implement optional scheduled checks and Windows notifications without a resident process.

Requirements are listed under Task 26 in `docs/requirements/TRACEABILITY.md`.

Implement:
- manual-only, daily, and weekly current-user schedules;
- Windows Task Scheduler registration/update/removal through a typed adapter;
- scheduled checks that scan/check only and never install;
- lower process priority and Task Scheduler conditions where supported by the accepted ADR;
- independent schedule and notification toggles;
- notification only for attention-needed results or repeated scan failure;
- single-instance/overlap protection;
- exit after completion, cancellation, or final timeout;
- no tray app, service, or background coordinator.

Add task-definition fixtures, create/update/delete/idempotence tests, overlap tests, no-install tests, and process-exit measurements. Use a harmless short test schedule only with explicit approval, then remove it. Update status and traceability.
```

## Completion rule

Do not proceed to Task 26A until this task is committed and `docs/STATUS.md` points to Task 26A. Review tasks must pass; otherwise run Task 90 and repeat the same review.
