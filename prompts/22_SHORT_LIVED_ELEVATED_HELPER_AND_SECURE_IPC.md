# 22 — Short-lived elevated helper and secure IPC

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/3-execution` |
| Run after | Task 21 complete |
| Expected commit | `feat: add short-lived elevated helper` |

**Primary requirement references:** `FR-UPD-04`, `AC-UPD-03`, `AC-UPD-04`, `AC-UPD-05`

## Copy/paste prompt

```text
Task 22 — implement the short-lived elevated helper and secure IPC exactly as accepted in the ADRs.

Requirements are listed under Task 22 in `docs/requirements/TRACEABILITY.md`.

Implement the narrowest possible helper:
- no service and no persistence;
- launched only when an approved job reaches execution and the provider requires elevation;
- authenticated, nonce-bound, single-purpose request tied to an immutable job;
- allowlisted operation verb/provider/package identifiers;
- validation against a non-user-editable approved-job representation;
- no arbitrary executable path, command string, working directory, environment, output path, or remote metadata;
- explicit user-denied, cancelled, timed-out, failed, and completed results;
- sanitized result channel and immediate exit.

Threat-model and test replay, substitution, stale job, tampered database, malformed IPC, duplicate request, path injection, privilege confusion, and helper crash. Do not invoke a real update on the product owner’s PC. UAC/integration tests must use a harmless test verb in a disposable VM or be documented as pending manual evidence.

Run static analysis and all helper boundary tests. Update the threat model, status, and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
