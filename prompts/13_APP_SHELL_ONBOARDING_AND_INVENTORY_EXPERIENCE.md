# 13 — App shell, onboarding, and inventory experience

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Task 12 complete |
| Expected commit | `feat: add onboarding and inventory experience` |

**Primary requirement references:** `FR-ONB-01`, `FR-ONB-02`, `FR-ONB-03`, `FR-ONB-04`, `FR-UI-02`, `FR-UI-03`, `FR-UI-05`, `FR-UI-07`, `FR-RSC-01`, `AC-UX-01`, `AC-UX-02`, `AC-UX-03`, `AC-UX-04`, `AC-UX-06`, `AC-UX-09`

## Copy/paste prompt

```text
Task 13 — implement the first-launch, local-only inventory user journey and inventory experience.

Requirements are listed under Task 13 in `docs/requirements/TRACEABILITY.md`.

Implement:
- first-run explanation of local data, connector/network behaviour, and permissions;
- local-only first scan as the default safe path;
- enabled-connector display and nonessential connector toggles;
- app navigation and responsive scan progress with phase, elapsed time, and Cancel scan;
- inventory list with search, sorting, status/source/scope filters as far as Phase 1 data permits;
- application details showing installation instances, raw-provider provenance, and freshness;
- clear live/local-only/cached terminology, although online cache is not implemented yet;
- keyboard, text-scaling, high-contrast, icon-plus-text status basics.

Do not expose an Update button, fabricate update statuses, implement source refresh, or request elevation.

Add view-model tests and the narrowest practical UI/integration tests. Manually exercise the first scan on the local test environment without changing the PC. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
