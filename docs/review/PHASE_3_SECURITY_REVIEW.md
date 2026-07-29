# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `b5cc57738d0e8583bc81bd8019226c44661e9466`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`FAIL`

The latest remediation closes the exact `P3R-10` eight-probe/display-compatibility defect and adds a user-visible initial running-target acknowledgement. The gate still fails. A malformed WinGet available-update display row can supply the immutable `--version` execution argument, contrary to ADR-003, and the desktop launches the helper through `runas` before checking its signature or signer from a path writable by the caller. A late running-target detection can also still execute without the user's target-specific acknowledgement.

Do not merge Phase 3 or begin Task 26. Run Task 90 and repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and reproducible results

- Complete implementation diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..b5cc57738d0e8583bc81bd8019226c44661e9466` — 88 files, 5,827 insertions, 185 deletions.
- Security fixtures — 16 passed.
- Architecture tests — 4 passed.
- Focused WinGet, production workflow, reviewed execution, batch and production-cache integration filter — 51 passed.
- Host-safe hostile script — 42 assertions passed; one documented elevated-only symbolic-link skip.
- Recorded approved elevated production-cache rerun after the root-open changes — 14 assertions passed; all file/directory/root-link and scratch-VHD sentinels survived.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 168 passed: Security 16, Architecture 4, Domain 51, Application integration 97.
- Host-safe WinGet boundary script — 11 assertions passed.
- Read-only machine-provider inspection — six controls passed: WindowsApps root, package directory and provider were non-reparse; Authenticode was valid; signer subject contained Microsoft Corporation; caller write allows were zero.
- Direct current-Release reproduction parsed mapped package `Contoso.Widget` and available version `9.9.9` from a malformed display table with later `ActualId`/`ActualVersion` columns, then emitted `upgrade --id Contoso.Widget --version 9.9.9 --source winget --exact --scope machine --architecture x64 --disable-interactivity`.
- Fresh Release helper inspection reported both desktop/helper `NotSigned`; the actual helper file and containing output directory grant the current user inherited `FullControl`. Static order is `LaunchHelper(... runas)` before the connected-process signature/signer check.
- `git diff --check` passed before this report was edited.
- No UAC prompt, WinGet command, provider-cache mutation, installer, real update, VHD, privileged certificate operation or non-fixture deletion was invoked during this review.

## Findings

| ID | Severity | Requirement | Evidence | Finding and required remediation |
|---|---|---|---|---|
| `P3R-14` | High | `FR-UPD-02–03`, `NFR-MNT-03`, `AC-UPD-01–02`, `TM-01–03/13`, ADR-003/017 | `WingetInstalledInventoryProvider.cs:187–224, 255–270, 308–323`; `InventoryWorkflow.cs:104–119, 223–234`; direct current-Release reproduction | `WingetUpdateSourceConnector` converts `winget upgrade` display-table fields into `SourcePackageRecord.AvailableVersion`; the fresh-preparation path promotes that value into `UpdateJobSelection.CandidateVersion`, and both runners place it in the fixed `--version` argument. Exact user mapping binds source/package but supplies no structured candidate-version authority. The parser accepts arbitrary extra columns and ignores all between Available and the last Source column; the direct reproduction therefore converted malformed display text into the immutable execution argument. This is the authority ADR-003 expressly deferred while Phase 2 had no execution UI. Stop all table-derived values from authorizing a job or provider argument. Use a supported structured/programmatic authority binding exact source, ID and candidate version, or keep the candidate non-actionable. Add malformed/localized/injected-spacing/extra-column and actual desktop-composition tests proving table-only evidence cannot create a job or call either runner. |
| `P3R-15` | Blocker | `FR-UPD-02/04`, `AC-UPD-02/04`, `TM-03–04`, ADR-002/018 | `WindowsElevatedHelperTransport.cs:36–55, 96–107, 156–182, 528–575`; fresh Release signature/ACL inspection; signed host-exception record | The desktop calls `Process.Start` with `Verb=runas` before any signature/signer verification of the helper. Verification occurs only after the elevated process connects; a substituted executable has already acquired high integrity and need never connect. The exact Release path is unsigned and caller-writable, while the approved signed host fixture proves only post-connect mutual identity. This is an arbitrary-elevation/confused-deputy path for the modeled same-user adversary. Resolve the helper from an installer-protected, non-caller-writable, non-reparse fixed root; validate its trusted signature and exact expected signer before `runas`; preserve the post-connect check; and close replacement between validation and launch through protected ownership/ACLs and reviewed launch semantics. Add caller-writable, unsigned, wrong-signer, reparse and replacement fixtures proving no elevated process/UAC launch occurs, plus clean installed-path evidence. Revisit ADR-002 if a per-user installation cannot provide that boundary. |
| `P3R-13` | Medium | `FR-UPD-05–06`, `TM-10` | `ReviewedUpdateExecutionWorkflow.cs:36–52`; `UpdateBatchWorkflow.cs:134–145`; `MainWindow.cs:208–225`; `SequentialUpdateBatchTests.cs:33–49`; `ReviewedUpdateExecutionWorkflowTests.cs:64–80` | Initial and confirmation snapshots are now user-visible/gated, and process objects are disposed. However, a target can start after the confirmation snapshot. The coordinator's immediate pre-provider detection then appends history and executes without consulting or obtaining the user's acknowledgement; the existing true-detector batch test proves this behavior. Make a newly detected pre-provider target stop before execution and return a user-visible acknowledgement-required result; only a subsequent explicit target-specific confirmation may resume. Add the three-snapshot false/false/true production-workflow regression and retain no close/kill authority. |

## Remediation disposition

| Finding | Disposition |
|---|---|
| `P3R-01–02` | Closed for the reported machine WinGet path and false cross-scope/cross-architecture `Success` defects. |
| `P3R-03` | Closed: handle-relative root acquisition is structurally reviewed and the elevated production link/mount fixture is fresh for that primitive. |
| `P3R-04–09` | Closed for the previously reported clear, inventory, recovery, parser-success and mixed quota defects. |
| `P3R-10` | Closed narrowly: the eight per-row probes are removed; installed table data makes one bounded display-only call and retains `Unknown` compatibility. The distinct available-version authority defect is `P3R-14`. |
| `P3R-11–12` | Closed: active directory descendants survive and missing inactive ledger entries reconcile while missing active entries remain. |
| `P3R-13` | Open — initial/confirmation acknowledgement works, but the final pre-provider detection is history-only and does not gate execution. |
| `P3R-14` | Open — display-table candidate version becomes an executable provider argument. |
| `P3R-15` | Open — caller-writable helper is elevated before trust validation. |

## Mandatory evidence status

- `AC-DAT-16` is Proven within the approved host exception against the current root-open primitive.
- `AC-NFR-10` and `FR-DAT-15` are Proven for the remediated active-subtree and missing-ledger cases.
- `AC-UPD-06/08` retains fail-closed post-update evidence, but the update that reaches it is not authorized through an ADR-003-compliant candidate-version path.
- `AC-UPD-04`/`TM-04` is not Proven for a trusted helper launch; post-connect signature checks cannot undo arbitrary code already launched elevated.
- `FR-UPD-05` is not Proven for a target first detected at the immediate pre-provider boundary.
- Passing automated counts and the prior exceptions do not override an arbitrary-elevation path or a High execution-authority defect.

## Task 90 remediation handoff

The Task 90 commit following this review addresses `P3R-13–15`; this section records the handoff and does not regrade the independent gate.

| Finding | Remediation-tree disposition | Verification |
|---|---|---|
| `P3R-15` | Closed in the remediation tree. Exact protected `%ProgramFiles%\PC Steward` layout, whole-path non-reparse and effective current-token non-modifiability, trusted desktop/helper signatures and exact signer equality are checked twice before the launcher/`runas` call. Post-connect identity remains. Current unsigned/writable developer output fails before UAC. | Security fixtures **19 passed**, including zero-launch initial/replacement failures; safe read-only local trust inspection. |
| `P3R-14` | Closed in the remediation tree. An immutable selection now requires exact bounded structured candidate authority; the WinGet table connector is display-only and actual desktop composition supplies no structured provider, so it fails closed before review/job/runner construction. | Focused application filter **56 passed**, including malformed/localized/extra-column, wrong binding/version and actual composition tests. |
| `P3R-13` | Closed in the remediation tree. A first positive final pre-provider snapshot returns target/process-specific acknowledgement-required before queue/provider execution; continuation requires a fresh review and explicit acknowledgement. | False/false/true production-workflow regression and no-close/kill surface checks pass. |

Release build succeeded with 0 warnings/errors; the complete suite passed **176** tests; hostile and WinGet scripts passed **42/1 approved skip** and **11**. Full evidence is in [Task 90 P3R7 evidence](TASK_90_P3R7_EVIDENCE.md). A clean signed protected-root/UAC replay remains unavailable, so Phase 3 is still `FAIL` until a fresh Task 25 review.

## Residual risks

Clean-image behavior, per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, same-user substitution of the name-based temporary export, root creation races before handle binding, imperfect package-ID/process-name correlation, and unconstrained hostile races remain unproven within their recorded exception scope. ADR-002, ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
