# 28 — Factory reset and advanced data controls

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 27 complete |
| Expected commit | `feat: add factory reset and advanced data controls` |

**Primary requirement references:** `FR-DAT-04`, `FR-DAT-12`, `FR-DAT-13`, `FR-DAT-14`, `FR-DAT-15`, `AC-DAT-12`, `AC-DAT-13`, `AC-DAT-14`, `AC-DAT-15`, `AC-DAT-16`, `AC-DAT-18`, `AC-UX-02`, `AC-UX-04`, `AC-UX-07`, `AC-UX-08`

## Copy/paste prompt

```text
Task 28 — implement advanced data controls and Factory reset PC Steward exactly from the accepted reset design.

Requirements are listed under Task 28 in `docs/requirements/TRACEABILITY.md`.

Implement:
- independent advanced clearing of source metadata, logs, update history, and manual mappings/policies;
- a visually separate Factory reset control;
- first confirmation explaining all categories;
- a second explicit confirmation resistant to accidental keyboard activation;
- active-work lockout;
- deletion of database, inventory, source metadata, logs, history, app-owned artifacts, settings, consent, user decisions, and current-user scheduled task;
- preservation of installed binaries and all provider-owned caches;
- accurate partial failure and no false success;
- restart/close and verified first-run onboarding;
- idempotence and no elevation.

Reuse the allowlisted-root deletion subsystem; do not create a broader delete primitive. Add full integration tests, hostile reparse/path fixtures, locked-file tests, scheduled-task cleanup, restart verification, and uninstall interaction tests where package tooling permits.

Run static analysis and the full suite. Update threat model, status, and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
