# 23 — Sequential batches, post-update verification, history, and diagnostics

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/3-execution` |
| Run after | Tasks 21–22 complete |
| Expected commit | `feat: add batches, verification, history, and diagnostics` |

**Primary requirement references:** `FR-UPD-05`, `FR-UPD-06`, `FR-UPD-08`, `FR-UPD-09`, `FR-UPD-10`, `FR-UPD-11`, `FR-HIS-01`, `FR-HIS-02`, `FR-HIS-03`, `FR-HIS-04`, `FR-HIS-05`, `FR-HIS-06`, `NFR-REL-03`, `NFR-OBS-01`, `NFR-OBS-02`, `NFR-OBS-03`, `NFR-OBS-04`, `NFR-OBS-05`, `AC-UPD-06`, `AC-UPD-07`, `AC-UPD-08`, `AC-UPD-09`, `AC-UPD-10`, `AC-UPD-11`, `AC-UX-05`

## Copy/paste prompt

```text
Task 23 — implement application-running warnings, sequential batches, cancellation between items, post-update verification, history, and sanitized diagnostics.

Requirements are listed under Task 23 in `docs/requirements/TRACEABILITY.md`.

Implement:
- running-process detection as a warning, never an unapproved force close;
- explicit consent before any close action;
- sequential execution with independent per-item states;
- cancellation that prevents the next unstarted job while preserving completed results;
- target re-inventory after provider exit;
- Success only when the expected version is detected, or a reliable restart-pending state is returned;
- Inconclusive/Restart required for provider success without version proof;
- product timeline and job history;
- source/download/installer/elevation/timeout/verification/cancellation distinctions;
- bounded retention and sanitized diagnostic bundle.

Use controlled fixtures or disposable VM packages; do not update unrelated real applications. Add crash/restart recovery tests and negative provider-result tests. Run the full suite and update status/traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
