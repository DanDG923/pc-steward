# 12 — Scan orchestration, cancellation, timings, and performance harness

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 11 complete |
| Expected commit | `feat: orchestrate cancellable, measured inventory scans` |

**Primary requirement references:** `FR-RSC-01`, `FR-RSC-02`, `FR-RSC-03`, `FR-RSC-05`, `FR-HIS-01`, `NFR-PERF-01`, `NFR-PERF-03`, `NFR-PERF-05`, `NFR-PERF-06`, `NFR-PERF-08`, `NFR-PERF-09`, `NFR-REL-02`, `NFR-MNT-01`, `NFR-MNT-04`, `NFR-OBS-01`, `NFR-OBS-02`, `AC-NFR-01`, `AC-NFR-03`, `AC-NFR-04`, `AC-NFR-06`, `AC-NFR-07`, `AC-NFR-09`

## Copy/paste prompt

```text
Task 12 — implement the local inventory scan orchestrator, transactional persistence, progress/cancellation, provider timing, and performance harness integration.

Requirements are listed under Task 12 in `docs/requirements/TRACEABILITY.md`.

Implement:
- exactly one active full scan;
- phases, elapsed time, correlation ID, provider timing/warnings, and cancellation;
- independent provider failure handling;
- bounded provider concurrency and internal configuration;
- transactional replacement of the last-known-good inventory only after a successful commit;
- cancellation acknowledgement and child-process termination semantics;
- no network activity in local-only scans;
- structured, bounded logs;
- benchmark entry points using the established 300-record fixtures.

Do not add source refresh, update matching, or update execution.

Run:
- targeted orchestration/failure/cancellation tests;
- crash/interruption persistence tests;
- at least the documented warm-up plus local benchmark sample sufficient to detect gross regressions;
- CPU/memory/idle instrumentation where the current shell permits it.

Record raw benchmark evidence and clearly distinguish measured pass, measured fail, and not-yet-measurable UI criteria. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
