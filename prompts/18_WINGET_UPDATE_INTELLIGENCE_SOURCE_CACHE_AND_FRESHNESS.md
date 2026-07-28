# 18 — WinGet update intelligence, source cache, and freshness

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/2-intelligence` |
| Run after | Task 17 complete |
| Expected commit | `feat: add WinGet update intelligence and cache` |

**Primary requirement references:** `FR-SRC-01`, `FR-SRC-02`, `FR-SRC-03`, `FR-SRC-04`, `FR-SRC-05`, `FR-SRC-10`, `FR-UI-01`, `FR-UI-04`, `FR-UI-05`, `FR-UI-06`, `NFR-PERF-02`, `NFR-REL-01`, `NFR-REL-04`, `NFR-MNT-01`, `NFR-MNT-03`, `AC-MAT-01`, `AC-MAT-03`, `AC-MAT-04`, `AC-MAT-05`, `AC-MAT-06`, `AC-DAT-04`, `AC-NFR-02`, `AC-NFR-05`, `AC-UX-06`

## Copy/paste prompt

```text
Task 18 — implement WinGet source refresh, available-update intelligence, validated source caching, and freshness.

Requirements are listed under Task 18 in `docs/requirements/TRACEABILITY.md`.

Using the accepted connector abstraction:
- refresh only enabled WinGet/Store sources on explicit online checks;
- expose last success, live/cached/local-only state, provider version, and warnings;
- validate remote/source fields with length, format, enum, and output-size limits;
- isolate connector failure and timeout;
- preserve the last known source result with an unmistakable stale timestamp;
- combine source candidates with the version and matching engines;
- generate Current, Verified update, Review required, Unmanaged, Pinned, Ignored, and Failed/Unavailable statuses;
- maintain online connector concurrency at the configured bound.

Do not execute updates, add raw command connectors, search arbitrary websites, or enable GitHub Releases unless running optional Task 18B later.

Add healthy, stale, malformed, timeout, disagreement, architecture/channel, and offline tests. Run the documented online-check benchmark against fixtures/test doubles; use real source calls only with network approval and without modifying package-manager state. Update status and traceability.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
