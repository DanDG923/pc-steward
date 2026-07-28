# 10 — WinGet installed-inventory provider

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 09 complete |
| Expected commit | `feat: add read-only WinGet inventory provider` |

**Primary requirement references:** `FR-INV-05`, `FR-INV-06`, `FR-INV-08`, `AC-INV-01`, `AC-INV-04`, `AC-INV-05`, `AC-INV-06`, `AC-INV-07`

## Copy/paste prompt

```text
Task 10 — implement the read-only WinGet installed-inventory provider using the accepted OD-03 route.

Requirements are listed under Task 10 in `docs/requirements/TRACEABILITY.md`.

Read:
- `AGENTS.md`
- Phase 1 brief;
- WinGet ADR and spike report;
- relevant provider/process abstractions.

Implement:
- capability/version/source detection;
- installed-application collection only;
- executable plus structured argument-list construction if a process adapter is used;
- bounded timeout, cancellation, output-size limits, encoding handling, exit-code classification, and sanitized diagnostics;
- parser fixtures for all supported WinGet output variants established by the spike;
- local-only mode that performs no source refresh or online update check.

Do not invoke upgrade, install, uninstall, source reset, agreement changes, or arbitrary package commands. Never accept raw commands from configuration, storage, or metadata.

Run parser/unit tests, a safe read-only integration test, and the full suite. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
