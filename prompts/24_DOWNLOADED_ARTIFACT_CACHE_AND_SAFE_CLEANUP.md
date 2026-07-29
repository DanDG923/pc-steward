# 24 — Downloaded-artifact cache and safe cleanup

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/3-execution` |
| Run after | Task 23 complete |
| Expected commit | `feat: manage and safely clear downloaded artifacts` |

**Primary requirement references:** `FR-DAT-08`, `FR-DAT-09`, `FR-DAT-10`, `FR-DAT-11`, `FR-DAT-13`, `FR-DAT-14`, `FR-DAT-15`, `NFR-PERF-10`, `AC-DAT-08`, `AC-DAT-09`, `AC-DAT-10`, `AC-DAT-11`, `AC-DAT-14`, `AC-DAT-15`, `AC-DAT-16`, `AC-DAT-17`, `AC-NFR-10`, `AC-UX-07`

## Copy/paste prompt

```text
Task 24 — implement PC Steward-owned downloaded-artifact tracking, quota/retention, and both downloaded-file cleanup buttons.

Requirements are listed under Task 24 in `docs/requirements/TRACEABILITY.md`.

Implement:
- a fixed per-user app-owned cache root and persisted artifact ownership records;
- active/inactive state, size, hash where available, job association, retention, last-cleaned time, and recoverable size;
- 2 GB soft quota with oldest-inactive eviction only;
- Settings **Clear downloaded files**;
- update-completion **Delete downloaded files** when files remain;
- exact UI disclosure that Windows, WinGet, Store, and other provider caches are untouched;
- canonical-root checks, handle-based/reparse-aware validation as defined by ADR, idempotence, locked-file partial results, and no false success;
- destructive-action lockout while dependent work is active.

Do not broaden deletion to provider-owned caches or accept any deletion path from metadata, package names, logs, or user text.

Reuse and promote the hostile deletion fixtures from the security spike. Add junction/symlink/reparse, path-swap, prefix-confusion, locked-file, quota, active-artifact, and preservation tests. Run static analysis and the full suite. Update threat model, status, and traceability.
```

## Completion rule

Do not proceed to Task 24A until this task is committed and `docs/STATUS.md` points to Task 24A. Review tasks must pass; otherwise run Task 90 and repeat the same review.
