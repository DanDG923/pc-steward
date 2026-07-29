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

Also read `docs/DESIGN_DELIVERY_PLAN.md`, the approved native design package, and the current desktop resources. Apply the scheduled-check capability only through the shared visual system; this task owns Design Phase 2 shell work needed by Settings and Design Phase 6 scheduling/settings work. Onboarding, scan-state, applications and update-review redesign belong to Tasks 26B and 26C.

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

In the visual system:
- replace the legacy host with the safe NavigationView shell required for Overview, Scan, Applications, Updates, Activity, Privacy & data, Settings and About & support; preserve current workflows and use safe placeholders for pages assigned to later tasks;
- do not add Startup or general device Storage to production navigation;
- build the Settings schedule card using the approved cadence radio group, task status/freshness, notification disclosure and the exact text: `Checks only. Scheduled work never installs updates.`;
- retain active-operation visibility across navigation, use semantic status text plus icon/colour, and preserve focus/back state.

Add task-definition fixtures, create/update/delete/idempotence tests, overlap tests, no-install tests, process-exit measurements, navigation/back-state tests and theme/resource coverage for the changed Settings surface. Use a harmless short test schedule only with explicit approval, then remove it. Verify the changed screen at light/dark/high-contrast, keyboard, 100/150/200% text scale and 900 x 640; update status and traceability only with measured evidence.
```

## Completion rule

Do not proceed to Task 26B until this task is committed and `docs/STATUS.md` points to Task 26B. Review tasks must pass; otherwise run Task 90 and repeat the same review.
