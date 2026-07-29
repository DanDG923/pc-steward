# Task 90 — Phase 3 remediation evidence (2026-07-29)

## Result

Complete evidence handoff. The working tree remediates `P3R-08` and `P3R-09`; the approved elevated fixture on this PC completes the required `P3R-03` production-cache link and mount cases. The Phase 3 gate remains **FAIL** only until the required fresh independent Task 25 review.

## Remediated findings

| Finding | Change | Evidence |
|---|---|---|
| `P3R-08` | `HasExactExpectedVersionAsync` now runs fixed-source `winget export --include-versions` to an app-created temporary JSON file. It accepts exactly one matching source/package/version from bounded structured data; WinGet display output cannot authorize success. | `ExactPostUpdateQueryRequiresOneExpectedPackageAndVersion` and `ExactPostUpdateQueryCannotAuthorizeSuccessFromDisplayTableText`. |
| `P3R-09` | Each owned filesystem object carries its last-write time; inactive crash-orphan candidates are sorted oldest-first before deterministic relative-path tie-break. | `QuotaEvictsTheOldestCrashOrphanBeforeANewerOne`. |
| `P3R-03` | Production cache cleanup rejects file, directory, and root symbolic links plus a scratch-VHD mount, each retaining its outside sentinel. Deterministic tests replace an ancestor and root after the fixed root handle binds; the operation fails closed and never deletes outside. | Elevated `HostExceptionEvidence.Tests.ps1` (14 assertions); `ProductionCacheRejectsAnAncestorSwapAfterTheRootHandleIsBound`; `ProductionCachePreventsARootSwapAfterTheRootHandleIsBound`; existing root/final/repeated-ancestor junction tests. |

## Commands and results

```text
dotnet test tests\PCSteward.Application.IntegrationTests\PCSteward.Application.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~WingetInstalledInventoryProviderTests|FullyQualifiedName~DownloadedArtifactCacheTests"
Passed: 22; Failed: 0; Skipped: 0

pwsh -NoProfile -File .\scripts\Build.ps1
Build succeeded: 0 warnings; 0 errors

pwsh -NoProfile -File .\scripts\Test.ps1
Passed: 158; Failed: 0; Skipped: 0

pwsh -NoProfile -File .\spikes\security-boundaries\tests\SecurityBoundarySpike.Tests.ps1
PASS: 42 assertions; 1 local-PC/manual skip

pwsh -NoProfile -File .\spikes\security-boundaries\tests\HostExceptionEvidence.Tests.ps1 (elevated, approved local-PC exception)
PASS: 14 assertions; production cache and legacy spike rejected file/directory/root links and scratch VHD mount; sentinels survived.
```

## Handoff boundary

All remediation evidence now runs on this PC under the existing product-owner-approved local exception. The fixture uses only a generated temporary root, detaches the new 32 MB VHD, and removes every fixture in `finally`. See [the resolved evidence record](../failures/90-phase3-production-hostile-evidence.md). Do not merge Phase 3 or start Task 26; repeat Task 25 under Sol/Extra High.
