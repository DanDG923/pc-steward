# 21 — Immutable update jobs and constrained non-elevated runner

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/3-execution` |
| Run after | Phase 2 gate passed and merged; accepted update-execution and helper ADRs exist |
| Expected commit | `feat: add immutable update jobs and constrained runner` |

**Primary requirement references:** `FR-UPD-01`, `FR-UPD-02`, `FR-UPD-03`, `FR-UPD-04`, `FR-UPD-07`, `NFR-MNT-03`, `NFR-OBS-01`, `NFR-OBS-02`, `NFR-OBS-03`, `AC-UPD-01`, `AC-UPD-02`, `AC-UPD-03`, `AC-UPD-04`, `AC-UPD-05`, `AC-UPD-07`, `AC-UPD-11`

## Copy/paste prompt

```text
Task 21 — implement immutable, user-approved update jobs and the constrained non-elevated WinGet runner.

Requirements are listed under Task 21 in `docs/requirements/TRACEABILITY.md`.

Before editing, confirm the accepted update-execution and helper ADRs still match the current connector implementation.

Implement:
- immutable job snapshot containing provider, stable package ID, installed/candidate versions, scope, architecture, source, approval timestamp, and correlation ID;
- review/confirmation state required before execution;
- provider-owned operation selection, with executable and argument list constructed only from allowlisted code paths and validated enum/ID values;
- timeout, cancellation, bounded output, exit-code classification, sanitized logs, and denial states;
- non-elevated execution for operations that do not require elevation;
- structural rejection of arbitrary commands, executable paths, working directories, environment injection, or database/metadata-supplied arguments.

Do not add batch execution, UAC helper implementation, direct installer downloads, or post-verification beyond interfaces.

Add negative tests that attempt command/path/argument injection and prove rejection. Run targeted and full tests. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
