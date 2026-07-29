# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `7a9452f9d7fcdc1221382e92591a5cdd0f84ad8d`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`FAIL`

The final remediation closes the stale hostile-cleanup evidence, active-subtree deletion, absent-ledger reconciliation and always-false running-detector defects. The gate still fails because the production WinGet composition now uses display-table parsing as execution compatibility authority, directly contradicting ADR-003 and the required `P3R-10` remediation. The detector can create an internal warning event, but that detected state is not surfaced to the user before provider execution, so `P3R-13` also remains open.

Do not merge Phase 3 or begin Task 26. Run Task 90 and repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and reproducible results

- Complete diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..7a9452f9d7fcdc1221382e92591a5cdd0f84ad8d` — 86 files, 5,544 insertions, 167 deletions.
- Security fixtures — 16 passed.
- Architecture tests — 4 passed.
- Focused WinGet, production-workflow, reviewed-execution, batch/recovery, SQLite, cleanup and quota integration filter — 60 passed.
- Host-safe hostile script — 42 assertions passed; one documented elevated-only symbolic-link skip.
- Recorded approved elevated production-cache rerun after the root-open changes — 14 assertions passed; all file/directory/root-link and scratch-VHD sentinels survived.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 163 passed: Security 16, Architecture 4, Domain 51, Application integration 92.
- Direct production-split reproduction parsed trusted ID/version `Contoso.Widget`/`1.0` from a row whose actual ID/version fields were `Contoso.Widget`/`9.9`.
- `git diff --check` still reports trailing whitespace only in seven previously committed review/evidence Markdown lines.
- No UAC prompt, WinGet command, provider-cache mutation, installer, real update, VHD, privileged certificate operation or non-fixture deletion was invoked during this review.

## Findings

| ID | Severity | Requirement | Evidence | Finding and required remediation |
|---|---|---|---|---|
| `P3R-10` | High | `FR-UPD-02/08–09`, `NFR-MNT-03`, `AC-UPD-01–02/06/08`, `TM-01–03/11–13`, ADR-003/017 | `AdditionalLocalInventoryProviders.cs:34–52`; `WingetInstalledInventoryProvider.cs:27–89`; `InventoryWorkflow.cs:116–152`; `WingetInstalledInventoryProviderTests.cs:68–79` | The desktop now resolves scope/architecture for every installed WinGet table row by calling `ResolveExactCompatibilityAsync`. That method runs eight exact `winget list` queries and accepts `ParseTable` output as the decisive ID/version evidence. `ParseTable` splits on two spaces without header boundaries; the direct reproduction shifted approved ID/version text from a crafted display field into trusted columns while the row's actual version was different. This is execution compatibility authority expressly forbidden by ADR-003. It also multiplies every inventory record into eight sequential provider processes inside a 35-second provider timeout, yet no test constructs `WingetLocalInventoryProvider`; the passing test covers only one injected provider method call. Use a structured/programmatic authority that binds source, ID, version, scope and architecture, or keep the provider non-actionable. Remove per-record exact probing and add malformed/localized/injected-spacing, bounded-call-count, timeout, ambiguity and actual desktop-composition tests. |
| `P3R-13` | Medium | `FR-UPD-05–06`, `TM-10` | `ReviewedUpdateExecutionWorkflow.cs:32–60`; `UpdateBatchWorkflow.cs:129–139`; `MainWindow.cs:206–221`; `SequentialUpdateBatchTests.cs:49–62` | The detector now performs bounded package-suffix process-name matching and has no close authority, but it runs only inside the coordinator after the user confirms. A positive result appends `RunningApplicationWarning` and then immediately queues and executes the provider. `ConfirmAndRunAsync` returns only the final entry's message, so the detected warning is not shown before execution; the dialog contains only an unconditional generic reminder. The new test proves detection of a deliberately matching `Contoso.pwsh` suffix, not a user-visible warning or representative package/process mapping. Surface target-specific detected state before final confirmation or pause execution for explicit acknowledgement; dispose enumerated `Process` objects and add production-flow positive, ambiguous-name, exited-process and no-force-close tests. |

## Remediation disposition

| Finding | Disposition |
|---|---|
| `P3R-01–02` | Closed for the previously reported provider-path and false cross-scope/cross-architecture `Success` defects. |
| `P3R-03` | Closed: handle-relative root acquisition is structurally reviewed and the elevated production link/mount fixture is fresh for that primitive. |
| `P3R-04–09` | Closed for the previously reported clear, inventory, recovery, parser-success and mixed quota defects. |
| `P3R-10` | Open — the remediation restores production reachability by making forbidden display-table output execution compatibility authority. |
| `P3R-11–12` | Closed: active directory descendants survive and missing inactive ledger entries reconcile while missing active entries remain. |
| `P3R-13` | Open — detection is production-composed, but its result is not a user-visible pre-execution warning. |

## Mandatory evidence status

- `AC-DAT-16` is Proven within the approved host exception against the current root-open primitive.
- `AC-NFR-10` and `FR-DAT-15` are Proven for the remediated active-subtree and missing-ledger cases.
- `AC-UPD-06/08` is not Proven through an ADR-compliant production path while table output supplies scope/architecture execution compatibility.
- `FR-UPD-05` is not Proven as a detected, user-visible warning before execution.
- Passing automated counts do not override a High execution-authority defect.

## Residual risks

Clean-image behavior, per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, same-user substitution of the name-based temporary export, root creation races before handle binding, unreliable package-ID/process-name correlation, and unconstrained hostile races remain unproven within their recorded exception scope. ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
