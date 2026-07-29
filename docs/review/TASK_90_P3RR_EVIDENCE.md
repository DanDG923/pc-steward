# Task 90 — repeated Phase 3 remediation evidence

**Date:** 2026-07-28  
**Scope:** `P3R-02`, `P3R-03`, `P3R-05`, and `P3R-07`. No UAC prompt, WinGet command, installer, provider cache, VHD, or real update was invoked.

| Finding | Reproducible command / artifact | Result | Status |
|---|---|---|---|
| `P3R-02` | `InventoryWorkflowUpdateSourceTests`, `WingetInstalledInventoryProviderTests` | Production `ReInventoryAsync` accepts only one persisted exact ID/source/version result after an exact WinGet query constrained by scope and architecture. Same-name registry evidence no longer authorizes success; ambiguous/no compatibility resolution fails closed. | Proven by 23-test focused fixture. |
| `P3R-03` | `DownloadedArtifactCacheTests`; `DownloadedArtifactCache.cs` | Native relative opens use NT `FILE_OPEN` (not `FILE_OPEN_IF`). Production cache tests reject reparse configured roots, final junctions, and 20 repeated ancestor junction cleanup attempts; every outside sentinel survived. | Proven for executed junction/root/ancestor production fixtures. Existing symbolic-link VM/manual limitation remains explicitly recorded. |
| `P3R-05` | `CurrentInventoryChangeBetweenReviewAndConfirmationNeverStartsTheProvider` | The real inventory workflow rescans before review and confirmation. A changed current provider result returns `Started=false`, saves no job, and calls no provider executor. | Proven by production-composition fixture. |
| `P3R-07` | `QuotaAccountsForAndEvictsAnInactiveCrashOrphan` | Quota begins with actual owned filesystem bytes and evicts a safe inactive crash orphan while preserving the active artifact. | Proven by production cache fixture. |

## Verification

- Focused update/cache/provider command: 23 passed.
- Production cache hostile filter: 9 passed.
- `pwsh -NoProfile -File .\spikes\security-boundaries\tests\SecurityBoundarySpike.Tests.ps1`: 42 assertions passed; 1 symbolic-link VM/manual skip.
- `pwsh -NoProfile -File .\scripts\Build.ps1`: 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1`: 154 passed — Security 16, Architecture 4, Domain 51, Application integration 83.

## Residual limits

No new clean-image, cross-integrity UAC, per-user no-prompt, provider-disconnect, real-update, symbolic-link creation, or volume-mount exercise was performed. Their approved exception scope is unchanged and must be reviewed independently; it is not represented as local production-test evidence.
