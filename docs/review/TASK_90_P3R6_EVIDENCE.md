# Task 90 — Phase 3 Sol closeout evidence (2026-07-29)

**Branch:** `phase/3-execution`
**Starting implementation:** `7a9452f9d7fcdc1221382e92591a5cdd0f84ad8d`
**Review input:** `cec37e6`
**Model/effort:** GPT-5.6 Sol / Extra High
**Disposition:** remediation complete; Phase 3 remains gated on a fresh independent Task 25 review.

## Findings and remediation

| Finding | Result | Durable evidence |
|---|---|---|
| `P3R-03` | Closed and not changed by this task. Handle-relative root acquisition remains in production. The current-head 14-assertion elevated file/directory/root-link and scratch-VHD record remains applicable because no cache primitive changed; the non-elevated hostile rerun retained its one documented elevated-only skip. | `DownloadedArtifactCacheTests`; `SecurityBoundarySpike.Tests.ps1`; `TASK_90_P3RRR_EVIDENCE.md`; `90-phase3-production-hostile-evidence.md` |
| `P3R-10` | Closed in the remediation tree. `ResolveExactCompatibilityAsync` and all eight-per-row exact table probes are removed. The actual desktop WinGet adapter moved to the testable provider assembly, makes one bounded display call, and always stores `Unknown` scope/architecture. Header-boundary parsing resists injected display spacing but remains display-only. An actionable structured registry/MSIX product requires an explicit approved exact-package mapping; post-update success requires exact WinGet export JSON source/ID/version plus one exact structured local version/scope/architecture instance for the same reviewed stable product. Table-only, wrong identity, wrong export, wrong scope/architecture and ambiguous local evidence fail closed. | `WingetLocalInventoryProvider`; `InventoryWorkflow.ReInventoryAsync`; `DesktopCompositionUsesOneDisplayCallAndNeverEmitsExecutionCompatibility`; `HeaderBoundariesResistInjectedSpacingAndDoNotDependOnEnglishLabels`; `TableOnlyWingetScopeCannotBeInferredFromAnUnrelatedSameNameRegistryProduct`; `ReInventoryCombinesExactExportWithTheReviewedStructuredProductAndRejectsConflicts` |
| `P3R-11–12` | Closed and rechecked. Active directory ledger entries protect their complete subtree; missing inactive rows reconcile while missing active rows remain. | `DownloadedArtifactCacheTests` in the 51-test focused run and the 168-test complete run |
| `P3R-13` | Closed in the remediation tree. The production detector returns bounded package-derived process names before final confirmation, rejects generic suffixes, disposes all `Process` snapshots, and exposes no close/kill operation. A positive state changes the dialog to an explicit **Install approved update anyway** acknowledgement. Confirmation rechecks and stops before job persistence/provider execution if a target started after review without acknowledgement; the coordinator rechecks again for append-only history. | `RunningTargetIsVisibleBeforeConfirmationAndRequiresExplicitAcknowledgement`; `TargetStartingAfterReviewStopsBeforeApprovalUntilWarningIsAcknowledged`; `ProductionRunningApplicationDetectorFindsARepresentativeBoundedPackageSuffix`; `AmbiguousGenericSuffixAndExitedProcessSnapshotDoNotCreateAWarning` |
| `P3R-01–02`, `P3R-04–09` | No regression found in the independent static review or rerun evidence. Machine provider identity, immutable evidence, current inventory, false-success prevention, interrupted recovery, explicit cleanup, orphan accounting and oldest-first quota controls retain their prior closed dispositions. | Security 16; architecture 4; focused 51; full 168; hostile 42/1 skip; WinGet boundary 11 |

## Reproducible evidence

| Evidence | Result |
|---|---|
| Focused application filter: WinGet installed inventory, production update workflow, reviewed execution, sequential batch and downloaded cache | **51 passed, 0 failed, 0 skipped** |
| `dotnet test` — security fixtures, Release/no-build | **16 passed** |
| `dotnet test` — architecture fixtures, Release/no-build | **4 passed** |
| `pwsh -NoProfile -File .\scripts\Build.ps1` | **Succeeded; 0 warnings, 0 errors** |
| `pwsh -NoProfile -File .\scripts\Test.ps1` | **168 passed**: Security 16, Architecture 4, Domain 51, Application integration 97 |
| `pwsh -NoProfile -File .\spikes\security-boundaries\tests\SecurityBoundarySpike.Tests.ps1` | **42 assertions passed; 1 elevated-only local-PC case skipped** |
| `pwsh -NoProfile -File .\spikes\winget-packaging\tests\WingetSpike.Tests.ps1` | **11 assertions passed** |
| Read-only Release-assembly machine-provider inspection | **6 checks passed**: machine WindowsApps root, provider and package directory non-reparse, valid signature, Microsoft subject, zero caller write allows |
| `git diff --check` | **Passed** |

The shell was not elevated, so this task did not rerun the link/VHD fixture or request UAC. The recorded 14-assertion elevated production-cache evidence remains current because this remediation does not change `DownloadedArtifactCache.cs`, its native handle-relative primitive, or the elevated fixture. No WinGet command, provider-cache mutation, installer, real update, VHD, certificate operation, UAC prompt or non-fixture deletion was invoked.

## Independent closeout review

No Blocker or High finding remains in this remediation tree. The code diff contains no executable/path input expansion, command-string construction, force-close action, new deletion primitive, secret, or Phase 4 work. The complete historical `P3R-01–13` set was rechecked against the current implementation and evidence; only `P3R-10` and `P3R-13` required code changes.

Residual risks remain within the already recorded exception scope: clean-image behavior, per-user no-prompt behavior, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, same-user substitution of the name-based temporary WinGet export, root creation races before handle binding, imperfect package-ID/process-name correlation and unconstrained hostile races. ADR-003 and ADR-011 remain Provisional. `AC-UX-01–09` remain Optional under ADR-021.

Do not merge Phase 3 or begin Task 26. Repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.
