# 11 — Normalization, install identity, and conservative deduplication

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Tasks 08–10 complete |
| Expected commit | `feat: normalize and conservatively deduplicate inventory` |

**Primary requirement references:** `FR-INV-07`, `FR-INV-09`, `FR-ID-01`, `AC-INV-02`, `NFR-MNT-02`

## Copy/paste prompt

```text
Task 11 — implement canonical product/install-instance normalization and conservative deduplication.

Requirements are listed under Task 11 in `docs/requirements/TRACEABILITY.md`.

Read:
- Phase 1 brief;
- domain/persistence code;
- the fixture catalog;
- accepted identity/grouping ADRs.

Create deterministic rules that:
- preserve every raw provider record;
- merge only evidence-equivalent records for the same installation instance;
- keep user/machine and x86/x64 instances inspectable;
- avoid over-grouping runtimes, redistributables, framework packages, channels, and similarly named products;
- produce stable product and instance fingerprints;
- retain provenance and an explanation of merge decisions;
- classify unknowns rather than invent data.

Add a fixture-driven matrix that proves expected merges and expected non-merges, including adversarial near-name cases. No update matching or executable confidence is implemented in this task.

Run all normalization tests plus the full suite. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
