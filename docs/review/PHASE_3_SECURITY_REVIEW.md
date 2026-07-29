# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `5e3638386a8729f7c9c111c7fb51c1bfbddce8b7`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`FAIL`

The follow-up remediation fixes the reproduced configured-root ancestor escape and unifies oldest-first eviction for registered files and crash orphans. The gate still fails. The current production composition cannot reach a verified post-update `Success`, the latest cleanup primitive has not rerun the mandatory elevated link/mount fixture, and two disposable production-cache reproductions show active extraction content being deleted and absent ledger state being retained while cleanup reports `Success`.

Do not merge Phase 3 or begin Task 26. Run Task 90 and repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and reproducible results

- Complete diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..5e3638386a8729f7c9c111c7fb51c1bfbddce8b7` — 85 files, 5,463 insertions, 166 deletions.
- Security fixtures — 16 passed.
- Architecture tests — 4 passed.
- Focused WinGet, production-workflow, reviewed-execution, batch/recovery, SQLite, cleanup and quota integration filter — 57 passed.
- Host-safe hostile script — 42 assertions passed; one approved elevated-only symbolic-link skip.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 160 passed: Security 16, Architecture 4, Domain 51, Application integration 89.
- Active-directory production-cache reproduction — `SUCCEEDED=True PAYLOAD_EXISTS=False REMAINING_BYTES=0 ACTIVE_LEDGER_COUNT=1`.
- Already-absent registered-artifact reproduction — `SUCCEEDED=True REMAINING_ITEMS=0 LEDGER_COUNT=1`.
- The recorded 14-assertion elevated production link/mount fixture ran at `17b46c4`, before `5e36383` changed production root acquisition; it was not rerun during this review.
- `git diff --check` still reports trailing whitespace only in seven previously committed review/evidence Markdown lines.
- No UAC prompt, WinGet mutation, provider-cache mutation, installer, real update, VHD, privileged certificate operation or non-fixture deletion was invoked.

## Findings

| ID | Severity | Requirement | Evidence | Finding and required remediation |
|---|---|---|---|---|
| `P3R-03` | Blocker | `AC-DAT-16`, `TM-05–06`, ADR-019 | `DownloadedArtifactCache.cs:112–119, 241–316`; `TASK_90_P3R4_EVIDENCE.md`; approved elevated fixture at `17b46c4` | The configured-root ancestor escape is structurally remediated: deletion opens the volume and each root component handle-relatively, rejects reparse objects, and denies delete sharing. Parent/grandparent junction and safe root/ancestor-swap fixtures pass. However, `5e36383` changed this production root-acquisition primitive after the approved 14-assertion symbolic-link/root-link/scratch-VHD run. The latest evidence reruns only the 42-assertion host-safe script with its elevated case skipped. Rerun the approved production-cache file/directory/root-link and VHD fixtures against the final remediated head and record sentinel survival and teardown. |
| `P3R-10` | High | `FR-UPD-08–09`, `AC-UPD-06/08`, `TM-02/12`, ADR-003/017 | `AdditionalLocalInventoryProviders.cs:34–49`; `InventoryWorkflow.cs:132–152`; `WingetInstalledInventoryProvider.cs:60–75`; `InventoryWorkflowUpdateSourceTests.cs:101–118` | `ReInventoryAsync` now requires the full-scan WinGet instance to match scope and architecture, but the desktop’s only production WinGet scan adapter hard-codes both fields to `Unknown`. The positive test uses a `FixedProvider` that injects `Machine/x64`, a state production cannot emit, so every real completed update remains Inconclusive and mandatory `AC-UPD-06` is unproven. The available targeted scope/architecture resolver still derives execution compatibility from display-table parsing, contrary to ADR-003. Provide one fail-closed, production-reachable authority for exact source, ID, candidate version, scope and architecture without display-table authorization; add a test using the actual desktop provider composition plus cross-scope/architecture negatives. |
| `P3R-11` | High | `FR-DAT-08`, `NFR-PERF-10`, `AC-DAT-08/15`, `AC-NFR-10`, `TM-11/15`, ADR-011/019 | `DownloadedArtifactCache.cs:63–92, 134–169`; disposable production-cache reproduction | Active protection compares only exact relative paths. When an active ledger artifact is an extraction directory, its descendant files are enumerated as separate unregistered objects and deleted by both explicit clear and quota enforcement. The reproduction registered active `active-extraction`, then `ClearInactiveAsync` deleted `active-extraction\required.bin` and reported `Success`. Protect the complete subtree of every active directory artifact, or formally restrict registered artifacts to files and enforce that invariant before persistence; add explicit clear, quota and retention regressions proving active content survives. |
| `P3R-12` | Medium | `FR-DAT-15`, `AC-DAT-08/11`, `TM-07/11/15`, ADR-011 | `DownloadedArtifactCache.cs:63–102`; disposable production-cache reproduction | The unified loop considers only filesystem objects. An inactive registered artifact whose file is already absent is never visited, so its ledger row remains while cleanup reports `Success` and zero remaining items. This regresses idempotent ledger reconciliation and can later create duplicate-path ambiguity. Reconcile absent inactive rows as `AlreadyAbsent`, retain active rows, and add repeated-clear plus missing-at-start ledger assertions. |

## Remediation disposition

| Finding | Disposition |
|---|---|
| `P3R-01` | Closed: provider resolution remains machine-rooted, non-reparse and signature/signer checked. |
| `P3R-02` | Closed for false cross-scope/cross-architecture `Success`; the mandatory production positive path is separately open as `P3R-10`. |
| `P3R-03` | Structural escape closed in code and safe fixtures; open because mandatory elevated evidence is stale for the changed primitive. |
| `P3R-04–07` | Closed for the previously reported explicit-clear, current-inventory, recovery and orphan-inclusion defects. |
| `P3R-08` | Closed for post-update display-table authority; targeted compatibility still uses table output under `P3R-10`. |
| `P3R-09` | Closed for mixed registered-file/orphan oldest-first eviction. Active-subtree protection is separately open as `P3R-11`. |

## Mandatory evidence status

- `AC-DAT-16` remains unproven at the final cleanup head because the approved elevated link/mount evidence predates the root-opening change.
- `AC-UPD-06/08` is not Proven through the actual desktop composition; the only positive test uses an impossible production WinGet instance.
- `AC-NFR-10` is contradicted by direct deletion of a required file beneath an active extraction-directory artifact.
- `FR-DAT-15` idempotent/accurate cleanup is contradicted by a retained missing ledger row with a reported `Success`.
- Passing automated counts do not override stale mandatory evidence or the two High production-path defects.

## Residual risks

Clean-image behavior, per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, same-user substitution of the name-based temporary export, root creation races before handle binding, and unconstrained hostile races remain unproven within their recorded exception scope. ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
