# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `30ba65ae5f461fd5526b63a44eb77d765c10b6a2`
**Remediation reviewed:** `9bc817f5a2da74385b31eb9412086c9e5f112a28..30ba65ae5f461fd5526b63a44eb77d765c10b6a2`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`FAIL`

The remediation removes the previously executable display-table candidate authority and correctly stops a newly detected final running target before queue/provider execution. The pre-`runas` implementation also fails closed on this development tree. Phase 3 still cannot merge: the actual desktop supplies no structured candidate authority, so no current production update can reach review or execution and the mandatory current-tree update-verification path is unproven; and the `P3R-15` trust boundary has neither production-path negative fixtures nor the required clean signed protected-root positive/UAC evidence.

Do not merge Phase 3 or begin Task 26. Run Task 90 for the remaining `P3R-14` gate evidence and `P3R-15` evidence blocker, then repeat Task 25 under a fresh GPT-5.6 Sol / Extra High review.

## Scope and fresh reproducible results

- Complete Phase 3 diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc..30ba65ae5f461fd5526b63a44eb77d765c10b6a2` — 92 files, 6,454 insertions, 195 deletions.
- Remediation diff: 19 files, 658 insertions, 48 deletions.
- Focused current-inventory, candidate-authority, reviewed-workflow, batch, WinGet and cache filter — **56 passed**.
- Security fixtures — **19 passed**.
- Architecture tests — **4 passed**.
- Host-safe hostile deletion/IPC script — **42 assertions passed; one documented elevated-only symbolic-link case skipped**.
- Host-safe WinGet boundary script — **11 assertions passed**.
- `pwsh -NoProfile -File .\scripts\Build.ps1` — succeeded, **0 warnings, 0 errors**.
- `pwsh -NoProfile -File .\scripts\Test.ps1` — **176 passed:** Security 19, Architecture 4, Domain 51, Application integration 102.
- Fresh read-only trust inspection — `%ProgramFiles%\PC Steward` absent; current Release desktop/helper and helper output directory non-reparse but both executables `NotSigned`; the current caller could open each executable for write and each inspected path had eight write-capable allow entries. No UAC was invoked.
- Static production composition — `App.xaml.cs` supplies `WingetUpdateSourceConnector` display connectors and `WingetPostUpdatePackageInventory`, but no `IStructuredUpdateCandidateAuthority`; `PrepareApprovedUpdateSelectionAsync` therefore returns no selection before review/job/runner construction.
- Static final-target flow — a positive coordinator snapshot records only `RunningApplicationWarning` and returns `AcknowledgementRequired` before `Queued`, `Running`, or executor calls; a fresh review and explicit acknowledgement are required to continue.
- Static elevation flow — fixed-path resolution and trust validation occur before pipe creation, are repeated immediately before the launcher, and the connected-process identity check remains after launch.
- `git diff --check` passed before this report was edited. The worktree was clean after all validation.
- No WinGet update, installer, UAC prompt, certificate mutation, VHD, provider-cache mutation or non-fixture deletion was invoked.

## Remaining findings and mandatory evidence gaps

| ID | Severity | Requirement | Evidence | Finding and required closure |
|---|---|---|---|---|
| `P3R-14` | High gate gap | `FR-UPD-02–03/08–09`, `AC-UPD-01–02/06/08`, `NFR-MNT-03`, `TM-01–03/12–13`, ADR-003/017 | `App.xaml.cs:29–38`; `InventoryWorkflow.cs:109–146`; `InventoryWorkflowUpdateSourceTests.cs`; prior Firefox record | The unsafe display-table-to-`--version` path is removed: a separate exact structured authority is required and table connectors do not implement it. However, the actual desktop injects no authority at all, so every apparent update stops before review and execution. The approved Firefox exercise predates this gate and used the now-rejected authority path; it cannot prove `AC-UPD-06` or the present production composition. Supply a supported bounded structured candidate source bound to exact source/package/version, wire it into the actual desktop, retain all malformed/wrong-binding negatives, and capture current-tree composed positive evidence through review, runner and post-update verification. Until then the remediation is safe but Phase 3's mandatory update path is absent. |
| `P3R-15` | Blocker | `FR-UPD-02/04`, `AC-UPD-02/04`, `TM-03–04/06`, ADR-002/018 | `WindowsElevatedHelperTransport.cs:25–103, 213–386`; `ElevatedHelperSecurityTests.cs:209–280`; fresh host inspection; `TASK_90_P3R7_EVIDENCE.md` | The implementation now checks fixed layout, reparse ancestry, effective current-token modification rights, trusted signatures and exact signer equality twice before `runas`, and current binaries fail closed. Evidence is not adequate to close the finding. `PreLaunchTrustRejects...` evaluates caller-supplied booleans; `PreLaunchRejectionAndReplacement...` injects both validator and launcher; and the temporary unsigned fixture calls individual helpers rather than the production `InstalledHelperLaunchTrust.Validate`/transport path. There is no real wrong-signer, reparse, writable-layout or replacement fixture proving the production validator prevents the launcher/UAC call, and no clean signed `%ProgramFiles%\PC Steward` non-caller-writable success path reaching the approved no-op UAC boundary. Run those negative and positive cases in an approved isolated environment, record exact ACL/owner/reparse/signature/signer evidence and zero-launch assertions, and verify teardown. |

## Complete `P3R-01`–`P3R-15` disposition

| Finding | Independent disposition at `30ba65a` |
|---|---|
| `P3R-01` | Closed. Machine WinGet resolution remains machine-rooted, non-reparse, Authenticode/signer checked and fail-closed; provider boundary fixtures pass. |
| `P3R-02` | Closed. Post-update success requires exact structured source/ID/version plus one exact concrete local version/scope/architecture target; cross-target negatives pass. |
| `P3R-03` | Closed within the recorded approved host evidence. Handle-relative, open-only root/descendant acquisition and the current production primitive retain the 14-assertion link/mount/swap record; no cache primitive changed in `30ba65a`. |
| `P3R-04` | Closed. Owned-root enumeration, orphan reconciliation and residual accounting prevent false cleanup Success. |
| `P3R-05` | Closed. Actual local inventory is refreshed at review and confirmation; changed/unavailable evidence stops before job/provider construction. |
| `P3R-06` | Closed. Started/interrupted work remains `VerificationRequired`; unstarted work alone is queued/cancelled. |
| `P3R-07` | Closed. Safe inactive crash-orphan bytes participate in quota enforcement. |
| `P3R-08` | Closed. Display tables cannot authorize post-update Success; exact bounded export JSON is required. |
| `P3R-09` | Closed. Registered inactive artifacts and safe crash orphans share one deterministic oldest-first eviction order. |
| `P3R-10` | Closed. Per-row compatibility probes are removed, table inventory remains display-only/`Unknown`, and actual composition makes one bounded display call. |
| `P3R-11` | Closed. An active directory ledger entry protects its entire descendant subtree. |
| `P3R-12` | Closed. Missing inactive ledger entries reconcile; missing active entries remain protected. |
| `P3R-13` | Closed. Initial/confirmation warnings are user-visible, and a first positive final snapshot stops before queue/provider execution with target/process-specific evidence and no close/kill authority. |
| `P3R-14` | Security defect treated, gate evidence open. Table text no longer supplies an executable version, but the current desktop has no actionable structured candidate route or current-tree positive update evidence. |
| `P3R-15` | Implementation treated, evidence open. The pre-launch policy is structurally present and fails closed locally, but required production-boundary negative fixtures and clean protected signed installed-path/UAC evidence are missing. |

## Mandatory evidence judgment

- `AC-DAT-16`, `AC-NFR-10` and `FR-DAT-15` remain Proven for the current cleanup primitive and remediated active/orphan/ledger cases.
- `FR-UPD-05–06` is Proven for the bounded detector and all three snapshots; no force-close surface exists.
- `AC-UPD-06/08` is not Proven through the actual current desktop because no production structured candidate authority can create a reviewed job.
- `AC-UPD-04` and `TM-04/06` are not Proven for the new pre-launch boundary. Local rejection is necessary but does not substitute for a clean protected signed positive path and production-path negative hostile fixtures.
- Task 25's rule says a missing mandatory production/hostile evidence row fails the gate regardless of passing automated counts.

## Residual risks and approved deferrals

The recorded host substitute and personal-use UI waiver retain only their narrow historical scope. Per-user no-prompt execution, provider-disconnect cancellation, the un-replayed outcome dialog, same-signer rollback, same-user substitution of the name-based temporary export, root-creation and unconstrained hostile races, and imperfect package-ID/process-name correlation remain residual risks. None waives the two mandatory gaps above. ADR-002, ADR-003 and ADR-011 remain Provisional; `AC-UX-01–09` remain Optional under ADR-021.
