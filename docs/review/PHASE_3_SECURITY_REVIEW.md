# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `117b25b27f204bf5d646d9d5cd9df89dd0c5bcab`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`FAIL`

The repeat remediation closes the previously reported scope/architecture binding, current-inventory confirmation, interrupted-update recovery, NT open-disposition, and orphan-inclusion defects. The gate still fails because the mandatory hostile cleanup evidence does not run the production primitive through symbolic-link, volume-mount, or concurrent ancestor-swap cases. A new High finding also shows that display-oriented WinGet table parsing can authorize a post-update `Success`. Quota eviction includes crash orphans now, but does not evict them oldest-first.

Do not merge Phase 3 or begin Task 26. Run Task 90 and repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and reproducible results

- Complete diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..117b25b27f204bf5d646d9d5cd9df89dd0c5bcab` — 78 files, 5,037 insertions, 155 deletions.
- Security fixtures — 16 passed.
- Architecture tests — 4 passed.
- Focused update, verification, recovery, and cache integration filter — 51 passed.
- Host-safe hostile script — 42 assertions passed; the symbolic-link VM/manual case remained skipped.
- Direct reproduction of the production split options parsed `Evil  Contoso.Widget  1.1.0  Contoso.Widget  1.0.0  winget` as trusted ID `Contoso.Widget` and version `1.1.0`, while the actual row version remained later in the row.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, 0 warnings, 0 errors.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — 154 passed: Security 16, Architecture 4, Domain 51, Application integration 83.
- `git diff --check` reports trailing whitespace in seven previously committed review/evidence Markdown lines; no production source whitespace error was reported.
- No UAC prompt, WinGet mutation, provider cache mutation, installer, real update, VHD, privileged certificate operation, or destructive host test was invoked.

## Findings

| ID | Severity | Requirement | Evidence | Finding and required remediation |
|---|---|---|---|---|
| `P3R-03` | Blocker | `AC-DAT-16`, `TM-05–06`, ADR-019 | `DownloadedArtifactCache.cs:226–296`; `DownloadedArtifactCacheTests.cs:104–175`; hostile scripts under `spikes/security-boundaries` | NT `FILE_OPEN` and handle-relative descendant opens fix the earlier disposition defect and strengthen the implementation. The production tests still use only static junctions: the “repeated ancestor” test creates a junction before each cleanup call rather than swapping an ancestor after validation/open. The root test rejects a junction during construction, not a runtime root replacement. No production symbolic-link or volume-mount fixture ran, while the hostile script still exercises the older spike primitive and skipped its symbolic-link case. Run the production cache primitive against final/root links, a volume mount, and deterministic repeated ancestor/root swaps; require every outside sentinel to survive and residual/recovered-byte accounting to remain exact. |
| `P3R-08` | High | `FR-UPD-08–09`, `AC-UPD-06/08`, `TM-01/12`, ADR-003/017 | `WingetInstalledInventoryProvider.cs:39–80`; `WingetInstalledInventoryProviderTests.cs:47–57` | `HasExactExpectedVersionAsync` authorizes `Success` from `ParseTable`, which splits every row on two spaces and treats fields 0, 1, 2 and the last field as name, ID, version and source without using header boundaries or validating the remaining columns. A display field containing double-space-delimited approved ID and target-version text can shift those values into the trusted positions even when the row's actual version differs. This contradicts ADR-003’s rule that table output may inform display only and never authorize execution. Bind post-update version evidence to the exact approved package through a non-display authority, fail closed on ambiguous/malformed/localized output, and add injected-spacing, extra-column, truncation, and negative production-workflow tests. |
| `P3R-09` | Medium | `NFR-PERF-10`, `AC-NFR-10`, `TM-11/15`, ADR-011 | `DownloadedArtifactCache.cs:63–105`; `DownloadedArtifactCacheTests.cs:88–102` | Registered inactive artifacts are considered by `CreatedAt`, but safe unregistered crash orphans are appended later and ordered by directory flag then relative path. With multiple over-quota orphans, a newer alphabetically earlier object can be deleted before an older one. The sole orphan quota test contains one orphan and cannot prove ordering. Build one oldest-first candidate order across inactive registered artifacts and safe orphans, then add mixed and multiple-orphan ordering tests with exact residual accounting. |

## Remediation disposition

| Finding | Disposition |
|---|---|
| `P3R-01` | Closed for this gate: provider resolution remains machine-rooted, non-reparse, signature/signer checked, and the recorded host inspection found no caller-write rule. |
| `P3R-02` | Closed for the reported same-name scope/architecture collision: the production workflow now combines exact ID/source/version query evidence with fixed scope/architecture inventory checks. The newly discovered table-authority defect is `P3R-08`. |
| `P3R-03` | Open — mandatory production hostile evidence is Missing; release blocker. |
| `P3R-04` | Closed: explicit clear reconciles safe orphans and reports undeleted objects as residuals rather than false success. |
| `P3R-05` | Closed: production composition rescans before review and confirmation, and a version change between those points blocks job creation and execution. |
| `P3R-06` | Closed: cancellation after provider start and persisted interrupted `Running` state become `VerificationRequired`, never plain Queued/Cancelled. |
| `P3R-07` | Closed for orphan quota inclusion. Oldest-first ordering across those objects is separately open as `P3R-09`. |

## Mandatory evidence status

- `AC-DAT-16` is Missing for the remediated production primitive. The approved host substitute does not waive production hostile testing, and the executed “repeated” junction fixture is not a concurrent swap.
- `AC-UPD-06/08` is not Proven through a fail-closed production outcome path while display-oriented table parsing can supply the decisive expected-version record.
- `AC-NFR-10` is not Proven for multiple crash orphans and is contradicted by the current path-based orphan ordering.
- All safe automated suites were rerun successfully. Passing counts do not override a Blocker, High false-Success path, or missing mandatory evidence.

## Residual risks

Clean-image behavior, per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer helper/provider rollback, and unconstrained hostile race behavior remain unproven within their recorded exception scope. ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
