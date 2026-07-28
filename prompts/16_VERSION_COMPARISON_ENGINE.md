# 16 — Version comparison engine

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/2-intelligence` |
| Run after | Phase 1 gate passed and merged |
| Expected commit | `feat: implement safe version comparison` |

**Primary requirement references:** `FR-VER-01`, `FR-VER-02`, `FR-VER-03`, `FR-VER-04`, `FR-VER-05`, `NFR-MNT-02`, `AC-MAT-04`, `AC-MAT-05`

## Copy/paste prompt

```text
Task 16 — implement the pure, deterministic version-comparison engine.

Requirements are listed under Task 16 in `docs/requirements/TRACEABILITY.md`.

Implement unit-testable comparison outcomes: Newer, Equal, Older, Incomparable, and connector-deferred. Cover numeric dotted versions, missing components, prerelease labels, Stable-versus-Preview defaults, leading zeros, vendor suffixes, malformed/unknown values, connector-specific semantics, and downgrade prevention.

Never use lexical ordering as a silent fallback and never invent an ordering for incomparable values. The engine must return an explanation code suitable for the UI.

Use the fixture catalog and add adversarial cases. No source calls, UI, matching, or update execution. Run targeted property/table tests and the full suite. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
