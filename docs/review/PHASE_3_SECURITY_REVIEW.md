# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `4b77e321d98f0030012847fab42c3afe33b9f270`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-28

## Gate result

`FAIL`

`P3R-01`, `P3R-04` and `P3R-06` are materially remediated. `P3R-02` still permits unrelated local evidence to satisfy the compatibility half of post-update verification, and the remediated production deletion primitive has not run the mandatory link/mount/ancestor-race fixtures required to close `P3R-03`. The latter is an unproven mandatory hostile-environment row and remains a release blocker. Two additional Medium gaps cover current-inventory confirmation evidence and orphan quota enforcement.

Do not merge Phase 3 or begin Task 26. Run Task 90 and repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and reproducible results

- Complete diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..4b77e321d98f0030012847fab42c3afe33b9f270` — 77 files, 4,784 insertions, 152 deletions.
- Security fixtures: `dotnet test tests\PCSteward.Security.FixtureTests\PCSteward.Security.FixtureTests.csproj --configuration Release --no-restore` — 16 passed.
- Architecture: corresponding Release project command — 4 passed.
- Focused update/verification/recovery/cache integration filter — 39 passed.
- Host-safe hostile script: `pwsh -NoProfile -File .\spikes\security-boundaries\tests\SecurityBoundarySpike.Tests.ps1` — 42 assertions passed; one documented symbolic-link VM/manual skip.
- Read-only machine provider inspection: `C:\Program Files\WindowsApps\Microsoft.DesktopAppInstaller_1.29.280.0_x64__8wekyb3d8bbwe\winget.exe` was non-reparse, Authenticode `Valid`, signed by Microsoft Corporation, with zero direct caller-write allow rules.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 146 passed: Security 16, Architecture 4, Domain 51, Application integration 75.
- No UAC prompt, WinGet command, provider cache, installer, real update, VHD, certificate, or privileged/destructive host test was invoked.

## Findings

| ID | Severity | Requirement | Evidence | Finding and required remediation |
|---|---|---|---|---|
| `P3R-02` | High | `FR-UPD-08–09`, `AC-UPD-06/08`, `TM-12`, ADR-017 | `InventoryWorkflow.cs:123–144`; `WingetInstalledInventoryProviderTests.cs:47–56` | The exact ID/source/version query is a good fail-closed first check, but scope/architecture reconciliation accepts any registry/MSIX instance with the same display name, version, scope and architecture. It is not bound to the exact WinGet package/product, so an unrelated same-name local row can authorize `Success`. No test invokes production `InventoryWorkflow.ReInventoryAsync`; current Success tests use stubs. Bind concrete evidence deterministically to the exact approved package and add composed positive/negative/collision tests through the production workflow. |
| `P3R-03` | Blocker | `AC-DAT-16`, `TM-05–06`, ADR-019 | `DownloadedArtifactCache.cs:231–291`; hostile scripts under `spikes/security-boundaries` | The handle-relative design is structurally stronger, but every junction, symbolic-link, volume-mount and swap fixture still invokes the older spike `SafeDeletion`, not `DownloadedArtifactCache`/`NativeDelete`. The required production repeated ancestor-race proof is absent. In addition, `NtCreateFile` receives Win32 `OPEN_EXISTING` value `3`, which is NT `FILE_OPEN_IF`; a disappearing descendant may be recreated and counted as deleted rather than opened-only. Use NT `FILE_OPEN` (`1`), add production link/mount/final/root/ancestor-swap tests, and require zero outside deletion with accurate residual/recovered-byte reporting. |
| `P3R-05` | Medium | `FR-UPD-02`, `AC-UPD-01–02`, `TM-07/13`, ADR-017 | `InventoryWorkflow.cs:86–116`; `InventoryWorkflowUpdateSourceTests.cs` | Production now scans before review and confirmation, but no remediation test configures local providers or proves external version/scope/architecture changes, deletion, provider failure, or tampered persisted inventory stop execution. Existing selection tests pass an empty provider list and bypass the new branch. Add the previously required review-to-confirmation production-composition regressions. |
| `P3R-07` | Medium | `NFR-PERF-10`, `AC-NFR-10`, `TM-11/15` | `DownloadedArtifactCache.cs:63–108`; `DownloadedArtifactCacheTests.cs:73–86` | Clear now reconciles orphans accurately, closing original `P3R-04`, but retention/quota mode never deletes any filesystem orphan and excludes orphan bytes from `retainedBytes`. Crash-before-registration payloads can therefore remain above the 2 GB soft quota indefinitely. Reconcile safe inactive orphans during quota enforcement, preserve active/unsafe objects, and test over-quota orphan ordering and residuals. |

## Remediation disposition

**Task 90 repeat remediation:** `814519c134618cfbfb562d1233af4f784dc9390a`. The follow-up adds fixed exact scope/architecture provider queries, production workflow evidence, NT `FILE_OPEN`, production reparse-root/final/repeated-ancestor junction tests, and orphan quota enforcement. Its verification is in `TASK_90_P3RR_EVIDENCE.md`; a fresh Task 25 review must independently decide closure.

| Finding | Disposition |
|---|---|
| `P3R-01` | Closed for this gate: elevated provider resolution is machine-rooted, non-reparse and signature/signer checked; the fresh read-only host inspection found no caller-write rule. Same-signer rollback/version pinning remains a residual hardening risk. |
| `P3R-02` | Open — High false-Success path and missing composed production evidence. |
| `P3R-03` | Open — mandatory production hostile evidence is Missing; release blocker. |
| `P3R-04` | Closed for explicit clear: filesystem orphans are measured, safely attempted, and retained as non-success residuals when undeletable. Quota behavior is split into `P3R-07`. |
| `P3R-05` | Partially remediated in code; required production-composition negative evidence remains Missing. |
| `P3R-06` | Closed: started cancellation/recovery is `VerificationRequired`, never relabelled plain Queued/Cancelled. |

## Mandatory evidence status

- The signed-helper/UAC host substitute, provider-disconnect limit, clean-image limit, per-user no-prompt limit, real Firefox exercise, and personal-use cleanup-UI waiver are explicitly product-owner-approved and remain confined to `PHASE_3_HOST_TEST_EXCEPTION.md`.
- `AC-UPD-06/08` is not fully Proven through the remediated production outcome path: the approved Firefox run proves the package version changed, but no test calls `InventoryWorkflow.ReInventoryAsync` and the desktop outcome fix was not replayed.
- `AC-DAT-16` is Missing for the remediated production primitive: approved host evidence proves the spike primitive only. The host-substitution exception does not waive production-code hostile testing.
- All other automated rows were rerun successfully at the reviewed head. Passing counts do not override the two missing production-path rows above.

## Residual risks

Clean-image behavior, per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, and unconstrained hostile race behavior remain unproven within their recorded exception scope. ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
