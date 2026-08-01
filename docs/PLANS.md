# Task 90 final-audit remediation plan

**Mode/context:** APPLY then VERIFY / PERSONAL-PC repository workspace

**Approval:** Product owner explicitly approved remediation and independent Terra / High agents on 2026-08-01.

**Preflight:** `phase/4-beta` at `7f06185815c18590e13e721352f3dedae0fc051e`; clean worktree; no elevation, restart, application update, signing, publishing, task mutation, or real-data deletion approved.

**Rollback:** Before commit, every change is recoverable from the reviewed Git diff; after commit, revert the exact Task 90 commit. Retained candidate artifacts remain evidence and are not overwritten until code/tests pass.

## High findings

### CHG-90-01 — reject unexpected factory-reset root objects (`FRA-11`)

- **Target/current state:** `FactoryReset` verifies only named database/download/log targets and can report Success while another direct child of `%LocalAppData%\PCSteward` remains.
- **Edit:** enumerate the already-verified root handle without following reparse points; allow only compiled objects for the current reset phase; record every other child as `UnexpectedOwnedObject`; retain the reset marker and block onboarding.
- **Risk:** a mistaken allowlist could delete or expose old state. No deletion authority may be widened.
- **Verify:** deterministic temporary-root tests for unexpected file/directory/reparse/inaccessible/race cases, outside sentinel preservation, Partial/retry, and no onboarding before exact absence.

### CHG-90-02 — fail closed during packaged exact-task cleanup (`FRA-12`)

- **Target/current state:** `Remove-PCSteward.ps1` treats every failed `schtasks /Query` as absence and does not verify absence after deletion.
- **Edit:** use a language-independent exact Task Scheduler query, distinguish exact absence from every error, delete only `PCSteward.ScheduledCheck`, and require exact post-delete absence before Success.
- **Risk:** false absence leaves an app-owned task; broad authority could alter unrelated tasks.
- **Verify:** deterministic absent/present/query-error/delete-error/residual/unrelated-task tests plus the existing opt-in supported-host isolation check.

### CHG-90-03 — complete mandatory performance evidence (`FRA-14`)

- **Target/current state:** `AC-NFR-01/02/06/07/11` and `NFR-PERF-01–03/05–07` remain partly Unverified.
- **Action:** re-run only safe existing fixture/package/performance tools first; identify exact final-app/live/idle/process-tree/final-timeout evidence that needs an approved interactive host action. Do not infer Pass from fixtures or exceptions.
- **Risk:** long-running or live measurements can disrupt the owner PC; an update run changes external state and is not approved here.
- **Verify:** raw 30/20, responsiveness, five-minute idle CPU/memory/disk/network, scan process-tree, and scheduled completion/cancellation/final-timeout records on the approved baseline, or retain an explicit blocker.

### CHG-90-04 — complete destructive and standalone lifecycle evidence (`FRA-15`)

- **Target/current state:** reset task-present/absent/residual, replacement/removal, reset-then-removal, and final-timeout observations remain incomplete.
- **Action:** run automated disposable-root/package tests after CHG-90-01/02. Any real factory reset, real task mutation, or package replacement requires a separately confirmed action-time scope and must preserve unrelated state.
- **Risk:** destructive evidence can remove user state or scheduled tasks; do not execute it by inference from this repository-edit approval.
- **Verify:** exact final hash, before/after state, task identity, outside sentinel, process exits, onboarding/zero-state, retained-data replacement, rollback, and zero helper/UAC evidence; otherwise retain an explicit blocker.

### CHG-90-05 — release signer/channel controls (`FRA-16`)

- **Target/current state:** no approved production signer, distribution channel, recipients, support contact, or rollback artifact exists.
- **Action:** no repository code can manufacture these controls. Keep the archive local-owner-only and record the external dependency accurately.
- **Risk:** creating a certificate/channel or distributing an unsigned archive would exceed authority and invalidate the evidence boundary.
- **Verify:** only an independently trusted exact signed hash plus approved channel/recipient/support/rollback evidence can close this finding.

## Mandatory evidence and Medium finding

### CHG-90-06 — implement bounded reset marker progress (`FRA-13`)

- **Target/current state:** the marker contains only schema version/state and cannot durably represent attempt/category results.
- **Edit:** add reset ID, requested UTC, attempt count, fixed state/category/result enums, atomic per-category progress, strict bounds, and no paths/deletion authority.
- **Risk:** incompatible or permissive parsing could load old state or widen deletion.
- **Verify:** schema/version/enum/bounds, malformed/unknown, temporary-only crash, per-category progress, repeated attempt, Partial/retry, and normal-shell exclusion tests.

## Low finding

### CHG-90-07 — reconcile release-facing limitations (`FRA-17`)

- **Target/current state:** limitations omit the proven VS Code current-user update and cite the superseded Task 29 performance exception.
- **Edit:** describe the update evidence narrowly; cite the current Task 90 boundaries; retain all unresolved release blockers without claiming readiness.
- **Risk:** stale or over-broad wording can mislead an owner about release, signing, privilege, or performance status.
- **Verify:** consistency search across About/support, limitations, release notes, checklist, owner exceptions, audit, and exact archive scope.

## Integration and completion gate

1. Review each isolated diff for scope, unsafe path/command authority, secrets, and missing tests.
2. Run narrow tests, then hostile/security/architecture checks, package validator/benchmark, Release build, and full suite.
3. Update `FINAL_RELEASE_AUDIT.md`, `TRACEABILITY.md`, and `STATUS.md` with only observed results and the fixing commit/evidence handoff.
4. Commit as `fix: remediate phase review findings` only if no Blocker/High implementation finding remains in the integrated diff and all required automated gates pass. External/interactive evidence that cannot be collected stays explicitly blocked; never convert it to Pass.
5. Stop after Task 90. The next permitted task is a fresh Task 31 under GPT-5.6 Sol / Extra High.

## Applied changes and verification

| Change | Result | Evidence |
|---|---|---|
| `CHG-90-01` / `FRA-11` | Resolved in implementation | Exact owned-root residuals, pre-migration onboarding authority, late promotion/acknowledgement injection, file/directory/reparse/hard-link collisions, outside sentinel and retry coverage pass; independent re-review found no remaining finding. |
| `CHG-90-02` / `FRA-12` | Resolved in implementation | Exact COM setup/query/delete/re-query contract distinguishes only `GetTask` file-not-found as absence; contract and independent wrapped-HRESULT review pass. |
| `CHG-90-03` / `FRA-14` | External/field evidence still required | Fixture 30/20 runs pass but are explicitly not final-app/live/idle/process-tree evidence. No live update, five-minute host capture, or final-timeout task action was authorized. |
| `CHG-90-04` / `FRA-15` | External/destructive field evidence still required | Disposable automated reset/cleanup tests pass. No real owner-state reset, task mutation, retained-data replacement, or reset-then-removal was authorized. |
| `CHG-90-05` / `FRA-16` | External authority still required | No signer, channel, recipients, support contact, or rollback artifact was created or inferred. |
| `CHG-90-06` / `FRA-13` | Resolved in implementation | Strict bounded marker, exact 99,999/100,000 attempt behavior, atomic progress, explicit Partial, recovery transitions and progress-write failure tests pass; independent re-review found no finding. |
| `CHG-90-07` / `FRA-17` | Resolved in release text | Known limitations and release notes now describe the narrow VS Code evidence and current hash/exception boundary consistently. |

Verification: reset 25/25; cleanup contract Pass; Domain 55, Application 157, Architecture 17, Security 20 — **249/249 full suite**; hostile path boundary 42 assertions with one approved manual symbolic-link skip; WinGet boundary 11; Release build 0 warnings/errors. The mechanically validated unsigned local archive has 94 files / 49,950,826 bytes, 15 components, 16 SBOM nodes/32 edges and SHA-256 `A8FD4AF124019E44B23C9F067C49F49A3D61FE8025F6ED9D8AF5E51E4CBCA3FC`.

Overall verification result: **Improved**. The implementation findings are remediated; release readiness remains blocked by `FRA-14–16` pending separately authorized field/external evidence. Rollback remains a revert of the exact Task 90 commit. No machine configuration, real scheduled task, owner data, application version, signer, channel, or external recipient was changed.
