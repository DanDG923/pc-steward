# 91 — Escalate a stuck task

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `current task branch` |
| Run after | The current task stopped after two materially different attempts |
| Expected commit | `fix: resolve a documented stuck task` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 91 — resolve a stuck task from a compact failure handoff.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- the current phase brief;
- `docs/failures/<task-id>.md`;
- only the files and ADRs named in that failure report.

Do not restart the entire phase or redesign unrelated components. Reproduce the exact failure first. Identify the smallest root cause and choose one evidence-backed fix. If the failure reveals an ADR or specification conflict, stop after writing a proposed decision update rather than improvising a new architecture.

Implement the fix, add a regression test, run targeted and full relevant tests, review the diff, update the failure report with resolution evidence, status, and traceability. Commit only if green. Return to the original task’s completion criteria; do not begin the next task.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
