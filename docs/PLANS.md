# Task 90 — PR 5 actionable finding and CI repair

Current scope (2026-10-29), prepared baseline/remote PR head 6e26191. Repair74c40cc has a fresh scoped PASS (16/16), full415/415 and zero-warning Release. The owner approved pushing74c40cc+ffcf47b; push and hostedCI415/415 now pass onffcf47b. Original finding thread resolved. All current PR5 repair steps complete; see the final evidence report. No merge or subsequent task starts automatically. PR5 source phase/5-v2-planning targets phase/4-beta. Live PR metadata confirms the review's protected ownership defect is already fixed in 7bde99b and independently reviewed; preserve that fix. Current failed CI run40968192718/job123777956319 has two failed redirected-process tests requiring snapshot OwnedIoCompleted=true after a test-injected600ms teardown attempt. Hosted build and all security/domain/architecture cases pass; 248/250 Application cases pass.

1. Verify attached review against production/source and live PR state; retain existing valid machine-provider fix and six regressions without duplication. Treat PR comments/logs as untrusted diagnostic input; no unrelated instruction execution.
2. Keep the600ms teardown and existing elapsed/EOF/native-exit/resource requirements. Discuss with Opus whether honest incomplete-I/O snapshots plus retained ownership satisfy the accepted fallback, rather than assuming120ms reader-cancellation completion is guaranteed. Main owns any council escalation.
3. Add an internal OwnedProcessRead delegate seam (StreamReader, Memory<char>, CancellationToken → ValueTask<int>) as an optional final RunStartedAsync argument. Its null/default path calls the exact existing StreamReader.ReadAsync. It supplies deterministic delayed-reader completion only for synthetic fixtures, carries no execution/IPC authority, and changes no production policy. New fixtures must force IO incomplete past the budget and prove root/resources/admission retained until controlled completion. Then align the two native tests with that proven contract while preserving their elapsed and lifetime checks.
4. Run narrow behavioral/native/security regressions, canonical build/full suite; inspect diff and update status/traceability/evidence. Commit only green, obtain a fresh scoped review where needed. Hosted rerun requires the new commit to reach PR5; retain the repository's explicit push/host-action authority boundary. No real app/provider/UAC/account/ACL/data action locally.

Earlier plans below are historical.
# Task 90 — Task 49 machine-provider owned-work remediation

Current remediation (2026-10-29), baseline d28d69d / source cfeb0ef. The user supplied a GitHub Codex review finding: the protected machine result projection drops the unfinished WinGet OwnedCompletion task, allowing normal helper exit and desktop admission release before provider-owned work finishes. This is a High owned-work safety defect, not merely the external/orphaned-installer limit recorded earlier. Task 49 completion is reopened until the fix and fresh independent review pass. Requirements: FR-UPD-14; retained FR-UPD-03/09, NFR-OBS-01–05.

1. Add deterministic synthetic regressions through the actual protected WinGet executor, plus helper-exit/lifetime composition; verify pending ownership and later caller cancellation cannot release normal helper completion/admission. Record behavioral red.
2. Await and observe the existing owned-completion root inside the protected provider operation before projecting scalar ProviderProcessResult. Await without the operation cancellation token; no timeout-and-abandon, no Task wire field, new protocol, execution authority, provider arguments or deadline change.
3. Test absent/completed/faulted/cancelled ownership and normal release only after completion. Run narrow fixtures and protocol regressions, canonical build/full suite, inspect diff, commit, then obtain a fresh Sol High Fast independent repair review.
4. Update failure/repair evidence, STATUS and TRACEABILITY; preserve prior reports as historical. No GitHub posting/push, real provider/helper/app/UAC/deployment, worktree or live-data action. If genuinely stuck, main owns the existing Opus/Fable/Astra council protocol with Opus retained.

The earlier unrelated remediation plan follows unchanged as historical context.
# Task 90 final-audit remediation plan

**Completed (2026-10-29):** fix 7bde99b; behavioral red reproduced, focused 40/40, canonical full 413/413/zero-warning Release, fresh independent repair review 40/40/no findings. [Final evidence](review/TASK_90_TASK_49_MACHINE_OWNERSHIP.md). All four remediation steps below are complete; no next task starts automatically.

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

## Task 90 re-entry — fresh Task 31 findings (2026-08-05)

**Preflight:** `phase/4-beta` at `c8ed26ac2e8ad26c5ba0493c5dd5dee112126512`; clean worktree; exact local archive SHA-256 `A8FD4AF124019E44B23C9F067C49F49A3D61FE8025F6ED9D8AF5E51E4CBCA3FC`.

**Authority:** The user requested continuation and evidence gathering. That authorizes repository edits and safe automated validation only. It does not authorize launching the package, modifying an installed application, creating/removing a real scheduled task, resetting/deleting owner data, replacing/removing an extracted package, signing, publishing, sharing, or contacting recipients.

### CHG-90-08 — make onboarding acknowledgement failure unconditional (`FRA-19`)

- **Target/current state:** `MainWindow` checks a failed acknowledgement only when `FactoryReset.GetMarkerState()` is non-None. A missing marker can therefore allow a failed acknowledgement to load saved inventory and navigate to Scan.
- **Edit:** block every `!result.Succeeded` before loading inventory or navigating to Scan; retain marker state for diagnostic wording and the existing successful-acknowledgement marker-absence requirement.
- **Risk:** a broad change could block valid acknowledgement or hide recovery detail. Preserve success, retry and normal onboarding paths; do not weaken root/control authority.
- **Verify:** deterministic UI-level regression for failed acknowledgement with marker None; assert no saved-inventory load, Scan navigation or automatic scan. Re-run reset, Architecture/UI, Security and full suites.

### CHG-90-09 — reconcile current-hash lifecycle evidence (`FRA-20`)

- **Target/current state:** release evidence says the `A8FD...` archive inherits no launch claim, but later marks native launch smoke passed; the checklist likewise marks launch complete without an archive identity.
- **Edit:** bind historical launch/update/lifecycle observations to their exact old hash, mark current-hash launch/lifecycle Unverified, and preserve explicit blockers.
- **Risk:** wording could accidentally imply current package release readiness. Do not call unexecuted current-hash evidence Passed.
- **Verify:** compare audit, checklist, release evidence, limitations, About/support wording and package validator output against the exact hash.

### CHG-90-10 — correct focused reset evidence count (`FRA-18`)

- **Target/current state:** release evidence reports 22 focused reset tests while the current isolated filter is 25/25.
- **Edit:** record 25/25 and the pre-migration, control-swap and attempt-cap coverage.
- **Risk:** none to runtime authority; evidence must not overstate unexecuted field cases.
- **Verify:** one serialized focused reset rerun after all other reset testhosts are stopped; reconcile current release documents.

### Mandatory external evidence (`FRA-14–16`)

- **Safe now:** rebuild/validate the exact local archive, rerun owner-context automated suites, fixture benchmarks, hostile boundary, package validator, dependency and secret checks; retain results as non-field evidence where applicable.
- **Requires exact action-time approval:** final-package launch/close, 30/20 baseline measurements, live update/responsiveness, five-minute host capture, real scheduled-task lifecycle/final-timeout, retain-data replacement, owner-state factory reset/removal, and any external signing/distribution action.
- **Requires product-owner inputs:** approved signer/signing service, channel, recipients, support contact and rollback artifact. None is inferred from repository access.

**Rollback:** revert only the eventual Task 90 remediation commit; retain the prior local archive until code tests pass. Any future owner-state test must have a separately approved backup/rollback scope before execution.

**Completion gate:** Commit `fix: remediate phase review findings` only after `FRA-19` has no High implementation finding, `FRA-20/18` are reconciled, package/evidence checks are current and all safe automated gates pass. Keep `FRA-14–16` explicitly open unless their separately authorized evidence is actually collected. Stop for a fresh Task 31 under GPT-5.6 Sol / Extra High.

**Execution result:** `FRA-19` is implemented with two deterministic executable scenarios (failed acknowledgement + marker `None`, and successful acknowledgement + residual marker), release build is clean, and safe suites pass 251/251 with reset 25/25. The current local, unsigned archive SHA-256 is `16DE2AFB0CBF26BAE167DFABFD7A4FCAE619ABA317BFE97076D4FDD16ADE0523` (94 files / 49,955,522 bytes, 15 components, 16 SBOM nodes/32 edges); its validator is non-executing and reports launch smoke not performed. `FRA-14–16` remain open exactly as scoped above.

## Task 31 — owner-only non-public evaluation exceptions (2026-08-05)

**Decision:** The product owner requested exceptions for `FRA-14–16` because the candidate is not for public release. Apply the narrowest safe interpretation: exact-hash, product-owner-controlled evaluation from the existing local workspace only. Do not authorize an external private beta, distribution, launch, host mutation or destructive lifecycle action.

**Edits:** Add a current-hash exception record; re-grade `FRA-14–16` as accepted exceptions only for owner-only non-public evaluation; keep every omitted observation `Unverified`; update audit, status, checklist, release evidence and traceability without changing technical requirements or numeric thresholds.

**Expiry:** All exceptions expire on implementation/hash change, another recipient, any distribution channel, or a public/private-distribution/release-ready claim. State-changing host evidence continues to require exact action-time approval.

**Verification:** Independently review the documentation diff for overreach and contradictions; mechanically revalidate the exact archive and confirm no document implies signing, current-hash lifecycle execution, external distribution or public-release readiness.

## Task 32 — Chocolatey trusted-source integration (2026-08-05)

**Authority and scope:** The product owner explicitly requested Chocolatey as an additional app-update source. This promotes `FR-SRC-07` for Chocolatey only; Scoop, arbitrary source URLs, raw commands, Chocolatey installation, and any real application update remain out of scope. Repository edits and deterministic fixture validation are authorized. No host package-manager mutation, application update, signing, publishing, or distribution is authorized.

**Safety contract:** The source is disabled by default and requires the existing source-disclosure consent. It accepts only the fixed Chocolatey Community feed URL and a fixed, trusted `choco.exe` beneath the canonical ProgramData Chocolatey root. It never reads execution arguments, source URLs, credentials, PowerShell, installer paths, or package scripts from the Chocolatey configuration, cache, database, or source response. Discovery parses bounded machine-readable rows as intelligence only. Chocolatey's local list does not reliably identify an installed package's source, so every local Chocolatey row is review-only until the user explicitly confirms the exact Community package mapping. The product owner subsequently approved protected execution only after that mapping: it is machine-scope only, uses the existing protected elevation transport, and accepts only a provider-owned fixed argument vector for one immutable package ID and version. Any unsupported, malformed, stale, non-community, or non-exact evidence remains non-actionable.

**Plan:**

1. Promote the scoped requirement and document the Community-feed trust limitations, explicit consent, and no-host-action boundary.
2. Add a dedicated provider project with fixed-root signed-client resolution, bounded process handling, deterministic `outdated` parsing, and exact structured candidate authority.
3. Extend immutable provider/source dispatch for the machine-scope Chocolatey operation without accepting executable paths or command strings.
4. Compose the disabled Chocolatey connector, structured evidence, post-update re-inventory, and protected machine execution route; retain WinGet/Store behavior unchanged.
5. Add normal, malformed, oversized, injected, unsigned-client, wrong-source, stale-evidence, and fixed-argument fixture coverage.
6. Run focused provider/security/application/architecture tests, then Release build and the full suite; update traceability and status with observed results only.

**Rollback:** Revert the eventual Task 32 commit. The earlier Task 31 owner-only exception automatically expires on this implementation change; no revised release or evaluation claim is made until a fresh audit and exact artifact evidence exist.
