# 90 — Remediate a phase review

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `current phase branch` |
| Run after | A phase review report exists and gate is FAIL |
| Expected commit | `fix: remediate phase review findings` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 90 — remediate the current failed phase review without widening scope.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- the current phase brief;
- the latest phase review report;
- only the ADRs/files/tests cited by the findings.

Create `docs/PLANS.md` for the remediation, ordered Blocker → High → mandatory evidence → Medium. Implement the smallest changes that resolve each finding. Do not suppress tests, weaken acceptance thresholds, convert a failure into a warning, or mark unexecuted evidence as passed.

For every finding:
- add or improve a regression test;
- record the fixing commit and verification in the review report;
- rerun the narrow test, then the full relevant suite;
- rerun affected security/performance/manual evidence.

Update status and traceability. Commit only when no Blocker/High finding remains in your own diff review. Then stop; rerun the original numbered phase-review task in a fresh Sol chat.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
