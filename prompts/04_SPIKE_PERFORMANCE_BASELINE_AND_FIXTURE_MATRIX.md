# 04 — Spike: performance baseline and fixture matrix

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/0-foundation` |
| Run after | Task 01 complete |
| Expected commit | `spike: establish performance baseline and fixture matrix` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 04 — establish the repeatable performance baseline and deterministic software fixture matrix for OD-06.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_0.md`
- specification sections 18 and 19
- `docs/environment/ENVIRONMENT_REPORT.md`
- `docs/architecture/TEST_STRATEGY.md`

Create `spikes/performance-fixtures/` containing:
- deterministic fixture generators for at least 300 provider records;
- normal, malformed, missing-field, duplicate, user/machine, x86/x64, runtime, prerelease, incomparable-version, and false-match cases;
- a benchmark harness that can measure elapsed time, process-tree CPU, working set, cancellation acknowledgement, and bounded concurrency without requiring the future UI;
- scripts or documented commands for 30 warmed local-scan runs and 20 healthy online-check runs;
- a redacted baseline-system capture;
- machine-readable benchmark output.

Do not claim the final application meets a budget based on spike code. Do not tune by weakening accuracy, safety, diagnostics, or cancellation.

Create:
- `spikes/performance-fixtures/BASELINE.md`
- `spikes/performance-fixtures/FIXTURE_CATALOG.md`
- `spikes/performance-fixtures/BENCHMARK_PROTOCOL.md`
- a concise evidence update to the OD-06 ADR.

Run the harness, preserve raw result files, summarize median/p95 and measurement limitations, and update `docs/STATUS.md`.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
