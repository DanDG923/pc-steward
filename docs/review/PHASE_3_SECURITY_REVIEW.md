# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head reviewed:** `5013c942ae2fa0dc62c9bf71258bcbffedd03347`
**Final remediation reviewed:** `b24d48ee0874c98ce7b9c9d783e2e34dc392a6bc..5013c942ae2fa0dc62c9bf71258bcbffedd03347`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-29

## Gate result

`PASS`

All fifteen Phase 3 findings are closed at the exact reviewed implementation commit. No Blocker, High or Medium finding remains. `P3R-14` now has a bounded official WinGet COM authority in the actual desktop and current-tree evidence through review, immutable runner construction and exact post-update verification. `P3R-15` now has production-boundary evidence for the real default transport and trust validator: one protected signed same-signer UAC no-op completed, five actual hostile layouts produced zero helper launches, and exact root, temporary-root and certificate teardown is independently corroborated.

Phase 3 is merge-ready. This review does not merge, push or begin Task 26; Task 26 remains blocked until the Phase 3 branch is merged through the separately authorized repository workflow.

## Fresh review scope and results

- Complete Phase 3 diff: `1b6389d..5013c94` — **109 files, 7,736 insertions, 210 deletions across 33 commits**.
- Final remediation diff: `b24d48e..5013c94` — **35 files, 1,331 insertions, 59 deletions**.
- Focused production authority/review/batch/WinGet/cache matrix — **61/61 passed**.
- Security fixtures — **19/19 passed**.
- Architecture fixtures — **5/5 passed**.
- Canonical Release build — **0 warnings, 0 errors**.
- Canonical full suite — **182/182 passed:** Security 19, Architecture 5, Domain 51, Application integration 107.
- Host-safe hostile deletion/IPC boundary — **42 assertions passed; 1 documented elevated-only symbolic-link case skipped**.
- Host-safe WinGet boundary — **11/11 assertions passed**; no update was executed.
- NuGet vulnerability audit — **0 known vulnerable direct or transitive packages across 17 projects** from the configured NuGet source.
- Pinned COM package trust — `Microsoft.WindowsPackageManager.ComInterop` `1.10.340` resolved exactly; its local package has valid Microsoft author and NuGet repository signatures.
- `git diff --check` passed before documentation edits; the implementation worktree began clean at the exact reviewed commit.

The first sandbox-identity focused attempt produced 52 passes and nine cache failures because that restricted identity inherited a temporary directory it could not fully modify. The identical 61-test command passed under the normal Windows identity, as did every canonical gate. This was an execution-environment ACL artifact, not a product failure.

## `P3R-14` independent closure audit

`WingetStructuredUpdateCandidateAuthority` is the only production implementation of `IStructuredUpdateCandidateAuthority`; neither display connector implements that interface. Its default constructor selects the production COM query and a 20-second bound. It accepts only the literal predefined source IDs `winget` and `msstore`, a bounded package ID and bounded versions; invalid input never reaches COM.

The production query:

- uses `Microsoft.Management.Deployment.PackageManager` from the pinned official `Microsoft.WindowsPackageManager.ComInterop` projection;
- selects only `OpenWindowsCatalog` or `MicrosoftStore` through `GetPredefinedPackageCatalog`;
- requires `PackageCatalogOrigin.Predefined`, exact expected catalog name and `PackageCatalogTrustLevel.Trusted`;
- applies one `PackageMatchField.Id` / `PackageFieldMatchOption.Equals` filter with `ResultLimit = 2`;
- accepts only `Ok`, non-limit-exceeded, exactly-one results whose returned match criteria remain exact;
- binds the returned package ID, installed version, default candidate version and candidate catalog source to the requested source/package;
- requires `IsUpdateAvailable` and a deterministic `VersionComparisonOutcome.Newer`; and
- returns no authority on zero, duplicate, fuzzy, wrong-source, wrong-ID, untrusted, malformed, equal, older, limit, activation/API/catalog error or non-caller timeout, while caller cancellation remains cancellation.

`App.xaml.cs` composes this authority beside the display-only `WingetUpdateSourceConnector` instances and the separate `WingetPostUpdatePackageInventory`. `InventoryWorkflow` independently requires the structured source, package ID, installed version and candidate version to equal the fresh actionable mapping and concrete current local product evidence before it creates an immutable `winget-com-v1` selection. Review and confirmation repeat preparation, so the production-composition fixture observes two structured queries before one runner call.

`ProductionStructuredAuthorityComposesThroughReviewRunnerAndPostUpdateVerification` proves the current production authority class around a controlled COM observation reaches explicit review and runner construction. A zero provider result becomes Success only after exact bounded WinGet export source/ID/version evidence and one concrete registry/MSIX version/scope/architecture target match the immutable selection. Wrong export, identity, scope, architecture, ambiguity or missing expected version cannot become Success. This proves the present `AC-UPD-06/08` path without treating display-table text as execution or verification authority.

## `P3R-15` independent closure audit

The parameterless `WindowsElevatedHelperTransport` binds directly to `InstalledHelperLaunchTrust.ResolveFixedPaths`, `InstalledHelperLaunchTrust.Validate` and the production `runas` launcher. It resolves only the actual desktop at exact `%ProgramFiles%\PC Steward\PCSteward.Desktop.exe` and its exact sibling `PCSteward.ElevatedHelper.exe`. Validation checks fixed layout and presence, every installed-root ancestor and both binaries for reparse state, effective current-token modification/replacement/DACL/owner rights, Authenticode trust and exact signer-certificate equality. It runs once before pipe creation and again immediately before the launcher. The local-only current-user/SYSTEM pipe and post-connect PID/path/session/SID/signer checks remain defence in depth.

The installed-helper evidence projects reference the production `PCSteward.Execution` assembly. The desktop fixture invokes the parameterless default transport except for the privileged between-validations hook; that overload still binds the real resolver, validator and launcher. The helper invokes the production one-shot entry point, protocol and peer checks, substituting only a friend-only no-op provider delegate so no software update occurs.

The durable host record `docs/review/TASK_90_P3R15_HOST_EVIDENCE.json` has RunId `c27f884b52e944e9a569def8d1dae073` and SHA-256 `4639a42388358b0c0f6ace05bdcca782a2346d6c9b8b32771ead4e591c6d0e7e`. Its six cases are internally consistent:

- protected root owner `BUILTIN\Administrators`; SYSTEM/Administrators full control and Users read/execute;
- root, desktop and helper non-reparse; both signatures `Valid`; exact signer thumbprints equal;
- clean signed protected UAC no-op returned `Completed`, exit `0`, and the helper-start marker was present;
- tampered signature, second trusted signer, Users-modifiable layout, helper symbolic link and privileged replacement between the two real validations each returned the expected production diagnostic with the helper-start marker absent; and
- the evidence driver returned zero for all six cases only after the expected positive or expected fail-closed result was observed.

Fresh read-only host checks independently find the exact `%ProgramFiles%\PC Steward` root absent, the exact RunId temporary root absent, and both recorded certificate thumbprints at zero entries across CurrentUser `My`, `Root` and `TrustedPublisher`. The record's exact teardown claim is therefore corroborated without re-mutating the host.

## Complete `P3R-01`–`P3R-15` disposition

| Finding | Final independent disposition at `5013c94` |
|---|---|
| `P3R-01` | Closed. Machine WinGet resolution remains machine-rooted, non-reparse, Authenticode/signer checked and fail-closed; provider and WinGet boundary fixtures pass. |
| `P3R-02` | Closed. Success requires exact structured source/ID/version plus one exact concrete local version/scope/architecture target; cross-target negatives pass. |
| `P3R-03` | Closed within the approved host record. Production cache root/descendant acquisition remains handle-relative and open-only; the final remediation does not alter the cache primitive, and the 14-assertion elevated link/mount/swap record remains applicable. |
| `P3R-04` | Closed. Owned-root enumeration, orphan reconciliation and residual accounting prevent false cleanup Success. |
| `P3R-05` | Closed. Actual local inventory is refreshed at review and confirmation; changed, conflicting or unavailable evidence stops before job/provider construction. |
| `P3R-06` | Closed. Started or interrupted work remains `VerificationRequired`; only proven-unstarted work is queued/cancelled. |
| `P3R-07` | Closed. Safe inactive crash-orphan bytes participate in quota measurement and eviction. |
| `P3R-08` | Closed. Display tables cannot authorize post-update Success; exact bounded export JSON is required. |
| `P3R-09` | Closed. Registered inactive artifacts and safe crash orphans share deterministic oldest-first eviction. |
| `P3R-10` | Closed. Per-row compatibility probes remain removed; installed table inventory is display-only/`Unknown`, and production performs one bounded display call. |
| `P3R-11` | Closed. An active directory ledger entry protects its complete descendant subtree. |
| `P3R-12` | Closed. Missing inactive ledger entries reconcile; missing active entries remain protected. |
| `P3R-13` | Closed. Initial/confirmation warnings are visible, and a first positive final snapshot stops before queue/provider execution with target-specific acknowledgement and no close/kill authority. |
| `P3R-14` | Closed. The official bounded COM authority is production-composed and exact-source/ID/installed/candidate bound through review, runner and post-update verification; display text has no executable authority. |
| `P3R-15` | Closed. The real fixed-root validator/default transport has one protected signed successful UAC path, five production-diagnostic zero-launch hostile cases and independently corroborated exact teardown. |

**Finding count:** 15 closed; 0 open; 0 Blocker; 0 High; 0 Medium.

## Evidence and traceability judgment

- `FR-UPD-02–04/08–09`, `AC-UPD-01–02/04/06/08`, `NFR-MNT-03` and `TM-01–04/06/12–13` are Proven for the reviewed Phase 3 boundaries described above.
- `AC-DAT-16`, `AC-NFR-10` and `FR-DAT-15` remain Proven for the current cleanup primitive and active/orphan/ledger cases.
- `FR-UPD-05–06` remains Proven for all three running-target snapshots and the absence of force-close authority.
- ADR-002, ADR-003 and ADR-011 correctly remain Provisional for later lifecycle/field evidence; their status is not a Phase 3 blocker.
- The Phase 3 readiness report, ADR-002/003/018, threat model, Task 90 summary and raw evidence remain immutable pre-review handoffs with a consistent code/evidence boundary; `docs/STATUS.md` and `docs/requirements/TRACEABILITY.md` now record this fresh verdict.

## Accepted residual risks

The PASS does not claim a clean-image traditional-installer upgrade/uninstall lifecycle, a new real external software update, a new live external-source COM query, clean-host per-user no-prompt behavior, provider-disconnect cancellation, unconstrained administrator/OS-compromise races, same-signer rollback prevention, immunity to same-user substitution of the name-based temporary WinGet export, perfect package-ID/process-name correlation or replay of the historical outcome dialog after its app crash. The host-safe hostile script retains one documented elevated-only symbolic-link skip; the immutable 14-assertion elevated production-cache record covers the unchanged final primitive. `AC-UX-01–09` remain Optional under ADR-021.

These are accepted, bounded follow-up risks and do not reopen a Phase 3 Blocker, High or Medium finding.
