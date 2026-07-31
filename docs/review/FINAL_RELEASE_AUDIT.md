# Final MVP release audit

**Verdict:** **NOT READY**

**Reviewed implementation:** `632b7afb6fb97aa8597ffe8a66ef6e3599396b3f`

**History reviewed:** `b0a37f78dd412f8b23ed2dc1586068f4e8b95781..632b7afb6fb97aa8597ffe8a66ef6e3599396b3f`

**Reviewer model/effort:** GPT-5.6 Sol / Extra High

**Date:** 2026-08-01

## Gate decision

PC Steward is not ready for release or external private-beta distribution. This fresh review found five High, one Medium, and one Low finding. Four mandatory acceptance criteria are Failed, five are Unverified, six mandatory non-functional requirements are Unverified, and multiple release-definition gates are Failed.

The decisive implementation failures are independent of the remaining field-evidence gaps:

- factory reset verifies only its named database/download/log targets, so an unclassified object directly below the owned state root survives while reset reports Success and enters onboarding, contrary to the accepted reset manifest;
- the packaged removal script treats every failed exact-task query as “absent” and never verifies absence after deletion, so it can report cleanup complete while `PCSteward.ScheduledCheck` remains; and
- the reset marker omits the accepted reset identity, time, attempt, category results, and per-category progress needed for bounded retry and auditable Partial recovery.

The local archive is mechanically coherent and the automated safety suites are green. Those facts do not replace mandatory final-app performance, destructive/reset, replacement/removal, scheduled-timeout, signing, or distribution-channel evidence. The Task 90 exceptions explicitly do not promote omitted evidence to Proven or authorize a release-ready claim.

## Audit scope and fresh verification

- Reviewed the complete product specification; Phase 0–4 requirements; traceability; ADR-001–021; architecture, data-flow, factory-reset, test-strategy, and threat-model documents; the approved native-design package; phase reviews; Task 90 evidence/exceptions/failures; release documents; retained package reports; source composition; and repository history.
- Inspected all 130 commits from the initial product-spec commit through the candidate, 607 tracked files, and the complete candidate change set: 577 files, 59,604 insertions, and 172 deletions.
- Release build: **0 warnings, 0 errors**. Full Release suite: **237/237 passed** — Domain 55, Application integration 145, Architecture 17, Security 20.
- Fresh focused safety matrix: **164/164 passed** — reset/cache/scheduling/update/inventory workflows 87, matching/version/update-job rules 40, Security 20, Architecture 17.
- Host-safe hostile boundary: **42 assertions passed**, with one approved local-PC/manual symbolic-link case skipped. WinGet production-boundary spike: **11 passed**.
- Fresh fixture performance: 30 local runs at 1.84 ms median / 2.00 ms p95 and 20 simulated-online runs at 15.51 ms median / 16.07 ms p95. The harness correctly states that it is not final-app, live-source, idle, process-tree, or acceptance evidence.
- Current NuGet advisory query: **0 known vulnerable direct or transitive packages** across all 17 projects. Release analyzers/build are clean. High-confidence and generic literal tracked-file secret scans found **0 candidate files**.
- Retained archive validator: **Pass** — 94 payload files / 49,929,973 bytes, 15 package components, 16 SBOM dependency nodes, 32 exact edges, portable hashes, licence inventory, and shipped notices. ZIP SHA-256: `20617AEFCFE42822534060CC7902BECFF5911D5B27C5F2274449A84935272FBF`.
- Signing status is **Not requested**. The archive remains unsigned, local-owner-only, and prohibited from upload, sharing, tagging, publishing, or external distribution.
- The worktree was clean before review; only this audit and `docs/STATUS.md` are changed by Task 31.

## Findings

| ID | Severity | Requirements | File/line | Finding | Exact remediation | Required verification |
|---|---|---|---|---|---|---|
| `FRA-11` | High | `FR-DAT-12/15`, `AC-DAT-12/13/15`, ADR-013/019, `TM-16` | `src/PCSteward.Storage/FactoryReset.cs:87–97`; `docs/architecture/FACTORY_RESET_DESIGN.md:64–74,164–172` | Reset deletes and verifies only `downloads`, `logs`, and the four SQLite leaves. It never enumerates the owned root for an unexpected child. An undeclared file/directory can therefore survive, `residuals` remains empty, the marker is replaced with onboarding, and reset reports Success. The accepted manifest explicitly requires an unclassified owned-root object to produce Partial and forbids onboarding while it remains. No regression exercises `UnexpectedOwnedObject`. | Enumerate the verified state-root handle without following reparse points. Permit only the compiled database leaves, `downloads`, `logs`, and the exact reset-control directory in the correct phase. Record every other child as `UnexpectedOwnedObject`; never widen deletion authority, remove the marker, create a fresh store, or enter onboarding while any such residual exists. | Add file, directory, unsafe/reparse, rename/race, and inaccessible unexpected-root cases. Preserve an outside sentinel, prove only compiled objects are deleted, assert Partial plus durable marker/progress, and prove retry reaches onboarding only after exact manifest absence. |
| `FRA-12` | High | `AC-DAT-18`, ADR-002/020, `TM-14/16`, release definition of done | `packaging/Remove-PCSteward.ps1:12–20`; `scripts/Test-StandaloneTaskCleanup.ps1:12–40`; `tests/PCSteward.ArchitectureTests/ReferenceBoundaryTests.cs:180–190` | The removal script treats every non-zero `schtasks /Query` result as task absence. It queries only before deletion and never proves exact absence afterward, yet always prints cleanup complete. Access denial, tool failure, malformed environment, or a retained task can therefore become false Success. Existing tests cover one positive task/removal/isolation path and static task-name authority only. | Replace exit-code inference with the language-independent exact Task Scheduler COM query used by product scheduling, or otherwise distinguish exact absence from every error. Fail closed on query/delete failure, query again after deletion, and print completion only after exact absence is proven. Keep unrelated-task authority unchanged. | Add absent, present/delete, access-denied/query-failure, delete-failure, task-still-present, malformed-tool, and unrelated-task cases. Run the packaged script on the supported host and capture exact before/after task identity and failure behavior. |
| `FRA-13` | Medium | `FR-DAT-12–15`, `AC-DAT-12/15`, ADR-013, `TM-16` | `src/PCSteward.Storage/DownloadedArtifactCache.cs:379–400,468–470`; `docs/architecture/FACTORY_RESET_DESIGN.md:54,128–132,146–154` | The durable reset marker contains only `schemaVersion` and `state`. It omits reset ID, requested UTC, attempt count, fixed category/result enums, residual progress, and per-category atomic progress replacement required by the accepted design. Retry is idempotent for compiled targets, but the durable record cannot represent the complete Partial result or distinguish attempts as specified. | Implement the fixed bounded marker schema, validate every field/version/state, atomically replace progress after each category, and retain sanitized fixed-category results on Partial. Do not add paths or deletion authority to the marker. Preserve fail-closed handling of malformed/unknown content and temporary-only crash states. | Add schema/version/enum/bounds tests, per-category failure/progress tests, crash-before/after-replacement cases, repeated-attempt accounting, and recovery assertions showing no normal state opens until the final verified completion. |
| `FRA-14` | High | `NFR-PERF-01–03/05–07`, `AC-NFR-01/02/06/07/11`, ADR-006 | `docs/review/TASK_90_OWNER_EXCEPTIONS.md:19–27`; `tests/PCSteward.Performance/Program.cs:10–32`; `docs/review/PERFORMANCE_AND_ACCESSIBILITY_EVIDENCE.md:18–25` | Mandatory final-app performance evidence is materially incomplete. The current exception records no 30-run final-app local series, no 20-run healthy live series, no update-responsiveness observation, no complete scan process-tree measurement, no process-attributed network result, early recurring I/O, and no real scheduled final-timeout exit. Fresh fixture timings cannot establish these gates. | Execute the complete ADR-006 protocol on the approved 3XS/`3XS-Overclock` baseline without changing thresholds: final packaged app, raw 30/20 samples, scan and update responsiveness, five-minute idle CPU/memory/disk/network, full process tree, and every scheduled-exit terminal path. Remediate any measured failure before re-audit. | Retain raw timestamped samples, environment/power-plan/tool versions, source health, p50/p95 calculations, process-tree attribution, cancellation/update interaction, and normal/completion/cancellation/final-timeout exit observations. Every threshold must pass; an exception remains Unverified. |
| `FRA-15` | High | `AC-DAT-12/13/15/18`, `NFR-PERF-07`, `AC-NFR-11`, ADR-002/009/013/020, release definition of done | `docs/review/TASK_90_OWNER_EXCEPTIONS.md:29–39`; `docs/release/PRIVATE_BETA_CHECKLIST.md:15–24` | Mandatory destructive and supported-host lifecycle observations remain omitted: complete two-confirmation reset proving zero prior state/onboarding, reset with task present/absent and residual marker outcomes, retain-data package replacement, reset-then-package removal, and real scheduled final-timeout exit. The exception expires before any release-ready claim. The new `FRA-11/12` false-success paths also invalidate the assumption that green positive lifecycle evidence is sufficient. | Fix `FRA-11–13`, rebuild and re-hash the package, then execute the complete supported-host lifecycle from a caller-writable extraction: launch, schedule/action/scan-only behavior, replacement with retained state, exact cleanup and folder deletion, reset choices, Partial/retry, and final-timeout behavior. | Record host/operator/UTC, exact artifact hash, actions, task XML/identity, process exits, before/after state and binary hashes, onboarding/zero-prior-state assertions, unrelated-task/outside-sentinel preservation, no helper/UAC, rollback, and zero residual PC Steward process/task/data as applicable. |
| `FRA-16` | High | Release definition of done, ADR-001/002/008/009, `TM-18` | `docs/review/TASK_90_OWNER_EXCEPTIONS.md:7–17`; `docs/release/PRIVATE_BETA_CHECKLIST.md:13,26–32`; `docs/release/RELEASE_EVIDENCE.md:16–19` | No approved production signer, trusted distribution channel, external recipient list, support contact, or distributable rollback artifact exists. The local-only exception explicitly expires before a release/publication claim. The mechanically valid unsigned archive is engineering evidence, not a release candidate. | Obtain product-owner-approved release identity/channel/support/rollback controls, rebuild after all code fixes, sign the exact payload and required distribution artifacts, bind the checksum to the final archive, and prohibit post-sign mutation. Do not reuse the removed development certificate. | On a clean supported host independently verify chain trust, code-signing EKU, signer identity, timestamp/validity, every payload hash, archive/checksum/channel binding, recipient access, rollback, and support path. Re-run package validation and the full lifecycle on the exact signed hash. |
| `FRA-17` | Low | `FR-UI-05`, release definition of done | `docs/release/KNOWN_LIMITATIONS.md:6–7`; `docs/review/TASK_90_OWNER_EXCEPTIONS.md:19–27`; `docs/review/FINAL_RELEASE_AUDIT.md` | Release-facing limitations still list controlled current-user update evidence as missing after Task 90 proved a VS Code 1.129.0 → 1.130.0 production-path update, and they name the superseded `PO-EXC-29-PERF-01` rather than the current Task 90 boundary. The wording is conservative but stale. | Reconcile in-app and release-note limitations to the final evidence: retain signing/channel, performance, lifecycle, provider, and package-scope blockers; describe the proven current-user update narrowly; cite the current exception or its replacement. | Compare packaged About/support text, known limitations, release notes, checklist, audit, and final hash. Require consistent scope/status language and no unsupported release, privilege, signing, performance, or lifecycle claim. |

**Finding count:** 7 open — 0 Blocker, 5 High, 1 Medium, 1 Low.

## Mandatory MVP acceptance criteria

Only `Proven`, `Failed`, and `Unverified` are used. An approved exception is not converted to Proven.

| Criterion | Status | Independent basis |
|---|---|---|
| `AC-INV-01` | Proven | Registry/MSIX/WinGet discovery and composed inventory coverage pass. |
| `AC-INV-02` | Proven | Conservative normalization preserves distinct scope/architecture instances. |
| `AC-INV-03` | Proven | Static and architecture checks find no `Win32_Product` inventory path. |
| `AC-INV-04` | Proven | Malformed/incomplete registry records warn or preserve unknowns without scan failure. |
| `AC-INV-05` | Proven | Disabled/local-only process-attributed evidence emits no connector request. |
| `AC-INV-06` | Proven | Inventory is standard-user and requests no elevation. |
| `AC-INV-07` | Proven | UI exposes provider and local/live/cached freshness. |
| `AC-MAT-01` | Proven | Actionable rows expose source, stable ID, versions, confidence, and explanation. |
| `AC-MAT-02` | Proven | Low-confidence, unmatched, stale, conflicting, and unavailable evidence remains non-actionable. |
| `AC-MAT-03` | Proven | Fresh zero-false-positive critical matrix passed. |
| `AC-MAT-04` | Proven | Architecture/channel/scope and multi-instance conflicts fail closed. |
| `AC-MAT-05` | Proven | Unsafe or incomparable versions remain Unknown/Review. |
| `AC-MAT-06` | Proven | Source failure retains visibly stale timestamped last-known-good results. |
| `AC-MAT-07` | Proven | Trusted exact-ID mappings are persisted, reversible, and tested. |
| `AC-UPD-01` | Proven | Immutable explicit review/confirmation is the only runner entry. |
| `AC-UPD-02` | Proven | Typed execution and negatives reject arbitrary command/path/argument authority. |
| `AC-UPD-03` | Proven | Task 90 recorded a current-user VS Code update with no observed UAC/helper. |
| `AC-UPD-04` | Proven | Standalone machine-scope updates are non-actionable and no helper is composed or packaged. |
| `AC-UPD-05` | Proven | Denial/cancellation remain distinct from failure and Success. |
| `AC-UPD-06` | Proven | VS Code 1.129.0 → 1.130.0 was independently corroborated in HKCU after execution. |
| `AC-UPD-07` | Proven | Non-zero, timeout, overflow, cancellation, and launch failures cannot become Success. |
| `AC-UPD-08` | Proven | Success requires exact bounded post-update version evidence. |
| `AC-UPD-09` | Proven | Batch execution is sequential with independent history. |
| `AC-UPD-10` | Proven | Cancellation preserves completed/running evidence and stops unstarted work. |
| `AC-UPD-11` | Proven | Typed, timestamped, sanitized local history is persisted and tested. |
| `AC-DAT-01` | Proven | SQLite persistence and actual close/relaunch retention are evidenced. |
| `AC-DAT-02` | Proven | CSV/JSON snapshot fidelity and timestamp coverage pass. |
| `AC-DAT-03` | Proven | Inventory/history and cached-state paths remain usable offline. |
| `AC-DAT-04` | Proven | Process-attributed enabled/disabled trace recorded zero lost events. |
| `AC-DAT-05` | Proven | No account/cloud identity capability or dependency exists. |
| `AC-DAT-06` | Proven | Transactional Clear scan data returns to no-scan state. |
| `AC-DAT-07` | Proven | Settings/schedule/history/downloads/decisions are preserved and reapplied as required. |
| `AC-DAT-08` | Proven | Inactive cache cleanup, accounting, and category isolation pass. |
| `AC-DAT-09` | Proven | UI exposes recoverable owned bytes and cleanup action. |
| `AC-DAT-10` | Proven | Fixed app-owned roots and provider-cache exclusion pass static/hostile fixtures. |
| `AC-DAT-11` | Proven | Locked owned artifacts produce Partial with retained residual evidence. |
| `AC-DAT-12` | Failed | An unexpected owned-root child survives while reset reports Success; see `FRA-11`. |
| `AC-DAT-13` | Failed | Onboarding may appear while unclassified previous owned state remains; see `FRA-11`. |
| `AC-DAT-14` | Proven | Per-user clear/reset requests no elevation. |
| `AC-DAT-15` | Failed | Reset can claim complete while unclassified state or an exact scheduled task remains; see `FRA-11/12`. |
| `AC-DAT-16` | Proven | Safe-delete hostile fixtures preserve outside sentinels and reject reparse/path escape. |
| `AC-DAT-17` | Proven | Bounded structured diagnostics and sanitizer fixtures pass. |
| `AC-DAT-18` | Failed | Packaged cleanup has an untested false-success path; see `FRA-12`. |
| `AC-NFR-01` | Unverified | No 30-run final-app local inventory series on the approved baseline. |
| `AC-NFR-02` | Unverified | No 20-run healthy live final-app series. |
| `AC-NFR-03` | Proven | Keyboard cancellation returned the app to operable cached state in 236 ms. |
| `AC-NFR-04` | Proven | Active-scan termination retained the previous successful snapshot. |
| `AC-NFR-05` | Proven | Connector timeout/fault isolation and partial publication pass. |
| `AC-NFR-06` | Unverified | CPU/memory are within the latest sample, but recurring I/O and network are not passed. |
| `AC-NFR-07` | Unverified | The complete approved-baseline scan process-tree protocol is absent. |
| `AC-NFR-08` | Proven | Normal close leaves no PC Steward process after 30 seconds. |
| `AC-NFR-09` | Proven | One-scan exclusivity and measured connector concurrency pass. |
| `AC-NFR-10` | Proven | Quota/oldest-inactive/active-artifact protection passes. |
| `AC-NFR-11` | Unverified | Completion/cancellation are observed; real final-timeout exit is omitted. |

**Mandatory acceptance count:** 54 — 45 Proven, 4 Failed, 5 Unverified.

`AC-UX-01–09` remain Optional — best effort under ADR-021. Current evidence is useful, but no accessibility-conformance claim is made.

## Mandatory non-functional requirements

| Requirement | Status | Independent basis |
|---|---|---|
| `NFR-PERF-01` | Unverified | No 30-run final-app local inventory on the approved baseline. |
| `NFR-PERF-02` | Unverified | No 20-run healthy live-source final-app check. |
| `NFR-PERF-03` | Unverified | Scan responsiveness is proven; final-candidate update responsiveness was not measured. |
| `NFR-PERF-04` | Proven | Task 90 observed the interactive shell in 548.891 ms on the approved baseline. |
| `NFR-PERF-05` | Unverified | Latest CPU/memory pass, but recurring I/O and process-attributed network do not. |
| `NFR-PERF-06` | Unverified | Complete final-candidate scan process-tree limits were not measured. |
| `NFR-PERF-07` | Unverified | Normal close and scheduled completion/cancellation pass; final-timeout exit is absent. |
| `NFR-PERF-08` | Proven | Cancellation acknowledgment/recovery and child containment pass. |
| `NFR-PERF-09` | Proven | One-scan lease and bounded connector concurrency pass. |
| `NFR-PERF-10` | Proven | Configurable 2 GB quota and active-artifact exclusion pass. |
| `NFR-REL-01` | Proven | Connector faults retain other successful results. |
| `NFR-REL-02` | Proven | Transactional promotion and crash recovery retain last-known-good state. |
| `NFR-REL-03` | Proven | Interrupted/cancelled batches preserve completed and verification-required states. |
| `NFR-REL-04` | Proven | Cached results retain timestamp and non-live status. |
| `NFR-REL-05` | Proven | Malformed/missing registry fixtures are contained. |
| `NFR-MNT-01` | Proven | Capability-specific provider/connector interfaces isolate failures. |
| `NFR-MNT-02` | Proven | Matching/version rules have UI-free unit coverage. |
| `NFR-MNT-03` | Proven | Provider command/parser construction is outside presentation. |
| `NFR-MNT-04` | Proven | Versioned SQLite migration ownership/recovery evidence exists. |
| `NFR-MNT-05` | Proven | Security/deployment decisions are recorded in ADR-001–021. |
| `NFR-OBS-01` | Proven | Scan/update correlation IDs are typed and tested. |
| `NFR-OBS-02` | Proven | Structured bounded severity events are tested. |
| `NFR-OBS-03` | Proven | Cancellation, denial, timeout, verification, provider, and product failures remain distinct. |
| `NFR-OBS-04` | Proven | Bounded sink/log retention controls and tests are present. |
| `NFR-OBS-05` | Proven | Sanitized diagnostic export and hostile-string fixtures pass. |

**Mandatory NFR count:** 25 — 19 Proven, 0 Failed, 6 Unverified.

## Release definition of done

| Release item | Status | Judgment |
|---|---|---|
| All mandatory acceptance criteria pass | Failed | Four Failed and five Unverified criteria remain. |
| Zero-false-positive fixture passes | Proven | Fresh critical matrix passed with no executable false positive. |
| Threat model reviewed against implemented architecture | Failed | Reset manifest verification and packaged task cleanup contradict `TM-16`/accepted architecture. |
| Release binaries signed before external distribution | Failed | No approved signer or trusted channel exists; see `FRA-16`. |
| Critical execution/privilege code has automated tests and manual review | Proven | Security/Architecture suites and current composition review pass for user-scope-only execution. |
| Dependency, static-analysis, and secret-scanning checks pass | Proven | Clean build/analyzers, zero known NuGet advisories, and zero tracked secret candidates. |
| Release SBOM generated | Proven | Payload validator reconciles 15 components, 16 nodes, 32 edges, licences, notices, and hashes. |
| Standalone launch/replacement/task cleanup/package removal/factory reset tested | Failed | Cleanup can report false Success; mandatory reset/replacement/removal cases remain omitted. |
| Scan and idle acceptance pass on agreed baseline | Failed | Required 30/20, scan-tree, recurring-I/O, and network gates remain Unverified. |
| Destructive path-safety, locked-file, and partial-reset tests pass | Failed | Safe-delete primitives pass, but unexpected owned-root state bypasses Partial; see `FRA-11`. |
| Known limitations documented in app and release notes | Failed | Release-facing limitations are stale; see `FRA-17`. |
| Product owner completed real-PC local-only scan before update execution | Proven | Retained Task 90 host evidence records local-only scans before the controlled update. |

## Residual risks requiring explicit product-owner acceptance

These risks do not override open findings or mandatory Unverified criteria:

- VM execution remains excluded by product-owner direction; supported-host evidence must identify the physical environment and exact final archive.
- ADR-021 keeps `AC-UX-01–09` Optional — best effort; incomplete Narrator/high-contrast/scaling evidence cannot support an accessibility-conformance claim.
- Provider-disconnect cancellation, same-user substitution of the name-based temporary WinGet export, imperfect package-ID/process-name correlation, and the historical outcome-dialog crash remain bounded documented risks.
- The protected installed-helper/UAC evidence is historical/future-installed-mode evidence only. The standalone payload must continue to contain and compose no elevated helper or machine-scope execution.
- A caller-writable extraction cannot provide installed-binary tamper resistance; signed channel/checksum controls and explicit guidance are still required before external distribution.
- ADR-003 and ADR-011 remain Provisional and must be revisited if provider authority or cache policy changes.

## Required sequence

Stop this run before Task 90 because Task 90 requires GPT-5.6 Terra / High. Under that setting, remediate `FRA-11–13` first, using independent agents only for isolated non-overlapping changes as directed by the product owner; integrate and independently re-test in the main thread. Then resolve or obtain fresh evidence for `FRA-14–17`, rebuild/re-hash the candidate, run every targeted and full gate, and commit the remediation only if green. Repeat Task 31 in a fresh GPT-5.6 Sol / Extra High review. Do not release, tag, publish, upload, share, or call the MVP ready before a fresh audit returns READY.
