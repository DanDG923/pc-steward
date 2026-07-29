# 20 — Phase 2 review gate

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Sol** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Review** |
| Branch | `phase/2-intelligence` |
| Run after | Task 19A complete |
| Expected commit | `review: assess Phase 2 intelligence gate` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 20 — review Phase 2. Start with `/review` and review the branch from its merge-base with `main`. Do not modify production code.

Read the Phase 2 brief, accepted ADRs, fixture catalog/results, source-cache evidence, `docs/review/PHASE_2_EVIDENCE_READINESS.md`, and complete diff.

Review for:
- false-positive actionable mappings;
- guessed/invented version ordering;
- architecture/channel/publisher ambiguity;
- unsafe manual mapping;
- stale results presented as live;
- unbounded or malformed source data;
- raw command connectors or arbitrary web behaviour;
- UI enabling actions not justified by domain state;
- cache/offline/timeout resilience;
- traceability and missing tests.

Run the curated mapping suite and relevant full tests. Write `docs/review/PHASE_2_REVIEW.md` using the template. A missing or stale readiness package is unproven mandatory evidence. The gate fails for any actionable false positive, Blocker/High finding, or unproven mandatory acceptance criterion.

Only update the review report and status. If failed, run Task 90 and repeat this review.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
