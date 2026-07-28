# 18B — Optional: official GitHub Releases read-only connector

| Setting | Use |
|---|---|
| Model | **GPT-5.6 Terra** |
| Reasoning | **High** |
| Speed | **Standard** |
| Mode | **Agent** |
| Branch | `phase/2-intelligence` |
| Run after | Phase 2 core gate passes and product owner elects to include desirable MVP scope |
| Expected commit | `feat: add read-only official GitHub Releases checks` |

**Primary requirement references:** `FR-SRC-06`

## Copy/paste prompt

```text
Optional Task 18B — implement user-confirmed, read-only official GitHub Releases version checks. Do not make GitHub an executable update provider.

Read FR-SRC-06 and the trusted-source policy. Require the user to map a product to an explicit official repository owner/name. Validate repository identity, API response sizes/fields, release channel, prerelease exclusion, rate-limit/error handling, and cached freshness.

The connector may return version/release-note intelligence only. It must not download or run assets, infer an official repository from search results, follow arbitrary URLs, authorize a match, or enable Update.

Add fixtures for renamed/deleted repositories, prereleases, malformed tags, no releases, rate limiting, stale cache, and repository ownership ambiguity. Run full tests and repeat Task 20 review with the added scope.
```

## Completion rule

Do not proceed to the next numbered task until this task is committed and `docs/STATUS.md` points to the next task. Review tasks must pass; otherwise run Task 90 and repeat the same review.
