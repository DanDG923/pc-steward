# 17 — Deterministic matching and confidence engine

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/2-intelligence` |
| Run after | Task 16 complete |
| Expected commit | `feat: implement deterministic update matching` |

**Primary requirement references:** `FR-ID-01`, `FR-ID-02`, `FR-ID-03`, `FR-ID-04`, `FR-ID-05`, `FR-ID-06`, `FR-ID-07`, `AC-MAT-01`, `AC-MAT-02`, `AC-MAT-03`, `AC-MAT-04`, `AC-MAT-05`, `AC-MAT-07`

## Copy/paste prompt

```text
Task 17 — implement the deterministic product-to-package matching and confidence engine.

Requirements are listed under Task 17 in `docs/requirements/TRACEABILITY.md`.

Use only explicit evidence such as exact provider/package IDs, package family/MSI identity where available, normalized publisher/product aliases, scope, architecture, channel, and user-confirmed mappings. Produce:
- canonical package mapping;
- confidence level;
- human-readable evidence explanation;
- actionable/non-actionable decision;
- mapping origin;
- incompatibility/review reasons.

Low confidence, architecture/channel conflict, ambiguous publisher, incomparable version, or unmatched data must never become actionable. Manual mappings target only a supported trusted connector package and are reversible.

Create a curated fixture suite with expected true matches, expected non-matches, and adversarial near-name/publisher cases. The test must assert zero false-positive actionable mappings. No update execution.

Run targeted and full tests; update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
