# 07 — Core domain model and SQLite persistence

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **Medium** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/1-inventory` |
| Run after | Phase 0 gate passed and merged |
| Expected commit | `feat: add core domain and SQLite persistence` |

**Primary requirement references:** See the task and current phase brief.

## Copy/paste prompt

```text
Task 07 — implement the core domain model and versioned SQLite persistence, with no inventory providers or UI features.

Read:
- `AGENTS.md`
- `docs/STATUS.md`
- `docs/requirements/PHASE_1.md`
- ADRs for architecture, persistence, logging, and data lifecycles.

Model separate lifecycle categories for:
- canonical products;
- installation instances;
- raw provider records;
- scan runs/provider timings/warnings;
- update candidates and source cache;
- stable user decisions and manual mappings;
- update jobs/history;
- downloaded artifacts;
- settings/consent/schedule metadata.

Implement:
- stable identifiers/fingerprints that can reapply decisions after Clear scan data;
- versioned, transactional migrations;
- repositories/unit-of-work boundaries appropriate to the ADR;
- last-known-good inventory semantics;
- bounded field lengths and safe normalization at persistence boundaries;
- test-only database factories.

Do not implement provider scanning, matching, version ordering, update execution, deletion UI, or factory reset.

Add migration, transaction, round-trip, interruption, and decision-preservation tests. Update `docs/STATUS.md` and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
