# Phase 3 security review

**Phase:** Phase 3 — Safe update execution and app-owned cache
**Base commit:** `1b6389deb1926a3134322da9760b7ef9ceb6fdbc`
**Head commit:** `949fc3be25f675eec05e32cf40e493d099746a09`
**Reviewer model/effort:** GPT-5.6 Sol / Extra High
**Date:** 2026-07-28

## Gate result

`FAIL`

Two Blocker and two High findings remain. The elevated helper can execute a user-controllable WinGet alias at high integrity, the owned-cache delete still has the explicitly unresolved ancestor-component race, the production post-update path cannot prove the expected version, and cleanup can report Success while orphaned app-owned files remain. Passing fixture tests do not close these composed-path failures.

## Evidence reviewed

- Requirements and acceptance criteria: `docs/requirements/PHASE_3.md`, including `FR-UPD-01–11`, `FR-HIS-01–06`, `FR-DAT-08–15`, `AC-UPD-01–11`, `AC-DAT-08–17`, `NFR-REL-03`, `NFR-OBS-01–05`, `NFR-PERF-10` and `AC-NFR-10`.
- ADRs: ADR-003, ADR-010, ADR-011 and ADR-015–019.
- Threat and prior hostile evidence: `docs/architecture/THREAT_MODEL.md`, `spikes/security-boundaries/SPIKE_REPORT.md`, both hostile-root scripts, the host-evidence harness, `docs/review/PHASE_3_HOST_TEST_EXCEPTION.md`, and `docs/review/PHASE_3_EVIDENCE_READINESS.md`.
- Complete diff: `1b6389deb1926a3134322da9760b7ef9ceb6fdbc...949fc3be25f675eec05e32cf40e493d099746a09` (74 files, 4,378 insertions and 123 deletions).
- Focused tests: Security fixture 16 passed; Architecture 4 passed; update/source/workflow integration filter 21 passed; downloaded-artifact cleanup 4 passed.
- Host-safe hostile-root script: 42 assertions passed with the symbolic-link case skipped; no UAC, WinGet, provider cache, installer or real update was invoked.
- Release build: succeeded with zero warnings and zero errors.
- Full Release suite: 142 passed — Security 16, Architecture 4, Domain 51 and Application integration 71.
- Local read-only alias inspection: `%LocalAppData%\Microsoft\WindowsApps` and its zero-length reparse `winget.exe` alias grant the current standard user inherited `FullControl`.

## Findings

| ID | Severity | Requirement | File/line | Finding | Required remediation | Verification |
|---|---|---|---|---|---|---|
| `P3R-01` | Blocker | `FR-UPD-02–04`, `AC-UPD-02/04`, `TM-03–04`, ADR-003/018 | `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:218–280`; `src/PCSteward.Execution/WindowsElevatedHelperTransport.cs:283–284` | The elevated helper reconstructs fixed arguments but starts WinGet from `%LocalAppData%\Microsoft\WindowsApps\winget.exe`. That alias and parent directory are writable by the same standard user, the alias is a reparse point, and no path, reparse, package identity, signature or signer check is applied to the provider executable. A same-user process can substitute the executable before a reviewed machine update and have the signed helper run it at high integrity after the user accepts PC Steward's UAC prompt. Fixed arguments do not prevent this arbitrary-elevation path. | Resolve the elevated provider only through a machine-trusted, non-user-writable identity and fail closed on path/reparse/package/signature/signer mismatch. Keep provider resolution inside trusted code and do not fall back to PATH or the current-user alias. | Add hostile alias substitution/reparse/unsigned/wrong-signer fixtures and a Windows identity/ACL test proving the accepted executable cannot be modified by the standard caller. Repeat the signed cross-integrity helper exercise without invoking a real update unless separately approved. |
| `P3R-02` | High | `FR-UPD-08–09`, `AC-UPD-06/08`, `TM-12`, ADR-017 | `src/PCSteward.Desktop/AdditionalLocalInventoryProviders.cs:42–49`; `src/PCSteward.Application/InventoryWorkflow.cs:104–120`; `src/PCSteward.Provider.WinGet/WingetInstalledInventoryProvider.cs:32–43`; `tests/PCSteward.Application.IntegrationTests/ReviewedUpdateExecutionWorkflowTests.cs:63–65` | The production WinGet inventory adapter always stores scope and architecture as `Unknown`, while post-update verification requires that same WinGet row to equal the approved `User`/`Machine` scope and concrete architecture. Therefore `ExpectedVersionDetected` cannot become true through the production path. The Success tests replace the real verifier with an always-verified stub. Production also uses the broad, delimiter-split `winget list` table rather than the exact ID/source query recorded in the Firefox evidence, so the direct host query does not prove the app's parser or outcome path. | Implement targeted, bounded, fail-closed post-update inventory for the exact approved source/package and reconcile scope/architecture using trusted concrete local evidence without relaxing compatibility checks. Do not make the broad display-table parser success authority. | Add a composed production-path regression from provider output through persisted inventory to `ReInventoryAsync`: exact expected identity/version succeeds; wrong ID/source/scope/architecture, malformed columns, injected spacing, exit-zero/no-change and unavailable inventory never succeed. Re-run an approved isolated update when a suitable fixture is available. |
| `P3R-03` | Blocker | `AC-DAT-16`, `TM-05–06`, ADR-019 | `src/PCSteward.Storage/DownloadedArtifactCache.cs:111–165`; `spikes/security-boundaries/SPIKE_REPORT.md:33–35/60` | Cleanup validates ancestor components by path and then calls `CreateFileW` on the full path with `FILE_FLAG_OPEN_REPARSE_POINT`. That flag binds only the final object; an ancestor can still be replaced by a junction between validation and handle acquisition. The spike explicitly says this unresolved race is a Task 25 release blocker. Host evidence exercised a deterministic final-component swap in the spike implementation, not a repeated ancestor swap against the production cache primitive. | Anchor the walk to trusted directory handles and open/delete each descendant relative to a non-following parent handle, or provide an equivalently strong reviewed primitive that cannot traverse a swapped ancestor. | Run production-code negative tests for junctions, file/directory symbolic links, volume mounts, final swaps and a repeated concurrent ancestor-swap loop. Require zero outside deletions and accurate Partial/denial results in the approved isolated environment. |
| `P3R-04` | High | `FR-DAT-08/10/15`, `AC-DAT-08/11`, `TM-15` | `src/PCSteward.Storage/DownloadedArtifactCache.cs:43–82`; `tests/PCSteward.Application.IntegrationTests/DownloadedArtifactCacheTests.cs:10–70` | Summary and cleanup enumerate only SQLite ledger rows. An orphan payload or temporary extraction file left by a crash, lost registration or tampered ledger is never inspected or removed; with an empty ledger the service returns `Succeeded=true`, zero remaining items and a zero-byte summary while the app-owned file remains. This is the false cleanup Success prohibited by `FR-DAT-15`. Existing tests register every file and contain no orphan case. | Enumerate the fixed owned root safely, reconcile it with active in-memory/ledger leases, treat unknown/residual objects as non-success until safely deleted or accurately reported, and derive visible remaining size from verified filesystem state. | Add orphan file/directory, crash-before-registration, stale/missing ledger, active lease, locked orphan and unsafe reparse residual tests against production code. Assert cleanup never reports Success or zero remaining bytes while relevant owned data remains. |
| `P3R-05` | Medium | `FR-UPD-02`, `AC-UPD-01–02`, `TM-07/13`, ADR-017 | `src/PCSteward.Application/InventoryWorkflow.cs:79–101/151–165`; `src/PCSteward.Application/ReviewedUpdateExecutionWorkflow.cs:32–42` | Confirmation refreshes source metadata but rereads installed version/scope/architecture from the last-known-good SQLite snapshot; it does not perform current installed inventory. A local install change or tampered snapshot between review and confirmation can reproduce the same selection and pass `job.Matches`, contrary to ADR-017's required installed-version/current-mapping revalidation. | Obtain bounded current installed evidence for the exact target immediately before saving/running the approved job and compare it with the reviewed selection. Treat unavailable, changed or conflicting evidence as non-actionable. | Add review-to-confirmation tests for external version/scope/architecture changes, deleted installs and tampered persisted inventory; all must stop before provider construction. |
| `P3R-06` | Medium | `NFR-REL-03`, `NFR-OBS-03`, `AC-UPD-10` | `src/PCSteward.Application/UpdateBatchWorkflow.cs:155–176`; `src/PCSteward.Persistence.Sqlite/SqliteDatabase.cs:308–318` | A provider-completed item whose verification is cancelled is recorded as `Cancelled`, and a crash-recovered `Running` item is rewritten as `Queued`. Both labels can describe an update that already started or completed provider work as if it were unstarted, obscuring the need for verification and risking an unsafe retry decision. | Preserve a distinct interrupted/inconclusive verification-required state for started items. Reserve Queued/Cancelled for work proven not to have started and keep completed prior results append-only. | Add cancellation-after-provider and crash-during-provider/verification recovery tests; started items must never return to Queued or plain Cancelled, while the next unstarted item remains clearly queued/cancelled. |

## Missing evidence

- No production-path test or manual replay proves `AC-UPD-06`; the recorded exact Firefox query was outside `InventoryWorkflow.ReInventoryAsync`, and the desktop closed before presenting its result.
- No test proves the elevated provider executable is machine-trusted and non-user-writable.
- No production cleanup test exercises junction/symbolic-link/volume-mount handling or the mandatory repeated ancestor-component race.
- No cleanup test proves orphaned app-owned files prevent false Success.
- The approved host substitute does not prove clean-image behavior, per-user no-prompt behavior or provider-disconnect cancellation. Those accepted scope limits remain residual risks but do not waive `P3R-01`–`P3R-04`.

## Positive controls confirmed

- Approved jobs are typed, immutable in memory, digest-checked on restore and contain no executable path, URL, working directory, environment or free-form flags.
- Provider construction uses structured argument arrays; non-zero exit, timeout, output overflow, cancellation and UAC denial do not become Success.
- IPC messages are bounded, strict-schema, nonce/job-bound, expiring and one-shot; peer PID/user/session/path/signer checks and local current-user/SYSTEM pipe ACLs are present.
- Batch provider attempts are sequential, no force-close authority is exposed, diagnostics are bounded/redacted, and completed history events are append-only.
- Literal root selection, traversal/UNC/device/ADS rejection, active-ledger exclusion, locked residual reporting, retention order and quota order pass their current focused fixtures.

## Residual risks accepted by product owner

The recorded host-test and personal-use UI exceptions cover only their stated evidence scope. Clean-image behavior, per-user no-prompt behavior, provider-disconnect cancellation, unconstrained ancestor racing and the un-replayed outcome-dialog fix remain explicit risks. ADR-003 and ADR-011 remain Provisional. ADR-021 keeps `AC-UX-01–09` Optional — best effort.

## Recommended next action

Run Task 90 under GPT-5.6 Terra / High to remediate `P3R-01`–`P3R-06`, update traceability/evidence, and then repeat Task 25 in a fresh GPT-5.6 Sol / Extra High review. Do not merge Phase 3 or begin Task 26.
