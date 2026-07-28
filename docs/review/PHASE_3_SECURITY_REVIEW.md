# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `17b46c4c2745ec41a49841d33aac3388d57fc655`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`FAIL`

The new elevated evidence closes the previously missing production file/directory/root-link and scratch-VHD cases, and structured WinGet export removes display-table text from post-update authority. The gate nevertheless fails. A controlled production-harness reproduction proves that a junction above the configured cache root redirects cleanup outside the allowlisted root and the outside sentinel is deleted. The export remediation also regresses the previously closed scope/architecture binding and can authorize `Success` from a different installation target. Finally, registered artifacts are still processed before crash orphans rather than through one oldest-first quota order.

Do not merge Phase 3 or begin Task 26. Run Task 90 and repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and reproducible results

- Complete diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..17b46c4c2745ec41a49841d33aac3388d57fc655` — 84 files, 5,355 insertions, 166 deletions.
- Security fixtures — 16 passed.
- Architecture tests — 4 passed.
- Focused WinGet, cache, current-inventory, reviewed-execution, batch/recovery and SQLite integration filter — 55 passed.
- Host-safe hostile script — 42 assertions passed; one documented elevated-only symbolic-link skip.
- Recorded approved elevated fixture — 14 assertions passed against the production primitive for file, directory and root symbolic links plus a scratch-VHD mount; not rerun during this review.
- Controlled configured-root ancestor reproduction: `PCSteward` was a junction to a disposable outside directory while the configured root was `PCSteward\downloads`; the production-cache harness exited `4` (`did not fail closed`) and `SENTINEL_EXISTS=False`.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 158 passed: Security 16, Architecture 4, Domain 51, Application integration 87.
- No UAC prompt, WinGet mutation, provider-cache mutation, installer, real update, VHD, privileged certificate operation or non-fixture deletion was invoked.

## Findings

| ID | Severity | Requirement | Evidence | Finding and required remediation |
|---|---|---|---|---|
| `P3R-03` | Blocker | `AC-DAT-16`, `TM-05–06`, ADR-019 | `DownloadedArtifactCache.cs:121–127, 207–216, 237–245`; controlled production-harness reproduction | `EnsureRoot` creates the configured directory and calls `RejectReparseComponents(canonical, canonical)`, which checks the final root only. It never checks ancestors above that root. A junction at the production-owned `PCSteward` parent therefore redirects the nominal `PCSteward\downloads` root; `NativeDelete` then follows that ancestor while acquiring its root handle and deletes within the outside target. The controlled disposable fixture deleted the outside sentinel. Bind the root from a trusted non-reparse ancestor with handle-relative, non-following opens; reject every existing configured-root ancestor before any creation/deletion; and add production tests for parent/grandparent junction, symlink, mount and deterministic ancestor swaps around root acquisition. |
| `P3R-02` | High | `FR-UPD-08–09`, `AC-UPD-06/08`, `TM-02/12`, ADR-003/017 | `WingetInstalledInventoryProvider.cs:32–58, 91–105`; `InventoryWorkflow.cs:132–151`; `WingetInstalledInventoryProviderTests.cs:47–66` | Replacing the exact `list --scope --architecture` query with `winget export` closes `P3R-08` but removes the approved target binding: export arguments and JSON contain only source/package/version, and the subsequent full-scan predicate also ignores `job.Selection.Scope` and `Architecture`. If another scope or architecture for the same ID is already at the candidate version while the approved target remains old, both checks pass and the batch records `Success`. Restore authoritative post-update evidence bound to exact ID, source, version, scope and architecture, fail closed when the provider cannot expose all fields, and add same-ID cross-scope/cross-architecture negative tests through the production workflow. |
| `P3R-09` | Medium | `NFR-PERF-10`, `AC-NFR-10`, `TM-11/15`, ADR-011 | `DownloadedArtifactCache.cs:63–104`; `DownloadedArtifactCacheTests.cs:10–24, 104–123` | The first cleanup loop always considers registered inactive artifacts by `CreatedAt`; only after that loop does the second loop sort crash orphans by filesystem last-write time. Thus a newer registered file is evicted before an older orphan whenever the registered deletion alone drops usage below quota. The new test covers two orphans only and does not satisfy the prior required mixed-order case. Build one oldest-first candidate order across registered inactive files and safe orphans, with deterministic tie-breaking, and add mixed registered/orphan tests with exact retained/recovered-byte assertions. |

## Remediation disposition

| Finding | Disposition |
|---|---|
| `P3R-01` | Closed for this gate: provider resolution remains machine-rooted, non-reparse, signature/signer checked, and the recorded host inspection found no caller-write rule. |
| `P3R-02` | Reopened — the structured-export change regresses exact scope/architecture post-update binding and permits false `Success`. |
| `P3R-03` | Open — direct link/mount evidence is now present, but a configured-root ancestor junction causes a reproduced deletion escape. |
| `P3R-04` | Closed: explicit clear reconciles safe orphans and reports undeleted objects as residuals rather than false success. |
| `P3R-05` | Closed: production composition rescans before review and confirmation, and a version change between those points blocks job creation and execution. |
| `P3R-06` | Closed: cancellation after provider start and persisted interrupted `Running` state become `VerificationRequired`, never plain Queued/Cancelled. |
| `P3R-07` | Closed for orphan quota inclusion. Oldest-first ordering across registered and orphan objects remains open as `P3R-09`. |
| `P3R-08` | Closed: display-oriented table output no longer supplies the targeted post-update version record. Exact target binding remains open as `P3R-02`. |
| `P3R-09` | Open — the implementation still has two sequential candidate orders and lacks the required mixed-order evidence. |

## Mandatory evidence status

- `AC-DAT-16` is contradicted by the controlled production-harness ancestor-junction reproduction; the approved direct link/mount fixture does not cover ancestors above the configured root.
- `AC-UPD-06/08` is not Proven while post-update authority omits the immutable scope/architecture and can return false `Success`.
- `AC-NFR-10` is not Proven for a mixed registered/crash-orphan cache and is contradicted by the current two-loop order.
- All safe automated suites pass. Passing counts do not override a reproduced deletion escape, a High false-Success path or unproven mandatory acceptance criteria.

## Residual risks

Clean-image behavior, per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, same-user substitution of the name-based temporary export, and unconstrained hostile races remain unproven within their recorded exception scope. ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
