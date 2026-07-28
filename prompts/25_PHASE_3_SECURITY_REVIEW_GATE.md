# 25 — Phase 3 security review gate

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **Extra High** |
| Speed | **Standard** |
| Mode | **Review** |
| Branch | `phase/3-execution` |
| Run after | Tasks 21–24 complete |
| Expected commit | `review: perform Phase 3 security gate` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 25 — perform the Phase 3 security review. Start with `/review` and inspect the branch from its merge-base with `main`. Do not modify production code.

Read the Phase 3 brief, all execution/helper/deletion ADRs, threat model, hostile fixtures, VM/manual evidence, and complete diff.

Review for:
- arbitrary command/path/argument execution;
- mutable or replayable approved jobs;
- UAC/IPC privilege escalation or confused deputy behaviour;
- database/metadata tampering;
- incorrect success states;
- unsafe process closing;
- batch cancellation/recovery defects;
- secrets or personal data in logs;
- cache ownership mistakes;
- path traversal, junction/symlink/reparse escape, TOCTOU, and false cleanup success;
- unbounded output/resource use;
- missing negative tests and unproven acceptance criteria.

Run all safe security, update-runner, and cleanup tests. Write `docs/review/PHASE_3_SECURITY_REVIEW.md` with PASS/FAIL. Any arbitrary-execution possibility, deletion escape, privilege-boundary weakness, false Success, Blocker/High finding, or missing mandatory VM evidence fails the gate.

Only update review/status. If failed, run Task 90 and repeat this review.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
