# Task 90 — Phase 3 remediation evidence

**Date:** 2026-07-28  
**Scope:** `P3R-01`–`P3R-06` only. No WinGet command, UAC prompt, installer, or real update was invoked.

| Finding | Reproducible command / artifact | Result | Status |
|---|---|---|---|
| `P3R-01` | Read-only PowerShell inspection of `%ProgramFiles%\WindowsApps\Microsoft.DesktopAppInstaller_*\winget.exe` using `Get-Item`, `Get-AuthenticodeSignature`, and `Get-Acl`; regression: `WingetInstalledInventoryProviderTests` | Machine x64 `winget.exe`: `Reparse=False`, signature `Valid`, signer `CN=Microsoft Corporation`; `BUILTIN\Users` has only `ReadAndExecute`/`Read`, while the inspected current-user alias is a reparse point and grants the caller `FullControl`. The unsigned fixture is rejected. | Proven on this host; resolver remains fail-closed if this machine identity cannot be read. |
| `P3R-02` | `dotnet test tests\PCSteward.Application.IntegrationTests\PCSteward.Application.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~DownloadedArtifactCacheTests|FullyQualifiedName~SequentialUpdateBatchTests|FullyQualifiedName~SqliteDatabaseTests|FullyQualifiedName~WingetInstalledInventoryProviderTests"` | 37 passed. Exact source/ID/version query accepts only one expected record; wrong ID fails. Production composition uses the targeted query plus concrete local scope/architecture reconciliation. | Proven by fixture. |
| `P3R-03` | Same focused command; `pwsh -NoProfile -File .\spikes\security-boundaries\tests\SecurityBoundarySpike.Tests.ps1` | Cache primitive uses root/directory-handle-relative, no-follow opens. Focused cleanup tests pass. Hostile-root script: 42 assertions passed; 1 symbolic-link VM/manual skip. | Proven for executed fixture/host-safe cases; the documented VM symbolic-link limitation remains an accepted residual risk. |
| `P3R-04` | Same focused command | 37 passed, including crash-before-registration locked orphan: cleanup is non-success, reports one remaining object and 9 remaining bytes. Summary derives bytes from the owned filesystem walk. | Proven by production cache fixture. |
| `P3R-05` | `InventoryWorkflow.cs`; full application integration suite | Production composition has local inventory providers, so preparation runs current installed inventory before review and again at confirmation. Failure makes selection non-actionable before provider construction. Full integration suite passed. | Proven by composition and regression suite. |
| `P3R-06` | Same focused command | 37 passed, including cancellation after provider completion and SQLite crash recovery; started work is `VerificationRequired`, never plain `Cancelled`/`Queued`. | Proven by fixture. |

## Whole-repository verification

- `pwsh -NoProfile -File .\scripts\Build.ps1`: succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1`: 146 passed — Security 16, Architecture 4, Domain 51, Application integration 75.
- `pwsh -NoProfile -File .\scripts\Benchmark.ps1`: reported 30 runs/300 records, 1.84 ms median and 2.36 ms p95, but exited 1 without a failure diagnostic. This is an anomaly, not a passing performance gate or a Phase 3 cache/UI acceptance claim.

## Residual limits

No new cross-integrity UAC replay, clean-image exercise, per-user no-prompt exercise, provider-disconnect exercise, real update, or VM symbolic-link creation was run. Existing product-owner exceptions and their scope remain unchanged; a fresh Task 25 review must judge the remediation before any merge or Task 26 work.
