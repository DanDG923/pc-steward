# 15 — Phase 1 review gate

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Review** |
| Branch | `phase/1-inventory` |
| Run after | Task 14A complete |
| Expected commit | `review: assess Phase 1 inventory gate` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 15 — review Phase 1. Start with `/review` and review the branch from its merge-base with `main`. Do not modify production code.

Read:
- `AGENTS.md`
- `docs/review/PHASE_1_EVIDENCE_READINESS.md`
- `docs/requirements/PHASE_1.md`
- accepted ADRs;
- benchmark evidence;
- `docs/review/PHASE_REVIEW_TEMPLATE.md`
- the complete Phase 1 diff.

Run safe builds/tests/benchmarks needed to verify evidence. Review specifically for:
- missed inventory surfaces in scope;
- accidental `Win32_Product`/MSI repair;
- privilege or network use during local scans;
- over-deduplication;
- malformed-input failures;
- transactional and cancellation defects;
- Clear scan data deleting or preserving the wrong categories;
- misleading UX;
- resource-budget evidence and measurement quality;
- requirement/test traceability.

Write `docs/review/PHASE_1_REVIEW.md` with file/line references, severities, requirement IDs, missing evidence, and PASS/FAIL. A missing or stale readiness package is unproven mandatory evidence. A gate cannot pass with a Blocker/High finding or an unproven mandatory acceptance criterion.

Only update the review report and `docs/STATUS.md`. If the gate fails, state that Task 90 must be run and this review repeated.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
