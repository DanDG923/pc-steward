# 29 — Accessibility, reliability, and performance hardening

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 28 complete |
| Expected commit | `perf: harden accessibility, reliability, and resource use` |

**Primary requirement references:** `NFR-PERF-01`, `NFR-PERF-02`, `NFR-PERF-03`, `NFR-PERF-04`, `NFR-PERF-05`, `NFR-PERF-06`, `NFR-PERF-07`, `NFR-PERF-08`, `NFR-PERF-09`, `NFR-PERF-10`, `NFR-REL-01`, `NFR-REL-02`, `NFR-REL-03`, `NFR-REL-04`, `NFR-REL-05`, `NFR-MNT-01`, `NFR-MNT-02`, `NFR-MNT-03`, `NFR-MNT-04`, `NFR-MNT-05`, `NFR-OBS-01`, `NFR-OBS-02`, `NFR-OBS-03`, `NFR-OBS-04`, `NFR-OBS-05`, `AC-NFR-01`, `AC-NFR-02`, `AC-NFR-03`, `AC-NFR-04`, `AC-NFR-05`, `AC-NFR-06`, `AC-NFR-07`, `AC-NFR-08`, `AC-NFR-09`, `AC-NFR-10`, `AC-NFR-11`, `AC-UX-01`, `AC-UX-02`, `AC-UX-03`, `AC-UX-04`, `AC-UX-05`, `AC-UX-06`, `AC-UX-07`, `AC-UX-08`, `AC-UX-09`

## Copy/paste prompt

```text
Task 29 — run the best-effort accessibility, reliability, cancellation, and resource-budget hardening pass.

Requirements are listed under Task 29 in `docs/requirements/TRACEABILITY.md`.

First measure; then change only evidenced bottlenecks or failures. Execute the documented protocols for:
- cold start;
- five-minute idle CPU, working set, disk, and network;
- 30 warmed local inventory runs with p95;
- 20 healthy fixture-backed online checks with p95;
- process-tree CPU/peak working set;
- cancellation acknowledgement and operable-state recovery;
- connector timeout isolation;
- second-scan rejection and concurrency bound;
- no resident process after close;
- scheduled-process exit;
- cache quota/active-artifact protection;
- crash/interruption last-known-good recovery.

Attempt keyboard, screen-reader naming, text scaling, high contrast, icon-plus-text, focus order, confirmation, and failure-message checks for every primary action. Under ADR-021, `AC-UX-01–09` are Optional — best effort; record any unproven item honestly but do not treat it as a phase or release-gate failure.

Store raw machine-readable results and a concise `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md`. Do not relax safety, matching precision, or destructive-operation controls to hit budgets. Fix failures, rerun affected protocols, and update status/traceability with measured—not assumed—results.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
