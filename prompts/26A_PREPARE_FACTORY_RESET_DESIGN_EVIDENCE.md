# 26A — Prepare factory-reset design-gate evidence

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/4-beta` |
| Run after | Task 26 complete |
| Expected commit | `docs: prepare factory-reset design evidence` |

**Primary requirement references:** `FR-DAT-04/12–15`, `AC-DAT-12–16/18`, `AC-UX-07–08`, ADR-013, ADR-019, and the Phase 4 reset scope.

## Copy/paste prompt

```text
Task 26A — prepare evidence for the Task 27 factory-reset design gate. Do not implement factory reset or grade the design.

Read AGENTS.md, STATUS.md, the Phase 4 brief, ADR-013 and ADR-019, the security spike, current persistence/schedule/logging/artifact roots, and Task 27. Record the current-state inventory and safe design constraints needed for a reviewable reset manifest: owned roots, retained categories, active-work locks, scheduled-task ownership, file-handle/restart observations, and hostile reparse/path coverage.

Create docs/review/FACTORY_RESET_DESIGN_EVIDENCE_READINESS.md with each Task 27 deliverable, the supporting source/command/evidence, status (Proven, Missing, or Approved exception), and an exact next action. No destructive action may run outside disposable allowlisted roots. Update traceability and status. Do not proceed to Task 27 unless every required design input is Proven or carries an explicit product-owner exception. Commit the evidence package.
```

## Completion rule

Do not proceed to Task 27 until this evidence task is committed and `docs/STATUS.md` points to Task 27.
