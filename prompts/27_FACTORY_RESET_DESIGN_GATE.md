# 27 — Factory-reset design gate

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Plan / read-only first** |
| Branch | `phase/4-beta` |
| Run after | Task 26 complete |
| Expected commit | `docs: finalize factory-reset design` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 27 — finalize the factory-reset and advanced data-control design before implementation. Do not change production code.

Read:
- `AGENTS.md`
- Phase 4 brief;
- OD-13 ADR and security spike;
- current persistence, schedule, logging, artifact, and settings implementations;
- specification sections 12.5, 15.5, 17.3, and relevant acceptance criteria.

Produce/update:
- an exact reset manifest listing every deleted and preserved category;
- operation ordering and transaction boundaries;
- active-work lockout/cancellation rules;
- locked-file and partial-failure behaviour;
- schedule removal;
- in-process restart versus marker/helper mechanism based on actual file-handle behaviour;
- first-launch verification;
- rollback/safe failure state;
- hostile reparse/path tests;
- uninstall retain/remove-data interaction.

Update the OD-13 ADR to Accepted with current evidence and create `docs/architecture/FACTORY_RESET_DESIGN.md`. The design must not require admin rights, delete binaries, delete provider-owned caches, or accept arbitrary paths.

Update status. Do not implement until the document has no unresolved security decision.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
