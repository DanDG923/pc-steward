# 19 — Manual decisions and update-intelligence UX

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/2-intelligence` |
| Run after | Task 18 complete |
| Expected commit | `feat: add mapping, pin, ignore, and attention UX` |

**Primary requirement references:** `FR-POL-01`, `FR-POL-02`, `FR-POL-03`, `FR-POL-04`, `FR-POL-05`, `FR-UI-01`, `FR-UI-02`, `FR-UI-03`, `FR-UI-04`, `FR-UI-05`, `FR-UI-06`, `FR-UI-07`, `FR-HIS-02`, `FR-HIS-04`, `AC-MAT-02`, `AC-MAT-07`, `AC-UX-01`, `AC-UX-02`, `AC-UX-03`, `AC-UX-04`, `AC-UX-05`, `AC-UX-06`

## Copy/paste prompt

```text
Task 19 — complete the update-intelligence user experience and stable user decisions.

Requirements are listed under Task 19 in `docs/requirements/TRACEABILITY.md`.

Implement:
- dashboard counts and attention-needed view;
- application details with installed/candidate metadata, source, stable package ID, confidence, explanation, freshness, architecture, channel, and reason an action is disabled;
- manual mapping create/review/delete against connector-supplied package IDs only;
- current-version/source-package pinning supported by the MVP;
- ignore indefinitely or until a date;
- optional reason, unified decisions settings view, and reversal;
- product timeline entries for scans and decisions;
- clear failure summaries and safe next actions.

Do not expose an executable Update action yet. Do not let UI state override matching safety.

Add view-model, persistence, decision-reapplication, and proportionate best-effort accessibility tests. Under ADR-021, missing `AC-UX-*` evidence is not a gate failure. Run the full suite and the zero-false-positive fixture test. Update status and traceability.
```

## Completion rule

Do not proceed to Task 19A until this task is committed and `docs/STATUS.md` points to Task 19A. Review tasks must pass; otherwise run Task 90 and repeat the same review.
